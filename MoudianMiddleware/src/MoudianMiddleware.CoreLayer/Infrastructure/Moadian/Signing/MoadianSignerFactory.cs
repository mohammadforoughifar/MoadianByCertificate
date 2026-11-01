using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;
using MoudianMiddleware.CoreLayer.Services.Moadian;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian.Signing;

/// <summary>Creates one of the three configured signature strategies from encrypted tenant material.</summary>
public sealed class MoadianSignerFactory : IMoadianSignerFactory
{
    private readonly ISecretProtector _protector;

    /// <summary>Create signer factory.</summary>
    public MoadianSignerFactory(ISecretProtector protector) => _protector = protector;

    /// <inheritdoc />
    public MoadianSigningMaterial Create(MoadianSetting setting, MoadianCredential credential)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(credential);
        return setting.SignatureMode switch
        {
            MoadianSignatureMode.Pkcs8Certificate => CreatePkcs8(credential),
            MoadianSignatureMode.Pkcs11Token => CreatePkcs11(credential),
            MoadianSignatureMode.PrivateKeyOnly when setting.IsSignatureExperimentalApproved => CreatePrivateKeyOnly(credential),
            MoadianSignatureMode.PrivateKeyOnly => throw new InvalidOperationException(
                "امضای فقط‌کلید آزمایشی است و باید پیش از استفاده صریحاً تأیید شود."),
            _ => throw new InvalidOperationException("حالت امضای مودی نامعتبر است.")
        };
    }

    private MoadianSigningMaterial CreatePkcs8(MoadianCredential credential)
    {
        using var privateKeyBytes = new ZeroingBuffer(UnprotectPrivateKey(credential));
        var privateKey = ImportPrivateKey(privateKeyBytes.Bytes);
        X509Certificate2? certificate = null;
        try
        {
            certificate = LoadCertificate(credential.CertificateBase64);
            EnsureKeyMatchesCertificate(privateKey, certificate);
            var signer = new Pkcs8CertificateJwsSigner(privateKey, certificate);
            return new MoadianSigningMaterial(signer, signer);
        }
        catch
        {
            privateKey.Dispose();
            certificate?.Dispose();
            throw;
        }
    }

    private MoadianSigningMaterial CreatePrivateKeyOnly(MoadianCredential credential)
    {
        using var privateKeyBytes = new ZeroingBuffer(UnprotectPrivateKey(credential));
        var privateKey = ImportPrivateKey(privateKeyBytes.Bytes);
        try
        {
            var signer = new PrivateKeyOnlyJwsSigner(privateKey);
            return new MoadianSigningMaterial(signer, signer);
        }
        catch
        {
            privateKey.Dispose();
            throw;
        }
    }

    private MoadianSigningMaterial CreatePkcs11(MoadianCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.Pkcs11LibraryPath)
            || string.IsNullOrWhiteSpace(credential.Pkcs11CertificateAlias)
            || string.IsNullOrWhiteSpace(credential.EncryptedPin)
            || string.IsNullOrWhiteSpace(credential.PinNonce)
            || string.IsNullOrWhiteSpace(credential.PinTag))
        {
            throw new InvalidOperationException("مواد PKCS#11 مودی کامل نیست.");
        }

        var pinBytes = _protector.Unprotect(
            new ProtectedSecret(credential.EncryptedPin, credential.PinNonce, credential.PinTag, credential.PinKeyVersion),
            credential.TenantId,
            MoadianCredentialService.Pkcs11PinPurpose);
        try
        {
            // SDK 2.0.29 accepts PIN as string and retains it in an internal provider; it has no disposal API.
            var pin = Encoding.UTF8.GetString(pinBytes);
            var signer = new Pkcs11TokenJwsSigner(credential.Pkcs11CertificateAlias, credential.Pkcs11LibraryPath, pin);
            return new MoadianSigningMaterial(signer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pinBytes);
        }
    }

    private byte[] UnprotectPrivateKey(MoadianCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.EncryptedPrivateKey)
            || string.IsNullOrWhiteSpace(credential.PrivateKeyNonce)
            || string.IsNullOrWhiteSpace(credential.PrivateKeyTag))
        {
            throw new InvalidOperationException("کلید خصوصی رمزنگاری‌شده برای این مودی ثبت نشده است.");
        }

        return _protector.Unprotect(
            new ProtectedSecret(credential.EncryptedPrivateKey, credential.PrivateKeyNonce, credential.PrivateKeyTag, credential.PrivateKeyKeyVersion),
            credential.TenantId,
            MoadianCredentialService.PrivateKeyPurpose);
    }

    private static RSA ImportPrivateKey(byte[] privateKeyBytes)
    {
        var chars = Encoding.UTF8.GetChars(privateKeyBytes);
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

    private static X509Certificate2 LoadCertificate(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new InvalidOperationException("گواهی عمومی مودی ثبت نشده است.");
        }

        var bytes = Convert.FromBase64String(encoded);
        try
        {
            var pem = Encoding.UTF8.GetString(bytes);
            return pem.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
                ? X509Certificate2.CreateFromPem(pem)
                : new X509Certificate2(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void EnsureKeyMatchesCertificate(RSA privateKey, X509Certificate2 certificate)
    {
        using var publicKey = certificate.GetRSAPublicKey()
            ?? throw new InvalidOperationException("گواهی انتخاب‌شده کلید عمومی RSA ندارد.");
        var challenge = RandomNumberGenerator.GetBytes(32);
        var signature = privateKey.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        try
        {
            if (!publicKey.VerifyData(challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                throw new InvalidOperationException("کلید خصوصی با گواهی انتخاب‌شده تطابق ندارد.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(challenge);
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private sealed class ZeroingBuffer : IDisposable
    {
        public ZeroingBuffer(byte[] bytes) => Bytes = bytes;
        public byte[] Bytes { get; }
        public void Dispose() => CryptographicOperations.ZeroMemory(Bytes);
    }
}
