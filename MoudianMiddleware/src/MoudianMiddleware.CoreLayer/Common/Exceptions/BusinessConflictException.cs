namespace MoudianMiddleware.CoreLayer.Common.Exceptions;

/// <summary>نشان می‌دهد عملیات با قاعده‌ی یکتایی یا وضعیت فعلی کسب‌وکار تعارض دارد.</summary>
public sealed class BusinessConflictException : Exception
{
    /// <summary>ساخت خطای تعارض با پیام قابل نمایش.</summary>
    public BusinessConflictException(string message) : base(message)
    {
    }
}
