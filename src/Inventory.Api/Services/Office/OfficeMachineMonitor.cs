using System.Collections.Concurrent;
using System.Net;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services.ItAssets;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office;

/// <summary>نتیجهٔ یک دور پایش دستگاه.</summary>
public sealed class OfficeMachinePollResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string State { get; set; } = "Unknown";
    public PrinterSnapshot? Snapshot { get; set; }
    public int NewEvents { get; set; }
    public int ResolvedEvents { get; set; }
}

/// <summary>
/// پایش SNMP ماشین‌های اداری: خواندن وضعیت، به‌روزرسانی مواد مصرفی/سینی‌ها/شمارنده،
/// مدیریت رویدادها (باز/بسته) و ارسال اعلان. فقط عملیات خواندن روی دستگاه انجام می‌شود.
/// </summary>
public sealed partial class OfficeMachineMonitor
{
    /// <summary>دامنهٔ رمزنگاری اسرار SNMP (AAD جدا از Credentialهای شرکت‌ها).</summary>
    public const int SecretScope = -9100;
    private const string FromName = "پایش ماشین‌های اداری";
    private const string FormName = "ماشین‌های اداری";

    // جلوگیری از پایش هم‌زمان یک دستگاه (Worker + دکمهٔ «پایش فوری»)
    private static readonly ConcurrentDictionary<int, byte> InFlight = new();

    private readonly AppDbContext _db;
    private readonly CredentialVaultCrypto _crypto;
    private readonly INotifyService _notify;
    private readonly IConfiguration _config;
    private readonly ILogger<OfficeMachineMonitor> _log;

    public OfficeMachineMonitor(AppDbContext db, CredentialVaultCrypto crypto, INotifyService notify,
        IConfiguration config, ILogger<OfficeMachineMonitor> log)
    {
        _db = db; _crypto = crypto; _notify = notify; _config = config; _log = log;
    }

    public int TimeoutMs => Math.Clamp(_config.GetValue<int?>("OfficeMachineMonitoring:SnmpTimeoutMs") ?? 2000, 300, 15000);
    public int Retries => Math.Clamp(_config.GetValue<int?>("OfficeMachineMonitoring:SnmpRetries") ?? 1, 0, 3);
    private int OfflineGraceMinutes => Math.Clamp(_config.GetValue<int?>("OfficeMachineMonitoring:OfflineGraceMinutes") ?? 15, 0, 1440);

    // ================================================================== اسرار

    public string? Encrypt(string? plain) => string.IsNullOrEmpty(plain) ? null : _crypto.Encrypt(plain, SecretScope);

    public string? Decrypt(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _crypto.Decrypt(cipher, SecretScope); }
        catch (Exception ex)
        {
            _log.LogWarning("رمزگشایی اسرار SNMP ناموفق بود (احتمالاً کلید گاوصندوق عوض شده): {Message}", ex.Message);
            return null;
        }
    }

    public SnmpTarget BuildTarget(OfficeMachine m, int? timeoutMs = null, int? retries = null) => new()
    {
        Host = m.IpAddress ?? "",
        Port = m.SnmpPort,
        Version = m.SnmpVersion,
        Community = Decrypt(m.SnmpCommunityCipher) ?? "public",
        V3User = m.SnmpV3User,
        V3AuthProtocol = m.SnmpV3AuthProtocol,
        V3AuthPassword = Decrypt(m.SnmpV3AuthCipher),
        V3PrivProtocol = m.SnmpV3PrivProtocol,
        V3PrivPassword = Decrypt(m.SnmpV3PrivCipher),
        TimeoutMs = timeoutMs ?? TimeoutMs,
        Retries = retries ?? Retries,
    };

    /// <summary>خواندن کامل بدون ذخیره (برای «تست اتصال» در فرم).</summary>
    public static async Task<PrinterSnapshot> ReadAsync(SnmpTarget target, bool full, CancellationToken ct = default)
    {
        var (snap, error) = await TryReadAsync(target, full, ct);
        return snap ?? throw error!;
    }

    /// <summary>
    /// مثل ReadAsync ولی خطاهای مورد انتظار (بی‌پاسخ، پورت بسته، اعتبارنامهٔ غلط) را برمی‌گرداند نه پرتاب.
    /// خطا داخل همان lambdaی Task.Run گرفته می‌شود تا از مرز Task بیرون نرود؛ وگرنه دیباگر Visual Studio
    /// (Just My Code) برای هر IP بی‌پاسخ در «کشف خودکار» روی آن توقف می‌کند (user-unhandled) و پرتاب صدها
    /// استثنا هزینه هم دارد.
    /// </summary>
    public static Task<(PrinterSnapshot? Snapshot, SnmpQueryException? Error)> TryReadAsync(SnmpTarget target, bool full, CancellationToken ct = default)
        => Task.Run<(PrinterSnapshot?, SnmpQueryException?)>(() =>
        {
            try
            {
                var session = new SnmpSession(target);
                return (full ? PrinterSnmpReader.ReadFull(session) : PrinterSnmpReader.ReadBasic(session), null);
            }
            catch (SnmpQueryException ex) { return (null, ex); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (null, new SnmpQueryException("خطای غیرمنتظره در ارتباط SNMP: " + ex.Message, false, ex));
            }
        }, ct);

    // ================================================================== پایش و ذخیره

    public async Task<OfficeMachinePollResult> PollAsync(int machineId, CancellationToken ct = default)
    {
        if (!InFlight.TryAdd(machineId, 0))
            return new OfficeMachinePollResult { Error = "پایش این دستگاه هم‌اکنون در جریان است." };
        try
        {
            var m = await _db.OfficeMachines.FirstOrDefaultAsync(x => x.Id == machineId, ct);
            if (m == null) return new OfficeMachinePollResult { Error = "دستگاه یافت نشد." };
            if (m.ConnectionType != "Network" || string.IsNullOrWhiteSpace(m.IpAddress))
                return new OfficeMachinePollResult { Error = "پایش SNMP فقط برای دستگاه‌های شبکه‌ای دارای IP ممکن است." };

            var now = DateTime.Now;
            var result = new OfficeMachinePollResult();
            PrinterSnapshot? snap = null;
            try
            {
                var (read, readError) = await TryReadAsync(BuildTarget(m), full: true, ct);
                snap = read;
                if (readError != null) result.Error = readError.Message;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Error = "خطای غیرمنتظره در پایش: " + ex.Message;
                _log.LogWarning(ex, "پایش SNMP ماشین {Id} ({Ip}) ناموفق بود", m.Id, m.IpAddress);
            }

            m.LastPollAt = now;
            var conditions = new Dictionary<string, (string Severity, string Message)>();
            List<OfficeMachineSupply> supplies = new();

            if (snap == null)
            {
                result.Success = false;
                m.OnlineState = "Offline";
                m.LastPollError = PrinterSnmpReader.Trunc(result.Error, 500);
                var lastSeen = m.LastSeenOnlineAt;
                if (lastSeen == null || now - lastSeen.Value >= TimeSpan.FromMinutes(OfflineGraceMinutes))
                    conditions["Offline"] = ("Critical", lastSeen == null
                        ? "دستگاه به SNMP پاسخ نمی‌دهد."
                        : $"دستگاه از {Math.Max(1, (int)(now - lastSeen.Value).TotalMinutes)} دقیقه پیش پاسخ نمی‌دهد.");
            }
            else
            {
                result.Success = true;
                result.Snapshot = snap;
                ApplyIdentity(m, snap);
                m.LastSeenOnlineAt = now;
                m.LastPollError = null;
                m.PrinterState = snap.PrinterState;
                m.UptimeSeconds = snap.UptimeSeconds;
                m.LastStatusText = PrinterSnmpReader.Trunc(snap.DisplayText ?? snap.Alerts.FirstOrDefault()?.Description, 300);
                m.LastErrorCodes = snap.ErrorFlags.Count == 0 ? null : PrinterSnmpReader.Trunc(string.Join(",", snap.ErrorFlags), 300);
                if (snap.PageCount is > 0) m.LastPageCount = snap.PageCount;

                supplies = await ReplaceSuppliesAsync(m.Id, snap, now, ct);
                await ReplaceTraysAsync(m.Id, snap, now, ct);
                if (snap.PageCount is > 0) await UpsertCounterAsync(m.Id, snap.PageCount.Value, now, ct);

                var tracked = supplies.Where(s => s.Kind is "Toner" or "Ink" or "Drum" && s.Percent != null).ToList();
                m.LowestSupplyPercent = tracked.Count == 0 ? null : tracked.Min(s => s.Percent);

                BuildConditions(m, snap, supplies, conditions);
                m.OnlineState = conditions.Values.Any(c => c.Severity == "Critical") ? "Error"
                              : conditions.Count > 0 ? "Warning" : "Online";
            }

            result.State = m.OnlineState;
            var (opened, resolved) = await SyncEventsAsync(m, conditions, pollSucceeded: snap != null, now, ct);
            result.NewEvents = opened.Count;
            result.ResolvedEvents = resolved;
            await _db.SaveChangesAsync(ct);

            if (opened.Count > 0 && m.AlertsEnabled && m.Status != "Retired")
                await NotifyAsync(m, opened, ct);
            return result;
        }
        finally
        {
            InFlight.TryRemove(machineId, out _);
        }
    }

    private static void ApplyIdentity(OfficeMachine m, PrinterSnapshot s)
    {
        // فقط فیلدهای خالی را پر می‌کنیم تا ورودی دستی کاربر بازنویسی نشود (جز مقادیر فنی).
        if (string.IsNullOrWhiteSpace(m.SerialNumber) && !string.IsNullOrWhiteSpace(s.SerialNumber)) m.SerialNumber = PrinterSnmpReader.Trunc(s.SerialNumber, 150);
        if (string.IsNullOrWhiteSpace(m.Brand) && !string.IsNullOrWhiteSpace(s.Brand)) m.Brand = s.Brand;
        if (string.IsNullOrWhiteSpace(m.Model) && !string.IsNullOrWhiteSpace(s.Model)) m.Model = s.Model!;
        if (!string.IsNullOrWhiteSpace(s.MacAddress)) m.MacAddress = s.MacAddress;
        if (!string.IsNullOrWhiteSpace(s.SysName)) m.Hostname = PrinterSnmpReader.Trunc(s.SysName, 100);
        if (!string.IsNullOrWhiteSpace(s.FirmwareVersion)) m.FirmwareVersion = s.FirmwareVersion;
        if (!string.IsNullOrWhiteSpace(s.SysDescr)) m.SysDescr = PrinterSnmpReader.Trunc(s.SysDescr, 300);
        if (s.IsColor == true) m.IsColor = true;
    }

    private async Task<List<OfficeMachineSupply>> ReplaceSuppliesAsync(int machineId, PrinterSnapshot s, DateTime now, CancellationToken ct)
    {
        var old = await _db.OfficeMachineSupplies.Where(x => x.MachineId == machineId).ToListAsync(ct);
        _db.OfficeMachineSupplies.RemoveRange(old);
        var rows = s.Supplies.Select(x => new OfficeMachineSupply
        {
            MachineId = machineId, SupplyIndex = x.Index, Kind = x.Kind, Color = PrinterSnmpReader.Trunc(x.Color, 30),
            Description = PrinterSnmpReader.Trunc(x.Description, 200), Level = x.Level, MaxCapacity = x.MaxCapacity,
            Percent = x.Percent, UpdatedAt = now
        }).ToList();
        _db.OfficeMachineSupplies.AddRange(rows);
        return rows;
    }

    private async Task ReplaceTraysAsync(int machineId, PrinterSnapshot s, DateTime now, CancellationToken ct)
    {
        var old = await _db.OfficeMachineTrays.Where(x => x.MachineId == machineId).ToListAsync(ct);
        _db.OfficeMachineTrays.RemoveRange(old);
        _db.OfficeMachineTrays.AddRange(s.Trays.Select(x => new OfficeMachineTray
        {
            MachineId = machineId, TrayIndex = x.Index, Name = PrinterSnmpReader.Trunc(x.Name, 100),
            MediaName = PrinterSnmpReader.Trunc(x.MediaName, 100), CurrentLevel = x.CurrentLevel,
            MaxCapacity = x.MaxCapacity, Percent = x.Percent, UpdatedAt = now
        }));
    }

    private async Task UpsertCounterAsync(int machineId, long pages, DateTime now, CancellationToken ct)
    {
        var day = now.Date;
        var row = await _db.OfficeMachineCounters.FirstOrDefaultAsync(c => c.MachineId == machineId && c.Day == day, ct);
        if (row == null) _db.OfficeMachineCounters.Add(new OfficeMachineCounter { MachineId = machineId, Day = day, TotalPages = pages, TakenAt = now });
        else { row.TotalPages = Math.Max(row.TotalPages, pages); row.TakenAt = now; }
    }

    // ================================================================== قواعد هشدار

    private static readonly Dictionary<string, (string Severity, string Message)> FlagRules = new()
    {
        ["Jammed"] = ("Critical", "کاغذ در دستگاه گیر کرده است (Paper Jam)."),
        ["NoPaper"] = ("Critical", "کاغذ دستگاه تمام شده است."),
        ["LowPaper"] = ("Warning", "کاغذ دستگاه رو به اتمام است."),
        ["NoToner"] = ("Critical", "تونر/جوهر تمام شده است."),
        ["LowToner"] = ("Warning", "تونر/جوهر رو به اتمام است."),
        ["DoorOpen"] = ("Critical", "درِ دستگاه باز است."),
        ["Offline"] = ("Critical", "دستگاه در حالت Offline قرار دارد."),
        ["ServiceRequested"] = ("Warning", "دستگاه درخواست سرویس دارد."),
        ["InputTrayMissing"] = ("Warning", "سینی ورودی جا نیفتاده یا وجود ندارد."),
        ["OutputTrayMissing"] = ("Warning", "سینی خروجی وجود ندارد."),
        ["MarkerSupplyMissing"] = ("Critical", "کارتریج/تونر نصب نیست."),
        ["OutputNearFull"] = ("Warning", "سینی خروجی تقریباً پر است."),
        ["OutputFull"] = ("Critical", "سینی خروجی پر است."),
        ["InputTrayEmpty"] = ("Warning", "یکی از سینی‌های کاغذ خالی است."),
        ["OverduePreventMaint"] = ("Warning", "موعد سرویس دوره‌ای دستگاه گذشته است."),
    };

    public static string FlagTitle(string code) => code switch
    {
        "Jammed" => "گیر کاغذ", "NoPaper" => "کاغذ تمام", "LowPaper" => "کاغذ کم", "NoToner" => "تونر تمام",
        "LowToner" => "تونر کم", "DoorOpen" => "در باز", "Offline" => "آفلاین", "ServiceRequested" => "نیاز به سرویس",
        "InputTrayMissing" => "سینی ورودی نیست", "OutputTrayMissing" => "سینی خروجی نیست", "MarkerSupplyMissing" => "کارتریج نصب نیست",
        "OutputNearFull" => "خروجی تقریباً پر", "OutputFull" => "خروجی پر", "InputTrayEmpty" => "سینی خالی",
        "OverduePreventMaint" => "سرویس دوره‌ای",
        "Paused" => "متوقف (Pause)", "Error" => "خطا", "UserIntervention" => "نیاز به اقدام کاربر", "NotAvailable" => "در دسترس نیست",
        "JobError" => "کار چاپی خطادار", "QueueStuck" => "صف گیرکرده", "PrinterRemoved" => "صف چاپ حذف شده", _ => code
    };

    public static string ColorTitle(string? color) => (color ?? "").ToLowerInvariant() switch
    {
        "black" => "مشکی", "cyan" => "فیروزه‌ای", "magenta" => "ارغوانی", "yellow" => "زرد", "" => "", var c => c
    };

    public static string KindTitle(string kind) => kind switch
    {
        "Toner" => "تونر", "Ink" => "جوهر", "Drum" => "درام", "Developer" => "دولوپر", "Fuser" => "فیوزر",
        "Transfer" => "واحد انتقال", "Waste" => "مخزن ضایعات", "Staples" => "منگنه", _ => "مادهٔ مصرفی"
    };

    private static void BuildConditions(OfficeMachine m, PrinterSnapshot s, List<OfficeMachineSupply> supplies,
        Dictionary<string, (string Severity, string Message)> c)
    {
        var threshold = Math.Clamp(m.SupplyLowThreshold, 1, 90);
        var hasSupplyLevels = supplies.Any(x => x.Kind is "Toner" or "Ink" && x.Percent != null);

        foreach (var flag in s.ErrorFlags)
        {
            // اگر سطح دقیق تونر را داریم، هشدار «تونر کم» را به تفکیک کارتریج (پایین) می‌دهیم.
            if (flag == "LowToner" && hasSupplyLevels) continue;
            if (FlagRules.TryGetValue(flag, out var rule)) c[flag] = rule;
        }
        if (s.DeviceStatus == "down" && c.Count == 0)
            c["DeviceDown"] = ("Critical", "وضعیت دستگاه «Down» گزارش شده است.");

        foreach (var sup in supplies.Where(x => x.Kind is "Toner" or "Ink" or "Drum" && x.Percent != null))
        {
            if (sup.Percent > threshold) continue;
            var title = $"{KindTitle(sup.Kind)} {ColorTitle(sup.Color)}".Trim();
            c[$"LowSupply:{sup.SupplyIndex}"] = sup.Percent <= 0
                ? ("Critical", $"{title} تمام شده است ({sup.Description}).")
                : ("Warning", $"{title} به {sup.Percent}٪ رسیده است ({sup.Description}).");
        }
    }

    private async Task<(List<OfficeMachineEvent> Opened, int Resolved)> SyncEventsAsync(OfficeMachine m,
        Dictionary<string, (string Severity, string Message)> conditions, bool pollSucceeded, DateTime now, CancellationToken ct)
    {
        var open = await _db.OfficeMachineEvents.Where(e => e.MachineId == m.Id && !e.IsResolved && e.Source == "Poll").ToListAsync(ct);
        var opened = new List<OfficeMachineEvent>();
        var resolved = 0;

        foreach (var (code, (sev, msg)) in conditions)
        {
            var ev = open.FirstOrDefault(e => e.Code == code);
            if (ev != null) { ev.LastSeenAt = now; ev.Message = PrinterSnmpReader.Trunc(msg, 500)!; ev.Severity = sev; continue; }
            ev = new OfficeMachineEvent
            {
                MachineId = m.Id, Code = PrinterSnmpReader.Trunc(code, 40)!, Severity = sev,
                Message = PrinterSnmpReader.Trunc(msg, 500)!, Source = "Poll", OpenedAt = now, LastSeenAt = now
            };
            _db.OfficeMachineEvents.Add(ev);
            opened.Add(ev);
        }

        foreach (var ev in open.Where(e => !conditions.ContainsKey(e.Code)))
        {
            // اگر پایش ناموفق بود فقط رویداد Offline معتبر است؛ بقیه دست نمی‌خورند (وضعیت نامعلوم است).
            if (!pollSucceeded) continue;
            ev.IsResolved = true; ev.ResolvedAt = now; ev.ResolvedByName = "خودکار (پایش)";
            resolved++;
        }
        return (opened, resolved);
    }

    private async Task NotifyAsync(OfficeMachine m, List<OfficeMachineEvent> opened, CancellationToken ct)
    {
        try
        {
            var important = opened.Where(e => e.Severity is "Critical" or "Warning").ToList();
            if (important.Count == 0) return;
            var critical = important.Any(e => e.Severity == "Critical");
            var title = $"{(critical ? "🔴" : "🟠")} {m.Model} — {m.Location ?? m.IpAddress ?? m.LinkedSystemLabel}";
            var body = string.Join("\n", important.Select(e => "• " + e.Message));
            var link = $"/office-machines?id={m.Id}";

            if (m.ResponsibleUserId is > 0)
                await _notify.SendAsync(m.ResponsibleUserId.Value, title, body, FromName, FormName, link);
            else
                await _notify.SendToRoleAsync("Admin", title, body, FromName, FormName, link);

            foreach (var e in important) e.Notified = true;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ارسال اعلان هشدار ماشین اداری {Id} ناموفق بود", m.Id);
        }
    }

    // ================================================================== کشف شبکه

    public sealed class DiscoveryCandidate
    {
        public string Ip { get; set; } = "";
        public bool IsPrinter { get; set; }
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? SysName { get; set; }
        public string? SysDescr { get; set; }
        public string? SysLocation { get; set; }
        public int? ExistingMachineId { get; set; }
    }

    /// <summary>بازهٔ IP را از CIDR (192.168.1.0/24) یا بازه (192.168.1.10-192.168.1.80 یا 192.168.1.10-80) استخراج می‌کند.</summary>
    public static List<IPAddress> ParseRange(string input, int max = 1024)
    {
        input = (input ?? "").Trim();
        var list = new List<IPAddress>();
        if (input.Contains('/'))
        {
            var parts = input.Split('/');
            if (!IPAddress.TryParse(parts[0], out var baseIp) || !int.TryParse(parts[1], out var prefix) || prefix < 16 || prefix > 32)
                throw new ArgumentException("CIDR نامعتبر است (پیشوند ۱۶ تا ۳۲ مجاز است).");
            var b = ToUInt(baseIp);
            var mask = prefix == 32 ? 0xFFFFFFFFu : ~(0xFFFFFFFFu >> prefix);
            var start = b & mask; var end = start | ~mask;
            if (prefix < 31) { start++; end--; }
            for (var x = start; x <= end && list.Count < max; x++) list.Add(FromUInt(x));
            if (end - start + 1 > max) throw new ArgumentException($"بازه بزرگ‌تر از {max} آدرس است.");
            return list;
        }
        if (input.Contains('-'))
        {
            var parts = input.Split('-', 2);
            if (!IPAddress.TryParse(parts[0].Trim(), out var a)) throw new ArgumentException("آدرس شروع نامعتبر است.");
            var endText = parts[1].Trim();
            if (!endText.Contains('.'))
            {
                var octets = a.ToString().Split('.');
                endText = $"{octets[0]}.{octets[1]}.{octets[2]}.{endText}";
            }
            if (!IPAddress.TryParse(endText, out var z)) throw new ArgumentException("آدرس پایان نامعتبر است.");
            uint s = ToUInt(a), e = ToUInt(z);
            if (e < s) (s, e) = (e, s);
            if (e - s + 1 > max) throw new ArgumentException($"بازه بزرگ‌تر از {max} آدرس است.");
            for (var x = s; x <= e; x++) list.Add(FromUInt(x));
            return list;
        }
        if (IPAddress.TryParse(input, out var single)) { list.Add(single); return list; }
        throw new ArgumentException("بازه را به شکل 192.168.1.0/24 یا 192.168.1.10-192.168.1.80 وارد کنید.");

        static uint ToUInt(IPAddress ip) { var b = ip.GetAddressBytes(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
        static IPAddress FromUInt(uint v) => new(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
    }

    public async Task<List<DiscoveryCandidate>> DiscoverAsync(List<IPAddress> ips, SnmpTarget template, bool printersOnly, CancellationToken ct)
    {
        var found = new ConcurrentBag<DiscoveryCandidate>();
        using var gate = new SemaphoreSlim(Math.Clamp(_config.GetValue<int?>("OfficeMachineMonitoring:DiscoveryParallel") ?? 32, 4, 128));
        var tasks = ips.Select(async ip =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var target = new SnmpTarget
                {
                    Host = ip.ToString(), Port = template.Port, Version = template.Version, Community = template.Community,
                    V3User = template.V3User, V3AuthProtocol = template.V3AuthProtocol, V3AuthPassword = template.V3AuthPassword,
                    V3PrivProtocol = template.V3PrivProtocol, V3PrivPassword = template.V3PrivPassword,
                    TimeoutMs = Math.Min(template.TimeoutMs, 1200), Retries = 0
                };
                var (snap, _) = await TryReadAsync(target, full: false, ct);
                if (snap == null) return;                    // بی‌پاسخ / پورت بسته ← دستگاه SNMP نیست
                if (printersOnly && !snap.IsPrinter) return;
                found.Add(new DiscoveryCandidate
                {
                    Ip = ip.ToString(), IsPrinter = snap.IsPrinter, Brand = snap.Brand, Model = snap.Model,
                    SerialNumber = snap.SerialNumber, SysName = snap.SysName, SysDescr = PrinterSnmpReader.Trunc(snap.SysDescr, 300),
                    SysLocation = snap.SysLocation
                });
            }
            catch (OperationCanceledException) { throw; }
            catch { /* بدون پاسخ ← دستگاه SNMP نیست */ }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);

        var list = found.OrderBy(c => Version.TryParse(c.Ip, out var v) ? v : new Version()).ToList();
        var ipsFound = list.Select(c => c.Ip).ToList();
        var serials = list.Where(c => !string.IsNullOrWhiteSpace(c.SerialNumber)).Select(c => c.SerialNumber!).ToList();
        var existing = await _db.OfficeMachines.AsNoTracking()
            .Where(m => (m.IpAddress != null && ipsFound.Contains(m.IpAddress)) || (m.SerialNumber != null && serials.Contains(m.SerialNumber)))
            .Select(m => new { m.Id, m.IpAddress, m.SerialNumber }).ToListAsync(ct);
        foreach (var c in list)
            c.ExistingMachineId = existing.FirstOrDefault(e => e.IpAddress == c.Ip
                || (!string.IsNullOrWhiteSpace(c.SerialNumber) && e.SerialNumber == c.SerialNumber))?.Id;
        return list;
    }
}
