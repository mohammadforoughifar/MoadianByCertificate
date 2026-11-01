using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InventoryAgent.Models;

namespace InventoryAgent.Services;

/// <summary>کلاینت HTTP ارتباط با API مدیریت سخت‌افزار (Inventory.Api).</summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;

    public string BaseUrl { get; }

    public ApiClient(string baseUrl, bool insecure = false, int timeoutSeconds = 30, string? agentKey = null)
    {
        BaseUrl = baseUrl.Trim().TrimEnd('/');
        HttpMessageHandler handler = new HttpClientHandler();
        if (insecure)
        {
            var h = (HttpClientHandler)handler;
            h.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("InventoryAgent/1.0");
        UseAgentKey(agentKey);
        _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    /// <summary>کلید مشترک ایجنت — هدر X-Agent-Key (اگر سرور تنظیم کرده باشد لازم است).</summary>
    public void UseAgentKey(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _http.DefaultRequestHeaders.Remove("X-Agent-Key");
        if (!string.IsNullOrWhiteSpace(key))
            _http.DefaultRequestHeaders.Add("X-Agent-Key", key.Trim());
    }

    public void UseToken(string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    /// <summary>ورود با نام کاربری/رمز و گرفتن توکن — POST api/auth/login</summary>
    public async Task<(bool ok, string message)> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync($"{BaseUrl}/api/auth/login",
                new { username, password }, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (false, $"Login failed ({(int)resp.StatusCode}): {ExtractMessage(body)}");
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("token", out var t) ||
                    doc.RootElement.TryGetProperty("Token", out t))
                {
                    var token = t.GetString() ?? "";
                    if (token.Length > 0)
                    {
                        UseToken(token);
                        return (true, "ورود موفق.");
                    }
                }
            }
            catch { }
            return (false, "پاسخ لاگین نامعتبر است.");
        }
        catch (Exception ex)
        {
            return (false, $"خطای اتصال در لاگین: {ex.Message}");
        }
    }

    /// <summary>ارسال گزارش سخت‌افزار — POST api/SystemInfo (با ۳ تلاش و backoff).</summary>
    public async Task<(bool ok, string message)> SendReportAsync(AgentReport report, CancellationToken ct = default)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var resp = await _http.PostAsJsonAsync(
                    $"{BaseUrl}/api/SystemInfo", report, AgentJson.Options, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode)
                    return (true, DescribeReportResponse(body));
                // خطای 4xx را تکرار نکن — مشکل از داده است نه شبکه
                if ((int)resp.StatusCode is >= 400 and < 500)
                {
                    var serverMsg = ExtractMessage(body);
                    var why = (int)resp.StatusCode switch
                    {
                        401 or 403 when serverMsg.Contains("کلید ایجنت") =>
                            " — این سرور «کلید مشترک ایجنت» دارد؛ مقدار key را در agent.json بگذارید یا با سوییچ --key بدهید " +
                            "(همان مقدار Agent:Key در تنظیمات سرور).",
                        401 or 403 => " — سرور درخواست بدون توکن را رد کرد. نسخهٔ سرور باید به‌روزرسانی شود (اکشن api/SystemInfo باید [AllowAnonymous] باشد)، " +
                                      "یا ایجنت را با --username و --password یک کاربر معتبر اجرا کنید.",
                        404 => " — مسیر api/SystemInfo روی این سرور نیست؛ احتمالاً برنامهٔ دیگری روی این پورت پاسخ می‌دهد.",
                        _ => ""
                    };
                    return (false, $"سرور قبول نکرد ({(int)resp.StatusCode}): {serverMsg}{why}");
                }
                last = new Exception($"HTTP {(int)resp.StatusCode}: {ExtractMessage(body)}");
            }
            catch (Exception ex)
            {
                last = ex;
            }
            if (attempt < 3)
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
        }
        return (false, $"ارسال ناموفق بعد از ۳ تلاش: {last?.Message}");
    }

    /// <summary>ارسال وضعیت چاپگرهای محلی — POST api/OfficeMachines/agent/printers</summary>
    public async Task<(bool ok, string message)> SendPrinterReportAsync(AgentPrinterReport report, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync($"{BaseUrl}/api/OfficeMachines/agent/printers", report, AgentJson.Options, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode) return (true, body.Length > 300 ? body[..300] : body);
            var hint = (int)resp.StatusCode switch
            {
                404 => " — این نسخهٔ سرور پایش چاپگر از طریق ایجنت را ندارد (سرور را به‌روزرسانی کنید).",
                401 or 403 => " — کلید ایجنت (X-Agent-Key) نادرست است.",
                _ => ""
            };
            return (false, $"HTTP {(int)resp.StatusCode}{hint}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>دریافت دستورهای در انتظار — GET api/SystemInfo/agent-commands?agentId=</summary>
    public async Task<List<RemoteCommand>> GetPendingCommandsAsync(string agentId, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/api/SystemInfo/agent-commands?agentId={Uri.EscapeDataString(agentId)}";
            var list = await _http.GetFromJsonAsync<List<RemoteCommand>>(url, _json, ct);
            return list ?? new List<RemoteCommand>();
        }
        catch
        {
            return new List<RemoteCommand>();
        }
    }

    /// <summary>گزارش نتیجه‌ی اجرای دستور — POST api/SystemInfo/commands/{id}/result</summary>
    public async Task AckCommandAsync(int cmdId, bool ok, string message, CancellationToken ct = default)
    {
        try
        {
            await _http.PostAsJsonAsync($"{BaseUrl}/api/SystemInfo/commands/{cmdId}/result",
                new { ok, message }, ct);
        }
        catch { }
    }

    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "بدون جزئیات";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out var m) || root.TryGetProperty("Message", out m))
                    return m.GetString() ?? body;
                if (root.TryGetProperty("title", out var t))
                    return t.GetString() ?? body;
            }
        }
        catch { }
        return body.Length > 300 ? body[..300] : body;
    }

    private static string DescribeReportResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string P(string camel, string pascal)
            {
                if (root.TryGetProperty(camel, out var v)) return v.ToString();
                if (root.TryGetProperty(pascal, out v)) return v.ToString();
                return "";
            }
            var id = P("id", "Id");
            if (root.TryGetProperty("firstRegistration", out var fr) && fr.ValueKind == JsonValueKind.True)
                return $"ثبت اول موفق (id={id}).";
            if (root.TryGetProperty("unchanged", out var un) && un.ValueKind == JsonValueKind.True)
                return $"بدون تغییر — فقط زمان به‌روز شد (id={id}).";
            var pending = P("pendingChanges", "PendingChanges");
            var msg = P("message", "Message");
            if (msg.Length == 0) msg = "تغییرات در انتظار تایید ثبت شد.";
            return string.IsNullOrEmpty(pending) ? $"{msg} (id={id})" : $"{msg} ({pending} تغییر، id={id})";
        }
        catch
        {
            return "ارسال موفق.";
        }
    }

    public void Dispose() => _http.Dispose();
}
