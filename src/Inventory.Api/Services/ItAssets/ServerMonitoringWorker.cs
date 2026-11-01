using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ItAssets;

/// <summary>
/// پایش زمان‌بندی‌شدهٔ سرورهای فعال: تنها سرورهایی را بررسی می‌کند که کاربر برایشان
/// MonitoringEnabled را فعال کرده است. نتیجه شامل Snapshot، لاگ عملیاتی و هشدارهای
/// Connection/Health است؛ هیچ فرمان Power یا Firmware ارسال نمی‌شود.
///
/// تنظیمات اختیاری:
/// ServerMonitoring:Enabled = true|false (پیش‌فرض true؛ ولی هر سرور پیش‌فرض غیرفعال است)
/// ServerMonitoring:SchedulerCheckSeconds = 60 (حداقل 15)
/// ServerMonitoring:HardwareDetailIntervalMinutes = 60 (حداقل 15)
/// </summary>
public sealed class ServerMonitoringWorker : BackgroundService
{
    private const string SystemActor = "سیستم پایش سرورها";
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly ILogger<ServerMonitoringWorker> _log;

    public ServerMonitoringWorker(IServiceProvider services, IConfiguration config, ILogger<ServerMonitoringWorker> log)
    {
        _services = services;
        _config = config;
        _log = log;
    }

    private bool Enabled => _config.GetValue<bool?>("ServerMonitoring:Enabled") ?? true;
    private int CheckSeconds => Math.Clamp(_config.GetValue<int?>("ServerMonitoring:SchedulerCheckSeconds") ?? 60, 15, 3600);
    // دریافت جزئیات قطعات از Health اصلی سنگین‌تر است؛ به‌صورت پیش‌فرض هر ساعت انجام می‌شود.
    private int HardwareDetailMinutes => Math.Clamp(_config.GetValue<int?>("ServerMonitoring:HardwareDetailIntervalMinutes") ?? 60, 15, 1440);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // فرصت برای تکمیل DbInitializer و آماده‌شدن کلید Vault.
        try { await Task.Delay(TimeSpan.FromSeconds(75), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (Enabled) await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "خطا در پایش خودکار سرورهای HPE/iLO");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(CheckSeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>یک دور بررسی سرورهای موعدرسیده؛ برای تست داخلی یا اجرای دستی قابل فراخوانی است.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CredentialVaultCrypto>();
        var redfish = scope.ServiceProvider.GetRequiredService<HpeRedfishClient>();
        var alertNotifications = scope.ServiceProvider.GetRequiredService<ServerAlertNotificationService>();
        var now = DateTime.Now;

        // فقط metadata سرورهای فعال خوانده می‌شود؛ سپس موعد هر سرور مستقل از فاصلهٔ خودش محاسبه می‌گردد.
        // در هر دور حداکثر 100 اتصال شبکه‌ای ایجاد می‌کنیم تا به شبکهٔ مدیریت فشار ناگهانی وارد نشود.
        var configured = await db.ManagedServers
            .Where(s => s.MonitoringEnabled && s.Status == "Active" && s.CredentialId != null)
            .ToListAsync(ct);

        var due = configured.Where(s => s.LastAutomaticPollAt is null
                || now - s.LastAutomaticPollAt.Value >= TimeSpan.FromMinutes(NormalizeInterval(s.MonitoringIntervalMinutes)))
            .OrderBy(s => s.LastAutomaticPollAt ?? DateTime.MinValue)
            .Take(100)
            .ToList();
        var completed = 0;

        foreach (var server in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var includeHardwareDetails = server.LastHardwareDetailsAt is null
                    || now - server.LastHardwareDetailsAt.Value >= TimeSpan.FromMinutes(HardwareDetailMinutes);
                await CollectOneAsync(db, crypto, redfish, server, now, includeHardwareDetails, ct);
                await db.SaveChangesAsync(ct);
                await NotifySafelyAsync(alertNotifications, server, now, ct);
                completed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _log.LogError(ex, "پایش خودکار سرور {ServerId} ({ServerName}) ناموفق بود.", server.Id, server.Name);
                // خطای غیرمنتظرهٔ Worker هم به‌شکل هشدار اتصال ثبت می‌شود؛ پیام داخلی Stack Trace ذخیره نمی‌گردد.
                server.LastAutomaticPollAt = now;
                server.LastHealthCheckAt = now;
                server.LastConnectionError = "خطای غیرمنتظره در سرویس پایش؛ لاگ سرور برنامه را بررسی کنید.";
                db.ServerHealthSnapshots.Add(new ServerHealthSnapshot { ManagedServerId = server.Id, CapturedAt = now, OverallHealth = "Unknown", Message = server.LastConnectionError });
                db.ServerOperationLogs.Add(Log(server, "AutoCollect", "Failed", server.LastConnectionError));
                await ServerAlertRules.RaiseAsync(db, server, "Connection", "Critical", server.LastConnectionError, now, ct);
                await db.SaveChangesAsync(ct);
                await NotifySafelyAsync(alertNotifications, server, now, ct);
            }
        }
        return completed;
    }

    private async Task NotifySafelyAsync(ServerAlertNotificationService notifications, ManagedServer server, DateTime now, CancellationToken ct)
    {
        try { await notifications.NotifyDueAsync(server, now, ct); }
        catch (Exception ex)
        {
            // اعلان کانال جانبی است؛ نباید باعث تولید هشدار اتصالِ کاذب یا توقف پایش شود.
            _log.LogWarning(ex, "ارزیابی/ارسال اعلان هشدار سرور {ServerId} ناموفق بود.", server.Id);
        }
    }

    private static int NormalizeInterval(int minutes) => Math.Clamp(minutes, 5, 1440);

    private static async Task CollectOneAsync(AppDbContext db, CredentialVaultCrypto crypto, HpeRedfishClient redfish,
        ManagedServer server, DateTime now, bool includeHardwareDetails, CancellationToken ct)
    {
        var credential = await db.ItCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == server.CredentialId
            && c.CompanyId == server.CompanyId && c.Category == "Server" && c.IsActive, ct);
        RedfishSnapshot snapshot;
        if (credential is null || string.IsNullOrWhiteSpace(credential.Username) || string.IsNullOrWhiteSpace(credential.PasswordCipher))
        {
            snapshot = new RedfishSnapshot { Error = "Credential پایش سرور پیدا نشد، غیرفعال است یا رمز ندارد." };
        }
        else
        {
            try
            {
                var password = crypto.Decrypt(credential.PasswordCipher, credential.CompanyId);
                snapshot = string.IsNullOrEmpty(password)
                    ? new RedfishSnapshot { Error = "رمز Credential پایش قابل خواندن نیست." }
                    : await redfish.GetSnapshotAsync(server.IloAddress, server.IloPort,
                        new RedfishCredentials(credential.Username, password), ct, includeHardwareDetails, server.IloCertThumbprint);
            }
            catch
            {
                snapshot = new RedfishSnapshot { Error = "رمز Credential پایش قابل رمزگشایی نیست؛ کلید گاوصندوق را بررسی کنید." };
            }
        }

        server.LastAutomaticPollAt = now;
        server.LastHealthCheckAt = now;
        if (!snapshot.Success)
        {
            server.LastConnectionError = Limit(snapshot.Error, 1000);
            server.LastHealthMessage = "پایش خودکار Redfish ناموفق بود.";
            db.ServerHealthSnapshots.Add(new ServerHealthSnapshot
            {
                ManagedServerId = server.Id, CapturedAt = now, OverallHealth = "Unknown",
                Message = Limit(snapshot.Error, 500)
            });
            db.ServerOperationLogs.Add(Log(server, "AutoCollect", "Failed", snapshot.Error));
            await ServerAlertRules.RaiseAsync(db, server, "Connection", "Critical", snapshot.Error ?? "اتصال iLO برقرار نشد.", now, ct);
            return;
        }

        server.Manufacturer = Limit(snapshot.Manufacturer, 50) ?? server.Manufacturer;
        server.Model = Limit(snapshot.Model, 150) ?? server.Model;
        server.SerialNumber = Limit(snapshot.SerialNumber, 100) ?? server.SerialNumber;
        server.AssetTag = Limit(snapshot.AssetTag, 100) ?? server.AssetTag;
        server.IloVersion = Limit(snapshot.IloVersion, 80) ?? server.IloVersion;
        server.LastHealthStatus = snapshot.Health;
        server.LastPowerState = Limit(snapshot.PowerState, 30);
        server.LastHealthMessage = "پایش خودکار Redfish انجام شد.";
        server.LastConnectionError = null;
        if (includeHardwareDetails) server.LastHardwareDetailsAt = now;
        server.LastInventoryAt = now;
        server.UpdatedAt = now;
        server.UpdatedByName = SystemActor;

        db.ServerHealthSnapshots.Add(new ServerHealthSnapshot
        {
            ManagedServerId = server.Id, CapturedAt = now, OverallHealth = snapshot.Health,
            PowerState = Limit(snapshot.PowerState, 30), FirmwareVersion = Limit(snapshot.FirmwareVersion, 100),
            BiosVersion = Limit(snapshot.BiosVersion, 100), Model = Limit(snapshot.Model, 150),
            SerialNumber = Limit(snapshot.SerialNumber, 100), Message = "دریافت خودکار از Redfish"
        });
        var hardwareRecord = await ServerHardwareRecorder.RecordAsync(db, server, snapshot, now, ct);
        db.ServerOperationLogs.Add(Log(server, "AutoCollect", "Success", $"Health: {snapshot.Health}; Power: {snapshot.PowerState ?? "Unknown"}; Components: {snapshot.Components.Count}"));

        await ServerAlertRules.ResolveAsync(db, server.Id, "Connection", now, ct);
        var healthIssue = ServerHardwareRecorder.DescribeHealthIssue(snapshot);
        if (healthIssue.Severity is null)
            await ServerAlertRules.ResolveAsync(db, server.Id, "Health", now, ct);
        else
            await ServerAlertRules.RaiseAsync(db, server, "Health", healthIssue.Severity, healthIssue.Message!, now, ct);

        // رخدادهای تازهٔ Critical/Warning به‌صورت مستقل ثبت می‌شوند و خودکار رفع نمی‌شوند؛
        // زیرا Event Log معمولاً تاریخچه است و رفع واقعی باید توسط مسئول تأیید یا Resolve شود.
        var eventIssue = ServerHardwareRecorder.DescribeImportantEvents(hardwareRecord.NewEvents);
        if (eventIssue.Severity is not null)
            await ServerAlertRules.RaiseAsync(db, server, "HardwareEvent", eventIssue.Severity, eventIssue.Message!, now, ct);
    }

    private static ServerOperationLog Log(ManagedServer server, string action, string result, string? details) => new()
    {
        ManagedServerId = server.Id, UserName = SystemActor, Action = action, Result = result,
        Details = Limit(details, 1000), At = DateTime.Now
    };

    private static string? Limit(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}

/// <summary>قواعد رفع/ایجاد هشدار؛ مستقل از Worker تا عملیات دستی API نیز بتواند از آن استفاده کند.</summary>
public static class ServerAlertRules
{
    public static async Task RaiseAsync(AppDbContext db, ManagedServer server, string category, string severity,
        string message, DateTime now, CancellationToken ct = default)
    {
        var alert = await db.ServerAlerts.FirstOrDefaultAsync(a => a.ManagedServerId == server.Id
            && a.Category == category && a.Status != "Resolved", ct);
        if (alert is null)
        {
            db.ServerAlerts.Add(new ServerAlert
            {
                ManagedServerId = server.Id, Category = category, Severity = severity, Message = Limit(message, 1000) ?? "",
                Status = "Open", OpenedAt = now, LastSeenAt = now
            });
            return;
        }

        var escalated = !string.Equals(alert.Severity, "Critical", StringComparison.OrdinalIgnoreCase)
            && string.Equals(severity, "Critical", StringComparison.OrdinalIgnoreCase);
        alert.Severity = severity;
        alert.Message = Limit(message, 1000) ?? "";
        alert.LastSeenAt = now;
        // تأیید هشدار Warning نباید باعث بی‌صدا شدن رخداد Critical بعدی شود.
        var newHardwareEventAfterAcknowledgement = string.Equals(category, "HardwareEvent", StringComparison.OrdinalIgnoreCase)
            && string.Equals(alert.Status, "Acknowledged", StringComparison.OrdinalIgnoreCase);
        if (escalated || newHardwareEventAfterAcknowledgement)
        {
            alert.Status = "Open";
            alert.AcknowledgedAt = null;
            alert.AcknowledgedByUserId = null;
            alert.AcknowledgedByName = null;
            alert.LastNotifiedAt = null;
            alert.NotificationCount = 0;
        }
    }

    public static async Task ResolveAsync(AppDbContext db, int serverId, string category, DateTime now, CancellationToken ct = default)
    {
        var alerts = await db.ServerAlerts.Where(a => a.ManagedServerId == serverId && a.Category == category
            && a.Status != "Resolved").ToListAsync(ct);
        foreach (var alert in alerts)
        {
            alert.Status = "Resolved";
            alert.ResolvedAt = now;
            alert.LastSeenAt = now;
        }
    }

    private static string? Limit(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}
