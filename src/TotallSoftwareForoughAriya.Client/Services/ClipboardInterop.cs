using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>
/// کپیِ متن در کلیپ‌بورد با فالبکِ جاوااسکریپتی.
/// <para>
/// <c>navigator.clipboard</c> فقط در «context امن» کار می‌کند؛ یعنی HTTPS یا localhost.
/// خیلی از نصب‌ها روی شبکهٔ داخلی با آدرس <c>http://192.168.x.x:8181</c> اجرا می‌شوند؛
/// در آن حالت <c>navigator.clipboard</c> وجود ندارد (یا دسترسی رد می‌شود) و فراخوانیِ
/// مستقیمِ <c>navigator.clipboard.writeText</c> استثنا می‌دهد و پیام «کپی ممکن نبود»
/// نمایش داده می‌شد، در حالی که کپی با روش قدیمیِ <c>document.execCommand('copy')</c>
/// کاملاً کار می‌کند.
/// </para>
/// </summary>
public static class ClipboardInterop
{
    /// <summary>اگر تابعِ عمومیِ کپی در صفحه نبود (مثلاً app-shell.js از کشِ قدیمی نیامده)، آن را می‌سازد.</summary>
    private const string EnsureScript = @"
(function () {
    if (typeof window.invCopyText === 'function') return true;
    window.invCopyText = function (text) {
        try {
            if (navigator.clipboard && window.isSecureContext) { navigator.clipboard.writeText(text); return true; }
            var ta = document.createElement('textarea');
            ta.value = text; ta.setAttribute('readonly', '');
            ta.style.position = 'fixed'; ta.style.top = '-1000px'; ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.select(); ta.setSelectionRange(0, ta.value.length);
            var ok = document.execCommand('copy');
            document.body.removeChild(ta);
            return ok;
        } catch (e) { return false; }
    };
    return true;
})()";

    /// <summary>کپیِ متن؛ خروجی false یعنی مرورگر اجازهٔ کپی نداد (باید به کاربر جایگزین نشان داد).</summary>
    public static async Task<bool> CopyAsync(IJSRuntime js, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            await js.InvokeAsync<bool>("eval", EnsureScript);
            return await js.InvokeAsync<bool>("invCopyText", text);
        }
        catch
        {
            // آخرین تلاش: مسیر مستقیم (روی HTTPS کار می‌کند)
            try
            {
                await js.InvokeVoidAsync("navigator.clipboard.writeText", text);
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>انتخابِ خودکار محتوای یک فیلد (برای کپیِ دستی وقتی مرورگر اجازهٔ کپی نمی‌دهد)</summary>
    public static async Task SelectAllAsync(IJSRuntime js, string elementId)
    {
        try
        {
            await js.InvokeVoidAsync("eval",
                $"(function(){{var el=document.getElementById('{elementId}');if(!el)return;el.focus();el.select();el.setSelectionRange(0,(el.value||'').length);}})()");
        }
        catch { }
    }

    /// <summary>اسکرول به یک عنصر و هایلایتِ موقت آن (برای «رفتن به ردیفِ مرجع»)</summary>
    public static async Task ScrollToAsync(IJSRuntime js, string elementId)
    {
        try
        {
            await js.InvokeVoidAsync("eval",
                $"(function(){{var el=document.getElementById('{elementId}');if(!el)return false;try{{el.scrollIntoView({{behavior:'smooth',block:'center'}});}}catch(e){{el.scrollIntoView();}}return true;}})()");
        }
        catch { }
    }
}
