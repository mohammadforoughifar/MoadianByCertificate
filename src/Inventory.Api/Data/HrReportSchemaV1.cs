using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// خودتعمیرِ اسکیمای «گزارش‌ساز»: قالب‌های شخصی گزارش.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class HrReportSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.GetDbConnection() is SqliteConnection sqlConn)
                await EnsureSqliteAsync(sqlConn.ConnectionString);
            else if (db.Database.GetDbConnection() is SqlConnection sqlServerConn)
                await EnsureSqlServerAsync(sqlServerConn.ConnectionString);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] HrReportSchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task EnsureSqliteAsync(string connectionString)
    {
        using var raw = new SqliteConnection(connectionString);
        raw.Open();

        void Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrReportTemplates (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Entity TEXT NOT NULL,
                ColumnsJson TEXT NOT NULL,
                FiltersJson TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrReportTpl_User ON HrReportTemplates (UserId)");

        await Task.CompletedTask;
    }

    private static async Task EnsureSqlServerAsync(string connectionString)
    {
        using var raw = new SqlConnection(connectionString);
        await raw.OpenAsync();

        async Task Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            await c.ExecuteNonQueryAsync();
        }

        await Exec(@"
IF OBJECT_ID(N'HrReportTemplates', N'U') IS NULL
CREATE TABLE HrReportTemplates (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    UserId int NOT NULL,
    Name nvarchar(150) NOT NULL,
    Entity nvarchar(20) NOT NULL,
    ColumnsJson nvarchar(max) NOT NULL,
    FiltersJson nvarchar(max) NOT NULL,
    CreatedAt datetime2 NOT NULL
)");
    }
}
