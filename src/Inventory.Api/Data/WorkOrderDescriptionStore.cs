using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  ذخیره/خواندن شرحِ بلند دستور کار
//
//  چرا: شرحی که از نامهٔ دارای تصویر ساخته می‌شود می‌تواند چند صد کیلوبایت
//  (data URI تصاویر) باشد؛ ستون WorkOrders.Description برای این حجم ساخته نشده
//  و فهرست‌ها هم نباید این بایت‌ها را جابه‌جا کنند.
//
//  رفتار:
//    • شرح کوتاه  → همان ستون قبلی، بدون تغییر (سازگاری کامل با دادهٔ فعلی)
//    • شرح بلند   → متن کامل در جدول WorkOrderDescriptions و ستون قبلی یک
//                   «پیش‌نمایش کوتاه و خوانا» (بدون data URI) برای فهرست/جست‌وجو
//    • Client علامت HasFullDescription را در فهرست می‌گیرد و متن کامل را فقط
//      هنگام باز کردن جزئیات با یک درخواست جدا می‌خواند.
//
//  جدول جانبی در WorkOrderDescriptionSchemaV1 ساخته می‌شود (بدون تغییر مایگریشن‌ها).
//  همهٔ دسترسی‌ها با ExecuteSqlRawAsync/SqlQueryRaw انجام می‌شود تا خودکار در
//  همان تراکنشِ EF و با همان اتصال اجرا شوند.
// ============================================================
public static class WorkOrderDescriptionStore
{
    /// <summary>تصاویر data URI — همان چیزی که شرح را سنگین می‌کند.</summary>
    private static readonly Regex DataUriImg = new(
        @"<img\b[^>]*?src\s*=\s*[""']data:[^""']*[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex DataUriAny = new(
        @"src\s*=\s*[""']data:[^""']*[""']",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>آیا شرح باید در جدول جانبی ذخیره شود؟ (یعنی از سقف پیش‌نمایش بلندتر است)</summary>
    public static bool NeedsSideTable(string? full) =>
        !string.IsNullOrEmpty(full) && full!.Length > WorkOrderDescriptionSchemaV1.PreviewMaxLength;

    /// <summary>
    /// پیش‌نمایش کوتاه و امن برای ستون WorkOrders.Description: تصاویر data URI با یک
    /// جای‌نگهدار کوتاه عوض می‌شوند و اگر باز هم بلند بود، متن بریده می‌شود.
    /// قالب‌بندی متنی (پاراگراف، لیست، بولد) حفظ می‌شود تا فهرست و جست‌وجو مثل قبل کار کند.
    /// </summary>
    public static string Preview(string? full)
    {
        if (string.IsNullOrEmpty(full)) return "";
        var s = full!;
        if (s.Length <= WorkOrderDescriptionSchemaV1.PreviewMaxLength) return s;

        s = DataUriImg.Replace(s, "<span class=\"wo-desc-img-note\">[تصویر نامه]</span>");
        s = DataUriAny.Replace(s, "src=\"\"");
        if (s.Length <= WorkOrderDescriptionSchemaV1.PreviewMaxLength) return s;

        var cap = WorkOrderDescriptionSchemaV1.PreviewMaxLength - 60;
        var cut = s.LastIndexOf('<', Math.Max(0, cap));
        if (cut < 200) cut = cap;
        return s[..cut] + "<p class=\"fs-9 text-muted\">… (ادامهٔ شرح در جزئیات دستور کار)</p>";
    }

    /// <summary>ذخیرهٔ متن کامل. مقدار کوتاه یا خالی ⇒ ردیف جانبی حذف می‌شود.</summary>
    public static async Task SaveAsync(AppDbContext db, int orderId, string? full)
    {
        if (orderId <= 0) return;
        try
        {
            if (!NeedsSideTable(full))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM WorkOrderDescriptions WHERE OrderId = {0}", orderId);
                return;
            }

            var content = full!.Length > WorkOrderDescriptionSchemaV1.FullMaxLength
                ? full[..WorkOrderDescriptionSchemaV1.FullMaxLength]
                : full;

            var updated = await db.Database.ExecuteSqlRawAsync(
                "UPDATE WorkOrderDescriptions SET Content = {0}, UpdatedAt = {1} WHERE OrderId = {2}",
                content, DateTime.UtcNow, orderId);
            if (updated > 0) return;

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO WorkOrderDescriptions (OrderId, Content, UpdatedAt) VALUES ({0}, {1}, {2})",
                orderId, content, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            // اگر جدول جانبی ساخته نشده باشد، نباید ثبت/ویرایش دستور کار ۵۰۰ بدهد؛
            // پیش‌نمایش کوتاه (بدون تصویر) همان‌طور در ستون اصلی محفوظ است.
            Console.WriteLine($"[WO] ذخیرهٔ شرح کامل دستور کار {orderId} انجام نشد: {ex.Message}");
        }
    }

    public static async Task DeleteAsync(AppDbContext db, int orderId)
    {
        if (orderId <= 0) return;
        try
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM WorkOrderDescriptions WHERE OrderId = {0}", orderId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WO] حذف شرح کامل دستور کار {orderId} انجام نشد: {ex.Message}");
        }
    }

    /// <summary>متن کامل یک دستور کار (اگر ردیف جانبی نداشته باشد null).</summary>
    public static async Task<string?> GetAsync(AppDbContext db, int orderId)
    {
        if (orderId <= 0) return null;
        try
        {
            var rows = await db.Database
                .SqlQueryRaw<string>("SELECT Content AS Value FROM WorkOrderDescriptions WHERE OrderId = {0}", orderId)
                .ToListAsync();
            return rows.Count > 0 ? rows[0] : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WO] خواندن شرح کامل دستور کار {orderId} انجام نشد: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// از بین شناسه‌های یک صفحه، آن‌هایی که شرح کامل دارند — با یک کوئری سبک روی کلید
    /// اصلی (بدون خواندن ستون Content). برای فهرست‌ها استفاده می‌شود.
    /// </summary>
    public static async Task<HashSet<int>> LongIdsAsync(AppDbContext db, IReadOnlyCollection<int> orderIds)
    {
        var set = new HashSet<int>();
        if (orderIds.Count == 0) return set;

        try
        {
            var ids = orderIds.Distinct().ToArray();
            // پارامترها دسته‌بندی می‌شوند: سقف پارامتر SQL Server (۲۱۰۰) و SQLite (۹۹۹)
            // نباید با فهرست‌های بزرگ (خروجی/تقویم/زیرمجموعهٔ پروژه) شکسته شود.
            foreach (var chunk in ids.Chunk(500))
            {
                var args = chunk.Cast<object>().ToArray();
                var placeholders = string.Join(",", chunk.Select((_, i) => "{" + i + "}"));

                var rows = await db.Database
                    .SqlQueryRaw<int>($"SELECT OrderId AS Value FROM WorkOrderDescriptions WHERE OrderId IN ({placeholders})", args)
                    .ToListAsync();

                foreach (var id in rows) set.Add(id);
            }
        }
        catch (Exception ex)
        {
            // فهرست دستور کارها نباید به‌خاطر این جدول جانبی از کار بیفتد.
            Console.WriteLine($"[WO] خواندن فهرست شرح‌های بلند انجام نشد: {ex.Message}");
        }
        return set;
    }
}
