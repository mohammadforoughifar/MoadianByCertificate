namespace MoudianMiddleware.DataLayer.Seed;

/// <summary>
/// مقداردهی اولیه‌ی داده‌های پایه (نقش‌ها، Permissionها، Lookupها و کاربر مدیر).
/// </summary>
/// <remarks>
/// این عملیات Idempotent است: اجرای مکرر آن داده‌ی تکراری ایجاد نمی‌کند.
/// </remarks>
public interface IDatabaseSeeder
{
    /// <summary>اجرای Seed.</summary>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>وظیفه‌ی انجام عملیات.</returns>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
