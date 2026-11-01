using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Inventory.Api.Services.ItAssets;

// =====================================================================
//  رمزنگاری گاوصندوق رمزهای تجهیزات
//
//  چرا «هش» نه؟ هش یک‌طرفه است؛ رمزِ هش‌شده دیگر قابل نمایش نیست و گاوصندوق بی‌فایده می‌شود.
//  پس رمزها با AES-256-GCM (رمزنگاری احرازشده) ذخیره می‌شوند:
//    • کلید اصلی ۲۵۶ بیتی «بیرون از دیتابیس» نگه‌داری می‌شود؛ دسترسی به بکاپ دیتابیس به‌تنهایی رمزها را لو نمی‌دهد.
//    • برای هر رکورد یک salt تصادفی ← کلید اختصاصی با HKDF-SHA256 ← nonce تصادفی ← AES-GCM.
//    • شناسهٔ شرکت به‌عنوان AAD در رمزنگاری بسته می‌شود؛ جابه‌جاکردن متن رمز بین شرکت‌ها در دیتابیس کشف می‌شود.
//
//  منبع کلید اصلی (به ترتیب):
//    ۱) CredentialVault:MasterKey (base64 ۳۲ بایتی) — متغیر محیطی:  CredentialVault__MasterKey
//    ۲) فایل CredentialVault:KeyPath
//    ۳) پیش‌فرض ویندوز:  %ProgramData%\Totall\vault\vault.key   (خارج از پوشهٔ پابلیش ← با هر استقرار جدید از بین نمی‌رود)
//       سایر سیستم‌ها:   <ContentRoot>/App_Data/vault/vault.key
//  ⚠️ از فایل کلید بکاپ بگیرید؛ بدون آن رمزهای ذخیره‌شده قابل بازیابی نیستند.
// =====================================================================
public sealed class CredentialVaultCrypto
{
    private const string Prefix = "v1.";
    private const int SaltSize = 16, NonceSize = 12, TagSize = 16, KeySize = 32;
    private static readonly byte[] EncInfo = Encoding.UTF8.GetBytes("Totall.ItVault.Enc.v1");
    private static readonly byte[] UnlockInfo = Encoding.UTF8.GetBytes("Totall.ItVault.Unlock.v1");

    private readonly byte[] _master;
    private readonly byte[] _unlockKey;

    /// <summary>اثر انگشت کوتاه کلید اصلی (برای تشخیص تعویض کلید) — خود کلید را لو نمی‌دهد.</summary>
    public string KeyId { get; }
    public string KeySource { get; }

    /// <summary>مدت اعتبار «باز بودن گاوصندوق» پس از تأیید رمز ورود کاربر.</summary>
    public static readonly TimeSpan UnlockLifetime = TimeSpan.FromMinutes(10);

    // محدودیت تلاش ناموفق برای باز کردن گاوصندوق (هر کاربر): ۵ تلاش ← ۵ دقیقه قفل
    private readonly ConcurrentDictionary<int, (int Fails, DateTime LockedUntil)> _fails = new();

    public CredentialVaultCrypto(IConfiguration cfg, IWebHostEnvironment env)
    {
        (_master, KeySource) = LoadOrCreateMasterKey(cfg, env);
        KeyId = Convert.ToHexString(SHA256.HashData(_master))[..16];
        _unlockKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _master, KeySize, salt: null, info: UnlockInfo);
        Console.WriteLine($"[ItVault] کلید گاوصندوق رمزها آماده است (KeyId={KeyId}، منبع: {KeySource}). از این فایل بکاپ بگیرید.");
    }

    // ------------------------------------------------------------------ رمزنگاری

    public string? Encrypt(string? plain, int companyId)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, _master, KeySize, salt, EncInfo);
        var pt = Encoding.UTF8.GetBytes(plain);
        var ct = new byte[pt.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, pt, ct, tag, Aad(companyId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(pt);
        }
        var blob = new byte[SaltSize + NonceSize + TagSize + ct.Length];
        Buffer.BlockCopy(salt, 0, blob, 0, SaltSize);
        Buffer.BlockCopy(nonce, 0, blob, SaltSize, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, SaltSize + NonceSize, TagSize);
        Buffer.BlockCopy(ct, 0, blob, SaltSize + NonceSize + TagSize, ct.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    /// <summary>رمزگشایی؛ اگر کلید عوض شده یا داده دستکاری شده باشد <see cref="CryptographicException"/> می‌دهد.</summary>
    public string? Decrypt(string? cipher, int companyId)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        if (!cipher.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("قالب دادهٔ رمزنگاری‌شده ناشناخته است.");
        var blob = Convert.FromBase64String(cipher[Prefix.Length..]);
        if (blob.Length < SaltSize + NonceSize + TagSize)
            throw new CryptographicException("دادهٔ رمزنگاری‌شده ناقص است.");
        var salt = blob.AsSpan(0, SaltSize).ToArray();
        var nonce = blob.AsSpan(SaltSize, NonceSize);
        var tag = blob.AsSpan(SaltSize + NonceSize, TagSize);
        var ct = blob.AsSpan(SaltSize + NonceSize + TagSize);
        var pt = new byte[ct.Length];
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, _master, KeySize, salt, EncInfo);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ct, tag, pt, Aad(companyId));
            return Encoding.UTF8.GetString(pt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(pt);
        }
    }

    private static byte[] Aad(int companyId) => Encoding.UTF8.GetBytes($"ItCredential|company:{companyId}");

    // ------------------------------------------------------------------ توکن باز بودن گاوصندوق

    /// <summary>توکن کوتاه‌مدت (۱۰ دقیقه) پس از تأیید رمز ورود کاربر — HMAC-SHA256، بدون حالت سمت سرور.</summary>
    public (string Token, DateTime ExpiresAt) CreateUnlockToken(int userId)
    {
        var exp = DateTimeOffset.UtcNow.Add(UnlockLifetime);
        var payload = $"{userId}|{exp.ToUnixTimeSeconds()}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var mac = HMACSHA256.HashData(_unlockKey, payloadBytes);
        return (B64Url(payloadBytes) + "." + B64Url(mac), exp.LocalDateTime);
    }

    public bool ValidateUnlockToken(string? token, int userId)
    {
        if (string.IsNullOrWhiteSpace(token) || userId <= 0) return false;
        var parts = token.Split('.');
        if (parts.Length != 2) return false;
        byte[] payloadBytes, mac;
        try { payloadBytes = FromB64Url(parts[0]); mac = FromB64Url(parts[1]); }
        catch { return false; }
        var expected = HMACSHA256.HashData(_unlockKey, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, mac)) return false;
        var fields = Encoding.UTF8.GetString(payloadBytes).Split('|');
        if (fields.Length != 2 || !int.TryParse(fields[0], out var uid) || uid != userId) return false;
        if (!long.TryParse(fields[1], out var expUnix)) return false;
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expUnix;
    }

    // ------------------------------------------------------------------ محدودیت تلاش ناموفق

    public bool IsLockedOut(int userId, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (_fails.TryGetValue(userId, out var s) && s.LockedUntil > DateTime.UtcNow)
        {
            remaining = s.LockedUntil - DateTime.UtcNow;
            return true;
        }
        return false;
    }

    public void RegisterFailure(int userId)
    {
        _fails.AddOrUpdate(userId,
            _ => (1, DateTime.MinValue),
            (_, s) =>
            {
                // اگر قبلاً قفل شده و زمان قفل گذشته، شمارش از نو شروع می‌شود
                var fails = s.LockedUntil != DateTime.MinValue ? 1 : s.Fails + 1;
                return fails >= 5 ? (0, DateTime.UtcNow.AddMinutes(5)) : (fails, DateTime.MinValue);
            });
    }

    public void RegisterSuccess(int userId) => _fails.TryRemove(userId, out _);

    // ------------------------------------------------------------------ کلید اصلی

    private static (byte[] Key, string Source) LoadOrCreateMasterKey(IConfiguration cfg, IWebHostEnvironment env)
    {
        var fromCfg = cfg["CredentialVault:MasterKey"];
        if (!string.IsNullOrWhiteSpace(fromCfg))
        {
            try
            {
                var k = Convert.FromBase64String(fromCfg.Trim());
                if (k.Length == KeySize) return (k, "CredentialVault:MasterKey (تنظیمات/متغیر محیطی)");
                Console.WriteLine("[ItVault] ⚠ CredentialVault:MasterKey باید base64 یک کلید ۳۲ بایتی باشد؛ نادیده گرفته شد.");
            }
            catch { Console.WriteLine("[ItVault] ⚠ CredentialVault:MasterKey قالب base64 معتبر ندارد؛ نادیده گرفته شد."); }
        }

        var path = cfg["CredentialVault:KeyPath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            path = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Totall", "vault", "vault.key")
                : Path.Combine(env.ContentRootPath, "App_Data", "vault", "vault.key");
        }

        if (File.Exists(path))
        {
            var text = File.ReadAllText(path).Trim();
            var k = Convert.FromBase64String(text);
            if (k.Length != KeySize) throw new InvalidOperationException($"فایل کلید گاوصندوق نامعتبر است: {path}");
            return (k, path);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllText(path, Convert.ToBase64String(key));
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* محدودسازی مجوز فایل اختیاری است */ }
        Console.WriteLine($"[ItVault] کلید جدید گاوصندوق ساخته شد: {path}");
        return (key, path);
    }

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
