using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// تاریخچه‌ی هر تلاش ارتباط با سامانه برای یک صورتحساب (ارسال/استعلام/تلاش مجدد/ثبت پرداخت).
/// </summary>
/// <remarks>
/// این جدول «Audit عملیاتی» است: هر درخواست و پاسخ با شناسه‌ی پیگیری، کد خطا و زمان ثبت می‌شود.
/// محتوای کامل Payload/Response در <see cref="AuditLog"/> و با ماسک‌کردن داده‌ی حساس ذخیره می‌شود.
/// </remarks>
public class InvoiceSubmission : BaseEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>شناسه‌ی صورتحساب.</summary>
    public int InvoiceId { get; set; }

    /// <summary>شماره‌ی تلاش (از ۱ به بالا).</summary>
    public int AttemptNumber { get; set; }

    /// <summary>نوع عملیات انجام‌شده.</summary>
    public SubmissionAction Action { get; set; }

    /// <summary>نتیجه‌ی تلاش.</summary>
    public SubmissionStatus Status { get; set; }

    /// <summary>شناسه‌ی درخواست (UID).</summary>
    public string? Uid { get; set; }

    /// <summary>شماره‌ی پیگیری دریافتی از سامانه.</summary>
    public string? ReferenceNumber { get; set; }

    /// <summary>وضعیت خام بازگشتی سامانه (SUCCESS/FAILED/IN_PROGRESS/TIMEOUT/NOT_FOUND).</summary>
    public string? MoadianStatus { get; set; }

    /// <summary>کد اولین خطای سامانه.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>پیام خطای سامانه.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>تعداد اخطارهای سامانه.</summary>
    public int WarningCount { get; set; }

    /// <summary>شناسه‌ی ردیابی درخواست (CorrelationId) برای اتصال به لاگ‌ها.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>زمان شروع تلاش.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>زمان پایان تلاش.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>مدت زمان تلاش (میلی‌ثانیه).</summary>
    public int? DurationMs { get; set; }

    /// <summary>صورتحساب مربوطه.</summary>
    public Invoice? Invoice { get; set; }
}
