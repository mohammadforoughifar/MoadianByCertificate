using MoudianMiddleware.DataLayer.Abstractions;

namespace MoudianMiddleware.Tests.TestHelpers;

/// <summary>
/// تأمین‌کننده‌ی مستأجر ساده برای تست.
/// </summary>
public sealed class FakeTenantProvider : ITenantProvider
{
    /// <summary>
    /// سازنده با شناسه‌ی مستأجر.
    /// </summary>
    /// <param name="tenantId">شناسه‌ی مستأجر (یا <c>null</c>).</param>
    public FakeTenantProvider(int? tenantId)
    {
        TenantId = tenantId;
    }

    /// <inheritdoc />
    public int? TenantId { get; set; }
}
