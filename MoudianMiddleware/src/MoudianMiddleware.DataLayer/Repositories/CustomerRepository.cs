using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Repositories;

/// <summary>
/// پیاده‌سازی داده‌ی مشتریان.
/// </summary>
public sealed class CustomerRepository : GenericRepository<Customer>, ICustomerRepository
{
    /// <summary>
    /// سازنده با تزریق Context.
    /// </summary>
    /// <param name="context">Context دیتابیس.</param>
    public CustomerRepository(MoudianDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default)
        => Set.AnyAsync(
            x => x.Code == code && (excludeId == null || x.Id != excludeId.Value),
            cancellationToken);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Customer> Items, int TotalCount)> GetPagedAsync(
        string? search,
        BuyerType? buyerType,
        bool? isActive,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var customers = Set.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            customers = customers.Where(x =>
                x.Name.Contains(term)
                || x.Code.Contains(term)
                || (x.NationalId != null && x.NationalId.Contains(term))
                || (x.EconomicCode != null && x.EconomicCode.Contains(term)));
        }

        if (buyerType is not null)
        {
            customers = customers.Where(x => x.BuyerType == buyerType.Value);
        }

        if (isActive is not null)
        {
            customers = customers.Where(x => x.IsActive == isActive.Value);
        }

        var totalCount = await customers.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await customers
            .OrderBy(x => x.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }
}
