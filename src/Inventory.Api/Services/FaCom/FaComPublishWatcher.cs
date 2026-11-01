namespace Inventory.Api.Services.FaCom;

/// <summary>
/// انتشار زمان‌بندی‌شده اطلاعیه‌ها — هر چند دقیقه (FaCom:PublishCheckMinutes، پیش‌فرض ۵ دقیقه)
/// اطلاعیه‌هایی که تاریخ انتشارشان فرارسیده اعلان سیستمی می‌گیرند (ضدتکرار با NotifiedAt).
/// </summary>
public class FaComPublishWatcher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<FaComPublishWatcher> _log;
    private readonly IConfiguration _cfg;

    public FaComPublishWatcher(IServiceProvider sp, ILogger<FaComPublishWatcher> log, IConfiguration cfg)
    {
        _sp = sp; _log = log; _cfg = cfg;
    }

    private int IntervalMinutes => _cfg.GetValue<int?>("FaCom:PublishCheckMinutes") is int v
        ? Math.Clamp(v, 1, 120) : 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IFaComService>();
                var n = await svc.CheckDueAnnouncementsAsync();
                if (n > 0) _log.LogInformation("انتشار زمان‌بندی‌شده اطلاعیه‌ها: {Count} مورد.", n);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "خطا در انتشار زمان‌بندی‌شده اطلاعیه‌ها");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(IntervalMinutes), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
