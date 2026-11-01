using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;

namespace Inventory.Api.Controllers;

/// <summary>
/// اسکن سیستم‌های شبکه — نسخه‌ی جدید مبتنی بر Job:
///   POST /api/NetworkScan/start   → شروع اسکن در پس‌زمینه و گرفتن jobId
///   GET  /api/NetworkScan/status/{jobId} → پیشرفت + نتایج زنده
/// رنج قابل انتخاب: auto (شبکه‌ی خودِ سرور) یا 192.168.1 یا 192.168.1.0/24 یا 192.168.1.1-192.168.1.254
/// تشخیص: پینگ + پشتیبان TCP (پورت‌های 445/139/135/80/22) + نام DNS + MAC از ARP
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class NetworkScanController : ControllerBase
{
    private readonly AppDbContext _db;
    public NetworkScanController(AppDbContext db) => _db = db;

    public class ScanHost
    {
        public string Ip { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string Mac { get; set; } = "";
        public int? SystemInfoId { get; set; }
        public string Method { get; set; } = "ping";   // ping | tcp
    }

    public class ScanStatus
    {
        public bool Running { get; set; }
        public int Progress { get; set; }        // 0..100
        public int Scanned { get; set; }
        public int TargetCount { get; set; }
        public int FoundCount { get; set; }
        public string Range { get; set; } = "";
        public string LocalIp { get; set; } = "";
        public string Adapter { get; set; } = "";
        public int ElapsedMs { get; set; }
        public string? Error { get; set; }
        public List<ScanHost> Hosts { get; set; } = new();
    }

    public class StartRequest
    {
        public string? Range { get; set; }
    }

    private class ScanJob
    {
        public ScanStatus Status { get; } = new();
    }

    private static readonly ConcurrentDictionary<string, ScanJob> Jobs = new();
    private static readonly int[] TcpProbePorts = { 445, 139, 135, 80, 22 };
    private const int MaxTargets = 1024;

    // ================= شروع اسکن =================

    [HttpPost("start")]
    public IActionResult Start([FromBody] StartRequest? req)
    {
        var rangeText = (req?.Range ?? "auto").Trim();

        var (targets, localIp, adapter, error) = ParseRange(rangeText);
        if (error != null)
            return BadRequest(new { message = error });

        var job = new ScanJob();
        job.Status.TargetCount = targets.Count;
        job.Status.Range = rangeText;
        job.Status.LocalIp = localIp;
        job.Status.Adapter = adapter;
        job.Status.Running = true;

        var jobId = Guid.NewGuid().ToString("N")[..8];
        Jobs[jobId] = job;

        // پاک‌سازی job های قدیمیِ تمام‌شده (بیش از ۱۰ تا نگه نمی‌داریم)
        foreach (var old in Jobs.Where(j => !j.Value.Status.Running).Skip(10).ToList())
            Jobs.TryRemove(old.Key, out _);

        var capturedTargets = targets;
        var capturedDb = _db;
        _ = Task.Run(async () => await RunScanAsync(job, capturedTargets, capturedDb));

        return Ok(new { jobId, targetCount = targets.Count, range = rangeText, localIp });
    }

    // ================= وضعیت اسکن =================

    [HttpGet("status/{jobId}")]
    public IActionResult Status(string jobId)
    {
        if (!Jobs.TryGetValue(jobId, out var job))
            return NotFound(new { message = "شناسه‌ی اسکن یافت نشد یا منقضی شده است." });
        return Ok(job.Status);
    }

    // ================= موتور اسکن =================

    private static async Task RunScanAsync(ScanJob job, List<string> targets, AppDbContext db)
    {
        var status = job.Status;
        var sw = Stopwatch.StartNew();
        try
        {
            var found = new ConcurrentDictionary<string, string>(); // ip → روش

            // ---------- فاز ۱: پینگ موازی (سریع) ----------
            using var sem = new SemaphoreSlim(100);
            var scannedCount = 0;

            var pingTasks = targets.Select(async ip =>
            {
                await sem.WaitAsync();
                try
                {
                    using var pinger = new Ping();
                    var reply = await pinger.SendPingAsync(ip, 400);
                    if (reply.Status == IPStatus.Success) found.TryAdd(ip, "ping");
                }
                catch { }
                finally
                {
                    var n = Interlocked.Increment(ref scannedCount);
                    status.Scanned = n;
                    status.Progress = (int)(n * 60.0 / Math.Max(1, targets.Count));
                    sem.Release();
                }
            });
            await Task.WhenAll(pingTasks);

            // ---------- فاز ۲: TCP برای بقیه (سیستم‌هایی که پینگ بسته دارند) ----------
            status.Progress = 60;
            var remaining = targets.Where(t => !found.ContainsKey(t)).ToList();
            var tcpDone = 0;
            var totalProbes = remaining.Count * TcpProbePorts.Length;

            var probes = remaining.SelectMany(ip => TcpProbePorts.Select(port => (ip, port)));
            var tcpTasks = probes.Select(async t =>
            {
                await sem.WaitAsync();
                try
                {
                    using var tcp = new TcpClient();
                    using var cts = new CancellationTokenSource(220);
                    await tcp.ConnectAsync(t.ip, t.port, cts.Token);
                    if (tcp.Connected) found.TryAdd(t.ip, "tcp");
                }
                catch { }
                finally
                {
                    var n = Interlocked.Increment(ref tcpDone);
                    status.Progress = 60 + (int)(n * 35.0 / Math.Max(1, totalProbes));
                    sem.Release();
                }
            });
            await Task.WhenAll(tcpTasks);
            status.Scanned = targets.Count;
            status.Progress = 95;

            // ---------- فاز ۳: MAC (ARP بعد از اسکن — کامل‌تر) + نام + تطبیق شناسنامه ----------
            var arp = ReadArpTable();
            var registered = await BuildRegisteredIpMapAsync(db);

            var hosts = new List<ScanHost>();
            foreach (var kv in found.OrderBy(k => IpKey(k.Key)))
            {
                var hostname = await DnsWithTimeoutAsync(kv.Key, 1200);
                arp.TryGetValue(kv.Key, out var mac);
                registered.TryGetValue(kv.Key, out var sysId);
                hosts.Add(new ScanHost
                {
                    Ip = kv.Key,
                    Hostname = hostname,
                    Mac = mac ?? "",
                    SystemInfoId = sysId,
                    Method = kv.Value
                });
                status.FoundCount = hosts.Count;
                status.Hosts = hosts.ToList(); // اسنپ‌شات زنده
            }

            status.Progress = 100;
        }
        catch (Exception ex)
        {
            status.Error = ex.Message;
        }
        finally
        {
            status.Running = false;
            status.ElapsedMs = (int)sw.ElapsedMilliseconds;
        }
    }

    // ================= تشخیص رنج =================

    private static (List<string> Targets, string LocalIp, string Adapter, string? Error) ParseRange(string rangeText)
    {
        rangeText = (rangeText ?? "").Trim();

        // ---- خودکار: شبکه‌ی خودِ سرور ----
        if (rangeText.Length == 0 || rangeText.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            IPAddress? localIp = null, mask = null, llIp = null, llMask = null;
            var adapter = "";
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = ua.Address.GetAddressBytes();
                    var priv = b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
                    var ll = b[0] == 169 && b[1] == 254;
                    if (priv) { localIp = ua.Address; mask = ua.IPv4Mask; adapter = nic.Name; break; }
                    if (ll && llIp is null) { llIp = ua.Address; llMask = ua.IPv4Mask; }
                }
                if (localIp != null) break;
            }
            if (localIp == null && llIp != null) { localIp = llIp; mask = llMask; }
            if (localIp == null || mask == null)
                return (new List<string>(), "", "", "هیچ کارت شبکه‌ی فعالی با آی‌پی مناسب پیدا نشد.");

            var targets = BuildFromCidr(localIp, mask, MaxTargets);
            return (targets, localIp.ToString(), adapter, null);
        }

        // ---- CIDR: 192.168.1.0/24 ----
        var cidr = Regex.Match(rangeText, @"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})/(\d{1,2})$");
        if (cidr.Success)
        {
            if (!IPAddress.TryParse(cidr.Groups[1].Value, out var netIp)) return (null!, "", "", "آی‌پی نامعتبر است.");
            var prefix = int.Parse(cidr.Groups[2].Value);
            if (prefix < 8 || prefix > 30) return (null!, "", "", "پیشوند باید بین ۸ تا ۳۰ باشد (مثلاً /24).");
            var maskBytes = PrefixToMask(prefix);
            var targets = BuildFromCidr(netIp, new IPAddress(maskBytes), MaxTargets);
            return (targets, netIp.ToString(), "", null);
        }

        // ---- بازه با خط تیره: 192.168.1.1-192.168.1.254 یا 192.168.1.1-254 ----
        var dash = Regex.Match(rangeText, @"^(\d{1,3}\.\d{1,3}\.\d{1,3})\.(\d{1,3})-(?:(\d{1,3}\.\d{1,3}\.\d{1,3})\.)?(\d{1,3})$");
        if (dash.Success)
        {
            var baseA = dash.Groups[1].Value;
            var start = int.Parse(dash.Groups[2].Value);
            var end = int.Parse(dash.Groups[4].Value);
            var baseB = dash.Groups[3].Success ? dash.Groups[3].Value : baseA;
            if (baseA != baseB) return (null!, "", "", "بازه باید داخل یک /24 باشد (سه اکتت اول یکسان).");
            if (start < 1 || end > 254 || start > end) return (null!, "", "", "بازه‌ی نامعتبر (۱ تا ۲۵۴).");
            var targets = new List<string>();
            for (var i = start; i <= end && targets.Count < MaxTargets; i++)
                targets.Add($"{baseA}.{i}");
            return (targets, "", "", null);
        }

        // ---- پیشوند: 192.168.1 ----
        var prefixOnly = Regex.Match(rangeText, @"^(\d{1,3})\.(\d{1,3})\.(\d{1,3})$");
        if (prefixOnly.Success)
        {
            var targets = new List<string>();
            for (var i = 1; i <= 254; i++)
                targets.Add($"{prefixOnly.Groups[1].Value}.{prefixOnly.Groups[2].Value}.{prefixOnly.Groups[3].Value}.{i}");
            return (targets, "", "", null);
        }

        // ---- یک آی‌پی تکی ----
        if (Regex.IsMatch(rangeText, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$"))
            return (new List<string> { rangeText }, rangeText, "", null);

        return (null!, "", "",
            "فرمت رنج نامعتبر است. مثال‌های مجاز: auto — 192.168.1 — 192.168.1.0/24 — 192.168.1.1-192.168.1.254");
    }

    private static byte[] PrefixToMask(int prefix)
    {
        var mask = 0xFFFFFFFF << (32 - prefix);
        return BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)mask));
    }

    private static List<string> BuildFromCidr(IPAddress ip, IPAddress mask, int maxCount)
    {
        var prefix = GetPrefixLength(mask);
        var hostBits = 32 - prefix;
        var total = hostBits >= 31 ? int.MaxValue : (1 << hostBits);

        var netBytes = GetNetworkAddress(ip, mask).GetAddressBytes();
        var netLong = (long)netBytes[0] << 24 | (long)netBytes[1] << 16 | (long)netBytes[2] << 8 | netBytes[3];

        // اگر رنج بزرگ بود، /24 خودمان را اسکن می‌کنیم
        if (total > maxCount)
        {
            var b = ip.GetAddressBytes();
            var list = new List<string>();
            for (var i = 1; i <= 254; i++)
            {
                var bb = (byte[])b.Clone();
                bb[3] = (byte)i;
                list.Add(new IPAddress(bb).ToString());
            }
            return list;
        }

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
        result.Add(ip.ToString());
        return result.Distinct().ToList();
    }

    // ================= ابزارها =================

    private static async Task<string> DnsWithTimeoutAsync(string ip, int timeoutMs)
    {
        try
        {
            var task = Dns.GetHostEntryAsync(IPAddress.Parse(ip));
            var done = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (done == task && task.IsCompletedSuccessfully) return task.Result.HostName ?? "";
        }
        catch { }
        return "";
    }

    private static long IpKey(string ip)
    {
        var p = ip.Split('.');
        return p.Length == 4 ? (long.Parse(p[0]) << 24) + (long.Parse(p[1]) << 16) + (long.Parse(p[2]) << 8) + long.Parse(p[3]) : 0;
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

    private static Dictionary<string, string> ReadArpTable()
    {
        var map = new Dictionary<string, string>();
        try
        {
            var psi = new ProcessStartInfo("arp", "-a")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd();
                foreach (Match m in Regex.Matches(output,
                    @"(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F]{2}[:-][0-9a-fA-F]{2}[:-][0-9a-fA-F]{2}[:-][0-9a-fA-F]{2}[:-][0-9a-fA-F]{2}[:-][0-9a-fA-F]{2})"))
                    map[m.Groups[1].Value] = m.Groups[2].Value.Replace('-', ':').ToUpperInvariant();
            }
        }
        catch { }

        if (map.Count == 0)
        {
            try
            {
                var psi = new ProcessStartInfo("ip", "neigh")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
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

    private static async Task<Dictionary<string, int>> BuildRegisteredIpMapAsync(AppDbContext db)
    {
        var map = new Dictionary<string, int>();
        try
        {
            var systems = await db.SystemInfos.AsNoTracking().ToListAsync();
            foreach (var s in systems)
            {
                if (string.IsNullOrWhiteSpace(s.DetailsJson)) continue;
                foreach (Match m in Regex.Matches(s.DetailsJson, @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})"))
                {
                    var ip = m.Groups[1].Value;
                    if (!map.ContainsKey(ip)) map[ip] = s.Id;
                }
            }
        }
        catch { }
        return map;
    }
}
