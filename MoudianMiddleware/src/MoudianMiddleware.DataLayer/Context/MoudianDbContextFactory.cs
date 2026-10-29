using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MoudianMiddleware.DataLayer.Context;

/// <summary>
/// کارخانه‌ی Context در زمان طراحی (Design-Time) برای اجرای دستورهای <c>dotnet ef</c>.
/// </summary>
/// <remarks>
/// <para>
/// فقط برای ساخت/به‌روزرسانی Migration استفاده می‌شود و در زمان اجرای برنامه نقشی ندارد.
/// رشته‌ی اتصال واقعی اینجا Hardcode نمی‌شود؛ از متغیر محیطی
/// <c>MOUDIAN__CONNECTION_STRING</c> خوانده می‌شود (و در نبود آن، یک مقدار فقط-طراحی).
/// </para>
/// <para>
/// دستور نمونه:
/// <c>dotnet ef migrations add InitialCreate -p src/MoudianMiddleware.DataLayer -s src/MoudianMiddleware.Api</c>
/// </para>
/// </remarks>
public sealed class MoudianDbContextFactory : IDesignTimeDbContextFactory<MoudianDbContext>
{
    /// <summary>ساخت Context برای ابزارهای طراحی.</summary>
    /// <param name="args">آرگومان‌های خط فرمان (استفاده نمی‌شود).</param>
    /// <returns>نمونه‌ی Context طراحی.</returns>
    public MoudianDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MOUDIAN__CONNECTION_STRING")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=MoudianMiddleware_Design;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<MoudianDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(MoudianDbContext).Assembly.FullName))
            .Options;

        // در زمان طراحی مستأجر وجود ندارد؛ فیلترها عملاً همه‌چیز را پنهان می‌کنند که برای Migration اهمیتی ندارد.
        return new MoudianDbContext(options, tenantProvider: null);
    }
}
