using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// ثبت حسابرسی عملیات کاربران و سیستم (بدون داده‌ی حساس).
/// </summary>
/// <remarks>
/// TenantId به‌صورت nullable است چون برخی رویدادها (مثل ورود ناموفق قبل از تعیین مودی) مستأجر ندارند.
/// در این جدول فیلتر سراسری مستأجر اعمال نمی‌شود و دسترسی با Permission کنترل می‌گردد.
/// </remarks>
public class AuditLog : BaseEntity
{
    /// <summary>شناسه‌ی مستأجر (در صورت مشخص بودن).</summary>
    public int? TenantId { get; set; }

    /// <summary>شناسه‌ی کاربر انجام‌دهنده (در عملیات سیستمی <c>null</c>).</summary>
    public int? UserId { get; set; }

    /// <summary>شناسه‌ی کلید API در صورت ورود از سیستم خارجی.</summary>
    public int? ApiClientId { get; set; }

    /// <summary>نوع عملیات.</summary>
    public AuditAction Action { get; set; }

    /// <summary>نام موجودیت مرتبط (مثلاً Invoice).</summary>
    public string? EntityName { get; set; }

    /// <summary>شناسه‌ی موجودیت مرتبط.</summary>
    public string? EntityId { get; set; }

    /// <summary>توضیح خوانا برای نمایش در گزارش.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>آدرس IP.</summary>
    public string? IpAddress { get; set; }

    /// <summary>User-Agent.</summary>
    public string? UserAgent { get; set; }

    /// <summary>شناسه‌ی ردیابی درخواست.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>آیا عملیات موفق بود؟</summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>داده‌ی تکمیلی JSON (همیشه ماسک‌شده؛ بدون کلید/رمز).</summary>
    public string? AdditionalData { get; set; }

    /// <summary>زمان ثبت رویداد (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
