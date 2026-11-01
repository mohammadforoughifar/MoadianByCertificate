using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۲ حقوق — افزونه‌های FaPay:
/// FaPayExtraSettings + FaPayLoans + FaPayLoanInstallments + FaPayArrears + FaPaySettlements،
/// ستون Kind روی FaPayRuns (با یکتایی Year/Month/Kind)، ستون InsuranceNo روی HrEmployees.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaPaySchemaV2
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
            Console.WriteLine($"[DB] FaPaySchemaV2 خطا: {ex.GetType().Name}: {ex.Message}");
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
            CREATE TABLE IF NOT EXISTS FaPayExtraSettings (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                WorkshopCode TEXT NULL,
                WorkshopName TEXT NULL,
                NightRatePercent REAL NOT NULL DEFAULT 35,
                EidiCapMultiplier REAL NOT NULL DEFAULT 3,
                EidiBaseMultiplier REAL NOT NULL DEFAULT 2
            );
            CREATE TABLE IF NOT EXISTS FaPayLoans (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                TotalAmount REAL NOT NULL DEFAULT 0,
                InstallmentCount INTEGER NOT NULL DEFAULT 1,
                InstallmentAmount REAL NOT NULL DEFAULT 0,
                StartYear INTEGER NOT NULL,
                StartMonth INTEGER NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                Note TEXT NULL,
                CreatedByUserId INTEGER NULL,
                CreatedByName TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaPayLoan_Emp ON FaPayLoans (EmployeeId);
            CREATE TABLE IF NOT EXISTS FaPayLoanInstallments (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                LoanId INTEGER NOT NULL,
                SeqNo INTEGER NOT NULL DEFAULT 1,
                Year INTEGER NOT NULL,
                Month INTEGER NOT NULL,
                Amount REAL NOT NULL DEFAULT 0,
                IsPaid INTEGER NOT NULL DEFAULT 0,
                PaidRunId INTEGER NULL,
                PaidAt TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaPayInst_Loan ON FaPayLoanInstallments (LoanId);
            CREATE INDEX IF NOT EXISTS IX_FaPayInst_YM ON FaPayLoanInstallments (Year, Month);
            CREATE TABLE IF NOT EXISTS FaPayArrears (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                FromYear INTEGER NOT NULL,
                FromMonth INTEGER NOT NULL,
                ToYear INTEGER NOT NULL,
                ToMonth INTEGER NOT NULL,
                Amount REAL NOT NULL DEFAULT 0,
                TargetYear INTEGER NOT NULL,
                TargetMonth INTEGER NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                AppliedRunId INTEGER NULL,
                Note TEXT NULL,
                CreatedByUserId INTEGER NULL,
                CreatedByName TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaPayArrear_Emp ON FaPayArrears (EmployeeId);
            CREATE INDEX IF NOT EXISTS IX_FaPayArrear_Target ON FaPayArrears (TargetYear, TargetMonth);
            CREATE TABLE IF NOT EXISTS FaPaySettlements (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                LeaveDate TEXT NOT NULL,
                Reason INTEGER NOT NULL DEFAULT 0,
                ServiceDays INTEGER NOT NULL DEFAULT 0,
                BaseSalary REAL NOT NULL DEFAULT 0,
                SenavatAmount REAL NOT NULL DEFAULT 0,
                EidiAmount REAL NOT NULL DEFAULT 0,
                LeaveBuybackDays REAL NOT NULL DEFAULT 0,
                LeaveBuybackAmount REAL NOT NULL DEFAULT 0,
                OtherEarnings REAL NOT NULL DEFAULT 0,
                OtherEarningsNote TEXT NULL,
                LoanRemaining REAL NOT NULL DEFAULT 0,
                OtherDeductions REAL NOT NULL DEFAULT 0,
                OtherDeductionsNote TEXT NULL,
                TaxAmount REAL NOT NULL DEFAULT 0,
                GrossTotal REAL NOT NULL DEFAULT 0,
                NetPayable REAL NOT NULL DEFAULT 0,
                Status INTEGER NOT NULL DEFAULT 0,
                DeactivateEmployee INTEGER NOT NULL DEFAULT 1,
                Note TEXT NULL,
                CreatedByUserId INTEGER NULL,
                CreatedByName TEXT NULL,
                CreatedAt TEXT NOT NULL,
                FinalizedAt TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaPaySettlement_Emp ON FaPaySettlements (EmployeeId);");

        var runCols = ColumnsOf("FaPayRuns");
        if (!runCols.Contains("Kind")) Exec("ALTER TABLE FaPayRuns ADD COLUMN Kind INTEGER NOT NULL DEFAULT 0");
        // یکتایی دوره: ماه + سال + نوع (تا دوره عیدی اسفند کنار دوره ماهانه اسفند بنشیند)
        Exec("DROP INDEX IF EXISTS IX_FaPayRun_YearMonth");
        Exec("CREATE UNIQUE INDEX IF NOT EXISTS IX_FaPayRun_YMK ON FaPayRuns (Year, Month, Kind)");

        var empCols = ColumnsOf("HrEmployees");
        if (!empCols.Contains("InsuranceNo")) Exec("ALTER TABLE HrEmployees ADD COLUMN InsuranceNo TEXT NULL");

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
IF OBJECT_ID(N'FaPayExtraSettings', N'U') IS NULL
CREATE TABLE FaPayExtraSettings (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    WorkshopCode nvarchar(14) NULL,
    WorkshopName nvarchar(200) NULL,
    NightRatePercent float NOT NULL DEFAULT 35,
    EidiCapMultiplier float NOT NULL DEFAULT 3,
    EidiBaseMultiplier float NOT NULL DEFAULT 2
);");
        await Exec(@"
IF OBJECT_ID(N'FaPayLoans', N'U') IS NULL
CREATE TABLE FaPayLoans (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    Title nvarchar(150) NOT NULL,
    TotalAmount float NOT NULL DEFAULT 0,
    InstallmentCount int NOT NULL DEFAULT 1,
    InstallmentAmount float NOT NULL DEFAULT 0,
    StartYear int NOT NULL,
    StartMonth int NOT NULL,
    Status int NOT NULL DEFAULT 0,
    Note nvarchar(500) NULL,
    CreatedByUserId int NULL,
    CreatedByName nvarchar(150) NULL,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPayLoan_Emp' AND object_id = OBJECT_ID(N'FaPayLoans'))
CREATE INDEX IX_FaPayLoan_Emp ON FaPayLoans (EmployeeId);");
        await Exec(@"
IF OBJECT_ID(N'FaPayLoanInstallments', N'U') IS NULL
CREATE TABLE FaPayLoanInstallments (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    LoanId int NOT NULL,
    SeqNo int NOT NULL DEFAULT 1,
    Year int NOT NULL,
    Month int NOT NULL,
    Amount float NOT NULL DEFAULT 0,
    IsPaid bit NOT NULL DEFAULT 0,
    PaidRunId int NULL,
    PaidAt datetime2 NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPayInst_Loan' AND object_id = OBJECT_ID(N'FaPayLoanInstallments'))
CREATE INDEX IX_FaPayInst_Loan ON FaPayLoanInstallments (LoanId);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPayInst_YM' AND object_id = OBJECT_ID(N'FaPayLoanInstallments'))
CREATE INDEX IX_FaPayInst_YM ON FaPayLoanInstallments (Year, Month);");
        await Exec(@"
IF OBJECT_ID(N'FaPayArrears', N'U') IS NULL
CREATE TABLE FaPayArrears (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    Title nvarchar(200) NOT NULL,
    FromYear int NOT NULL,
    FromMonth int NOT NULL,
    ToYear int NOT NULL,
    ToMonth int NOT NULL,
    Amount float NOT NULL DEFAULT 0,
    TargetYear int NOT NULL,
    TargetMonth int NOT NULL,
    Status int NOT NULL DEFAULT 0,
    AppliedRunId int NULL,
    Note nvarchar(500) NULL,
    CreatedByUserId int NULL,
    CreatedByName nvarchar(150) NULL,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPayArrear_Emp' AND object_id = OBJECT_ID(N'FaPayArrears'))
CREATE INDEX IX_FaPayArrear_Emp ON FaPayArrears (EmployeeId);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPayArrear_Target' AND object_id = OBJECT_ID(N'FaPayArrears'))
CREATE INDEX IX_FaPayArrear_Target ON FaPayArrears (TargetYear, TargetMonth);");
        await Exec(@"
IF OBJECT_ID(N'FaPaySettlements', N'U') IS NULL
CREATE TABLE FaPaySettlements (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    LeaveDate datetime2 NOT NULL,
    Reason int NOT NULL DEFAULT 0,
    ServiceDays int NOT NULL DEFAULT 0,
    BaseSalary float NOT NULL DEFAULT 0,
    SenavatAmount float NOT NULL DEFAULT 0,
    EidiAmount float NOT NULL DEFAULT 0,
    LeaveBuybackDays float NOT NULL DEFAULT 0,
    LeaveBuybackAmount float NOT NULL DEFAULT 0,
    OtherEarnings float NOT NULL DEFAULT 0,
    OtherEarningsNote nvarchar(300) NULL,
    LoanRemaining float NOT NULL DEFAULT 0,
    OtherDeductions float NOT NULL DEFAULT 0,
    OtherDeductionsNote nvarchar(300) NULL,
    TaxAmount float NOT NULL DEFAULT 0,
    GrossTotal float NOT NULL DEFAULT 0,
    NetPayable float NOT NULL DEFAULT 0,
    Status int NOT NULL DEFAULT 0,
    DeactivateEmployee bit NOT NULL DEFAULT 1,
    Note nvarchar(500) NULL,
    CreatedByUserId int NULL,
    CreatedByName nvarchar(150) NULL,
    CreatedAt datetime2 NOT NULL,
    FinalizedAt datetime2 NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPaySettlement_Emp' AND object_id = OBJECT_ID(N'FaPaySettlements'))
CREATE INDEX IX_FaPaySettlement_Emp ON FaPaySettlements (EmployeeId);");

        await Exec("IF COL_LENGTH('FaPayRuns','Kind') IS NULL ALTER TABLE FaPayRuns ADD Kind int NOT NULL DEFAULT 0;");
        await Exec(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPayRun_YearMonth' AND object_id = OBJECT_ID(N'FaPayRuns'))
    DROP INDEX IX_FaPayRun_YearMonth ON FaPayRuns;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaPayRun_YMK' AND object_id = OBJECT_ID(N'FaPayRuns'))
    CREATE UNIQUE INDEX IX_FaPayRun_YMK ON FaPayRuns (Year, Month, Kind);");

        await Exec("IF COL_LENGTH('HrEmployees','InsuranceNo') IS NULL ALTER TABLE HrEmployees ADD InsuranceNo nvarchar(12) NULL;");
    }
}
