using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// تنظیمات اتصال به سرویس OCR پایتون (بخش «OcrService» در appsettings).
/// خالی بودن BaseUrl یعنی سرویس متصل نیست و موتور محلی (tesseract/poppler) استفاده می‌شود.
/// معادل متغیرهای محیطی: OcrService__BaseUrl ، OcrService__ApiKey ، OcrService__TimeoutSeconds
/// </summary>
public class OcrServiceOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// اگر BaseUrl خالی باشد، هنگام بالا آمدن برنامه سرویس پیش‌فرض 127.0.0.1:8765
    /// به‌صورت خودکار جستجو و در صورت یافتن وصل می‌شود.
    /// </summary>
    public bool AutoDetect { get; set; } = true;

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>پاسخ سرویس OCR پایتون.</summary>
public class RemoteOcrResponse
{
    public bool Success { get; set; }
    public string? Engine { get; set; }
    public string? Method { get; set; }
    public string? Text { get; set; }
    public string? Error { get; set; }
    public int CharacterCount { get; set; }
    public List<int>? OcrPages { get; set; }
    public int? TotalPages { get; set; }
    public double? DurationMs { get; set; }
}

public interface IOcrServiceClient
{
    bool IsConfigured { get; }
    string BaseUrl { get; }

    /// <summary>
    /// تشخیص متن تصویر از راه دور.
    /// خروجی null یعنی سرویس قابل‌دسترس نبود (قطع اتصال/تایم‌اوت) → موتور محلی جایگزین می‌شود.
    /// خروجی غیر-null یعنی سرویس پاسخ داد؛ Success نشان‌دهندهٔ موفقیت خودِ تشخیص است.
    /// </summary>
    Task<RemoteOcrResponse?> TryOcrImageAsync(byte[] fileBytes, string fileName, CancellationToken ct = default);

    /// <summary>استخراج متن PDF (دیجیتال یا اسکن‌شده) از راه دور. مانند بالا.</summary>
    Task<RemoteOcrResponse?> TryOcrPdfAsync(byte[] fileBytes, string fileName, CancellationToken ct = default);

    /// <summary>استخراج متن فایل آفیس (Word/Excel) از راه دور. مانند بالا.</summary>
    Task<RemoteOcrResponse?> TryOcrOfficeAsync(byte[] fileBytes, string fileName, CancellationToken ct = default);
}

/// <summary>
/// کلاینت HTTP سرویس OCR پایتون (ocr-service). سینگلتون؛ یک HttpClient بلندمدت.
/// </summary>
public class OcrServiceClient : IOcrServiceClient
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly OcrServiceOptions _options;
    private readonly ILogger<OcrServiceClient> _logger;

    public OcrServiceClient(OcrServiceOptions options, ILogger<OcrServiceClient> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;
    public string BaseUrl => _options.BaseUrl;

    public Task<RemoteOcrResponse?> TryOcrImageAsync(byte[] fileBytes, string fileName, CancellationToken ct = default)
        => PostFileAsync("/api/ocr/image", fileBytes, fileName, ct);

    public Task<RemoteOcrResponse?> TryOcrPdfAsync(byte[] fileBytes, string fileName, CancellationToken ct = default)
        => PostFileAsync("/api/ocr/pdf", fileBytes, fileName, ct);

    public Task<RemoteOcrResponse?> TryOcrOfficeAsync(byte[] fileBytes, string fileName, CancellationToken ct = default)
        => PostFileAsync("/api/ocr/office", fileBytes, fileName, ct);

    private async Task<RemoteOcrResponse?> PostFileAsync(string route, byte[] fileBytes, string fileName, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(15, _options.TimeoutSeconds)));

        try
        {
            using var content = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "file.bin" : fileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl.TrimEnd('/') + route)
            {
                Content = content
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                request.Headers.TryAddWithoutValidation("X-OCR-Key", _options.ApiKey);

            using var response = await Http.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            RemoteOcrResponse? result;
            try
            {
                result = System.Text.Json.JsonSerializer.Deserialize<RemoteOcrResponse>(text,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                result = null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // خطای سرویس (مثل ۴۱۳ حجم زیاد) — پاسخ «ناموفق» برمی‌گردانیم نه null، تا fallback نشود
                return new RemoteOcrResponse
                {
                    Success = false,
                    Error = $"سرویس OCR پاسخ ناموفق داد ({(int)response.StatusCode}): {Truncate(text)}"
                };
            }

            return result ?? new RemoteOcrResponse { Success = false, Error = "پاسخ سرویس OCR قابل‌خواندن نبود." };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[OcrService] درخواست به سرویس OCR ({Route}) به‌دلیل تایم‌اوت لغو شد.", route);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[OcrService] اتصال به سرویس OCR ({Base}{Route}) برقرار نشد.",
                _options.BaseUrl, route);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OcrService] خطای غیرمنتظره در ارتباط با سرویس OCR ({Route}).", route);
            return null;
        }
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] : s;
}
