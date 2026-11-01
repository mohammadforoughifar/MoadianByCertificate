using MoudianMiddleware.CoreLayer.Infrastructure.Moadian;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;

/// <summary>مرز یگانه‌ی تماس با SDK رسمی TaxCollectData.</summary>
public interface IMoadianGateway
{
    /// <summary>ارسال یک صورتحساب؛ SDK 2.0.29 توکن لغو نمی‌پذیرد، پس لغو فقط پیش از آغاز درخواست شبکه اعمال می‌شود.</summary>
    Task<MoadianSendReceipt> SendInvoiceAsync(
        MoadianSetting setting,
        MoadianCredential credential,
        Invoice invoice,
        CancellationToken cancellationToken = default);

    /// <summary>استعلام وضعیت بر اساس شماره‌های پیگیری؛ SDK توکن لغو نمی‌پذیرد.</summary>
    Task<IReadOnlyList<MoadianInquiryResult>> InquiryByReferenceNumbersAsync(
        MoadianSetting setting,
        MoadianCredential credential,
        IReadOnlyCollection<string> referenceNumbers,
        CancellationToken cancellationToken = default);

    /// <summary>استعلام وضعیت بر اساس TaxId برای reconcile نتیجه‌ی مبهم ارسال.</summary>
    Task<IReadOnlyList<MoadianInvoiceStatusResult>> InquiryByTaxIdsAsync(
        MoadianSetting setting,
        MoadianCredential credential,
        IReadOnlyCollection<string> taxIds,
        CancellationToken cancellationToken = default);
}
