using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using WebPush;
using System.Security.Cryptography;

namespace Inventory.Api.Services;

public interface IPushService
{
    Task SaveSubscriptionAsync(int userId, string endpoint, string p256dh, string auth, string? userAgent);
    Task RemoveSubscriptionAsync(int userId, string endpoint);
    Task SendToUserAsync(int userId, string title, string? body, string? link);
    Task SendToUsersAsync(IEnumerable<int> userIds, string title, string? body, string? link);
    /// <summary>پاک‌سازی همه‌ی اشتراک‌های دستگاه‌ها (زمانی که کلید کلید اعلان تغییر می‌کند).</summary>
    Task<int> ClearSubscriptionsAsync();
    string VapidPublicKey { get; }
    bool IsConfigured { get; }
    string Source { get; }
}

public static class PushEndpointPolicy
{
    public static bool ValidEndpoint(string endpoint)
    {
        if (endpoint.Length > 500 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) return false;
        return (uri.Host == "fcm.googleapis.com" && (uri.AbsolutePath.StartsWith("/fcm/send/") || uri.AbsolutePath.StartsWith("/wp/")))
            || (uri.Host == "updates.push.services.mozilla.com" && uri.AbsolutePath.StartsWith("/wpush/"))
            || (uri.Host == "web.push.apple.com" && uri.AbsolutePath.StartsWith("/Q"));
    }
    public static byte[] Decode(string text) => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
    public static bool ValidKeys(string publicKey, string auth)
    {
        try
        {
            var key = Decode(publicKey);
            if (key.Length != 65 || key[0] != 4 || Decode(auth).Length != 16) return false;
            using var ec = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = key[1..33], Y = key[33..65] } });
            return true;
        }
        catch { return false; }
    }
}

/// <summary>
/// کلیدهای اعلان مرورگر/گوشی (VAPID). مقادیر می‌توانند از فایل/متغیر محیطی سرور بیایند یا
/// از داخل «تنظیمات ← اعلان گوشی» ساخته و ذخیره شوند؛ در حالت دوم بدون ری‌استارت سرویس، زنده اعمال می‌شوند.
/// </summary>
public sealed class PushSettings
{
    /// <summary>عکس فوری (immutable) از کلیدهای فعال — برای خواندن بی‌قفل و امن از چند ترد.</summary>
    public sealed record Snapshot(string PublicKey, string PrivateKey, string Subject, bool IsConfigured, string Source);

    private volatile Snapshot _current;
    private readonly Snapshot _serverConfig;

    public string PublicKey => _current.PublicKey;
    public string PrivateKey => _current.PrivateKey;
    public string Subject => _current.Subject;
    public bool IsConfigured => _current.IsConfigured;

    /// <summary>«settings» = از داخل تنظیمات نرم‌افزار، «config» = فایل/متغیر محیطی سرور، «none» = تنظیم‌نشده.</summary>
    public string Source => _current.Source;
    public bool HasServerConfigKeys => _serverConfig.IsConfigured;

    public PushSettings(IConfiguration cfg)
    {
        var pub = Environment.GetEnvironmentVariable("VAPID_PUBLIC_KEY") ?? cfg["PushNotifications:VapidPublicKey"] ?? "";
        var priv = Environment.GetEnvironmentVariable("VAPID_PRIVATE_KEY") ?? cfg["PushNotifications:VapidPrivateKey"] ?? "";
        var subject = Environment.GetEnvironmentVariable("VAPID_SUBJECT") ?? cfg["PushNotifications:VapidSubject"] ?? "";
        _serverConfig = Build(pub, priv, subject, "config");
        _current = _serverConfig;
    }

    public static Snapshot Build(string? publicKey, string? privateKey, string? subject, string source)
    {
        publicKey ??= ""; privateKey ??= ""; subject ??= "";
        if (string.IsNullOrWhiteSpace(publicKey) && string.IsNullOrWhiteSpace(privateKey))
            return new Snapshot("", "", subject, false, "none");
        try
        {
            var pub = PushEndpointPolicy.Decode(publicKey); var secret = PushEndpointPolicy.Decode(privateKey);
            if (pub.Length != 65 || pub[0] != 4 || secret.Length != 32 || !(subject.StartsWith("mailto:") || subject.StartsWith("https://")))
                return new Snapshot(publicKey, privateKey, subject, false, source);
            using var signing = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = secret });
            var derived = signing.ExportParameters(false);
            var valid = derived.Q.X!.SequenceEqual(pub[1..33]) && derived.Q.Y!.SequenceEqual(pub[33..65]);
            return new Snapshot(publicKey, privateKey, subject, valid, source);
        }
        catch { return new Snapshot(publicKey, privateKey, subject, false, source); }
    }

    /// <summary>اعتبارسنجی یک جفت‌کلید بدون اعمال آن (برای ورودی دستی مدیر).</summary>
    public static bool Validate(string? publicKey, string? privateKey, string? subject) => Build(publicKey, privateKey, subject, "settings").IsConfigured;

    /// <summary>اعمال کلیدهای جدید به‌صورت زنده. در صورت نامعتبر بودن، خطا برمی‌گرداند و چیزی تغییر نمی‌کند.</summary>
    public bool Apply(string? publicKey, string? privateKey, string? subject, out string error)
    {
        var snapshot = Build(publicKey, privateKey, subject, "settings");
        if (!snapshot.IsConfigured)
        {
            error = "جفت‌کلید VAPID معتبر نیست؛ مقدار کلید عمومی باید ۶۵ بایت (شروع با 0x04) و کلید خصوصی ۳۲ بایت باشد و Subject با mailto: یا https:// شروع شود.";
            return false;
        }
        _current = snapshot;
        error = "";
        return true;
    }

    /// <summary>بازگشت به کلیدهای فایل/متغیر محیطی سرور (وقتی کلیدهای ساخته‌شده در نرم‌افزار حذف می‌شوند).</summary>
    public void ResetToServerConfig() => _current = _serverConfig;
}

public class PushService(AppDbContext db, PushSettings settings) : IPushService
{
    public string VapidPublicKey => settings.IsConfigured ? settings.PublicKey : "";
    public bool IsConfigured => settings.IsConfigured;
    public string Source => settings.Source;
    public async Task<int> ClearSubscriptionsAsync()
    {
        await db.PushDeliveries.ExecuteDeleteAsync();
        return await db.PushSubscriptions.ExecuteDeleteAsync();
    }
    public async Task SaveSubscriptionAsync(int userId, string endpoint, string p256dh, string auth, string? userAgent)
    {
        if (userId <= 0 || !PushEndpointPolicy.ValidEndpoint(endpoint) || !PushEndpointPolicy.ValidKeys(p256dh, auth)) throw new ArgumentException("اشتراک دستگاه معتبر نیست؛ از Chrome به‌روز و HTTPS استفاده کنید.");
        if (!IsConfigured) throw new InvalidOperationException("کلیدهای اعلان روی سرور تنظیم نشده یا معتبر نیستند.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var existing = await db.PushSubscriptions.SingleOrDefaultAsync(s => s.Endpoint == endpoint);
        if (existing == null) { existing = new Inventory.Api.Data.PushSubscription { Endpoint = endpoint }; db.PushSubscriptions.Add(existing); }
        existing.UserId = userId; existing.P256DH = p256dh; existing.Auth = auth;
        existing.UserAgent = userAgent is { Length: > 250 } ? userAgent[..250] : userAgent;
        existing.LastSeenAt = DateTime.Now;
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task RemoveSubscriptionAsync(int userId, string endpoint)
        => await db.PushSubscriptions.Where(s => s.UserId == userId && s.Endpoint == endpoint).ExecuteDeleteAsync();
    public Task SendToUserAsync(int userId, string title, string? body, string? link) => SendToUsersAsync(new[] { userId }, title, body, link);
    public async Task SendToUsersAsync(IEnumerable<int> userIds, string title, string? body, string? link)
    {
        await PushQueue.StageAsync(db, userIds, title, body, link); await db.SaveChangesAsync();
    }
}

public interface IPushTransport { Task<int> SendAsync(Inventory.Api.Data.PushSubscription subscription, string payload, CancellationToken ct); }
public class WebPushTransport(PushSettings settings, IHttpClientFactory factory) : IPushTransport
{
    public async Task<int> SendAsync(Inventory.Api.Data.PushSubscription subscription, string payload, CancellationToken ct)
    {
        using var client = new WebPushClient(factory.CreateClient("WebPush"));
        using var json = System.Text.Json.JsonDocument.Parse(payload);
        var expires = json.RootElement.GetProperty("expiresAtUtc").GetDateTime();
        var ttl = Math.Clamp((int)(expires - DateTime.UtcNow).TotalSeconds, 0, 172800);
        try
        {
            await client.SendNotificationAsync(new WebPush.PushSubscription(subscription.Endpoint, subscription.P256DH, subscription.Auth), payload,
                new Dictionary<string, object> { ["vapidDetails"] = new VapidDetails(settings.Subject, settings.PublicKey, settings.PrivateKey), ["TTL"] = ttl, ["headers"] = new Dictionary<string, object> { ["Urgency"] = "high" } }, ct);
            return 201;
        }
        catch (WebPushException ex) { return (int)ex.StatusCode; }
    }
}
