using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۴.۱ جذب و استخدام — جداول آگهی، متقاضی و مصاحبه.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class HrRecruitmentSchemaV1
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
            Console.WriteLine($"[DB] HrRecruitmentSchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
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
            CREATE TABLE IF NOT EXISTS HrJobPostings (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                OrgUnitId INTEGER NULL,
                Description TEXT NULL,
                Requirements TEXT NULL,
                Headcount INTEGER NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                PublishDate TEXT NULL,
                ExpireDate TEXT NULL,
                CreatedAt TEXT NOT NULL
            );");
        Exec(@"
            CREATE TABLE IF NOT EXISTS HrApplicants (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                JobPostingId INTEGER NOT NULL,
                FirstName TEXT NOT NULL,
                LastName TEXT NOT NULL,
                Mobile TEXT NULL,
                Email TEXT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                Score INTEGER NULL,
                Note TEXT NULL,
                EmployeeId INTEGER NULL,
                CreatedAt TEXT NOT NULL
            );");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrApplicant_Posting ON HrApplicants (JobPostingId);");
        Exec(@"
            CREATE TABLE IF NOT EXISTS HrInterviews (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ApplicantId INTEGER NOT NULL,
                InterviewDate TEXT NOT NULL,
                InterviewerName TEXT NULL,
                Score INTEGER NULL,
                Result INTEGER NOT NULL DEFAULT 0,
                Note TEXT NULL,
                CreatedAt TEXT NOT NULL
            );");
        Exec("CREATE INDEX IF NOT EXISTS IX_HrInterview_Applicant ON HrInterviews (ApplicantId);");
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
IF OBJECT_ID(N'HrJobPostings', N'U') IS NULL
CREATE TABLE HrJobPostings (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Title nvarchar(150) NOT NULL,
    OrgUnitId int NULL,
    Description nvarchar(500) NULL,
    Requirements nvarchar(500) NULL,
    Headcount int NULL,
    Status int NOT NULL DEFAULT 0,
    PublishDate datetime2 NULL,
    ExpireDate datetime2 NULL,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF OBJECT_ID(N'HrApplicants', N'U') IS NULL
CREATE TABLE HrApplicants (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    JobPostingId int NOT NULL,
    FirstName nvarchar(100) NOT NULL,
    LastName nvarchar(100) NOT NULL,
    Mobile nvarchar(15) NULL,
    Email nvarchar(150) NULL,
    Status int NOT NULL DEFAULT 0,
    Score int NULL,
    Note nvarchar(500) NULL,
    EmployeeId int NULL,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_HrApplicant_Posting')
CREATE INDEX IX_HrApplicant_Posting ON HrApplicants (JobPostingId);");
        await Exec(@"
IF OBJECT_ID(N'HrInterviews', N'U') IS NULL
CREATE TABLE HrInterviews (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ApplicantId int NOT NULL,
    InterviewDate datetime2 NOT NULL,
    InterviewerName nvarchar(150) NULL,
    Score int NULL,
    Result int NOT NULL DEFAULT 0,
    Note nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_HrInterview_Applicant')
CREATE INDEX IX_HrInterview_Applicant ON HrInterviews (ApplicantId);");
    }
}
