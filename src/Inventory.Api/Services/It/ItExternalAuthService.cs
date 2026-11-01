using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// احراز هویت ماشین‌به‌ماشین بین نرم‌افزارِ شرکت‌های راه‌دور و سرور مرکزی.
/// هر شرکت یک «کلید API» می‌گیرد که فقط هشِ آن نگه‌داری می‌شود؛ کلید در هدر
/// <c>X-It-Api-Key</c> ارسال می‌شود. علاوه بر احراز هویت، سقف نرخِ ساده هم اعمال می‌شود.
/// </summary>
public class ItExternalAuthService
{
    public const string HeaderName = "X-It-Api-Key";
    public const string CodeHeaderName = "X-It-Company-Code";

    private readonly AppDbContext _db;

    // سقف نرخ: (کلید شرکت، بازهٔ دقیقه) → تعداد
    private static readonly ConcurrentDictionary<string, int> Hits = new();
    private static readonly ConcurrentDictionary<string, DateTime> LastSeen = new();

    public ItExternalAuthService(AppDbContext db) => _db = db;

    public static string HashKey(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim())));

    /// <summary>کلید تازه با پیشوند خوانا — فقط همین یک‌بار به مدیر نشان داده می‌شود.</summary>
    public static string NewKey() => "itk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static string Hint(string raw) => raw.Length <= 8 ? raw[..4] : raw[..8];

    /// <summary>مقایسه‌ی مقاوم در برابر حمله‌ی زمانی</summary>
    private static bool FixedEquals(string a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a);
        var y = Encoding.UTF8.GetBytes(b);
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }

    /// <summary>
    /// کلید را اعتبارسنجی و شرکتِ متناظر را برمی‌گرداند.
    /// خروجی: null = معتبر و فعال | پیام خطا = علت رد شدن.
    /// </summary>
    public async Task<(ItClientCompany? Company, string? Error)> AuthenticateAsync(string? rawKey, string? codeHint)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return (null, $"هدر {HeaderName} ارسال نشده است. کلید API شرکت را در تنظیمات نرم‌افزار وارد کنید.");

        rawKey = rawKey.Trim();
        var hash = HashKey(rawKey);

        var companies = await _db.ItClientCompanies.AsNoTracking().Where(c => c.IsActive).ToListAsync();
        if (companies.Count == 0)
            return (null, "هیچ شرکتی در سرور مرکزی تعریف نشده است. مدیر باید از "
                        + "«درخواست خدمت IT ← تنظیمات شرکت‌های راه‌دور» یک شرکت بسازد و کلید API آن را صادر کند.");

        var match = companies.FirstOrDefault(c => FixedEquals(c.ApiKeyHash, hash));
        if (match == null)
        {
            // اگر کد شرکت درست باشد ولی کلید اشتباه، پیام دقیق‌تری می‌دهیم
            if (!string.IsNullOrWhiteSpace(codeHint))
            {
                var byCode = companies.FirstOrDefault(c =>
                    string.Equals(c.Code, codeHint.Trim(), StringComparison.OrdinalIgnoreCase));
                if (byCode != null)
                    return (null, $"کلید API برای شرکت «{byCode.Name}» نادرست است. "
                                + "(اگر کلید را تعویض کرده‌اید، کلید جدید را در نرم‌افزار شرکت وارد کنید.)");
            }
            return (null, "کلید API نامعتبر است یا غیرفعال شده است.");
        }

        if (!string.IsNullOrWhiteSpace(codeHint) &&
            !string.Equals(match.Code, codeHint.Trim(), StringComparison.OrdinalIgnoreCase))
            return (null, $"این کلید متعلق به شرکت «{match.Name}» است، نه «{codeHint.Trim()}».");

        return (match, null);
    }

    /// <summary>سقف نرخ ساعتی شرکت. true = مجاز.</summary>
    public bool AllowRequest(ItClientCompany company)
    {
        if (company.HourlyLimit <= 0) return true;
        var now = DateTime.Now;
        var hourWindow = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var count = Hits.AddOrUpdate($"{company.Id}:h:{hourWindow:yyyyMMddHH}", 1, (_, v) => v + 1);
        if (count > company.HourlyLimit) return false;

        // ضدّ سیل کوتاه‌مدت: حداکثر ۲۰ درخواست در هر دقیقه
        var minuteWindow = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
        var perMinute = Hits.AddOrUpdate($"{company.Id}:m:{minuteWindow:yyyyMMddHHmm}", 1, (_, v) => v + 1);
        if (perMinute > 20)
        {
            Hits.TryUpdate($"{company.Id}:h:{hourWindow:yyyyMMddHH}", count - 1, count);
            return false;
        }
        return true;
    }

    public void Touch(ItClientCompany company) => LastSeen[company.Id.ToString()] = DateTime.Now;

    /// <summary>پاک‌سازی پنجره‌های منقضی‌شده (فراخوانی اختیاری از سرویس پس‌زمینه)</summary>
    public static void Trim()
    {
        var cutoffHour = DateTime.Now.AddHours(-2);
        var cutoffMinute = DateTime.Now.AddMinutes(-10);
        foreach (var k in Hits.Keys)
        {
            var parts = k.Split(':');
            if (parts.Length != 3 || !DateTime.TryParseExact(parts[2],
                    parts[1] == "h" ? "yyyyMMddHH" : "yyyyMMddHHmm", null,
                    System.Globalization.DateTimeStyles.None, out var w))
                continue;
            if ((parts[1] == "h" ? w < cutoffHour : w < cutoffMinute))
                Hits.TryRemove(k, out _);
        }
    }
}
