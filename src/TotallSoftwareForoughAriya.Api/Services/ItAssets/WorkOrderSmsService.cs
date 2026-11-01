using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.ItAssets;

/// <summary>
/// Kavenegar settings. Keep ApiKey in a secret store or Kavenegar__ApiKey environment variable;
/// the key is part of Kavenegar's URL path, so this service deliberately uses a non-logging HttpClient.
/// </summary>
public sealed class KavenegarSmsOptions
{
    public string ApiKey { get; set; } = "";
    public string Sender { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 20;
}

public interface IWorkOrderSmsService
{
    bool IsConfigured { get; }
    Task<WorkOrderSmsResult> SendNewWorkOrderAsync(WorkOrder order, IReadOnlyCollection<User> recipients);
}

/// <summary>Sends an optional work-order notice to its assigned users through Kavenegar.</summary>
public sealed class WorkOrderSmsService : IWorkOrderSmsService
{
    private const int MaxRecipientsPerRequest = 200;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // HttpClientFactory's default diagnostics log full request URIs. Kavenegar requires the API key
    // in that URI, so use a shared client with redirects disabled and never log the request URI.
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        UseCookies = false
    }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly KavenegarSmsOptions _options;
    private readonly ILogger<WorkOrderSmsService> _logger;

    public WorkOrderSmsService(IOptions<KavenegarSmsOptions> options, ILogger<WorkOrderSmsService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<WorkOrderSmsResult> SendNewWorkOrderAsync(WorkOrder order, IReadOnlyCollection<User> recipients)
    {
        var result = new WorkOrderSmsResult();
        var recipientsByMobile = new Dictionary<string, List<WorkOrderSmsRecipientResult>>(StringComparer.Ordinal);

        foreach (var user in recipients)
        {
            var row = new WorkOrderSmsRecipientResult
            {
                UserId = user.Id,
                Name = DisplayName(user)
            };
            result.Recipients.Add(row);

            var mobile = NormalizeMobile(user.Mobile);
            if (mobile is null)
            {
                row.State = "skipped";
                row.Message = string.IsNullOrWhiteSpace(user.Mobile)
                    ? "شماره موبایل در پرونده کاربر ثبت نشده است."
                    : "شماره موبایل ثبت‌شده معتبر نیست.";
                continue;
            }

            // Avoid sending the same work-order message more than once to a shared phone number.
            if (!recipientsByMobile.TryGetValue(mobile, out var rows))
                recipientsByMobile[mobile] = rows = new List<WorkOrderSmsRecipientResult>();
            rows.Add(row);
        }

        if (recipientsByMobile.Count == 0)
            return result;

        if (!IsConfigured)
        {
            foreach (var row in recipientsByMobile.Values.SelectMany(rows => rows))
            {
                row.State = "failed";
                row.Message = "درگاه کاوه‌نگار روی سرور پیکربندی نشده است.";
            }
            return result;
        }

        try
        {
            var message = ComposeMessage(order);
            var batches = recipientsByMobile.ToArray().Chunk(MaxRecipientsPerRequest);
            foreach (var batch in batches)
                await SendBatchAsync(batch, message);
        }
        catch (Exception ex)
        {
            // The work order is already committed by the controller. Never turn an SMS failure into
            // a failed Create response that might cause users to submit the order a second time.
            _logger.LogWarning("Work-order SMS processing stopped ({ExceptionType}); delivery state may be unknown.", ex.GetType().Name);
            foreach (var row in result.Recipients.Where(r => string.IsNullOrEmpty(r.State)))
            {
                row.State = "unknown";
                row.Message = UnknownMessage;
            }
        }

        return result;
    }

    private async Task SendBatchAsync(KeyValuePair<string, List<WorkOrderSmsRecipientResult>>[] batch, string message)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("receptor", string.Join(",", batch.Select(item => item.Key))),
            new("message", message)
        };
        if (!string.IsNullOrWhiteSpace(_options.Sender))
            parameters.Add(new("sender", _options.Sender.Trim()));

        var key = Uri.EscapeDataString(_options.ApiKey.Trim());
        var url = $"https://api.kavenegar.com/v1/{key}/sms/send.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(parameters)
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120)));

        int httpStatusCode;
        bool httpSuccess;
        string body;
        try
        {
            using var response = await Http.SendAsync(request, timeout.Token);
            httpStatusCode = (int)response.StatusCode;
            httpSuccess = response.IsSuccessStatusCode;
            body = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            SetBatch(batch, "unknown", UnknownMessage);
            _logger.LogWarning("Kavenegar did not respond before the SMS request timed out; delivery state is unknown.");
            return;
        }
        catch (HttpRequestException ex)
        {
            SetBatch(batch, "unknown", UnknownMessage);
            _logger.LogWarning("Kavenegar SMS request failed while awaiting a response ({ExceptionType}); delivery state is unknown.", ex.GetType().Name);
            return;
        }

        GatewayResponse? gateway;
        try
        {
            gateway = JsonSerializer.Deserialize<GatewayResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            gateway = null;
        }

        if (!httpSuccess)
        {
            if (gateway?.Return?.Status is int providerCode && providerCode != 200)
            {
                SetBatch(batch, "failed", RejectionMessage(providerCode));
            }
            else if (httpStatusCode >= 500 || httpStatusCode is 408 or 429)
            {
                SetBatch(batch, "unknown", UnknownMessage);
            }
            else
            {
                SetBatch(batch, "failed", $"درگاه کاوه‌نگار پاسخ ناموفق داد (HTTP {httpStatusCode}).");
            }
            return;
        }

        if (gateway is null || gateway.Return?.Status != 200)
        {
            if (gateway?.Return?.Status is int providerCode && providerCode != 200)
                SetBatch(batch, "failed", RejectionMessage(providerCode));
            else
                SetBatch(batch, "unknown", UnknownMessage);
            return;
        }

        var entries = gateway.Entries;
        if (entries is null || entries.Count == 0)
        {
            SetBatch(batch, "unknown", UnknownMessage);
            return;
        }

        var entriesInRequestOrder = entries.Count == batch.Length && entries.All(item => NormalizeMobile(item.Receptor) is null);
        for (var i = 0; i < batch.Length; i++)
        {
            var expectedMobile = batch[i].Key;
            var entry = entries.FirstOrDefault(item => NormalizeMobile(item.Receptor) == expectedMobile);

            // Kavenegar normally returns a receptor per entry. Use documented input order only
            // when every response receptor is omitted or masked and the counts match exactly.
            if (entry is null && entriesInRequestOrder)
                entry = entries[i];

            if (entry?.Status is not int entryStatus)
            {
                SetPhone(batch[i], "unknown", UnknownMessage);
                continue;
            }

            switch (entryStatus)
            {
                case 1: // queued
                case 2: // scheduled
                case 4: // handed to carrier
                case 5: // handed to carrier
                    SetPhone(batch[i], "accepted", "درخواست ارسال در کاوه‌نگار پذیرفته شد؛ رسید تحویل نهایی ممکن است بعداً مشخص شود.");
                    break;
                case 10: // delivered
                    SetPhone(batch[i], "accepted", "تحویل پیامک توسط کاوه‌نگار تأیید شد.");
                    break;
                case 6:
                case 11:
                case 13:
                case 14:
                    SetPhone(batch[i], "failed", RejectionMessage(entryStatus));
                    break;
                default:
                    SetPhone(batch[i], "unknown", UnknownMessage);
                    break;
            }
        }
    }

    private static void SetBatch(IEnumerable<KeyValuePair<string, List<WorkOrderSmsRecipientResult>>> batch, string state, string message)
    {
        foreach (var item in batch)
            SetPhone(item, state, message);
    }

    private static void SetPhone(KeyValuePair<string, List<WorkOrderSmsRecipientResult>> item, string state, string message)
    {
        foreach (var row in item.Value)
        {
            row.State = state;
            row.Message = message;
        }
    }

    private static string ComposeMessage(WorkOrder order)
    {
        var calendar = new PersianCalendar();
        var due = $"{calendar.GetYear(order.DueAt):0000}/{calendar.GetMonth(order.DueAt):00}/{calendar.GetDayOfMonth(order.DueAt):00} {order.DueAt:HH:mm}";
        return $"کاربر گرامی، شما یک دستور کار جدید دارید.\nعنوان: {order.Title}\nشماره: {order.Number}\nمهلت انجام: {due}\nلطفاً برای مشاهده و پیگیری به سامانه مراجعه کنید.";
    }

    /// <summary>Normalizes Persian/Arabic digits and safe formatting characters; rejects lists/injection.</summary>
    private static string? NormalizeMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new System.Text.StringBuilder();
        foreach (var original in value.Trim())
        {
            var c = char.GetNumericValue(original);
            if (char.IsDigit(original) && c is >= 0 and <= 9)
            {
                normalized.Append((char)('0' + (int)c));
                continue;
            }
            if (original == '+' && normalized.Length == 0)
            {
                normalized.Append('+');
                continue;
            }
            if (char.IsWhiteSpace(original) || original is '-' or '(' or ')') continue;
            return null;
        }

        var digits = normalized.ToString();
        var digitsOnly = digits.StartsWith('+') ? digits[1..] : digits;
        if (digitsOnly.Length is < 7 or > 15 || !digitsOnly.All(c => c is >= '0' and <= '9'))
            return null;

        // Canonicalize common Iranian mobile spellings so the same number isn't texted twice.
        if (digits.StartsWith("+98", StringComparison.Ordinal) && digits.Length == 13 && digits[3] == '9')
            return "0" + digits[3..];
        if (digits.StartsWith("0098", StringComparison.Ordinal) && digits.Length == 14 && digits[4] == '9')
            return "0" + digits[4..];
        return digits.StartsWith("00", StringComparison.Ordinal) ? "+" + digits[2..] : digits;
    }

    private static string DisplayName(User user)
    {
        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? user.Username : fullName;
    }

    private static string RejectionMessage(int code) => $"درگاه کاوه‌نگار نتیجهٔ ناموفق گزارش کرد (کد {code}).";
    private const string UnknownMessage = "پاسخ نهایی درگاه پیامک دریافت نشد؛ وضعیت ارسال نامشخص است.";

    private sealed class GatewayResponse
    {
        public GatewayResponse() { }
        [JsonPropertyName("return")] public GatewayReturn? Return { get; set; }
        [JsonPropertyName("entries")] public List<GatewayEntry>? Entries { get; set; }
    }

    private sealed class GatewayReturn
    {
        public GatewayReturn() { }
        [JsonPropertyName("status")] public int? Status { get; set; }
    }

    private sealed class GatewayEntry
    {
        public GatewayEntry() { }
        [JsonPropertyName("receptor")] public string? Receptor { get; set; }
        [JsonPropertyName("status")] public int? Status { get; set; }
    }
}
