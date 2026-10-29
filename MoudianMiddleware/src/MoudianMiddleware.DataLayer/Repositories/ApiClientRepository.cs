using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Repositories;

/// <summary>
/// پیاده‌سازی داده‌ی کلیدهای API.
/// </summary>
public sealed class ApiClientRepository : GenericRepository<ApiClient>, IApiClientRepository
{
    /// <summary>
    /// سازنده با تزریق Context.
    /// </summary>
    /// <param name="context">Context دیتابیس.</param>
    public ApiClientRepository(MoudianDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public Task<ApiClient?> GetByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .FirstOrDefaultAsync(x => x.KeyPrefix == keyPrefix, cancellationToken);

    /// <inheritdoc />
    public Task<ApiClient?> GetByHashAsync(string keyHash, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .FirstOrDefaultAsync(x => x.KeyHash == keyHash, cancellationToken);
}
