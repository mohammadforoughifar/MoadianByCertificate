using System.Security.Cryptography;

namespace Inventory.Api.Services;

/// <summary>
/// کدهای یک‌بارمصرفِ اتصال پیام‌رسان (مثلاً ایتا):
/// کاربر در نرم‌افزار کد می‌گیرد و همان کد را داخل برنامک ایتا وارد می‌کند تا
/// شناسه‌ی ایتای او به حساب کاربری‌اش متصل شود. کدها کوتاه‌عمر و در حافظه نگهداری می‌شوند.
/// </summary>
public interface IMessengerLinkCodes
{
    /// <summary>ساخت کد تازه برای کاربر (کد فعال قبلی همان کاربر باطل می‌شود).</summary>
    (string code, DateTime expiresAtUtc) Create(int userId);

    /// <summary>مصرف کد (یک‌بارمصرف) — در صورت موفقیت شناسه کاربر برگردانده می‌شود.</summary>
    bool TryTake(string code, out int userId);

    /// <summary>آیا همین حالا کد فعالی برای این کاربر وجود دارد؟</summary>
    bool HasActive(int userId);
}

public class MessengerLinkCodes : IMessengerLinkCodes
{
    // حروف بدون ابهام (بدون 0/O/1/I)
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private readonly Dictionary<string, (int userId, DateTime expiresAtUtc)> _codes = new(StringComparer.OrdinalIgnoreCase);

    private static string NewCode()
    {
        Span<char> buffer = stackalloc char[6];
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(buffer);
    }

    public (string code, DateTime expiresAtUtc) Create(int userId)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(Lifetime);
        lock (_gate)
        {
            foreach (var key in _codes.Where(kv => kv.Value.expiresAtUtc <= now || kv.Value.userId == userId).Select(kv => kv.Key).ToList())
                _codes.Remove(key);

            string code;
            do { code = NewCode(); } while (_codes.ContainsKey(code));
            _codes[code] = (userId, expires);
            return (code, expires);
        }
    }

    public bool TryTake(string code, out int userId)
    {
        userId = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            foreach (var key in _codes.Where(kv => kv.Value.expiresAtUtc <= now).Select(kv => kv.Key).ToList())
                _codes.Remove(key);

            if (!_codes.TryGetValue(code.Trim(), out var entry)) return false;
            _codes.Remove(code.Trim());
            userId = entry.userId;
            return entry.expiresAtUtc > now;
        }
    }

    public bool HasActive(int userId)
    {
        var now = DateTime.UtcNow;
        lock (_gate) return _codes.Any(kv => kv.Value.userId == userId && kv.Value.expiresAtUtc > now);
    }
}
