using System.Management;
using System.Runtime.Versioning;
using InventoryAgent.Models;

namespace InventoryAgent.Collectors;

/// <summary>
/// جمع‌کننده‌ی سخت‌افزار ویندوز از طریق WMI.
/// همه‌ی قطعات چندتایی پشتیبانی می‌شوند: چند CPU، چند ماژول رم، چند هارد، چند گرافیک، چند مانیتور.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCollector : IHardwareCollector
{
    private string? _osName;

    public string OsName
    {
        get
        {
            if (_osName is null)
            {
                _osName = "";
                Each("SELECT Caption FROM Win32_OperatingSystem",
                    mo => { if (string.IsNullOrEmpty(_osName)) _osName = S(mo, "Caption"); });
                if (string.IsNullOrEmpty(_osName))
                    _osName = Environment.OSVersion.VersionString;
            }
            return _osName;
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
        Each("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard", mo =>
        {
            if (string.IsNullOrEmpty(d.Board))
            {
                d.Board = Join2(S(mo, "Manufacturer"), S(mo, "Product"));
                d.BoardSerial = S(mo, "SerialNumber");
            }
        });
        Each("SELECT Manufacturer, Model FROM Win32_ComputerSystem", mo =>
        {
            if (string.IsNullOrEmpty(d.ComputerModel))
                d.ComputerModel = Join2(S(mo, "Manufacturer"), S(mo, "Model"));
        });
    }

    private static void CollectCpus(DetailsDocument d)
    {
        Each("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor", mo =>
        {
            d.Cpus.Add(new CpuDetail
            {
                Name = S(mo, "Name"),
                Cores = I(mo, "NumberOfCores"),
                Threads = I(mo, "NumberOfLogicalProcessors"),
                ClockGhz = Math.Round(I(mo, "MaxClockSpeed") / 1000.0, 2)
            });
        });
    }

    private static void CollectRam(DetailsDocument d)
    {
        var idx = 0;
        Each("SELECT BankLabel, DeviceLocator, Capacity, MemoryType, SMBIOSMemoryType, ConfiguredClockSpeed, Speed, Manufacturer, PartNumber, SerialNumber FROM Win32_PhysicalMemory", mo =>
        {
            idx++;
            var slot = Join2(S(mo, "BankLabel"), S(mo, "DeviceLocator"));
            if (string.IsNullOrEmpty(slot)) slot = $"DIMM {idx}";
            var speed = I(mo, "ConfiguredClockSpeed");
            if (speed == 0) speed = I(mo, "Speed");
            d.RamSticks.Add(new RamDetail
            {
                Slot = slot,
                CapacityGb = SizeUtil.BytesToGb(U64(mo, "Capacity")),
                Type = MapMemoryType(I(mo, "SMBIOSMemoryType"), I(mo, "MemoryType")),
                SpeedMhz = speed,
                Manufacturer = S(mo, "Manufacturer"),
                PartNumber = S(mo, "PartNumber"),
                SerialNumber = S(mo, "SerialNumber")
            });
        });
    }

    private static void CollectDisks(DetailsDocument d)
    {
        var smarts = ReadSmartStatuses(); // PNPDeviceID(normalized) -> status
        var smartList = smarts.Values.ToList();
        var diskIdx = 0;
        Each("SELECT Model, Size, InterfaceType, SerialNumber, PNPDeviceID FROM Win32_DiskDrive", mo =>
        {
            var pnp = Norm(S(mo, "PNPDeviceID"));
            string smart = "Unknown";
            if (pnp.Length > 0 && smarts.TryGetValue(pnp, out var s))
                smart = s;
            else if (smartList.Count == 1)
                smart = smartList[0]; // تک‌دیسک: تطبیق قطعی
            else if (diskIdx < smartList.Count && smarts.Count == CountDisks())
                smart = smartList[diskIdx]; // تعداد برابر: تطبیق ترتیبی
            diskIdx++;
            d.Disks.Add(new DiskDetail
            {
                Model = S(mo, "Model"),
                SizeGb = SizeUtil.BytesToGb(U64(mo, "Size")),
                Interface = S(mo, "InterfaceType"),
                SerialNumber = S(mo, "SerialNumber"),
                Smart = smart
            });
        });
    }

    private static void CollectGpus(DetailsDocument d)
    {
        Each("SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController", mo =>
        {
            var name = S(mo, "Name");
            if (name.Length == 0) return;
            var w = I(mo, "CurrentHorizontalResolution");
            var h = I(mo, "CurrentVerticalResolution");
            d.Gpus.Add(new GpuDetail
            {
                Name = name,
                Resolution = (w > 0 && h > 0) ? $"{w}x{h}" : ""
            });
        });
    }

    private static void CollectMonitors(DetailsDocument d)
    {
        // نام و سریال دقیق از EDID (WmiMonitorID) — به‌ترتیب همه‌ی مانیتورها
        Each("SELECT ManufacturerName, ProductCodeID, SerialNumberID, UserFriendlyName FROM WmiMonitorID",
            mo =>
            {
                var friendly = DecodeU16(mo["UserFriendlyName"]);
                var maker = DecodeU16(mo["ManufacturerName"]);
                ushort prod = mo["ProductCodeID"] is ushort[] pa && pa.Length > 0 ? pa[0] : (ushort)0;
                var name = friendly.Length > 0 ? friendly
                    : (maker.Length > 0 ? $"{maker} {prod:X4}".Trim() : "");
                if (name.Length == 0) name = "Generic Monitor";
                d.Monitors.Add(new MonitorDetail
                {
                    Name = name,
                    SerialNumber = DecodeU16(mo["SerialNumberID"]),
                    IsPrimary = d.Monitors.Count == 0
                });
            }, @"\\.\root\WMI");

        // رزولوشن فعلی از کارت گرافیک — به مانیتور اصلی نسبت داده می‌شود
        string res = "";
        Each("SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController", mo =>
        {
            if (res.Length == 0)
            {
                var w = I(mo, "CurrentHorizontalResolution");
                var h = I(mo, "CurrentVerticalResolution");
                if (w > 0 && h > 0) res = $"{w}x{h}";
            }
        });
        var primary = d.Monitors.FirstOrDefault(m => m.IsPrimary) ?? d.Monitors.FirstOrDefault();
        if (primary != null) primary.Resolution = res;
    }

    private static void CollectNet(DetailsDocument d)
    {
        Each("SELECT Description, MACAddress, IPAddress, DefaultIPGateway FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE", mo =>
        {
            var desc = S(mo, "Description");
            var mac = S(mo, "MACAddress");
            var ips = StrArr(mo, "IPAddress").Where(IsIPv4).ToList();
            var gws = StrArr(mo, "DefaultIPGateway");
            d.NetAdapters.Add(new NetAdapterDetail
            {
                Name = desc,
                Description = desc,
                Type = GuessNetType(desc),
                MacAddress = mac,
                Ipv4 = ips.Count > 0 ? string.Join(";", ips) : "",
                Gateway = gws.Count > 0 ? gws[0] : ""
            });
        });
    }

    private static void CollectVolumes(DetailsDocument d)
    {
        Each("SELECT DeviceID, VolumeName, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3", mo =>
        {
            var total = U64(mo, "Size");
            var free = U64(mo, "FreeSpace");
            var totalGb = SizeUtil.BytesToGb(total);
            d.Volumes.Add(new VolumeDetail
            {
                Letter = S(mo, "DeviceID"),
                Label = S(mo, "VolumeName"),
                TotalGb = totalGb,
                UsedGb = total >= free ? SizeUtil.BytesToGb(total - free) : 0
            });
        });
    }

    // ================= وضعیت S.M.A.R.T =================

    private static Dictionary<string, string> ReadSmartStatuses()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Each("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", mo =>
            {
                var inst = Norm(S(mo, "InstanceName"));
                if (inst.Length == 0) return;
                var fail = B(mo, "PredictFailure");
                map[inst] = fail ? "PredFail" : "Healthy";
            }, @"\\.\root\WMI");
        }
        catch { }
        return map;
    }

    private static int CountDisks()
    {
        var n = 0;
        Each("SELECT PNPDeviceID FROM Win32_DiskDrive", _ => n++);
        return n;
    }

    // ================= ابزار WMI =================

    private static void Each(string wql, Action<ManagementObject> action, string scope = @"\\.\root\CIMV2")
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            using var results = searcher.Get();
            foreach (ManagementObject mo in results)
            {
                try { action(mo); }
                catch { }
                finally { try { mo.Dispose(); } catch { } }
            }
        }
        catch { }
    }

    private static void Try(Action a)
    {
        try { a(); }
        catch { }
    }

    private static string S(ManagementBaseObject mo, string prop)
    {
        try
        {
            var v = mo[prop];
            return v is null ? "" : (v.ToString()?.Trim() ?? "");
        }
        catch { return ""; }
    }

    private static ulong U64(ManagementBaseObject mo, string prop)
    {
        try
        {
            var v = mo[prop];
            return v switch
            {
                ulong u => u,
                uint u => u,
                ushort u => u,
                long l when l > 0 => (ulong)l,
                int i when i > 0 => (uint)i,
                string s when ulong.TryParse(s.Trim(), out var r) => r,
                _ => 0
            };
        }
        catch { return 0; }
    }

    private static int I(ManagementBaseObject mo, string prop)
    {
        try
        {
            var v = mo[prop];
            return v switch
            {
                ushort u => u,
                uint u => (int)Math.Min(u, int.MaxValue),
                ulong u => (int)Math.Min(u, int.MaxValue),
                int i => i,
                short s => s,
                string s when int.TryParse(s.Trim(), out var r) => r,
                _ => 0
            };
        }
        catch { return 0; }
    }

    private static bool B(ManagementBaseObject mo, string prop)
    {
        try { return mo[prop] is bool b && b; }
        catch { return false; }
    }

    private static List<string> StrArr(ManagementBaseObject mo, string prop)
    {
        try
        {
            if (mo[prop] is string[] arr)
                return arr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        }
        catch { }
        return new List<string>();
    }

    private static string DecodeU16(object? v)
    {
        try
        {
            if (v is not ushort[] arr) return "";
            var chars = new char[arr.Length];
            var n = 0;
            foreach (var c in arr)
            {
                if (c == 0) break;
                chars[n++] = (char)c;
            }
            return new string(chars, 0, n).Trim();
        }
        catch { return ""; }
    }

    private static string Join2(string a, string b)
        => string.Join(" ", new[] { a, b }.Where(s => s.Length > 0));

    private static string Norm(string s)
        => s.ToUpperInvariant().Replace(" ", "").Trim();

    private static bool IsIPv4(string s)
        => s.Contains('.') && !s.Contains(':') &&
           System.Net.IPAddress.TryParse(s, out var ip) &&
           ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    private static string GuessNetType(string desc)
    {
        var t = desc.ToLowerInvariant();
        if (t.Contains("wi-fi") || t.Contains("wifi") || t.Contains("wireless") || t.Contains("wlan")) return "WiFi";
        if (t.Contains("bluetooth")) return "Bluetooth";
        if (t.Contains("virtual") || t.Contains("vpn") || t.Contains("vmware") || t.Contains("hyper-v") ||
            t.Contains("vethernet") || t.Contains("tunnel") || t.Contains("loopback")) return "Virtual";
        if (t.Contains("cellular") || t.Contains("mobile") || t.Contains("lte") || t.Contains("5g")) return "Cellular";
        return "Ethernet";
    }

    /// <summary>نگاشت نوع رم از روی SMBIOS (با fallback روی MemoryType).</summary>
    private static string MapMemoryType(int smbios, int legacy)
    {
        return smbios switch
        {
            18 => "SDRAM",
            19 => "RDRAM",
            20 => "DDR",
            21 => "DDR2",
            22 => "DDR2 FB-DIMM",
            24 => "DDR3",
            25 => "FBD2",
            26 => "DDR4",
            27 => "LPDDR",
            28 => "LPDDR2",
            29 => "LPDDR3",
            30 => "LPDDR4",
            31 => "Logical non-volatile",
            32 => "HBM",
            33 => "HBM2",
            34 => "DDR5",
            35 => "LPDDR5",
            _ => legacy switch
            {
                3 => "DRAM",
                4 => "EDRAM",
                5 => "VRAM",
                6 => "SRAM",
                7 => "RAM",
                9 => "FLASH",
                _ => ""
            }
        };
    }
}
