using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Moadian;
using MoudianMiddleware.CoreLayer.Common.Exceptions;
using MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Services.Moadian;

/// <summary>ذخیره‌ی مواد امضا؛ فقط ciphertext/nonce/tag به DbContext سپرده می‌شود.</summary>
public sealed class MoadianCredentialService : IMoadianCredentialService
{
    private readonly MoudianDbContext _context;
    private readonly ISecretProtector _protector;
    private readonly IValidator<SaveMoadianCredentialRequest> _validator;
    private readonly ITenantProvider _tenantProvider;
    private readonly ITenantScope _tenantScope;

    /// <summary>ساخت سرویس credential.</summary>
    public MoadianCredentialService(
        MoudianDbContext context,
        ISecretProtector protector,
        IValidator<SaveMoadianCredentialRequest> validator,
        ITenantProvider tenantProvider,
        ITenantScope tenantScope)
    {
        _context = context;
        _protector = protector;
        _validator = validator;
        _tenantProvider = tenantProvider;
        _tenantScope = tenantScope;
    }

    /// <inheritdoc />
    public async Task<MoadianCredentialViewModel> SaveAsync(
        SaveMoadianCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _validator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var tenantId = CurrentTenantId();
        var setting = await _context.MoadianSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MoadianSetting), tenantId);
        ValidateModeRequirements(setting, request);

        var certificateBase64 = Normalize(request.CertificateBase64);
        using var certificate = certificateBase64 is null ? null : ParseCertificate(certificateBase64);
        using var privateKey = string.IsNullOrWhiteSpace(request.PrivateKeyPem)
            ? null
            : ParsePrivateKey(request.PrivateKeyPem);
        if (setting.SignatureMode == MoadianSignatureMode.Pkcs8Certificate && certificate is not null && privateKey is not null)
        {
            EnsureKeyMatchesCertificate(privateKey, certificate);
        }

        var credential = await _context.MoadianCredentials.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            credential = new MoadianCredential { TenantId = tenantId };
            await _context.MoadianCredentials.AddAsync(credential, cancellationToken).ConfigureAwait(false);
        }

        credential.CertificateBase64 = certificateBase64;
        credential.CertificateThumbprint = certificate?.Thumbprint;
        credential.CertificateSubject = certificate?.Subject;
        credential.CertificateNotAfter = certificate is null
            ? null
            : new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        credential.Pkcs11LibraryPath = Normalize(request.Pkcs11LibraryPath);
        credential.Pkcs11CertificateAlias = Normalize(request.Pkcs11CertificateAlias);

        if (!string.IsNullOrWhiteSpace(request.PrivateKeyPem))
        {
            var privateKeyBytes = Encoding.UTF8.GetBytes(request.PrivateKeyPem);
            try
            {
                var protectedKey = _protector.Protect(privateKeyBytes, tenantId, PrivateKeyPurpose);
                credential.EncryptedPrivateKey = protectedKey.CiphertextBase64;
                credential.PrivateKeyNonce = protectedKey.NonceBase64;
                credential.PrivateKeyTag = protectedKey.TagBase64;
                credential.PrivateKeyKeyVersion = protectedKey.KeyVersion;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKeyBytes);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Pkcs11Pin))
        {
            var pinBytes = Encoding.UTF8.GetBytes(request.Pkcs11Pin);
            try
            {
                var protectedPin = _protector.Protect(pinBytes, tenantId, Pkcs11PinPurpose);
                credential.EncryptedPin = protectedPin.CiphertextBase64;
                credential.PinNonce = protectedPin.NonceBase64;
                credential.PinTag = protectedPin.TagBase64;
                credential.PinKeyVersion = protectedPin.KeyVersion;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pinBytes);
            }
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(credential);
    }

    /// <inheritdoc />
    public async Task<MoadianCredentialViewModel?> GetAsync(CancellationToken cancellationToken = default)
    {
        _ = CurrentTenantId();
        var credential = await _context.MoadianCredentials.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return credential is null ? null : Map(credential);
    }

    internal const string PrivateKeyPurpose = "moadian-private-key";
    internal const string Pkcs11PinPurpose = "moadian-pkcs11-pin";

    private int CurrentTenantId() => _tenantScope.TenantId ?? _tenantProvider.TenantId
        ?? throw new InvalidOperationException("مودی جاری مشخص نیست؛ دسترسی به credential رد شد.");

    private static void ValidateModeRequirements(MoadianSetting setting, SaveMoadianCredentialRequest request)
    {
        if (setting.SignatureMode == MoadianSignatureMode.Pkcs8Certificate
            && (string.IsNullOrWhiteSpace(request.PrivateKeyPem)
                || string.IsNullOrWhiteSpace(request.CertificateBase64)))
        {
            throw new ValidationException("در حالت PKCS#8، کلید خصوصی و گواهی X.509 لازم است.");
        }

        if (setting.SignatureMode == MoadianSignatureMode.PrivateKeyOnly
            && !setting.IsSignatureExperimentalApproved)
        {
            throw new ValidationException("حالت PrivateKeyOnly تا زمان تأیید صریح آزمایشی و غیرفعال است.");
        }

        if (setting.SignatureMode == MoadianSignatureMode.PrivateKeyOnly
            && string.IsNullOrWhiteSpace(request.PrivateKeyPem))
        {
            throw new ValidationException("در حالت PrivateKeyOnly آزمایشی، کلید خصوصی PKCS#8 لازم است.");
        }

        if (setting.SignatureMode == MoadianSignatureMode.Pkcs11Token
            && (string.IsNullOrWhiteSpace(request.Pkcs11LibraryPath)
                || string.IsNullOrWhiteSpace(request.Pkcs11CertificateAlias)
                || string.IsNullOrWhiteSpace(request.Pkcs11Pin)))
        {
            throw new ValidationException("برای حالت PKCS#11، مسیر کتابخانه، Alias و PIN لازم است.");
        }

        if (!Enum.IsDefined(setting.SignatureMode))
        {
            throw new ValidationException("حالت امضای مودی نامعتبر است.");
        }
    }

    private static X509Certificate2 ParseCertificate(string encoded)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new ValidationException("گواهی باید Base64 معتبر باشد.");
        }

        try
        {
            var pem = Encoding.UTF8.GetString(bytes);
            return pem.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
                ? X509Certificate2.CreateFromPem(pem)
                : new X509Certificate2(bytes);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new ValidationException("گواهی X.509 معتبر نیست.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static RSA ParsePrivateKey(string pem)
    {
        var normalized = pem.Trim();
        if (!normalized.StartsWith("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal)
            || !normalized.EndsWith("-----END PRIVATE KEY-----", StringComparison.Ordinal))
        {
            throw new ValidationException("کلید خصوصی باید PEM بدون رمزنگاری و با قالب PKCS#8 باشد.");
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(normalized);
            return rsa;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            rsa.Dispose();
            throw new ValidationException("کلید خصوصی RSA PKCS#8 معتبر نیست.");
        }
    }

    private static void EnsureKeyMatchesCertificate(RSA privateKey, X509Certificate2 certificate)
    {
        using var publicKey = certificate.GetRSAPublicKey()
            ?? throw new ValidationException("گواهی X.509 باید کلید عمومی RSA داشته باشد.");
        var challenge = RandomNumberGenerator.GetBytes(32);
        byte[]? signature = null;
        try
        {
            signature = privateKey.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!publicKey.VerifyData(challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                throw new ValidationException("کلید خصوصی با گواهی X.509 انتخاب‌شده مطابقت ندارد.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(challenge);
            if (signature is not null)
            {
                CryptographicOperations.ZeroMemory(signature);
            }
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MoadianCredentialViewModel Map(MoadianCredential credential) => new()
    {
        HasCertificate = !string.IsNullOrWhiteSpace(credential.CertificateBase64),
        HasPrivateKey = !string.IsNullOrWhiteSpace(credential.EncryptedPrivateKey),
        HasPkcs11Pin = !string.IsNullOrWhiteSpace(credential.EncryptedPin),
        CertificateThumbprint = credential.CertificateThumbprint,
        CertificateSubject = credential.CertificateSubject,
        CertificateNotAfter = credential.CertificateNotAfter
    };
}
