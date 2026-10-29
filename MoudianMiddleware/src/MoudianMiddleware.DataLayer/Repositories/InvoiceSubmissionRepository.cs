using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Repositories;

/// <summary>
/// پیاده‌سازی داده‌ی تاریخچه‌ی تلاش‌های ارسال/استعلام.
/// </summary>
public sealed class InvoiceSubmissionRepository : GenericRepository<InvoiceSubmission>, IInvoiceSubmissionRepository
{
    /// <summary>
    /// سازنده با تزریق Context.
    /// </summary>
    /// <param name="context">Context دیتابیس.</param>
    public InvoiceSubmissionRepository(MoudianDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public Task<InvoiceSubmission?> GetLatestAsync(int invoiceId, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .Where(x => x.InvoiceId == invoiceId)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> GetAttemptCountAsync(int invoiceId, CancellationToken cancellationToken = default)
        => Set.CountAsync(x => x.InvoiceId == invoiceId, cancellationToken);

    /// <inheritdoc />
    public Task<List<InvoiceSubmission>> ListByInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
        => Set.AsNoTracking()
            .Where(x => x.InvoiceId == invoiceId)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
}
