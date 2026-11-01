using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RadisHr.Api.Data;

/// <summary>Keep EF tooling independent of the web host, with the module's own migration history.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = DesignTimeDatabaseConfiguration.GetSqlServerConnectionString(args);
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection, sql =>
            {
                sql.EnableRetryOnFailure(3);
                sql.MigrationsHistoryTable("__EFMigrationsHistory_RadisHr");
            }).Options);
    }
}
