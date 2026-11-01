using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using InventoryAgent.Models;

namespace InventoryAgent.Collectors;

/// <summary>
/// جمع‌کننده‌ی سخت‌افزار لینوکس از روی ‎/proc و ‎/sys (بدون نیاز به root در اکثر بخش‌ها).
/// بخش رم در صورت دسترسی root و نصب بودن dmidecode دقیق‌تر می‌شود.
/// </summary>
public sealed class LinuxCollector : IHardwareCollector
{
    public string OsName
    {
        get
        {
            try
            {
                var desc = RuntimeInformation.OSDescription.Trim();
                if (!string.IsNullOrEmpty(desc)) return desc;
            }
            catch { }
            return ReadFirst("/etc/os-release", "PRETTY_NAME=") ?? Environment.OSVersion.VersionString;
        }
    }

    public DetailsDocument Collect()
    {
        var d = new DetailsDocument();
        Try(() => CollectBoard(d));
        Try(() => CollectCpus(d));
        Try(() => CollectRam(d));
        Try(() => CollectDisks(d));
        Try(() => CollectGpus(d));
        Try(() => CollectMonitors(d));
        Try(() => CollectNet(d));
        Try(() => CollectVolumes(d));
        return d;
    }

    // ================= بخش‌ها =================

    private static void CollectBoard(DetailsDocument d)
    {
        d.Board = Join2(Read("/sys/class/dmi/id/board_vendor"), Read("/sys/class/dmi/id/board_name"));
        d.BoardSerial = Read("/sys/class/dmi/id/board_serial");
        d.ComputerModel = Join2(Read("/sys/class/dmi/id/sys_vendor"), Read("/sys/class/dmi/id/product_name"));
    }

    private static void CollectCpus(DetailsDocument d)
    {
        // گروه‌بندی بر اساس physical id (پشتیبانی چند سوکت)
        var packages = new Dictionary<string, CpuAcc>();
        try
        {
            var lines = File.ReadAllLines("/proc/cpuinfo");
            string phys = "0", model = "", cores = "", mhz = "";
            void Flush()
            {
                if (model.Length == 0 && mhz.Length == 0) return;
                if (!packages.TryGetValue(phys, out var acc))
                {
                    acc = new CpuAcc { Name = model, Cores = cores, Mhz = mhz };
                    packages[phys] = acc;
                }
                acc.Threads++;
                if (acc.Name.Length == 0) acc.Name = model;
            }
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) { Flush(); phys = "0"; model = ""; cores = ""; mhz = ""; continue; }
                var kv = line.Split(':', 2);
                if (kv.Length != 2) continue;
                var k = kv[0].Trim();
                var v = kv[1].Trim();
                if (k == "physical id") phys = v;
                else if (k == "model name" && model.Length == 0) model = v;
                else if (k == "cpu cores" && cores.Length == 0) cores = v;
                else if (k == "cpu MHz" && mhz.Length == 0) mhz = v;
            }
            Flush();
        }
        catch { }

        if (packages.Count == 0)
        {
            d.Cpus.Add(new CpuDetail { Name = RuntimeInformation.ProcessArchitecture.ToString() });
            return;
        }
        foreach (var acc in packages.Values)
        {
            int.TryParse(acc.Cores, out var cores);
            double.TryParse(acc.Mhz, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var mhz);
            if (cores == 0) cores = acc.Threads;
            d.Cpus.Add(new CpuDetail
            {
                Name = acc.Name.Length > 0 ? acc.Name : "Unknown CPU",
                Cores = cores,
                Threads = acc.Threads,
                ClockGhz = Math.Round(mhz / 1000.0, 2)
            });
        }
    }

    private sealed class CpuAcc
    {
        public string Name = "";
        public string Cores = "";
        public string Mhz = "";
        public int Threads;
    }

    private static void CollectRam(DetailsDocument d)
    {
        // تلاش اول: dmidecode (دقیق، هر ماژول جدا) — معمولاً نیاز به root دارد
        var outp = Run("dmidecode", "-t memory");
        if (!string.IsNullOrEmpty(outp) && ParseDmidecode(outp, d) > 0)
            return;

        // fallback: یک رکورد تجمیعی از روی MemTotal
        try
        {
            foreach (var line in File.ReadAllLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                    {
                        var gb = SizeUtil.BytesToGb(kb * 1024L);
                        d.RamSticks.Add(new RamDetail { Slot = "System Memory", CapacityGb = gb });
                    }
                    break;
                }
            }
        }
        catch { }
    }

    private static int ParseDmidecode(string text, DetailsDocument d)
    {
        var count = 0;
        try
        {
            string size = "", type = "", speed = "", maker = "", part = "", serial = "", slot = "";
            void Flush()
            {
                if (size.Length == 0 || size.StartsWith("No Module", StringComparison.OrdinalIgnoreCase)) return;
                var gb = ParseDmidecodeSizeGb(size);
                if (gb <= 0) return;
                int.TryParse(new string(speed.Where(char.IsDigit).ToArray()), out var mhz);
                d.RamSticks.Add(new RamDetail
                {
                    Slot = slot.Length > 0 ? slot : $"DIMM {count + 1}",
                    CapacityGb = gb,
                    Type = type,
                    SpeedMhz = mhz,
                    Manufacturer = CleanUnknown(maker),
                    PartNumber = CleanUnknown(part),
                    SerialNumber = CleanUnknown(serial)
                });
                count++;
            }
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("Memory Device", StringComparison.Ordinal)) { Flush(); size = type = speed = maker = part = serial = slot = ""; continue; }
                var kv = line.Split(':', 2);
                if (kv.Length != 2) continue;
                var k = kv[0].Trim();
                var v = kv[1].Trim();
                switch (k)
                {
                    case "Size": size = v; break;
                    case "Type": type = v; break;
                    case "Speed": speed = v; break;
                    case "Manufacturer": maker = v; break;
                    case "Part Number": part = v; break;
                    case "Serial Number": serial = v; break;
                    case "Bank Locator":
                    case "Locator": if (slot.Length == 0) slot = v; else if (k == "Bank Locator") slot = $"{v} {slot}"; break;
                }
            }
            Flush();
        }
        catch { }
        return count;
    }

    private static int ParseDmidecodeSizeGb(string size)
    {
        // نمونه‌ها: "8192 MB" | "8 GB" | "1024 MB"
        try
        {
            var parts = size.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && double.TryParse(parts[0],
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                var unit = parts[1].ToUpperInvariant();
                var gb = unit.StartsWith("GB") ? n : unit.StartsWith("MB") ? n / 1024.0 : unit.StartsWith("KB") ? n / 1024 / 1024 : 0;
                var r = (int)Math.Round(gb);
                return r <= 0 && gb > 0 ? 1 : r;
            }
        }
        catch { }
        return 0;
    }

    private static void CollectDisks(DetailsDocument d)
    {
        string[] blocks;
        try { blocks = Directory.GetDirectories("/sys/block"); }
        catch { return; }
        foreach (var blk in blocks.OrderBy(x => x))
        {
            try
            {
                var name = Path.GetFileName(blk);
                if (!(name.StartsWith("sd") || name.StartsWith("hd") || name.StartsWith("vd") ||
                      name.StartsWith("nvme") || name.StartsWith("mmcblk")))
                    continue;
                var model = Read(Path.Combine(blk, "device/model"));
                if (model.Length == 0) model = name;
                var sizeSectors = Read(Path.Combine(blk, "size"));
                var sectorSize = Read(Path.Combine(blk, "queue/logical_block_size"));
                long.TryParse(sizeSectors, out var sectors);
                if (!int.TryParse(sectorSize, out var bsize) || bsize <= 0) bsize = 512;
                var serial = Read(Path.Combine(blk, "device/serial"));
                string iface = name.StartsWith("nvme") ? "NVMe"
                    : name.StartsWith("mmcblk") ? "eMMC"
                    : name.StartsWith("vd") ? "Virtual" : "SATA";
                d.Disks.Add(new DiskDetail
                {
                    Model = model,
                    SizeGb = SizeUtil.BytesToGb(sectors * (long)bsize),
                    Interface = iface,
                    SerialNumber = serial,
                    Smart = "Unknown" // روی لینوکس نیاز به smartctl دارد — فعلاً Unknown
                });
            }
            catch { }
        }
    }

    private static void CollectGpus(DetailsDocument d)
    {
        // تلاش اول: lspci
        var outp = Run("lspci", "-mm");
        if (!string.IsNullOrEmpty(outp))
        {
            foreach (var line in outp.Split('\n'))
            {
                // نمونه: 01:00.0 "VGA compatible controller" "NVIDIA Corporation" "GA106 [GeForce RTX 3060]"
                if (!(line.Contains("VGA") || line.Contains("3D controller") || line.Contains("Display controller")))
                    continue;
                var quoted = new List<string>();
                int i = 0;
                while (true)
                {
                    var a = line.IndexOf('"', i);
                    if (a < 0) break;
                    var b = line.IndexOf('"', a + 1);
                    if (b < 0) break;
                    quoted.Add(line.Substring(a + 1, b - a - 1));
                    i = b + 1;
                }
                if (quoted.Count >= 3)
                {
                    var name = $"{quoted[1]} {quoted[2]}".Trim();
                    if (name.Length > 0) d.Gpus.Add(new GpuDetail { Name = name });
                }
            }
            if (d.Gpus.Count > 0) return;
        }
        // fallback: کارت‌های DRM (بدون نام دقیق)
        try
        {
            foreach (var card in Directory.GetDirectories("/sys/class/drm", "card*").OrderBy(x => x))
            {
                if (card.Contains('-')) continue; // فقط خود کارت، نه خروجی‌ها
                var vendor = Read(Path.Combine(card, "device/vendor"));
                var device = Read(Path.Combine(card, "device/device"));
                d.Gpus.Add(new GpuDetail { Name = $"GPU {vendor.Replace("0x", "")}:{device.Replace("0x", "")}".Trim() });
            }
        }
        catch { }
    }

    private static void CollectMonitors(DetailsDocument d)
    {
        string[] outputs;
        try { outputs = Directory.GetDirectories("/sys/class/drm").Where(p => Path.GetFileName(p).Contains('-')).OrderBy(x => x).ToArray(); }
        catch { return; }
        foreach (var output in outputs)
        {
            try
            {
                var status = Read(Path.Combine(output, "status"));
                if (!status.Equals("connected", StringComparison.OrdinalIgnoreCase)) continue;
                var edidPath = Path.Combine(output, "edid");
                if (!File.Exists(edidPath)) continue;
                var edid = File.ReadAllBytes(edidPath);
                if (edid.Length < 128) continue;
                var (name, serial) = ParseEdid(edid);
                var modesPath = Path.Combine(output, "modes");
                var res = "";
                if (File.Exists(modesPath))
                {
                    var first = File.ReadLines(modesPath).FirstOrDefault()?.Trim() ?? "";
                    res = first; // مثل 1920x1080
                }
                d.Monitors.Add(new MonitorDetail
                {
                    Name = name.Length > 0 ? name : Path.GetFileName(output),
                    Resolution = res,
                    SerialNumber = serial,
                    IsPrimary = d.Monitors.Count == 0
                });
            }
            catch { }
        }
    }

    private static void CollectNet(DetailsDocument d)
    {
        var gateways = ReadGateways(); // iface -> gateway
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { return; }
        foreach (var nic in nics)
        {
            try
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var macRaw = nic.GetPhysicalAddress().ToString();
                if (macRaw.Length == 0) continue;
                var mac = string.Join(":", Enumerable.Range(0, macRaw.Length / 2).Select(k => macRaw.Substring(k * 2, 2)));
                string ipv4 = "";
                try
                {
                    var unicasts = nic.GetIPProperties().UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => a.Address.ToString())
                        .ToList();
                    ipv4 = unicasts.FirstOrDefault(ip => !ip.StartsWith("169.254."))
                        ?? unicasts.FirstOrDefault() ?? "";
                }
                catch { }
                gateways.TryGetValue(nic.Name, out var gw);
                d.NetAdapters.Add(new NetAdapterDetail
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    Type = MapNicType(nic.NetworkInterfaceType),
                    MacAddress = mac,
                    Ipv4 = ipv4,
                    Gateway = gw ?? ""
                });
            }
            catch { }
        }
    }

    private static void CollectVolumes(DetailsDocument d)
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return; }
        foreach (var drive in drives)
        {
            try
            {
                if (!drive.IsReady) continue;
                if (drive.DriveType != DriveType.Fixed) continue;
                var totalGb = SizeUtil.BytesToGb(drive.TotalSize);
                d.Volumes.Add(new VolumeDetail
                {
                    Letter = drive.Name,
                    Label = drive.VolumeLabel,
                    TotalGb = totalGb,
                    UsedGb = SizeUtil.BytesToGb(drive.TotalSize - drive.AvailableFreeSpace)
                });
            }
            catch { }
        }
    }

    // ================= ابزار =================

    private static void Try(Action a)
    {
        try { a(); }
        catch { }
    }

    private static string Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return "";
            return File.ReadAllText(path).Trim();
        }
        catch { return ""; }
    }

    private static string? ReadFirst(string path, string prefix)
    {
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                    return line.Substring(prefix.Length).Trim().Trim('"');
            }
        }
        catch { }
        return null;
    }

    private static string Join2(string a, string b)
        => string.Join(" ", new[] { a, b }.Where(s => s.Length > 0));

    private static string CleanUnknown(string s)
    {
        s = s.Trim();
        if (s.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Not Specified", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            s.All(c => c == '0'))
            return "";
        return s;
    }

    /// <summary>اجرای دستور خارجی با تایم‌اوت (برای dmidecode/lspci). خروجی کوچک فرض شده است.</summary>
    private static string? Run(string file, string args, int timeoutMs = 8000)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            if (!p.Start()) return null;
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(); } catch { }
                return null;
            }
            if (p.ExitCode != 0) return null;
            return p.StandardOutput.ReadToEnd();
        }
        catch { return null; }
    }

    private static Dictionary<string, string> ReadGateways()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadAllLines("/proc/net/route"))
            {
                var f = line.Split('\t', ' ');
                if (f.Length < 8) continue;
                if (f[0] == "Iface") continue;
                if (f[1] != "00000000") continue; // فقط مسیر پیش‌فرض
                if (!int.TryParse(f[3], System.Globalization.NumberStyles.HexNumber, null, out var flags)) continue;
                if ((flags & 0x2) == 0) continue; // RTF_GATEWAY
                var gwHex = f[2];
                if (gwHex.Length == 8)
                {
                    var b = Enumerable.Range(0, 4)
                        .Select(k => Convert.ToByte(gwHex.Substring(k * 2, 2), 16))
                        .Reverse().ToArray(); // little-endian
                    map[f[0]] = string.Join(".", b);
                }
            }
        }
        catch { }
        return map;
    }

    private static string MapNicType(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit or
        NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or
        NetworkInterfaceType.FastEthernetFx => "Ethernet",
        NetworkInterfaceType.Wireless80211 => "WiFi",
        NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp => "Virtual",
        NetworkInterfaceType.GenericModem => "Modem",
        _ => t.ToString()
    };

    /// <summary>تجزیه‌ی حداقلی EDID: نام مانیتور (تگ 0xFC) و سریال (تگ 0xFF یا عددی).</summary>
    private static (string name, string serial) ParseEdid(byte[] edid)
    {
        string name = "", serial = "";
        try
        {
            for (var k = 0; k < 4; k++)
            {
                var off = 54 + k * 18;
                if (off + 18 > edid.Length) break;
                if (edid[off] != 0 || edid[off + 1] != 0) continue;
                var tag = edid[off + 3];
                var text = Encoding.ASCII.GetString(edid, off + 5, 13).Trim().TrimEnd('\n', '\r', ' ');
                if (tag == 0xFC && name.Length == 0) name = text;
                else if (tag == 0xFF && serial.Length == 0) serial = text;
            }
            if (serial.Length == 0)
            {
                var sn = BitConverter.ToUInt32(edid, 12);
                if (sn != 0) serial = sn.ToString("X8");
            }
            if (name.Length == 0)
            {
                var m = (ushort)((edid[8] << 8) | edid[9]);
                var prod = (ushort)(edid[10] | (edid[11] << 8));
                string L(int v) => (v is >= 1 and <= 26) ? ((char)('A' + v - 1)).ToString() : "?";
                name = $"{L((m >> 10) & 0x1F)}{L((m >> 5) & 0x1F)}{L(m & 0x1F)} {prod:X4}";
            }
        }
        catch { }
        return (name.Trim(), serial.Trim());
    }
}
