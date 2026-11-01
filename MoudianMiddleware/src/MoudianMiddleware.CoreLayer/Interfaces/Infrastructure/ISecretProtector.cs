namespace MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;

/// <summary>داده‌ی رمزنگاری‌شده‌ی AES-GCM؛ هر بسته ciphertext، nonce، tag و نسخه‌ی کلید را جدا دارد.</summary>
public sealed record ProtectedSecret(string CiphertextBase64, string NonceBase64, string TagBase64, int KeyVersion);

/// <summary>رمزنگاری/رمزگشایی envelope برای کلید خصوصی و PIN بدون ذخیره‌ی متن واضح.</summary>
public interface ISecretProtector
{
    /// <summary>رمزنگاری متن باینری با وابستگی به tenant و purpose.</summary>
    ProtectedSecret Protect(ReadOnlySpan<byte> plaintext, int tenantId, string purpose);
    /// <summary>رمزگشایی envelope فقط در حافظه.</summary>
    byte[] Unprotect(ProtectedSecret protectedSecret, int tenantId, string purpose);
}
