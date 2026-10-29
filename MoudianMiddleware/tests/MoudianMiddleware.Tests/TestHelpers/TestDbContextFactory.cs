using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Context.Interceptors;

namespace MoudianMiddleware.Tests.TestHelpers;

/// <summary>
/// ساخت <see cref="MoudianDbContext"/> روی Provider حافظه‌ای برای تست (بدون نیاز به SQL Server).
/// </summary>
public static class TestDbContextFactory
{
    /// <summary>
    /// ساخت Context با پایگاه‌داده‌ی حافظه‌ای مشترک (بر اساس نام) و Interceptorهای واقعی.
    /// </summary>
    /// <param name="databaseName">نام پایگاه‌داده‌ی حافظه‌ای؛ دو Context با نام یکسان داده را به اشتراک می‌گذارند.</param>
    /// <param name="tenantId">شناسه‌ی مستأجر جاری.</param>
    /// <param name="userId">شناسه‌ی کاربر جاری (برای تست فیلدهای Audit).</param>
    /// <returns>Context آماده‌ی استفاده.</returns>
    public static MoudianDbContext Create(string databaseName, int? tenantId, int? userId = null)
    {
        var tenantProvider = new FakeTenantProvider(tenantId);

        var options = new DbContextOptionsBuilder<MoudianDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(
                new AuditableEntityInterceptor(new FakeCurrentUserProvider(userId), new FakeClock()),
                new TenantStampInterceptor(tenantProvider))
            .Options;

        return new MoudianDbContext(options, tenantProvider);
    }
}
