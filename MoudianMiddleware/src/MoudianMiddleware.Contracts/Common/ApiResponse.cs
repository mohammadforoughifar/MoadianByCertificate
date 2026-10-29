namespace MoudianMiddleware.Contracts.Common;

/// <summary>
/// پاسخ استاندارد و یکسان همه‌ی Endpointهای API (چه برای کاربران UI و چه برای سیستم‌های خارجی).
/// </summary>
/// <typeparam name="T">نوع داده‌ی بازگشتی.</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>آیا عملیات موفق بود؟</summary>
    public bool IsSuccess { get; init; }

    /// <summary>کد ماشین‌خوان پاسخ (از <see cref="ApiResponseCodes"/>).</summary>
    public string Code { get; init; } = ApiResponseCodes.Ok;

    /// <summary>پیام قابل نمایش به کاربر (فارسی).</summary>
    public string? Message { get; init; }

    /// <summary>داده‌ی پاسخ؛ در صورت خطا مقدار <c>null</c> است.</summary>
    public T? Data { get; init; }

    /// <summary>فهرست خطاها (برای خطاهای اعتبارسنجی چندگانه).</summary>
    public IReadOnlyList<ApiError>? Errors { get; init; }

    /// <summary>شناسه‌ی ردیابی درخواست برای پیگیری در لاگ.</summary>
    public string? TraceId { get; init; }

    /// <summary>ساخت پاسخ موفق.</summary>
    /// <param name="data">داده‌ی بازگشتی.</param>
    /// <param name="message">پیام اختیاری.</param>
    /// <param name="code">کد پاسخ.</param>
    /// <returns>پاسخ موفق.</returns>
    public static ApiResponse<T> Ok(T data, string? message = null, string code = ApiResponseCodes.Ok)
        => new() { IsSuccess = true, Data = data, Message = message, Code = code };

    /// <summary>ساخت پاسخ ناموفق.</summary>
    /// <param name="code">کد خطا.</param>
    /// <param name="message">پیام خطا.</param>
    /// <param name="errors">فهرست ریزخطاها.</param>
    /// <returns>پاسخ ناموفق.</returns>
    public static ApiResponse<T> Fail(string code, string message, IReadOnlyList<ApiError>? errors = null)
        => new() { IsSuccess = false, Code = code, Message = message, Errors = errors };
}

/// <summary>
/// یک خطای موردی داخل پاسخ API (مثلاً خطای یک فیلد).
/// </summary>
public sealed class ApiError
{
    /// <summary>
    /// سازنده‌ی خطا.
    /// </summary>
    /// <param name="message">پیام خطا.</param>
    /// <param name="field">نام فیلد مرتبط (در صورت وجود).</param>
    /// <param name="code">کد خطا (اختیاری).</param>
    public ApiError(string message, string? field = null, string? code = null)
    {
        Message = message;
        Field = field;
        Code = code;
    }

    /// <summary>پیام خطا.</summary>
    public string Message { get; }

    /// <summary>نام فیلد مرتبط (در صورت وجود).</summary>
    public string? Field { get; }

    /// <summary>کد خطا (اختیاری).</summary>
    public string? Code { get; }
}
