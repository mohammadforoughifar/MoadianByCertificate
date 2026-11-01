using System.Security.Cryptography;
using System.Text;

namespace Inventory.Api.Services.Chat;

/// <summary>
/// توکن کوتاه و امضاشده فقط برای «دانلود/پیش‌نمایش فایل‌های پیام‌رسان».
///
/// چرا لازم است: تگ‌های a / img / audio / video نمی‌توانند هدر Authorization بفرستند، پس
/// قبلاً کل JWT در query string می‌آمد. JWT این سیستم همهٔ مجوزها را داخل خودش دارد
/// (برای مدیر ≈ ۱۰ هزار کاراکتر) و سرور با «414 URI Too Long» آن را رد می‌کرد
/// (Kestrel: ۸ کیلوبایت، IIS: ۲ کیلوبایت، nginx: ۸ کیلوبایت) ⇒ هیچ فایلی دانلود نمی‌شد.
///
/// این توکن حدود ۷۰ نویسه است، فقط شناسهٔ کاربر + زمان انقضا دارد و فقط روی مسیرهای
/// <c>/api/chat/…/download</c> و <c>/api/chat/…/preview</c> پذیرفته می‌شود. عضویت در گفتگو
/// همچنان در ChatAttachmentService بررسی می‌شود؛ پس این توکن دسترسی تازه‌ای نمی‌دهد.
/// قالب: <c>{userId}.{expUnix}.{hmac-base64url}</c>
/// </summary>
public static class ChatFileToken
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    private const string Purpose = "chat-file-v1";

    public static (string Token, DateTime ExpiresAtUtc) Create(int userId, DateTime? nowUtc = null)
    {
        var exp = (nowUtc ?? DateTime.UtcNow).Add(Lifetime);
        var unix = new DateTimeOffset(exp).ToUnixTimeSeconds();
        var payload = $"{userId}.{unix}";
        return ($"{payload}.{Sign(payload)}", exp);
    }

    public static bool TryValidate(string? token, out int userId, DateTime? nowUtc = null)
    {
        userId = 0;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return false;
        var parts = token.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var uid) || uid <= 0) return false;
        if (!long.TryParse(parts[1], out var unix)) return false;
        if (DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime < (nowUtc ?? DateTime.UtcNow)) return false;

        var expected = Sign($"{parts[0]}.{parts[1]}");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])))
            return false;
        userId = uid;
        return true;
    }

    private static string Sign(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AuthService.JwtKey + "|" + Purpose));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
