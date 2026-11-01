using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// خودتعمیرِ اسکیمای «پرونده کارمندان»:
/// HrEmployeeDependents + HrEmployeeCourses + HrEmployeeSkills + HrEmployeeLanguages + HrEmployeeDocuments
/// به‌علاوه‌ی ستون‌های جدید پرونده روی HrEmployees (عکس، تلفن ثابت، تماس اضطراری، محل کار، تحصیلات).
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class HrEmployeeDossierSchemaV1
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
            Console.WriteLine($"[DB] HrEmployeeDossierSchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
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

        bool HasColumn(string table, string column)
        {
            using var c = raw.CreateCommand();
            c.CommandText = $"PRAGMA table_info({table})";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrEmployeeDependents (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                FullName TEXT NOT NULL,
                Relation TEXT NOT NULL,
                BirthDate TEXT NULL,
                NationalCode TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_HrEmployeeDependents_EmployeeId ON HrEmployeeDependents (EmployeeId);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrEmployeeCourses (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Institute TEXT NULL,
                Year INTEGER NULL,
                DurationHours INTEGER NULL,
                HasCertificate INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_HrEmployeeCourses_EmployeeId ON HrEmployeeCourses (EmployeeId);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrEmployeeSkills (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Level INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_HrEmployeeSkills_EmployeeId ON HrEmployeeSkills (EmployeeId);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrEmployeeLanguages (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Language TEXT NOT NULL,
                Level INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_HrEmployeeLanguages_EmployeeId ON HrEmployeeLanguages (EmployeeId);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrEmployeeDocuments (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                DocType INTEGER NOT NULL DEFAULT 9,
                FilePath TEXT NULL,
                FileName TEXT NULL,
                ContentType TEXT NULL,
                FileSize INTEGER NULL,
                IssueDate TEXT NULL,
                ExpiryDate TEXT NULL,
                Notes TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_HrEmployeeDocuments_EmployeeId ON HrEmployeeDocuments (EmployeeId);
            CREATE INDEX IF NOT EXISTS IX_HrEmployeeDocuments_ExpiryDate ON HrEmployeeDocuments (ExpiryDate);");

        // ستون‌های جدید پرونده روی جدول موجود پرسنل
        var newCols = new (string Col, string Ddl)[]
        {
            ("PhotoPath", "TEXT NULL"),
            ("Landline", "TEXT NULL"),
            ("EmergencyContactName", "TEXT NULL"),
            ("EmergencyContactRelation", "TEXT NULL"),
            ("EmergencyContactPhone", "TEXT NULL"),
            ("Workplace", "TEXT NULL"),
            ("Degree", "TEXT NULL"),
            ("FieldOfStudy", "TEXT NULL"),
        };
        foreach (var (col, ddl) in newCols)
        {
            if (!HasColumn("HrEmployees", col))
                Exec($"ALTER TABLE HrEmployees ADD COLUMN {col} {ddl};");
        }

        await Task.CompletedTask;
    }

    // ================== SQL Server ==================

    private static async Task EnsureSqlServerAsync(string connectionString)
    {
        using var raw = new SqlConnection(connectionString);
        await raw.OpenAsync();

        async Task ExecAsync(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            await c.ExecuteNonQueryAsync();
        }

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrEmployeeDependents', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrEmployeeDependents] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [EmployeeId] int NOT NULL,
                    [FullName] nvarchar(150) NOT NULL,
                    [Relation] nvarchar(50) NOT NULL,
                    [BirthDate] datetime2 NULL,
                    [NationalCode] nvarchar(10) NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_HrEmployeeDependents_IsActive] DEFAULT(1),
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HrEmployeeDependents_EmployeeId' AND object_id = OBJECT_ID(N'dbo.HrEmployeeDependents'))
                CREATE INDEX [IX_HrEmployeeDependents_EmployeeId] ON [HrEmployeeDependents] ([EmployeeId]);");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrEmployeeCourses', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrEmployeeCourses] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [EmployeeId] int NOT NULL,
                    [Title] nvarchar(200) NOT NULL,
                    [Institute] nvarchar(200) NULL,
                    [Year] int NULL,
                    [DurationHours] int NULL,
                    [HasCertificate] bit NOT NULL CONSTRAINT [DF_HrEmployeeCourses_HasCertificate] DEFAULT(0),
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HrEmployeeCourses_EmployeeId' AND object_id = OBJECT_ID(N'dbo.HrEmployeeCourses'))
                CREATE INDEX [IX_HrEmployeeCourses_EmployeeId] ON [HrEmployeeCourses] ([EmployeeId]);");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrEmployeeSkills', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrEmployeeSkills] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [EmployeeId] int NOT NULL,
                    [Title] nvarchar(150) NOT NULL,
                    [Level] int NOT NULL CONSTRAINT [DF_HrEmployeeSkills_Level] DEFAULT(1),
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HrEmployeeSkills_EmployeeId' AND object_id = OBJECT_ID(N'dbo.HrEmployeeSkills'))
                CREATE INDEX [IX_HrEmployeeSkills_EmployeeId] ON [HrEmployeeSkills] ([EmployeeId]);");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrEmployeeLanguages', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrEmployeeLanguages] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [EmployeeId] int NOT NULL,
                    [Language] nvarchar(80) NOT NULL,
                    [Level] int NOT NULL CONSTRAINT [DF_HrEmployeeLanguages_Level] DEFAULT(1),
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HrEmployeeLanguages_EmployeeId' AND object_id = OBJECT_ID(N'dbo.HrEmployeeLanguages'))
                CREATE INDEX [IX_HrEmployeeLanguages_EmployeeId] ON [HrEmployeeLanguages] ([EmployeeId]);");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrEmployeeDocuments', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrEmployeeDocuments] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [EmployeeId] int NOT NULL,
                    [Title] nvarchar(200) NOT NULL,
                    [DocType] int NOT NULL CONSTRAINT [DF_HrEmployeeDocuments_DocType] DEFAULT(9),
                    [FilePath] nvarchar(300) NULL,
                    [FileName] nvarchar(200) NULL,
                    [ContentType] nvarchar(100) NULL,
                    [FileSize] bigint NULL,
                    [IssueDate] datetime2 NULL,
                    [ExpiryDate] datetime2 NULL,
                    [Notes] nvarchar(500) NULL,
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HrEmployeeDocuments_EmployeeId' AND object_id = OBJECT_ID(N'dbo.HrEmployeeDocuments'))
                CREATE INDEX [IX_HrEmployeeDocuments_EmployeeId] ON [HrEmployeeDocuments] ([EmployeeId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HrEmployeeDocuments_ExpiryDate' AND object_id = OBJECT_ID(N'dbo.HrEmployeeDocuments'))
                CREATE INDEX [IX_HrEmployeeDocuments_ExpiryDate] ON [HrEmployeeDocuments] ([ExpiryDate]);");

        // ستون‌های جدید پرونده روی جدول موجود پرسنل
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrEmployees', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.HrEmployees', N'PhotoPath') IS NULL
                ALTER TABLE dbo.HrEmployees ADD PhotoPath nvarchar(300) NULL;
            IF OBJECT_ID(N'dbo.HrEmployees', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.HrEmployees', N'Landline') IS NULL
                ALTER TABLE dbo.HrEmployees ADD Landline nvarchar(20) NULL;
            IF OBJECT_ID(N'dbo.HrEmployees', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.HrEmployees', N'EmergencyContactName') IS NULL
                ALTER TABLE dbo.HrEmployees ADD EmergencyContactName nvarchar(100) NULL;
            IF OBJECT_ID(N'dbo.HrEmployees', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.HrEmployees', N'EmergencyContactRelation') IS NULL
                ALTER TABLE dbo.HrEmployees ADD EmergencyContactRelation nvarchar(50) NULL;
            IF OBJECT_ID(N'dbo.HrEmployees', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.HrEmployees', N'EmergencyContactPhone') IS NULL
                ALTER TABLE dbo.HrEmployees ADD EmergencyContactPhone nvarchar(20) NULL;
            IF OBJECT_ID(N'dbo.HrEmployees', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.HrEmployees', N'Workplace') IS NULL
                ALTER TABLE dbo.HrEmployees ADD Workplace nvarchar(150) NULL;
            IF OBJECT_ID(N'dbo.HrEmployees', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.HrEmployees', N'Degree') IS NULL
                ALTER TABLE dbo.HrEmployees ADD Degree nvarchar(50) NULL;
            IF OBJECT_ID(N'dbo.HrEmployees', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.HrEmployees', N'FieldOfStudy') IS NULL
                ALTER TABLE dbo.HrEmployees ADD FieldOfStudy nvarchar(100) NULL;");
    }
}
