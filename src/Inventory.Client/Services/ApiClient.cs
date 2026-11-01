using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

public class ApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5100";
}

public class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}

/// <summary>کلاینت HTTP برای ارتباط با API با استخراج پیام خطای فارسی.</summary>
public class ApiClient : IApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ApiOptions _opts;

    /// <summary>سقف انتظار فراخوانی‌های معمولی (رفتار قبلی: ۱۰۰ ثانیه).</summary>
    private static readonly TimeSpan DefaultCallTimeout = TimeSpan.FromSeconds(100);
    /// <summary>سقف انتظار فراخوانی‌های AI (استنتاج CPU + حلقه ابزار ممکن است چند دقیقه طول بکشد).</summary>
    private static readonly TimeSpan LongCallTimeout = TimeSpan.FromMinutes(10);

    private readonly IAuthState _auth;
    private readonly IJSRuntime _js;

    public ApiClient(HttpClient http, ApiOptions opts, IAuthState auth, IJSRuntime js)
    {
        _http = http;
        _opts = opts;
        _auth = auth;
        _js = js;
    }

    private void AddAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(_auth.Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _auth.Token);
    }

    /// <summary>
    /// زمینهٔ شرکت فعال را روی خودِ درخواست می‌گذارد، نه فقط روی DefaultRequestHeaders.
    /// ApiClient برای درخواست‌های مودیان و فرم‌های دیگر ساخته می‌شود و ممکن است قبل/بعد از
    /// MainLayout اجرا شود؛ بنابراین تکیه بر هدر سراسری باعث می‌شد API شرکت پیش‌فرض را بخواند
    /// و داده‌های CompanyId انتخاب‌شده در کمبوی بالای برنامه نمایش داده نشوند.
    /// </summary>
    private async Task AddActiveCompanyHeaderAsync(HttpRequestMessage req)
    {
        if (req.Headers.Contains("X-Company-Id")) return;
        try
        {
            var saved = await _js.InvokeAsync<string?>("localStorage.getItem", "activeCompanyId");
            if (int.TryParse(saved, out var companyId) && companyId > 0)
                req.Headers.TryAddWithoutValidation("X-Company-Id", companyId.ToString());
        }
        catch
        {
            // درخواست‌های ورود/شروع برنامه ممکن است پیش از دسترس‌بودن JS اجرا شوند.
        }
    }

    private string Url(string path) => $"{_opts.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    public string BuildUrl(string path) => Url(path);

    /// <summary>
    /// دریافت فایل باینری از API. نام فایل از هدر Content-Disposition خوانده می‌شود
    /// و اگر نبود از انتهای مسیر ساخته می‌شود.
    /// </summary>
    public async Task<(byte[] Data, string FileName, string ContentType)> GetFileAsync(string path, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Url(path));
        AddAuth(req);
        await AddActiveCompanyHeaderAsync(req);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new Exception("ارتباط با سرور برقرار نشد: " + ex.Message);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(cancellationToken);
                var msg = $"خطای سرور ({(int)resp.StatusCode})";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(text);
                        if (doc.RootElement.TryGetProperty("message", out var m))
                            msg = m.GetString() ?? msg;
                    }
                    catch { /* پاسخ JSON نبود */ }
                }
                throw new Exception(msg);
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
            var ctype = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

            var name = resp.Content.Headers.ContentDisposition?.FileNameStar
                       ?? resp.Content.Headers.ContentDisposition?.FileName;
            name = name?.Trim('"');
            if (string.IsNullOrWhiteSpace(name))
                name = path.Split('/').LastOrDefault()?.Split('?').FirstOrDefault() ?? "download";

            return (bytes, name, ctype);
        }
    }

    public async Task<T> PostFileAsync<T>(string path, Stream fileStream, string fileName, string formFieldName = "file", string? contentType = null)
    {
        using var content = new MultipartFormDataContent();
        var sc = new StreamContent(fileStream);
        sc.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var mime)
            ? mime : new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(sc, formFieldName, fileName);

        using var fileReq = new HttpRequestMessage(HttpMethod.Post, Url(path)) { Content = content };
        AddAuth(fileReq);
        await AddActiveCompanyHeaderAsync(fileReq);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(fileReq);
        }
        catch (Exception ex)
        {
            throw new ApiException($"ارتباط با سرور برقرار نشد. ({ex.Message})");
        }

        using var response = resp;
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            string msg = "خطایی رخ داد.";
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    msg = m.GetString() ?? msg;
                else msg = DescribeBody(text);
            }
            catch { msg = DescribeBody(text); }
            throw new ApiException(msg);
        }

        if (string.IsNullOrWhiteSpace(text)) return default!;
        try
        {
            return JsonSerializer.Deserialize<T>(text, JsonOpts) ?? default!;
        }
        catch (JsonException)
        {
            // پاسخ ۲۰۰ ولی بدنه JSON نبود (مثلاً صفحهٔ HTML) — پیام کوتاه به‌جای خطای خام
            throw new ApiException(DescribeBody(text));
        }
    }

    public async Task<T> GetAsync<T>(string path) => await SendAsync<T>(HttpMethod.Get, path);

    /// <summary>GET قابل لغو — برای جستجوهای زنده (هر بار درخواست قبلی کنار می‌رود).</summary>
    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken) =>
        await SendAsync<T>(HttpMethod.Get, path, null, null, cancellationToken);
    public async Task<T> GetLongAsync<T>(string path) => await SendAsync<T>(HttpMethod.Get, path, null, LongCallTimeout);

    public async Task<T> PostAsync<T>(string path, object? body = null) => await SendAsync<T>(HttpMethod.Post, path, body);
    public async Task<T> PostLongAsync<T>(string path, object? body = null) => await SendAsync<T>(HttpMethod.Post, path, body, LongCallTimeout);

    public async Task<T> PutAsync<T>(string path, object? body = null) => await SendAsync<T>(HttpMethod.Put, path, body);

    public async Task DeleteAsync(string path) => await SendAsync<object>(HttpMethod.Delete, path);

    public async Task<T> DeleteAsync<T>(string path) => await SendAsync<T>(HttpMethod.Delete, path);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var req = new HttpRequestMessage(method, Url(path));
        AddAuth(req);
        await AddActiveCompanyHeaderAsync(req);
        if (body is not null)
            req.Content = JsonContent.Create(body, options: JsonOpts);

        var limit = timeout ?? DefaultCallTimeout;
        // سقف انتظار + لغو از بیرون (جستجوی زنده): هر کدام زودتر برسد درخواست تمام می‌شود.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(limit);
        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, cts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // لغو از سوی فراخوان (درخواست تازه‌تر جای این را گرفته) — خطا نیست
            throw;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new ApiException($"پاسخ سرور بیش از {(int)limit.TotalSeconds} ثانیه طول کشید و قطع شد. اگر مدل تازه بارگذاری شده، یک بار دیگر تلاش کن (دفعه بعد سریع‌تر است).");
        }
        catch (Exception ex)
        {
            throw new ApiException($"ارتباط با سرور برقرار نشد. ({ex.Message})");
        }

        string text;
        try
        {
            text = await resp.Content.ReadAsStringAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            resp.Dispose();
            throw;
        }

        if (!resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new ApiException("نشست شما معتبر نیست یا منقضی شده است؛ لطفاً دوباره وارد سامانه شوید.");
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // A confidential-password failure is also HTTP 403. Do not turn the
                // server's "wrong password" into a misleading "no permission" message.
                var forbiddenMessage = "شما مجوز انجام این عملیات را ندارید.";
                try
                {
                    using var problem = JsonDocument.Parse(text);
                    if (problem.RootElement.ValueKind == JsonValueKind.Object && problem.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(message.GetString()))
                        forbiddenMessage = message.GetString()![..Math.Min(message.GetString()!.Length, 600)];
                }
                catch (JsonException) { }
                throw new ApiException(forbiddenMessage);
            }
            string msg;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    msg = m.GetString() ?? "خطایی رخ داد.";
                else
                    msg = DescribeBody(text);
            }
            catch
            {
                msg = DescribeBody(text);
            }
            // افزودن کد وضعیت و آدرس برای تشخیص آسان‌تر
            throw new ApiException($"{msg} (HTTP {(int)resp.StatusCode} — {Url(path)})");
        }

        if (string.IsNullOrWhiteSpace(text)) return default!;
        if (typeof(T) == typeof(string)) return (T)(object)text;
        try
        {
            return JsonSerializer.Deserialize<T>(text, JsonOpts) ?? default!;
        }
        catch (JsonException)
        {
            // پاسخ ۲۰۰ ولی بدنه JSON نبود (مثلاً صفحهٔ HTML) — پیام کوتاه به‌جای خطای خام
            throw new ApiException($"{DescribeBody(text)} (HTTP {(int)resp.StatusCode} — {Url(path)})");
        }
    }

    /// <summary>
    /// خلاصهٔ کوتاه و قابل‌فهم از بدنهٔ غیر JSON — به‌جای ریختن کل صفحهٔ HTML در پیام خطا.
    /// اگر پاسخ یک صفحهٔ وب بود (خطای IIS/پروکسی یا پوستهٔ SPA)، پیام راهنما برمی‌گردد؛
    /// سایر متن‌ها به ۲۰۰ کاراکتر خلاصه می‌شوند.
    /// </summary>
    internal static string DescribeBody(string? text)
    {
        var trimmed = text?.TrimStart('\uFEFF', ' ', '\t', '\r', '\n') ?? string.Empty;
        if (trimmed.Length == 0) return "بدنه‌ی پاسخ خالی بود.";
        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            return "پاسخ سرور به‌جای داده (JSON) یک صفحهٔ HTML بود؛ احتمالاً نشانی سرور API اشتباه است یا سرویس API در دسترس نیست.";
        var oneLine = trimmed.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= 200 ? oneLine : oneLine[..200] + "…";
    }
}
