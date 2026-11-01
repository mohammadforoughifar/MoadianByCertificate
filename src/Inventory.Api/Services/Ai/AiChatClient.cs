using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// کلاینت گفتگو با مدل زبانی — قرارداد OpenAI (سازگار با Ollama لوکال:
// http://localhost:11434/v1 ، و همچنین LM Studio و vLLM و درگاه‌های واسط).
// پشتیبانی کامل از Tool Calling برای اینکه دستیار بتواند «کار انجام بدهد».
// =====================================================================

public class AiChatMessage
{
    public string Role { get; set; } = "user"; // system | user | assistant | tool
    public string? Content { get; set; }
    public string? ToolCallId { get; set; }
    public List<AiToolCall>? ToolCalls { get; set; }
}

public class AiToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>آرگومان‌ها به‌صورت رشته JSON.</summary>
    public string ArgumentsJson { get; set; } = "{}";
}

public class AiChatResult
{
    public string? Content { get; set; }
    public List<AiToolCall> ToolCalls { get; set; } = new();
    public string FinishReason { get; set; } = "";
}

public class AiToolSchema
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>اسکیمای JSON پارامترها (object با properties).</summary>
    public object Parameters { get; set; } = new { type = "object", properties = new { } };
}

public interface IAiChatClient
{
    Task<AiChatResult> ChatAsync(List<AiChatMessage> messages, List<AiToolSchema>? tools, CancellationToken ct);
    Task<(bool ok, string? error)> CheckHealthAsync(CancellationToken ct);
}

public class OpenAiCompatibleChatClient : IAiChatClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiCompatibleChatClient> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public OpenAiCompatibleChatClient(
        IHttpClientFactory httpFactory,
        IOptions<AiOptions> options,
        ILogger<OpenAiCompatibleChatClient> log)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _log = log;
    }

    private void AddAuth(HttpRequestMessage req)
    {
        var key = (_options.ApiKey ?? "").Trim();
        if (key.Length > 0)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
    }

    public async Task<(bool ok, string? error)> CheckHealthAsync(CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient("ai");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_options.NormalizedBaseUrl}/models");
            AddAuth(req);
            var resp = await http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode)
                return (false, $"مدل پاسخ {(int)resp.StatusCode} داد.");
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            return (false, "اتصال به مدل زمان‌بر شد (timeout).");
        }
        catch (Exception ex)
        {
            return (false, "ارتباط با مدل برقرار نشد: " + ex.Message);
        }
    }

    public async Task<AiChatResult> ChatAsync(List<AiChatMessage> messages, List<AiToolSchema>? tools, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("ai");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, _options.TimeoutSeconds)));

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.ChatModel,
            ["messages"] = messages.Select(ToWire).ToList(),
            ["temperature"] = 0.2,
            ["stream"] = false,
        };
        if (tools is { Count: > 0 })
        {
            payload["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.Parameters },
            }).ToList();
            payload["tool_choice"] = "auto";
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_options.NormalizedBaseUrl}/chat/completions");
        req.Content = JsonContent.Create(payload, options: JsonOpts);
        AddAuth(req);
        using var resp = await http.SendAsync(req, cts.Token);
        var text = await resp.Content.ReadAsStringAsync(cts.Token);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("خطای مدل ({Status}): {Body}", (int)resp.StatusCode, text.Length > 500 ? text[..500] : text);
            throw new InvalidOperationException($"مدل پاسخ خطا داد ({(int)resp.StatusCode}).");
        }

        return ParseResponse(text);
    }

    private static object ToWire(AiChatMessage m)
    {
        if (m.Role == "tool")
            return new { role = "tool", tool_call_id = m.ToolCallId ?? "", content = m.Content ?? "" };
        if (m.ToolCalls is { Count: > 0 })
            return new
            {
                role = "assistant",
                content = m.Content,
                tool_calls = m.ToolCalls.Select(t => new
                {
                    id = t.Id,
                    type = "function",
                    function = new { name = t.Name, arguments = t.ArgumentsJson },
                }).ToList(),
            };
        return new { role = m.Role, content = m.Content ?? "" };
    }

    private static AiChatResult ParseResponse(string text)
    {
        var result = new AiChatResult();
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("پاسخ مدل خالی است.");
        var first = choices[0];
        if (first.TryGetProperty("finish_reason", out var fr))
            result.FinishReason = fr.GetString() ?? "";
        if (!first.TryGetProperty("message", out var msg))
            return result;
        if (msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            result.Content = c.GetString();
        if (msg.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in calls.EnumerateArray())
            {
                var id = call.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : Guid.NewGuid().ToString("N");
                if (!call.TryGetProperty("function", out var fn)) continue;
                result.ToolCalls.Add(new AiToolCall
                {
                    Id = id,
                    Name = fn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    ArgumentsJson = fn.TryGetProperty("arguments", out var a)
                        ? (a.ValueKind == JsonValueKind.String ? a.GetString() ?? "{}" : a.GetRawText())
                        : "{}",
                });
            }
        }
        return result;
    }
}
