using System.Text;
using Lextm.SharpSnmpLib;

namespace Inventory.Api.Services.Office;

/// <summary>نتیجهٔ تمیز و مستقل از کتابخانهٔ یک دور خواندن SNMP از دستگاه.</summary>
public sealed class PrinterSnapshot
{
    public string? SysDescr { get; set; }
    public string? SysObjectId { get; set; }
    public string? SysName { get; set; }
    public string? SysLocation { get; set; }
    public long? UptimeSeconds { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? MacAddress { get; set; }
    public string? FirmwareVersion { get; set; }
    /// <summary>آیا Printer-MIB پشتیبانی می‌شود (یعنی دستگاه چاپگر است)؟</summary>
    public bool IsPrinter { get; set; }
    public bool? IsColor { get; set; }
    /// <summary>running | warning | testing | down | unknown</summary>
    public string? DeviceStatus { get; set; }
    /// <summary>idle | printing | warmup | other | unknown</summary>
    public string? PrinterState { get; set; }
    public List<string> ErrorFlags { get; } = new();
    public long? PageCount { get; set; }
    public List<SupplyInfo> Supplies { get; } = new();
    public List<TrayInfo> Trays { get; } = new();
    public List<AlertInfo> Alerts { get; } = new();
    public string? DisplayText { get; set; }

    public sealed class SupplyInfo
    {
        public int Index { get; set; }
        public string Kind { get; set; } = "Other";
        public string? Color { get; set; }
        public string? Description { get; set; }
        public int Level { get; set; }
        public int MaxCapacity { get; set; }
        public int? Percent { get; set; }
    }

    public sealed class TrayInfo
    {
        public int Index { get; set; }
        public string? Name { get; set; }
        public string? MediaName { get; set; }
        public int CurrentLevel { get; set; }
        public int MaxCapacity { get; set; }
        public int? Percent { get; set; }
    }

    public sealed class AlertInfo
    {
        /// <summary>critical | warning | other</summary>
        public string Severity { get; set; } = "other";
        public int Code { get; set; }
        public string? Description { get; set; }
    }
}

/// <summary>
/// خواندن اطلاعات استاندارد چاپگر از Printer-MIB (RFC 3805) و Host-Resources-MIB (RFC 2790).
/// مستقل از برند؛ HP، Canon، Kyocera، Ricoh، Sharp، Xerox، Brother، Konica، Lexmark و ... را پوشش می‌دهد.
/// </summary>
public static class PrinterSnmpReader
{
    // ---- SNMPv2-MIB
    public const string SysDescr = "1.3.6.1.2.1.1.1.0";
    public const string SysObjectId = "1.3.6.1.2.1.1.2.0";
    public const string SysUpTime = "1.3.6.1.2.1.1.3.0";
    public const string SysName = "1.3.6.1.2.1.1.5.0";
    public const string SysLocation = "1.3.6.1.2.1.1.6.0";
    // ---- IF-MIB
    public const string IfPhysAddress = "1.3.6.1.2.1.2.2.1.6";
    // ---- HOST-RESOURCES-MIB
    public const string HrDeviceDescr = "1.3.6.1.2.1.25.3.2.1.3";
    public const string HrDeviceStatus = "1.3.6.1.2.1.25.3.2.1.5";
    public const string HrPrinterEntry = "1.3.6.1.2.1.25.3.5.1";       // .1 status  .2 detectedErrorState
    // ---- Printer-MIB
    public const string PrtGeneralSerial = "1.3.6.1.2.1.43.5.1.1.17";
    public const string PrtInputEntry = "1.3.6.1.2.1.43.8.2.1";
    public const string PrtMarkerLifeCount = "1.3.6.1.2.1.43.10.2.1.4";
    public const string PrtSuppliesEntry = "1.3.6.1.2.1.43.11.1.1";
    public const string PrtColorantValue = "1.3.6.1.2.1.43.12.1.1.4";
    public const string PrtConsoleText = "1.3.6.1.2.1.43.16.5.1.2";
    public const string PrtAlertEntry = "1.3.6.1.2.1.43.18.1.1";

    /// <summary>خواندن سریع برای کشف شبکه: فقط اطلاعات پایه + تشخیص چاپگر.</summary>
    public static PrinterSnapshot ReadBasic(SnmpSession s)
    {
        var snap = new PrinterSnapshot();
        var sys = s.Get(new[] { SysDescr, SysObjectId, SysName, SysLocation, SysUpTime });
        if (sys.Count == 0) throw new SnmpQueryException("دستگاه به SNMP پاسخ داد ولی اطلاعات سیستم (sysDescr) را برنگرداند.");
        snap.SysDescr = Text(sys.GetValueOrDefault(SysDescr));
        snap.SysObjectId = sys.GetValueOrDefault(SysObjectId)?.ToString();
        snap.SysName = Text(sys.GetValueOrDefault(SysName));
        snap.SysLocation = Text(sys.GetValueOrDefault(SysLocation));
        if (sys.GetValueOrDefault(SysUpTime) is TimeTicks tt) snap.UptimeSeconds = (long)(tt.ToUInt32() / 100);
        snap.Brand = DetectBrand(snap.SysObjectId, snap.SysDescr);

        var serial = s.Walk(PrtGeneralSerial, 4).FirstOrDefault();
        snap.SerialNumber = Text(serial?.Data);
        var printer = s.Walk(HrPrinterEntry, 8);
        snap.IsPrinter = serial != null || printer.Count > 0;

        var deviceDescr = s.Walk(HrDeviceDescr, 20);
        snap.Model = PickModel(deviceDescr, printer, snap.SysDescr);
        snap.FirmwareVersion = ExtractFirmware(snap.SysDescr);
        return snap;
    }

    /// <summary>خواندن کامل: وضعیت، خطاها، شمارنده، مواد مصرفی، سینی‌ها، هشدارها، LCD، MAC.</summary>
    public static PrinterSnapshot ReadFull(SnmpSession s)
    {
        var snap = ReadBasic(s);

        // ---- وضعیت چاپگر و پرچم‌های خطا (اولین ردیف hrPrinterTable)
        var printer = s.Walk(HrPrinterEntry, 8);
        var statusRow = printer.FirstOrDefault(v => Suffix(v, HrPrinterEntry).FirstOrDefault() == 1);
        if (statusRow != null)
        {
            var devIndex = Suffix(statusRow, HrPrinterEntry).ElementAtOrDefault(1);
            snap.PrinterState = Int(statusRow.Data) switch { 3 => "idle", 4 => "printing", 5 => "warmup", 1 => "other", _ => "unknown" };
            var ds = s.Get($"{HrDeviceStatus}.{devIndex}");
            snap.DeviceStatus = Int(ds) switch { 2 => "running", 3 => "warning", 4 => "testing", 5 => "down", _ => "unknown" };
        }
        var errRow = printer.FirstOrDefault(v => Suffix(v, HrPrinterEntry).FirstOrDefault() == 2);
        if (errRow?.Data is OctetString errBits) snap.ErrorFlags.AddRange(DecodeErrorState(errBits.GetRaw()));

        // ---- شمارندهٔ کل صفحات
        var life = s.Walk(PrtMarkerLifeCount, 4).FirstOrDefault();
        if (life != null) { var n = Long(life.Data); if (n is >= 0) snap.PageCount = n; }

        // ---- رنگ‌ها و مواد مصرفی
        var colorants = new Dictionary<int, string>();
        foreach (var v in s.Walk(PrtColorantValue, 64))
        {
            var sfx = Suffix(v, PrtColorantValue);
            if (sfx.Length >= 2) colorants[sfx[1]] = (Text(v.Data) ?? "").Trim().ToLowerInvariant();
        }
        var supplyRows = new SortedDictionary<int, Dictionary<int, ISnmpData>>();
        foreach (var v in s.Walk(PrtSuppliesEntry, 600))
        {
            var sfx = Suffix(v, PrtSuppliesEntry);            // col, hrDeviceIndex, supplyIndex
            if (sfx.Length < 3) continue;
            if (!supplyRows.TryGetValue(sfx[2], out var row)) supplyRows[sfx[2]] = row = new();
            row[sfx[0]] = v.Data;
        }
        foreach (var (idx, row) in supplyRows)
        {
            var type = Int(row.GetValueOrDefault(5)) ?? 2;
            var desc = Text(row.GetValueOrDefault(6));
            var max = Int(row.GetValueOrDefault(8)) ?? -2;
            var level = Int(row.GetValueOrDefault(9)) ?? -2;
            var colorIdx = Int(row.GetValueOrDefault(3)) ?? 0;
            var color = colorants.GetValueOrDefault(colorIdx);
            if (string.IsNullOrWhiteSpace(color) || color is "unknown" or "other") color = GuessColor(desc);
            var kind = SupplyKind(type, desc);
            snap.Supplies.Add(new PrinterSnapshot.SupplyInfo
            {
                Index = idx, Kind = kind, Color = string.IsNullOrWhiteSpace(color) ? null : color,
                Description = desc, Level = level, MaxCapacity = max, Percent = Percent(level, max)
            });
        }
        var tonerColors = snap.Supplies.Where(x => x.Kind is "Toner" or "Ink").Select(x => x.Color).Where(c => c != null).Distinct().ToList();
        if (tonerColors.Count > 0) snap.IsColor = tonerColors.Any(c => c is "cyan" or "magenta" or "yellow");

        // ---- سینی‌ها (فقط سینی‌هایی با ظرفیت معلوم)
        var trayRows = new SortedDictionary<int, Dictionary<int, ISnmpData>>();
        foreach (var v in s.Walk(PrtInputEntry, 400))
        {
            var sfx = Suffix(v, PrtInputEntry);              // col, hrDeviceIndex, inputIndex
            if (sfx.Length < 3) continue;
            if (!trayRows.TryGetValue(sfx[2], out var row)) trayRows[sfx[2]] = row = new();
            row[sfx[0]] = v.Data;
        }
        foreach (var (idx, row) in trayRows)
        {
            var max = Int(row.GetValueOrDefault(9)) ?? -2;
            var cur = Int(row.GetValueOrDefault(10)) ?? -2;
            var name = Text(row.GetValueOrDefault(13));
            if (string.IsNullOrWhiteSpace(name)) name = Text(row.GetValueOrDefault(18));
            snap.Trays.Add(new PrinterSnapshot.TrayInfo
            {
                Index = idx, Name = string.IsNullOrWhiteSpace(name) ? $"Tray {idx}" : name,
                MediaName = Text(row.GetValueOrDefault(12)), CurrentLevel = cur, MaxCapacity = max, Percent = Percent(cur, max)
            });
        }

        // ---- هشدارهای فعال دستگاه
        var alertRows = new SortedDictionary<string, Dictionary<int, ISnmpData>>();
        foreach (var v in s.Walk(PrtAlertEntry, 400))
        {
            var sfx = Suffix(v, PrtAlertEntry);              // col, hrDeviceIndex, alertIndex
            if (sfx.Length < 3) continue;
            var key = $"{sfx[1]}.{sfx[2]:D6}";
            if (!alertRows.TryGetValue(key, out var row)) alertRows[key] = row = new();
            row[sfx[0]] = v.Data;
        }
        foreach (var row in alertRows.Values)
        {
            var sev = Int(row.GetValueOrDefault(2)) ?? 1;
            snap.Alerts.Add(new PrinterSnapshot.AlertInfo
            {
                Severity = sev switch { 3 => "critical", 4 or 5 => "warning", _ => "other" },
                Code = Int(row.GetValueOrDefault(7)) ?? 0,
                Description = Text(row.GetValueOrDefault(8))
            });
        }

        // ---- متن نمایشگر (LCD)
        var lines = s.Walk(PrtConsoleText, 10).Select(v => Text(v.Data)).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (lines.Count > 0) snap.DisplayText = Trunc(string.Join(" | ", lines), 300);

        // ---- MAC
        foreach (var v in s.Walk(IfPhysAddress, 16))
        {
            if (v.Data is not OctetString os) continue;
            var raw = os.GetRaw();
            if (raw.Length == 6 && raw.Any(b => b != 0))
            {
                snap.MacAddress = string.Join(":", raw.Select(b => b.ToString("X2")));
                break;
            }
        }
        return snap;
    }

    // ================================================================== تفسیر

    /// <summary>بیت‌های hrPrinterDetectedErrorState (بیت ۰ = MSB بایت اول).</summary>
    public static IEnumerable<string> DecodeErrorState(byte[] raw)
    {
        string[] names =
        {
            "LowPaper", "NoPaper", "LowToner", "NoToner", "DoorOpen", "Jammed", "Offline", "ServiceRequested",
            "InputTrayMissing", "OutputTrayMissing", "MarkerSupplyMissing", "OutputNearFull", "OutputFull", "InputTrayEmpty", "OverduePreventMaint"
        };
        for (var bit = 0; bit < names.Length; bit++)
        {
            var b = bit / 8;
            if (b >= raw.Length) break;
            if ((raw[b] & (0x80 >> (bit % 8))) != 0) yield return names[bit];
        }
    }

    public static int? Percent(int level, int max)
    {
        if (level < 0) return level == -3 ? null : null;    // -3 = «مقداری باقی است»، -2 نامعلوم، -1 other
        if (max > 0) return Math.Clamp((int)Math.Round(level * 100.0 / max), 0, 100);
        return null;
    }

    public static string SupplyKind(int type, string? desc)
    {
        var d = (desc ?? "").ToLowerInvariant();
        return type switch
        {
            3 or 21 => "Toner",
            5 or 6 or 7 => "Ink",
            4 or 8 or 14 or 24 or 26 => "Waste",
            9 => "Drum",
            10 => "Developer",
            11 or 15 or 17 or 19 or 22 => "Fuser",
            20 => "Transfer",
            32 or 29 => "Staples",
            _ when d.Contains("toner") && d.Contains("waste") => "Waste",
            _ when d.Contains("waste") => "Waste",
            _ when d.Contains("toner") => "Toner",
            _ when d.Contains("drum") || d.Contains("imaging") => "Drum",
            _ when d.Contains("fuser") => "Fuser",
            _ when d.Contains("ink") => "Ink",
            _ => "Other"
        };
    }

    public static string? GuessColor(string? desc)
    {
        var d = (desc ?? "").ToLowerInvariant();
        if (d.Contains("cyan")) return "cyan";
        if (d.Contains("magenta")) return "magenta";
        if (d.Contains("yellow")) return "yellow";
        if (d.Contains("black") || d.Contains(" bk") || d.Contains("schwarz")) return "black";
        return null;
    }

    /// <summary>تشخیص برند از شمارهٔ Enterprise در sysObjectID یا متن sysDescr.</summary>
    public static string? DetectBrand(string? sysObjectId, string? sysDescr)
    {
        var map = new Dictionary<int, string>
        {
            [11] = "HP", [1602] = "Canon", [1347] = "Kyocera", [367] = "Ricoh", [2385] = "Sharp", [2435] = "Brother",
            [253] = "Xerox", [18334] = "Konica Minolta", [1248] = "Epson", [641] = "Lexmark", [2001] = "OKI",
            [1129] = "Toshiba", [236] = "Samsung", [10642] = "Pantum", [3369] = "Panasonic", [5722] = "Develop",
            [24807] = "Sindoh", [26696] = "Utax", [3793] = "Zebra",
        };
        if (!string.IsNullOrWhiteSpace(sysObjectId) && sysObjectId.StartsWith("1.3.6.1.4.1."))
        {
            var parts = sysObjectId.Split('.');
            if (parts.Length > 6 && int.TryParse(parts[6], out var ent) && map.TryGetValue(ent, out var b)) return b;
        }
        var d = sysDescr ?? "";
        foreach (var name in new[] { "HP", "Hewlett", "Canon", "Kyocera", "Ricoh", "Sharp", "Brother", "Xerox", "Konica", "Epson", "Lexmark", "OKI", "Toshiba", "Samsung", "Pantum", "Panasonic", "Develop", "Sindoh", "Utax", "Zebra" })
            if (d.Contains(name, StringComparison.OrdinalIgnoreCase))
                return name switch { "Hewlett" => "HP", "Konica" => "Konica Minolta", _ => name };
        return null;
    }

    private static string? PickModel(List<Variable> deviceDescr, List<Variable> printerRows, string? sysDescr)
    {
        // ترجیح: hrDeviceDescr همان ردیفی که در hrPrinterTable است
        var printerIdx = printerRows.Select(v => Suffix(v, HrPrinterEntry)).Where(sf => sf.Length >= 2).Select(sf => sf[1]).FirstOrDefault();
        var byIdx = deviceDescr.FirstOrDefault(v => Suffix(v, HrDeviceDescr).FirstOrDefault() == printerIdx && printerIdx > 0);
        var model = Text(byIdx?.Data) ?? deviceDescr.Select(v => Text(v.Data)).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        if (string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(sysDescr))
            model = sysDescr.Split(new[] { ';', ',' }, 2)[0];
        return Trunc(model?.Trim(), 150);
    }

    private static string? ExtractFirmware(string? sysDescr)
    {
        if (string.IsNullOrWhiteSpace(sysDescr)) return null;
        foreach (var key in new[] { "FW:", "FW ", "Firmware", "firmware", "Version", "version", "ROM " })
        {
            var i = sysDescr.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) continue;
            var tail = sysDescr[(i + key.Length)..].TrimStart(' ', ':', '=');
            var end = tail.IndexOfAny(new[] { ',', ';', ' ', ')' });
            var v = end > 0 ? tail[..end] : tail;
            if (v.Any(char.IsDigit)) return Trunc(v, 100);
        }
        return null;
    }

    // ================================================================== تبدیل نوع‌ها

    public static int[] Suffix(Variable v, string root)
    {
        var id = v.Id.ToNumerical();
        var rootLen = root.Count(c => c == '.') + 1;
        return id.Length <= rootLen ? Array.Empty<int>() : id.Skip(rootLen).Select(x => (int)x).ToArray();
    }

    public static int? Int(ISnmpData? d) => d switch
    {
        Integer32 i => i.ToInt32(),
        Counter32 c => (int)Math.Min(int.MaxValue, c.ToUInt32()),
        Gauge32 g => (int)Math.Min(int.MaxValue, g.ToUInt32()),
        _ => null
    };

    public static long? Long(ISnmpData? d) => d switch
    {
        Integer32 i => i.ToInt32(),
        Counter32 c => c.ToUInt32(),
        Gauge32 g => g.ToUInt32(),
        Counter64 c64 => (long)Math.Min(long.MaxValue, c64.ToUInt64()),
        _ => null
    };

    public static string? Text(ISnmpData? d)
    {
        if (d is null) return null;
        string s;
        if (d is OctetString os)
        {
            var raw = os.GetRaw();
            // برخی دستگاه‌ها رشته را با NUL پایان می‌دهند یا UTF-8 نامعتبر می‌فرستند
            var len = Array.IndexOf(raw, (byte)0);
            if (len >= 0) raw = raw[..len];
            s = Encoding.UTF8.GetString(raw);
            if (s.Contains('\uFFFD')) s = Encoding.Latin1.GetString(raw);
        }
        else s = d.ToString() ?? "";
        s = new string(s.Where(ch => !char.IsControl(ch) || ch == ' ').ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }

    public static string? Trunc(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
