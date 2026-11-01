using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ItAssets;

/// <summary>
/// اعلان عملیاتی هشدارهای سرور. اعلان فقط پس از انتخاب مسئول و فعال‌کردن صریح آن در
/// شناسنامهٔ همان سرور ارسال می‌شود. هشدارهای Warning یک‌بار و Critical با فاصلهٔ کنترل‌شده
/// یادآوری می‌شوند؛ در Maintenance Window اعلان ارسال نمی‌شود اما پایش و ثبت هشدار ادامه دارد.
/// </summary>
public sealed class ServerAlertNotificationService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly IConfiguration _config;
    private readonly ILogger<ServerAlertNotificationService> _log;

    public ServerAlertNotificationService(AppDbContext db, INotifyService notify, IConfiguration config,
        ILogger<ServerAlertNotificationService> log)
    {
        _db = db;
        _notify = notify;
        _config = config;
        _log = log;
    }

    private int CriticalReminderMinutes => Math.Clamp(
        _config.GetValue<int?>("ServerMonitoring:CriticalReminderMinutes") ?? 240, 30, 1440);

    public async Task NotifyDueAsync(ManagedServer server, DateTime now, CancellationToken ct = default)
    {
        if (!server.AlertNotificationsEnabled || server.ResponsibleUserId is not > 0) return;
        if (server.MaintenanceUntil.HasValue && server.MaintenanceUntil.Value > now) return;

        var recipient = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == server.ResponsibleUserId && u.IsActive, ct);
        if (recipient is null) return;

        // هشدار تأییدشده به‌معنای پذیرش مسئولیت است. اگر از Warning به Critical برسد، قواعد هشدار
        // وضعیت را دوباره Open می‌کنند و در اینجا اعلان تازه خواهد گرفت.
        var alerts = await _db.ServerAlerts
            .Where(a => a.ManagedServerId == server.Id && a.Status == "Open")
            .OrderBy(a => a.Severity == "Critical" ? 0 : 1)
            .ThenBy(a => a.OpenedAt)
            .ToListAsync(ct);

        foreach (var alert in alerts)
        {
            var critical = string.Equals(alert.Severity, "Critical", StringComparison.OrdinalIgnoreCase);
            var isDue = alert.LastNotifiedAt is null
                || (critical && now - alert.LastNotifiedAt.Value >= TimeSpan.FromMinutes(CriticalReminderMinutes));
            if (!isDue) continue;

            var category = string.Equals(alert.Category, "Connection", StringComparison.OrdinalIgnoreCase)
                ? "ارتباط iLO"
                : string.Equals(alert.Category, "HardwareEvent", StringComparison.OrdinalIgnoreCase) ? "رخداد iLO" : "سلامت سخت‌افزار";
            var title = critical ? $"هشدار بحرانی سرور: {server.Name}" : $"هشدار سرور: {server.Name}";
            var reminder = alert.NotificationCount > 0 ? " (یادآوری)" : "";
            var body = $"{category}{reminder} — {Trim(alert.Message, 380)}";

            try
            {
                // NotifyService اعلان درون‌برنامه‌ای، SignalR، پیام‌رسان متصل و صف Push را پوشش می‌دهد.
                // SaveChanges داخل آن، تغییر زمان/شمارندهٔ زیر را نیز به‌صورت یکجا پایدار می‌کند.
                alert.LastNotifiedAt = now;
                alert.NotificationCount++;
                await _notify.SendAsync(recipient.Id, title, body, "پایش سرورها", "مدیریت سرورها", "/servers");
            }
            catch (Exception ex)
            {
                // خرابی کانال اعلان نباید چرخهٔ پایش یا ثبت هشدار را متوقف کند.
                _db.Entry(alert).State = EntityState.Unchanged;
                _log.LogWarning(ex, "ارسال اعلان هشدار سرور {ServerId} به کاربر {UserId} ناموفق بود.", server.Id, recipient.Id);
            }
        }
    }

    private static string Trim(string? value, int max)
    {
        value = string.IsNullOrWhiteSpace(value) ? "جزئیات بیشتری ثبت نشده است." : value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}
