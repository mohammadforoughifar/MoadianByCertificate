using System.Globalization;

namespace Inventory.Client.Extensions;

/// <summary>
/// مستطیل یک المان در مختصات viewport — از تابع JS «uiRect» (wwwroot/js/ui.js) دریافت می‌شود.
/// </summary>
public sealed record UiRect(double Left, double Top, double Right, double Bottom, double Width, double Height, double Vw, double Vh);

/// <summary>
/// محاسبه موقعیت منوهای شناور (تقویم DatePicker و منوی کمبوباکس‌ها).
///
/// چرا این الگو؟ منوی absolute داخل فرم، زیر «کارت‌ویو» و کارت‌های بعدی می‌رود
/// (هر والد دارای transform/filter/backdrop-filter یک stacking context مستقل می‌سازد و
/// overflow والد هم منو را می‌بُرد). راه‌حل — همان الگوی تأییدشده‌ی «منوی ⋮ سطرها» و
/// «فیلتر تاریخ سرستون» (DateFilterInput): منو به‌صورت position:fixed در مختصات viewport
/// رندر می‌شود و یک پوشش نامرئی تمام‌صفحه (pop-overlay) کلیک بیرون را می‌گیرد.
/// نتیجه: منو هیچ‌گاه زیر کارت‌ها نمی‌رود و در مودال و داخل جدول اسکرول‌دار هم درست است.
/// </summary>
public static class Popover
{
    /// <summary>z-index پوشش نامرئی — بالاتر از مودال بوت‌استرپ (1055)</summary>
    public const int OverlayZ = 4490;

    /// <summary>z-index خود منو — یک لایه بالای پوشش</summary>
    public const int MenuZ = 4500;

    /// <summary>
    /// بهترین جای قرارگیری منو زیر (یا در نبود جا، بالای) المانِ مرجع.
    /// width = 0 یعنی حداقلِ عرضِ المان مرجع (حداقل 230px).
    /// </summary>
    public static (double Top, double Left, double Width) Below(
        UiRect r, double width = 0, double maxMenuHeight = 264)
    {
        var w = width > 0 ? width : Math.Max(r.Width, 230);
        w = Math.Min(w, Math.Max(240, r.Vw - 16));

        var left = Math.Clamp(r.Left, 8, Math.Max(8, r.Vw - w - 8));

        // پیش‌فرض: زیر المان؛ اگر تا پایین صفحه جا نیست، بالای المان
        var top = r.Bottom + 4;
        if (top + maxMenuHeight > r.Vh - 8)
            top = Math.Max(8, r.Top - maxMenuHeight - 4);

        return (Math.Round(top), Math.Round(left), Math.Round(w));
    }

    /// <summary>استایل inline منوی شناور (position:fixed + مختصات + z)</summary>
    public static string FixedStyle(double top, double left, double width, double maxHeight = 0)
    {
        var s = FormattableString.Invariant(
            $"position:fixed;top:{top:0}px;left:{left:0}px;right:auto;width:{width:0}px;")
            + FormattableString.Invariant(
            $"z-index:{MenuZ};max-width:calc(100vw - 16px);");
        return maxHeight > 0
            ? s + FormattableString.Invariant($"max-height:{maxHeight:0}px;")
            : s;
    }

    /// <summary>
    /// استایل تقویم DatePicker بر اساس نقطه‌ی کلیک (بدون نیاز به JS) —
    /// با max/min در CSS داخل خود viewport بند می‌شود تا از لبه بیرون نزند.
    /// </summary>
    public static string CalendarStyle(double clickX, double clickY) =>
        FormattableString.Invariant(
            $"position:fixed;top:max(8px,min({clickY + 8:0}px,calc(100vh - 340px)));")
        + FormattableString.Invariant(
            $"left:max(8px,min({clickX - 40:0}px,calc(100vw - 300px)));right:auto;")
        + FormattableString.Invariant(
            $"z-index:{MenuZ};max-width:calc(100vw - 16px);max-height:calc(100vh - 24px);overflow:auto;");
}
