using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// =====================================================================
//  داشبورد شخصی کاربر
//    UserDashboard  ← UserDashWidget
//
//  هر کاربر می‌تواند چند داشبورد بسازد، ویجت‌ها را در یک گرید ۱۲ ستونی
//  بچیند و چیدمان را ذخیره کند. «پرمیشن» هر ویجت سمت سرور و در
//  WidgetDataService بررسی می‌شود، نه اینجا.
// =====================================================================

/// <summary>داشبورد ساختهٔ کاربر.</summary>
public class UserDashboard
{
    public int Id { get; set; }

    /// <summary>مالک داشبورد — همیشه کاربر جاری؛ هیچ‌کس داشبورد دیگری را نمی‌بیند.</summary>
    public int UserId { get; set; }

    [MaxLength(120)] public string Name { get; set; } = "داشبورد من";

    /// <summary>داشبورد پیش‌فرض کاربر (فقط یکی در هر کاربر).</summary>
    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    /// <summary>تعداد ستون گرید — فعلاً ثابت ۱۲ است ولی برای آینده نگه داشته می‌شود.</summary>
    public int GridColumns { get; set; } = 12;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<UserDashWidget> Widgets { get; set; } = new();
}

/// <summary>یک ویجت چیده‌شده در داشبورد.</summary>
public class UserDashWidget
{
    public int Id { get; set; }

    public int DashboardId { get; set; }
    public UserDashboard? Dashboard { get; set; }

    /// <summary>کلید ویجت در کاتالوگ (مثل "fac-sales-month").</summary>
    [MaxLength(60)] public string WidgetKey { get; set; } = "";

    /// <summary>عنوان سفارشی کاربر — خالی یعنی عنوان پیش‌فرض کاتالوگ.</summary>
    [MaxLength(160)] public string? Title { get; set; }

    // ---------- موقعیت در گرید ۱۲ ستونی ----------
    public int Row { get; set; }
    public int Col { get; set; }
    /// <summary>عرض بر حسب ستون گرید (۱ تا ۱۲)</summary>
    public int W { get; set; } = 3;
    /// <summary>ارتفاع بر حسب واحد ردیف</summary>
    public int H { get; set; } = 2;
    public int SortOrder { get; set; }

    /// <summary>نوع نمودار انتخابی کاربر (عددِ DashChartType) — null یعنی پیش‌فرض ویجت.</summary>
    public int? ChartType { get; set; }

    /// <summary>بازهٔ زمانی (عددِ DashRange).</summary>
    public int Range { get; set; } = 2; // Month

    /// <summary>پیکربندی آزاد به‌صورت JSON: {"limit":"5","warehouseId":"3"}</summary>
    public string? ConfigJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
