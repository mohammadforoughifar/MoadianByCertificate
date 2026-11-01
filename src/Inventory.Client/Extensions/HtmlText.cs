using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Inventory.Client.Extensions;

/// <summary>
/// ابزار تبدیل محتوای HTML (نامه، ایمیل، مدرک) به متن خوانا.
/// <para>
/// چرا لازم است: شرح «دستور کار» با ویرایشگر متن غنی پر می‌شود و به‌صورت HTML رندر
/// می‌شود؛ ولی متن نامه‌ها — مخصوصاً نامهٔ ساخته‌شده از ایمیل دریافتی — خودش HTML است.
/// اگر آن را با <c>HtmlEncode</c> داخل شرح بریزیم، همهٔ تگ‌ها به شکل
/// <c>&amp;lt;p&amp;gt;</c> روی صفحه ظاهر می‌شوند (همان باگ «متن نامه در دستور کار
/// به‌هم‌ریخته است»). راه درست: اول HTML را به متن ساده و خوانا تبدیل کنیم، سپس
/// متن را encode و با <c>&lt;br /&gt;</c> به HTML امن برگردانیم.
/// </para>
/// </summary>
public static class HtmlText
{
    /// <summary>تبدیل HTML به متن ساده (بدون تگ، با شکست خط طبیعی و رمزگشایی موجودیت‌ها).</summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var s = html;

        // ۱) بلوک‌های بی‌مصرف در متن نهایی
        s = Regex.Replace(s, @"<(script|style|head)\b[^>]*>.*?</\1\s*>", " ",
                          RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<!--.*?-->", " ", RegexOptions.Singleline);

        // ۲) شکست خط در محل تگ‌های بلوکی (تا پاراگراف‌ها و ردیف‌های جدول به هم نچسبند)
        s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</\s*(p|div|tr|li|ul|ol|table|tbody|thead|blockquote|h[1-6]|section|article)\s*>", "\n",
                          RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*li\b[^>]*>", "• ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</\s*(td|th)\s*>", " ", RegexOptions.IgnoreCase);

        // ۳) حذف بقیهٔ تگ‌ها
        s = Regex.Replace(s, @"<[^>]+>", " ");

        // ۴) رمزگشایی موجودیت‌ها + حذف تگ‌هایی که «بعد از» رمزگشایی آشکار می‌شوند.
        // دادهٔ قدیمی ممکن است یک‌بار یا دوبار encode شده باشد (مثلاً بدنهٔ متنیِ ایمیلی
        // که خودش تگ HTML داشت و با HtmlEncode ذخیره شده بود). در آن حالت، تگ‌ها فقط بعد
        // از رمزگشایی پیدا می‌شوند؛ اگر همان‌جا حذف نشوند، در متن دستور کار به شکل
        // <p> و &lt;p&gt; روی صفحه ظاهر می‌شوند (همان «متن به‌هم‌ریخته»).
        for (var pass = 0; pass < 2; pass++)
        {
            var before = s;
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</\s*(p|div|tr|li|ul|ol|table|tbody|thead|blockquote|pre|h[1-6]|section|article)\s*>", "\n",
                              RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<\s*/?\s*[a-zA-Z][^>]*>", " ");
            if (s == before) break;
        }

        // ۵) تمیزکاری فاصله‌ها و خطوط خالی تکراری
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = s.Split('\n')
                     .Select(l => Regex.Replace(l, @"[ \t\u00A0]+", " ").Trim())
                     .ToList();

        var sb = new StringBuilder();
        var emptyRun = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                emptyRun++;
                if (emptyRun > 1) continue;   // بیش از یک خط خالی پشت‌سرهم لازم نیست
            }
            else emptyRun = 0;

            sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// متن خوانای یک محتوای HTML، به‌صورت HTML امن (پاراگراف + شکست خط) — مناسب ریختن
    /// در ویرایشگر شرح دستور کار یا هر فیلد غنی دیگر.
    /// </summary>
    public static string ToSafeHtml(string? html)
    {
        var text = ToPlainText(html);
        if (text.Length == 0) return "";
        return "<p>" + WebUtility.HtmlEncode(text).Replace("\n", "<br />") + "</p>";
    }

    /// <summary>
    /// محتوای یک نامه را برای نمایش داخل یک فیلد غنی (مثل شرح دستور کار) آماده می‌کند.
    /// <para>
    /// برخلاف <see cref="ToSafeHtml"/> که همیشه متن ساده می‌دهد، اینجا اگر متن نامه واقعاً
    /// HTML باشد (نامهٔ داخلی/صادره که با ویرایشگر غنی نوشته شده) قالب‌بندی سبک آن حفظ
    /// می‌شود: تگ‌های خطرناک (script/style/iframe/embed) و هندلرهای رویداد و لینک‌های
    /// javascript حذف می‌شوند و اگر متن به‌اشتباه escape شده باشد یک‌بار رمزگشایی می‌شود.
    /// اگر متن هیچ تگی نداشته باشد (متن ساده با شکست خط)، به پاراگراف امن تبدیل می‌شود.
    /// </para>
    /// </summary>
    public static string ToDisplayHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var s = html;

        // متنِ escape‌شده (مثل &amp;lt;p&amp;gt;) یا دوبار escape‌شده را به HTML واقعی برگردان
        if (!Regex.IsMatch(s, @"<\s*/?\s*[a-zA-Z][^>]*>"))
        {
            if (s.Contains("&lt;") || s.Contains("&gt;") || s.Contains("&amp;lt;"))
            {
                s = WebUtility.HtmlDecode(s);
                if (s.Contains("&lt;") || s.Contains("&amp;")) s = WebUtility.HtmlDecode(s);
            }
        }

        // بلوک‌های خطرناک
        s = Regex.Replace(s, @"<\s*(script|style|iframe|object|embed|head)\b[^>]*>.*?<\s*/\s*\1\s*>", " ",
                          RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<\s*/?\s*(script|style|iframe|object|embed|head)\b[^>]*>", " ",
                          RegexOptions.IgnoreCase);

        // هندلرهای رویداد (onclick، onerror، …) و لینک‌های javascript:
        s = Regex.Replace(s, "\\s+on[a-zA-Z]+\\s*=\\s*(\"[^\"]*\"|'[^']*'|[^\\s>]+)", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "(href|src)\\s*=\\s*([\"'])\\s*javascript:[^\"']*\\2", "$1=\"#\"", RegexOptions.IgnoreCase);

        // متن سادهٔ بدون تگ: پاراگراف امن با شکست خط
        if (!Regex.IsMatch(s, @"<\s*/?\s*[a-zA-Z][^>]*>"))
            return "<p>" + WebUtility.HtmlEncode(s).Replace("\r\n", "\n").Replace("\n", "<br />") + "</p>";

        return s.Trim();
    }

    /// <summary>مقدارِ data URI داخل src — همان چیزی که هم متن را سنگین می‌کند و هم پاک‌سازی regex را فریب می‌دهد.</summary>
    private static readonly Regex DataUriValue = new(
        @"data:[A-Za-z0-9+/=;,:._\-]{20,}", RegexOptions.Compiled);

    /// <summary>
    /// همان <see cref="ToDisplayHtml"/> ولی با «بودجهٔ حجم» — برای محتوایی که در فیلد
    /// غنی دیگری (مثل شرح دستور کار) کپی می‌شود.
    /// <para>
    /// چرا: متن نامهٔ ساخته‌شده از ایمیل، تصاویر را به‌صورت data URI <b>داخل خود متن</b>
    /// نگه می‌دارد (تنها راهی که تصویر بدون هدر Bearer هم نمایش داده شود). یک تصویر
    /// معمولی می‌تواند چند صد کیلوبایت base64 شود؛ اگر بدون سقف کپی شوند، شرح دستور کار
    /// چند مگابایتی می‌شود. اینجا تصاویر از بزرگ به کوچک حذف می‌شوند تا محتوا در بودجه
    /// بماند و در انتها یک یادداشت فارسی اضافه می‌شود.
    /// </para>
    /// <para>
    /// نکتهٔ مهم امنیتی/کیفی: پیش از پاک‌سازی، خودِ دادهٔ base64 موقتاً کنار گذاشته می‌شود؛
    /// وگرنه قواعد «حذف هندلرهای رویداد» ممکن است توالی‌هایی مثل <c>on…=</c> را وسط
    /// base64 ببینند و تصویر را خراب کنند.
    /// </para>
    /// </summary>
    /// <param name="html">HTML مبدأ (متن نامه)</param>
    /// <param name="maxChars">سقف طول خروجی (پیش‌فرض ۴۰۰٬۰۰۰ نویسه)</param>
    public static string ToDisplayHtmlWithinBudget(string? html, int maxChars = 400_000)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        // ۱) دادهٔ base64 را موقتاً با یک نشانهٔ کوتاه عوض کن
        var stash = new List<string>();
        var shielded = DataUriValue.Replace(html, m =>
        {
            stash.Add(m.Value);
            return "\u0001" + (stash.Count - 1).ToString() + "\u0001";
        });

        // ۲) پاک‌سازی معمول روی متنِ سبک‌شده
        var s = ToDisplayHtml(shielded);

        // ۳) تصاویر را برگردان
        for (var i = 0; i < stash.Count; i++)
            s = s.Replace("\u0001" + i.ToString() + "\u0001", stash[i]);

        if (s.Length <= maxChars) return s;

        // ۴) اگر از بودجه بزرگ‌تر است، تصاویر را از بزرگ به کوچک حذف کن
        var dropped = 0;
        var dataImg = new Regex(@"<img\b[^>]*?src\s*=\s*[""']data:[^""']*[""'][^>]*>",
                                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        while (s.Length > maxChars)
        {
            var matches = dataImg.Matches(s);
            if (matches.Count == 0) break;
            var largest = matches.Cast<Match>().OrderByDescending(m => m.Length).First();
            s = s.Remove(largest.Index, largest.Length);
            dropped++;
        }

        if (dropped > 0)
            s += "<p style=\"color:#64748b;font-size:12px\">"
               + $"({dropped} تصویر نامه به‌دلیل حجم زیاد در شرح درج نشد؛ تصاویر کامل در پیوست‌های نامه موجود است.)"
               + "</p>";

        // ۵) اگر باز هم بلندتر از سقف است (متن خیلی طولانی بدون تصویر) از انتها کوتاه کن
        if (s.Length > maxChars + 400)
            s = s[..maxChars] + "… <span class=\"fs-9 text-muted\">(متن نامه کوتاه شد)</span>";

        return s;
    }
}
