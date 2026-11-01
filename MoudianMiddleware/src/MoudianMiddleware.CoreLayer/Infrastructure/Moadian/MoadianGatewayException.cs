namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian;

/// <summary>خطای بی‌خطر/نرمال‌شده‌ی Gateway؛ payload و credential در Message قرار نمی‌گیرند.</summary>
public sealed class MoadianGatewayException : Exception
{
    /// <summary>ساخت خطای Gateway؛ استثنای SDK عمداً inner exception نمی‌شود چون ممکن است پاسخ خام حساس داشته باشد.</summary>
    public MoadianGatewayException(string sdkExceptionType)
        : base("ارتباط با سامانه‌ی مودیان ناموفق بود.")
    {
        SdkExceptionType = sdkExceptionType;
    }

    /// <summary>نام نوع استثنای SDK، بدون پیام/بدنه‌ی حساس.</summary>
    public string SdkExceptionType { get; }
}
