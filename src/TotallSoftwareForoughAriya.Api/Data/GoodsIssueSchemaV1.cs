using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «حواله تحویل کالا (بدون قیمت)» — نسخه ۱
//  • EnsureCreated (SQLite) و Migrate (SQL Server) جدول‌های جدید را به
//    دیتابیس‌های قدیمی اضافه نمی‌کنند؛ مثل WorkOrderSchemaV1 اینجا
//    جدول تازه را به‌صورت ایمن و idempotent می‌سازیم.
//  • GoodsIssues + GoodsIssueLines — سند «فقط مقدار» برای مشتری
//  • عمداً بدون FK به Parties/Products: حذف مشتری یا کالا نباید سابقه را بشکند
// ============================================================
public static class GoodsIssueSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.GoodsIssues', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[GoodsIssues](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Number] nvarchar(30) NOT NULL,
        [Date] datetime2 NOT NULL,
        [PartyId] int NOT NULL,
        [PartyName] nvarchar(200) NOT NULL,
        [Description] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE UNIQUE INDEX [IX_GoodsIssues_Number] ON [dbo].[GoodsIssues] ([Number]);
    CREATE INDEX [IX_GoodsIssues_PartyId] ON [dbo].[GoodsIssues] ([PartyId]);
    CREATE INDEX [IX_GoodsIssues_Date] ON [dbo].[GoodsIssues] ([Date]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.GoodsIssueLines', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[GoodsIssueLines](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [IssueId] int NOT NULL,
        [ProductId] int NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [Note] nvarchar(300) NULL
    );
    CREATE INDEX [IX_GoodsIssueLines_IssueId] ON [dbo].[GoodsIssueLines] ([IssueId]);
    CREATE INDEX [IX_GoodsIssueLines_ProductId] ON [dbo].[GoodsIssueLines] ([ProductId]);
    ALTER TABLE [dbo].[GoodsIssueLines] ADD CONSTRAINT [FK_GoodsIssueLines_GoodsIssues_IssueId]
        FOREIGN KEY ([IssueId]) REFERENCES [dbo].[GoodsIssues] ([Id]) ON DELETE CASCADE;
END");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        // EnsureCreated روی دیتابیس تازه، جدول را با ستون‌های درست می‌سازد؛
        // این خودتعمیر فقط برای دیتابیس‌های قدیمی است. پس اول وجودِ جدول را می‌سنجیم.
        var hasTable = false;
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='GoodsIssues';";
            hasTable = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { /* جدول نیست — EnsureCreated آن را ساخته است */ }

        if (hasTable) return;

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS GoodsIssues (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Number TEXT NOT NULL,
    Date TEXT NOT NULL,
    PartyId INTEGER NOT NULL,
    PartyName TEXT NOT NULL,
    Description TEXT NULL,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_GoodsIssues_Number ON GoodsIssues (Number);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_GoodsIssues_PartyId ON GoodsIssues (PartyId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_GoodsIssues_Date ON GoodsIssues (Date);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS GoodsIssueLines (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    IssueId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    ProductName TEXT NOT NULL,
    Quantity TEXT NOT NULL,
    Note TEXT NULL,
    FOREIGN KEY (IssueId) REFERENCES GoodsIssues (Id) ON DELETE CASCADE
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_GoodsIssueLines_IssueId ON GoodsIssueLines (IssueId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_GoodsIssueLines_ProductId ON GoodsIssueLines (ProductId);");
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] GoodsIssueSchemaV1: {m}");
        }
    }
}
