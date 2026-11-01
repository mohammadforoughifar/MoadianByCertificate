namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian;

/// <summary>حداقل اطلاعات نتیجه‌ی استعلام؛ داده‌ی کامل SDK از Gateway خارج نمی‌شود.</summary>
public sealed record MoadianInquiryResult
{
    /// <summary>ساخت نتیجه‌ی نرمال‌شده‌ی استعلام.</summary>
    /// <param name="referenceNumber">شماره‌ی پیگیری.</param>
    /// <param name="uid">شناسه‌ی یکتای صورتحساب.</param>
    /// <param name="status">وضعیت خام کوتاه مانند SUCCESS یا IN_PROGRESS.</param>
    public MoadianInquiryResult(string referenceNumber, string uid, string status)
    {
        ReferenceNumber = referenceNumber;
        Uid = uid;
        Status = status;
    }

    /// <summary>شماره‌ی پیگیری.</summary>
    public string ReferenceNumber { get; }
    /// <summary>شناسه‌ی یکتای صورتحساب.</summary>
    public string Uid { get; }
    /// <summary>وضعیت خام کوتاه.</summary>
    public string Status { get; }
}
