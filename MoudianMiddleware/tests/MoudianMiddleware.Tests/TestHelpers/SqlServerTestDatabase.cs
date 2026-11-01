using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Context.Interceptors;
using MoudianMiddleware.Tests.TestHelpers;

namespace MoudianMiddleware.Tests.TestHelpers;

/// <summary>Creates a unique disposable SQL Server database using MOUDIAN_TEST_SQLSERVER as the server credential.</summary>
public sealed class SqlServerTestDatabase : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly string _databaseName;

    private SqlServerTestDatabase(string connectionString, string databaseName)
    { _connectionString = connectionString; _databaseName = databaseName; }

    public static async Task<SqlServerTestDatabase> CreateAsync()
    {
        var source = Environment.GetEnvironmentVariable("MOUDIAN_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("Set MOUDIAN_TEST_SQLSERVER to a SQL Server connection string with database-create privileges.");
        var builder = new SqlConnectionStringBuilder(source) { InitialCatalog = "MoudianConcurrency_" + Guid.NewGuid().ToString("N") };
        var database = new SqlServerTestDatabase(builder.ConnectionString, builder.InitialCatalog);
        await using var context = database.CreateContext(null, null);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
        return database;
    }

    public MoudianDbContext CreateContext(int? tenantId, MoudianMiddleware.DataLayer.Context.TenantScope? scope)
    {
        var tenant = new FakeTenantProvider(tenantId);
        var options = new DbContextOptionsBuilder<MoudianDbContext>().UseSqlServer(_connectionString)
            .AddInterceptors(new AuditableEntityInterceptor(new FakeCurrentUserProvider(), new FakeClock()), new TenantStampInterceptor(tenant, scope)).Options;
        return new MoudianDbContext(options, tenant, scope);
    }

    public string ConnectionString => _connectionString;

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext(null, null);
        await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
    }
}
