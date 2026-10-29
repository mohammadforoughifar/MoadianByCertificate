using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Repositories;

/// <summary>
/// پیاده‌سازی داده‌ی مودیان.
/// </summary>
public sealed class TenantRepository : GenericRepository<Tenant>, ITenantRepository
{
    /// <summary>
    /// سازنده با تزریق Context.
    /// </summary>
    /// <param name="context">Context دیتابیس.</param>
    public TenantRepository(MoudianDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public Task<Tenant?> GetWithSettingsAsync(int id, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .Include(x => x.MoadianSetting)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Tenant?> GetByMemoryIdAsync(string memoryId, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .Include(x => x.MoadianSetting)
            .FirstOrDefaultAsync(
                x => x.MoadianSetting != null && x.MoadianSetting.MemoryId == memoryId,
                cancellationToken);

    /// <inheritdoc />
    public Task<List<Tenant>> ListActiveAsync(CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
}
