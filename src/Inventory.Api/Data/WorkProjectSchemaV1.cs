using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «پروژه کاری» (داخل دستور کار) — نسخه ۱
//  • مثل سایر SchemaV1ها — idempotent و ایمن برای هر دو پرووایدر
//    (SQL Server و SQLite). جدول‌ها:
//   • WorkProjects        — پروژه‌های کاری
//   • WorkProjectMembers  — اعضای تیم هر پروژه
//   • WorkProjectLogs     — تاریخچه فعالیت پروژه
//  • دو ستون جدید روی WorkOrders (هر دو اختیاری/پیش‌فرض‌دار — داده موجود دست نمی‌خورد):
//   • ProjectId (int NULL) — اتصال اختیاری به پروژه
//   • Weight (int NOT NULL DEFAULT 1) — وزن در پیشرفت (۰ تا ۱۰)
// ============================================================
public static class WorkProjectSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkProjects', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkProjects
    (
        Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkProjects PRIMARY KEY,
        CompanyId       int NOT NULL,
        Number          nvarchar(30) NOT NULL,
        Title           nvarchar(200) NOT NULL,
        Description     nvarchar(4000) NULL,
        OwnerUserId     int NOT NULL,
        OwnerName       nvarchar(150) NOT NULL,
        StartAt         datetime2 NOT NULL,
        DueAt           datetime2 NOT NULL,
        Status          nvarchar(20) NOT NULL,
        Priority        int NOT NULL CONSTRAINT DF_WorkProjects_Priority DEFAULT(1),
        ExtensionCount  int NOT NULL CONSTRAINT DF_WorkProjects_ExtensionCount DEFAULT(0),
        Tags            nvarchar(300) NULL,
        CloseNote       nvarchar(1000) NULL,
        ClosedAt        datetime2 NULL,
        DeletedAt       datetime2 NULL,
        DeletedByUserId int NULL,
        CreatedAt       datetime2 NOT NULL
    );
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkProjectMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkProjectMembers
    (
        Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkProjectMembers PRIMARY KEY,
        ProjectId int NOT NULL CONSTRAINT FK_WorkProjectMembers_Project FOREIGN KEY REFERENCES dbo.WorkProjects(Id) ON DELETE CASCADE,
        UserId    int NOT NULL,
        Name      nvarchar(150) NOT NULL,
        AddedAt   datetime2 NOT NULL
    );
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkProjectLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkProjectLogs
    (
        Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkProjectLogs PRIMARY KEY,
        ProjectId int NOT NULL CONSTRAINT FK_WorkProjectLogs_Project FOREIGN KEY REFERENCES dbo.WorkProjects(Id) ON DELETE CASCADE,
        ActorName nvarchar(150) NOT NULL,
        Action    nvarchar(30) NOT NULL,
        Text      nvarchar(4000) NULL,
        CreatedAt datetime2 NOT NULL
    );
END;");

        // ---------- ستون‌های جدید WorkOrders (دیتابیس‌های موجود) ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'ProjectId') IS NULL
    ALTER TABLE dbo.WorkOrders ADD ProjectId int NULL;");
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'Weight') IS NULL
    ALTER TABLE dbo.WorkOrders ADD Weight int NOT NULL CONSTRAINT DF_WorkOrders_Weight DEFAULT(1);");
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.FK_WorkOrders_WorkProjects', N'F') IS NULL
AND OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.WorkProjects', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.WorkOrders', N'ProjectId') IS NOT NULL
    ALTER TABLE dbo.WorkOrders ADD CONSTRAINT FK_WorkOrders_WorkProjects FOREIGN KEY (ProjectId) REFERENCES dbo.WorkProjects(Id);");

        // ---------- ایندکس‌ها ----------
        await SafeAsync(db, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkProjects_Company_Status' AND object_id = OBJECT_ID(N'dbo.WorkProjects'))
    CREATE INDEX IX_WorkProjects_Company_Status ON dbo.WorkProjects(CompanyId, Status);");
        await SafeAsync(db, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkProjects_Company_Due' AND object_id = OBJECT_ID(N'dbo.WorkProjects'))
    CREATE INDEX IX_WorkProjects_Company_Due ON dbo.WorkProjects(CompanyId, DueAt);");
        await SafeAsync(db, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkProjectMembers_Project_User' AND object_id = OBJECT_ID(N'dbo.WorkProjectMembers'))
    CREATE UNIQUE INDEX IX_WorkProjectMembers_Project_User ON dbo.WorkProjectMembers(ProjectId, UserId);");
        await SafeAsync(db, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkProjectLogs_Project' AND object_id = OBJECT_ID(N'dbo.WorkProjectLogs'))
    CREATE INDEX IX_WorkProjectLogs_Project ON dbo.WorkProjectLogs(ProjectId);");
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.WorkOrders', N'ProjectId') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkOrders_ProjectId' AND object_id = OBJECT_ID(N'dbo.WorkOrders'))
    CREATE INDEX IX_WorkOrders_ProjectId ON dbo.WorkOrders(ProjectId);");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS WorkProjects
(
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyId       INTEGER NOT NULL,
    Number          TEXT NOT NULL,
    Title           TEXT NOT NULL,
    Description     TEXT NULL,
    OwnerUserId     INTEGER NOT NULL,
    OwnerName       TEXT NOT NULL,
    StartAt         TEXT NOT NULL,
    DueAt           TEXT NOT NULL,
    Status          TEXT NOT NULL,
    Priority        INTEGER NOT NULL DEFAULT 1,
    ExtensionCount  INTEGER NOT NULL DEFAULT 0,
    Tags            TEXT NULL,
    CloseNote       TEXT NULL,
    ClosedAt        TEXT NULL,
    DeletedAt       TEXT NULL,
    DeletedByUserId INTEGER NULL,
    CreatedAt       TEXT NOT NULL
);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS WorkProjectMembers
(
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectId INTEGER NOT NULL REFERENCES WorkProjects(Id) ON DELETE CASCADE,
    UserId    INTEGER NOT NULL,
    Name      TEXT NOT NULL,
    AddedAt   TEXT NOT NULL
);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS WorkProjectLogs
(
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectId INTEGER NOT NULL REFERENCES WorkProjects(Id) ON DELETE CASCADE,
    ActorName TEXT NOT NULL,
    Action    TEXT NOT NULL,
    Text      TEXT NULL,
    CreatedAt TEXT NOT NULL
);");

        // ستون‌های جدید WorkOrders — خطای «duplicate column» توسط SafeAsync نادیده گرفته می‌شود
        await SafeAsync(db, "ALTER TABLE WorkOrders ADD COLUMN ProjectId INTEGER NULL;");
        await SafeAsync(db, "ALTER TABLE WorkOrders ADD COLUMN Weight INTEGER NOT NULL DEFAULT 1;");

        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_WorkProjects_Company_Status ON WorkProjects(CompanyId, Status);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_WorkProjects_Company_Due ON WorkProjects(CompanyId, DueAt);");
        await SafeAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkProjectMembers_Project_User ON WorkProjectMembers(ProjectId, UserId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_WorkProjectLogs_Project ON WorkProjectLogs(ProjectId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_WorkOrders_ProjectId ON WorkOrders(ProjectId);");
    }

    /// <summary>اجرای امن — خطای «تکراری» راه‌اندازی برنامه را متوقف نکند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] WorkProjectSchemaV1: {m}");
        }
    }
}
