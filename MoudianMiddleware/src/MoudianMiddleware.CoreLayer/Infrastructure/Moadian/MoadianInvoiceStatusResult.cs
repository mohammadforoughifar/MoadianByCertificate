namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian;

/// <summary>نتیجه‌ی امن استعلام وضعیت بر اساس TaxId؛ متن خطای خام SDK عمداً حذف شده است.</summary>
public sealed record MoadianInvoiceStatusResult
{
    /// <summary>ساخت نتیجه‌ی نرمال‌شده‌ی وضعیت.</summary>
    /// <param name="taxId">شناسه‌ی مالیاتی صورتحساب.</param>
    /// <param name="invoiceStatus">وضعیت صورتحساب در allowlist.</param>
    /// <param name="article6Status">وضعیت اعتبارسنجی ماده‌ی ۶ در allowlist.</param>
    public MoadianInvoiceStatusResult(string taxId, string invoiceStatus, string article6Status)
    {
        TaxId = taxId;
        InvoiceStatus = invoiceStatus;
        Article6Status = article6Status;
    }

    /// <summary>شناسه‌ی مالیاتی صورتحساب.</summary>
    public string TaxId { get; }
    /// <summary>وضعیت صورتحساب.</summary>
    public string InvoiceStatus { get; }
    /// <summary>وضعیت اعتبارسنجی ماده‌ی ۶.</summary>
    public string Article6Status { get; }
}
