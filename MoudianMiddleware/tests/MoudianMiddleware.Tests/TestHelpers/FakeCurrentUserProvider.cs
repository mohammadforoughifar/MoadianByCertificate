using MoudianMiddleware.DataLayer.Abstractions;

namespace MoudianMiddleware.Tests.TestHelpers;

/// <summary>
/// تأمین‌کننده‌ی کاربر جاری برای تست (پیش‌فرض: عملیات سیستمی).
/// </summary>
public sealed class FakeCurrentUserProvider : ICurrentUserProvider
{
    /// <summary>
    /// سازنده با شناسه‌ی کاربر اختیاری.
    /// </summary>
    /// <param name="userId">شناسه‌ی کاربر.</param>
    /// <param name="userName">نام کاربری.</param>
    public FakeCurrentUserProvider(int? userId = null, string? userName = null)
    {
        UserId = userId;
        UserName = userName;
    }

    /// <inheritdoc />
    public int? UserId { get; }

    /// <inheritdoc />
    public string? UserName { get; }
}
