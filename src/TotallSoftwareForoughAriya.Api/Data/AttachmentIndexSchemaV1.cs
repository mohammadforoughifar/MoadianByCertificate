using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای پیوست‌ها — نسخه ۱
//
//  مشکل: جدول AppAttachments هیچ ایندکسی روی (Module, RefId) نداشت، در حالی که
//  تقریباً همهٔ مسیرهای «نامه/مدرک» با همین دو ستون جست‌وجو می‌کنند:
//    • باز کردن نامه وارده/داخلی/صادره → فهرست پیوست‌های همان نامه
//    • «دارای پیوست» در فهرست کارتابل‌ها → EXISTS روی (Module, RefId) به‌ازای هر ردیف
//    • دانلود/پیش‌نمایش/بستهٔ ZIP پیوست‌ها
//
//  بدون ایندکس، هر بار «کل جدول» اسکن می‌شد و چون همین جدول ستون varbinary(max)
//  (پیوست‌های قدیمی که فایلشان داخل دیتابیس است) دارد، خواندن یک نامه یا حتی
//  نمایش فهرست، به‌طور محسوسی سنگین می‌شد. ایندکس زیر این جست‌وجوها را از
//  «Table Scan» به «Index Seek» تبدیل می‌کند و بایت‌های فایل هم اصلاً خوانده
//  نمی‌شوند (EXISTS فقط به ستون‌های کلید نیاز دارد).
//
//  اجرا idempotent است: اگر ایندکس موجود باشد، دستور دوباره کاری نمی‌کند.
// ============================================================
public static class AttachmentIndexSchemaV1
{
    /// <summary>نام ایندکس — در هر دو موتور یکسان تا لاگ/عیب‌یابی ساده باشد.</summary>
    private const string IndexName = "IX_AppAttachments_Module_RefId";

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.AppAttachments', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                    WHERE name = N'{IndexName}'
                      AND object_id = OBJECT_ID(N'dbo.AppAttachments'))
    CREATE INDEX [{IndexName}] ON [dbo].[AppAttachments] ([Module], [RefId]);");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, $@"
CREATE INDEX IF NOT EXISTS [{IndexName}] ON [AppAttachments] ([Module], [RefId]);");
    }

    /// <summary>اجرای امن — خطای «جدول/ایندکس تکراری» راه‌اندازی برنامه را متوقف نکند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("already exists", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] AttachmentIndexSchemaV1: {m}");
        }
    }
}
