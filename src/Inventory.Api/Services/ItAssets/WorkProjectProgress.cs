namespace Inventory.Api.Services.ItAssets;

/// <summary>
/// موتور محاسبهٔ پیشرفت پروژه کاری — خالص و بدون وابستگی به دیتابیس (قابل تست).
/// <para>پیشرفت دستور: بسته=۱۰۰٪، وگرنه درصد تیک‌های چک‌لیست، وگرنه ۰٪.</para>
/// <para>پیشرفت پروژه: میانگین وزنی دستورها (وزن صفر = مستثنی).</para>
/// </summary>
public static class WorkProjectProgress
{
    /// <summary>پیشرفت یک دستور کار (۰ تا ۱۰۰).</summary>
    public static int OrderProgress(bool isClosed, int checklistTotal, int checklistDone)
    {
        if (isClosed) return 100;
        if (checklistTotal <= 0) return 0;
        if (checklistDone <= 0) return 0;
        if (checklistDone >= checklistTotal) return 100;
        return checklistDone * 100 / checklistTotal;
    }

    /// <summary>
    /// پیشرفت پروژه (۰ تا ۱۰۰)؛ null یعنی هیچ دستور واجدی ندارد (نمایش «بدون دستور»).
    /// دستورهای با وزن ≤ ۰ در محاسبه حساب نمی‌شوند.
    /// </summary>
    public static int? ProjectProgress(IEnumerable<(int Weight, int Progress)> orders)
    {
        var items = orders.Where(o => o.Weight > 0).ToList();
        if (items.Count == 0) return null;
        var totalWeight = items.Sum(o => o.Weight);
        if (totalWeight <= 0) return null;
        return (int)Math.Round(items.Sum(o => (double)o.Weight * o.Progress) / totalWeight);
    }
}
