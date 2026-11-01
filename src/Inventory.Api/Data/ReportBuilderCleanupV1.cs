using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  پاکسازی گزارش‌سازِ بازنشسته — نسخه ۱
//
//  گزارش‌ساز قدیمی حذف شده و قرار است از نو طراحی شود. این کلاس
//  جدول‌های باقی‌مانده را از دیتابیس‌های موجود برمی‌دارد تا اسکیما
//  با کد هم‌خوان بماند.
//
//  • UserReportRoleShares  (اول حذف می‌شود چون به UserReports کلید خارجی دارد)
//  • UserReports
//
//  عملیات idempotent است؛ اگر جدول‌ها نباشند بی‌صدا رد می‌شود.
//  پس از یک‌بار اجرا روی همهٔ محیط‌ها، این فایل قابل حذف است.
// ============================================================
public static class ReportBuilderCleanupV1
{
    public static async Task RunAsync(AppDbContext db)
    {
        var sqlite = db.Database.IsSqlite();

        // ترتیب مهم است: اول جدول وابسته، بعد جدول اصلی
        await SafeAsync(db, sqlite
            ? "DROP TABLE IF EXISTS UserReportRoleShares;"
            : "IF OBJECT_ID(N'dbo.UserReportRoleShares', N'U') IS NOT NULL DROP TABLE [dbo].[UserReportRoleShares];");

        await SafeAsync(db, sqlite
            ? "DROP TABLE IF EXISTS UserReports;"
            : "IF OBJECT_ID(N'dbo.UserReports', N'U') IS NOT NULL DROP TABLE [dbo].[UserReports];");

        // مجوزهای ماژول گزارش‌ساز هم دیگر معنایی ندارند
        await SafeAsync(db,
            "DELETE FROM RolePermissions WHERE PermissionId IN (SELECT Id FROM Permissions WHERE Module = 'ReportBuilder');");
        await SafeAsync(db, "DELETE FROM Permissions WHERE Module = 'ReportBuilder';");
    }

    /// <summary>خطای پاکسازی نباید راه‌اندازی برنامه را متوقف کند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] پاکسازی گزارش‌ساز — دستور نادیده گرفته شد: {ex.Message}");
        }
    }
}
