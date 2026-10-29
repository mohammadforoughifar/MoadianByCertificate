using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// عضویت کاربر در یک مستأجر (مودی) همراه با نقش او در آن مستأجر.
/// </summary>
/// <remarks>
/// یک کاربر می‌تواند در چند مودی با نقش‌های متفاوت عضو باشد؛ TenantId کاربر از همین جدول
/// (یا Claim توکن) تعیین می‌شود.
/// </remarks>
public class UserTenant : AuditableEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>شناسه‌ی کاربر.</summary>
    public int UserId { get; set; }

    /// <summary>شناسه‌ی نقش کاربر در این مستأجر.</summary>
    public int RoleId { get; set; }

    /// <summary>آیا این عضویت فعال است؟</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>کاربر مربوطه.</summary>
    public User? User { get; set; }

    /// <summary>مستأجر مربوطه.</summary>
    public Tenant? Tenant { get; set; }

    /// <summary>نقش مربوطه.</summary>
    public Role? Role { get; set; }
}
