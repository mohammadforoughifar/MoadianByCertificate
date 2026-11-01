using System.Globalization;
using System.Text.Json;
using Inventory.Api.Services.ItAssets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Inventory.Api.Services.Core;

/// <summary>تنظیمات OTP و SMTP مرکزیِ اختیاری برای تأیید ایمیل کاربران.</summary>
public sealed class UserProfileOtpOptions
{
    public int CodeTtlMinutes { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxSendsPerHour { get; set; } = 5;
    public int MaxInvalidAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 10;
    public EmailSmtpOptions Email { get; set; } = new();
}

public sealed class EmailSmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "سامانه";
    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>ارسال کدها از طریق کاوه‌نگار و SMTP مرکزیِ پیکربندی‌شده روی سرور.</summary>
public interface IUserContactOtpDelivery
{
    bool IsMobileConfigured { get; }
    bool IsEmailConfigured { get; }
    Task SendMobileCodeAsync(string mobile, string code, CancellationToken cancellationToken = default);
    Task SendEmailCodeAsync(string email, string code, CancellationToken cancellationToken = default);
}

public sealed class UserContactOtpDelivery : IUserContactOtpDelivery
{
    // کلید کاوه‌نگار در مسیر URL قرار می‌گیرد؛ از HttpClient تشخیصیِ کارخانه استفاده نمی‌کنیم
    // تا آدرس درخواست (و در نتیجه کلید) وارد لاگ‌های پیش‌فرض نشود.
    private static readonly HttpClient KavenegarHttp = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        UseCookies = false
    }) { Timeout = Timeout.InfiniteTimeSpan };

    private readonly KavenegarSmsOptions _kavenegar;
    private readonly UserProfileOtpOptions _options;
    private readonly ILogger<UserContactOtpDelivery> _logger;

    public UserContactOtpDelivery(
        IOptions<KavenegarSmsOptions> kavenegar,
        IOptions<UserProfileOtpOptions> options,
        ILogger<UserContactOtpDelivery> logger)
    {
        _kavenegar = kavenegar.Value;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsMobileConfigured => !string.IsNullOrWhiteSpace(_kavenegar.ApiKey);

    public bool IsEmailConfigured
    {
        get
        {
            var email = _options.Email;
            var hasUsername = !string.IsNullOrWhiteSpace(email.Username);
            var hasPassword = !string.IsNullOrWhiteSpace(email.Password);
            return !string.IsNullOrWhiteSpace(email.Host)
                   && !string.IsNullOrWhiteSpace(email.FromAddress)
                   && email.Port is > 0 and <= 65535
                   && hasUsername == hasPassword;
        }
    }

    public async Task SendMobileCodeAsync(string mobile, string code, CancellationToken cancellationToken = default)
    {
        if (!IsMobileConfigured)
            throw new InvalidOperationException("درگاه کاوه‌نگار روی سرور فعال نیست.");

        var message = $"کد تأیید حساب کاربری: {code}\nاین کد تا چند دقیقه معتبر است. اگر این درخواست از طرف شما نیست، آن را نادیده بگیرید.";
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("receptor", mobile),
            new("message", message)
        };
        if (!string.IsNullOrWhiteSpace(_kavenegar.Sender))
            parameters.Add(new("sender", _kavenegar.Sender.Trim()));

        var apiKey = Uri.EscapeDataString(_kavenegar.ApiKey.Trim());
        var url = $"https://api.kavenegar.com/v1/{apiKey}/sms/send.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(parameters)
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_kavenegar.TimeoutSeconds, 5, 120)));

        try
        {
            using var response = await KavenegarHttp.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode || !HasSuccessfulKavenegarStatus(body))
            {
                _logger.LogWarning("Kavenegar rejected a profile verification message (HTTP {StatusCode}).", (int)response.StatusCode);
                throw new InvalidOperationException("کاوه‌نگار ارسال کد تأیید را نپذیرفت. بعداً دوباره تلاش کنید.");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Kavenegar profile verification request timed out or was canceled.");
            throw new InvalidOperationException("پاسخی از درگاه پیامک دریافت نشد. بعداً دوباره تلاش کنید.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Kavenegar profile verification request failed ({ExceptionType}).", ex.GetType().Name);
            throw new InvalidOperationException("ارتباط با درگاه پیامک برقرار نشد. بعداً دوباره تلاش کنید.");
        }
    }

    public async Task SendEmailCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        if (!IsEmailConfigured)
            throw new InvalidOperationException("ارسال کد ایمیل روی سرور پیکربندی نشده است.");

        try
        {
            var settings = _options.Email;
            var message = new MimeMessage();
            var fromName = string.IsNullOrWhiteSpace(settings.FromName) ? "سامانه" : settings.FromName.Trim();
            message.From.Add(new MailboxAddress(fromName, settings.FromAddress.Trim()));
            message.To.Add(MailboxAddress.Parse(email));
            message.Subject = "کد تأیید ایمیل حساب کاربری";
            message.Body = new TextPart("plain")
            {
                Text = $"کد تأیید ایمیل شما: {code}\n\nاین کد تا چند دقیقه معتبر است و فقط یک‌بار قابل استفاده است. اگر این درخواست از طرف شما نیست، این پیام را نادیده بگیرید."
            };

            using var client = new SmtpClient
            {
                Timeout = Math.Clamp(settings.TimeoutSeconds, 5, 120) * 1000
            };
            var socketOptions = !settings.UseSsl
                ? SecureSocketOptions.None
                : settings.Port == 465
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120)));

            await client.ConnectAsync(settings.Host.Trim(), settings.Port, socketOptions, timeout.Token);
            if (!string.IsNullOrWhiteSpace(settings.Username))
                await client.AuthenticateAsync(settings.Username.Trim(), settings.Password, timeout.Token);
            await client.SendAsync(message, timeout.Token);
            await client.DisconnectAsync(true, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Profile verification email request timed out or was canceled.");
            throw new InvalidOperationException("ارسال ایمیل تأیید به‌دلیل پایان زمان انتظار انجام نشد.");
        }
        catch (Exception ex)
        {
            // نشانی، متن OTP، رمز SMTP و جزئیات خام سرور نباید در لاگ یا پاسخ API ظاهر شوند.
            _logger.LogWarning("Profile verification email failed ({ExceptionType}).", ex.GetType().Name);
            throw new InvalidOperationException("ارسال کد تأیید ایمیل انجام نشد. تنظیمات ایمیل سرور را بررسی کنید.");
        }
    }

    private static bool HasSuccessfulKavenegarStatus(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!TryGetPropertyIgnoreCase(document.RootElement, "return", out var returned)
                || !TryGetPropertyIgnoreCase(returned, "status", out var status))
                return false;

            if (status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var numeric))
                return numeric == 200;
            return status.ValueKind == JsonValueKind.String
                   && int.TryParse(status.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                   && parsed == 200;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}
