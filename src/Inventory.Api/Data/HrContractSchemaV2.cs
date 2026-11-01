using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۹ مدیریت قراردادها — گسترش HrContract:
///   ۱) ستون‌های قالب و امضای الکترونیکی روی HrContracts
///   ۲) جدول قالب‌های آماده (HrContractTemplates)
///   ۳) جدول نسخه‌های آرشیوی (HrContractVersions)
///   ۴) جدول رد هشدارهای انقضا (HrContractExpiryAlerts)
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class HrContractSchemaV2
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
            Console.WriteLine($"[DB] HrContractSchemaV2 خطا: {ex.GetType().Name}: {ex.Message}");
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
            CREATE TABLE IF NOT EXISTS HrContractTemplates (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Type INTEGER NOT NULL DEFAULT 1,
                DurationMonths INTEGER NULL,
                JobTitle TEXT NULL,
                Terms TEXT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS HrContractVersions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ContractId INTEGER NOT NULL,
                VersionNo INTEGER NOT NULL,
                ContractNo TEXT NOT NULL,
                Type INTEGER NOT NULL,
                StartDate TEXT NOT NULL,
                EndDate TEXT NULL,
                BaseSalary REAL NOT NULL DEFAULT 0,
                JobTitle TEXT NULL,
                OrgUnitId INTEGER NULL,
                Description TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                ChangedByUserId INTEGER NULL,
                ChangedByName TEXT NULL,
                ChangedAt TEXT NOT NULL,
                ChangeNote TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_HrContractVersion_Contract ON HrContractVersions (ContractId);
            CREATE TABLE IF NOT EXISTS HrContractExpiryAlerts (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ContractId INTEGER NOT NULL,
                ThresholdDays INTEGER NOT NULL,
                ExpireDate TEXT NOT NULL,
                NotifiedCount INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_HrContractExpiryAlert_Contract ON HrContractExpiryAlerts (ContractId);");

        var cols = ColumnsOf("HrContracts");
        if (cols.Contains("Id"))
        {
            if (!cols.Contains("TemplateId"))
                Exec("ALTER TABLE HrContracts ADD COLUMN TemplateId INTEGER NULL");
            if (!cols.Contains("SignStatus"))
                Exec("ALTER TABLE HrContracts ADD COLUMN SignStatus INTEGER NOT NULL DEFAULT 0");
            if (!cols.Contains("EmployeeSignedBy"))
                Exec("ALTER TABLE HrContracts ADD COLUMN EmployeeSignedBy TEXT NULL");
            if (!cols.Contains("EmployeeSignedAt"))
                Exec("ALTER TABLE HrContracts ADD COLUMN EmployeeSignedAt TEXT NULL");
            if (!cols.Contains("EmployerSignedByUserId"))
                Exec("ALTER TABLE HrContracts ADD COLUMN EmployerSignedByUserId INTEGER NULL");
            if (!cols.Contains("EmployerSignedByName"))
                Exec("ALTER TABLE HrContracts ADD COLUMN EmployerSignedByName TEXT NULL");
            if (!cols.Contains("EmployerSignedAt"))
                Exec("ALTER TABLE HrContracts ADD COLUMN EmployerSignedAt TEXT NULL");
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
IF OBJECT_ID(N'HrContractTemplates', N'U') IS NULL
CREATE TABLE HrContractTemplates (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Name nvarchar(100) NOT NULL,
    Type int NOT NULL DEFAULT 1,
    DurationMonths int NULL,
    JobTitle nvarchar(150) NULL,
    Terms nvarchar(2000) NULL,
    SortOrder int NOT NULL DEFAULT 0,
    IsActive bit NOT NULL DEFAULT 1
);");
        await Exec(@"
IF OBJECT_ID(N'HrContractVersions', N'U') IS NULL
CREATE TABLE HrContractVersions (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ContractId int NOT NULL,
    VersionNo int NOT NULL,
    ContractNo nvarchar(30) NOT NULL,
    Type int NOT NULL,
    StartDate datetime2 NOT NULL,
    EndDate datetime2 NULL,
    BaseSalary float NOT NULL DEFAULT 0,
    JobTitle nvarchar(150) NULL,
    OrgUnitId int NULL,
    Description nvarchar(500) NULL,
    IsActive bit NOT NULL DEFAULT 1,
    ChangedByUserId int NULL,
    ChangedByName nvarchar(150) NULL,
    ChangedAt datetime2 NOT NULL,
    ChangeNote nvarchar(300) NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HrContractVersion_Contract' AND object_id = OBJECT_ID(N'HrContractVersions'))
CREATE INDEX IX_HrContractVersion_Contract ON HrContractVersions (ContractId);");
        await Exec(@"
IF OBJECT_ID(N'HrContractExpiryAlerts', N'U') IS NULL
CREATE TABLE HrContractExpiryAlerts (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ContractId int NOT NULL,
    ThresholdDays int NOT NULL,
    ExpireDate datetime2 NOT NULL,
    NotifiedCount int NOT NULL DEFAULT 0,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HrContractExpiryAlert_Contract' AND object_id = OBJECT_ID(N'HrContractExpiryAlerts'))
CREATE INDEX IX_HrContractExpiryAlert_Contract ON HrContractExpiryAlerts (ContractId);");
        await Exec("IF COL_LENGTH('HrContracts','TemplateId') IS NULL ALTER TABLE HrContracts ADD TemplateId int NULL;");
        await Exec("IF COL_LENGTH('HrContracts','SignStatus') IS NULL ALTER TABLE HrContracts ADD SignStatus int NOT NULL DEFAULT 0;");
        await Exec("IF COL_LENGTH('HrContracts','EmployeeSignedBy') IS NULL ALTER TABLE HrContracts ADD EmployeeSignedBy nvarchar(150) NULL;");
        await Exec("IF COL_LENGTH('HrContracts','EmployeeSignedAt') IS NULL ALTER TABLE HrContracts ADD EmployeeSignedAt datetime2 NULL;");
        await Exec("IF COL_LENGTH('HrContracts','EmployerSignedByUserId') IS NULL ALTER TABLE HrContracts ADD EmployerSignedByUserId int NULL;");
        await Exec("IF COL_LENGTH('HrContracts','EmployerSignedByName') IS NULL ALTER TABLE HrContracts ADD EmployerSignedByName nvarchar(150) NULL;");
        await Exec("IF COL_LENGTH('HrContracts','EmployerSignedAt') IS NULL ALTER TABLE HrContracts ADD EmployerSignedAt datetime2 NULL;");
    }
}
