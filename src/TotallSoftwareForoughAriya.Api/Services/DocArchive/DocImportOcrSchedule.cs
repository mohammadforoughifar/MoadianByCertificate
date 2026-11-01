namespace Inventory.Api.Services.DocArchive;

/// <summary>تنظیمات متن‌گیری شبانهٔ فایل‌های ورود انبوه.</summary>
public class DocImportOcrOptions
{
    public const string SectionName = "DocImportOcr";

    /// <summary>تا روشن نشود، فایل‌های ورود انبوه متن‌گیری نمی‌شوند.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>آغاز پنجرهٔ شبانه (ساعت محلی سرور).</summary>
    public string StartTime { get; set; } = "22:00";

    /// <summary>پایان پنجرهٔ شبانه (ساعت محلی سرور).</summary>
    public string EndTime { get; set; } = "06:00";

    /// <summary>چند فایل در هر دسته — بین دسته‌ها مکث می‌آید تا سرویس OCR اشباع نشود.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>مکث بین دو دسته (میلی‌ثانیه).</summary>
    public int PauseBetweenBatchesMs { get; set; } = 5000;
}

/// <summary>
/// متن‌گیری شبانهٔ پیوست‌های ورود انبوه.
///
/// ورود انبوه عمداً OCR نمی‌کند تا ورود ۵۰ هزار فایل چند روز طول نکشد. این کارگر
/// خارج از ساعت کاری، همان فایل‌ها را آرام‌آرام متن‌گیری می‌کند تا جست‌وجوی داخل
/// متنشان کار کند. خارج از پنجرهٔ شبانه هیچ کاری نمی‌کند.
/// </summary>

/// <summary>
/// منطق تعیین پنجرهٔ زمانی متن‌گیری شبانه. جدا از کارگر است تا بدون بالا آوردن
/// برنامه قابل تست باشد.
/// </summary>
public static class DocImportOcrSchedule
{
    /// <summary>آیا ساعت داده‌شده داخل پنجرهٔ شبانه است؟ پنجره از نیمه‌شب هم رد می‌شود.</summary>
    public static bool InWindow(DocImportOcrOptions opt, DateTime now)
    {
        var start = ParseTime(opt.StartTime, 22 * 60);
        var end = ParseTime(opt.EndTime, 6 * 60);
        var minutes = now.Hour * 60 + now.Minute;

        return start <= end
            ? minutes >= start && minutes < end      // پنجرهٔ داخل یک روز
            : minutes >= start || minutes < end;     // پنجرهٔ ردشده از نیمه‌شب
    }

    /// <summary>«ساعت:دقیقه» → دقیقه از آغاز روز. مقدار نامعتبر → پیش‌فرض.</summary>
    public static int ParseTime(string? value, int fallbackMinutes)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallbackMinutes;
        var parts = value.Trim().Split(':');
        if (parts.Length != 2) return fallbackMinutes;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return fallbackMinutes;
        if (h < 0 || h > 23 || m < 0 || m > 59) return fallbackMinutes;
        return h * 60 + m;
    }
}
