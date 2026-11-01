namespace Inventory.Shared.Dtos;

// =====================================================================
//  داشبورد شخصی کاربر — مدل‌های مشترک کلاینت/سرور
//
//  • WidgetDefDto   : یک ردیف از «کاتالوگ ویجت‌ها» (سرور می‌سازد، بر اساس مجوز فیلتر می‌شود)
//  • UserDashboardDto: داشبورد ساختهٔ کاربر + چیدمان ویجت‌هایش
//  • WidgetDataDto  : خروجی دادهٔ یک ویجت (سرور محاسبه می‌کند)
//
//  نکتهٔ امنیتی: چیدمان کاربر فقط UI است؛ هر ویجت در سرور با
//  ForbiddenUnlessAsync(Module, "Read") کنترل می‌شود.
// =====================================================================

/// <summary>نوع ویجت — تعیین می‌کند رندرر کلاینت داده را چطور نشان دهد.</summary>
public enum DashWidgetKind
{
    /// <summary>یک عدد بزرگ + واحد + روند (مثل «فروش امروز»)</summary>
    Kpi = 0,
    /// <summary>نمودار (میله‌ای / خطی / دایره‌ای)</summary>
    Chart = 1,
    /// <summary>جدول با ستون و ردیف</summary>
    Table = 2,
    /// <summary>فهرست کوتاه همراه با لینک</summary>
    List = 3
}

/// <summary>نوع نمودار — مستقیماً به Chart.js نگاشت می‌شود.</summary>
public enum DashChartType { Bar = 0, Line = 1, Pie = 2, Doughnut = 3 }

/// <summary>بازهٔ زمانی قابل انتخاب روی ویجت.</summary>
public enum DashRange { Today = 0, Week = 1, Month = 2, Quarter = 3, Year = 4, All = 5 }

/// <summary>یک گزینهٔ پیکربندی ویجت (در پنل تنظیمات ویجت نمایش داده می‌شود).</summary>
public class WidgetOptionDefDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>text | number | select | bool | range</summary>
    public string Kind { get; set; } = "text";
    /// <summary>گزینه‌های انتخابی وقتی Kind = select یا range باشد.</summary>
    public List<string> Choices { get; set; } = new();
    public string Default { get; set; } = "";
}

/// <summary>تعریف یک ویجت در کاتالوگ.</summary>
public class WidgetDefDto
{
    /// <summary>کلید یکتا — مثل "fac-sales-month"</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>دستهٔ نمایشی در پنل «افزودن ویجت»</summary>
    public string Category { get; set; } = "";
    /// <summary>آیکون Bootstrap Icons</summary>
    public string Icon { get; set; } = "bi-graph-up";
    /// <summary>ماژول RBAC لازم برای دیدن/گرفتن دادهٔ این ویجت</summary>
    public string Module { get; set; } = "";
    public DashWidgetKind Kind { get; set; } = DashWidgetKind.Kpi;
    /// <summary>انواع نمودار مجاز (وقتی Kind = Chart)</summary>
    public List<DashChartType> ChartTypes { get; set; } = new();
    /// <summary>واحد پیش‌فرض برای KPI (ریال، عدد، …)</summary>
    public string Unit { get; set; } = "";
    /// <summary>آیا بازهٔ زمانی دارد؟</summary>
    public bool HasRange { get; set; }
    public int DefaultW { get; set; } = 3;
    public int DefaultH { get; set; } = 2;
    /// <summary>لینک drill-down — با کلیک روی ویجت باز می‌شود</summary>
    public string Drilldown { get; set; } = "";
    public List<WidgetOptionDefDto> Options { get; set; } = new();
}

/// <summary>یک ویجت چیده‌شده در داشبورد کاربر.</summary>
public class UserDashWidgetDto
{
    public int Id { get; set; }
    public string WidgetKey { get; set; } = "";
    /// <summary>عنوان سفارشی — خالی یعنی عنوان پیش‌فرض کاتالوگ</summary>
    public string? Title { get; set; }
    public int Row { get; set; }
    public int Col { get; set; }
    public int W { get; set; } = 3;
    public int H { get; set; } = 2;
    public int SortOrder { get; set; }
    /// <summary>نوع نمودار انتخابی کاربر (برای ویجت نموداری)</summary>
    public DashChartType? ChartType { get; set; }
    public DashRange Range { get; set; } = DashRange.Month;
    /// <summary>پیکربندی آزاد ویجت (کلید → مقدار)</summary>
    public Dictionary<string, string> Config { get; set; } = new();
}

/// <summary>داشبورد کاربر.</summary>
public class UserDashboardDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>داشبورد پیش‌فرض — هنگام بازکردن «داشبورد من» همین نشان داده می‌شود</summary>
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<UserDashWidgetDto> Widgets { get; set; } = new();
}

/// <summary>یک سری نمودار.</summary>
public class DashSeriesDto
{
    public string Name { get; set; } = "";
    public List<string> Labels { get; set; } = new();
    public List<decimal> Values { get; set; } = new();
}

/// <summary>یک ردیف جدول/فهرست.</summary>
public class DashRowDto
{
    public List<string> Cells { get; set; } = new();
    /// <summary>لینک اختیاری ردیف</summary>
    public string? Href { get; set; }
    /// <summary>رنگ وضعیت (success/warning/danger/…)</summary>
    public string? Tone { get; set; }
}

/// <summary>دادهٔ آمادهٔ نمایش یک ویجت.</summary>
public class WidgetDataDto
{
    public string Key { get; set; } = "";
    public DashWidgetKind Kind { get; set; }
    public string Title { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    // ---------- KPI ----------
    public decimal? Value { get; set; }
    public string? Unit { get; set; }
    /// <summary>متن روند (مثل «+۱۲٪ نسبت به ماه قبل»)</summary>
    public string? DeltaText { get; set; }
    public bool? DeltaUp { get; set; }
    /// <summary>عدد ثانویه (مثل تعداد سند)</summary>
    public decimal? SubValue { get; set; }
    public string? SubLabel { get; set; }

    // ---------- Chart ----------
    public DashChartType ChartType { get; set; } = DashChartType.Bar;
    public List<DashSeriesDto> Series { get; set; } = new();

    // ---------- Table / List ----------
    public List<string> Columns { get; set; } = new();
    public List<DashRowDto> Rows { get; set; } = new();

    public string? Drilldown { get; set; }
    /// <summary>اگر پر نشود یعنی خطا (مثلاً ۴۰۳ یا نبود داده) — ویجت پیام را نشان می‌دهد</summary>
    public string? Error { get; set; }
}
