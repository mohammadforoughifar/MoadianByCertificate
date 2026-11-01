namespace Inventory.Api.Services.FaCom;

/// <summary>
/// یادآوری سالگرد استخدام (سنوات) پرسنل — روزی یک‌بار (ساعت FaCom:AnniversaryCheckHour، پیش‌فرض ۸ صبح).
/// بدون نیاز به جدول ضدتکرار (مبنای تاریخ است؛ هر سالگرد سالی یک‌بار رخ می‌دهد).
/// </summary>
public class FaComAnniversaryWatcher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<FaComAnniversaryWatcher> _log;
    private readonly IConfiguration _cfg;

    public FaComAnniversaryWatcher(IServiceProvider sp, ILogger<FaComAnniversaryWatcher> log, IConfiguration cfg)
    {
        _sp = sp; _log = log; _cfg = cfg;
    }

    private int RunAtHour => _cfg.GetValue<int?>("FaCom:AnniversaryCheckHour") ?? 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(35), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IFaComService>();
                var n = await svc.CheckWorkAnniversariesAsync();
                if (n > 0) _log.LogInformation("یادآوری سالگرد همکاری: {Count} نفر.", n);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "خطا در یادآوری سالگرد همکاری");
            }

            var now = DateTime.Now;
            var next = now.Date.AddHours(RunAtHour);
            if (next <= now) next = next.AddDays(1);

            try { await Task.Delay(next - now, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
