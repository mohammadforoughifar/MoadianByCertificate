using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;

namespace MoudianMiddleware.CoreLayer.Infrastructure.Security;

/// <summary>AES-256-GCM envelope encryptor with tenant/purpose authenticated additional data.</summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly SecretProtectionOptions _options;

    /// <summary>Create the protector from externally supplied key configuration.</summary>
    public AesGcmSecretProtector(IOptions<SecretProtectionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new InvalidOperationException("تنظیمات رمزنگاری در دسترس نیست.");
    }

    /// <inheritdoc />
    public ProtectedSecret Protect(ReadOnlySpan<byte> plaintext, int tenantId, string purpose)
    {
        Validate(tenantId, purpose);
        if (plaintext.IsEmpty)
        {
            throw new ArgumentException("داده‌ی محرمانه خالی است.", nameof(plaintext));
        }

        var version = _options.ActiveKeyVersion;
        var key = GetKey(version);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var associatedData = CreateAssociatedData(tenantId, purpose, version);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            return new ProtectedSecret(
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag),
                version);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    /// <inheritdoc />
    public byte[] Unprotect(ProtectedSecret protectedSecret, int tenantId, string purpose)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);
        Validate(tenantId, purpose);
        var ciphertext = Convert.FromBase64String(protectedSecret.CiphertextBase64);
        var nonce = Convert.FromBase64String(protectedSecret.NonceBase64);
        var tag = Convert.FromBase64String(protectedSecret.TagBase64);
        if (nonce.Length != NonceSize || tag.Length != TagSize)
        {
            throw new CryptographicException("طول nonce یا tag رمزنگاری نامعتبر است.");
        }

        var plaintext = new byte[ciphertext.Length];
        var key = GetKey(protectedSecret.KeyVersion);
        var associatedData = CreateAssociatedData(tenantId, purpose, protectedSecret.KeyVersion);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    private byte[] GetKey(int version)
    {
        if (!_options.KeysBase64.TryGetValue(version, out var encoded))
        {
            throw new InvalidOperationException($"کلید رمزنگاری نسخه‌ی {version} تنظیم نشده است.");
        }

        var key = Convert.FromBase64String(encoded);
        if (key.Length != 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException("کلید AES-GCM باید دقیقاً ۳۲ بایت باشد.");
        }

        return key;
    }

    private static byte[] CreateAssociatedData(int tenantId, string purpose, int version)
        => Encoding.UTF8.GetBytes($"MoudianMiddleware|tenant:{tenantId}|purpose:{purpose}|key:{version}");

    private static void Validate(int tenantId, string purpose)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
    }
}
