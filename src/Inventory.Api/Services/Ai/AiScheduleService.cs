using System.Text.Json;
using System.Text.RegularExpressions;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// گزارش‌های زمان‌بندی‌شده (§۲۱) — «هر شنبه ساعت ۸ فروش هفته گذشته را بفرست»:
// kind ‏bi = یکی از ۶ خلاصه آماده (متن فقط)؛ kind ‏explore = هر کاوش روی
// ۲۶ موجودیت (متن + اکسل اختیاری). اجرا دوره‌ای + ارسال در بله/ایتا.
// =====================================================================

public class AiScheduleService
{
    public const int MaxActivePerUser = 10;
    public static readonly string[] BiReports =
        { "sales_summary", "recent_invoices", "stock_status", "cheques_due", "top_debtors", "cash_status" };

    private readonly AppDbContext _db;
    private readonly ILogger<AiScheduleService> _log;

    public AiScheduleService(AppDbContext db, ILogger<AiScheduleService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>ثبت زمان‌بندی با اجرای آزمایشی؛ خطا: bad_kind/bad_spec/bad_schedule/too_many/dry_failed.</summary>
    public async Task<(AiReportSchedule? schedule, string? error, string? preview)> CreateAsync(
        int userId, string title, string kind, string specJson, string schedType, int day,
        string time, bool excel, IServiceProvider services, CancellationToken ct)
    {
        kind = (kind ?? "").Trim().ToLowerInvariant();
        if (kind != "bi" && kind != "explore") return (null, "bad_kind", null);
        if (excel && kind == "bi") return (null, "bad_spec", null);
        if (schedType is not ("daily" or "weekly" or "monthly")) return (null, "bad_schedule", null);
        if (schedType == "weekly" && (day < 0 || day > 6)) return (null, "bad_schedule", null);
        if (schedType == "monthly" && (day < 0 || day > 31)) return (null, "bad_schedule", null);
        var span = ParseTimeSpan(time);
        if (span == null) return (null, "bad_schedule", null);
        title = (title ?? "").Trim();
        if (title == "") title = "گزارش زمان‌بندی‌شده";
        if (title.Length > 300) title = title[..300];

        var active = await _db.AiReportSchedules.AsNoTracking()
            .CountAsync(s => s.UserId == userId && s.IsActive, ct);
        if (active >= MaxActivePerUser) return (null, "too_many", null);

        var dry = await ExecuteAsync(kind, specJson, userId, title, false, services, ct);
        if (dry.error != null) return (null, "dry_failed:" + dry.error, null);

        var now = DateTime.Now;
        var sch = new AiReportSchedule
        {
            UserId = userId, Title = title, Kind = kind, SpecJson = specJson,
            ScheduleType = schedType, Day = day, Time = span.Value.ToString(@"hh\:mm"),
            WantExcel = excel, NextRunAt = NextAfter(schedType, day, span.Value, now),
        };
        _db.AiReportSchedules.Add(sch);
        await _db.SaveChangesAsync(ct);
        return (sch, null, AiTextUtil.Truncate(dry.text ?? "", 600));
    }

    public async Task<List<AiReportSchedule>> ListActiveAsync(int userId, CancellationToken ct) =>
        await _db.AiReportSchedules.AsNoTracking()
            .Where(s => s.UserId == userId && s.IsActive)
            .OrderBy(s => s.NextRunAt).Take(20).ToListAsync(ct);

    public async Task<bool> CancelAsync(int userId, int id, CancellationToken ct)
    {
        var sch = await _db.AiReportSchedules
            .Where(s => s.Id == id && s.UserId == userId && s.IsActive)
            .FirstOrDefaultAsync(ct);
        if (sch == null) return false;
        sch.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>اجرای زمان‌بندی‌های سررسیده + ارسال؛ برمی‌گرداند: تعداد ارسال موفق.</summary>
    public async Task<int> RunDueAsync(IMessengerService messenger, IServiceProvider services, CancellationToken ct)
    {
        var now = DateTime.Now;
        var due = await _db.AiReportSchedules
            .Where(s => s.IsActive && s.NextRunAt <= now)
            .OrderBy(s => s.NextRunAt).Take(20).ToListAsync(ct);
        var sent = 0;
        foreach (var sch in due)
        {
            var span = ParseTimeSpan(sch.Time) ?? new TimeSpan(8, 0, 0);
            try
            {
                var user = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == sch.UserId)
                    .Select(u => new { u.Role, u.BaleChatId, u.EitaaChatId })
                    .FirstOrDefaultAsync(ct);
                var linked = user != null && (user.BaleChatId != null || user.EitaaChatId != null);
                var allowed = user != null && await AiAccessHelper.UserHasAsync(
                    _db, sch.UserId, "AiAssistant", "Use", user.Role, ct);
                if (!allowed || !linked)
                {
                    sch.NextRunAt = NextAfter(sch.ScheduleType, sch.Day, span, now);
                    await _db.SaveChangesAsync(ct);
                    continue;
                }
                var r = await ExecuteAsync(sch.Kind, sch.SpecJson, sch.UserId, sch.Title, sch.WantExcel, services, ct);
                if (r.error != null) throw new InvalidOperationException(r.error);
                var plain = AiMessengerFormat.ToPlain(r.text ?? "");
                if (plain.Length > 3000) plain = plain[..3000] + "…";
                var res = await messenger.SendToUserAsync(sch.UserId, "📅 " + sch.Title, plain);
                if (res.BaleSent || res.EitaaSent)
                {
                    if (r.excel != null)
                        await messenger.SendExcelToUserAsync(sch.UserId, $"📥 {sch.Title}", r.excel, r.fileName ?? "gozaresh.xlsx");
                    sch.LastRunAt = now;
                    sch.FailCount = 0;
                    sent++;
                }
                sch.NextRunAt = NextAfter(sch.ScheduleType, sch.Day, span, now);
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "اجرای زمان‌بندی {Id} ناموفق بود.", sch.Id);
                sch.FailCount++;
                if (sch.FailCount >= 5)
                {
                    sch.IsActive = false;
                    try
                    {
                        await messenger.SendToUserAsync(sch.UserId, "⏸ توقف زمان‌بندی",
                            $"زمان‌بندی «{sch.Title}» به‌خاطر ۵ خطای پیاپی متوقف شد. با «زمان‌بندی‌های من» ببین و دوباره بساز.");
                    }
                    catch { /* نادیده */ }
                }
                else
                {
                    sch.NextRunAt = NextAfter(sch.ScheduleType, sch.Day, span, now);
                }
                try { await _db.SaveChangesAsync(ct); }
                catch { /* نادیده */ }
            }
        }
        return sent;
    }

    /// <summary>اجرای یک مشخصات (خشک یا واقعی)؛ خروجی: متن + اکسل اختیاری.</summary>
    public async Task<(string? text, byte[]? excel, string? fileName, string? error)> ExecuteAsync(
        string kind, string specJson, int userId, string title, bool wantExcel,
        IServiceProvider services, CancellationToken ct)
    {
        try
        {
            if (kind == "bi") return (await ExecuteBiAsync(specJson, userId, services, ct), null, null, null);
            return await ExecuteExploreAsync(specJson, userId, title, wantExcel, services, ct);
        }
        catch (Exception ex)
        {
            return (null, null, null, ex.Message);
        }
    }

    private async Task<string> ExecuteBiAsync(
        string specJson, int userId, IServiceProvider services, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(specJson) ? "{}" : specJson);
        var root = doc.RootElement;
        var report = root.TryGetProperty("report", out var rp) ? rp.GetString() ?? "" : "";
        if (!BiReports.Contains(report))
            throw new InvalidOperationException($"گزارش «{report}» معتبر نیست.");
        var argsRaw = root.TryGetProperty("args", out var a) ? a.GetRawText() : "{}";
        var tools = services.GetRequiredService<AiToolRegistry>();
        var ctx = new AiToolContext { UserId = userId, Services = services, CancellationToken = ct };
        var json = await tools.ExecuteAsync(report, argsRaw, ctx);
        if (ToolError(json) is { } err) throw new InvalidOperationException(err);
        var searched = argsRaw.Contains("\"search\"");
        return report switch
        {
            "sales_summary" => AiFallbackRouter.FormatSales(json),
            "recent_invoices" => AiFallbackRouter.FormatInvoices(json),
            "stock_status" => AiFallbackRouter.FormatStock(json, searched),
            "cheques_due" => AiFallbackRouter.FormatCheques(json),
            "top_debtors" => AiFallbackRouter.FormatDebtors(json),
            _ => AiFallbackRouter.FormatCash(json),
        };
    }

    private async Task<(string? text, byte[]? excel, string? fileName, string? error)> ExecuteExploreAsync(
        string specJson, int userId, string title, bool wantExcel,
        IServiceProvider services, CancellationToken ct)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var req = JsonSerializer.Deserialize<AiExploreRequest>(specJson, opts);
        if (req == null || string.IsNullOrWhiteSpace(req.Entity))
            return (null, null, null, "موجودیت کاوش مشخص نیست.");
        req.Limit = Math.Clamp(req.Limit <= 0 ? 20 : req.Limit, 1, 50);
        var explorer = services.GetRequiredService<AiDataExplorer>();
        var (ok, error, result) = await explorer.QueryAsync(userId, req, ct);
        if (!ok || result == null) return (null, null, null, error ?? "خطای کاوش.");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📊 {title} — {AiTextUtil.ToFaDigits(result.TotalCount.ToString())} مورد:");
        foreach (var row in result.Rows.Take(8))
            sb.AppendLine("• " + string.Join(" / ", row.Select(c => AiTextUtil.Truncate(c?.ToString() ?? "—", 40))));
        if (result.TotalCount > result.Rows.Take(8).Count())
            sb.AppendLine($"…و {AiTextUtil.ToFaDigits((result.TotalCount - Math.Min(8, result.Rows.Count)).ToString())} مورد دیگر" +
                (wantExcel ? " (در فایل اکسل)" : ""));
        byte[]? bytes = null;
        string? fileName = null;
        if (wantExcel && result.Rows.Count > 0)
        {
            var reports = services.GetRequiredService<AiReportService>();
            var preview = await reports.BuildCustomAsync(userId, title, result.Columns, result.Rows);
            var spec = reports.TryGetSpec(userId, preview.ReportId);
            if (spec != null)
            {
                bytes = Inventory.Api.Services.Export.ExcelWriter.Build(spec);
                fileName = $"gozaresh-{DateTime.Today:yyyyMMdd}.xlsx";
            }
        }
        return (sb.ToString().Trim(), bytes, fileName, null);
    }

    private static string? ToolError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out _))
                return doc.RootElement.TryGetProperty("message", out var m)
                    ? m.GetString() ?? "خطای ابزار." : "خطای ابزار.";
            return null;
        }
        catch { return null; }
    }

    public static TimeSpan? ParseTimeSpan(string? time)
    {
        if (TimeSpan.TryParse((time ?? "").Trim(), out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
            return new TimeSpan(t.Hours, t.Minutes, 0);
        return null;
    }

    /// <summary>نوبت بعدی اجرا بعد از now.</summary>
    public static DateTime NextAfter(string type, int day, TimeSpan time, DateTime now)
    {
        if (type == "daily")
        {
            var c = now.Date.Add(time);
            return c <= now ? c.AddDays(1) : c;
        }
        if (type == "monthly")
        {
            int D(int y, int m) => day <= 0 ? DateTime.DaysInMonth(y, m) : Math.Min(day, DateTime.DaysInMonth(y, m));
            var c = new DateTime(now.Year, now.Month, D(now.Year, now.Month)).Add(time);
            if (c <= now)
            {
                var n = now.AddMonths(1);
                c = new DateTime(n.Year, n.Month, D(n.Year, n.Month)).Add(time);
            }
            return c;
        }
        var days = ((day - (int)now.DayOfWeek) + 7) % 7;
        var w = now.Date.AddDays(days).Add(time);
        return w <= now ? w.AddDays(7) : w;
    }

    private static readonly string[] FaWeekdays =
        { "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه" };

    public static string FaSchedule(string type, int day, string time)
    {
        var t = AiTextUtil.ToFaDigits((time ?? "08:00").Trim());
        if (type == "daily") return $"هر روز ساعت {t}";
        if (type == "monthly")
            return day <= 0 ? $"آخر هر ماه ساعت {t}" : $"روز {AiTextUtil.ToFaDigits(day.ToString())} هر ماه ساعت {t}";
        var wd = day >= 0 && day < 7 ? FaWeekdays[day] : "شنبه";
        return $"هر {wd} ساعت {t}";
    }

    public static string FaDateTime(DateTime dt) =>
        $"{AiDateUtil.ToFaShort(dt)} ساعت {AiTextUtil.ToFaDigits(dt.ToString("HH:mm"))}";
}

// =====================================================================
// ورکر گزارش‌های زمان‌بندی‌شده: هر ۶۰ ثانیه سررسیده‌ها را اجرا می‌کند.
// =====================================================================

public class AiScheduleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AiScheduleWorker> _log;

    public AiScheduleWorker(IServiceScopeFactory scopes, ILogger<AiScheduleWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value;
                if (!options.Enabled || !options.ScheduleEnabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                    continue;
                }
                var svc = scope.ServiceProvider.GetRequiredService<AiScheduleService>();
                var messenger = scope.ServiceProvider.GetRequiredService<IMessengerService>();
                var sent = await svc.RunDueAsync(messenger, scope.ServiceProvider, stoppingToken);
                if (sent > 0)
                    _log.LogInformation("{Sent} گزارش زمان‌بندی‌شده ارسال شد.", sent);
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "خطا در چرخه گزارش‌های زمان‌بندی‌شده؛ تلاش مجدد بعداً.");
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}

// =====================================================================
// پارس دوره فارسی: «هر روز ساعت ۸»، «هر شنبه ساعت ۸»، «اول هر ماه ساعت ۹»
// =====================================================================

internal static class AiScheduleTime
{
    public static (string? type, int day, string time) Parse(string? raw, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, 0, "");
        var t = AiTextUtil.NormalizeFa(AiTextUtil.ToEnDigits(raw)).Replace("‌", "").Trim();
        var time = ParseClock(t) ?? "08:00";
        if (t.Contains("هر روز") || t.Contains("هرروز") || t.Contains("روزانه")) return ("daily", 0, time);
        if (t.Contains("آخر") && t.Contains("ماه")) return ("monthly", 0, time);
        if (t.Contains("اول") && t.Contains("ماه")) return ("monthly", 1, time);
        var dm = Regex.Match(t, @"روز\s*(\d{1,2})\s*(هر\s*)?ماه");
        if (dm.Success && int.TryParse(dm.Groups[1].Value, out var d) && d >= 1 && d <= 31)
            return ("monthly", d, time);
        if (t.Contains("هر ماه") || t.Contains("هرماه") || t.Contains("ماهانه")) return ("monthly", 1, time);
        if (t.Contains("هر هفته") || t.Contains("هرهفته") || t.Contains("هفتگی"))
        {
            var w = ParseWeekday(t);
            return w != null ? ("weekly", (int)w.Value, time) : ("weekly", (int)now.DayOfWeek, time);
        }
        var wd = ParseWeekday(t);
        if (wd != null) return ("weekly", (int)wd.Value, time);
        return (null, 0, "");
    }

    private static string? ParseClock(string t)
    {
        var m = Regex.Match(t, @"ساعت\s*(\d{1,2})(?::(\d{1,2}))?");
        if (!m.Success) return null;
        var h = int.Parse(m.Groups[1].Value);
        var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        if (t.Contains("ظهر") && h < 10) h += 12;
        else if ((t.Contains("عصر") || t.Contains("شب")) && h < 12) h += 12;
        if (h > 23 || min > 59) return null;
        return $"{h:00}:{min:00}";
    }

    private static DayOfWeek? ParseWeekday(string t)
    {
        if (t.Contains("شنبه") && !t.Contains("یکشنبه") && !t.Contains("دوشنبه") && !t.Contains("سهشنبه") && !t.Contains("چهارشنبه") && !t.Contains("پنجشنبه")) return DayOfWeek.Saturday;
        if (t.Contains("یکشنبه")) return DayOfWeek.Sunday;
        if (t.Contains("دوشنبه")) return DayOfWeek.Monday;
        if (t.Contains("سهشنبه")) return DayOfWeek.Tuesday;
        if (t.Contains("چهارشنبه")) return DayOfWeek.Wednesday;
        if (t.Contains("پنجشنبه")) return DayOfWeek.Thursday;
        if (t.Contains("جمعه")) return DayOfWeek.Friday;
        return null;
    }
}
