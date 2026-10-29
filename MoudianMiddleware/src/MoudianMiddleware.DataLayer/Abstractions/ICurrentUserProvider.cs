namespace MoudianMiddleware.DataLayer.Abstractions;

/// <summary>
/// تأمین‌کننده‌ی هویت کاربر جاری برای پر کردن فیلدهای Audit.
/// </summary>
/// <remarks>
/// در عملیات سیستمی (Jobها و Seed) مقدار <see cref="UserId"/> برابر <c>null</c> است.
/// </remarks>
public interface ICurrentUserProvider
{
    /// <summary>شناسه‌ی کاربر جاری یا <c>null</c> برای عملیات سیستمی.</summary>
    int? UserId { get; }

    /// <summary>نام کاربری جاری (برای لاگ) یا <c>null</c>.</summary>
    string? UserName { get; }
}
