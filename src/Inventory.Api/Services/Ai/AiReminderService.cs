using System.Text.RegularExpressions;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// یادآور شخصی «فروغ آریا» (§۲۰) — «فردا ساعت ۹ یادم بنداز جلسه دارم»:
// ثبت با زمان فارسی آزاد (یکبار/روزانه/هفتگی) + ارسال سر وقت در بله/ایتا.
// ارسال فقط برای کاربران لینک‌شده؛ ثبت و فهرست برای همه.
// =====================================================================

public class AiReminderService
{
    public const int MaxActivePerUser = 20;

    private readonly AppDbContext _db;
    private readonly ILogger<AiReminderService> _log;

    public AiReminderService(AppDbContext db, ILogger<AiReminderService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>ثبت یادآور؛ خطا: empty/past/far/too_many.</summary>
    public async Task<(AiReminder? reminder, string? error)> CreateAsync(
        int userId, string text, DateTime remindAt, int recurrence, CancellationToken ct)
    {
        text = (text ?? "").Trim();
        if (text == "") return (null, "empty");
        if (text.Length > 500) text = text[..500];
        var now = DateTime.Now;
        if (remindAt <= now.AddMinutes(1)) return (null, "past");
        if (remindAt > now.AddYears(1)) return (null, "far");
        if (recurrence < 0 || recurrence > 2) recurrence = 0;
        var active = await _db.AiReminders.AsNoTracking()
            .CountAsync(r => r.UserId == userId && !r.IsSent, ct);
        if (active >= MaxActivePerUser) return (null, "too_many");
        var r = new AiReminder { UserId = userId, Text = text, RemindAt = remindAt, Recurrence = recurrence };
        _db.AiReminders.Add(r);
        await _db.SaveChangesAsync(ct);
        return (r, null);
    }

    public async Task<List<AiReminder>> ListPendingAsync(int userId, CancellationToken ct) =>
        await _db.AiReminders.AsNoTracking()
            .Where(r => r.UserId == userId && !r.IsSent)
            .OrderBy(r => r.RemindAt).Take(10).ToListAsync(ct);

    public async Task<bool> CancelAsync(int userId, int id, CancellationToken ct)
    {
        var r = await _db.AiReminders
            .Where(x => x.Id == id && x.UserId == userId && !x.IsSent)
            .FirstOrDefaultAsync(ct);
        if (r == null) return false;
        r.IsSent = true;
        r.SentAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>ارسال یادآورهای سررسیده؛ برمی‌گرداند: تعداد ارسال موفق.</summary>
    public async Task<int> SendDueAsync(IMessengerService messenger, CancellationToken ct)
    {
        var now = DateTime.Now;
        var due = await _db.AiReminders
            .Where(r => !r.IsSent && r.RemindAt <= now)
            .OrderBy(r => r.RemindAt).Take(50).ToListAsync(ct);
        var sent = 0;
        foreach (var r in due)
        {
            try
            {
                var links = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == r.UserId)
                    .Select(u => new { u.BaleChatId, u.EitaaChatId })
                    .FirstOrDefaultAsync(ct);
                if (links == null || (links.BaleChatId == null && links.EitaaChatId == null))
                    continue; // لینک نشده؛ معلق می‌ماند تا لینک شود
                var late = now - r.RemindAt > TimeSpan.FromMinutes(15);
                var title = late ? "⏰ یادآوری معوق" : "⏰ یادآوری";
                var result = await messenger.SendToUserAsync(r.UserId, title, r.Text + RepeatSuffix(r.Recurrence));
                if (!result.BaleSent && !result.EitaaSent) continue; // بعداً دوباره
                if (r.Recurrence == 0)
                {
                    r.IsSent = true;
                    r.SentAt = now;
                }
                else
                {
                    var step = r.Recurrence == 1 ? 1 : 7;
                    do { r.RemindAt = r.RemindAt.AddDays(step); } while (r.RemindAt <= now);
                }
                await _db.SaveChangesAsync(ct);
                sent++;
            }
            catch (Exception ex)
            {
                r.Attempts++;
                if (r.Attempts >= 10)
                {
                    r.IsSent = true;
                    r.SentAt = now;
                }
                try { await _db.SaveChangesAsync(ct); }
                catch { /* نادیده */ }
                _log.LogDebug(ex, "ارسال یادآور {Id} ناموفق بود (تلاش {Attempts}).", r.Id, r.Attempts);
            }
        }
        return sent;
    }

    private static string RepeatSuffix(int rec) => rec switch
    {
        1 => "\n🔁 (تکرار روزانه)",
        2 => "\n🔁 (تکرار هفتگی)",
        _ => "",
    };

    public static string FaDateTime(DateTime dt) =>
        $"{AiDateUtil.ToFaShort(dt)} ساعت {AiTextUtil.ToFaDigits(dt.ToString("HH:mm"))}";

    public static string RepeatFa(int rec) => rec switch
    {
        1 => "روزانه 🔁",
        2 => "هفتگی 🔁",
        _ => "یکبار",
    };
}

// =====================================================================
// ورکر یادآور: هر ۶۰ ثانیه یادآورهای سررسیده را می‌فرستد.
// =====================================================================

public class AiReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AiReminderWorker> _log;

    public AiReminderWorker(IServiceScopeFactory scopes, ILogger<AiReminderWorker> log)
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
                if (!options.Enabled || !options.ReminderEnabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                    continue;
                }
                var reminders = scope.ServiceProvider.GetRequiredService<AiReminderService>();
                var messenger = scope.ServiceProvider.GetRequiredService<IMessengerService>();
                var sent = await reminders.SendDueAsync(messenger, stoppingToken);
                if (sent > 0)
                    _log.LogInformation("{Sent} یادآور ارسال شد.", sent);
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "خطا در چرخه یادآور؛ تلاش مجدد بعداً.");
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}

// =====================================================================
// پارس زمان فارسی یادآور: «۲۰ دقیقه دیگه»، «فردا ساعت ۹»، «هر روز ساعت
// ۸ صبح»، «جمعه ساعت ۵ عصر»، «۱۴۰۴/۰۷/۱۰ ساعت ۱۰». خروجی: زمان + تکرار.
// =====================================================================

internal static class AiReminderTime
{
    public static (DateTime? at, int recurrence) Parse(string? when, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(when)) return (null, 0);
        var t = AiTextUtil.NormalizeFa(AiTextUtil.ToEnDigits(when)).Replace("‌", "").Replace("  ", " ").Trim();
        var rec = t.Contains("هر روز") || t.Contains("هرروز") || t.Contains("روزانه") ? 1
            : t.Contains("هر هفته") || t.Contains("هرهفته") || t.Contains("هفتگی") ? 2 : 0;

        // ۱) تاریخ شمسی صریح — اول جدا می‌شود تا با «N ساعت» قاطی نشود
        DateTime? baseDate = null;
        var dateM = Regex.Match(t, @"(\d{4}/\d{1,2}/\d{1,2})");
        if (dateM.Success)
        {
            baseDate = AiLeaveHelper.ParseFaDate(dateM.Groups[1].Value, now.Date)?.Date;
            if (baseDate == null) return (null, 0);
            t = t.Replace(dateM.Groups[1].Value, " ");
        }

        // ۲) نسبی: «۲۰ دقیقه دیگه»، «نیم ساعت دیگه»، «۵ ساعت بعد»
        if (t.Contains("نیم ساعت"))
            return (now.AddMinutes(30), rec);
        var minM = Regex.Match(t, @"(\d{1,4})\s*دقیقه");
        if (!t.Contains("ساعت") && minM.Success && int.TryParse(minM.Groups[1].Value, out var mins) && mins >= 1 && mins <= 1440)
            return (now.AddMinutes(mins), rec);
        var hourRelM = Regex.Match(t, @"(\d{1,3})\s*ساعت\s*(دیگه|دیگر|بعد)?");
        if (hourRelM.Success && int.TryParse(hourRelM.Groups[1].Value, out var hrs) && hrs >= 1 && hrs <= 168
            && !t.Contains("ساعت " + hourRelM.Groups[1].Value))
            return (now.AddHours(hrs), rec);

        // ۳) روز مبنا
        DayOfWeek? weekday = null;
        if (baseDate == null)
        {
            if (t.Contains("پسفردا")) baseDate = now.Date.AddDays(2);
            else if (t.Contains("فردا")) baseDate = now.Date.AddDays(1);
            else if (t.Contains("امروز")) baseDate = now.Date;
            else weekday = ParseWeekday(t);
            if (baseDate == null && weekday == null) baseDate = now.Date;
        }

        // ۴) ساعت: «ساعت ۹:۳۰»، «۹ صبح»، «۵ عصر»، «شب» تنها
        var (h, m) = ParseClock(t);
        if (h == null && t.Contains("ساعت"))
            return (null, 0); // «ساعت» هست ولی نامعتبر است → خطا، نه حدس
        if (h == null)
        {
            if (baseDate != null && (t.Contains("فردا") || t.Contains("پسفردا") || t.Contains("امروز") || dateM.Success || weekday != null))
                h = 9; // روز مشخص بدون ساعت → ۹ صبح
            else
                return (null, 0); // نه زمان نسبی، نه روز، نه ساعت
            m = 0;
        }

        DateTime candidate;
        if (weekday != null)
        {
            var days = (((int)weekday.Value - (int)now.DayOfWeek) + 7) % 7;
            candidate = now.Date.AddDays(days).AddHours(h.Value).AddMinutes(m!.Value);
            if (candidate <= now) candidate = candidate.AddDays(7);
        }
        else
        {
            candidate = baseDate!.Value.AddHours(h.Value).AddMinutes(m!.Value);
            var hasDayWord = t.Contains("فردا") || t.Contains("پسفردا") || t.Contains("امروز") || dateM.Success;
            if (!hasDayWord && candidate <= now) candidate = candidate.AddDays(1); // «ساعت ۹» گذشته → فردا
        }
        return (candidate, rec);
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

    private static (int? h, int? m) ParseClock(string t)
    {
        var cm = Regex.Match(t, @"ساعت\s*(\d{1,2})(?:\s*:\s*(\d{1,2}))?");
        if (cm.Success)
        {
            var h = int.Parse(cm.Groups[1].Value);
            var m = cm.Groups[2].Success ? int.Parse(cm.Groups[2].Value) : 0;
            if (h <= 23 && m <= 59) return (AdjustDaypart(h, t), m);
            return (null, null);
        }
        var pm = Regex.Match(t, @"(\d{1,2})\s*(صبح|ظهر|عصر|شب)");
        if (pm.Success)
        {
            var h = AdjustDaypart(int.Parse(pm.Groups[1].Value), pm.Groups[2].Value);
            if (h <= 23) return (h, 0);
            return (null, null);
        }
        if (t.Contains("صبح")) return (8, 0);
        if (t.Contains("ظهر")) return (13, 0);
        if (t.Contains("عصر")) return (17, 0);
        if (t.Contains("شب")) return (21, 0);
        return (null, null);
    }

    private static int AdjustDaypart(int h, string t)
    {
        if (t.Contains("صبح")) return h;
        if (t.Contains("ظهر")) return h < 10 ? h + 12 : h;
        if (t.Contains("عصر") || t.Contains("شب")) return h < 12 ? h + 12 : h;
        return h;
    }
}
