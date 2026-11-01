using Inventory.Shared.Security;
using Microsoft.AspNetCore.Components;

namespace Inventory.Client.Services;

/// <summary>
/// پاک‌سازی HTML متن نامه را «یک‌بار» انجام می‌دهد و نتیجه را برای رندرهای بعدی نگه می‌دارد.
///
/// <para>
/// چرا لازم شد: <see cref="RichLetterHtml.Sanitize"/> یک پاک‌سازیِ regex-محور است و برای
/// هر پاراگراف چند پاس regex روی کل رشته می‌زند.وقتی این متد داخل درخت رندر (مثلاً
/// <c>@((MarkupString)RichLetterHtml.Sanitize(letter.Description))</c>) صدا زده می‌شود،
/// Blazor آن را در <b>هر</b> رندر دوباره اجرا می‌کند — و متن نامه‌های برگرفته از ایمیل
/// می‌تواند چند مگابایت باشد (تصاویر درون‌متن به‌صورت base64). نتیجه: هر کلیک، هر
/// StateHasChanged یا هر رویداد دیگر، ثانیه‌ها پردازش regex روی رشتهٔ چندمگابایتی و
/// حس «سنگین بودن» صفحه.
/// </para>
///
/// <para>
/// راه‌حل: نتیجه به‌ازای «همان نمونهٔ رشته» کش می‌شود. چون کامپوننت در هر رندر همان
/// نمونهٔ <c>activeDetail.Description</c> را پاس می‌دهد، مقایسه با
/// <see cref="object.ReferenceEquals(object?, object?)"/> کافی و ارزان است؛ به‌محضِ
/// بارگذاری نامهٔ دیگر (یا ویرایش متن) نمونه عوض می‌شود و پاک‌سازی دوباره انجام می‌گیرد.
/// </para>
/// </summary>
public sealed class SanitizedLetterBody
{
    private string? _source;
    private MarkupString _rendered;

    /// <summary>نسخهٔ امنِ HTML برای رندر؛ فقط وقتی ورودی عوض شده باشد محاسبه می‌شود.</summary>
    public MarkupString Get(string? html)
    {
        if (!ReferenceEquals(_source, html))
        {
            _source = html;
            _rendered = new MarkupString(RichLetterHtml.Sanitize(html));
        }

        return _rendered;
    }

    /// <summary>پاک کردن کش — هنگام بستن نامه یا باز کردن نامهٔ تازه.</summary>
    public void Reset()
    {
        _source = null;
        _rendered = default;
    }
}
