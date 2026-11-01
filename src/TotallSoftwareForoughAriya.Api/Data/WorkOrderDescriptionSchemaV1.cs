using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  شرحِ دستور کارِ ساخته‌شده از «نامهٔ دارای تصویر» — نسخه ۱
//
//  مشکل: وقتی از یک نامهٔ وارده (ساخته‌شده از ایمیل) دستور کار ساخته می‌شود، شرح
//  دستور کار از متن نامه کپی می‌شود. تصویرهای inline ایمیل به‌صورت data URI داخل
//  همان متن هستند، یعنی شرح به‌راحتی از چند صد کیلوبایت تا چند مگابایت می‌شود.
//  ستون WorkOrders.Description از نوع متن محدود است و برای همین حجم ساخته نشده:
//  ذخیره یا با خطای طول شکست می‌خورد یا ردیف را سنگین می‌کند.
//
//  راه‌حل بدون تغییر در مایگریشن‌ها و بدون دست‌زدن به ساختار جدول موجود:
//  یک جدول جانبی (Extension Table) که فقط برای شرح‌های «بلند» پر می‌شود.
//    • شرح کوتاه → همان ستون قبلی WorkOrders.Description مثل امروز
//    • شرح بلند  → در WorkOrderDescriptions و ستون قبلی به خلاصهٔ کوتاه (پیش‌نمایش)
//                  تبدیل می‌شود تا فهرست‌ها و جست‌وجوی قبلی دقیقاً مثل قبل کار کند.
//
//  چرا جدول جدا و نه ستون تازه روی WorkOrders: فهرست دستور کارها با همان ردیف
//  سبک کار می‌کند؛ اگر ستون سنگین به همان جدول اضافه شود، هر SELECT فهرست هم
//  آن بایت‌ها را جابه‌جا می‌کند. جدول جدا همچنین این قابلیت را می‌دهد که جدا
//  از مایگریشن‌های موجود، روی هر دو موتور (SQL Server و SQLite) خودکار ساخته شود.
//
//  اجرا idempotent است: اگر جدول موجود باشد کاری نمی‌کند.
// ============================================================
public static class WorkOrderDescriptionSchemaV1
{
    /// <summary>سقف مجاز شرح در ستون قدیمی (پیش‌نمایش کوتاه) — محافظه‌کارانه و امن.</summary>
    public const int PreviewMaxLength = 2000;

    /// <summary>سقف مطلق شرح کامل (پس از فشرده‌سازی PNG).</summary>
    public const int FullMaxLength = 4_000_000;

    /// <summary>نام مرجع در AppAttachments برای فایل‌هایی که در شرح کپی شده‌اند.</summary>
    public const string EmbeddedModule = "WorkOrderDescription";

    public static async Task EnsureAsync(AppDbContext db)
    {
        // خطای ساخت این جدول نباید راه‌اندازی برنامه را متوقف کند؛ کدِ ذخیره/خواندن
        // (WorkOrderDescriptionStore) هم بدون این جدول «افت محترمانه» دارد.
        try
        {
            if (db.Database.IsSqlite())
            {
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS WorkOrderDescriptions (
    OrderId INTEGER NOT NULL CONSTRAINT PK_WorkOrderDescriptions PRIMARY KEY,
    Content TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);");
                return;
            }

            await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.WorkOrderDescriptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkOrderDescriptions (
        OrderId int NOT NULL CONSTRAINT PK_WorkOrderDescriptions PRIMARY KEY,
        Content nvarchar(max) NOT NULL,
        UpdatedAt datetime2 NOT NULL,
        CONSTRAINT FK_WorkOrderDescriptions_WorkOrders_OrderId
            FOREIGN KEY (OrderId) REFERENCES dbo.WorkOrders(Id) ON DELETE CASCADE
    );
END;");
        }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("already exists", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] WorkOrderDescriptionSchemaV1: {m}");
        }
    }
}
