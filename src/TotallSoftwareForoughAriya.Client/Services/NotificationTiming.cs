namespace Inventory.Client.Services;

/// <summary>
/// مدت نمایش اعلان‌ها و پیام‌های سامانه — سراسری و فقط توسط «مدیر سامانه» از صفحهٔ «تنظیمات» قابل تغییر.
/// مقدار از سرور (AppSettings) خوانده می‌شود و روی همهٔ کاربران و همهٔ نمایش‌ها اعمال می‌گردد:
///   • اعلان زندهٔ بالای صفحه (نوتیف رویدادها مثل تأیید/درخواست/پیام)
///   • پیام‌های کوتاه (موفق/خطا/اطلاع)
/// مقدار ۰ یعنی «تا بستن دستی» — تا خود کاربر دکمهٔ بستن را نزند، پیام می‌ماند.
/// </summary>
public static class NotificationTiming
{
    /// <summary>پیش‌فرض اعلان زندهٔ بالای صفحه: ۲ دقیقه (قبلاً ۱۵ ثانیه بود).</summary>
    public const int DefaultBannerMs = 120_000;

    /// <summary>پیش‌فرض پیام کوتاه (Toast): ۲۰ ثانیه (قبلاً ۴.۲ ثانیه بود).</summary>
    public const int DefaultToastMs = 20_000;

    /// <summary>گزینه‌های اعلان زندهٔ بالای صفحه.</summary>
    public static readonly int[] BannerOptions = { 30_000, 60_000, 120_000, 300_000, 600_000, 1_800_000, 0 };

    /// <summary>گزینه‌های پیام کوتاه (Toast).</summary>
    public static readonly int[] ToastOptions = { 10_000, 20_000, 30_000, 60_000, 180_000, 600_000, 0 };

    public static int BannerMs { get; private set; } = DefaultBannerMs;
    public static int ToastMs { get; private set; } = DefaultToastMs;

    /// <summary>اعلان زنده تا بستن دستی بماند؟</summary>
    public static bool BannerSticky => BannerMs <= 0;

    /// <summary>پیام کوتاه تا بستن دستی بماند؟</summary>
    public static bool ToastSticky => ToastMs <= 0;

    public static void SetBanner(int ms) => BannerMs = Sanitize(ms, BannerOptions, DefaultBannerMs);
    public static void SetToast(int ms) => ToastMs = Sanitize(ms, ToastOptions, DefaultToastMs);

    /// <summary>بارگذاری مقادیر ذخیره‌شدهٔ مرورگر (ورودی نامعتبر نادیده گرفته می‌شود).</summary>
    public static void Load(string? banner, string? toast)
    {
        if (int.TryParse(banner, out var b) && (b == 0 || b >= 5_000) && b <= 3_600_000) BannerMs = b;
        if (int.TryParse(toast, out var t) && (t == 0 || t >= 5_000) && t <= 3_600_000) ToastMs = t;
    }

    /// <summary>بارگذاری مقادیر سراسری از سرور (AppSettings). ۰ = تا بستن دستی؛ مقدار نامعتبر نادیده گرفته می‌شود.</summary>
    public static void LoadServer(int banner, int toast)
    {
        if (banner == 0 || (banner >= 5_000 && banner <= 3_600_000)) BannerMs = banner;
        if (toast == 0 || (toast >= 5_000 && toast <= 3_600_000)) ToastMs = toast;
    }

    /// <summary>برچسب فارسی مدت (مثلاً «۲ دقیقه» یا «تا بستن دستی»).</summary>
    public static string Label(int ms)
    {
        if (ms <= 0) return "تا بستن دستی";
        if (ms % 60_000 == 0) return ToFa(ms / 60_000) + " دقیقه";
        return ToFa(ms / 1000) + " ثانیه";
    }

    private static int Sanitize(int ms, int[] allowed, int fallback)
        => ms == 0 ? 0 : allowed.Contains(ms) ? ms : fallback;

    private static string ToFa(int value) => value.ToString()
        .Replace('0', '۰').Replace('1', '۱').Replace('2', '۲').Replace('3', '۳').Replace('4', '۴')
        .Replace('5', '۵').Replace('6', '۶').Replace('7', '۷').Replace('8', '۸').Replace('9', '۹');
}
