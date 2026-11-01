using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Context.Interceptors;

namespace MoudianMiddleware.Tests.TestHelpers;

/// <summary>SQLite in-memory database held open on a single connection for relational tests.</summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteTestDatabase(SqliteConnection connection) => _connection = connection;

    /// <summary>Create and initialize a fresh relational in-memory database.</summary>
    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);
        var database = new SqliteTestDatabase(connection);
        await using var context = database.CreateContext(tenantId: null);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
        return database;
    }

    /// <summary>Create a context over this database with the requested default-deny tenant.</summary>
    public MoudianDbContext CreateContext(int? tenantId, TenantScope? explicitScope = null)
    {
        var tenantProvider = new FakeTenantProvider(tenantId);
        var tenantScope = explicitScope ?? new TenantScope();
        var options = new DbContextOptionsBuilder<MoudianDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(
                new AuditableEntityInterceptor(new FakeCurrentUserProvider(), new FakeClock()),
                new TenantStampInterceptor(tenantProvider, tenantScope))
            .Options;
        return new MoudianDbContext(options, tenantProvider, tenantScope);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
