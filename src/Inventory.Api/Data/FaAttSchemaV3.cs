using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۲/§۳ — شب‌کاری و نوبت‌کاری:
/// ستون NightMinutes روی FaAttDailies + ستون AllowancePercent روی FaAttShifts.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaAttSchemaV3
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
            Console.WriteLine($"[DB] FaAttSchemaV3 خطا: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ================== SQLite ==================

    private static async Task EnsureSqliteAsync(string connectionString)
    {
        using var raw = new SqliteConnection(connectionString);
        raw.Open();

        List<string> ColumnsOf(string table)
        {
            var cols = new List<string>();
            using var c = raw.CreateCommand();
            c.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
            using var rd = c.ExecuteReader();
            while (rd.Read()) cols.Add(rd.GetString(0));
            return cols;
        }

        void Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        var dailyCols = ColumnsOf("FaAttDailies");
        if (!dailyCols.Contains("NightMinutes")) Exec("ALTER TABLE FaAttDailies ADD COLUMN NightMinutes INTEGER NOT NULL DEFAULT 0");
        var shiftCols = ColumnsOf("FaAttShifts");
        if (!shiftCols.Contains("AllowancePercent")) Exec("ALTER TABLE FaAttShifts ADD COLUMN AllowancePercent REAL NOT NULL DEFAULT 0");
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

        await Exec("IF COL_LENGTH('FaAttDailies','NightMinutes') IS NULL ALTER TABLE FaAttDailies ADD NightMinutes int NOT NULL DEFAULT 0;");
        await Exec("IF COL_LENGTH('FaAttShifts','AllowancePercent') IS NULL ALTER TABLE FaAttShifts ADD AllowancePercent float NOT NULL DEFAULT 0;");
    }
}
