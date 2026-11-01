namespace Inventory.Api.Services;

/// <summary>
/// سرویس پس‌زمینه‌ی ربات بله:
/// هر چند ثانیه یک‌بار آپدیت‌های ربات را می‌خواند تا
///  • کاربرانی که ربات را «/start» می‌کنند پیام خوش‌آمد + دکمه‌ی «ارسال شماره من» بگیرند،
///  • شماره‌های به‌اشتراک‌گذاشته‌شده به‌صورت خودکار با موبایل کاربران تطبیق داده و ذخیره شوند
///    (بدون نیاز به زدن دستی دکمه‌ی «همگام‌سازی بله»).
/// اگر توکن بله در تنظیمات خالی باشد، این سرویس کاری انجام نمی‌دهد.
/// </summary>
public class BaleBotWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BaleBotWorker> _log;
    private long _offset;

    public BaleBotWorker(IServiceScopeFactory scopes, ILogger<BaleBotWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // کمی صبر تا سرویس کامل بالا بیاید
        try { await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var messenger = scope.ServiceProvider.GetRequiredService<IMessengerService>();
                var (linked, maxUpdateId, note) = await messenger.PollBaleAsync(_offset);

                if (maxUpdateId > 0) _offset = maxUpdateId + 1;

                if (linked > 0)
                    _log.LogInformation("ربات بله: {Count} کاربر متصل شد.", linked);
                else if (maxUpdateId == 0 && note != "پیام تازه‌ای از کاربران دریافت نشد." && !note.Contains("توکن"))
                    _log.LogDebug("بله: {Note}", note);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "خطا در خواندن آپدیت‌های بله");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch { break; }
        }
    }
}
