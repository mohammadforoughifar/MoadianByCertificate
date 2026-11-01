using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Office;

/// <summary>
/// پایش زمان‌بندی‌شدهٔ SNMP ماشین‌های اداری؛ فقط دستگاه‌های شبکه‌ای که SnmpEnabled دارند و
/// «خارج از رده» نیستند، طبق PollIntervalMinutes همان دستگاه بررسی می‌شوند.
///
/// تنظیمات اختیاری (appsettings.json):
///   OfficeMachineMonitoring:Enabled = true|false        (پیش‌فرض true)
///   OfficeMachineMonitoring:SchedulerCheckSeconds = 60  (۱۵ تا ۳۶۰۰)
///   OfficeMachineMonitoring:MaxParallel = 8             (۱ تا ۳۲)
///   OfficeMachineMonitoring:SnmpTimeoutMs = 2000
///   OfficeMachineMonitoring:SnmpRetries = 1
///   OfficeMachineMonitoring:OfflineGraceMinutes = 15
///   OfficeMachineMonitoring:CounterRetentionDays = 730
/// </summary>
public sealed class OfficeMachineMonitorWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly ILogger<OfficeMachineMonitorWorker> _log;
    private DateTime _lastCleanup = DateTime.MinValue;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public OfficeMachineMonitorWorker(IServiceProvider services, IConfiguration config, ILogger<OfficeMachineMonitorWorker> log)
    {
        _services = services; _config = config; _log = log;
    }

    private bool Enabled => _config.GetValue<bool?>("OfficeMachineMonitoring:Enabled") ?? true;
    private int CheckSeconds => Math.Clamp(_config.GetValue<int?>("OfficeMachineMonitoring:SchedulerCheckSeconds") ?? 60, 15, 3600);
    private int MaxParallel => Math.Clamp(_config.GetValue<int?>("OfficeMachineMonitoring:MaxParallel") ?? 8, 1, 32);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // فرصت برای تکمیل DbInitializer و آماده‌شدن کلید گاوصندوق
        try { await Task.Delay(TimeSpan.FromSeconds(90), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (Enabled) await RunOnceAsync(force: false, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { _log.LogError(ex, "خطا در پایش خودکار ماشین‌های اداری"); }

            try { await Task.Delay(TimeSpan.FromSeconds(CheckSeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>یک دور پایش؛ با force=true همهٔ دستگاه‌های SNMP بدون توجه به موعد بررسی می‌شوند.</summary>
    public async Task<int> RunOnceAsync(bool force, CancellationToken ct = default)
    {
        if (!await _runLock.WaitAsync(0, ct)) return 0; // دور قبلی هنوز تمام نشده
        try
        {
            List<int> due;
            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.Now;
                var candidates = await db.OfficeMachines.AsNoTracking()
                    .Where(m => m.SnmpEnabled && m.ConnectionType == "Network" && m.IpAddress != null && m.IpAddress != "" && m.Status != "Retired")
                    .Select(m => new { m.Id, m.LastPollAt, m.PollIntervalMinutes })
                    .ToListAsync(ct);
                due = candidates
                    .Where(m => force || m.LastPollAt == null
                        || now - m.LastPollAt.Value >= TimeSpan.FromMinutes(Math.Clamp(m.PollIntervalMinutes, 2, 1440)))
                    .OrderBy(m => m.LastPollAt ?? DateTime.MinValue)
                    .Take(500)
                    .Select(m => m.Id).ToList();
                await CleanupAsync(db, ct);

                // چاپگرهای USB پایش‌شده با ایجنت: اگر سیستم میزبان مدتی گزارش نداده ← «نامعلوم»
                try { await scope.ServiceProvider.GetRequiredService<OfficeMachineMonitor>().MarkStaleAgentMachinesAsync(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogDebug(ex, "بررسی ایجنت‌های بی‌خبر ناموفق بود"); }
            }
            if (due.Count == 0) return 0;

            using var gate = new SemaphoreSlim(MaxParallel);
            var tasks = due.Select(async id =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    using var scope = _services.CreateScope();
                    var monitor = scope.ServiceProvider.GetRequiredService<OfficeMachineMonitor>();
                    await monitor.PollAsync(id, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch (Exception ex) { _log.LogWarning(ex, "پایش ماشین اداری {Id} ناموفق بود", id); }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks);
            return due.Count;
        }
        finally { _runLock.Release(); }
    }

    private async Task CleanupAsync(AppDbContext db, CancellationToken ct)
    {
        if (DateTime.Now - _lastCleanup < TimeSpan.FromHours(12)) return;
        _lastCleanup = DateTime.Now;
        try
        {
            var keepDays = Math.Clamp(_config.GetValue<int?>("OfficeMachineMonitoring:CounterRetentionDays") ?? 730, 30, 3650);
            var counterCut = DateTime.Now.Date.AddDays(-keepDays);
            var eventCut = DateTime.Now.AddDays(-365);
            await db.OfficeMachineCounters.Where(c => c.Day < counterCut).ExecuteDeleteAsync(ct);
            await db.OfficeMachineEvents.Where(e => e.IsResolved && e.ResolvedAt < eventCut).ExecuteDeleteAsync(ct);
        }
        catch (Exception ex) { _log.LogDebug(ex, "پاک‌سازی داده‌های قدیمی پایش ماشین‌ها ناموفق بود"); }
    }
}
