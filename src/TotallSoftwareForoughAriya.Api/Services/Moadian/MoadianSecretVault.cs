using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Inventory.Api.Services.Moadian;

/// <summary>
/// گاوصندوق اختصاصی کلید خصوصی، گواهی امضای الکترونیکی و پین توکن سامانه مودیان.
/// • رمزنگاری احرازشده AES-256-GCM با کلید مشتق‌شده HKDF-SHA256 و Salt یکتا برای هر رکورد.
/// • کلید اصلی ۲۵۶ بیتی خارج از دیتابیس نگهداری می‌شود (MoadianVault:MasterKey یا فایل کلید امن سرور).
/// • شناسه شرکت (CompanyId) به عنوان AAD قفل می‌شود تا کپی متن رمز بین شرکت‌ها غیرممکن باشد.
/// • هیچ کلید خصوصی یا گواهی هرگز به صورت فایل متنی خام روی دیسک نوشته نمی‌شود.
/// </summary>
public sealed class MoadianSecretVault
{
    private const string Prefix = "mdn.v2.";
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private static readonly byte[] EncInfo = Encoding.UTF8.GetBytes("Totall.Moadian.CertVault.v2");

    private readonly byte[] _masterKey;

    public string KeyId { get; }
    public string KeySource { get; }

    public MoadianSecretVault(IConfiguration cfg, IWebHostEnvironment env)
    {
        (_masterKey, KeySource) = LoadOrCreateMasterKey(cfg, env);
        KeyId = Convert.ToHexString(SHA256.HashData(_masterKey))[..16];
        Console.WriteLine($"[MoadianVault] کلید رمزنگاری گواهی امضای مودیان آماده است (KeyId={KeyId}، منبع: {KeySource}).");
    }

    public string Encrypt(string plainText, int companyId, string purpose)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("مقدار ورودی برای رمزنگاری خالی است.", nameof(plainText));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var derivedKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _masterKey, KeySize, salt, EncInfo);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(derivedKey, TagSize);
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag, BuildAad(companyId, purpose));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(plainBytes);
        }

        var combined = new byte[SaltSize + NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, SaltSize);
        Buffer.BlockCopy(nonce, 0, combined, SaltSize, NonceSize);
        Buffer.BlockCopy(tag, 0, combined, SaltSize + NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, combined, SaltSize + NonceSize + TagSize, cipherBytes.Length);
        return Prefix + Convert.ToBase64String(combined);
    }

    public byte[] DecryptToBytes(string cipherText, int companyId, string purpose)
    {
        if (string.IsNullOrWhiteSpace(cipherText) || !cipherText.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("فرمت دادهٔ رمزنگاری‌شدهٔ گواهی مودیان معتبر نیست.");

        var combined = Convert.FromBase64String(cipherText[Prefix.Length..]);
        if (combined.Length < SaltSize + NonceSize + TagSize)
            throw new CryptographicException("طول دادهٔ رمزنگاری‌شده نامعتبر است.");

        var salt = combined.AsSpan(0, SaltSize).ToArray();
        var nonce = combined.AsSpan(SaltSize, NonceSize).ToArray();
        var tag = combined.AsSpan(SaltSize + NonceSize, TagSize).ToArray();
        var cipherBytes = combined.AsSpan(SaltSize + NonceSize + TagSize).ToArray();

        var derivedKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _masterKey, KeySize, salt, EncInfo);
        var plainBytes = new byte[cipherBytes.Length];
        try
        {
            using var aes = new AesGcm(derivedKey, TagSize);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes, BuildAad(companyId, purpose));
            return plainBytes;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    /// <summary>
    /// استخراج، اعتبارسنجی ریاضی و تطبیق کلید خصوصی RSA با گواهی امضای X.509 (PEM/CRT/CER یا PFX/P12).
    /// </summary>
    public static ParsedCertificateMaterial ParseAndValidatePkcs8Material(
        string? privateKeyPem,
        string? certificateContent,
        string? pfxBase64,
        string? pfxPassword)
    {
        if (!string.IsNullOrWhiteSpace(pfxBase64))
        {
            byte[] pfxBytes;
            try
            {
                pfxBytes = Convert.FromBase64String(CleanBase64Input(pfxBase64));
            }
            catch
            {
                throw new InvalidOperationException("فایل PFX/P12 انتخاب‌شده قالب Base64 معتبری ندارد.");
            }

            try
            {
                using var pfxCert = new X509Certificate2(
                    pfxBytes,
                    pfxPassword ?? string.Empty,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

                if (!pfxCert.HasPrivateKey)
                    throw new InvalidOperationException("فایل PFX/P12 فاقد کلید خصوصی امضا است.");

                using var rsaFromPfx = pfxCert.GetRSAPrivateKey()
                    ?? throw new InvalidOperationException("کلید خصوصی داخل فایل PFX باید از نوع RSA باشد.");

                ValidateRsaAndCertificateMatch(rsaFromPfx, pfxCert);

                var exportedPem = rsaFromPfx.ExportPkcs8PrivateKeyPem();
                var certDerBase64 = Convert.ToBase64String(pfxCert.Export(X509ContentType.Cert));
                return BuildMetadata(exportedPem, certDerBase64, pfxCert);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException($"بارگذاری فایل PFX/P12 ناموفق بود (رمز عبور یا ساختار فایل را بررسی کنید): {ex.Message}");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfxBytes);
            }
        }

        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new InvalidOperationException("متن یا فایل کلید خصوصی (PEM) وارد نشده است.");
        if (string.IsNullOrWhiteSpace(certificateContent))
            throw new InvalidOperationException("فایل گواهی امضای الکترونیکی (.crt / .cer / PEM) وارد نشده است.");

        using var rsa = ImportRsaPrivateKeyFromText(privateKeyPem);
        using var cert = LoadCertificateFromTextOrBase64(certificateContent);

        ValidateRsaAndCertificateMatch(rsa, cert);

        var normalizedPkcs8Pem = rsa.ExportPkcs8PrivateKeyPem();
        var normalizedCertDerBase64 = Convert.ToBase64String(cert.Export(X509ContentType.Cert));
        return BuildMetadata(normalizedPkcs8Pem, normalizedCertDerBase64, cert);
    }

    public static RSA ImportRsaFromUtf8PemBytes(byte[] utf8PemBytes)
    {
        var chars = Encoding.UTF8.GetChars(utf8PemBytes);
        try
        {
            var rsa = RSA.Create();
            try
            {
                rsa.ImportFromPem(chars);
                return rsa;
            }
            catch
            {
                rsa.Dispose();
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars.AsSpan()));
        }
    }

    public static X509Certificate2 LoadCertificateFromDerBase64Bytes(byte[] utf8Base64Bytes)
    {
        var base64Text = Encoding.UTF8.GetString(utf8Base64Bytes).Trim();
        var derBytes = Convert.FromBase64String(base64Text);
        try
        {
            return new X509Certificate2(derBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derBytes);
        }
    }

    private static RSA ImportRsaPrivateKeyFromText(string input)
    {
        var trimmed = input.Trim();
        var rsa = RSA.Create();
        try
        {
            if (trimmed.Contains("-----BEGIN", StringComparison.Ordinal))
            {
                rsa.ImportFromPem(trimmed);
            }
            else
            {
                var keyBytes = Convert.FromBase64String(CleanBase64Input(trimmed));
                try
                {
                    rsa.ImportPkcs8PrivateKey(keyBytes, out _);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(keyBytes);
                }
            }

            return rsa;
        }
        catch (Exception ex)
        {
            rsa.Dispose();
            throw new InvalidOperationException($"کلید خصوصی معتبر نیست (فرمت PEM بدون رمز PKCS#8 یا RSA PRIVATE KEY مورد نیاز است): {ex.Message}");
        }
    }

    private static X509Certificate2 LoadCertificateFromTextOrBase64(string input)
    {
        var trimmed = input.Trim();
        try
        {
            if (trimmed.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
                return X509Certificate2.CreateFromPem(trimmed);

            var rawBytes = Convert.FromBase64String(CleanBase64Input(trimmed));
            try
            {
                var maybePem = Encoding.UTF8.GetString(rawBytes);
                if (maybePem.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
                    return X509Certificate2.CreateFromPem(maybePem);

                return new X509Certificate2(rawBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rawBytes);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"گواهی امضای الکترونیکی معتبر نیست (فایل .crt یا .cer استاندارد X.509 انتخاب کنید): {ex.Message}");
        }
    }

    private static void ValidateRsaAndCertificateMatch(RSA privateKey, X509Certificate2 certificate)
    {
        if (privateKey.KeySize < 2048)
            throw new InvalidOperationException($"طول کلید RSA باید حداقل ۲۰۴۸ بیت باشد (کلید فعلی: {privateKey.KeySize} بیت).");

        using var publicKey = certificate.GetRSAPublicKey()
            ?? throw new InvalidOperationException("گواهی انتخاب‌شده دارای کلید عمومی RSA نیست.");

        var challenge = RandomNumberGenerator.GetBytes(32);
        var signature = privateKey.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        try
        {
            if (!publicKey.VerifyData(challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new InvalidOperationException("کلید خصوصی انتخاب‌شده با گواهی امضای الکترونیکی مطابقت ندارد!");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(challenge);
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static ParsedCertificateMaterial BuildMetadata(string pkcs8Pem, string certDerBase64, X509Certificate2 cert)
        => new(
            pkcs8Pem,
            certDerBase64,
            cert.Subject,
            cert.Issuer,
            cert.SerialNumber,
            cert.Thumbprint,
            cert.NotBefore.ToUniversalTime(),
            cert.NotAfter.ToUniversalTime());

    private static string CleanBase64Input(string text)
    {
        var commaIndex = text.IndexOf(',');
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
            text = text[(commaIndex + 1)..];
        return text.Replace("\r", "").Replace("\n", "").Replace(" ", "");
    }

    private static byte[] BuildAad(int companyId, string purpose)
        => Encoding.UTF8.GetBytes($"MoadianCertVault|company:{companyId}|purpose:{purpose}");

    private static (byte[] Key, string Source) LoadOrCreateMasterKey(IConfiguration cfg, IWebHostEnvironment env)
    {
        var configured = cfg["MoadianVault:MasterKey"] ?? cfg["CredentialVault:MasterKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                var k = Convert.FromBase64String(configured.Trim());
                if (k.Length == KeySize) return (k, "تنظیمات/متغیر محیطی");
            }
            catch { /* در صورت نامعتبر بودن، از فایل کلید سرور استفاده می‌شود */ }
        }

        var path = cfg["MoadianVault:KeyPath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            path = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Totall", "vault", "moadian-vault.key")
                : Path.Combine(env.ContentRootPath, "App_Data", "vault", "moadian-vault.key");
        }

        if (File.Exists(path))
        {
            var k = Convert.FromBase64String(File.ReadAllText(path).Trim());
            if (k.Length == KeySize) return (k, path);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var generated = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllText(path, Convert.ToBase64String(generated));
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* نادیده گرفتن در فایل‌سیستم‌های غیرپشتیبانی */ }

        return (generated, path);
    }
}

public sealed record ParsedCertificateMaterial(
    string Pkcs8PrivateKeyPem,
    string CertificateDerBase64,
    string Subject,
    string Issuer,
    string SerialNumber,
    string Thumbprint,
    DateTime NotBeforeUtc,
    DateTime NotAfterUtc);
