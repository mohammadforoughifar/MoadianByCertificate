namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian;

/// <summary>کمینه‌ی رسید ارسال که برای ثبت در invoice/submission کافی است.</summary>
public sealed record MoadianSendReceipt
{
    /// <summary>ساخت رسید ارسال.</summary>
    /// <param name="uid">شناسه‌ی یکتای دریافت‌شده.</param>
    /// <param name="referenceNumber">شماره‌ی پیگیری.</param>
    /// <param name="taxId">شماره‌ی مالیاتی صورتحساب.</param>
    public MoadianSendReceipt(string uid, string referenceNumber, string taxId)
    {
        Uid = uid;
        ReferenceNumber = referenceNumber;
        TaxId = taxId;
    }

    /// <summary>شناسه‌ی یکتای دریافت‌شده از سامانه.</summary>
    public string Uid { get; }
    /// <summary>شماره‌ی پیگیری دریافت‌شده از سامانه.</summary>
    public string ReferenceNumber { get; }
    /// <summary>شماره‌ی مالیاتی صورتحساب.</summary>
    public string TaxId { get; }
}
