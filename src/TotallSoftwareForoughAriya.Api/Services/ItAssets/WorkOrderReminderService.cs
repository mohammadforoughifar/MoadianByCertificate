using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ItAssets;

/// <summary>
/// سرویس پس‌زمینه یادآور مهلت دستور کار.
/// هر چند دقیقه یک‌بار (پیش‌فرض ۱۵ دقیقه) دستورهای باز را بررسی می‌کند و در آستانه‌های
/// تعریف‌شده (پیش‌فرض ۲۴ ساعت مانده و لحظه سررسید) به گیرندگانِ بی‌پاسخ و دستوردهنده اعلان می‌فرستد.
/// برای هر (دستور، آستانه، مهلت) فقط یک‌بار اعلان می‌رود (جدول WorkOrderReminderLogs)؛
/// بعد از «تمدید مهلت»، چون DueAt عوض می‌شود، یادآورها به‌صورت خودکار دوباره فعال می‌شوند.
///
/// تنظیمات (appsettings — اختیاری):
///   WorkOrders:ReminderThresholdHours  → آرایه ساعت‌های مانده تا مهلت، مثل [48, 24, 0]
///   WorkOrders:ReminderCheckMinutes    → فاصله بررسی به دقیقه (پیش‌فرض ۱۵)
/// </summary>
public class WorkOrderReminderService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<WorkOrderReminderService> _log;
    private readonly IConfiguration _cfg;

    public WorkOrderReminderService(IServiceProvider sp, ILogger<WorkOrderReminderService> log, IConfiguration cfg)
    {
        _sp = sp; _log = log; _cfg = cfg;
    }

    private const string FormName = "دستور کار";

    /// <summary>آستانه‌های یادآور بر حسب «ساعت مانده تا مهلت». 0 یعنی لحظه سررسید.</summary>
    private int[] Thresholds =>
        _cfg.GetSection("WorkOrders:ReminderThresholdHours").Get<int[]>() is { Length: > 0 } t
            ? t.Where(x => x >= 0).Distinct().OrderByDescending(x => x).ToArray()
            : new[] { 24, 0 };

    /// <summary>فاصله بین دو بررسی (دقیقه) — حداقل ۱ دقیقه.</summary>
    private int CheckMinutes => Math.Max(1, _cfg.GetValue<int?>("WorkOrders:ReminderCheckMinutes") ?? 15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // صبر اولیه تا مایگریشن‌ها و راه‌اندازی برنامه تمام شود
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "خطا در بررسی یادآور مهلت دستور کار");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(CheckMinutes), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>یک دور کامل بررسی. خروجی: تعداد یادآورهای ارسال‌شده (برای تست/اجرای دستی).</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<INotifyService>();

        var now = DateTime.Now;
        var thresholds = Thresholds;
        var maxHours = thresholds.Max();

        // فقط دستورهای باز که مهلتشان در بازه دیدِ بزرگ‌ترین آستانه است.
        // دستورهایی که مهلتشان بیش از ۷ روز گذشته را رها می‌کنیم تا برای رکوردهای قدیمی سیل اعلان نرود.
        var horizon = now.AddHours(maxHours);
        var floor = now.AddDays(-7);
        var orders = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Status == "Open" && w.DueAt <= horizon && w.DueAt >= floor)
            .ToListAsync(ct);
        if (orders.Count == 0) return 0;

        var ids = orders.Select(o => o.Id).ToList();
        var assignees = await db.WorkOrderAssignees.AsNoTracking()
            .Where(a => ids.Contains(a.OrderId))
            .ToListAsync(ct);
        var sentLogs = await db.WorkOrderReminderLogs.AsNoTracking()
            .Where(r => ids.Contains(r.OrderId))
            .ToListAsync(ct);

        var sent = 0;
        foreach (var wo in orders)
        {
            ct.ThrowIfCancellationRequested();

            // بزرگ‌ترین آستانه‌ای که الان در محدوده آن هستیم (فقط همان یکی — نه همه آستانه‌های ردشده)
            var hoursLeft = (wo.DueAt - now).TotalHours;
            var threshold = thresholds.Where(t => hoursLeft <= t).DefaultIfEmpty(-1).Min();
            if (threshold < 0) continue; // هنوز به هیچ آستانه‌ای نرسیده

            // قبلاً برای همین مهلت و همین آستانه (یا آستانه کوچکتر) ارسال شده؟
            if (sentLogs.Any(r => r.OrderId == wo.Id && r.DueAtSnapshot == wo.DueAt && r.ThresholdHours <= threshold))
                continue;

            var pending = assignees
                .Where(a => a.OrderId == wo.Id && a.RepliedAt == null)
                .ToList();
            if (pending.Count == 0) continue; // همه پاسخ داده‌اند — یادآور لازم نیست

            var (title, body) = BuildMessage(wo, threshold, now);

            // گیرندگانِ بی‌پاسخ
            foreach (var a in pending.Where(a => a.UserId != wo.OwnerUserId))
                await notify.SendAsync(a.UserId, title, body, "سیستم", FormName, $"/work-orders?open={wo.Id}");

            // دستوردهنده هم در جریان قرار بگیرد (اگر خودش جزو بی‌پاسخ‌ها نبود، فقط گزارش وضعیت)
            var ownerBody = pending.Any(a => a.UserId == wo.OwnerUserId)
                ? body
                : $"{body} — {pending.Count} گیرنده هنوز پاسخ نداده‌اند: {string.Join("، ", pending.Select(a => a.Name))}";
            await notify.SendAsync(wo.OwnerUserId, title, ownerBody, "سیستم", FormName, $"/work-orders?open={wo.Id}");

            db.WorkOrderReminderLogs.Add(new WorkOrderReminderLog
            {
                OrderId = wo.Id,
                ThresholdHours = threshold,
                DueAtSnapshot = wo.DueAt,
                SentAt = now
            });
            sent++;
        }

        if (sent > 0)
        {
            await db.SaveChangesAsync(ct);
            _log.LogInformation("یادآور مهلت دستور کار: {Count} اعلان ارسال شد.", sent);
        }
        return sent;
    }

    private static (string Title, string Body) BuildMessage(WorkOrder wo, int threshold, DateTime now)
    {
        var pr = wo.Priority >= WorkOrderPriority.High ? $" ({WorkOrderPriority.ToFa(wo.Priority)})" : "";
        if (threshold == 0)
        {
            return now > wo.DueAt
                ? ($"مهلت دستور کار گذشت ⏰{pr}", $"{wo.Number} — «{wo.Title}» — مهلت: {ToFa(wo.DueAt)}")
                : ($"امروز مهلت دستور کار است 📅{pr}", $"{wo.Number} — «{wo.Title}» — تا ساعت {wo.DueAt:HH:mm}");
        }
        return ($"یادآوری مهلت دستور کار ⏳{pr}",
                $"{wo.Number} — «{wo.Title}» — کمتر از {threshold} ساعت تا مهلت ({ToFa(wo.DueAt)})");
    }

    private static string ToFa(DateTime d)
    {
        var pc = new PersianCalendar();
        return $"{pc.GetYear(d)}/{pc.GetMonth(d):00}/{pc.GetDayOfMonth(d):00} {d:HH:mm}";
    }
}
