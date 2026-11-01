using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۸ حقوق و دستمزد — اسکیمای FaPay:
/// FaPaySettings + FaPayTaxBrackets + FaPayItemTypes + FaPayRuns + FaPaySlips + FaPaySlipItems + FaPayAdjustments.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaPaySchemaV1
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
            Console.WriteLine($"[DB] FaPaySchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
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

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaPaySettings (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                DaysPerMonth REAL NOT NULL DEFAULT 30,
                HoursPerDay REAL NOT NULL DEFAULT 8,
                OvertimeFactor REAL NOT NULL DEFAULT 1.4,
                DelayFactor REAL NOT NULL DEFAULT 1,
                InsuranceEmployeeRate REAL NOT NULL DEFAULT 7,
                InsuranceEmployerRate REAL NOT NULL DEFAULT 23,
                TaxFreeMonthly REAL NOT NULL DEFAULT 0,
                AbsentDeductEnabled INTEGER NOT NULL DEFAULT 1,
                UnpaidLeaveDeductEnabled INTEGER NOT NULL DEFAULT 1,
                Note TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS FaPayTaxBrackets (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                FromAmount REAL NOT NULL DEFAULT 0,
                ToAmount REAL NULL,
                Rate REAL NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS FaPayItemTypes (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                Kind INTEGER NOT NULL DEFAULT 0,
                IsFixed INTEGER NOT NULL DEFAULT 0,
                DefaultAmount REAL NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS FaPayRuns (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Year INTEGER NOT NULL,
                Month INTEGER NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                Note TEXT NULL,
                CreatedByUserId INTEGER NULL,
                CreatedByName TEXT NULL,
                CreatedAt TEXT NOT NULL,
                FinalizedAt TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_FaPayRun_YearMonth ON FaPayRuns (Year, Month);
            CREATE TABLE IF NOT EXISTS FaPaySlips (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL,
                EmployeeId INTEGER NOT NULL,
                BaseSalary REAL NOT NULL DEFAULT 0,
                PresentDays INTEGER NOT NULL DEFAULT 0,
                AbsentDays INTEGER NOT NULL DEFAULT 0,
                UnpaidLeaveDays REAL NOT NULL DEFAULT 0,
                OvertimeMinutes INTEGER NOT NULL DEFAULT 0,
                OvertimeAmount REAL NOT NULL DEFAULT 0,
                DelayMinutes INTEGER NOT NULL DEFAULT 0,
                DelayAmount REAL NOT NULL DEFAULT 0,
                MissionDays INTEGER NOT NULL DEFAULT 0,
                AbsentAmount REAL NOT NULL DEFAULT 0,
                GrossEarnings REAL NOT NULL DEFAULT 0,
                TotalDeductions REAL NOT NULL DEFAULT 0,
                TaxAmount REAL NOT NULL DEFAULT 0,
                InsuranceAmount REAL NOT NULL DEFAULT 0,
                NetPay REAL NOT NULL DEFAULT 0,
                IsPaid INTEGER NOT NULL DEFAULT 0,
                CalculatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaPaySlip_Run ON FaPaySlips (RunId);
            CREATE INDEX IF NOT EXISTS IX_FaPaySlip_Emp ON FaPaySlips (EmployeeId);
            CREATE TABLE IF NOT EXISTS FaPaySlipItems (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                SlipId INTEGER NOT NULL,
                ItemTypeId INTEGER NULL,
                Title TEXT NOT NULL,
                Kind INTEGER NOT NULL DEFAULT 0,
                Amount REAL NOT NULL DEFAULT 0,
                IsAuto INTEGER NOT NULL DEFAULT 0,
                Note TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaPaySlipItem_Slip ON FaPaySlipItems (SlipId);
            CREATE TABLE IF NOT EXISTS FaPayAdjustments (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Year INTEGER NOT NULL,
                Month INTEGER NOT NULL,
                ItemTypeId INTEGER NOT NULL,
                Amount REAL NOT NULL DEFAULT 0,
                Note TEXT NULL,
                CreatedByUserId INTEGER NULL,
                CreatedByName TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaPayAdjustment_EmpYM ON FaPayAdjustments (EmployeeId, Year, Month);");

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
IF OBJECT_ID(N'FaPaySettings', N'U') IS NULL
CREATE TABLE FaPaySettings (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    DaysPerMonth float NOT NULL DEFAULT 30,
    HoursPerDay float NOT NULL DEFAULT 8,
    OvertimeFactor float NOT NULL DEFAULT 1.4,
    DelayFactor float NOT NULL DEFAULT 1,
    InsuranceEmployeeRate float NOT NULL DEFAULT 7,
    InsuranceEmployerRate float NOT NULL DEFAULT 23,
    TaxFreeMonthly float NOT NULL DEFAULT 0,
    AbsentDeductEnabled bit NOT NULL DEFAULT 1,
    UnpaidLeaveDeductEnabled bit NOT NULL DEFAULT 1,
    Note nvarchar(500) NULL
);");
        await Exec(@"
IF OBJECT_ID(N'FaPayTaxBrackets', N'U') IS NULL
CREATE TABLE FaPayTaxBrackets (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    FromAmount float NOT NULL DEFAULT 0,
    ToAmount float NULL,
    Rate float NOT NULL DEFAULT 0,
    SortOrder int NOT NULL DEFAULT 0,
    IsActive bit NOT NULL DEFAULT 1
);");
        await Exec(@"
IF OBJECT_ID(N'FaPayItemTypes', N'U') IS NULL
CREATE TABLE FaPayItemTypes (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Code nvarchar(20) NOT NULL,
    Name nvarchar(100) NOT NULL,
    Kind int NOT NULL DEFAULT 0,
    IsFixed bit NOT NULL DEFAULT 0,
    DefaultAmount float NOT NULL DEFAULT 0,
    SortOrder int NOT NULL DEFAULT 0,
    IsActive bit NOT NULL DEFAULT 1
);");
        await Exec(@"
IF OBJECT_ID(N'FaPayRuns', N'U') IS NULL
CREATE TABLE FaPayRuns (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Year int NOT NULL,
    Month int NOT NULL,
    Status int NOT NULL DEFAULT 0,
    Note nvarchar(500) NULL,
    CreatedByUserId int NULL,
    CreatedByName nvarchar(150) NULL,
    CreatedAt datetime2 NOT NULL,
    FinalizedAt datetime2 NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPayRun_YearMonth' AND object_id = OBJECT_ID(N'FaPayRuns'))
CREATE UNIQUE INDEX IX_FaPayRun_YearMonth ON FaPayRuns (Year, Month);");
        await Exec(@"
IF OBJECT_ID(N'FaPaySlips', N'U') IS NULL
CREATE TABLE FaPaySlips (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    RunId int NOT NULL,
    EmployeeId int NOT NULL,
    BaseSalary float NOT NULL DEFAULT 0,
    PresentDays int NOT NULL DEFAULT 0,
    AbsentDays int NOT NULL DEFAULT 0,
    UnpaidLeaveDays float NOT NULL DEFAULT 0,
    OvertimeMinutes int NOT NULL DEFAULT 0,
    OvertimeAmount float NOT NULL DEFAULT 0,
    DelayMinutes int NOT NULL DEFAULT 0,
    DelayAmount float NOT NULL DEFAULT 0,
    MissionDays int NOT NULL DEFAULT 0,
    AbsentAmount float NOT NULL DEFAULT 0,
    GrossEarnings float NOT NULL DEFAULT 0,
    TotalDeductions float NOT NULL DEFAULT 0,
    TaxAmount float NOT NULL DEFAULT 0,
    InsuranceAmount float NOT NULL DEFAULT 0,
    NetPay float NOT NULL DEFAULT 0,
    IsPaid bit NOT NULL DEFAULT 0,
    CalculatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPaySlip_Run' AND object_id = OBJECT_ID(N'FaPaySlips'))
CREATE INDEX IX_FaPaySlip_Run ON FaPaySlips (RunId);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPaySlip_Emp' AND object_id = OBJECT_ID(N'FaPaySlips'))
CREATE INDEX IX_FaPaySlip_Emp ON FaPaySlips (EmployeeId);");
        await Exec(@"
IF OBJECT_ID(N'FaPaySlipItems', N'U') IS NULL
CREATE TABLE FaPaySlipItems (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    SlipId int NOT NULL,
    ItemTypeId int NULL,
    Title nvarchar(150) NOT NULL,
    Kind int NOT NULL DEFAULT 0,
    Amount float NOT NULL DEFAULT 0,
    IsAuto bit NOT NULL DEFAULT 0,
    Note nvarchar(300) NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPaySlipItem_Slip' AND object_id = OBJECT_ID(N'FaPaySlipItems'))
CREATE INDEX IX_FaPaySlipItem_Slip ON FaPaySlipItems (SlipId);");
        await Exec(@"
IF OBJECT_ID(N'FaPayAdjustments', N'U') IS NULL
CREATE TABLE FaPayAdjustments (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    Year int NOT NULL,
    Month int NOT NULL,
    ItemTypeId int NOT NULL,
    Amount float NOT NULL DEFAULT 0,
    Note nvarchar(300) NULL,
    CreatedByUserId int NULL,
    CreatedByName nvarchar(150) NULL,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPayAdjustment_EmpYM' AND object_id = OBJECT_ID(N'FaPayAdjustments'))
CREATE INDEX IX_FaPayAdjustment_EmpYM ON FaPayAdjustments (EmployeeId, Year, Month);");
    }
}
