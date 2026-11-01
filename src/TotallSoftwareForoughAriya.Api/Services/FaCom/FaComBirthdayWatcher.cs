namespace Inventory.Api.Services.FaCom;

/// <summary>
/// یادآوری تولد پرسنل — روزی یک‌بار (ساعت FaCom:BirthdayCheckHour، پیش‌فرض ۸ صبح).
/// بدون نیاز به جدول ضدتکرار (مبنای تاریخ است؛ هر تولد سالی یک‌بار).
/// </summary>
public class FaComBirthdayWatcher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<FaComBirthdayWatcher> _log;
    private readonly IConfiguration _cfg;

    public FaComBirthdayWatcher(IServiceProvider sp, ILogger<FaComBirthdayWatcher> log, IConfiguration cfg)
    {
        _sp = sp; _log = log; _cfg = cfg;
    }

    private int RunAtHour => _cfg.GetValue<int?>("FaCom:BirthdayCheckHour") ?? 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IFaComService>();
                var n = await svc.CheckBirthdaysAsync();
                if (n > 0) _log.LogInformation("یادآوری تولد: {Count} نفر.", n);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "خطا در یادآوری تولد");
            }

            var now = DateTime.Now;
            var next = now.Date.AddHours(RunAtHour);
            if (next <= now) next = next.AddDays(1);

            try { await Task.Delay(next - now, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
