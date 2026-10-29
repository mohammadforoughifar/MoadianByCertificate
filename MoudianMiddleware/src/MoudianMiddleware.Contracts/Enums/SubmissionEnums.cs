namespace MoudianMiddleware.Contracts.Enums;

/// <summary>
/// نوع عملیاتی که با سامانه‌ی مودیان انجام شده است (برای تاریخچه و Audit).
/// </summary>
public enum SubmissionAction
{
    /// <summary>ارسال صورتحساب.</summary>
    Send = 1,

    /// <summary>استعلام وضعیت.</summary>
    Inquiry = 2,

    /// <summary>تلاش مجدد ارسال.</summary>
    Retry = 3,

    /// <summary>ثبت پرداخت مستقل.</summary>
    RegisterPayment = 4
}

/// <summary>
/// نتیجه‌ی یک تلاش ارتباط با سامانه (ذخیره‌شده در تاریخچه).
/// </summary>
public enum SubmissionStatus
{
    /// <summary>در انتظار اجرا / بدون نتیجه.</summary>
    Pending = 1,

    /// <summary>موفق.</summary>
    Succeeded = 2,

    /// <summary>رد شده یا خطای اعتبارسنجی.</summary>
    Failed = 3,

    /// <summary>مهلت تمام شد (TIMEOUT).</summary>
    Timeout = 4,

    /// <summary>یافت نشد (NOT_FOUND).</summary>
    NotFound = 5
}

/// <summary>
/// وضعیت صورتحساب در پاسخ استعلام سامانه‌ی مودیان.
/// </summary>
public enum MoadianRequestStatus
{
    /// <summary>ثبت و تأیید شد (SUCCESS).</summary>
    Success = 1,

    /// <summary>رد شد (FAILED).</summary>
    Failed = 2,

    /// <summary>پردازش طولانی شد (TIMEOUT).</summary>
    Timeout = 3,

    /// <summary>در حال اعتبارسنجی (IN_PROGRESS).</summary>
    InProgress = 4,

    /// <summary>شماره‌ی پیگیری یافت نشد (NOT_FOUND).</summary>
    NotFound = 5
}

/// <summary>
/// وضعیت پردازش پیام‌های Outbox (ارسال قابل‌اطمینان به سامانه).
/// </summary>
public enum OutboxStatus
{
    /// <summary>در انتظار پردازش.</summary>
    Pending = 1,

    /// <summary>در حال پردازش.</summary>
    Processing = 2,

    /// <summary>پردازش شد.</summary>
    Processed = 3,

    /// <summary>خطا خورد و در انتظار تلاش مجدد است.</summary>
    Failed = 4,

    /// <summary>پس از حداکثر تلاش‌ها کنار گذاشته شد.</summary>
    Dead = 5
}
