namespace MoudianMiddleware.DataLayer.Seed;

/// <summary>
/// تنظیمات Seed اولیه‌ی دیتابیس (کاربر مدیر).
/// </summary>
/// <remarks>
/// رمز عبور هرگز در کد یا appsettings مخزن ذخیره نمی‌شود؛ از Environment Variable یا
/// User Secrets استفاده کنید (مثلاً <c>DatabaseSeed__AdminPassword</c>).
/// اگر نام کاربری یا رمز خالی باشد، کاربر مدیر ساخته نمی‌شود.
/// </remarks>
public sealed class DatabaseSeedOptions
{
    /// <summary>نام بخش تنظیمات در appsettings.</summary>
    public const string SectionName = "DatabaseSeed";

    /// <summary>نام کاربری مدیر سیستم.</summary>
    public string? AdminUserName { get; set; }

    /// <summary>رمز عبور اولیه‌ی مدیر (فقط از Environment/Secrets).</summary>
    public string? AdminPassword { get; set; }

    /// <summary>نام و نام خانوادگی مدیر.</summary>
    public string? AdminFullName { get; set; }

    /// <summary>ایمیل مدیر (اختیاری).</summary>
    public string? AdminEmail { get; set; }
}
