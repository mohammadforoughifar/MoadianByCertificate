using MoudianMiddleware.Contracts.ViewModels.Invoices;

namespace MoudianMiddleware.CoreLayer.Interfaces.Services;

/// <summary>تولید شناسه‌های مالیاتی و آماده‌سازی پیش‌نویس برای ارسال.</summary>
public interface IInvoicePreparationService
{
    /// <summary>ساخت TaxId و serial پایدار برای پیش‌نویس؛ فراخوانی تکراری روی ReadyToSend idempotent است.</summary>
    Task<InvoicePreparationResult> PrepareAsync(int invoiceId, CancellationToken cancellationToken = default);
}
