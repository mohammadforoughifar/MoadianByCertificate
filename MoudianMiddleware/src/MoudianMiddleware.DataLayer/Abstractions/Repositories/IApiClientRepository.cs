using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Abstractions.Repositories;

/// <summary>
/// عملیات داده‌ی کلیدهای API سیستم‌های خارجی.
/// </summary>
public interface IApiClientRepository : IGenericRepository<ApiClient>
{
    /// <summary>یافتن کلید با بخش ابتدایی آن (برای شناسایی سریع رکورد).</summary>
    /// <param name="keyPrefix">۸ کاراکتر ابتدایی کلید.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>کلید یا <c>null</c>.</returns>
    Task<ApiClient?> GetByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default);

    /// <summary>یافتن کلید با هش (مقایسه‌ی امن).</summary>
    /// <param name="keyHash">هش Base64 کلید.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>کلید یا <c>null</c>.</returns>
    Task<ApiClient?> GetByHashAsync(string keyHash, CancellationToken cancellationToken = default);
}
