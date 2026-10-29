using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// کلید دسترسی سیستم‌های خارجی (ERP/فروش) به API واسط.
/// </summary>
/// <remarks>
/// <para>
/// کلید خام فقط یک‌بار هنگام ساخت به کاربر نمایش داده می‌شود؛ در دیتابیس تنها
/// <see cref="KeyHash"/> (SHA-256) و <see cref="KeyPrefix"/> برای شناسایی نگه‌داری می‌شود.
/// </para>
/// <para>
/// هر کلید به یک مستأجر تعلق دارد؛ بنابراین TenantId درخواست‌های خارجی از همین رکورد تعیین می‌شود.
/// </para>
/// </remarks>
public class ApiClient : AuditableEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>نام سیستم یا سرویس مصرف‌کننده (مثلاً «ERP فروشگاه مرکزی»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>بخش ابتدایی کلید (۸ کاراکتر) برای شناسایی سریع رکورد.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>هش SHA-256 کلید (Base64).</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>دسترسی‌های واگذارشده به این کلید.</summary>
    public ApiScope Scopes { get; set; } = ApiScope.None;

    /// <summary>آیا کلید فعال است؟</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>زمان انقضای کلید (اختیاری).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>زمان آخرین استفاده‌ی موفق.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>سقف درخواست در دقیقه برای این کلید (Rate Limit).</summary>
    public int RequestsPerMinute { get; set; } = 60;
}
