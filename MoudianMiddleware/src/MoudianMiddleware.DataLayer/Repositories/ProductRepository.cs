using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Repositories;

/// <summary>
/// پیاده‌سازی داده‌ی کالا و خدمات.
/// </summary>
public sealed class ProductRepository : GenericRepository<Product>, IProductRepository
{
    /// <summary>
    /// سازنده با تزریق Context.
    /// </summary>
    /// <param name="context">Context دیتابیس.</param>
    public ProductRepository(MoudianDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public Task<bool> ExistsSstidAsync(string sstid, int? excludeId = null, CancellationToken cancellationToken = default)
        => Set.AnyAsync(
            x => x.Sstid == sstid && (excludeId == null || x.Id != excludeId.Value),
            cancellationToken);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> GetPagedAsync(
        string? search,
        bool? isActive,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var products = Set.AsNoTracking().Include(x => x.MeasuringUnit).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            products = products.Where(x => x.Title.Contains(term) || x.Sstid.Contains(term));
        }

        if (isActive is not null)
        {
            products = products.Where(x => x.IsActive == isActive.Value);
        }

        var totalCount = await products.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await products
            .OrderBy(x => x.Title)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }
}
