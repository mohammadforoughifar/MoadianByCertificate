using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// کاربر سیستم؛ هویت سراسری است و از طریق <see cref="UserTenant"/> به یک یا چند مودی متصل می‌شود.
/// </summary>
public class User : AuditableEntity
{
    /// <summary>نام کاربری (یکتا).</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>نام کاربری نرمال‌شده (حروف بزرگ) برای جست‌وجوی یکتا.</summary>
    public string NormalizedUserName { get; set; } = string.Empty;

    /// <summary>نام و نام خانوادگی.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>ایمیل (اختیاری).</summary>
    public string? Email { get; set; }

    /// <summary>هش رمز عبور (PBKDF2؛ هرگز رمز خام ذخیره نمی‌شود).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>آیا حساب فعال است؟</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>دسترسی سوپر‌ادمین به همه‌ی مستأجرها.</summary>
    public bool IsSuperAdmin { get; set; }

    /// <summary>زمان آخرین ورود موفق.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>تعداد تلاش‌های ناموفق متوالی ورود.</summary>
    public int AccessFailedCount { get; set; }

    /// <summary>پایان قفل حساب در صورت تلاش‌های ناموفق.</summary>
    public DateTimeOffset? LockoutEndAt { get; set; }

    /// <summary>عضویت‌های کاربر در مستأجرها.</summary>
    public ICollection<UserTenant> UserTenants { get; set; } = new List<UserTenant>();

    /// <summary>توکن‌های تازه‌سازی (Refresh Token) صادرشده برای کاربر.</summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
