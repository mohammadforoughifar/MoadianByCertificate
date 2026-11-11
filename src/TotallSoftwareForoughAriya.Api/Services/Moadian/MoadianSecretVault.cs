using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Inventory.Api.Services.Moadian;

// نگهداری رمزنگاری‌شده کلید خصوصی، گواهی امضا و پین توکن مودیان
public sealed class MoadianSecretVault
{
    private const string Prefix = "mdn.v2.";
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private static readonly byte[] EncInfo = Encoding.UTF8.GetBytes("Totall.Moadian.CertVault.v2");

    private static readonly Regex PemBlockRegex = new(
        @"-{3,}\s*BEGIN\s+([A-Z0-9 #._-]+?)\s*-{3,}([\s\S]*?)-{3,}\s*END\s+\1\s*-{3,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
    /// استخراج، اعتبارسنجی ریاضی و تطبیق کلید خصوصی RSA با گواهی امضای X.509
    /// (پشتیبانی کامل از PEM، CRT، CER باینری DER، Base64 خام، UTF-8 BOM، UTF-16، زنجیره PKCS#7/.p7b و PFX/P12).
    /// </summary>
    public static ParsedCertificateMaterial ParseAndValidatePkcs8Material(
        string? privateKeyPem,
        string? certificateContent,
        string? pfxBase64,
        string? pfxPassword,
        string? privateKeyPassword = null)
    {
        var effectivePfxPassword = !string.IsNullOrEmpty(pfxPassword) ? pfxPassword : privateKeyPassword;

        if (!string.IsNullOrWhiteSpace(pfxBase64))
        {
            if (!TryDecodeFlexibleBase64(pfxBase64, out var pfxBytes) || pfxBytes.Length == 0)
                throw new InvalidOperationException("فایل PFX/P12 انتخاب‌شده قالب Base64 معتبری ندارد.");

            return ExtractFromPkcs12Bytes(pfxBytes, effectivePfxPassword);
        }

        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new InvalidOperationException("متن یا فایل کلید خصوصی (PEM / .key) وارد نشده است.");
        if (string.IsNullOrWhiteSpace(certificateContent))
            throw new InvalidOperationException("فایل گواهی امضای الکترونیکی (.crt / .cer / .pem) وارد نشده است.");

        // اگر کاربر به اشتباه فایل تجمیعی PFX/P12 را در زبانهٔ اول بارگذاری کرده باشد، خودکار تشخیص داده می‌شود.
        if (TryDetectPkcs12Bytes(certificateContent, out var certAsPfx))
            return ExtractFromPkcs12Bytes(certAsPfx, effectivePfxPassword);
        if (TryDetectPkcs12Bytes(privateKeyPem, out var keyAsPfx))
            return ExtractFromPkcs12Bytes(keyAsPfx, effectivePfxPassword);

        var keyInput = privateKeyPem;
        var certInput = certificateContent;

        // اگر کاربر کادر اول (کلید خصوصی) و کادر دوم (گواهی امضا) را جابه‌جا پر کرده باشد، خودکار اصلاح می‌شود.
        if (LooksLikeCertificateOnly(keyInput) && LooksLikePrivateKeyOnly(certInput))
            (keyInput, certInput) = (certInput, keyInput);

        using var rsa = ImportRsaPrivateKeyFromInput(keyInput, privateKeyPassword ?? pfxPassword);
        if (rsa.KeySize < 2048)
            throw new InvalidOperationException($"طول کلید RSA باید حداقل ۲۰۴۸ بیت باشد (کلید فعلی: {rsa.KeySize} بیت).");

        var candidates = LoadCandidateCertificates(certInput);
        try
        {
            X509Certificate2? matched = null;
            foreach (var c in candidates)
            {
                if (!MoadianPkcs11Inspector.IsCaCertificate(c) && DoesRsaMatchCertificate(rsa, c))
                {
                    matched = c;
                    break;
                }
            }

            // اگر هیچ گواهی غیر CA منطبق نشد، گواهی‌های دیگر (مثل گواهی خودامضای آزمایشی با پرچم CA) هم بررسی می‌شوند
            if (matched is null)
            {
                foreach (var c in candidates)
                {
                    if (DoesRsaMatchCertificate(rsa, c))
                    {
                        matched = c;
                        break;
                    }
                }
            }

            if (matched is null)
                throw new InvalidOperationException(BuildDetailedKeyCertMismatchMessage(rsa, candidates));

            var normalizedPkcs8Pem = rsa.ExportPkcs8PrivateKeyPem();
            var normalizedCertDerBase64 = Convert.ToBase64String(matched.Export(X509ContentType.Cert));
            return BuildMetadata(normalizedPkcs8Pem, normalizedCertDerBase64, matched);
        }
        finally
        {
            foreach (var c in candidates)
                c.Dispose();
        }
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
#pragma warning disable SYSLIB0057 // جایگزین X509CertificateLoader فقط در .NET 9 موجود است
            return new X509Certificate2(derBytes);
#pragma warning restore SYSLIB0057
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derBytes);
        }
    }

    // استخراج PFX / PKCS#12

    private static ParsedCertificateMaterial ExtractFromPkcs12Bytes(byte[] pfxBytes, string? password)
    {
        try
        {
            var detected = X509Certificate2.GetCertContentType(pfxBytes);
            if (detected == X509ContentType.Cert || detected == X509ContentType.Pkcs7)
            {
                throw new InvalidOperationException(
                    "علت عدم تطابق: فایل انتخاب‌شده فقط شامل گواهی عمومی (.cer / .crt / .p7b بدون کلید خصوصی) است، نه فایل تجمیعی PFX/P12! لطفاً کلید خصوصی و این فایل گواهی را در زبانهٔ اول «کلید خصوصی + گواهی (.pem / .crt)» بارگذاری کنید.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch { /* ادامه با تلاش بارگذاری مستقیم PFX */ }

        var flags = OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.Exportable
            : X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable;

        var collection = new X509Certificate2Collection();
        try
        {
#pragma warning disable SYSLIB0057
            collection.Import(pfxBytes, password ?? string.Empty, flags);
#pragma warning restore SYSLIB0057

            if (collection.Count == 0)
                throw new InvalidOperationException("هیچ گواهی X.509 داخل فایل PFX/P12 یافت نشد.");

            var signingCert = collection.Cast<X509Certificate2>()
                .FirstOrDefault(c => c.HasPrivateKey && !MoadianPkcs11Inspector.IsCaCertificate(c))
                ?? collection.Cast<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey);

            if (signingCert is null)
            {
                var subjects = string.Join(" | ", collection.Cast<X509Certificate2>().Select(c => c.Subject));
                throw new InvalidOperationException(
                    $"علت عدم تطابق: فایل PFX/P12 باز شد (شامل {collection.Count} گواهی: {subjects})، اما فاقد کلید خصوصی است! هنگام خروجی گرفتن (Export) از گواهی، گزینهٔ «Yes, export the private key» انتخاب نشده است.");
            }

            using var rsaFromPfx = signingCert.GetRSAPrivateKey()
                ?? throw new InvalidOperationException(
                    $"علت عدم تطابق: کلید خصوصی داخل فایل PFX از نوع غیر RSA ({signingCert.PublicKey.Oid?.FriendlyName ?? signingCert.PublicKey.Oid?.Value}) است؛ سامانه مودیان فقط کلید RSA (حداقل ۲۰۴۸ بیت) را می‌پذیرد.");

            ValidateRsaAndCertificateMatch(rsaFromPfx, signingCert);

            var exportedPem = rsaFromPfx.ExportPkcs8PrivateKeyPem();
            var certDerBase64 = Convert.ToBase64String(signingCert.Export(X509ContentType.Cert));
            return BuildMetadata(exportedPem, certDerBase64, signingCert);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (CryptographicException ex)
        {
            var pwdHint = string.IsNullOrEmpty(password)
                ? "این فایل PFX/P12 رمزگذاری شده است؛ لطفاً رمز عبور فایل PFX را وارد کنید."
                : "رمز عبور واردشده برای فایل PFX/P12 نادرست است یا ساختار فایل آسیب دیده است.";
            throw new InvalidOperationException($"{pwdHint} (جزئیات فنی: {ex.Message})");
        }
        finally
        {
            foreach (var cert in collection)
                cert.Dispose();
            CryptographicOperations.ZeroMemory(pfxBytes);
        }
    }

    private static bool TryDetectPkcs12Bytes(string input, out byte[] pfxBytes)
    {
        pfxBytes = Array.Empty<byte>();
        var cleaned = StripInvisibleCharacters(input).Trim();
        if (cleaned.Contains("-----BEGIN", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryDecodeFlexibleBase64(cleaned, out var raw) || raw.Length < 32)
            return false;

        try
        {
            if (X509Certificate2.GetCertContentType(raw) == X509ContentType.Pkcs12)
            {
                pfxBytes = raw;
                return true;
            }
        }
        catch { /* فرمت دیگر */ }

        CryptographicOperations.ZeroMemory(raw);
        return false;
    }

    // پارس هوشمند کلید خصوصی RSA

    private static RSA ImportRsaPrivateKeyFromInput(string rawInput, string? password)
    {
        var text = UnwrapTextIfBase64EncodedText(StripInvisibleCharacters(rawInput).Trim());
        text = ExtractJsonStringFieldIfPresent(text, "privateKey", "private_key", "key");

        var pemBlocks = ExtractPemBlocks(text);
        if (pemBlocks.Count > 0)
        {
            foreach (var (label, fullPem, body) in pemBlocks)
            {
                if (label.Contains("CERTIFICATE REQUEST", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("در کادر کلید خصوصی به اشتباه فایل «درخواست صدور گواهی (CSR)» وارد شده است. فایل کلید خصوصی (.key / .pem حاوی PRIVATE KEY) را انتخاب کنید.");

                if (label.Equals("PUBLIC KEY", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("RSA PUBLIC KEY", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("در کادر کلید خصوصی به اشتباه فایل «کلید عمومی (Public Key)» وارد شده است. فایل کلید خصوصی (PRIVATE KEY) را انتخاب کنید.");

                if (label.Contains("CERTIFICATE", StringComparison.OrdinalIgnoreCase) &&
                    !label.Contains("PRIVATE", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("در کادر کلید خصوصی به اشتباه فایل «گواهی امضا (Certificate)» وارد شده است. فایل کلید خصوصی را در کادر اول قرار دهید.");

                if (label.Contains("EC PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("DSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("OPENSSH PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"کلید خصوصی از نوع «{label}» پشتیبانی نمی‌شود؛ سامانه مودیان به کلید خصوصی استاندارد RSA (PKCS#8 یا RSA PRIVATE KEY) نیاز دارد.");

                if (!label.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
                    continue;

                var isEncrypted = label.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase) ||
                                  body.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase);

                var rsa = RSA.Create();
                try
                {
                    if (isEncrypted)
                    {
                        if (string.IsNullOrEmpty(password))
                            throw new InvalidOperationException("فایل کلید خصوصی رمزگذاری شده است؛ لطفاً رمز کلید خصوصی را در کادر مربوطه وارد کنید.");
                        rsa.ImportFromEncryptedPem(fullPem, password);
                    }
                    else
                    {
                        rsa.ImportFromPem(fullPem);
                    }
                    return rsa;
                }
                catch (InvalidOperationException)
                {
                    rsa.Dispose();
                    throw;
                }
                catch (Exception ex)
                {
                    rsa.Dispose();
                    throw new InvalidOperationException(
                        isEncrypted
                            ? $"رمز کلید خصوصی نادرست است یا ساختار کلید پشتیبانی نمی‌شود: {ex.Message}"
                            : $"کلید خصوصی معتبر نیست (فرمت استاندارد PKCS#8 یا RSA PRIVATE KEY مورد نیاز است): {ex.Message}");
                }
            }
        }

        // حالت بدون هدر PEM: کلید خصوصی به صورت DER باینری یا Base64 خام
        if (!TryDecodeFlexibleBase64(text, out var keyBytes) || keyBytes.Length == 0)
            throw new InvalidOperationException("کلید خصوصی انتخاب‌شده ساختار PEM یا Base64 معتبری ندارد (فایل .pem یا .key استاندارد انتخاب کنید).");

        try
        {
            // اگر فایل روی دیسک خودش متن Base64 یا PEM بوده و مرورگر آن را یک لایه دیگر Base64 کرده باشد:
            if (TryDecodeBytesAsCleanText(keyBytes, out var innerText) &&
                (innerText.Contains("-----BEGIN", StringComparison.OrdinalIgnoreCase) ||
                 TryDecodeFlexibleBase64(innerText, out _)))
            {
                if (!string.Equals(innerText, text, StringComparison.Ordinal))
                    return ImportRsaPrivateKeyFromInput(innerText, password);
            }

            return ImportRsaFromDerBytes(keyBytes, password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static RSA ImportRsaFromDerBytes(byte[] derBytes, string? password)
    {
        var rsa = RSA.Create();
        try
        {
            try
            {
                rsa.ImportPkcs8PrivateKey(derBytes, out _);
                return rsa;
            }
            catch (CryptographicException) { /* تلاش با PKCS#1 */ }

            try
            {
                rsa.ImportRSAPrivateKey(derBytes, out _);
                return rsa;
            }
            catch (CryptographicException) { /* تلاش با Encrypted PKCS#8 */ }

            if (!string.IsNullOrEmpty(password))
            {
                rsa.ImportEncryptedPkcs8PrivateKey(password, derBytes, out _);
                return rsa;
            }

            throw new InvalidOperationException(
                "کلید خصوصی قابل خواندن نیست (اگر فایل کلید رمز دارد، رمز آن را وارد کنید؛ در غیر این صورت کلید خصوصی بدون رمز PKCS#8 یا RSA انتخاب نمایید).");
        }
        catch (InvalidOperationException)
        {
            rsa.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            rsa.Dispose();
            throw new InvalidOperationException($"کلید خصوصی معتبر نیست: {ex.Message}");
        }
    }

    // پارس هوشمند گواهی امضای X.509

    private static List<X509Certificate2> LoadCandidateCertificates(string rawInput, int depth = 0)
    {
        var cleaned = StripInvisibleCharacters(rawInput).Trim();
        cleaned = ExtractJsonStringFieldIfPresent(cleaned, "certificate", "cert", "crt", "x509", "cer");

        if (string.IsNullOrWhiteSpace(cleaned))
            throw new InvalidOperationException("محتوای فایل گواهی امضای الکترونیکی خالی است.");

        // ۱) بررسی بلوک‌های متنی PEM (با پشتیبانی از Bag Attributes، حروف کوچک/بزرگ، زنجیرهٔ چندگواهی و PKCS#7)
        var pemBlocks = ExtractPemBlocks(cleaned);
        if (pemBlocks.Count > 0)
            return LoadCertificatesFromPemBlocks(pemBlocks);

        // تشخیص خطاهای رایج متنی (HTML، JSON، XML، هدر ناقص PEM)
        ThrowIfRecognizedNonCertificateText(cleaned);

        // ۲) رمزگشایی Base64 انعطاف‌پذیر (برای فایل‌های باینری DER، PKCS#7/.p7b، یا گواهی‌های Base64 بدون هدر PEM)
        if (!TryDecodeFlexibleBase64(cleaned, out var rawBytes) || rawBytes.Length == 0)
        {
            throw new InvalidOperationException(
                "گواهی امضای الکترونیکی معتبر نیست؛ متن یا فایل انتخاب‌شده نه قالب PEM (-----BEGIN CERTIFICATE-----) دارد و نه یک گواهی باینری/Base64 استاندارد (.crt / .cer) است.");
        }

        try
        {
            // تشخیص فایل‌های اشتباهی رایج (ZIP، PDF، تصویر و ...)
            ThrowIfRecognizedNonCertificateBinary(rawBytes);

            // اگر فایل انتخاب‌شده روی دیسک خودش متنی بوده (UTF-8 با BOM، UTF-16، PEM، یا Base64 خام بدون هدر)
            // و توسط InputFile به Base64 تبدیل شده است، یک لایه بازگشایی می‌شود:
            if (depth < 2 && TryDecodeBytesAsCleanText(rawBytes, out var innerText))
            {
                var trimmedInner = innerText.Trim();
                if (!string.IsNullOrEmpty(trimmedInner) &&
                    !string.Equals(trimmedInner, cleaned, StringComparison.Ordinal))
                {
                    if (ExtractPemBlocks(trimmedInner).Count > 0)
                        return LoadCandidateCertificates(trimmedInner, depth + 1);

                    ThrowIfRecognizedNonCertificateText(trimmedInner);

                    if (TryDecodeFlexibleBase64(trimmedInner, out var innerDer) && innerDer.Length > 0)
                    {
                        try
                        {
                            var fromInner = TryLoadCertificatesFromBinaryBytes(innerDer);
                            if (fromInner.Count > 0)
                                return fromInner;
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(innerDer);
                        }
                    }
                }
            }

            var certs = TryLoadCertificatesFromBinaryBytes(rawBytes);
            if (certs.Count > 0)
                return certs;

            throw new InvalidOperationException(
                "ساختار گواهی امضای الکترونیکی X.509 قابل شناسایی نیست. لطفاً فایل گواهی صادرشده (.crt، .cer یا .p7b) از مرکز صدور گواهی را انتخاب کنید.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawBytes);
        }
    }

    private static List<X509Certificate2> LoadCertificatesFromPemBlocks(
        List<(string Label, string FullPem, string Body)> pemBlocks)
    {
        var certs = new List<X509Certificate2>();
        var sawCsr = false;
        var sawPublicKey = false;
        var sawPrivateKey = false;

        try
        {
            foreach (var (label, _, body) in pemBlocks)
            {
                if (label.Contains("CERTIFICATE REQUEST", StringComparison.OrdinalIgnoreCase))
                {
                    sawCsr = true;
                    continue;
                }

                if (label.Equals("PUBLIC KEY", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("RSA PUBLIC KEY", StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("SSH2 PUBLIC KEY", StringComparison.OrdinalIgnoreCase))
                {
                    sawPublicKey = true;
                    continue;
                }

                if (label.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
                {
                    sawPrivateKey = true;
                    continue;
                }

                if (label.Equals("CERTIFICATE", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("X509 CERTIFICATE", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("TRUSTED CERTIFICATE", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("PKCS7", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("CMS", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryDecodeFlexibleBase64(body, out var blockBytes) || blockBytes.Length == 0)
                        continue;

                    try
                    {
                        var loaded = TryLoadCertificatesFromBinaryBytes(blockBytes);
                        certs.AddRange(loaded);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(blockBytes);
                    }
                }
            }

            if (certs.Count > 0)
                return certs;

            if (sawCsr)
                throw new InvalidOperationException(
                    "فایل بارگذاری‌شده «درخواست صدور گواهی (CSR)» است، نه خودِ گواهی امضای الکترونیکی! باید فایل گواهی نهایی (.crt یا .cer) دریافت‌شده از مرکز صدور گواهی (مانند GICA / ریشه / عقیق / پردیس / عصر دانش‌افزار) را انتخاب کنید.");

            if (sawPublicKey)
                throw new InvalidOperationException(
                    "فایل انتخاب‌شده کلید عمومی (Public Key) است، نه گواهی امضا. فایل گواهی صادرشده (.crt یا .cer) را انتخاب کنید.");

            if (sawPrivateKey)
                throw new InvalidOperationException(
                    "در کادر گواهی امضای الکترونیکی به اشتباه فایل «کلید خصوصی (Private Key)» انتخاب شده است! کلید خصوصی را در کادر اول و فایل گواهی (.crt / .cer) را در کادر دوم قرار دهید.");

            var labels = string.Join(", ", pemBlocks.Select(b => b.Label).Distinct());
            throw new InvalidOperationException(
                $"نوع فایل PEM انتخاب‌شده ({labels}) یک گواهی امضای استاندارد X.509 (CERTIFICATE) نیست.");
        }
        catch
        {
            foreach (var c in certs)
                c.Dispose();
            throw;
        }
    }

    private static List<X509Certificate2> TryLoadCertificatesFromBinaryBytes(byte[] rawBytes)
    {
        var list = new List<X509Certificate2>();
        try
        {
            X509ContentType contentType = X509ContentType.Unknown;
            try { contentType = X509Certificate2.GetCertContentType(rawBytes); }
            catch { /* تلاش مستقیم در ادامه */ }

            if (contentType == X509ContentType.Pkcs7)
            {
                var col = new X509Certificate2Collection();
#pragma warning disable SYSLIB0057
                col.Import(rawBytes);
#pragma warning restore SYSLIB0057
                foreach (var c in col) list.Add(c);
                if (list.Count > 0) return list;
            }

            if (contentType == X509ContentType.Pkcs12)
                throw new InvalidOperationException(
                    "فایل انتخاب‌شده از نوع تجمیعی PFX/P12 است؛ لطفاً آن را از زبانهٔ «فایل تجمیعی (.pfx / .p12)» به همراه رمز عبور بارگذاری کنید.");

            try
            {
                var col = new X509Certificate2Collection();
#pragma warning disable SYSLIB0057
                col.Import(rawBytes);
#pragma warning restore SYSLIB0057
                foreach (var c in col) list.Add(c);
                if (list.Count > 0) return list;
            }
            catch { /* تلاش تک‌گواهی */ }

            try
            {
#pragma warning disable SYSLIB0057
                list.Add(new X509Certificate2(rawBytes));
#pragma warning restore SYSLIB0057
                return list;
            }
            catch { /* نامعتبر */ }

            return list;
        }
        catch (InvalidOperationException)
        {
            foreach (var c in list) c.Dispose();
            throw;
        }
        catch
        {
            foreach (var c in list) c.Dispose();
            return new List<X509Certificate2>();
        }
    }

    // تشخیص و نرمال‌سازی متن و Base64

    private static List<(string Label, string FullPem, string Body)> ExtractPemBlocks(string text)
    {
        var results = new List<(string, string, string)>();
        foreach (Match m in PemBlockRegex.Matches(text))
        {
            var label = Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", " ").ToUpperInvariant();
            var body = m.Groups[2].Value;
            // ساخت نسخهٔ استاندارد ۵ خط‌تیره‌ای برای سازگاری کامل با ImportFromPem / CreateFromPem
            var canonicalPem = $"-----BEGIN {label}-----\n{body.Trim()}\n-----END {label}-----";
            results.Add((label, canonicalPem, body));
        }
        return results;
    }

    private static bool TryDecodeFlexibleBase64(string text, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = StripInvisibleCharacters(text).Trim();

        // حذف پیشوند Data URL در صورت وجود (data:application/x-x509-ca-cert;base64,...)
        var commaIndex = s.IndexOf(',');
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
            s = s[(commaIndex + 1)..];

        // حذف کوتیشن‌های احتمالی اطراف رشته
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1];

        var allowUrlSafeChars = !s.Contains('+') && !s.Contains('/');

        // فیلتر خط‌به‌خط برای حذف هدرهای احتمالی RFC1421/OpenSSL (مثل Comment: یا subject= یا -----BEGIN)
        var sb = new StringBuilder(s.Length);
        using (var reader = new StringReader(s))
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.Length == 0) continue;
                if (trimmedLine.StartsWith("---", StringComparison.Ordinal) ||
                    trimmedLine.StartsWith("#", StringComparison.Ordinal))
                    continue;
                // خطوط متادیتای OpenSSL / MIME مثل Proc-Type:, DEK-Info:, Bag Attributes, subject=, issuer=
                if (trimmedLine.Contains(':') ||
                    trimmedLine.StartsWith("Bag Attributes", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("subject=", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("issuer=", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var ch in trimmedLine)
                {
                    if (char.IsWhiteSpace(ch)) continue;
                    if (allowUrlSafeChars && ch == '-') sb.Append('+');
                    else if (allowUrlSafeChars && ch == '_') sb.Append('/');
                    else sb.Append(ch);
                }
            }
        }

        if (sb.Length < 16) return false;

        // نرمال‌سازی کاراکترهای Padding (=) در انتهای رشته
        while (sb.Length > 0 && sb[^1] == '=')
            sb.Length--;

        // بررسی اینکه تمام کاراکترهای باقی‌مانده واقعاً در الفبای Base64 باشند
        for (var i = 0; i < sb.Length; i++)
        {
            var c = sb[i];
            var isB64 = (c >= 'A' && c <= 'Z') ||
                        (c >= 'a' && c <= 'z') ||
                        (c >= '0' && c <= '9') ||
                        c == '+' || c == '/';
            if (!isB64) return false;
        }

        var mod = sb.Length % 4;
        if (mod == 1) return false;
        if (mod == 2) sb.Append("==");
        else if (mod == 3) sb.Append('=');

        try
        {
            bytes = Convert.FromBase64String(sb.ToString());
            return bytes.Length > 0;
        }
        catch
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static string UnwrapTextIfBase64EncodedText(string input)
    {
        if (input.Contains("-----BEGIN", StringComparison.OrdinalIgnoreCase))
            return input;

        if (TryDecodeFlexibleBase64(input, out var decoded))
        {
            try
            {
                if (TryDecodeBytesAsCleanText(decoded, out var innerText) &&
                    innerText.Contains("-----BEGIN", StringComparison.OrdinalIgnoreCase))
                {
                    return innerText;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
        }

        return input;
    }

    private static bool TryDecodeBytesAsCleanText(byte[] bytes, out string text)
    {
        text = string.Empty;
        if (bytes.Length < 8) return false;

        // گواهی باینری DER مستقیماً با تگ ASN.1 SEQUENCE (0x30 0x81/0x82/0x83) آغاز می‌شود
        if (bytes[0] == 0x30 && (bytes[1] == 0x81 || bytes[1] == 0x82 || bytes[1] == 0x83 || bytes[1] < 0x80))
            return false;

        string candidate;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            candidate = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            candidate = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            candidate = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else if (LooksLikeUtf16LeAscii(bytes))
        {
            candidate = Encoding.Unicode.GetString(bytes);
        }
        else if (LooksLikeUtf16BeAscii(bytes))
        {
            candidate = Encoding.BigEndianUnicode.GetString(bytes);
        }
        else
        {
            // بررسی اینکه بایت‌ها متن قابل چاپ UTF-8/ASCII باشند
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                if (b == 0x00) return false;
                if (b < 0x09 || (b > 0x0D && b < 0x20)) return false;
            }
            candidate = Encoding.UTF8.GetString(bytes);
        }

        candidate = StripInvisibleCharacters(candidate);
        if (candidate.Contains('\uFFFD')) return false;

        text = candidate;
        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool LooksLikeUtf16LeAscii(byte[] bytes)
    {
        if (bytes.Length < 32 || bytes.Length % 2 != 0) return false;
        var samplePairs = Math.Min(bytes.Length / 2, 64);
        var zeroHigh = 0;
        for (var i = 0; i < samplePairs; i++)
        {
            var lo = bytes[i * 2];
            var hi = bytes[i * 2 + 1];
            if (hi == 0 && (lo == '\r' || lo == '\n' || lo == '\t' || (lo >= 0x20 && lo <= 0x7E)))
                zeroHigh++;
        }
        return zeroHigh >= samplePairs * 3 / 4;
    }

    private static bool LooksLikeUtf16BeAscii(byte[] bytes)
    {
        if (bytes.Length < 32 || bytes.Length % 2 != 0) return false;
        var samplePairs = Math.Min(bytes.Length / 2, 64);
        var zeroHigh = 0;
        for (var i = 0; i < samplePairs; i++)
        {
            var hi = bytes[i * 2];
            var lo = bytes[i * 2 + 1];
            if (hi == 0 && (lo == '\r' || lo == '\n' || lo == '\t' || (lo >= 0x20 && lo <= 0x7E)))
                zeroHigh++;
        }
        return zeroHigh >= samplePairs * 3 / 4;
    }

    private static string StripInvisibleCharacters(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            // حذف BOM، کاراکترهای صفرعرض (ZWNJ/ZWSP) و نشانگرهای جهت متن (LRM/RLM/BiDi)
            if (ch is '\uFEFF' or '\0' or '\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F' ||
                (ch >= '\u202A' && ch <= '\u202E') ||
                (ch >= '\u2066' && ch <= '\u2069'))
            {
                continue;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string ExtractJsonStringFieldIfPresent(string text, params string[] fieldNames)
    {
        if (text.Length < 4 || text[0] != '{' || text[^1] != '}')
            return text;

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return text;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                foreach (var name in fieldNames)
                {
                    if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var val = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
            }
        }
        catch { /* متن JSON نیست */ }

        return text;
    }

    private static void ThrowIfRecognizedNonCertificateText(string text)
    {
        if (text.Contains("BEGIN CERTIFICATE REQUEST", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("BEGIN NEW CERTIFICATE REQUEST", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "فایل بارگذاری‌شده «درخواست صدور گواهی (CSR)» است، نه خودِ گواهی امضای الکترونیکی! باید فایل گواهی نهایی (.crt یا .cer) دریافت‌شده از مرکز صدور گواهی (GICA / ریشه / عقیق / پردیس) را انتخاب کنید.");
        }

        if (text.Contains("BEGIN PUBLIC KEY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("BEGIN RSA PUBLIC KEY", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "فایل انتخاب‌شده کلید عمومی (Public Key) است، نه گواهی امضا. فایل گواهی صادرشده (.crt یا .cer) را انتخاب کنید.");
        }

        if (text.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("BEGIN RSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("BEGIN ENCRYPTED PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "در کادر گواهی امضا به اشتباه «کلید خصوصی (Private Key)» وارد شده است، یا تگ پایانی -----END----- ناقص است.");
        }

        if (text.Contains("-----BEGIN", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ساختار بلوک متنی PEM ناقص است (خط -----BEGIN ...----- یا -----END ...----- به‌طور کامل کپی نشده است).");
        }

        if (text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "فایل انتخاب‌شده یک صفحهٔ وب (HTML/XML) است، نه فایل گواهی امضای الکترونیکی (.crt / .cer)!");
        }
    }

    private static void ThrowIfRecognizedNonCertificateBinary(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K' && bytes[2] == 0x03 && bytes[3] == 0x04)
            throw new InvalidOperationException(
                "فایل انتخاب‌شده یک آرشیو فشرده (ZIP) است؛ ابتدا آن را از حالت فشرده خارج کرده و فایل گواهی (.crt یا .cer) داخل آن را انتخاب کنید.");

        if (bytes.Length >= 4 && bytes[0] == (byte)'%' && bytes[1] == (byte)'P' && bytes[2] == (byte)'D' && bytes[3] == (byte)'F')
            throw new InvalidOperationException(
                "فایل انتخاب‌شده یک سند PDF است، نه فایل گواهی امضای الکترونیکی (.crt یا .cer)!");

        if (bytes.Length >= 6 && bytes[0] == (byte)'R' && bytes[1] == (byte)'a' && bytes[2] == (byte)'r' && bytes[3] == (byte)'!')
            throw new InvalidOperationException(
                "فایل انتخاب‌شده یک آرشیو فشرده (RAR) است؛ ابتدا آن را استخراج کرده و فایل گواهی (.crt یا .cer) داخل آن را انتخاب کنید.");
    }

    private static bool LooksLikeCertificateOnly(string input)
    {
        var t = UnwrapTextIfBase64EncodedText(StripInvisibleCharacters(input).Trim());
        return t.Contains("BEGIN CERTIFICATE-----", StringComparison.OrdinalIgnoreCase) &&
               !t.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePrivateKeyOnly(string input)
    {
        var t = UnwrapTextIfBase64EncodedText(StripInvisibleCharacters(input).Trim());
        return t.Contains("PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase) &&
               !t.Contains("BEGIN CERTIFICATE-----", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DoesRsaMatchCertificate(RSA privateKey, X509Certificate2 certificate)
    {
        using var publicKey = certificate.GetRSAPublicKey();
        if (publicKey is null) return false;

        var challenge = RandomNumberGenerator.GetBytes(32);
        var signature = privateKey.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        try
        {
            return publicKey.VerifyData(challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(challenge);
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static void ValidateRsaAndCertificateMatch(RSA privateKey, X509Certificate2 certificate)
    {
        if (privateKey.KeySize < 2048)
            throw new InvalidOperationException($"طول کلید RSA باید حداقل ۲۰۴۸ بیت باشد (کلید فعلی: {privateKey.KeySize} بیت).");

        if (!DoesRsaMatchCertificate(privateKey, certificate))
            throw new InvalidOperationException(BuildDetailedKeyCertMismatchMessage(privateKey, new List<X509Certificate2> { certificate }));
    }

    private static string BuildDetailedKeyCertMismatchMessage(RSA privateKey, List<X509Certificate2> candidates)
    {
        var privFingerprint = ComputeRsaModulusFingerprint(privateKey);
        var allAreCa = candidates.Count > 0 && candidates.All(MoadianPkcs11Inspector.IsCaCertificate);
        var anyNonRsa = candidates.Any(c => c.GetRSAPublicKey() is null);

        var sb = new StringBuilder();
        sb.AppendLine("عدم تطابق ریاضی بین کلید خصوصی و گواهی امضای الکترونیکی!");

        if (allAreCa)
        {
            sb.AppendLine("• علت دقیق: فایل گواهی بارگذاری‌شده متعلق به «مرکز صدور گواهی (گواهی ریشه/میانی CA)» است (BasicConstraints: CA=True)، نه گواهی اختصاصی صادرشده برای مودی! لطفاً فایل گواهی امضای شرکت/شخص خودتان را انتخاب کنید.");
        }
        else if (anyNonRsa && candidates.All(c => c.GetRSAPublicKey() is null))
        {
            var oid = candidates[0].PublicKey.Oid?.FriendlyName ?? candidates[0].PublicKey.Oid?.Value ?? "غیر RSA";
            sb.AppendLine($"• علت دقیق: کلید عمومی داخل گواهی از نوع «{oid}» است، در حالی که کلید خصوصی از نوع RSA است.");
        }
        else
        {
            sb.AppendLine("• علت دقیق: ماژول ریاضی (Modulus) کلید خصوصی بارگذاری‌شده با کلید عمومی داخل گواهی یکسان نیست؛ یعنی این گواهی از روی یک کلید خصوصی / درخواست گواهی (CSR) دیگر صادر شده است.");
        }

        sb.AppendLine($"• مشخصات کلید خصوصی بارگذاری‌شده (کادر ۱): RSA {privateKey.KeySize}-bit | شناسه ماژول کلید: [{privFingerprint}]");
        sb.AppendLine($"• مشخصات گواهی‌های بررسی‌شده در فایل (کادر ۲ — {candidates.Count} گواهی):");

        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var isCa = MoadianPkcs11Inspector.IsCaCertificate(c);
            var expired = c.NotAfter.ToUniversalTime() < DateTime.UtcNow;
            string pubInfo;
            using (var pub = c.GetRSAPublicKey())
            {
                pubInfo = pub is not null
                    ? $"RSA {pub.KeySize}-bit | شناسه ماژول کلید عمومی: [{ComputeRsaModulusFingerprint(pub)}]"
                    : $"نوع غیر RSA ({c.PublicKey.Oid?.FriendlyName ?? c.PublicKey.Oid?.Value})";
            }

            var roleTag = isCa ? "گواهی مرکز صدور (CA)" : "گواهی پایانی (مودی)";
            var expTag = expired ? " — منقضی‌شده!" : "";
            sb.AppendLine($"  [{i + 1}] {roleTag}: {c.Subject}");
            sb.AppendLine($"      صادرکننده: {c.Issuer} | اعتبار: {c.NotBefore:yyyy/MM/dd} تا {c.NotAfter:yyyy/MM/dd}{expTag}");
            sb.AppendLine($"      کلید داخل گواهی: {pubInfo}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string ComputeRsaModulusFingerprint(RSA rsa)
    {
        try
        {
            var p = rsa.ExportParameters(includePrivateParameters: false);
            if (p.Modulus is null || p.Modulus.Length == 0)
                return "نامشخص";
            var hash = SHA256.HashData(p.Modulus);
            return $"{Convert.ToHexString(hash, 0, 2)}-{Convert.ToHexString(hash, 2, 2)}-{Convert.ToHexString(hash, 4, 2)}-{Convert.ToHexString(hash, 6, 2)}";
        }
        catch
        {
            return "نامشخص";
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
