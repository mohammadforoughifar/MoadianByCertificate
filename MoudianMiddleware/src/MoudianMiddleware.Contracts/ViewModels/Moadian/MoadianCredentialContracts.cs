namespace MoudianMiddleware.Contracts.ViewModels.Moadian;

/// <summary>ورودی write-only مواد امضا؛ PEM و PIN هرگز در پاسخ برگردانده نمی‌شوند.</summary>
public sealed class SaveMoadianCredentialRequest
{
    /// <summary>گواهی عمومی Base64 از DER یا PEM؛ برای PKCS#8 لازم و برای PKCS#11/private-key-only اختیاری است.</summary>
    public string? CertificateBase64 { get; init; }
    /// <summary>کلید خصوصی PKCS#8 PEM؛ فقط در درخواست دریافت می‌شود و فوراً رمزنگاری خواهد شد.</summary>
    public string? PrivateKeyPem { get; init; }
    /// <summary>مسیر کتابخانه‌ی PKCS#11؛ فقط در حالت توکن سخت‌افزاری.</summary>
    public string? Pkcs11LibraryPath { get; init; }
    /// <summary>Alias گواهی روی توکن.</summary>
    public string? Pkcs11CertificateAlias { get; init; }
    /// <summary>PIN توکن؛ write-only و فقط به شکل رمزنگاری‌شده ذخیره می‌شود.</summary>
    public string? Pkcs11Pin { get; init; }
}

/// <summary>خلاصه‌ی امن credential مودی؛ بدون گواهی PEM محرمانه، کلید خصوصی یا PIN.</summary>
public sealed class MoadianCredentialViewModel
{
    /// <summary>آیا گواهی عمومی ثبت شده است؟</summary>
    public bool HasCertificate { get; init; }
    /// <summary>آیا کلید خصوصی رمزنگاری‌شده موجود است؟</summary>
    public bool HasPrivateKey { get; init; }
    /// <summary>آیا PIN رمزنگاری‌شده موجود است؟</summary>
    public bool HasPkcs11Pin { get; init; }
    /// <summary>اثر انگشت گواهی عمومی.</summary>
    public string? CertificateThumbprint { get; init; }
    /// <summary>Subject گواهی.</summary>
    public string? CertificateSubject { get; init; }
    /// <summary>زمان انقضای گواهی.</summary>
    public DateTimeOffset? CertificateNotAfter { get; init; }
}
