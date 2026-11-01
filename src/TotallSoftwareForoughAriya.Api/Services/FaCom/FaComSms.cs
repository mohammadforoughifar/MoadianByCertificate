namespace Inventory.Api.Services.FaCom;

public interface ISmsSender
{
    bool IsConfigured { get; }
    Task SendAsync(string to, string text, CancellationToken ct = default);
}

/// <summary>
/// فرستنده پیامک با وب‌هوک قابل‌پیکربندی (درگاه دلخواه: کاوه‌نگار/قاصدک/ملی‌پیامک/...).
/// تنظیمات در appsettings: Sms:Url (الگوی POST؛ توکن‌های {to} و {text})،
/// Sms:BodyTemplate (اختیاری؛ پیش‌فرض JSON ساده receptor/message)،
/// Sms:Headers (اختیاری؛ قالب «Key:Value;Key2:Value2» مثل apikey).
/// </summary>
public class ConfigSmsSender : ISmsSender
{
    private readonly IHttpClientFactory _http;
    private readonly string? _url;
    private readonly string? _bodyTemplate;
    private readonly string? _headers;

    public ConfigSmsSender(IHttpClientFactory http, IConfiguration cfg)
    {
        _http = http;
        _url = cfg["Sms:Url"];
        _bodyTemplate = cfg["Sms:BodyTemplate"];
        _headers = cfg["Sms:Headers"];
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_url);

    public async Task SendAsync(string to, string text, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("درگاه پیامک پیکربندی نشده است (بخش Sms در تنظیمات).");
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        var url = _url!.Replace("{to}", Uri.EscapeDataString(to)).Replace("{text}", Uri.EscapeDataString(text));
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(_headers))
            foreach (var h in _headers!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = h.Split(':', 2);
                if (kv.Length == 2) req.Headers.TryAddWithoutValidation(kv[0].Trim(), kv[1].Trim());
            }
        var body = string.IsNullOrWhiteSpace(_bodyTemplate)
            ? $"{{\"receptor\":\"{JsonEscape(to)}\",\"message\":\"{JsonEscape(text)}\"}}"
            : _bodyTemplate!.Replace("{to}", JsonEscape(to)).Replace("{text}", JsonEscape(text));
        req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static string JsonEscape(string s) => System.Text.Json.JsonEncodedText.Encode(s).ToString();
}
