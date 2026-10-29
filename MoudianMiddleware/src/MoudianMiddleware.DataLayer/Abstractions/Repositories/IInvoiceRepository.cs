using MoudianMiddleware.DataLayer.Abstractions.Queries;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.DataLayer.Abstractions.Repositories;

/// <summary>
/// عملیات داده‌ی مخصوص صورتحساب (بارگذاری کامل، جست‌وجوی Idempotent و صفحه‌بندی گزارش).
/// </summary>
public interface IInvoiceRepository : IGenericRepository<Invoice>
{
    /// <summary>دریافت صورتحساب همراه با اقلام، پرداخت‌ها و تاریخچه‌ی ارسال.</summary>
    /// <param name="id">شناسه.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>صورتحساب کامل یا <c>null</c>.</returns>
    Task<Invoice?> GetWithDetailsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>یافتن صورتحساب با شناسه‌ی خارجی (مبنای Idempotency).</summary>
    /// <param name="externalId">شناسه‌ی سیستم خارجی.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>صورتحساب یا <c>null</c>.</returns>
    Task<Invoice?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default);

    /// <summary>یافتن صورتحساب با شماره‌ی مالیاتی.</summary>
    /// <param name="taxId">شماره‌ی مالیاتی.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>صورتحساب یا <c>null</c>.</returns>
    Task<Invoice?> GetByTaxIdAsync(string taxId, CancellationToken cancellationToken = default);

    /// <summary>بررسی وجود شناسه‌ی خارجی در مستأجر جاری.</summary>
    /// <param name="externalId">شناسه‌ی خارجی.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>در صورت وجود <c>true</c>.</returns>
    Task<bool> ExistsExternalIdAsync(string externalId, CancellationToken cancellationToken = default);

    /// <summary>دریافت صفحه‌ای از صورتحساب‌ها با فیلتر گزارش.</summary>
    /// <param name="query">پارامترهای پرس‌وجو.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>آیتم‌های صفحه و تعداد کل.</returns>
    Task<(IReadOnlyList<Invoice> Items, int TotalCount)> GetPagedAsync(
        InvoiceQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>دریافت صورتحساب‌هایی که ارسال شده‌اند ولی نتیجه‌ی نهایی ندارند (برای Job استعلام).</summary>
    /// <param name="sentBefore">فقط ارسال‌های قبل از این زمان.</param>
    /// <param name="take">حداکثر تعداد.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    /// <returns>فهرست صورتحساب‌ها.</returns>
    Task<List<Invoice>> GetAwaitingInquiryAsync(
        DateTimeOffset sentBefore,
        int take,
        CancellationToken cancellationToken = default);
}
