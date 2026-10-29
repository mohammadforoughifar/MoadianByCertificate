using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Abstractions.Queries;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Repositories;

/// <summary>
/// پیاده‌سازی داده‌ی صورتحساب‌ها.
/// </summary>
public sealed class InvoiceRepository : GenericRepository<Invoice>, IInvoiceRepository
{
    /// <summary>
    /// سازنده با تزریق Context.
    /// </summary>
    /// <param name="context">Context دیتابیس.</param>
    public InvoiceRepository(MoudianDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public Task<Invoice?> GetWithDetailsAsync(int id, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Include(x => x.Submissions)
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Invoice?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExternalId == externalId, cancellationToken);

    /// <inheritdoc />
    public Task<Invoice?> GetByTaxIdAsync(string taxId, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TaxId == taxId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsExternalIdAsync(string externalId, CancellationToken cancellationToken = default)
        => Set.AnyAsync(x => x.ExternalId == externalId, cancellationToken);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Invoice> Items, int TotalCount)> GetPagedAsync(
        InvoiceQuery query,
        CancellationToken cancellationToken = default)
    {
        var invoices = ApplyFilters(Set.AsNoTracking(), query);

        var totalCount = await invoices.CountAsync(cancellationToken).ConfigureAwait(false);

        invoices = query.Descending
            ? invoices.OrderByDescending(x => x.IssueDate).ThenByDescending(x => x.Id)
            : invoices.OrderBy(x => x.IssueDate).ThenBy(x => x.Id);

        var items = await invoices
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    /// <inheritdoc />
    public Task<List<Invoice>> GetAwaitingInquiryAsync(
        DateTimeOffset sentBefore,
        int take,
        CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .Where(x => (x.Status == InvoiceStatus.Sent || x.Status == InvoiceStatus.InProgress)
                        && x.SentAt != null
                        && x.SentAt <= sentBefore)
            .OrderBy(x => x.SentAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    private static IQueryable<Invoice> ApplyFilters(IQueryable<Invoice> source, InvoiceQuery query)
    {
        var result = source;

        if (query.Status is not null)
        {
            result = result.Where(x => x.Status == query.Status.Value);
        }

        if (query.Type is not null)
        {
            result = result.Where(x => x.Type == query.Type.Value);
        }

        if (query.FromDate is not null)
        {
            result = result.Where(x => x.IssueDate >= query.FromDate.Value);
        }

        if (query.ToDate is not null)
        {
            result = result.Where(x => x.IssueDate <= query.ToDate.Value);
        }

        if (query.CustomerId is not null)
        {
            result = result.Where(x => x.CustomerId == query.CustomerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            result = result.Where(x =>
                x.InvoiceNumber.Contains(search)
                || x.BuyerName.Contains(search)
                || (x.TaxId != null && x.TaxId.Contains(search))
                || (x.ReferenceNumber != null && x.ReferenceNumber.Contains(search))
                || (x.ExternalId != null && x.ExternalId.Contains(search)));
        }

        return result;
    }
}
