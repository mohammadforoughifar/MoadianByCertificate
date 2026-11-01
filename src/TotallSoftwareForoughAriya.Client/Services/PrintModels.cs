namespace Inventory.Client.Services;

/// <summary>
/// گزینه‌های چاپ گزارش جدولی — به تابع جاوااسکریپت <c>printTableReport</c>
/// (فایل <c>wwwroot/js/project-interop.js</c>) پاس داده می‌شود.
/// نام پراپرتی‌ها در JSInterop به‌صورت camelCase سریالایز می‌شوند
/// (Title→title، SubTitle→subTitle، FooterTotal→footerTotal و…) و دقیقاً
/// همان کلیدهایی هستند که تابع JS انتظار دارد.
/// </summary>
public class PrintTableOptions
{
    /// <summary>عنوان اصلی سند (بالای صفحه، کنار نام سامانه)</summary>
    public string Title { get; set; } = "";

    /// <summary>زیرعنوان — معمولاً نام و کد پروژه</summary>
    public string? SubTitle { get; set; }

    /// <summary>چیپ‌های اطلاعات بالای جدول (کارفرما، بازه تاریخ، تعداد و…)</summary>
    public List<string> Meta { get; set; } = new();

    /// <summary>سرستون‌های جدول</summary>
    public List<string> Headers { get; set; } = new();

    /// <summary>سطرهای جدول — طول هر سطر باید با تعداد سرستون‌ها یکی باشد</summary>
    public List<List<string>> Rows { get; set; } = new();

    /// <summary>سطر جمع پایین جدول (اختیاری)</summary>
    public string? FooterTotal { get; set; }

    /// <summary>تاریخ چاپ (شمسی، آمادهٔ نمایش)</summary>
    public string? PrintedAt { get; set; }

    /// <summary>نام کاربر چاپ‌کننده</summary>
    public string? PrintedBy { get; set; }
}
