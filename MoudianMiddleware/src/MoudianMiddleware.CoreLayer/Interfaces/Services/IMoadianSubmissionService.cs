using MoudianMiddleware.CoreLayer.Infrastructure.Moadian;

namespace MoudianMiddleware.CoreLayer.Interfaces.Services;

/// <summary>ارسال و پیگیری صورتحساب با ثبت تاریخچه‌ی metadata-only.</summary>
public interface IMoadianSubmissionService
{
    /// <summary>ارسال صورتحسابی که صریحاً در وضعیت ReadyToSend باشد.</summary>
    Task<MoadianSendReceipt> SubmitAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>استعلام صورتحساب با شماره‌ی پیگیری و ثبت وضعیت بازگشتی.</summary>
    Task<IReadOnlyList<MoadianInquiryResult>> InquiryAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>استعلام بر اساس TaxId برای بازیابی ارسال‌هایی که شماره‌ی پیگیری نگرفته‌اند.</summary>
    Task<IReadOnlyList<MoadianInvoiceStatusResult>> InquiryByTaxIdAsync(int invoiceId, CancellationToken cancellationToken = default);
}
