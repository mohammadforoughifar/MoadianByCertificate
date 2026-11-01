using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۱۲ آموزش و توسعه — اسکیمای FaLms:
/// FaLmsCourses + FaLmsNeeds + FaLmsEnrollments + FaLmsSessions + FaLmsAttendances +
/// FaLmsExams + FaLmsQuestions + FaLmsAttempts + FaLmsCertificates + FaLmsBudgets.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaLmsSchemaV1
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
            Console.WriteLine($"[DB] FaLmsSchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
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
            CREATE TABLE IF NOT EXISTS FaLmsCourses (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL,
                Title TEXT NOT NULL,
                Kind INTEGER NOT NULL DEFAULT 0,
                Description TEXT NULL,
                DurationHours REAL NOT NULL DEFAULT 0,
                CostPerPerson REAL NOT NULL DEFAULT 0,
                MaxSeats INTEGER NULL,
                TrainerName TEXT NULL,
                Location TEXT NULL,
                StartDate TEXT NULL,
                EndDate TEXT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                HasExam INTEGER NOT NULL DEFAULT 1,
                PassScore REAL NOT NULL DEFAULT 60,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS FaLmsNeeds (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Year INTEGER NOT NULL,
                Source INTEGER NOT NULL DEFAULT 3,
                PerfPeriodId INTEGER NULL,
                PerfKpiId INTEGER NULL,
                PerfScore REAL NULL,
                SkillTitle TEXT NOT NULL,
                Priority INTEGER NOT NULL DEFAULT 1,
                Status INTEGER NOT NULL DEFAULT 0,
                LinkedCourseId INTEGER NULL,
                Note TEXT NULL,
                RequestedByUserId INTEGER NULL,
                RequestedByName TEXT NULL,
                CreatedAt TEXT NOT NULL,
                DecidedByName TEXT NULL,
                DecidedAt TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaLmsNeed_Emp ON FaLmsNeeds (EmployeeId);
            CREATE TABLE IF NOT EXISTS FaLmsEnrollments (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CourseId INTEGER NOT NULL,
                EmployeeId INTEGER NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                EnrolledAt TEXT NOT NULL,
                DecidedByName TEXT NULL,
                DecidedAt TEXT NULL,
                FinalScore REAL NULL,
                Passed INTEGER NULL,
                AttendancePercent REAL NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaLmsEnrollment_Course ON FaLmsEnrollments (CourseId);
            CREATE INDEX IF NOT EXISTS IX_FaLmsEnrollment_Emp ON FaLmsEnrollments (EmployeeId);
            CREATE TABLE IF NOT EXISTS FaLmsSessions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CourseId INTEGER NOT NULL,
                SessionDate TEXT NOT NULL,
                StartTime TEXT NULL,
                EndTime TEXT NULL,
                Topic TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaLmsSession_Course ON FaLmsSessions (CourseId);
            CREATE TABLE IF NOT EXISTS FaLmsAttendances (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NOT NULL,
                EmployeeId INTEGER NOT NULL,
                Present INTEGER NOT NULL DEFAULT 0,
                Note TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaLmsAttendance_Session ON FaLmsAttendances (SessionId);
            CREATE TABLE IF NOT EXISTS FaLmsExams (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CourseId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                ExamDate TEXT NULL,
                DurationMinutes INTEGER NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS FaLmsQuestions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ExamId INTEGER NOT NULL,
                Text TEXT NOT NULL,
                OptA TEXT NULL,
                OptB TEXT NULL,
                OptC TEXT NULL,
                OptD TEXT NULL,
                CorrectIndex INTEGER NOT NULL DEFAULT 0,
                Score REAL NOT NULL DEFAULT 1,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_FaLmsQuestion_Exam ON FaLmsQuestions (ExamId);
            CREATE TABLE IF NOT EXISTS FaLmsAttempts (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ExamId INTEGER NOT NULL,
                EmployeeId INTEGER NOT NULL,
                StartedAt TEXT NOT NULL,
                SubmittedAt TEXT NULL,
                Answers TEXT NULL,
                Score REAL NULL,
                Passed INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaLmsAttempt_Exam ON FaLmsAttempts (ExamId);
            CREATE TABLE IF NOT EXISTS FaLmsCertificates (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CourseId INTEGER NOT NULL,
                EmployeeId INTEGER NOT NULL,
                CertNo TEXT NOT NULL,
                IssueDate TEXT NOT NULL,
                Score REAL NULL,
                VerifyCode TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_FaLmsCertificate_No ON FaLmsCertificates (CertNo);
            CREATE INDEX IF NOT EXISTS IX_FaLmsCertificate_Course ON FaLmsCertificates (CourseId);
            CREATE TABLE IF NOT EXISTS FaLmsBudgets (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Year INTEGER NOT NULL,
                Amount REAL NOT NULL DEFAULT 0,
                Note TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_FaLmsBudget_Year ON FaLmsBudgets (Year);");

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
IF OBJECT_ID(N'FaLmsCourses', N'U') IS NULL
CREATE TABLE FaLmsCourses (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Code nvarchar(30) NOT NULL,
    Title nvarchar(200) NOT NULL,
    Kind int NOT NULL DEFAULT 0,
    Description nvarchar(1000) NULL,
    DurationHours float NOT NULL DEFAULT 0,
    CostPerPerson float NOT NULL DEFAULT 0,
    MaxSeats int NULL,
    TrainerName nvarchar(150) NULL,
    Location nvarchar(200) NULL,
    StartDate datetime2 NULL,
    EndDate datetime2 NULL,
    Status int NOT NULL DEFAULT 0,
    HasExam bit NOT NULL DEFAULT 1,
    PassScore float NOT NULL DEFAULT 60,
    IsActive bit NOT NULL DEFAULT 1,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsNeeds', N'U') IS NULL
CREATE TABLE FaLmsNeeds (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    Year int NOT NULL,
    Source int NOT NULL DEFAULT 3,
    PerfPeriodId int NULL,
    PerfKpiId int NULL,
    PerfScore float NULL,
    SkillTitle nvarchar(200) NOT NULL,
    Priority int NOT NULL DEFAULT 1,
    Status int NOT NULL DEFAULT 0,
    LinkedCourseId int NULL,
    Note nvarchar(500) NULL,
    RequestedByUserId int NULL,
    RequestedByName nvarchar(150) NULL,
    CreatedAt datetime2 NOT NULL,
    DecidedByName nvarchar(150) NULL,
    DecidedAt datetime2 NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaLmsNeed_Emp' AND object_id = OBJECT_ID(N'FaLmsNeeds'))
CREATE INDEX IX_FaLmsNeed_Emp ON FaLmsNeeds (EmployeeId);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsEnrollments', N'U') IS NULL
CREATE TABLE FaLmsEnrollments (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    CourseId int NOT NULL,
    EmployeeId int NOT NULL,
    Status int NOT NULL DEFAULT 0,
    EnrolledAt datetime2 NOT NULL,
    DecidedByName nvarchar(150) NULL,
    DecidedAt datetime2 NULL,
    FinalScore float NULL,
    Passed bit NULL,
    AttendancePercent float NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaLmsEnrollment_Course' AND object_id = OBJECT_ID(N'FaLmsEnrollments'))
CREATE INDEX IX_FaLmsEnrollment_Course ON FaLmsEnrollments (CourseId);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaLmsEnrollment_Emp' AND object_id = OBJECT_ID(N'FaLmsEnrollments'))
CREATE INDEX IX_FaLmsEnrollment_Emp ON FaLmsEnrollments (EmployeeId);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsSessions', N'U') IS NULL
CREATE TABLE FaLmsSessions (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    CourseId int NOT NULL,
    SessionDate datetime2 NOT NULL,
    StartTime time NULL,
    EndTime time NULL,
    Topic nvarchar(200) NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaLmsSession_Course' AND object_id = OBJECT_ID(N'FaLmsSessions'))
CREATE INDEX IX_FaLmsSession_Course ON FaLmsSessions (CourseId);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsAttendances', N'U') IS NULL
CREATE TABLE FaLmsAttendances (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    SessionId int NOT NULL,
    EmployeeId int NOT NULL,
    Present bit NOT NULL DEFAULT 0,
    Note nvarchar(200) NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaLmsAttendance_Session' AND object_id = OBJECT_ID(N'FaLmsAttendances'))
CREATE INDEX IX_FaLmsAttendance_Session ON FaLmsAttendances (SessionId);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsExams', N'U') IS NULL
CREATE TABLE FaLmsExams (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    CourseId int NOT NULL,
    Title nvarchar(200) NOT NULL,
    ExamDate datetime2 NULL,
    DurationMinutes int NULL,
    IsActive bit NOT NULL DEFAULT 1,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsQuestions', N'U') IS NULL
CREATE TABLE FaLmsQuestions (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ExamId int NOT NULL,
    Text nvarchar(1000) NOT NULL,
    OptA nvarchar(500) NULL,
    OptB nvarchar(500) NULL,
    OptC nvarchar(500) NULL,
    OptD nvarchar(500) NULL,
    CorrectIndex int NOT NULL DEFAULT 0,
    Score float NOT NULL DEFAULT 1,
    SortOrder int NOT NULL DEFAULT 0
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaLmsQuestion_Exam' AND object_id = OBJECT_ID(N'FaLmsQuestions'))
CREATE INDEX IX_FaLmsQuestion_Exam ON FaLmsQuestions (ExamId);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsAttempts', N'U') IS NULL
CREATE TABLE FaLmsAttempts (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ExamId int NOT NULL,
    EmployeeId int NOT NULL,
    StartedAt datetime2 NOT NULL,
    SubmittedAt datetime2 NULL,
    Answers nvarchar(1000) NULL,
    Score float NULL,
    Passed bit NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaLmsAttempt_Exam' AND object_id = OBJECT_ID(N'FaLmsAttempts'))
CREATE INDEX IX_FaLmsAttempt_Exam ON FaLmsAttempts (ExamId);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsCertificates', N'U') IS NULL
CREATE TABLE FaLmsCertificates (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    CourseId int NOT NULL,
    EmployeeId int NOT NULL,
    CertNo nvarchar(30) NOT NULL,
    IssueDate datetime2 NOT NULL,
    Score float NULL,
    VerifyCode nvarchar(20) NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaLmsCertificate_No' AND object_id = OBJECT_ID(N'FaLmsCertificates'))
CREATE UNIQUE INDEX IX_FaLmsCertificate_No ON FaLmsCertificates (CertNo);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaLmsCertificate_Course' AND object_id = OBJECT_ID(N'FaLmsCertificates'))
CREATE INDEX IX_FaLmsCertificate_Course ON FaLmsCertificates (CourseId);");
        await Exec(@"
IF OBJECT_ID(N'FaLmsBudgets', N'U') IS NULL
CREATE TABLE FaLmsBudgets (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Year int NOT NULL,
    Amount float NOT NULL DEFAULT 0,
    Note nvarchar(300) NULL,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaLmsBudget_Year' AND object_id = OBJECT_ID(N'FaLmsBudgets'))
CREATE UNIQUE INDEX IX_FaLmsBudget_Year ON FaLmsBudgets (Year);");
    }
}
