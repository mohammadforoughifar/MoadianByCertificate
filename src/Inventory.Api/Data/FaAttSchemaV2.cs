using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۷ مدیریت مرخصی — موج دوم اسکیمای FaAtt:
///   ۱) جدول مانده مرخصی سالانه (FaAttLeaveBalances)
///   ۲) ستون‌های گردش‌کار دومرحله‌ای روی FaAttLeaves (نزد مدیر ← نزد HR)
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaAttSchemaV2
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
            Console.WriteLine($"[DB] FaAttSchemaV2 خطا: {ex.GetType().Name}: {ex.Message}");
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

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaAttLeaveBalances (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Year INTEGER NOT NULL,
                LeaveTypeId INTEGER NOT NULL,
                EntitledDays REAL NOT NULL DEFAULT 0,
                UsedDays REAL NOT NULL DEFAULT 0,
                CarriedDays REAL NOT NULL DEFAULT 0,
                CashedDays REAL NOT NULL DEFAULT 0,
                CashAmount REAL NULL,
                Note TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaAttLeaveBalances_EmpYear ON FaAttLeaveBalances (EmployeeId, Year);");

        var cols = ColumnsOf("FaAttLeaves");
        if (cols.Contains("Id"))
        {
            if (!cols.Contains("WorkflowStep"))
                Exec("ALTER TABLE FaAttLeaves ADD COLUMN WorkflowStep INTEGER NOT NULL DEFAULT 0");
            if (!cols.Contains("ManagerDecidedByUserId"))
                Exec("ALTER TABLE FaAttLeaves ADD COLUMN ManagerDecidedByUserId INTEGER NULL");
            if (!cols.Contains("ManagerDecidedByName"))
                Exec("ALTER TABLE FaAttLeaves ADD COLUMN ManagerDecidedByName TEXT NULL");
            if (!cols.Contains("ManagerDecidedAt"))
                Exec("ALTER TABLE FaAttLeaves ADD COLUMN ManagerDecidedAt TEXT NULL");
        }

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

        await Exec(@"
IF OBJECT_ID(N'FaAttLeaveBalances', N'U') IS NULL
CREATE TABLE FaAttLeaveBalances (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    Year int NOT NULL,
    LeaveTypeId int NOT NULL,
    EntitledDays float NOT NULL DEFAULT 0,
    UsedDays float NOT NULL DEFAULT 0,
    CarriedDays float NOT NULL DEFAULT 0,
    CashedDays float NOT NULL DEFAULT 0,
    CashAmount float NULL,
    Note nvarchar(300) NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaAttLeaveBalances_EmpYear' AND object_id = OBJECT_ID(N'FaAttLeaveBalances'))
CREATE INDEX IX_FaAttLeaveBalances_EmpYear ON FaAttLeaveBalances (EmployeeId, Year);");
        await Exec("IF COL_LENGTH('FaAttLeaves','WorkflowStep') IS NULL ALTER TABLE FaAttLeaves ADD WorkflowStep int NOT NULL DEFAULT 0;");
        await Exec("IF COL_LENGTH('FaAttLeaves','ManagerDecidedByUserId') IS NULL ALTER TABLE FaAttLeaves ADD ManagerDecidedByUserId int NULL;");
        await Exec("IF COL_LENGTH('FaAttLeaves','ManagerDecidedByName') IS NULL ALTER TABLE FaAttLeaves ADD ManagerDecidedByName nvarchar(150) NULL;");
        await Exec("IF COL_LENGTH('FaAttLeaves','ManagerDecidedAt') IS NULL ALTER TABLE FaAttLeaves ADD ManagerDecidedAt datetime2 NULL;");
    }
}
