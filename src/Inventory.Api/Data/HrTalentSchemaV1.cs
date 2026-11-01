using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// خودتعمیرِ اسکیمای «استعداد و ارزیابی» (HrTalent):
/// آنبوردینگ + ترک‌کار + سوابق شغلی + دوره آزمایشی + ارزیابی عملکرد.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class HrTalentSchemaV1
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
            Console.WriteLine($"[DB] HrTalentSchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
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
            CREATE TABLE IF NOT EXISTS HrOnboardings (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                StartDate TEXT NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                Note TEXT NULL,
                CompletedBy TEXT NULL,
                CompletedAt TEXT NULL,
                CreatedAt TEXT NOT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrOnboarding_Emp ON HrOnboardings (EmployeeId)");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrOnboardingItems (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                OnboardingId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Owner INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Status INTEGER NOT NULL DEFAULT 0,
                DoneBy TEXT NULL,
                DoneAt TEXT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrOnboardingItem_Case ON HrOnboardingItems (OnboardingId)");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrExitCases (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                RequestDate TEXT NOT NULL,
                LastWorkDate TEXT NULL,
                Type INTEGER NOT NULL DEFAULT 0,
                Reason TEXT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                CompletedBy TEXT NULL,
                CompletedAt TEXT NULL,
                CreatedAt TEXT NOT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrExitCase_Emp ON HrExitCases (EmployeeId)");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrExitItems (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ExitCaseId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Owner INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Status INTEGER NOT NULL DEFAULT 0,
                DoneBy TEXT NULL,
                DoneAt TEXT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrExitItem_Case ON HrExitItems (ExitCaseId)");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrJobHistories (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                FromDate TEXT NOT NULL,
                ToDate TEXT NULL,
                PostTitle TEXT NOT NULL,
                OrgUnitName TEXT NULL,
                EmploymentType INTEGER NULL,
                Note TEXT NULL,
                CreatedAt TEXT NOT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrJobHistory_Emp ON HrJobHistories (EmployeeId)");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrTrialPeriods (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                StartDate TEXT NOT NULL,
                Months INTEGER NOT NULL DEFAULT 3,
                EndDate TEXT NOT NULL,
                Result INTEGER NOT NULL DEFAULT 0,
                ResultNote TEXT NULL,
                DecidedBy TEXT NULL,
                DecidedAt TEXT NULL,
                CreatedAt TEXT NOT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrTrial_Emp ON HrTrialPeriods (EmployeeId)");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrAppraisals (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Year INTEGER NOT NULL,
                Period INTEGER NOT NULL DEFAULT 0,
                Status INTEGER NOT NULL DEFAULT 0,
                FinalizedBy TEXT NULL,
                FinalizedAt TEXT NULL,
                CreatedAt TEXT NOT NULL
            )");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrAppraisalKpis (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                AppraisalId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Weight REAL NOT NULL DEFAULT 10,
                MaxScore REAL NOT NULL DEFAULT 100,
                SortOrder INTEGER NOT NULL DEFAULT 0
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrAppraisalKpi_App ON HrAppraisalKpis (AppraisalId)");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrAppraisalScores (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                AppraisalId INTEGER NOT NULL,
                KpiId INTEGER NOT NULL,
                EmployeeId INTEGER NOT NULL,
                ManagerScore REAL NULL,
                SelfScore REAL NULL,
                Note TEXT NULL,
                UpdatedAt TEXT NOT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrAppraisalScore_App ON HrAppraisalScores (AppraisalId)");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrAppraisalScore_Emp ON HrAppraisalScores (EmployeeId)");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrProfileEditRequests (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Fields TEXT NOT NULL,
                Reason TEXT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                DecidedBy TEXT NULL,
                DecidedAt TEXT NULL,
                DecideNote TEXT NULL,
                CreatedAt TEXT NOT NULL
            )");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrProfileEditRequest_Emp ON HrProfileEditRequests (EmployeeId)");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrProfileEditRequest_Status ON HrProfileEditRequests (Status)");

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
IF OBJECT_ID(N'HrOnboardings', N'U') IS NULL
CREATE TABLE HrOnboardings (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    StartDate datetime2 NOT NULL,
    Status int NOT NULL DEFAULT 0,
    Note nvarchar(500) NULL,
    CompletedBy nvarchar(150) NULL,
    CompletedAt datetime2 NULL,
    CreatedAt datetime2 NOT NULL
)");

        await Exec(@"
IF OBJECT_ID(N'HrOnboardingItems', N'U') IS NULL
CREATE TABLE HrOnboardingItems (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    OnboardingId int NOT NULL,
    Title nvarchar(200) NOT NULL,
    Owner int NOT NULL DEFAULT 0,
    SortOrder int NOT NULL DEFAULT 0,
    Status int NOT NULL DEFAULT 0,
    DoneBy nvarchar(150) NULL,
    DoneAt datetime2 NULL
)");

        await Exec(@"
IF OBJECT_ID(N'HrExitCases', N'U') IS NULL
CREATE TABLE HrExitCases (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    RequestDate datetime2 NOT NULL,
    LastWorkDate datetime2 NULL,
    Type int NOT NULL DEFAULT 0,
    Reason nvarchar(500) NULL,
    Status int NOT NULL DEFAULT 0,
    CompletedBy nvarchar(150) NULL,
    CompletedAt datetime2 NULL,
    CreatedAt datetime2 NOT NULL
)");

        await Exec(@"
IF OBJECT_ID(N'HrExitItems', N'U') IS NULL
CREATE TABLE HrExitItems (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ExitCaseId int NOT NULL,
    Title nvarchar(200) NOT NULL,
    Owner int NOT NULL DEFAULT 0,
    SortOrder int NOT NULL DEFAULT 0,
    Status int NOT NULL DEFAULT 0,
    DoneBy nvarchar(150) NULL,
    DoneAt datetime2 NULL
)");

        await Exec(@"
IF OBJECT_ID(N'HrJobHistories', N'U') IS NULL
CREATE TABLE HrJobHistories (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    FromDate datetime2 NOT NULL,
    ToDate datetime2 NULL,
    PostTitle nvarchar(150) NOT NULL,
    OrgUnitName nvarchar(150) NULL,
    EmploymentType int NULL,
    Note nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL
)");

        await Exec(@"
IF OBJECT_ID(N'HrTrialPeriods', N'U') IS NULL
CREATE TABLE HrTrialPeriods (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    StartDate datetime2 NOT NULL,
    Months int NOT NULL DEFAULT 3,
    EndDate datetime2 NOT NULL,
    Result int NOT NULL DEFAULT 0,
    ResultNote nvarchar(500) NULL,
    DecidedBy nvarchar(150) NULL,
    DecidedAt datetime2 NULL,
    CreatedAt datetime2 NOT NULL
)");

        await Exec(@"
IF OBJECT_ID(N'HrAppraisals', N'U') IS NULL
CREATE TABLE HrAppraisals (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Title nvarchar(150) NOT NULL,
    Year int NOT NULL,
    Period int NOT NULL DEFAULT 0,
    Status int NOT NULL DEFAULT 0,
    FinalizedBy nvarchar(150) NULL,
    FinalizedAt datetime2 NULL,
    CreatedAt datetime2 NOT NULL
)");

        await Exec(@"
IF OBJECT_ID(N'HrAppraisalKpis', N'U') IS NULL
CREATE TABLE HrAppraisalKpis (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    AppraisalId int NOT NULL,
    Title nvarchar(200) NOT NULL,
    Weight float NOT NULL DEFAULT 10,
    MaxScore float NOT NULL DEFAULT 100,
    SortOrder int NOT NULL DEFAULT 0
)");

        await Exec(@"
IF OBJECT_ID(N'HrAppraisalScores', N'U') IS NULL
CREATE TABLE HrAppraisalScores (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    AppraisalId int NOT NULL,
    KpiId int NOT NULL,
    EmployeeId int NOT NULL,
    ManagerScore float NULL,
    SelfScore float NULL,
    Note nvarchar(300) NULL,
    UpdatedAt datetime2 NOT NULL
)");

        await Exec(@"
IF OBJECT_ID(N'HrProfileEditRequests', N'U') IS NULL
CREATE TABLE HrProfileEditRequests (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    Fields nvarchar(max) NOT NULL,
    Reason nvarchar(500) NULL,
    Status int NOT NULL DEFAULT 0,
    DecidedBy nvarchar(150) NULL,
    DecidedAt datetime2 NULL,
    DecideNote nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL
)");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HrProfileEditRequest_Emp' AND object_id = OBJECT_ID(N'HrProfileEditRequests'))
    CREATE INDEX IX_HrProfileEditRequest_Emp ON HrProfileEditRequests (EmployeeId)");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HrProfileEditRequest_Status' AND object_id = OBJECT_ID(N'HrProfileEditRequests'))
    CREATE INDEX IX_HrProfileEditRequest_Status ON HrProfileEditRequests (Status)");
    }
}
