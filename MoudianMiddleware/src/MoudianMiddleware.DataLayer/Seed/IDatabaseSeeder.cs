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

    /// <summary>اجرای Seed در scope صریح یک مودی برای عملیات آینده‌ی tenant-scoped.</summary>
    /// <param name="tenantId">شناسه‌ی مودی؛ باید مثبت باشد.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>وظیفه‌ی انجام عملیات.</returns>
    Task SeedForTenantAsync(int tenantId, CancellationToken cancellationToken = default);
}
