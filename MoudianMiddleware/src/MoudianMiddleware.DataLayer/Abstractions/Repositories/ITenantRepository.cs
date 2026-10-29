using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Abstractions.Repositories;

/// <summary>
/// عملیات داده‌ی مودیان (مستأجرها).
/// </summary>
/// <remarks>
/// <see cref="Tenant"/> خودش <c>ITenantEntity</c> نیست؛ بنابراین این Repository برای
/// عملیات مدیریتی (سوپر‌ادمین) و Seed قابل استفاده است.
/// </remarks>
public interface ITenantRepository : IGenericRepository<Tenant>
{
    /// <summary>دریافت مودی همراه با تنظیمات مودیان.</summary>
    /// <param name="id">شناسه.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>مودی یا <c>null</c>.</returns>
    Task<Tenant?> GetWithSettingsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>یافتن مودی بر اساس شناسه‌ی حافظه‌ی مالیاتی.</summary>
    /// <param name="memoryId">شناسه‌ی حافظه (مثل A11216).</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>مودی یا <c>null</c>.</returns>
    Task<Tenant?> GetByMemoryIdAsync(string memoryId, CancellationToken cancellationToken = default);

    /// <summary>فهرست مودیان فعال.</summary>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>فهرست مودیان.</returns>
    Task<List<Tenant>> ListActiveAsync(CancellationToken cancellationToken = default);
}
