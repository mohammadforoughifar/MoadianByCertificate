using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Abstractions.Repositories;

/// <summary>
/// عملیات داده‌ی تاریخچه‌ی تلاش‌های ارسال/استعلام صورتحساب.
/// </summary>
public interface IInvoiceSubmissionRepository : IGenericRepository<InvoiceSubmission>
{
    /// <summary>آخرین تلاش ثبت‌شده برای صورتحساب.</summary>
    /// <param name="invoiceId">شناسه‌ی صورتحساب.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>آخرین تلاش یا <c>null</c>.</returns>
    Task<InvoiceSubmission?> GetLatestAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>تعداد تلاش‌های انجام‌شده برای صورتحساب.</summary>
    /// <param name="invoiceId">شناسه‌ی صورتحساب.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>تعداد تلاش.</returns>
    Task<int> GetAttemptCountAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>فهرست تلاش‌های یک صورتحساب به ترتیب زمان (نزولی).</summary>
    /// <param name="invoiceId">شناسه‌ی صورتحساب.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>فهرست تلاش‌ها.</returns>
    Task<List<InvoiceSubmission>> ListByInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);
}
