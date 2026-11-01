using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای تنظیمات برنامه — نسخه ۱
//  • Migrate (SQL Server) و EnsureCreated (SQLite) ستون‌های تازه را به
//    دیتابیس‌های قدیمی اضافه نمی‌کنند؛ مثل WorkOrderSchemaV1 اینجا
//    ستون‌های جدید را به‌صورت ایمن و idempotent می‌سازیم.
//  • AppSettings.NotifyBannerMs: مدت نمایش اعلان زندهٔ بالای صفحه (ms) — پیش‌فرض ۲ دقیقه
//  • AppSettings.NotifyToastMs:  مدت نمایش پیام کوتاه/توست (ms) — پیش‌فرض ۲۰ ثانیه
//    (مدیر سامانه این‌ها را از صفحهٔ «تنظیمات» تعیین می‌کند؛ سراسری برای همهٔ کاربران)
// ============================================================
public static class AppSettingsSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.AppSettings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AppSettings', N'NotifyBannerMs') IS NULL
    ALTER TABLE dbo.AppSettings ADD NotifyBannerMs int NOT NULL DEFAULT(120000);");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.AppSettings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AppSettings', N'NotifyToastMs') IS NULL
    ALTER TABLE dbo.AppSettings ADD NotifyToastMs int NOT NULL DEFAULT(20000);");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        if (!await ColumnExistsAsync(db, "AppSettings", "NotifyBannerMs"))
            await SafeAsync(db, "ALTER TABLE AppSettings ADD COLUMN NotifyBannerMs INTEGER NOT NULL DEFAULT 120000;");

        if (!await ColumnExistsAsync(db, "AppSettings", "NotifyToastMs"))
            await SafeAsync(db, "ALTER TABLE AppSettings ADD COLUMN NotifyToastMs INTEGER NOT NULL DEFAULT 20000;");
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { return false; /* جدول هنوز ساخته نشده — EnsureCreated آن را با ستون جدید می‌سازد */ }
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] AppSettingsSchemaV1: {m}");
        }
    }
}
