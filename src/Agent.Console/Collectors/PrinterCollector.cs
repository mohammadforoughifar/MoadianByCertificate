using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using InventoryAgent.Models;

namespace InventoryAgent.Collectors;

/// <summary>
/// جمع‌آوری وضعیت چاپگرهای محلی سیستم (USB / LPT / پورت شبکه) برای ماژول «ماشین‌های اداری».
///   • ویندوز: WMI (Win32_Printer, Win32_PrintJob, Win32_TCPIPPrinterPort) + لاگ PrintService برای شمارش صفحات
///   • لینوکس: CUPS (lpstat)
/// هرگز exception بیرون نمی‌دهد؛ در بدترین حالت فهرست خالی برمی‌گرداند.
/// </summary>
public static class PrinterCollector
{
    public static (List<LocalPrinter> Printers, bool PageLogEnabled) Collect(bool enablePrintLog)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsPrinters.Collect(enablePrintLog);
            if (OperatingSystem.IsLinux()) return (CupsPrinters.Collect(), false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[چاپگر] جمع‌آوری ناموفق بود: {ex.Message}");
        }
        return (new List<LocalPrinter>(), false);
    }

    // ------------------------------------------------------------------ مشترک

    internal static readonly string[] VirtualPorts = { "PORTPROMPT:", "FILE:", "NUL:", "SHRFAX:", "XPSPORT:", "ONENOTE", "PDF" };
    internal static readonly string[] VirtualDriverHints =
        { "PDF", "XPS", "ONENOTE", "FAX", "SEND TO", "SNAGIT", "DOCUMENT WRITER", "IMAGE WRITER", "CUTEPDF", "NITRO", "FOXIT" };

    internal static string ClassifyWindows(string name, string port, string driver, bool isNetworkConnection)
    {
        var p = port.Trim().ToUpperInvariant();
        var d = driver.ToUpperInvariant();
        var n = name.ToUpperInvariant();
        if (name.StartsWith(@"\\", StringComparison.Ordinal) || isNetworkConnection) return "Connection";   // چاپگر اشتراکی سیستم دیگر
        if (p.StartsWith("TS", StringComparison.Ordinal) && p.Length <= 6 && p[2..].All(char.IsDigit)) return "Virtual"; // ریدایرکت RDP
        if (VirtualPorts.Any(v => p.StartsWith(v, StringComparison.Ordinal))) return "Virtual";
        if (VirtualDriverHints.Any(h => d.Contains(h) || n.Contains(h))) return "Virtual";
        if (p.StartsWith("USB", StringComparison.Ordinal) || p.StartsWith("DOT4", StringComparison.Ordinal)) return "Usb";
        if (p.StartsWith("LPT", StringComparison.Ordinal) || p.StartsWith("COM", StringComparison.Ordinal)) return "Local";
        if (p.StartsWith("IP_", StringComparison.Ordinal) || p.StartsWith("WSD", StringComparison.Ordinal)
            || p.StartsWith("HTTP", StringComparison.Ordinal) || IPAddress.TryParse(StripPortSuffix(p), out _)) return "Network";
        return "Other";
    }

    internal static string StripPortSuffix(string port)
    {
        // «192.168.1.50_1» یا «IP_192.168.1.50»
        var s = port.Trim();
        if (s.StartsWith("IP_", StringComparison.OrdinalIgnoreCase)) s = s[3..];
        var us = s.IndexOf('_');
        if (us > 0) s = s[..us];
        return s;
    }

    internal static void AddFlag(LocalPrinter p, string flag)
    {
        if (!p.ErrorFlags.Contains(flag)) p.ErrorFlags.Add(flag);
    }

    /// <summary>وضعیت کلی از روی پرچم‌ها و حالت پایه.</summary>
    internal static void FinalizeStatus(LocalPrinter p, string baseState)
    {
        var f = p.ErrorFlags;
        if (f.Contains("Offline") || f.Contains("NotAvailable")) p.Status = "Offline";
        else if (f.Any(x => x is "Error" or "Jammed" or "NoPaper" or "NoToner" or "DoorOpen" or "OutputFull" or "UserIntervention")) p.Status = "Error";
        else if (f.Contains("Paused")) p.Status = "Paused";
        else p.Status = baseState;
    }
}

// ============================================================================ ویندوز

[SupportedOSPlatform("windows")]
internal static class WindowsPrinters
{
    private const string PrintLog = "Microsoft-Windows-PrintService/Operational";

    public static (List<LocalPrinter>, bool) Collect(bool enablePrintLog)
    {
        var list = new List<LocalPrinter>();
        var tcpPorts = TcpPorts();

        Each("SELECT Name, DriverName, PortName, Default, Shared, ShareName, Location, Comment, PrinterStatus, " +
             "ExtendedPrinterStatus, DetectedErrorState, ExtendedDetectedErrorState, WorkOffline, PrinterState, Network, Local " +
             "FROM Win32_Printer", mo =>
        {
            var name = S(mo, "Name");
            if (string.IsNullOrWhiteSpace(name)) return;
            var port = S(mo, "PortName");
            var driver = S(mo, "DriverName");
            var isConnection = B(mo, "Network") && !B(mo, "Local");
            var p = new LocalPrinter
            {
                Name = name, DriverName = driver, PortName = port,
                Kind = PrinterCollector.ClassifyWindows(name, port, driver, isConnection),
                IsDefault = B(mo, "Default"), IsShared = B(mo, "Shared"), ShareName = S(mo, "ShareName"),
                Location = S(mo, "Location"), Comment = S(mo, "Comment"),
            };
            if (p.Kind == "Network")
            {
                if (tcpPorts.TryGetValue(port, out var host) && IPAddress.TryParse(host, out _)) p.PortIp = host;
                else if (IPAddress.TryParse(PrinterCollector.StripPortSuffix(port), out var ip)) p.PortIp = ip.ToString();
            }

            var printerStatus = U(mo, "PrinterStatus");
            var extStatus = U(mo, "ExtendedPrinterStatus");
            var err = U(mo, "DetectedErrorState");
            var extErr = U(mo, "ExtendedDetectedErrorState");
            var state = U(mo, "PrinterState");   // در عمل همان bitmask «PRINTER_STATUS_*» اسپولر است

            // --- bitmask اسپولر
            if ((state & 0x1) != 0) PrinterCollector.AddFlag(p, "Paused");
            if ((state & 0x2) != 0) PrinterCollector.AddFlag(p, "Error");
            if ((state & 0x8) != 0) PrinterCollector.AddFlag(p, "Jammed");
            if ((state & 0x10) != 0) PrinterCollector.AddFlag(p, "NoPaper");
            if ((state & 0x80) != 0) PrinterCollector.AddFlag(p, "Offline");
            if ((state & 0x800) != 0) PrinterCollector.AddFlag(p, "OutputFull");
            if ((state & 0x1000) != 0) PrinterCollector.AddFlag(p, "NotAvailable");
            if ((state & 0x20000) != 0) PrinterCollector.AddFlag(p, "LowToner");
            if ((state & 0x40000) != 0) PrinterCollector.AddFlag(p, "NoToner");
            if ((state & 0x100000) != 0) PrinterCollector.AddFlag(p, "UserIntervention");
            if ((state & 0x400000) != 0) PrinterCollector.AddFlag(p, "DoorOpen");

            // --- DetectedErrorState (CIM)
            switch (err)
            {
                case 3: PrinterCollector.AddFlag(p, "LowPaper"); break;
                case 4: PrinterCollector.AddFlag(p, "NoPaper"); break;
                case 5: PrinterCollector.AddFlag(p, "LowToner"); break;
                case 6: PrinterCollector.AddFlag(p, "NoToner"); break;
                case 7: PrinterCollector.AddFlag(p, "DoorOpen"); break;
                case 8: PrinterCollector.AddFlag(p, "Jammed"); break;
                case 9: PrinterCollector.AddFlag(p, "Offline"); break;
                case 10: PrinterCollector.AddFlag(p, "ServiceRequested"); break;
                case 11: PrinterCollector.AddFlag(p, "OutputFull"); break;
            }
            switch (extErr)
            {
                case 9: PrinterCollector.AddFlag(p, "ServiceRequested"); break;
                case 13: PrinterCollector.AddFlag(p, "UserIntervention"); break;
            }
            if (B(mo, "WorkOffline") || printerStatus == 7 || extStatus == 7) PrinterCollector.AddFlag(p, "Offline");
            if (extStatus == 8) PrinterCollector.AddFlag(p, "Paused");
            if (extStatus == 11) PrinterCollector.AddFlag(p, "NotAvailable");

            var baseState = printerStatus switch
            {
                4 => "Printing", 5 => "WarmingUp", 3 => "Idle",
                _ => (state & 0x400) != 0 ? "Printing" : (state & 0x10000) != 0 ? "WarmingUp" : extStatus == 3 ? "Idle" : "Unknown"
            };
            PrinterCollector.FinalizeStatus(p, baseState);
            list.Add(p);
        });

        CollectJobs(list);

        var logEnabled = false;
        try { logEnabled = CountPages(list, enablePrintLog); }
        catch (Exception ex) { Console.WriteLine($"[چاپگر] شمارش صفحات از لاگ چاپ ناموفق بود: {ex.Message}"); }
        return (list, logEnabled);
    }

    private static Dictionary<string, string> TcpPorts()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Each("SELECT Name, HostAddress FROM Win32_TCPIPPrinterPort", mo =>
        {
            var n = S(mo, "Name");
            if (n.Length > 0) map[n] = S(mo, "HostAddress");
        });
        return map;
    }

    private static void CollectJobs(List<LocalPrinter> printers)
    {
        var now = DateTime.Now;
        Each("SELECT Name, StatusMask, TimeSubmitted FROM Win32_PrintJob", mo =>
        {
            // Name = «نام چاپگر, شمارهٔ کار»
            var raw = S(mo, "Name");
            var comma = raw.LastIndexOf(',');
            if (comma <= 0) return;
            var printerName = raw[..comma].Trim();
            var p = printers.FirstOrDefault(x => string.Equals(x.Name, printerName, StringComparison.OrdinalIgnoreCase));
            if (p == null) return;
            p.JobsCount++;
            var mask = U(mo, "StatusMask");
            if ((mask & (2 | 32 | 64 | 512 | 1024)) != 0) p.JobsInError++;
            var submitted = S(mo, "TimeSubmitted");
            if (submitted.Length > 0)
            {
                try
                {
                    var t = ManagementDateTimeConverter.ToDateTime(submitted);
                    var age = (int)Math.Max(0, (now - t).TotalMinutes);
                    if (p.OldestJobMinutes == null || age > p.OldestJobMinutes) p.OldestJobMinutes = age;
                }
                catch { }
            }
        });
    }

    // ---------------------------------------------------------------- شمارش صفحات (رویداد 307)

    private sealed class PageState
    {
        public long LastRecordId { get; set; }
        public Dictionary<string, long> Pages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// صفحات چاپ‌شده را از رویداد 307 لاگ PrintService/Operational می‌خواند و به شمارندهٔ تجمعی هر چاپگر
    /// (ذخیره در ProgramData) اضافه می‌کند. سرور فقط اختلاف شمارندهٔ تجمعی را حساب می‌کند، پس ارسال تکراری
    /// گزارش باعث شمارش دوباره نمی‌شود.
    /// </summary>
    private static bool CountPages(List<LocalPrinter> printers, bool enableIfDisabled)
    {
        using var cfg = new System.Diagnostics.Eventing.Reader.EventLogConfiguration(PrintLog);
        if (!cfg.IsEnabled)
        {
            if (!enableIfDisabled) return false;
            try
            {
                cfg.IsEnabled = true;
                cfg.SaveChanges();
                Console.WriteLine("[چاپگر] لاگ چاپ ویندوز (PrintService/Operational) برای شمارش صفحات فعال شد.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[چاپگر] فعال‌سازی لاگ چاپ نیاز به دسترسی مدیر دارد: {ex.Message}");
                return false;
            }
        }

        var statePath = StatePath();
        var st = LoadState(statePath);
        var newest = NewestRecordId();

        if (st.LastRecordId == 0 || newest < st.LastRecordId)
        {
            // اولین اجرا (یا لاگ پاک شده): از همین لحظه شمارش می‌کنیم تا سابقهٔ قدیمی، تیراژ امروز را متورم نکند.
            if (st.LastRecordId == 0) st.LastRecordId = newest;
            else st.LastRecordId = 0;
        }

        if (newest > st.LastRecordId)
        {
            var q = new System.Diagnostics.Eventing.Reader.EventLogQuery(PrintLog,
                System.Diagnostics.Eventing.Reader.PathType.LogName,
                $"*[System[(EventID=307) and (EventRecordID > {st.LastRecordId})]]");
            using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(q);
            var max = st.LastRecordId;
            for (var e = reader.ReadEvent(); e != null; e = reader.ReadEvent())
            {
                using (e)
                {
                    if (e.RecordId is long rid && rid > max) max = rid;
                    var (printer, pages) = Parse307(e.ToXml());
                    if (string.IsNullOrWhiteSpace(printer) || pages <= 0) continue;
                    st.Pages[printer] = st.Pages.GetValueOrDefault(printer) + pages;
                }
            }
            st.LastRecordId = Math.Max(max, newest);
        }
        SaveState(statePath, st);

        foreach (var p in printers)
            p.CumulativePages = st.Pages.GetValueOrDefault(p.Name);
        return true;
    }

    private static long NewestRecordId()
    {
        var q = new System.Diagnostics.Eventing.Reader.EventLogQuery(PrintLog, System.Diagnostics.Eventing.Reader.PathType.LogName)
        { ReverseDirection = true };
        using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(q);
        using var e = reader.ReadEvent();
        return e?.RecordId ?? 0;
    }

    /// <summary>رویداد 307: UserData/DocumentPrinted → Param5 = نام چاپگر، Param8 = تعداد صفحات.</summary>
    internal static (string? Printer, long Pages) Parse307(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var dp = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "DocumentPrinted");
            if (dp == null) return (null, 0);
            string? P(string n) => dp.Elements().FirstOrDefault(x => x.Name.LocalName == n)?.Value;
            var pages = long.TryParse(P("Param8"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
            return (P("Param5")?.Trim(), Math.Clamp(pages, 0, 100000));
        }
        catch { return (null, 0); }
    }

    private static string StatePath()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "InventoryAgent");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "printers.state.json");
        }
        catch { return Path.Combine(AppContext.BaseDirectory, "printers.state.json"); }
    }

    private static PageState LoadState(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var st = JsonSerializer.Deserialize<PageState>(File.ReadAllText(path));
                if (st != null)
                {
                    st.Pages = new Dictionary<string, long>(st.Pages ?? new(), StringComparer.OrdinalIgnoreCase);
                    return st;
                }
            }
        }
        catch { }
        return new PageState();
    }

    private static void SaveState(string path, PageState st)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(st));
            File.Move(tmp, path, overwrite: true);
        }
        catch { }
    }

    // ---------------------------------------------------------------- WMI helpers

    private static void Each(string wql, Action<ManagementBaseObject> action)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            using var results = searcher.Get();
            foreach (var mo in results)
            {
                using (mo)
                {
                    try { action(mo); } catch { }
                }
            }
        }
        catch { }
    }

    private static string S(ManagementBaseObject mo, string prop)
    {
        try { return (mo[prop]?.ToString() ?? "").Trim(); } catch { return ""; }
    }

    private static bool B(ManagementBaseObject mo, string prop)
    {
        try { return mo[prop] is bool b && b; } catch { return false; }
    }

    private static uint U(ManagementBaseObject mo, string prop)
    {
        try { return mo[prop] == null ? 0u : Convert.ToUInt32(mo[prop], CultureInfo.InvariantCulture); } catch { return 0; }
    }
}

// ============================================================================ لینوکس (CUPS)

internal static class CupsPrinters
{
    public static List<LocalPrinter> Collect()
    {
        var list = new List<LocalPrinter>();
        var detail = Run("lpstat", "-l -p");
        if (string.IsNullOrWhiteSpace(detail)) return list;   // CUPS نصب نیست

        LocalPrinter? cur = null;
        foreach (var raw in detail.Split('\n'))
        {
            var line = raw.TrimEnd();
            var m = Regex.Match(line, @"^printer (\S+) (.*)$");
            if (m.Success)
            {
                cur = new LocalPrinter { Name = m.Groups[1].Value };
                var rest = m.Groups[2].Value;
                var baseState = rest.Contains("now printing") ? "Printing" : rest.Contains("is idle") ? "Idle" : "Unknown";
                if (rest.Contains("disabled")) PrinterCollector.AddFlag(cur, "Paused");
                cur.Status = baseState;
                list.Add(cur);
                continue;
            }
            if (cur == null) continue;
            var t = line.Trim();
            if (t.StartsWith("Alerts:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var a in t[7..].Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var flag = a switch
                    {
                        var x when x.StartsWith("media-empty") => "NoPaper",
                        var x when x.StartsWith("media-low") => "LowPaper",
                        var x when x.StartsWith("media-jam") => "Jammed",
                        var x when x.StartsWith("toner-empty") || x.StartsWith("marker-supply-empty") => "NoToner",
                        var x when x.StartsWith("toner-low") || x.StartsWith("marker-supply-low") => "LowToner",
                        var x when x.StartsWith("door-open") || x.StartsWith("cover-open") => "DoorOpen",
                        var x when x.StartsWith("offline") || x.StartsWith("connecting-to-device") => "Offline",
                        var x when x.StartsWith("output-area-full") => "OutputFull",
                        var x when x.EndsWith("-error") => "Error",
                        _ => null
                    };
                    if (flag != null) PrinterCollector.AddFlag(cur, flag);
                }
            }
            else if (t.StartsWith("Location:", StringComparison.OrdinalIgnoreCase)) cur.Location = t[9..].Trim();
            else if (t.StartsWith("Description:", StringComparison.OrdinalIgnoreCase)) cur.DriverName = t[12..].Trim();
        }

        // پورت/URI دستگاه
        foreach (var line in (Run("lpstat", "-v") ?? "").Split('\n'))
        {
            var m = Regex.Match(line.Trim(), @"^device for (\S+):\s*(\S+)");
            if (!m.Success) continue;
            var p = list.FirstOrDefault(x => x.Name == m.Groups[1].Value);
            if (p == null) continue;
            var uri = m.Groups[2].Value;
            p.PortName = uri;
            p.Kind = uri.StartsWith("usb:") || uri.StartsWith("hp:/usb") ? "Usb"
                   : uri.StartsWith("parallel:") || uri.StartsWith("serial:") ? "Local"
                   : uri.StartsWith("cups-pdf") || uri.StartsWith("file:") ? "Virtual"
                   : uri.StartsWith("ipp") || uri.StartsWith("socket:") || uri.StartsWith("lpd:") || uri.StartsWith("http") || uri.StartsWith("dnssd") || uri.StartsWith("hp:/net") ? "Network"
                   : "Other";
            if (p.Kind == "Network" && Uri.TryCreate(uri, UriKind.Absolute, out var u) && IPAddress.TryParse(u.Host, out var ip))
                p.PortIp = ip.ToString();
        }

        var def = Regex.Match(Run("lpstat", "-d") ?? "", @"destination:\s*(\S+)");
        if (def.Success) foreach (var p in list) p.IsDefault = p.Name == def.Groups[1].Value;

        foreach (var line in (Run("lpstat", "-o") ?? "").Split('\n'))
        {
            var id = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (id == null) continue;
            var dash = id.LastIndexOf('-');
            if (dash <= 0) continue;
            var p = list.FirstOrDefault(x => x.Name == id[..dash]);
            if (p != null) p.JobsCount++;
        }

        foreach (var p in list) PrinterCollector.FinalizeStatus(p, p.Status);
        return list;
    }

    private static string? Run(string file, string args, int timeoutMs = 8000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            };
            psi.Environment["LC_ALL"] = "C";
            psi.Environment["LANG"] = "C";
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(timeoutMs)) { try { proc.Kill(); } catch { } return null; }
            return output;
        }
        catch { return null; }
    }
}
