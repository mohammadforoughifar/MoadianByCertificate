using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// Material امضای مودی: گواهی عمومی و کلید خصوصی/پین <b>رمزنگاری‌شده</b>.
/// </summary>
/// <remarks>
/// <para>
/// کلید خصوصی هرگز به‌صورت متن واضح ذخیره نمی‌شود. رمزنگاری به‌صورت Envelope انجام می‌شود:
/// AES-GCM با یک کلید داده‌ی (DEK) که خودش با KEK خارج از دیتابیس (Environment/User Secrets) محافظت می‌شود.
/// </para>
/// <para>
/// رمزگشایی فقط در حافظه و در لحظه‌ی امضا انجام می‌شود و هیچ‌گاه روی دیسک نوشته نمی‌شود.
/// </para>
/// </remarks>
public class MoadianCredential : AuditableEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>گواهی عمومی (Base64، فرمت DER یا PEM). داده‌ی عمومی است و رمزنگاری لازم ندارد.</summary>
    public string? CertificateBase64 { get; set; }

    /// <summary>اثر انگشت (Thumbprint) گواهی برای نمایش و مقایسه.</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Subject گواهی (برای نمایش به کاربر).</summary>
    public string? CertificateSubject { get; set; }

    /// <summary>تاریخ انقضای گواهی.</summary>
    public DateTimeOffset? CertificateNotAfter { get; set; }

    /// <summary>کلید خصوصی رمزنگاری‌شده (PKCS#8 PEM، به‌صورت Base64 از ciphertext).</summary>
    public string? EncryptedPrivateKey { get; set; }

    /// <summary>Nonce رمزنگاری AES-GCM کلید خصوصی (Base64).</summary>
    public string? PrivateKeyNonce { get; set; }

    /// <summary>Tag احراز اصالت AES-GCM کلید خصوصی (Base64).</summary>
    public string? PrivateKeyTag { get; set; }

    /// <summary>نسخه‌ی کلید (برای چرخش KEK).</summary>
    public int PrivateKeyKeyVersion { get; set; } = 1;

    /// <summary>مسیر کتابخانه‌ی PKCS#11 (در حالت توکن سخت‌افزاری).</summary>
    public string? Pkcs11LibraryPath { get; set; }

    /// <summary>Alias/برچسب گواهی روی توکن (در حالت PKCS#11).</summary>
    public string? Pkcs11CertificateAlias { get; set; }

    /// <summary>پین توکن رمزنگاری‌شده (Base64 از ciphertext).</summary>
    public string? EncryptedPin { get; set; }

    /// <summary>Nonce رمزنگاری AES-GCM پین (Base64).</summary>
    public string? PinNonce { get; set; }

    /// <summary>Tag احراز اصالت AES-GCM پین (Base64).</summary>
    public string? PinTag { get; set; }
}
