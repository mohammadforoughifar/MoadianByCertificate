using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیر اسکیمای «میز کار توسعه» — نسخه ۱
//  idempotent برای SQL Server و SQLite
// ============================================================
public static class DevTeamSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);

        await SeedDefaultsAsync(db);
    }

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtWorkflowStatuses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtWorkflowStatuses (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtWorkflowStatuses PRIMARY KEY,
        [Key] nvarchar(40) NOT NULL,
        NameFa nvarchar(80) NOT NULL,
        Color nvarchar(20) NOT NULL,
        SortOrder int NOT NULL CONSTRAINT DF_DtWs_Sort DEFAULT(0),
        IsInitial bit NOT NULL CONSTRAINT DF_DtWs_Init DEFAULT(0),
        IsDone bit NOT NULL CONSTRAINT DF_DtWs_Done DEFAULT(0),
        IsBlocked bit NOT NULL CONSTRAINT DF_DtWs_Blk DEFAULT(0),
        IsActive bit NOT NULL CONSTRAINT DF_DtWs_Act DEFAULT(1)
    );
    CREATE UNIQUE INDEX IX_DtWorkflowStatuses_Key ON dbo.DtWorkflowStatuses([Key]);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtProductModules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtProductModules (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtProductModules PRIMARY KEY,
        [Key] nvarchar(40) NOT NULL,
        NameFa nvarchar(120) NOT NULL,
        Icon nvarchar(40) NULL,
        Color nvarchar(20) NOT NULL,
        Description nvarchar(300) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_DtPm_Sort DEFAULT(0),
        IsActive bit NOT NULL CONSTRAINT DF_DtPm_Act DEFAULT(1)
    );
    CREATE UNIQUE INDEX IX_DtProductModules_Key ON dbo.DtProductModules([Key]);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtSprints', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtSprints (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtSprints PRIMARY KEY,
        Name nvarchar(120) NOT NULL,
        Goal nvarchar(500) NULL,
        StartDate datetime2 NOT NULL,
        EndDate datetime2 NOT NULL,
        Status nvarchar(20) NOT NULL,
        CreatedAt datetime2 NOT NULL,
        CreatedByUserId int NOT NULL,
        CreatedByName nvarchar(150) NOT NULL
    );
    CREATE INDEX IX_DtSprints_Status ON dbo.DtSprints(Status);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTasks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtTasks (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtTasks PRIMARY KEY,
        Number nvarchar(30) NOT NULL,
        Title nvarchar(250) NOT NULL,
        Description nvarchar(max) NULL,
        [Type] nvarchar(30) NOT NULL,
        Priority int NOT NULL CONSTRAINT DF_DtTask_Pri DEFAULT(1),
        StatusId int NOT NULL,
        ModuleId int NULL,
        SprintId int NULL,
        AssigneeUserId int NULL,
        AssigneeName nvarchar(150) NULL,
        ReporterUserId int NOT NULL,
        ReporterName nvarchar(150) NOT NULL,
        DueAt datetime2 NULL,
        EstimateHours decimal(10,2) NULL,
        SpentHours decimal(10,2) NOT NULL CONSTRAINT DF_DtTask_Spent DEFAULT(0),
        Progress int NOT NULL CONSTRAINT DF_DtTask_Prog DEFAULT(0),
        Tags nvarchar(400) NULL,
        CreatedAt datetime2 NOT NULL,
        UpdatedAt datetime2 NULL,
        CompletedAt datetime2 NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_DtTask_Del DEFAULT(0),
        DeletedAt datetime2 NULL,
        DeletedByUserId int NULL
    );
    CREATE UNIQUE INDEX IX_DtTasks_Number ON dbo.DtTasks(Number);
    CREATE INDEX IX_DtTasks_StatusId ON dbo.DtTasks(StatusId);
    CREATE INDEX IX_DtTasks_Assignee ON dbo.DtTasks(AssigneeUserId);
    CREATE INDEX IX_DtTasks_Module ON dbo.DtTasks(ModuleId);
    CREATE INDEX IX_DtTasks_Sprint ON dbo.DtTasks(SprintId);
    CREATE INDEX IX_DtTasks_Deleted ON dbo.DtTasks(IsDeleted);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTaskComments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtTaskComments (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtTaskComments PRIMARY KEY,
        TaskId int NOT NULL,
        AuthorUserId int NOT NULL,
        AuthorName nvarchar(150) NOT NULL,
        Text nvarchar(4000) NOT NULL,
        CreatedAt datetime2 NOT NULL
    );
    CREATE INDEX IX_DtTaskComments_Task ON dbo.DtTaskComments(TaskId);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTaskActivities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtTaskActivities (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtTaskActivities PRIMARY KEY,
        TaskId int NOT NULL,
        ActorUserId int NOT NULL,
        ActorName nvarchar(150) NOT NULL,
        Action nvarchar(40) NOT NULL,
        Detail nvarchar(1000) NULL,
        CreatedAt datetime2 NOT NULL
    );
    CREATE INDEX IX_DtTaskActivities_Task ON dbo.DtTaskActivities(TaskId);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTaskGitLinks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtTaskGitLinks (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtTaskGitLinks PRIMARY KEY,
        TaskId int NOT NULL,
        Kind nvarchar(20) NOT NULL,
        Ref nvarchar(200) NOT NULL,
        Url nvarchar(500) NULL,
        Title nvarchar(300) NULL,
        AddedByUserId int NOT NULL,
        AddedByName nvarchar(150) NOT NULL,
        CreatedAt datetime2 NOT NULL
    );
    CREATE INDEX IX_DtTaskGitLinks_Task ON dbo.DtTaskGitLinks(TaskId);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTimeEntries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtTimeEntries (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtTimeEntries PRIMARY KEY,
        TaskId int NOT NULL,
        UserId int NOT NULL,
        UserName nvarchar(150) NOT NULL,
        Hours decimal(10,2) NOT NULL,
        WorkDate datetime2 NOT NULL,
        Note nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL
    );
    CREATE INDEX IX_DtTimeEntries_Task ON dbo.DtTimeEntries(TaskId);
    CREATE INDEX IX_DtTimeEntries_User ON dbo.DtTimeEntries(UserId);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtProblems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtProblems (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtProblems PRIMARY KEY,
        Title nvarchar(250) NOT NULL,
        Description nvarchar(max) NULL,
        Severity nvarchar(20) NOT NULL,
        Status nvarchar(20) NOT NULL,
        ModuleId int NULL,
        TaskId int NULL,
        AssigneeUserId int NULL,
        AssigneeName nvarchar(150) NULL,
        ReporterUserId int NOT NULL,
        ReporterName nvarchar(150) NOT NULL,
        StackTrace nvarchar(max) NULL,
        Environment nvarchar(300) NULL,
        CreatedAt datetime2 NOT NULL,
        ResolvedAt datetime2 NULL,
        ResolutionNote nvarchar(1000) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_DtProb_Del DEFAULT(0)
    );
    CREATE INDEX IX_DtProblems_Status ON dbo.DtProblems(Status);
    CREATE INDEX IX_DtProblems_Severity ON dbo.DtProblems(Severity);
    CREATE INDEX IX_DtProblems_Module ON dbo.DtProblems(ModuleId);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtModuleChanges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtModuleChanges (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtModuleChanges PRIMARY KEY,
        ModuleId int NOT NULL,
        TaskId int NULL,
        Summary nvarchar(300) NOT NULL,
        Details nvarchar(max) NULL,
        CommitSha nvarchar(80) NULL,
        Branch nvarchar(120) NULL,
        CommitUrl nvarchar(500) NULL,
        AuthorUserId int NOT NULL,
        AuthorName nvarchar(150) NOT NULL,
        ChangedAt datetime2 NOT NULL,
        CreatedAt datetime2 NOT NULL
    );
    CREATE INDEX IX_DtModuleChanges_Module ON dbo.DtModuleChanges(ModuleId);
    CREATE INDEX IX_DtModuleChanges_ChangedAt ON dbo.DtModuleChanges(ChangedAt);
END;");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtWorkflowStatuses (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Key TEXT NOT NULL,
    NameFa TEXT NOT NULL,
    Color TEXT NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsInitial INTEGER NOT NULL DEFAULT 0,
    IsDone INTEGER NOT NULL DEFAULT 0,
    IsBlocked INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1
);");
        await SafeAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_DtWorkflowStatuses_Key ON DtWorkflowStatuses(Key);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtProductModules (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Key TEXT NOT NULL,
    NameFa TEXT NOT NULL,
    Icon TEXT NULL,
    Color TEXT NOT NULL,
    Description TEXT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1
);");
        await SafeAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_DtProductModules_Key ON DtProductModules(Key);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtSprints (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Goal TEXT NULL,
    StartDate TEXT NOT NULL,
    EndDate TEXT NOT NULL,
    Status TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    CreatedByUserId INTEGER NOT NULL,
    CreatedByName TEXT NOT NULL
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtSprints_Status ON DtSprints(Status);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtTasks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    Title TEXT NOT NULL,
    Description TEXT NULL,
    Type TEXT NOT NULL,
    Priority INTEGER NOT NULL DEFAULT 1,
    StatusId INTEGER NOT NULL,
    ModuleId INTEGER NULL,
    SprintId INTEGER NULL,
    AssigneeUserId INTEGER NULL,
    AssigneeName TEXT NULL,
    ReporterUserId INTEGER NOT NULL,
    ReporterName TEXT NOT NULL,
    DueAt TEXT NULL,
    EstimateHours REAL NULL,
    SpentHours REAL NOT NULL DEFAULT 0,
    Progress INTEGER NOT NULL DEFAULT 0,
    Tags TEXT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NULL,
    CompletedAt TEXT NULL,
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    DeletedAt TEXT NULL,
    DeletedByUserId INTEGER NULL
);");
        await SafeAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_DtTasks_Number ON DtTasks(Number);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTasks_StatusId ON DtTasks(StatusId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTasks_Assignee ON DtTasks(AssigneeUserId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTasks_Module ON DtTasks(ModuleId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTasks_Sprint ON DtTasks(SprintId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtTaskComments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER NOT NULL,
    AuthorUserId INTEGER NOT NULL,
    AuthorName TEXT NOT NULL,
    Text TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTaskComments_Task ON DtTaskComments(TaskId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtTaskActivities (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER NOT NULL,
    ActorUserId INTEGER NOT NULL,
    ActorName TEXT NOT NULL,
    Action TEXT NOT NULL,
    Detail TEXT NULL,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTaskActivities_Task ON DtTaskActivities(TaskId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtTaskGitLinks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER NOT NULL,
    Kind TEXT NOT NULL,
    Ref TEXT NOT NULL,
    Url TEXT NULL,
    Title TEXT NULL,
    AddedByUserId INTEGER NOT NULL,
    AddedByName TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTaskGitLinks_Task ON DtTaskGitLinks(TaskId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtTimeEntries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    UserName TEXT NOT NULL,
    Hours REAL NOT NULL,
    WorkDate TEXT NOT NULL,
    Note TEXT NULL,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTimeEntries_Task ON DtTimeEntries(TaskId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtProblems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Title TEXT NOT NULL,
    Description TEXT NULL,
    Severity TEXT NOT NULL,
    Status TEXT NOT NULL,
    ModuleId INTEGER NULL,
    TaskId INTEGER NULL,
    AssigneeUserId INTEGER NULL,
    AssigneeName TEXT NULL,
    ReporterUserId INTEGER NOT NULL,
    ReporterName TEXT NOT NULL,
    StackTrace TEXT NULL,
    Environment TEXT NULL,
    CreatedAt TEXT NOT NULL,
    ResolvedAt TEXT NULL,
    ResolutionNote TEXT NULL,
    IsDeleted INTEGER NOT NULL DEFAULT 0
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtProblems_Status ON DtProblems(Status);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtProblems_Module ON DtProblems(ModuleId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtModuleChanges (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ModuleId INTEGER NOT NULL,
    TaskId INTEGER NULL,
    Summary TEXT NOT NULL,
    Details TEXT NULL,
    CommitSha TEXT NULL,
    Branch TEXT NULL,
    CommitUrl TEXT NULL,
    AuthorUserId INTEGER NOT NULL,
    AuthorName TEXT NOT NULL,
    ChangedAt TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtModuleChanges_Module ON DtModuleChanges(ModuleId);");
    }

    // ==================== Seed defaults ====================
    private static async Task SeedDefaultsAsync(AppDbContext db)
    {
        try
        {
            if (!await db.DtWorkflowStatuses.AnyAsync())
            {
                var statuses = new[]
                {
                    new DtWorkflowStatus { Key = "backlog", NameFa = "بک‌لاگ", Color = "#94a3b8", SortOrder = 10, IsInitial = true },
                    new DtWorkflowStatus { Key = "todo", NameFa = "آماده انجام", Color = "#3b82f6", SortOrder = 20 },
                    new DtWorkflowStatus { Key = "doing", NameFa = "در حال انجام", Color = "#8b5cf6", SortOrder = 30 },
                    new DtWorkflowStatus { Key = "review", NameFa = "بازبینی / تست", Color = "#f59e0b", SortOrder = 40 },
                    new DtWorkflowStatus { Key = "blocked", NameFa = "مسدود", Color = "#ef4444", SortOrder = 50, IsBlocked = true },
                    new DtWorkflowStatus { Key = "done", NameFa = "انجام‌شده", Color = "#10b981", SortOrder = 60, IsDone = true },
                };
                db.DtWorkflowStatuses.AddRange(statuses);
                await db.SaveChangesAsync();
                Console.WriteLine("[DB] DevTeam: وضعیت‌های workflow پیش‌فرض ثبت شد.");
            }

            if (!await db.DtProductModules.AnyAsync())
            {
                var modules = new (string Key, string Name, string Icon, string Color, int Sort)[]
                {
                    ("Core", "هسته / احراز هویت", "bi-shield-lock", "#0f172a", 10),
                    ("Catalog", "کاتالوگ و کالا", "bi-box-seam", "#0891b2", 20),
                    ("Sales", "فروش و سفارش", "bi-cart3", "#2563eb", 30),
                    ("Finance", "مالی و هزینه", "bi-cash-coin", "#059669", 40),
                    ("Accounting", "حسابداری", "bi-journal-text", "#0d9488", 50),
                    ("Treasury", "خزانه‌داری", "bi-bank", "#047857", 60),
                    ("Warehousing", "انبارداری", "bi-building", "#0284c7", 70),
                    ("Invoicing", "فاکتور", "bi-receipt", "#7c3aed", 80),
                    ("Hr", "منابع انسانی", "bi-people", "#db2777", 90),
                    ("HrMain", "منابع انسانی اصلی", "bi-person-badge", "#be185d", 100),
                    ("Office", "اتوماسیون اداری", "bi-envelope-paper", "#4f46e5", 110),
                    ("DocArchive", "آرشیو اسناد", "bi-folder2-open", "#6366f1", 120),
                    ("ItAssets", "دارایی‌های IT", "bi-pc-display", "#475569", 130),
                    ("WorkOrders", "دستور کار", "bi-card-checklist", "#8b5cf6", 140),
                    ("Projects", "مدیریت پروژه‌ها", "bi-kanban", "#ea580c", 150),
                    ("Cctv", "دوربین و NVR", "bi-camera-video", "#64748b", 160),
                    ("Chat", "پیام‌رسان", "bi-chat-dots", "#06b6d4", 170),
                    ("ReportStudio", "گزارش‌ساز", "bi-bar-chart-line", "#0ea5e9", 180),
                    ("Dashboards", "داشبوردها", "bi-speedometer2", "#14b8a6", 190),
                    ("Android", "اپ اندروید", "bi-phone", "#22c55e", 200),
                    ("Ocr", "سرویس OCR", "bi-ocr", "#a855f7", 210),
                    ("DevTeam", "میز کار توسعه", "bi-code-slash", "#f97316", 220),
                    ("Other", "سایر / زیرساخت", "bi-gear", "#78716c", 999),
                };
                db.DtProductModules.AddRange(modules.Select(m => new DtProductModule
                {
                    Key = m.Key,
                    NameFa = m.Name,
                    Icon = m.Icon,
                    Color = m.Color,
                    SortOrder = m.Sort,
                    IsActive = true
                }));
                await db.SaveChangesAsync();
                Console.WriteLine($"[DB] DevTeam: {modules.Length} ماژول محصول ثبت شد.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] DevTeam seed: {ex.Message}");
        }
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] DevTeamSchemaV1: {m}");
        }
    }
}
