using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.ViewModels.Invoices;

namespace MoudianMiddleware.CoreLayer.Interfaces.Services;

/// <summary>عملیات کاربردی صورتحساب.</summary>
public interface IInvoiceService
{
    /// <summary>دریافت صورتحساب همراه با اقلام، پرداخت‌ها و تلاش‌های ارسال.</summary>
    Task<InvoiceViewModel> GetAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>فهرست صفحه‌بندی‌شده‌ی صورتحساب‌های مودی جاری.</summary>
    Task<PagedResult<InvoiceViewModel>> ListAsync(InvoiceListQuery query, CancellationToken cancellationToken = default);
    /// <summary>ثبت پیش‌نویس؛ ExternalId در محدوده‌ی مودی idempotent است.</summary>
    Task<InvoiceViewModel> CreateAsync(InvoiceWriteRequest request, CancellationToken cancellationToken = default);
    /// <summary>ویرایش پیش‌نویس؛ وضعیت‌های ارسال‌شده قفل هستند.</summary>
    Task<InvoiceViewModel> UpdateDraftAsync(int id, InvoiceWriteRequest request, CancellationToken cancellationToken = default);
}
