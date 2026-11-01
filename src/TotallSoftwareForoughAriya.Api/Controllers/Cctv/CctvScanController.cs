using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// اسکن دوربین‌های مداربسته و دستگاه‌های NVR/DVR داخل شبکه — چندلایه برای دقت بالا:
/// ۱) ONVIF WS-Discovery (مولتی‌کست استاندارد 239.255.255.250:3702)
/// ۲) اسکن پورت‌های مخصوص CCTV (RTSP/Hikvision/Dahua/ONVIF)
/// ۳) پروب RTSP (OPTIONS) و خواندن Server
/// ۴) پروب HTTP و یافتن برند/عنوان
/// ۵) برند از پیشوند MAC (OUI)
/// فقط دستگاه‌هایی که حداقل یک سیگنال مخصوص CCTV دارند گزارش می‌شوند.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CctvScanController : ControllerBase
{
    public class CctvDevice
    {
        public string Ip { get; set; } = "";
        public string Kind { get; set; } = "unknown";     // camera | nvr | unknown
        public string Vendor { get; set; } = "";
        public List<int> OpenPorts { get; set; } = new();
        public string RtspServer { get; set; } = "";
        public string HttpTitle { get; set; } = "";
        public bool Onvif { get; set; }
        public string OnvifInfo { get; set; } = "";
        public string Mac { get; set; } = "";
        public string MacVendor { get; set; } = "";
        public string Confidence { get; set; } = "low";   // high | medium | low
    }

    public class CctvScanResult
    {
        public string LocalIp { get; set; } = "";
        public string Subnet { get; set; } = "";
        public int ScannedCount { get; set; }
        public int ElapsedMs { get; set; }
        public List<CctvDevice> Devices { get; set; } = new();
    }

    // پورت‌های مهم CCTV: RTSP، Hikvision SDK، Dahua، ONVIF/media و وب
    private static readonly int[] CctvPorts = { 554, 8000, 37777, 37778, 8899, 80, 8080, 8081 };
    private static readonly int[] StrongPorts = { 554, 8000, 37777, 37778, 8899 };

    // برندهای شناخته‌شده از پیشوند MAC (سه اکتت اول به حروف بزرگ)
    private static readonly Dictionary<string, string> OuiVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C056E3"] = "Hikvision", ["4447CC"] = "Hikvision", ["BCAD28"] = "Hikvision",
        ["3CEF8C"] = "Dahua", ["A0BD1D"] = "Dahua",
        ["00408C"] = "Axis", ["ACCC8E"] = "Axis", ["B8A44F"] = "Axis"
    };

    [HttpGet]
    public async Task<ActionResult<CctvScanResult>> Scan()
    {
        var sw = Stopwatch.StartNew();
        var result = new CctvScanResult();

        // ---------- شبکه‌ی محلی ----------
        IPAddress? localIp = null; IPAddress? mask = null;
        IPAddress? llIp = null; IPAddress? llMask = null; // جایگزین: link-local (169.254)
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = ua.Address.GetAddressBytes();
                var isPrivate = b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
                var isLinkLocal = b[0] == 169 && b[1] == 254;
                if (isPrivate)
                {
                    localIp = ua.Address; mask = ua.IPv4Mask;
                    break;
                }
                if (isLinkLocal && llIp is null) { llIp = ua.Address; llMask = ua.IPv4Mask; }
            }
            if (localIp != null) break;
        }
        // اگر رنج خصوصی نبود، از link-local استفاده کن (بهتر از هیچ)
        if (localIp == null && llIp != null) { localIp = llIp; mask = llMask; }
        if (localIp == null || mask == null)
            return Ok(new CctvScanResult { ElapsedMs = 0 });

        result.LocalIp = localIp.ToString();
        var network = GetNetworkAddress(localIp, mask);
        result.Subnet = $"{network}/{GetPrefixLength(mask)}";

        var targets = BuildTargets(localIp, mask, 254);
        result.ScannedCount = targets.Count;

        // ---------- لایه ۱: ONVIF WS-Discovery (مولتی‌کست) ----------
        var onvif = await OnvifDiscoverAsync(TimeSpan.FromSeconds(3));

        // ---------- لایه ۲: اسکن پورت موازی ----------
        var openByIp = new System.Collections.Concurrent.ConcurrentDictionary<string, List<int>>();
        using var sem = new SemaphoreSlim(64);
        var tasks = targets.Select(async ip =>
        {
            await sem.WaitAsync();
            try
            {
                var opens = new List<int>();
                foreach (var port in CctvPorts)
                {
                    if (await TcpOpenAsync(ip, port, 350)) opens.Add(port);
                }
                if (opens.Count > 0) openByIp[ip] = opens;
            }
            catch { }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);

        // ---------- لایه ۳و۴: پروب عمیق روی کاندیداها + لایه ۵: MAC ----------
        var arpTable = ReadArpTable();
        var devices = new System.Collections.Concurrent.ConcurrentBag<CctvDevice>();

        var candidates = openByIp.Keys.Union(onvif.Keys).ToList();
        var probeTasks = candidates.Select(async ip =>
        {
            var d = new CctvDevice { Ip = ip };
            openByIp.TryGetValue(ip, out var opens);
            d.OpenPorts = opens ?? new List<int>();

            // ONVIF
            if (onvif.TryGetValue(ip, out var info))
            {
                d.Onvif = true;
                d.OnvifInfo = info.Types;
                d.Vendor = info.VendorHint;
            }

            // RTSP
            if (d.OpenPorts.Contains(554))
            {
                var (ok, server) = await RtspProbeAsync(ip);
                if (ok) d.RtspServer = server;
            }

            // HTTP
            var httpPort = new[] { 80, 8080, 8000, 8081 }.FirstOrDefault(p => d.OpenPorts.Contains(p));
            if (httpPort > 0)
            {
                var title = await HttpTitleAsync(ip, httpPort);
                d.HttpTitle = title.Title;
                if (string.IsNullOrEmpty(d.Vendor) && !string.IsNullOrEmpty(title.Vendor)) d.Vendor = title.Vendor;
            }

            // MAC
            arpTable.TryGetValue(ip, out var mac);
            d.Mac = mac ?? "";
            if (!string.IsNullOrEmpty(mac))
            {
                var prefix = mac.Replace(":", "").Substring(0, 6);
                if (OuiVendors.TryGetValue(prefix, out var mv))
                {
                    d.MacVendor = mv;
                    if (string.IsNullOrEmpty(d.Vendor)) d.Vendor = mv;
                }
            }

            // دسته‌بندی
            var blob = $"{d.OnvifInfo} {d.HttpTitle} {d.RtspServer} {d.Vendor}".ToLowerInvariant();
            var isNvr = blob.Contains("networkvideorecorder") || blob.Contains("nvr") || blob.Contains("dvr") || blob.Contains("xvr");
            var isCam = blob.Contains("networkvideotransmitter") || blob.Contains("ip camera") || blob.Contains("ipcamera") || blob.Contains("ipc-") || d.RtspServer.Length > 0;
            d.Kind = isNvr ? "nvr" : isCam ? "camera" : "unknown";

            // فقط دستگاه‌هایی که سیگنال مخصوص CCTV دارند
            var hasStrongPort = d.OpenPorts.Any(p => StrongPorts.Contains(p));
            var hasVendor = !string.IsNullOrEmpty(d.Vendor) || d.MacVendor.Length > 0;
            var signals = new[] { d.Onvif, d.RtspServer.Length > 0, hasStrongPort, hasVendor }.Count(x => x);
            if (signals == 0) return; // دستگاه معمولی شبکه — گزارش نکن

            d.Confidence = signals >= 2 || d.Onvif ? "high" : signals == 1 ? "medium" : "low";
            devices.Add(d);
        });
        await Task.WhenAll(probeTasks);

        result.Devices = devices
            .OrderBy(d => SortKey(d.Ip))
            .ToList();
        result.ElapsedMs = (int)sw.ElapsedMilliseconds;
        return Ok(result);
    }

    // ================= ONVIF WS-Discovery =================

    private static async Task<Dictionary<string, (string Types, string VendorHint)>> OnvifDiscoverAsync(TimeSpan window)
    {
        var found = new Dictionary<string, (string, string)>();
        try
        {
            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"));
            udp.Client.ReceiveTimeout = 500;

            var probe = @"<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"" xmlns:a=""http://schemas.xmlsoap.org/ws/2004/08/addressing""><s:Header><a:Action s:mustUnderstand=""1"">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action><a:MessageID>urn:uuid:" + Guid.NewGuid() + @"</a:MessageID><a:To s:mustUnderstand=""1"">urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To></s:Header><s:Body><Probe xmlns=""http://schemas.xmlsoap.org/ws/2005/04/discovery""/></s:Body></s:Envelope>";
            var bytes = Encoding.UTF8.GetBytes(probe);
            await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702));
            // تکرار برای اطمینان
            await Task.Delay(150);
            await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702));

            var deadline = DateTime.UtcNow + window;
            while (DateTime.UtcNow < deadline)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                try
                {
                    // نکته: ReceiveAsync به ReceiveTimeout توجه نمی‌کند — باید CancellationToken بدهیم
                    using var rcts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
                    var res = await udp.ReceiveAsync(rcts.Token);
                    var text = Encoding.UTF8.GetString(res.Buffer);
                    var xaddr = Regex.Match(text, "<[\\w:]*XAddrs[^>]*>([^<]+)<").Groups[1].Value;
                    var types = Regex.Match(text, "<[\\w:]*Types[^>]*>([^<]*)<").Groups[1].Value;
                    var scopes = Regex.Match(text, "<[\\w:]*Scopes[^>]*>([^<]*)<").Groups[1].Value;
                    var ipMatch = Regex.Match(xaddr, @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})");
                    if (ipMatch.Success)
                    {
                        var ip = ipMatch.Groups[1].Value;
                        var vendor = DetectVendor(types + " " + scopes) ?? "";
                        found[ip] = (types.Trim(), vendor);
                    }
                }
                catch (OperationCanceledException) { /* تایم‌اوت — ادامه */ }
                catch (SocketException) { /* تایم‌اوت — ادامه */ }
                catch (ObjectDisposedException) { break; }
            }
        }
        catch { /* مولتی‌کست در برخی محیط‌ها مجاز نیست */ }
        return found;
    }

    // ================= پروب RTSP =================

    private static async Task<(bool Ok, string Server)> RtspProbeAsync(string ip)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(800);
            await tcp.ConnectAsync(ip, 554, cts.Token);
            var stream = tcp.GetStream();
            var req = $"OPTIONS rtsp://{ip}:554/ RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: CCTV-Scan\r\n\r\n";
            var data = Encoding.ASCII.GetBytes(req);
            await stream.WriteAsync(data);
            var buf = new byte[1024];
            using var cts2 = new CancellationTokenSource(1200);
            var n = await stream.ReadAsync(buf, cts2.Token);
            var resp = Encoding.ASCII.GetString(buf, 0, n);
            var isRtsp = resp.Contains("RTSP/1.0");
            var server = Regex.Match(resp, @"Server:\s*(.+?)\r?\n", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            return (isRtsp, server);
        }
        catch { return (false, ""); }
    }

    // ================= پروب HTTP =================

    private static async Task<(string Title, string Vendor)> HttpTitleAsync(string ip, int port)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(900);
            await tcp.ConnectAsync(ip, port, cts.Token);
            var stream = tcp.GetStream();
            var req = $"GET / HTTP/1.0\r\nHost: {ip}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(req));
            var buf = new byte[4096];
            using var cts2 = new CancellationTokenSource(1500);
            var n = await stream.ReadAsync(buf, cts2.Token);
            var body = Encoding.ASCII.GetString(buf, 0, Math.Min(n, buf.Length));
            var title = Regex.Match(body, @"<title[^>]*>\s*([^<]{1,120})", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            var serverHeader = Regex.Match(body, @"Server:\s*(.+?)\r?\n", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            var vendor = DetectVendor(body + " " + serverHeader) ?? "";
            return (title, vendor);
        }
        catch { return ("", ""); }
    }

    private static string? DetectVendor(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("hikvision") || t.Contains("hik")) return "Hikvision";
        if (t.Contains("dahua") || t.Contains("amcrest")) return "Dahua";
        if (t.Contains("axis")) return "Axis";
        if (t.Contains("bosch")) return "Bosch";
        if (t.Contains("samsung") || t.Contains("hanwha") || t.Contains("wisenet")) return "Hanwha/Wisenet";
        if (t.Contains("uniview")) return "Uniview";
        if (t.Contains("tp-link") || t.Contains("tapo")) return "TP-Link/Tapo";
        return null;
    }

    // ================= شبکه =================

    private static async Task<bool> TcpOpenAsync(string ip, int port, int timeoutMs)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await tcp.ConnectAsync(ip, port, cts.Token);
            return tcp.Connected;
        }
        catch { return false; }
    }

    private static IPAddress GetNetworkAddress(IPAddress ip, IPAddress mask)
    {
        var a = ip.GetAddressBytes();
        var m = mask.GetAddressBytes();
        for (var i = 0; i < 4; i++) a[i] &= m[i];
        return new IPAddress(a);
    }

    private static int GetPrefixLength(IPAddress mask)
    {
        var bits = 0;
        foreach (var b in mask.GetAddressBytes()) bits += System.Numerics.BitOperations.PopCount((uint)b);
        return bits;
    }

    private static long SortKey(string ip)
    {
        var p = ip.Split('.');
        return p.Length == 4 ? (long.Parse(p[0]) << 24) + (long.Parse(p[1]) << 16) + (long.Parse(p[2]) << 8) + long.Parse(p[3]) : 0;
    }

    private static List<string> BuildTargets(IPAddress localIp, IPAddress mask, int maxCount)
    {
        var prefix = GetPrefixLength(mask);
        var hostBits = 32 - prefix;
        var total = hostBits >= 30 ? int.MaxValue : (1 << hostBits);

        if (total > maxCount)
        {
            var list = new List<string>();
            var baseIp = localIp.GetAddressBytes();
            for (var i = 1; i <= 254; i++)
            {
                var b = (byte[])baseIp.Clone();
                b[3] = (byte)i;
                list.Add(new IPAddress(b).ToString());
            }
            return list;
        }

        var network = GetNetworkAddress(localIp, mask).GetAddressBytes();
        var netLong = (long)network[0] << 24 | (long)network[1] << 16 | (long)network[2] << 8 | network[3];
        var result = new List<string>();
        for (var i = 1; i < total - 1 && result.Count < maxCount; i++)
        {
            // Explicit network byte order: independent of machine endianness and
            // of C# overload resolution between Enumerable.Reverse and Span.Reverse.
            var address = (uint)(netLong + i);
            var bytes = new byte[]
            {
                (byte)((address >> 24) & 0xff),
                (byte)((address >> 16) & 0xff),
                (byte)((address >> 8) & 0xff),
                (byte)(address & 0xff)
            };
            result.Add(new IPAddress(bytes).ToString());
        }
        result.Add(localIp.ToString());
        return result.Distinct().ToList();
    }

    private static Dictionary<string, string> ReadArpTable()
    {
        var map = new Dictionary<string, string>();
        void Parse(string output)
        {
            foreach (Match m in Regex.Matches(output,
                @"(\d+\.\d+\.\d+\.\d+)\s+.*?([0-9a-fA-F]{2}[:-][0-9a-fA-F]{2}[:-][0-9a-fA-F]{2}[:-][0-9a-fA-F]{2}[:-][0-9a-fA-F]{2}[:-][0-9a-fA-F]{2})"))
                map[m.Groups[1].Value] = m.Groups[2].Value.Replace('-', ':').ToUpperInvariant();
        }
        try
        {
            var psi = new ProcessStartInfo("arp", "-a") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p != null) Parse(p.StandardOutput.ReadToEnd());
        }
        catch { }
        if (map.Count == 0)
        {
            try
            {
                var psi = new ProcessStartInfo("ip", "neigh") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    var output = p.StandardOutput.ReadToEnd();
                    foreach (Match m in Regex.Matches(output, @"(\d+\.\d+\.\d+\.\d+).*?lladdr\s+([0-9a-fA-F:]{17})"))
                        map[m.Groups[1].Value] = m.Groups[2].Value.ToUpperInvariant();
                }
            }
            catch { }
        }
        return map;
    }
}
