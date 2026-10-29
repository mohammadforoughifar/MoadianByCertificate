namespace MoudianMiddleware.Contracts.Enums;

/// <summary>
/// محیط سامانه‌ی مودیان که درخواست‌ها به آن ارسال می‌شوند.
/// </summary>
public enum MoadianEnvironment
{
    /// <summary>محیط آزمایشی (آدرس آن در مخزن مرجع موجود نبود و «نیاز به تأیید» است).</summary>
    Sandbox = 1,

    /// <summary>محیط عملیاتی.</summary>
    Production = 2
}

/// <summary>
/// استراتژی امضای بسته‌ی صورتحساب برای هر مودی.
/// </summary>
public enum MoadianSignatureMode
{
    /// <summary>فایل کلید خصوصی (PKCS#8 PEM) + گواهی (crt/cer) — پیش‌فرض و توصیه‌شده.</summary>
    Pkcs8Certificate = 1,

    /// <summary>توکن سخت‌افزاری PKCS#11 (مثلاً ePass3003) — برای پایانه‌ی محلی.</summary>
    Pkcs11Token = 2,

    /// <summary>
    /// فقط کلید خصوصی بدون گواهی (JWS بدون هدر x5c) — <b>آزمایشی</b>؛
    /// تا تأیید با سامانه‌ی مودیان نباید پیش‌فرض باشد.
    /// </summary>
    PrivateKeyOnly = 3
}
