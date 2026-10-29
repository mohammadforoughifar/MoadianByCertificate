using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// نقش سیستمی (مثل SuperAdmin، TenantAdmin، Accountant، Operator، Viewer).
/// </summary>
/// <remarks>
/// نقش‌ها سراسری هستند و به مستأجر خاصی تعلق ندارند؛ اتصال کاربر به نقش، در <see cref="UserTenant"/> است.
/// </remarks>
public class Role : BaseEntity
{
    /// <summary>نام نقش (یکتا).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>نام نرمال‌شده (حروف بزرگ) برای مقایسه.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>توضیح نقش.</summary>
    public string? Description { get; set; }

    /// <summary>آیا نقش سیستمی است؟ (قابل حذف از UI نیست)</summary>
    public bool IsSystem { get; set; }

    /// <summary>Permissionهای این نقش.</summary>
    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
}
