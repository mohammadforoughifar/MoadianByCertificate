namespace Inventory.Shared.Dtos;

// =====================================================================
//  گزارش‌ساز حرفه‌ای (Report Studio) — قراردادهای مشترک کلاینت و سرور
//
//  تفاوت کلیدی با نسخهٔ قبلی:
//   • کاربر خودش جدول‌ها را انتخاب و جوین می‌کند (نه دیتاست از پیش آماده)
//   • اشتراک‌گذاری هم با «کاربران مشخص» و هم با «نقش‌ها»، با سطح دسترسی
//
//  امنیت: هیچ‌جای این فایل SQL خام پذیرفته نمی‌شود. کلاینت فقط «شناسه»
//  می‌فرستد (نام جدول/ستون/عملگر) و سرور آن‌ها را با فهرست سفیدِ
//  ReportSchemaCatalog تطبیق می‌دهد. هر شناسهٔ ناشناخته = خطا.
// =====================================================================

// ---------------------------------------------------------------------
//  ۱) کاتالوگ اسکیما — چه جدول‌هایی و چه جوین‌هایی مجاز است
// ---------------------------------------------------------------------

/// <summary>نوع منطقی ستون — تعیین می‌کند چه عملگرها و چه تجمیع‌هایی مجاز است.</summary>
public enum RsFieldType
{
    Text = 0,
    Number = 1,
    Money = 2,
    Date = 3,
    Bool = 4,
    /// <summary>عدد با برچسب فارسی (مثل وضعیت نامه) — در خروجی متن نشان داده می‌شود.</summary>
    Enum = 5
}

/// <summary>یک ستون قابل استفاده در گزارش.</summary>
public class RsFieldDto
{
    /// <summary>شناسهٔ یکتا در کل گزارش: «جدول.ستون» — مثل «letter.title»</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public RsFieldType Type { get; set; }
    /// <summary>واحد نمایشی (ریال، عدد، روز…)</summary>
    public string? Unit { get; set; }
    /// <summary>راهنمای کوتاه برای کاربر</summary>
    public string? Hint { get; set; }
    /// <summary>برای Enum: نگاشت مقدار عددی به متن فارسی</summary>
    public Dictionary<string, string>? EnumMap { get; set; }
    /// <summary>آیا می‌شود روی آن گروه‌بندی کرد؟</summary>
    public bool Groupable { get; set; } = true;
    /// <summary>آیا می‌شود از آن سنجه ساخت؟ (مجموع/میانگین…)</summary>
    public bool Aggregatable { get; set; }
}

/// <summary>یک جدول (موجودیت) که کاربر می‌تواند در گزارش بیاورد.</summary>
public class RsTableDto
{
    /// <summary>شناسهٔ کوتاه — مثل «letter»، «erja»، «invoice»</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>دستهٔ منو — «اتوماسیون اداری»، «فروش» …</summary>
    public string Category { get; set; } = "";
    public string Icon { get; set; } = "bi-table";
    /// <summary>ماژول RBAC؛ کاربر بدون مجوز مشاهدهٔ آن، این جدول را نمی‌بیند.</summary>
    public string Module { get; set; } = "";
    public string? Description { get; set; }
    public List<RsFieldDto> Fields { get; set; } = new();
}

/// <summary>یک مسیر جوین از پیش تعریف‌شده بین دو جدول.</summary>
public class RsJoinPathDto
{
    /// <summary>شناسهٔ یکتا — مثل «letter->erja»</summary>
    public string Key { get; set; } = "";
    public string FromTable { get; set; } = "";
    public string ToTable { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>توضیح رابطه برای کاربر — «هر نامه چند ارجاع دارد»</summary>
    public string? Description { get; set; }
    /// <summary>آیا این رابطه یک‌به‌چند است؟ (هشدار تکرار سطر به کاربر)</summary>
    public bool Multiplies { get; set; }
}

/// <summary>کل کاتالوگ قابل مشاهده برای کاربر جاری.</summary>
public class RsCatalogDto
{
    public List<RsTableDto> Tables { get; set; } = new();
    public List<RsJoinPathDto> Joins { get; set; } = new();
    public bool CanDesign { get; set; }
    public bool CanShare { get; set; }
}

// ---------------------------------------------------------------------
//  ۲) تعریف گزارش — چیزی که کاربر می‌سازد
// ---------------------------------------------------------------------

public enum RsJoinKind
{
    /// <summary>فقط سطرهایی که در هر دو طرف وجود دارند</summary>
    Inner = 0,
    /// <summary>همهٔ سطرهای جدول اصلی، حتی بدون تطابق</summary>
    Left = 1
}

/// <summary>یک جدول که به گزارش اضافه شده.</summary>
public class RsQueryTableDto
{
    public string TableKey { get; set; } = "";
    /// <summary>برای جدول‌های دوم به بعد: از کدام مسیر جوین آمده‌اند.</summary>
    public string? JoinKey { get; set; }
    public RsJoinKind JoinKind { get; set; } = RsJoinKind.Left;
}

public enum RsAgg
{
    None = 0,
    Count = 1,
    Sum = 2,
    Avg = 3,
    Min = 4,
    Max = 5,
    CountDistinct = 6
}

/// <summary>گروه‌بندی زمانی روی ستون تاریخ.</summary>
public enum RsDateBucket
{
    None = 0,
    Day = 1,
    Month = 2,
    Quarter = 3,
    Year = 4
}

/// <summary>یک ستون در خروجی گزارش.</summary>
public class RsColumnDto
{
    public string FieldKey { get; set; } = "";
    /// <summary>عنوان دلخواه کاربر (اگر خالی باشد از کاتالوگ گرفته می‌شود)</summary>
    public string? Label { get; set; }
    public RsAgg Agg { get; set; } = RsAgg.None;
    /// <summary>فقط برای ستون تاریخِ گروه‌بندی‌شده</summary>
    public RsDateBucket Bucket { get; set; } = RsDateBucket.None;
    public bool Visible { get; set; } = true;
    public int Order { get; set; }
}

public enum RsOp
{
    Eq = 0, Ne = 1, Gt = 2, Gte = 3, Lt = 4, Lte = 5,
    Contains = 6, StartsWith = 7, EndsWith = 8,
    In = 9, Between = 10, IsNull = 11, NotNull = 12,
    /// <summary>بازهٔ نسبی تاریخ — امروز، ۷ روز اخیر، ماه جاری…</summary>
    DateRange = 13
}

/// <summary>یک شرط فیلتر.</summary>
public class RsFilterDto
{
    public string FieldKey { get; set; } = "";
    public RsOp Op { get; set; }
    public string? Value { get; set; }
    /// <summary>مقدار دوم برای Between</summary>
    public string? Value2 { get; set; }
    /// <summary>برای In — فهرست مقادیر</summary>
    public List<string>? Values { get; set; }
    /// <summary>true = با شرط قبلی OR شود (پیش‌فرض AND)</summary>
    public bool OrWithPrevious { get; set; }
    /// <summary>پارامتر زمان اجرا — کاربرِ گزارش موقع اجرا مقدار می‌دهد.</summary>
    public bool IsPrompt { get; set; }
    public string? PromptLabel { get; set; }
}

public class RsSortDto
{
    public string FieldKey { get; set; } = "";
    public bool Desc { get; set; }
}

public enum RsOutput { Table = 0, Chart = 1, Kpi = 2, Pivot = 3 }
public enum RsChart { Bar = 0, Line = 2, Pie = 3, Doughnut = 4, Area = 5, StackedBar = 6 }

/// <summary>تعریف کامل یک گزارش.</summary>
public class RsQueryDto
{
    /// <summary>جدول اصلی + جدول‌های جوین‌شده (اولی همیشه جدول اصلی است)</summary>
    public List<RsQueryTableDto> Tables { get; set; } = new();
    public List<RsColumnDto> Columns { get; set; } = new();
    public List<RsFilterDto> Filters { get; set; } = new();
    public List<RsSortDto> Sorts { get; set; } = new();

    /// <summary>اگر هر ستونی Agg داشته باشد، بقیه به‌صورت خودکار گروه‌بندی می‌شوند.</summary>
    public bool Distinct { get; set; }
    public int Take { get; set; } = 200;

    public RsOutput Output { get; set; } = RsOutput.Table;
    public RsChart Chart { get; set; } = RsChart.Bar;
    /// <summary>برای Pivot: ستونی که به سرستون تبدیل می‌شود</summary>
    public string? PivotColumnField { get; set; }
}

// ---------------------------------------------------------------------
//  ۳) اشتراک‌گذاری — هستهٔ خواستهٔ کاربر
// ---------------------------------------------------------------------

/// <summary>سطح دسترسی به یک گزارش.</summary>
public enum RsAccess
{
    /// <summary>فقط اجرا و دیدن نتیجه</summary>
    View = 0,
    /// <summary>اجرا + گرفتن خروجی Excel</summary>
    Export = 1,
    /// <summary>ویرایش تعریف گزارش</summary>
    Edit = 2
}

/// <summary>اشتراک با یک کاربر مشخص.</summary>
public class RsUserShareDto
{
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public string? FullName { get; set; }
    public RsAccess Access { get; set; } = RsAccess.View;
}

/// <summary>اشتراک با یک نقش.</summary>
public class RsRoleShareDto
{
    public int RoleId { get; set; }
    public string? RoleName { get; set; }
    public RsAccess Access { get; set; } = RsAccess.View;
}

public enum RsVisibility
{
    /// <summary>فقط خودم</summary>
    Private = 0,
    /// <summary>کاربران و نقش‌های انتخاب‌شده</summary>
    Shared = 1,
    /// <summary>همهٔ کسانی که مجوز ماژول‌های به‌کاررفته را دارند</summary>
    Everyone = 2
}

/// <summary>یک گزارش ذخیره‌شده.</summary>
public class RsReportDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Icon { get; set; } = "bi-file-earmark-bar-graph";
    public string? Folder { get; set; }

    public RsQueryDto Query { get; set; } = new();
    public RsVisibility Visibility { get; set; } = RsVisibility.Private;
    public List<RsUserShareDto> UserShares { get; set; } = new();
    public List<RsRoleShareDto> RoleShares { get; set; } = new();

    // --- فقط خواندنی (سرور پر می‌کند) ---
    public int OwnerUserId { get; set; }
    public string? OwnerName { get; set; }
    public bool IsMine { get; set; }
    /// <summary>دسترسی کاربر جاری به این گزارش</summary>
    public RsAccess MyAccess { get; set; }
    public bool CanEdit { get; set; }
    public bool CanShare { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ---------------------------------------------------------------------
//  ۴) اجرا و نتیجه
// ---------------------------------------------------------------------

/// <summary>مقدار یک پارامتر زمان اجرا.</summary>
public class RsPromptValueDto
{
    public string FieldKey { get; set; } = "";
    public string? Value { get; set; }
    public string? Value2 { get; set; }
}

public class RsRunRequestDto
{
    /// <summary>برای اجرای گزارش ذخیره‌شده</summary>
    public int? ReportId { get; set; }
    /// <summary>برای پیش‌نمایش زنده در طراح (بدون ذخیره)</summary>
    public RsQueryDto? Query { get; set; }
    public List<RsPromptValueDto> Prompts { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public class RsResultColumnDto
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public RsFieldType Type { get; set; }
    public string? Unit { get; set; }
    /// <summary>راست‌چین برای عدد، وسط برای بولین…</summary>
    public string Align { get; set; } = "right";
}

public class RsSeriesDto
{
    public string Name { get; set; } = "";
    public List<string> Labels { get; set; } = new();
    public List<decimal> Values { get; set; } = new();
}

public class RsResultDto
{
    public bool Ok { get; set; } = true;
    /// <summary>پیام خطای قابل‌فهم فارسی (اگر Ok=false)</summary>
    public string? Error { get; set; }

    public string Title { get; set; } = "";
    public RsOutput Output { get; set; }
    public RsChart Chart { get; set; }

    public List<RsResultColumnDto> Columns { get; set; } = new();
    /// <summary>سطرها — هر سطر آرایه‌ای از متنِ آمادهٔ نمایش (فرمت‌شده)</summary>
    public List<List<string>> Rows { get; set; } = new();
    public List<RsSeriesDto> Series { get; set; } = new();

    /// <summary>برای KPI</summary>
    public decimal? KpiValue { get; set; }
    public string? KpiUnit { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int TotalRows { get; set; }
    public bool HasMore { get; set; }
    public long ElapsedMs { get; set; }
    /// <summary>هشدارهای غیرمرگبار — مثل «جوین یک‌به‌چند سطرها را تکرار می‌کند»</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>گزینه‌های انتخاب کاربر/نقش برای پنل اشتراک‌گذاری.</summary>
public class RsShareTargetsDto
{
    public List<RsUserShareDto> Users { get; set; } = new();
    public List<RsRoleShareDto> Roles { get; set; } = new();
}
