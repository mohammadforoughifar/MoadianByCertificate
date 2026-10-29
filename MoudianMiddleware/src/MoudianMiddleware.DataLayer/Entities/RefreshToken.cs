using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// توکن تازه‌سازی (Refresh Token) با چرخش توکن.
/// </summary>
/// <remarks>
/// فقط «هش» توکن ذخیره می‌شود؛ خود توکن تنها یک‌بار به کلاینت برگردانده می‌شود.
/// </remarks>
public class RefreshToken : BaseEntity
{
    /// <summary>شناسه‌ی کاربر.</summary>
    public int UserId { get; set; }

    /// <summary>هش توکن (SHA-256 به‌صورت Base64).</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>زمان انقضا (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>زمان ایجاد (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>آدرس IP سازنده‌ی توکن.</summary>
    public string? CreatedByIp { get; set; }

    /// <summary>User-Agent سازنده‌ی توکن.</summary>
    public string? UserAgent { get; set; }

    /// <summary>زمان ابطال (UTC)؛ <c>null</c> یعنی فعال.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>آدرس IP ابطال‌کننده.</summary>
    public string? RevokedByIp { get; set; }

    /// <summary>هش توکنی که جایگزین این توکن شده است.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>کاربر مربوطه.</summary>
    public User? User { get; set; }
}
