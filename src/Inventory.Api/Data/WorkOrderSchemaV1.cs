using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای دستور کار — نسخه ۱ (اولویت + یادآور مهلت)
//  • EnsureCreated (SQLite) و Migrate (SQL Server) ساختارهای جدید را به
//    دیتابیس‌های قدیمی اضافه نمی‌کنند؛ مثل OfficeEmailSchemaV1 اینجا
//    ستون/جدول تازه را به‌صورت ایمن و idempotent می‌سازیم.
//  • ستون WorkOrders.Priority: اولویت (0=کم 1=عادی 2=بالا 3=فوری)
//  • جدول WorkOrderReminderLogs: ثبت یادآورهای ارسال‌شده مهلت
//    (هر آستانه به‌ازای هر مهلت فقط یک‌بار — بعد از تمدید دوباره فعال می‌شود)
// ============================================================
public static class WorkOrderSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'Priority') IS NULL
    ALTER TABLE dbo.WorkOrders ADD Priority int NOT NULL DEFAULT(1);");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrderReminderLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkOrderReminderLogs](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [OrderId] int NOT NULL,
        [ThresholdHours] int NOT NULL,
        [DueAtSnapshot] datetime2 NOT NULL,
        [SentAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE UNIQUE INDEX [IX_WorkOrderReminderLogs_OrderId_ThresholdHours_DueAtSnapshot]
        ON [dbo].[WorkOrderReminderLogs] ([OrderId], [ThresholdHours], [DueAtSnapshot]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkOrders_Status_DueAt' AND object_id = OBJECT_ID(N'dbo.WorkOrders'))
    CREATE INDEX [IX_WorkOrders_Status_DueAt] ON [dbo].[WorkOrders] ([Status], [DueAt]);");

        // ---------- موج ۲: تکرارشونده + چک‌لیست زیرکار ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'Recurrence') IS NULL
    ALTER TABLE dbo.WorkOrders ADD Recurrence int NOT NULL DEFAULT(0);
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'RecurrenceParentId') IS NULL
    ALTER TABLE dbo.WorkOrders ADD RecurrenceParentId int NULL;
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'SourceRefId') IS NULL
    ALTER TABLE dbo.WorkOrders ADD SourceRefId int NULL;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrderChecklistItems', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkOrderChecklistItems](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [OrderId] int NOT NULL,
        [Text] nvarchar(300) NOT NULL,
        [SortOrder] int NOT NULL DEFAULT(0),
        [IsDone] bit NOT NULL DEFAULT(0),
        [DoneByUserId] int NULL,
        [DoneByName] nvarchar(150) NULL,
        [DoneAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_WorkOrderChecklistItems_OrderId] ON [dbo].[WorkOrderChecklistItems] ([OrderId]);
END");

        // ---------- موج ۳: رشتهٔ گفتگو (کامنت) داخل دستور ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrderComments', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkOrderComments](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [OrderId] int NOT NULL,
        [AuthorUserId] int NOT NULL,
        [AuthorName] nvarchar(150) NOT NULL,
        [Text] nvarchar(2000) NOT NULL,
        [ReplyToId] int NULL,
        [IsDeleted] bit NOT NULL DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        [EditedAt] datetime2 NULL
    );
    CREATE INDEX [IX_WorkOrderComments_OrderId] ON [dbo].[WorkOrderComments] ([OrderId]);
END");

        // ---------- موج ۴: برچسب/دسته‌بندی ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'Tags') IS NULL
    ALTER TABLE dbo.WorkOrders ADD Tags nvarchar(300) NULL;");

        // ---------- موج ۵: ارجاع زنجیره‌ای (زیر-دستور) ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'ParentOrderId') IS NULL
    ALTER TABLE dbo.WorkOrders ADD ParentOrderId int NULL;");

        // ---------- موج ۷: قالب‌های آمادهٔ دستور کار ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.WorkOrderTemplates', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkOrderTemplates](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [OwnerUserId] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(max) NOT NULL DEFAULT(N''),
        [Priority] int NOT NULL DEFAULT(1),
        [Recurrence] int NOT NULL DEFAULT(0),
        [AssigneeUserIds] nvarchar(500) NULL,
        [ChecklistItems] nvarchar(4000) NULL,
        [Tags] nvarchar(300) NULL,
        [UsageCount] int NOT NULL DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_WorkOrderTemplates_OwnerUserId] ON [dbo].[WorkOrderTemplates] ([OwnerUserId]);
END");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        // ستون Priority — فقط اگر جدول هست و ستون نیست
        var hasPriority = false;
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('WorkOrders') WHERE name='Priority'";
            hasPriority = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { /* جدول هنوز ساخته نشده — EnsureCreated آن را با ستون جدید می‌سازد */ }

        if (!hasPriority)
            await SafeAsync(db, "ALTER TABLE WorkOrders ADD COLUMN Priority INTEGER NOT NULL DEFAULT 1;");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS WorkOrderReminderLogs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OrderId INTEGER NOT NULL,
    ThresholdHours INTEGER NOT NULL,
    DueAtSnapshot TEXT NOT NULL,
    SentAt TEXT NOT NULL
);");
        await SafeAsync(db, @"
CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkOrderReminderLogs_OrderId_ThresholdHours_DueAtSnapshot
    ON WorkOrderReminderLogs (OrderId, ThresholdHours, DueAtSnapshot);");
        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS IX_WorkOrders_Status_DueAt ON WorkOrders (Status, DueAt);");

        // ---------- موج ۲: تکرارشونده + چک‌لیست زیرکار ----------
        foreach (var (col, ddl) in new[]
        {
            ("Recurrence", "ALTER TABLE WorkOrders ADD COLUMN Recurrence INTEGER NOT NULL DEFAULT 0;"),
            ("RecurrenceParentId", "ALTER TABLE WorkOrders ADD COLUMN RecurrenceParentId INTEGER NULL;"),
        })
        {
            var has = false;
            try
            {
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('WorkOrders') WHERE name='{col}'";
                has = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
            }
            catch { }
            if (!has) await SafeAsync(db, ddl);
        }

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS WorkOrderChecklistItems (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OrderId INTEGER NOT NULL,
    Text TEXT NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsDone INTEGER NOT NULL DEFAULT 0,
    DoneByUserId INTEGER NULL,
    DoneByName TEXT NULL,
    DoneAt TEXT NULL,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS IX_WorkOrderChecklistItems_OrderId ON WorkOrderChecklistItems (OrderId);");

        // ---------- موج ۳: رشتهٔ گفتگو (کامنت) داخل دستور ----------
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS WorkOrderComments (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OrderId INTEGER NOT NULL,
    AuthorUserId INTEGER NOT NULL,
    AuthorName TEXT NOT NULL,
    Text TEXT NOT NULL,
    ReplyToId INTEGER NULL,
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    EditedAt TEXT NULL
);");
        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS IX_WorkOrderComments_OrderId ON WorkOrderComments (OrderId);");

        // ---------- موج ۴: برچسب/دسته‌بندی ----------
        var hasTags = false;
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('WorkOrders') WHERE name='Tags'";
            hasTags = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        if (!hasTags) await SafeAsync(db, "ALTER TABLE WorkOrders ADD COLUMN Tags TEXT NULL;");

        // ---------- موج ۵: ارجاع زنجیره‌ای (زیر-دستور) ----------
        var hasParent = false;
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('WorkOrders') WHERE name='ParentOrderId'";
            hasParent = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        if (!hasParent) await SafeAsync(db, "ALTER TABLE WorkOrders ADD COLUMN ParentOrderId INTEGER NULL;");

        // ---------- موج ۸: شناسهٔ والد مبدأ (لینک بند صورتجلسه به صورتجلسه) ----------
        var hasSourceRef = false;
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('WorkOrders') WHERE name='SourceRefId'";
            hasSourceRef = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { }
        if (!hasSourceRef) await SafeAsync(db, "ALTER TABLE WorkOrders ADD COLUMN SourceRefId INTEGER NULL;");

        // ---------- موج ۷: قالب‌های آمادهٔ دستور کار ----------
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS WorkOrderTemplates (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OwnerUserId INTEGER NOT NULL,
    Name TEXT NOT NULL,
    Title TEXT NOT NULL,
    Description TEXT NOT NULL DEFAULT '',
    Priority INTEGER NOT NULL DEFAULT 1,
    Recurrence INTEGER NOT NULL DEFAULT 0,
    AssigneeUserIds TEXT NULL,
    ChecklistItems TEXT NULL,
    Tags TEXT NULL,
    UsageCount INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS IX_WorkOrderTemplates_OwnerUserId ON WorkOrderTemplates (OwnerUserId);");
    }

    /// <summary>اجرای امن — خطای «شیء تکراری/موجود» راه‌اندازی برنامه را متوقف نکند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            // تکراری‌ها بی‌صدا؛ سایر خطاها فقط لاگ (مثل الگوی OfficeEmailSchemaV1)
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] WorkOrderSchemaV1: {m}");
        }
    }
}
