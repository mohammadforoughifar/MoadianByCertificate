using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office;

// ============================================================================
// پایش چاپگرهای USB/محلی از طریق ایجنت سیستم‌ها (Agent.Console → POST api/OfficeMachines/agent/printers)
//
// ایجنت هر چند دقیقه فهرست صف‌های چاپ سیستم را با وضعیت، پرچم‌های خطا، صف کارها و شمارندهٔ تجمعی
// صفحات (از لاگ چاپ ویندوز) می‌فرستد. سرور:
//   ۱) جدول OfficeAgentPrinters را به‌روز می‌کند (کشف خودکار چاپگرهای هر سیستم)
//   ۲) اگر دستگاه کابلیِ متناظر لینک شده باشد، وضعیت/هشدار/تیراژ آن را مثل پایش SNMP ثبت می‌کند
//   ۳) اگر روی سیستم فقط یک دستگاه کابلی و یک چاپگر USB باشد، خودکار لینک می‌کند
// ============================================================================

public sealed class AgentPrinterReportDto
{
    public string AgentId { get; set; } = "";
    public string? MachineName { get; set; }
    public string? AgentVersion { get; set; }
    public string? OsName { get; set; }
    public bool PageLogEnabled { get; set; }
    public List<AgentLocalPrinterDto> Printers { get; set; } = new();
}

public sealed class AgentLocalPrinterDto
{
    public string Name { get; set; } = "";
    public string? DriverName { get; set; }
    public string? PortName { get; set; }
    public string? Kind { get; set; }
    public string? PortIp { get; set; }
    public bool IsDefault { get; set; }
    public bool IsShared { get; set; }
    public string? Location { get; set; }
    public string? Status { get; set; }
    public List<string>? ErrorFlags { get; set; }
    public int JobsCount { get; set; }
    public int JobsInError { get; set; }
    public int? OldestJobMinutes { get; set; }
    public long? CumulativePages { get; set; }
}

public sealed class AgentIngestResult
{
    public int Printers { get; set; }
    public int LinkedMachines { get; set; }
    public int AutoLinked { get; set; }
    public string? SystemLabel { get; set; }
}

public sealed partial class OfficeMachineMonitor
{
    private static readonly string[] AgentKinds = { "Usb", "Local", "Network", "Virtual", "Connection", "Other" };
    private static readonly string[] AgentStatuses = { "Idle", "Printing", "WarmingUp", "Offline", "Paused", "Error", "Unknown" };
    public static bool IsPhysicalLocal(string kind) => kind is "Usb" or "Local";

    /// <summary>بعد از این مدت بی‌خبری از ایجنت، وضعیت دستگاه «نامعلوم» می‌شود (سیستم خاموش است یا ایجنت اجرا نمی‌شود).</summary>
    public int AgentStaleMinutes => Math.Clamp(_config.GetValue<int?>("OfficeMachineMonitoring:AgentStaleMinutes") ?? 30, 5, 1440);
    private int QueueStuckMinutes => Math.Clamp(_config.GetValue<int?>("OfficeMachineMonitoring:QueueStuckMinutes") ?? 20, 5, 1440);

    private static readonly Dictionary<string, (string Severity, string Message)> AgentFlagRules = new()
    {
        ["Paused"] = ("Warning", "چاپگر در ویندوز در حالت توقف (Pause) است و چیزی چاپ نمی‌کند."),
        ["Error"] = ("Critical", "چاپگر وضعیت خطا گزارش می‌کند."),
        ["UserIntervention"] = ("Warning", "چاپگر منتظر اقدام کاربر است (پیغام روی دستگاه را بررسی کنید)."),
        ["OutputFull"] = ("Critical", "سینی خروجی پر است."),
    };

    // ------------------------------------------------------------------ دریافت گزارش ایجنت

    public async Task<AgentIngestResult> IngestAgentReportAsync(AgentPrinterReportDto report, CancellationToken ct = default)
    {
        var agentId = (report.AgentId ?? "").Trim();
        if (agentId.Length == 0) throw new ArgumentException("AgentId الزامی است.");
        agentId = PrinterSnmpReader.Trunc(agentId, 100)!;
        var now = DateTime.Now;

        var system = await _db.SystemInfos.AsNoTracking().Where(s => s.AgentId == agentId)
            .Select(s => new { s.Id, s.AgentId }).FirstOrDefaultAsync(ct);
        var rows = await _db.OfficeAgentPrinters.Where(p => p.AgentId == agentId).ToListAsync(ct);
        var deltas = new Dictionary<OfficeAgentPrinter, long>();

        var reported = (report.Printers ?? new())
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Take(100).ToList();

        foreach (var p in reported)
        {
            var name = PrinterSnmpReader.Trunc(p.Name.Trim(), 250)!;
            var row = rows.FirstOrDefault(r => string.Equals(r.PrinterName, name, StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                row = new OfficeAgentPrinter { AgentId = agentId, PrinterName = name, FirstSeenAt = now };
                _db.OfficeAgentPrinters.Add(row);
                rows.Add(row);
            }
            row.SystemInfoId = system?.Id ?? row.SystemInfoId;
            row.HostName = PrinterSnmpReader.Trunc(report.MachineName?.Trim(), 100);
            row.DriverName = PrinterSnmpReader.Trunc(p.DriverName?.Trim(), 250);
            row.PortName = PrinterSnmpReader.Trunc(p.PortName?.Trim(), 150);
            row.Kind = AgentKinds.Contains(p.Kind) ? p.Kind! : "Other";
            row.PortIp = PrinterSnmpReader.Trunc(string.IsNullOrWhiteSpace(p.PortIp) ? null : p.PortIp.Trim(), 50);
            row.IsDefault = p.IsDefault;
            row.IsShared = p.IsShared;
            row.Location = PrinterSnmpReader.Trunc(string.IsNullOrWhiteSpace(p.Location) ? null : p.Location.Trim(), 250);
            row.Status = AgentStatuses.Contains(p.Status) ? p.Status! : "Unknown";
            var flags = (p.ErrorFlags ?? new()).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).Distinct().Take(20).ToList();
            row.ErrorCodes = flags.Count == 0 ? null : PrinterSnmpReader.Trunc(string.Join(",", flags), 300);
            row.JobsCount = Math.Clamp(p.JobsCount, 0, 100000);
            row.JobsInError = Math.Clamp(p.JobsInError, 0, 100000);
            row.OldestJobMinutes = p.OldestJobMinutes is >= 0 ? Math.Min(p.OldestJobMinutes.Value, 10_000_000) : null;
            row.IsPresent = true;
            row.LastSeenAt = now;

            // شمارندهٔ تجمعی ← اختلاف (ارسال تکراری گزارش دوباره‌شماری نمی‌کند؛ ریست ایجنت = شروع از صفر)
            long delta = 0;
            if (p.CumulativePages is >= 0)
            {
                var cur = p.CumulativePages.Value;
                if (row.AgentCumulativePages is { } last) delta = cur >= last ? cur - last : cur;
                row.AgentCumulativePages = cur;
                delta = Math.Clamp(delta, 0, 1_000_000);
                row.PagesLogged += delta;
            }
            deltas[row] = delta;
        }

        foreach (var row in rows)
        {
            row.LastReportAt = now;
            if (!reported.Any(p => string.Equals(p.Name.Trim(), row.PrinterName, StringComparison.OrdinalIgnoreCase)))
            {
                row.IsPresent = false;
                row.Status = "Unknown";
            }
        }
        await _db.SaveChangesAsync(ct);   // شناسهٔ ردیف‌های تازه

        var result = new AgentIngestResult { Printers = reported.Count, SystemLabel = system?.AgentId };

        // ---- لینک خودکار: روی این سیستم دقیقاً یک دستگاه کابلیِ بی‌لینک و یک چاپگر USB آزاد
        if (system != null)
        {
            var rowIds = rows.Select(r => r.Id).ToList();
            var takenIds = await _db.OfficeMachines.Where(m => m.AgentPrinterId != null && rowIds.Contains(m.AgentPrinterId.Value))
                .Select(m => m.AgentPrinterId!.Value).ToListAsync(ct);
            var free = rows.Where(r => IsPhysicalLocal(r.Kind) && r.IsPresent && !r.IsIgnored && !takenIds.Contains(r.Id)).ToList();
            var unlinked = await _db.OfficeMachines
                .Where(m => m.ConnectionType == "Cable" && m.LinkedSystemInfoId == system.Id && m.AgentPrinterId == null && m.Status != "Retired")
                .ToListAsync(ct);
            if (free.Count == 1 && unlinked.Count == 1)
            {
                unlinked[0].AgentPrinterId = free[0].Id;
                unlinked[0].UpdatedAt = now;
                result.AutoLinked = 1;
                await _db.SaveChangesAsync(ct);
                _log.LogInformation("چاپگر «{Printer}» سیستم {Agent} خودکار به ماشین اداری {Id} لینک شد", free[0].PrinterName, agentId, unlinked[0].Id);
            }
        }

        // ---- اعمال وضعیت روی دستگاه‌های لینک‌شده
        var ids = rows.Select(r => r.Id).ToList();
        var machines = await _db.OfficeMachines.Where(m => m.AgentPrinterId != null && ids.Contains(m.AgentPrinterId.Value)).ToListAsync(ct);
        foreach (var m in machines)
        {
            var row = rows.First(r => r.Id == m.AgentPrinterId);
            await ApplyAgentStatusAsync(m, row, deltas.GetValueOrDefault(row), now, ct);
        }
        result.LinkedMachines = machines.Count;
        return result;
    }

    /// <summary>بلافاصله پس از لینک/تغییر لینک در فرم: آخرین وضعیت شناخته‌شدهٔ چاپگر را روی دستگاه اعمال می‌کند.</summary>
    public async Task ApplyAgentLinkNowAsync(int machineId, CancellationToken ct = default)
    {
        var m = await _db.OfficeMachines.FirstOrDefaultAsync(x => x.Id == machineId, ct);
        if (m?.AgentPrinterId == null) return;
        var row = await _db.OfficeAgentPrinters.FirstOrDefaultAsync(p => p.Id == m.AgentPrinterId, ct);
        if (row == null) return;
        var now = DateTime.Now;
        if (now - row.LastReportAt > TimeSpan.FromMinutes(AgentStaleMinutes)) { MarkStale(m, row, now); await _db.SaveChangesAsync(ct); return; }
        await ApplyAgentStatusAsync(m, row, 0, now, ct);
    }

    private async Task ApplyAgentStatusAsync(OfficeMachine m, OfficeAgentPrinter p, long pagesDelta, DateTime now, CancellationToken ct)
    {
        var host = p.HostName ?? p.AgentId;
        var flags = (p.ErrorCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        var offline = p.IsPresent && (p.Status == "Offline" || flags.Contains("Offline") || flags.Contains("NotAvailable"));
        var conditions = new Dictionary<string, (string Severity, string Message)>();

        m.LastPollAt = now;
        m.LastPollError = null;

        if (!p.IsPresent)
        {
            conditions["PrinterRemoved"] = ("Warning", $"چاپگر «{p.PrinterName}» دیگر روی سیستم «{host}» نصب نیست (درایور حذف شده یا نام صف تغییر کرده). لینک را در ویرایش دستگاه اصلاح کنید.");
            m.OnlineState = "Warning";
            m.PrinterState = "unknown";
            m.LastStatusText = "صف چاپ روی سیستم میزبان پیدا نشد";
            m.LastErrorCodes = "PrinterRemoved";
        }
        else if (offline)
        {
            m.OnlineState = "Offline";
            m.PrinterState = "unknown";
            m.LastStatusText = PrinterSnmpReader.Trunc($"خاموش است یا از سیستم «{host}» جدا شده" + JobsText(p), 300);
            m.LastErrorCodes = null;
            var lastSeen = m.LastSeenOnlineAt;
            if (lastSeen == null || now - lastSeen.Value >= TimeSpan.FromMinutes(OfflineGraceMinutes))
                // خاموش کردن چاپگر USB در پایان روز عادی است ← رویداد «اطلاع» (ثبت در تاریخچه، بدون اعلان)
                conditions["Offline"] = ("Info", $"چاپگر خاموش است یا کابل آن از سیستم «{host}» جدا شده است.");
            if (p.JobsCount > 0 && p.OldestJobMinutes >= QueueStuckMinutes)
                conditions["QueueStuck"] = ("Warning", $"{p.JobsCount} کار چاپی از {p.OldestJobMinutes} دقیقه پیش در صف مانده و چاپگر در دسترس نیست.");
        }
        else
        {
            m.LastSeenOnlineAt = now;
            m.PrinterState = p.Status switch { "Idle" => "idle", "Printing" => "printing", "WarmingUp" => "warmup", "Paused" => "other", _ => "unknown" };
            foreach (var f in flags)
            {
                if (FlagRules.TryGetValue(f, out var rule) || AgentFlagRules.TryGetValue(f, out rule)) conditions[f] = rule;
            }
            if (p.JobsInError > 0)
                conditions["JobError"] = ("Warning", $"{p.JobsInError} کار چاپی با خطا در صف چاپ «{p.PrinterName}» مانده است.");
            else if (p.JobsCount > 0 && p.OldestJobMinutes >= QueueStuckMinutes && p.Status != "Printing")
                conditions["QueueStuck"] = ("Warning", $"{p.JobsCount} کار چاپی از {p.OldestJobMinutes} دقیقه پیش در صف مانده و چاپ نمی‌شود.");

            m.OnlineState = conditions.Values.Any(c => c.Severity == "Critical") ? "Error"
                          : conditions.Count > 0 ? "Warning" : "Online";
            m.LastStatusText = PrinterSnmpReader.Trunc(AgentStatusTitle(p.Status) + JobsText(p), 300);
            m.LastErrorCodes = flags.Count == 0 ? null : PrinterSnmpReader.Trunc(string.Join(",", flags), 300);
        }

        // تیراژ: فقط اگر ایجنت شمارش صفحات دارد (لاگ چاپ ویندوز فعال)
        if (p.AgentCumulativePages != null)
        {
            if (m.LastPageCount == null)
            {
                // شمارش ایجنت از لحظهٔ لینک از صفر شروع می‌شود؛ یک نقطهٔ مرجع صفر برای «دیروز» ثبت می‌شود
                // تا تیراژ همان روز اول هم در آمار ۳۰ روزه/روزانه محاسبه شود (مرجع قبل از بازه).
                var yesterday = now.Date.AddDays(-1);
                if (!await _db.OfficeMachineCounters.AnyAsync(c => c.MachineId == m.Id && c.Day <= yesterday, ct))
                    _db.OfficeMachineCounters.Add(new OfficeMachineCounter { MachineId = m.Id, Day = yesterday, TotalPages = 0, TakenAt = now });
            }
            if (pagesDelta > 0 || m.LastPageCount == null) m.LastPageCount = (m.LastPageCount ?? 0) + pagesDelta;
            await UpsertCounterAsync(m.Id, m.LastPageCount.Value, now, ct);
        }

        var (opened, _) = await SyncEventsAsync(m, conditions, pollSucceeded: p.IsPresent && !offline, now, ct);
        await _db.SaveChangesAsync(ct);
        if (opened.Count > 0 && m.AlertsEnabled && m.Status != "Retired")
            await NotifyAsync(m, opened, ct);
    }

    /// <summary>دستگاه‌هایی که ایجنت سیستم میزبانشان مدتی گزارش نداده ← «نامعلوم» (بدون باز/بستن رویداد).</summary>
    public async Task<int> MarkStaleAgentMachinesAsync(CancellationToken ct = default)
    {
        var cut = DateTime.Now.AddMinutes(-AgentStaleMinutes);
        var stale = await (from m in _db.OfficeMachines
                           join p in _db.OfficeAgentPrinters on m.AgentPrinterId equals p.Id
                           where m.Status != "Retired" && m.OnlineState != "Unknown" && p.LastReportAt < cut
                           select new { m, p }).Take(500).ToListAsync(ct);
        if (stale.Count == 0) return 0;
        var now = DateTime.Now;
        foreach (var x in stale) MarkStale(x.m, x.p, now);
        await _db.SaveChangesAsync(ct);
        return stale.Count;
    }

    private static void MarkStale(OfficeMachine m, OfficeAgentPrinter p, DateTime now)
    {
        var minutes = Math.Max(1, (int)(now - p.LastReportAt).TotalMinutes);
        m.OnlineState = "Unknown";
        m.LastPollError = PrinterSnmpReader.Trunc(
            $"سیستم میزبان «{p.HostName ?? p.AgentId}» از {minutes} دقیقه پیش گزارشی نفرستاده (سیستم خاموش است یا ایجنت اجرا نمی‌شود).", 500);
        m.LastStatusText = "سیستم میزبان گزارش نمی‌دهد";
    }

    private static string JobsText(OfficeAgentPrinter p) =>
        p.JobsCount <= 0 ? "" : p.JobsInError > 0 ? $" · {p.JobsCount} کار در صف ({p.JobsInError} خطادار)" : $" · {p.JobsCount} کار در صف";

    public static string AgentStatusTitle(string status) => status switch
    {
        "Idle" => "آماده", "Printing" => "در حال چاپ", "WarmingUp" => "در حال گرم شدن", "Offline" => "خاموش / جدا شده",
        "Paused" => "متوقف (Pause)", "Error" => "خطا", _ => "نامعلوم"
    };
}
