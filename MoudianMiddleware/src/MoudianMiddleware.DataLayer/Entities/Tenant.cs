using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// مستأجر (مودی)؛ هر شناسه‌ی حافظه‌ی مالیاتی یک Tenant مستقل است.
/// </summary>
/// <remarks>
/// جداسازی داده در سطح این موجودیت انجام می‌شود: همه‌ی جداول عملیاتی یک <c>TenantId</c> دارند.
/// </remarks>
public class Tenant : AuditableEntity
{
    /// <summary>نام تجاری/نمایشی مودی.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>نام حقوقی کامل (اختیاری).</summary>
    public string? LegalName { get; set; }

    /// <summary>آیا مستأجر فعال است؟ (غیرفعال = عدم امکان ورود و ارسال)</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>تنظیمات مودی (رابطه‌ی یک‌به‌یک).</summary>
    public MoadianSetting? MoadianSetting { get; set; }

    /// <summary>عضویت‌های کاربران در این مستأجر.</summary>
    public ICollection<UserTenant> UserTenants { get; set; } = new List<UserTenant>();
}
