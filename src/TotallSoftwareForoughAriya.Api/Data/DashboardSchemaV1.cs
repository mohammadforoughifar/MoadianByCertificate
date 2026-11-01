using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «داشبورد شخصی کاربر» — نسخه ۱
//  • EnsureCreated (SQLite) و Migrate (SQL Server) جدول‌های تازه را به
//    دیتابیس‌های قدیمی اضافه نمی‌کنند؛ مثل WorkOrderSchemaV1 اینجا
//    جدول‌ها را به‌صورت ایمن و idempotent می‌سازیم.
//  • UserDashboards  : داشبوردهای ساختهٔ هر کاربر
//  • UserDashWidgets : ویجت‌های چیده‌شده در هر داشبورد (گرید ۱۲ ستونی)
// ============================================================
public static class DashboardSchemaV1
{
    private static int _done;
    private static int _running;

    /// <summary>آخرین خطای ساخت جدول (null یعنی موفق).</summary>
    public static string? LastError { get; private set; }

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    /// <summary>تضمین یک‌بار ساخت جدول‌ها در هر فرایند (با تلاش دوباره در صورت شکست قبلی).</summary>
    public static async Task EnsureOnceAsync(AppDbContext db)
    {
        if (Volatile.Read(ref _done) == 1 && LastError is null) return;
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            LastError = null;
            await EnsureAsync(db);
            if (LastError is null) Volatile.Write(ref _done, 1);
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.UserDashboards', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserDashboards](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [UserId] int NOT NULL,
        [Name] nvarchar(120) NOT NULL DEFAULT(N'داشبورد من'),
        [IsDefault] bit NOT NULL DEFAULT(0),
        [SortOrder] int NOT NULL DEFAULT(0),
        [GridColumns] int NOT NULL DEFAULT(12),
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        [UpdatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_UserDashboards_UserId] ON [dbo].[UserDashboards] ([UserId]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.UserDashWidgets', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserDashWidgets](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [DashboardId] int NOT NULL,
        [WidgetKey] nvarchar(60) NOT NULL,
        [Title] nvarchar(160) NULL,
        [Row] int NOT NULL DEFAULT(0),
        [Col] int NOT NULL DEFAULT(0),
        [W] int NOT NULL DEFAULT(3),
        [H] int NOT NULL DEFAULT(2),
        [SortOrder] int NOT NULL DEFAULT(0),
        [ChartType] int NULL,
        [Range] int NOT NULL DEFAULT(2),
        [ConfigJson] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        CONSTRAINT [FK_UserDashWidgets_UserDashboards] FOREIGN KEY ([DashboardId])
            REFERENCES [dbo].[UserDashboards] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_UserDashWidgets_DashboardId] ON [dbo].[UserDashWidgets] ([DashboardId]);
END");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS UserDashboards (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    Name TEXT NOT NULL DEFAULT 'داشبورد من',
    IsDefault INTEGER NOT NULL DEFAULT 0,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    GridColumns INTEGER NOT NULL DEFAULT 12,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);");
        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS IX_UserDashboards_UserId ON UserDashboards (UserId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS UserDashWidgets (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    DashboardId INTEGER NOT NULL,
    WidgetKey TEXT NOT NULL,
    Title TEXT NULL,
    Row INTEGER NOT NULL DEFAULT 0,
    Col INTEGER NOT NULL DEFAULT 0,
    W INTEGER NOT NULL DEFAULT 3,
    H INTEGER NOT NULL DEFAULT 2,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    ChartType INTEGER NULL,
    Range INTEGER NOT NULL DEFAULT 2,
    ConfigJson TEXT NULL,
    CreatedAt TEXT NOT NULL,
    CONSTRAINT FK_UserDashWidgets_UserDashboards FOREIGN KEY (DashboardId)
        REFERENCES UserDashboards (Id) ON DELETE CASCADE
);");
        await SafeAsync(db, @"
CREATE INDEX IF NOT EXISTS IX_UserDashWidgets_DashboardId ON UserDashWidgets (DashboardId);");
    }

    /// <summary>اجرای امن — خطای «شیء تکراری/موجود» راه‌اندازی برنامه را متوقف نکند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("already exists", StringComparison.OrdinalIgnoreCase)) return;

            LastError = m;
            Console.WriteLine();
            Console.WriteLine("================= خطای ساخت جداول داشبورد شخصی =================");
            Console.WriteLine($"[DB] DashboardSchemaV1: {m}");
            Console.WriteLine("جدول‌های UserDashboards / UserDashWidgets ساخته نشدند؛");
            Console.WriteLine("صفحهٔ «داشبورد من» کار نخواهد کرد. اسکریپت آماده در مسیر زیر را با یک کاربر دارای");
            Console.WriteLine("مجوز DDL در SSMS اجرا کنید:  inventory/sql/UserDashboards.sql");
            Console.WriteLine("==================================================================");
            Console.WriteLine();
        }
    }
}
