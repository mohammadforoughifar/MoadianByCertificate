using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventory.Api.Data;

/// <summary>EF tools use the same deployment configuration, without starting HTTP/HTTPS or workers.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = DesignTimeDatabaseConfiguration.GetSqlServerConnectionString(args);
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString).Options);
    }
}

internal static class DesignTimeDatabaseConfiguration
{
    internal static string GetSqlServerConnectionString(string[] args)
    {
        // dotnet ef may be invoked from the repo, solution, or API directory.
        var cwd = Directory.GetCurrentDirectory();
        var root = new[]
        {
            cwd,
            Path.Combine(cwd, "src", "Inventory.Api"),
            Path.Combine(cwd, "inventory", "src", "Inventory.Api"),
            AppContext.BaseDirectory
        }.FirstOrDefault(p => File.Exists(Path.Combine(p, "appsettings.json")))
            ?? throw new InvalidOperationException("پوشهٔ تنظیمات API پیدا نشد؛ دستور EF را از ریشه مخزن یا پوشهٔ API اجرا کنید.");

        // Includes appsettings.{Environment}.json, environment variables and command-line overrides.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = root,
            ApplicationName = typeof(DesignTimeDbContextFactory).Assembly.GetName().Name
        });
        var provider = builder.Configuration["Database:Provider"] ?? "SqlServer";
        if (!provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("مایگریشن‌های EF این پروژه مخصوص SQL Server هستند. برای SQLite از --update-database استفاده کنید.");

        return builder.Configuration.GetConnectionString("Default") is { Length: > 0 } connection
            && !string.IsNullOrWhiteSpace(connection)
                ? connection
                : throw new InvalidOperationException("ConnectionStrings:Default تنظیم نشده است.");
    }
}
