namespace MoudianMiddleware.Contracts.Common;

/// <summary>
/// کدهای ثابت پاسخ API؛ جایگزین Magic String در Controllerها، Middleware و Client.
/// </summary>
public static class ApiResponseCodes
{
    /// <summary>عملیات با موفقیت انجام شد.</summary>
    public const string Ok = "OK";

    /// <summary>موجودیت ایجاد شد (HTTP 201).</summary>
    public const string Created = "CREATED";

    /// <summary>درخواست تکراری بود و پاسخ قبلی (Idempotent) بازگردانده شد.</summary>
    public const string IdempotentReplay = "IDEMPOTENT_REPLAY";

    /// <summary>خطای اعتبارسنجی ورودی (HTTP 400).</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>موجودیت یافت نشد (HTTP 404).</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>نقض قاعده‌ی کسب‌وکار (HTTP 409).</summary>
    public const string BusinessRuleViolation = "BUSINESS_RULE_VIOLATION";

    /// <summary>عدم احراز هویت (HTTP 401).</summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>Api Key نامعتبر یا منقضی (HTTP 401).</summary>
    public const string InvalidApiKey = "INVALID_API_KEY";

    /// <summary>عدم دسترسی / کمبود Scope (HTTP 403).</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>عبور از محدودیت نرخ درخواست (HTTP 429).</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>رد شدن صورتحساب توسط سامانه‌ی مودیان (HTTP 422).</summary>
    public const string MoadianRejected = "MOADIAN_REJECTED";

    /// <summary>خطای ارتباط با سامانه‌ی مودیان (HTTP 502).</summary>
    public const string MoadianUnavailable = "MOADIAN_UNAVAILABLE";

    /// <summary>پیکربندی مودی ناقص است (HTTP 409).</summary>
    public const string MoadianNotConfigured = "MOADIAN_NOT_CONFIGURED";

    /// <summary>خطای پیش‌بینی‌نشده‌ی سرور (HTTP 500).</summary>
    public const string InternalError = "INTERNAL_ERROR";
}
