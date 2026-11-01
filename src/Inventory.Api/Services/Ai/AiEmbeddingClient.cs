using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// کلاینت امبدینگ (بردار معنایی متن) — برای جستجوی معنایی راهنما.
// قرارداد OpenAI سازگار با Ollama: POST /embeddings
// اگر مدل در دسترس نباشد null برمی‌گردد و جستجو به حالت کلیدواژه‌ای می‌رود.
// =====================================================================

public interface IAiEmbeddingClient
{
    /// <summary>بردار متن را برمی‌گرداند؛ در صورت خطا null (بدون exception).</summary>
    Task<float[]?> EmbedAsync(string text, CancellationToken ct);
}

public class OpenAiCompatibleEmbeddingClient : IAiEmbeddingClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiCompatibleEmbeddingClient> _log;

    public OpenAiCompatibleEmbeddingClient(
        IHttpClientFactory httpFactory,
        IOptions<AiOptions> options,
        ILogger<OpenAiCompatibleEmbeddingClient> log)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _log = log;
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var http = _httpFactory.CreateClient("ai");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(90));
            var payload = new { model = _options.EmbeddingModel, input = text.Trim() };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_options.NormalizedBaseUrl}/embeddings");
            req.Content = JsonContent.Create(payload);
            var key = (_options.ApiKey ?? "").Trim();
            if (key.Length > 0)
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var resp = await http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) return null;
            if (!data[0].TryGetProperty("embedding", out var emb) || emb.ValueKind != JsonValueKind.Array) return null;
            var vec = new float[emb.GetArrayLength()];
            var i = 0;
            foreach (var v in emb.EnumerateArray())
                vec[i++] = (float)v.GetDouble();
            return vec.Length > 0 ? vec : null;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "امبدینگ ناموفق بود؛ جستجوی کلیدواژه‌ای استفاده می‌شود.");
            return null;
        }
    }
}

// =====================================================================
// ابزارهای برداری و متنی مشترک
// =====================================================================
public static class AiVectorMath
{
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}

public static class AiTextUtil
{
    /// <summary>نرمال‌سازی فارسی: ی/ک عربی، نیم‌فاصله‌ها و فاصله‌های اضافه.</summary>
    public static string NormalizeFa(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = text.Replace('ي', 'ی').Replace('ك', 'ک').Replace('‌', ' ');
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    /// <summary>توکن‌های معنادار متن (حذف کلمات خیلی کوتاه).</summary>
    public static HashSet<string> Tokens(string? text)
    {
        var set = new HashSet<string>();
        foreach (var w in NormalizeFa(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = w.Trim('،', '؛', '؟', '.', ',', '!', ':', '«', '»', '"', '\'', '(', ')');
            if (t.Length >= 2) set.Add(t);
        }
        return set;
    }

    /// <summary>حذف تگ‌های HTML برای دادن متن تمیز به مدل.</summary>
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = System.Text.RegularExpressions.Regex.Replace(html, @"<(br|p|div|li|tr)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<[^>]+>", " ");
        s = System.Net.WebUtility.HtmlDecode(s);
        return System.Text.RegularExpressions.Regex.Replace(s, @"[ \t]+", " ").Trim();
    }

    /// <summary>حذف لینک‌های مارک‌داون (برای بله که لینک داخلی باز نمی‌کند): [متن](مسیر) ← متن.</summary>
    public static string StripLinks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");
    }

    /// <summary>تبدیل ارقام انگلیسی به فارسی.</summary>
    public static string ToFaDigits(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c);
        return sb.ToString();
    }

    /// <summary>تبدیل ارقام فارسی/عربی به انگلیسی (برای پارس عدد و ساعت).</summary>
    public static string ToEnDigits(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(c switch
            {
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                _ => c,
            });
        return sb.ToString();
    }

    /// <summary>کوتاه‌سازی امن متن برای ورودی مدل.</summary>
    public static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text ?? "";
        return text[..maxChars] + "…";
    }
}

public static class AiDateUtil
{
    private static readonly string[] FaDays = { "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه" };
    private static readonly string[] FaMonths = { "", "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند" };

    /// <summary>تاریخ امروز شمسی به‌صورت خوانا (مثلا «جمعه ۴ مهر ۱۴۰۴»).</summary>
    public static string TodayFa()
    {
        try
        {
            var pc = new System.Globalization.PersianCalendar();
            var now = DateTime.Now;
            return $"{FaDays[(int)now.DayOfWeek]} {pc.GetDayOfMonth(now)} {FaMonths[pc.GetMonth(now)]} {pc.GetYear(now)}";
        }
        catch
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }
    }

    public static string ToFaShort(DateTime dt)
    {
        try
        {
            var pc = new System.Globalization.PersianCalendar();
            return $"{pc.GetYear(dt)}/{pc.GetMonth(dt):00}/{pc.GetDayOfMonth(dt):00}";
        }
        catch
        {
            return dt.ToString("yyyy-MM-dd");
        }
    }
}
