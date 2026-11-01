using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۵ ارتباطات: اعلان انتشار زمان‌بندی‌شده اطلاعیه‌ها + صندوق پیشنهادها.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaComSchemaV3
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
            Console.WriteLine($"[DB] FaComSchemaV3 خطا: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ================== SQLite ==================

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

        List<string> ColumnsOf(string table)
        {
            var list = new List<string>();
            using var c = raw.CreateCommand();
            c.CommandText = $"PRAGMA table_info({table})";
            using var r = c.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(1));
            return list;
        }

        var annCols = ColumnsOf("FaComAnnouncements");
        if (!annCols.Contains("NotifiedAt")) Exec("ALTER TABLE FaComAnnouncements ADD COLUMN NotifiedAt TEXT NULL");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaComSuggestions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NULL,
                Title TEXT NOT NULL,
                Body TEXT NOT NULL,
                Category INTEGER NOT NULL DEFAULT 0,
                Status INTEGER NOT NULL DEFAULT 0,
                IsAnonymous INTEGER NOT NULL DEFAULT 0,
                Response TEXT NULL,
                RespondedByName TEXT NULL,
                RespondedAt TEXT NULL,
                CreatedAt TEXT NOT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_FaComSuggestion_Emp ON FaComSuggestions (EmployeeId)");
        Exec("CREATE INDEX IF NOT EXISTS IX_FaComSuggestion_Status ON FaComSuggestions (Status)");

        await Task.CompletedTask;
    }

    // ================== SQL Server ==================

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

        await Exec("IF COL_LENGTH('FaComAnnouncements','NotifiedAt') IS NULL ALTER TABLE FaComAnnouncements ADD NotifiedAt datetime2 NULL;");

        await Exec(@"
IF OBJECT_ID(N'FaComSuggestions', N'U') IS NULL
CREATE TABLE FaComSuggestions (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NULL,
    Title nvarchar(200) NOT NULL,
    Body nvarchar(2000) NOT NULL,
    Category int NOT NULL DEFAULT 0,
    Status int NOT NULL DEFAULT 0,
    IsAnonymous bit NOT NULL DEFAULT 0,
    Response nvarchar(1000) NULL,
    RespondedByName nvarchar(150) NULL,
    RespondedAt datetime2 NULL,
    CreatedAt datetime2 NOT NULL
)");
    }
}
