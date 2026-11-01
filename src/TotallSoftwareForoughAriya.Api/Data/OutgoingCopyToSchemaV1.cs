using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای رونوشت نامه صادره — نسخه ۱
//  • EnsureCreated (SQLite) جدول را برای دیتابیس‌های تازه می‌سازد،
//    اما روی دیتابیس‌های موجود کاری نمی‌کند.
//  • Migrate (SQL Server) هم اگر Migration فعالی برای این جدول
//    ثبت نشده باشد، آن را نمی‌سازد.
//  • لذا مانند سایر اسکیماهای پروژه، ساختِ idempotent را اینجا انجام
//    می‌دهیم تا برنامه روی هر دیتابیس قدیمی/جدیدی بالا بیاید.
// ============================================================
public static class OutgoingCopyToSchemaV1
{
    private const string Table = "OutgoingLetterCopyToes";

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.{Table}', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[{Table}] (
        [Id]                int IDENTITY(1,1) NOT NULL,
        [OutgoingLetterId]  int NOT NULL,
        [RowNo]             int NOT NULL CONSTRAINT DF_{Table}_RowNo DEFAULT 1,
        [Name]              nvarchar(300) NOT NULL,
        [Desc]              nvarchar(300) NULL,
        [RefNo]             nvarchar(100) NULL,
        [CreatorUserId]     int NOT NULL,
        [CreatedAt]         datetime2 NOT NULL,
        [IsDelete]          bit NOT NULL CONSTRAINT DF_{Table}_IsDelete DEFAULT 0,
        CONSTRAINT [PK_{Table}] PRIMARY KEY ([Id])
    );
END");

        await SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.{Table}', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_{Table}_Letter' AND object_id = OBJECT_ID(N'dbo.{Table}'))
    CREATE INDEX [IX_{Table}_Letter] ON [dbo].[{Table}] ([OutgoingLetterId], [IsDelete]);");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, $@"
CREATE TABLE IF NOT EXISTS {Table} (
    Id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    OutgoingLetterId INTEGER NOT NULL,
    RowNo            INTEGER NOT NULL DEFAULT 1,
    Name             TEXT NOT NULL,
    Desc             TEXT NULL,
    RefNo            TEXT NULL,
    CreatorUserId    INTEGER NOT NULL,
    CreatedAt        TEXT NOT NULL,
    IsDelete         INTEGER NOT NULL DEFAULT 0
);");

        await SafeAsync(db, $"CREATE INDEX IF NOT EXISTS IX_{Table}_Letter ON {Table} (OutgoingLetterId, IsDelete);");
    }

    /// <summary>اجرای امن — خطای «شیء تکراری» راه‌اندازی برنامه را متوقف نکند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] OutgoingCopyToSchemaV1: {m}");
        }
    }
}
