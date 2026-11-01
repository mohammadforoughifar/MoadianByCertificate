using Inventory.Api.Services.HrTalent;

namespace Inventory.Api.Services.HrCore;

/// <summary>
/// سرویس پس‌زمینه‌ی روزانه‌ی «عملیات خودکار HR» که پیش‌تر فقط با دکمه‌ی دستی اجرا می‌شدند:
///   ۱) بررسی و هشدار انقضای قرارداد (IHrCoreService.CheckAlertsAsync) — یادآوری به کارمند/پیامک طبق آستانه‌ی روز پیکربندی‌شده.
///   ۲) ایجاد خودکار دوره‌ی آزمایشی برای استخدام‌های اخیر (IHrTalentService.AutoCreateTrialsAsync).
///   ۳) اجرای خودکار احکامِ رسیده‌به‌تاریخ (IHrCoreService.ApplyDueDecreesAsync) — تغییر پست/واحد/حقوق پایه
///      روی پرونده اعمال می‌شود تا دوره‌ی حقوقی بعدی (FaPay) خودکار مبلغ جدید را بردارد.
/// هر روز ساعت مشخص (پیش‌فرض ۷ صبح، قابل تنظیم با HrCore:DailyOpsCheckHour) اجرا می‌شود.
/// </summary>
public class HrOpsDailyWatcher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<HrOpsDailyWatcher> _log;
    private readonly IConfiguration _cfg;

    public HrOpsDailyWatcher(IServiceProvider sp, ILogger<HrOpsDailyWatcher> log, IConfiguration cfg)
    {
        _sp = sp; _log = log; _cfg = cfg;
    }

    private int RunAtHour => _cfg.GetValue<int?>("HrCore:DailyOpsCheckHour") ?? 7;

    /// <summary>آستانه‌ی روزهای مانده تا پایان قرارداد برای هشدار (پیش‌فرض ۳۰ روز).</summary>
    private int ContractAlertDays => Math.Max(1, _cfg.GetValue<int?>("HrCore:ContractAlertDays") ?? 30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();

                var hrCore = scope.ServiceProvider.GetRequiredService<IHrCoreService>();
                var contractAlerts = await hrCore.CheckAlertsAsync(ContractAlertDays);
                if (contractAlerts > 0)
                    _log.LogInformation("هشدار انقضای قرارداد: {Count} قرارداد جدید در آستانه‌ی {Days} روز.", contractAlerts, ContractAlertDays);

                var talent = scope.ServiceProvider.GetRequiredService<IHrTalentService>();
                var trialsCreated = await talent.AutoCreateTrialsAsync();
                if (trialsCreated > 0)
                    _log.LogInformation("دوره‌ی آزمایشی خودکار: {Count} پرونده‌ی جدید ایجاد شد.", trialsCreated);

                var decreesApplied = await hrCore.ApplyDueDecreesAsync();
                if (decreesApplied > 0)
                    _log.LogInformation("اجرای خودکار احکام رسیده‌به‌تاریخ: {Count} حکم روی پرونده پرسنل اعمال شد.", decreesApplied);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "خطا در بررسی روزانه‌ی عملیات HR (هشدار قرارداد / دوره آزمایشی)");
            }

            var now = DateTime.Now;
            var next = now.Date.AddHours(RunAtHour);
            if (next <= now) next = next.AddDays(1);

            try { await Task.Delay(next - now, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
