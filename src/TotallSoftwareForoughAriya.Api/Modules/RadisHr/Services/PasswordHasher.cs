using System.Security.Cryptography;

namespace RadisHr.Api.Services;

/// <summary>
/// هش رمز عبور با PBKDF2-SHA256 (۲۱۰٬۰۰۰ تکرار، نمک ۱۶ بایتی).
/// این تنها بخشی است که نسبت به نسخهٔ اصلی عمداً ارتقا یافته؛
/// در نسخهٔ قدیمی رمزها به‌صورت متن آشکار در localStorage نگهداری می‌شد.
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string hash, string salt)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt)) return false;
        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expected = Convert.FromBase64String(hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch { return false; }
    }
}
