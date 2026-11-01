using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// «تبدیل ویس به متن» — رونویسی پیام‌های صوتی پیام‌رسان با مدل گفتار به متن.
//
// قرارداد OpenAI (سازگار با OpenAI، Groq، OpenRouterهای سازگار و سرورهای
// لوکال مثل faster-whisper-server / speaches / whisper.cpp --server):
//     POST {baseUrl}/audio/transcriptions
//     multipart/form-data: file, model, language, response_format=json
//     پاسخ: { "text": "..." }
//
// نکته: Ollama فعلی endpoint رونویسی ندارد؛ برای همین Transcription* از
// BaseUrl گفتگو جدا شده تا گفتگو روی Ollama بماند و رونویسی به سرویس دیگر برود.
// =====================================================================

public record AiTranscriptionResult(string? Text, string? Error)
{
    public bool Ok => !string.IsNullOrWhiteSpace(Text) && Error == null;
}

public interface IAiTranscriptionClient
{
    /// <summary>آیا سرویس رونویسی تنظیم شده است؟ (مدل + آدرس)</summary>
    bool IsConfigured { get; }

    /// <summary>حداکثر حجم مجاز فایل صوتی برای رونویسی (بایت‌ها).</summary>
    long MaxBytes { get; }

    /// <summary>
    /// رونویسی یک فایل صوتی. در صورت خطا، پیام فارسیِ قابل‌نمایش برمی‌گرداند
    /// (استثنا پرت نمی‌شود تا یک وویس خراب، کل درخواست کاربر را ۵۰۰ نکند).
    /// </summary>
    Task<AiTranscriptionResult> TranscribeAsync(byte[] audio, string fileName, string contentType, CancellationToken ct);
}

public class OpenAiCompatibleTranscriptionClient : IAiTranscriptionClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiCompatibleTranscriptionClient> _log;

    public OpenAiCompatibleTranscriptionClient(
        IHttpClientFactory httpFactory,
        IOptions<AiOptions> options,
        ILogger<OpenAiCompatibleTranscriptionClient> log)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _log = log;
    }

    public bool IsConfigured => _options.TranscriptionReady;

    public long MaxBytes => Math.Max(1, _options.TranscriptionMaxMegabytes) * 1024L * 1024L;

    public async Task<AiTranscriptionResult> TranscribeAsync(byte[] audio, string fileName, string contentType, CancellationToken ct)
    {
        if (!IsConfigured)
            return new AiTranscriptionResult(null,
                "سرویس تبدیل گفتار به متن تنظیم نشده است؛ مدیر سامانه باید Ai:TranscriptionModel (و در صورت نیاز Ai:TranscriptionBaseUrl) را تنظیم کند.");
        if (audio.Length == 0)
            return new AiTranscriptionResult(null, "فایل صوتی خالی است.");
        if (audio.Length > MaxBytes)
            return new AiTranscriptionResult(null,
                $"حجم فایل صوتی برای تبدیل به متن زیاد است (بیشتر از {_options.TranscriptionMaxMegabytes} مگابایت).");

        try
        {
            var http = _httpFactory.CreateClient("ai");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(60, _options.TimeoutSeconds)));

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(audio);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
                string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
            form.Add(fileContent, "file", SafeFileName(fileName));
            form.Add(new StringContent(_options.TranscriptionModel), "model");
            if (!string.IsNullOrWhiteSpace(_options.TranscriptionLanguage))
                form.Add(new StringContent(_options.TranscriptionLanguage.Trim()), "language");
            // خروجی سادهٔ json؛ سرورهای ناسازگار با verbose_json هم با این کار می‌کنند.
            form.Add(new StringContent("json"), "response_format");

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_options.NormalizedTranscriptionBaseUrl}/audio/transcriptions") { Content = form };
            var key = _options.EffectiveTranscriptionApiKey.Trim();
            if (key.Length > 0)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            using var resp = await http.SendAsync(req, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("رونویسی: سرویس گفتار به متن خطا داد ({Status}): {Body}",
                    (int)resp.StatusCode, body.Length > 400 ? body[..400] : body);
                return new AiTranscriptionResult(null, ((int)resp.StatusCode) switch
                {
                    401 or 403 => "کلید سرویس تبدیل گفتار به متن نامعتبر است (Ai:TranscriptionApiKey).",
                    404 => "سرویس تبدیل گفتار به متن در این آدرس پیدا نشد (Ai:TranscriptionBaseUrl).",
                    413 => "حجم فایل صوتی برای سرویس تبدیل گفتار به متن زیاد است.",
                    _ => $"سرویس تبدیل گفتار به متن خطا داد ({(int)resp.StatusCode})."
                });
            }

            var text = ExtractText(body);
            if (string.IsNullOrWhiteSpace(text))
                return new AiTranscriptionResult(null, "سرویس تبدیل گفتار به متن متنی برنگرداند (شاید صدای فایل واضح نیست).");
            return new AiTranscriptionResult(text.Trim(), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new AiTranscriptionResult(null, "تبدیل به متن زمان‌بر شد؛ دوباره تلاش کنید.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "رونویسی پیام صوتی ناموفق بود.");
            return new AiTranscriptionResult(null, "ارتباط با سرویس تبدیل گفتار به متن برقرار نشد: " + ex.Message);
        }
    }

    /// <summary>متن را از پاسخ سرویس بیرون می‌کشد؛ هم {"text"} و هم {"data":[{"text"}]} پذیرفته می‌شود.</summary>
    private static string? ExtractText(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String) return root.GetString();
            if (root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
            if (root.TryGetProperty("transcription", out var tr) && tr.ValueKind == JsonValueKind.String)
                return tr.GetString();
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                var first = data[0];
                if (first.TryGetProperty("text", out var dt) && dt.ValueKind == JsonValueKind.String)
                    return dt.GetString();
            }
            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
            {
                var first = results[0];
                if (first.TryGetProperty("text", out var rt) && rt.ValueKind == JsonValueKind.String)
                    return rt.GetString();
            }
            if (root.TryGetProperty("alternatives", out var alts) && alts.ValueKind == JsonValueKind.Array && alts.GetArrayLength() > 0)
            {
                var first = alts[0];
                if (first.TryGetProperty("transcript", out var at) && at.ValueKind == JsonValueKind.String)
                    return at.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }
        return null;
    }

    /// <summary>نام فایل امن برای هدر multipart (سرویس‌های ابری به مسیر و کاراکتر کنترل حساس‌اند).</summary>
    private static string SafeFileName(string? name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray());
        clean = clean.Replace('\\', '/');
        clean = clean[(clean.LastIndexOf('/') + 1)..].Trim();
        if (clean.Length == 0) clean = "voice.webm";
        return clean.Length > 200 ? clean[^200..] : clean;
    }
}
