using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای دبیرخانه نامه صادره — نسخه ۱
//  • EnsureCreated (SQLite) و Migrate (SQL Server) ستون‌های جدید را به
//    دیتابیس‌های قدیمی اضافه نمی‌کنند؛ مثل WorkOrderSchemaV1 اینجا
//    ستون‌های تازه را به‌صورت ایمن و idempotent می‌سازیم.
//
//  ستون‌های جدیدِ OutgoingLetters:
//   • DelivererName    — نام تحویل‌گیرنده (پست/پست پیشتاز/پیک/تحویل حضوری)
//   • TrackingCode     — کد رهگیری مرسوله پستی
//   • DestFax          — شماره فکس مقصد
//   • IsArchived       — نامه در بایگانی دبیرخانه ثبت شده است؟
//   • ArchivedAt       — زمان بایگانی
//   • ArchivedByUserId — کاربر دبیرخانه‌ای که بایگانی کرده
// ============================================================
public static class DabirkhaneSchemaV1
{
    /// <summary>ستون‌های افزوده‌شده به جدول نامه‌های صادره (نام ستون، تعریف SQL Server، تعریف SQLite)</summary>
    private static readonly (string Name, string SqlServer, string Sqlite)[] Columns =
    {
        ("DelivererName",    "nvarchar(250) NULL", "TEXT NULL"),
        ("TrackingCode",     "nvarchar(100) NULL", "TEXT NULL"),
        ("DestFax",          "nvarchar(50) NULL",  "TEXT NULL"),
        ("IsArchived",       "bit NOT NULL DEFAULT(0)", "INTEGER NOT NULL DEFAULT 0"),
        ("ArchivedAt",       "datetime2 NULL",     "TEXT NULL"),
        ("ArchivedByUserId", "int NULL",           "INTEGER NULL"),
        ("CreatorUserId",    "int NOT NULL DEFAULT(0)", "INTEGER NOT NULL DEFAULT 0"),
        ("CreatorId",        "int NOT NULL DEFAULT(0)", "INTEGER NOT NULL DEFAULT 0"),
    };

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        foreach (var (name, ddl, _) in Columns)
        {
            await SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.OutgoingLetters', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.OutgoingLetters', N'{name}') IS NULL
    ALTER TABLE dbo.OutgoingLetters ADD {name} {ddl};");
        }

        // همگام‌سازی شناسه سازنده بین اسکیمای قدیمی (CreatorId) و مدل جدید
        // (CreatorUserId). از این پس هنگام درج نیز هر دو ستون مقدار می‌گیرند.
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.OutgoingLetters', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.OutgoingLetters', N'CreatorId') IS NOT NULL
   AND COL_LENGTH(N'dbo.OutgoingLetters', N'CreatorUserId') IS NOT NULL
BEGIN
    UPDATE dbo.OutgoingLetters
       SET CreatorUserId = CreatorId
     WHERE ISNULL(CreatorUserId, 0) = 0 AND ISNULL(CreatorId, 0) > 0;

    UPDATE dbo.OutgoingLetters
       SET CreatorId = CreatorUserId
     WHERE ISNULL(CreatorId, 0) = 0 AND ISNULL(CreatorUserId, 0) > 0;
END");

        // نمایه برای فیلتر سریع «بایگانی‌شده / بایگانی‌نشده» در دبیرخانه
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.OutgoingLetters', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OutgoingLetters_IsArchived' AND object_id = OBJECT_ID(N'dbo.OutgoingLetters'))
    CREATE INDEX [IX_OutgoingLetters_IsArchived] ON [dbo].[OutgoingLetters] ([IsArchived]);");

        // نمایه برای جستجوی سریع بر اساس روش ارسال
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.OutgoingLetters', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.OutgoingLetters', N'SendMethod') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OutgoingLetters_SendMethod' AND object_id = OBJECT_ID(N'dbo.OutgoingLetters'))
    CREATE INDEX [IX_OutgoingLetters_SendMethod] ON [dbo].[OutgoingLetters] ([SendMethod]);");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        foreach (var (name, _, ddl) in Columns)
        {
            if (await SqliteColumnExistsAsync(db, "OutgoingLetters", name)) continue;
            await SafeAsync(db, $"ALTER TABLE OutgoingLetters ADD COLUMN {name} {ddl};");
        }

        await SafeAsync(db, @"
UPDATE OutgoingLetters
   SET CreatorUserId = CreatorId
 WHERE IFNULL(CreatorUserId, 0) = 0 AND IFNULL(CreatorId, 0) > 0;");
        await SafeAsync(db, @"
UPDATE OutgoingLetters
   SET CreatorId = CreatorUserId
 WHERE IFNULL(CreatorId, 0) = 0 AND IFNULL(CreatorUserId, 0) > 0;");

        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_OutgoingLetters_IsArchived ON OutgoingLetters (IsArchived);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_OutgoingLetters_SendMethod ON OutgoingLetters (SendMethod);");
    }

    /// <summary>بررسی وجود ستون در SQLite با pragma_table_info</summary>
    private static async Task<bool> SqliteColumnExistsAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch
        {
            // جدول هنوز ساخته نشده — EnsureCreated آن را با ستون‌های جدید می‌سازد
            return true;
        }
    }

    /// <summary>اجرای امن — خطای «ستون/شیء تکراری» راه‌اندازی برنامه را متوقف نکند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] DabirkhaneSchemaV1: {m}");
        }
    }
}
