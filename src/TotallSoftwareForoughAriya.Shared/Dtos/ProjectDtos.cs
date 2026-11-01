namespace Inventory.Shared.Dtos;

// ============================================================
//  ماژول مدیریت پروژه‌ها (ورود و خروج، گزارش کار، کارفرما، فاکتور)
// ============================================================

/// <summary>کارفرما</summary>
public class KarFarmaDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }

    /// <summary>شماره تماس مدیرعامل</summary>
    public string? ModirAmelPhone { get; set; }
    public string? Telephone { get; set; }
    public string? Fax { get; set; }

    /// <summary>شماره ثبت شرکت</summary>
    public string? ShomareSabt { get; set; }

    /// <summary>تعداد پروژه‌های این کارفرما (فقط برای نمایش)</summary>
    public int ProjectCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>نوع فاکتور / نوع پروژه</summary>
public class TypeFactorDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ProjectCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>رکورد ورود و خروج پروژه</summary>
public class ProjectEntryExitDto
{
    public int Id { get; set; }

    /// <summary>کد پروژه — خودکار: ورود جدید = آخرین کد+۱ ؛ برگشتی = REn-کد مبدأ (خروجی-فقط)</summary>
    public string CodeProject { get; set; } = "";

    /// <summary>عدد برگشتی (n در REn) — کاربر دستی وارد می‌کند؛ ۰ یعنی برگشتی نیست</summary>
    public int ReturnProjectId { get; set; }

    /// <summary>شناسه‌ی پروژه مبدأ در ثبت برگشتی (فقط ورودی ایجاد — کد از «عدد برگشتی + کد مبدأ» ساخته می‌شود)</summary>
    public int ReturnOfProjectId { get; set; }

    public string SerialNumber { get; set; } = "";
    public string ProjectName { get; set; } = "";

    /// <summary>شماره قبض خروج</summary>
    public string? GhabzExit { get; set; }

    /// <summary>شماره فاکتور</summary>
    public string? FactorNumber { get; set; }

    /// <summary>شماره کارشناسی اولیه</summary>
    public string? KarshenasiAvalie { get; set; }

    /// <summary>تحویل گیرنده پروژه</summary>
    public string ProjectReceiver { get; set; } = "";
    public string? Description { get; set; }

    public int KarFarmaId { get; set; }

    /// <summary>نوع فاکتور (اختیاری — از فرم مجزای فاکتور تکمیل می‌شود)</summary>
    public int? FactorTypeId { get; set; }

    /// <summary>کاربر لاگین مرتبط (اپراتور)</summary>
    public int UserId { get; set; }

    /// <summary>تاریخ خروج</summary>
    public DateTime? ExitDate { get; set; }

    /// <summary>تاریخ ورود</summary>
    public DateTime? EntryDate { get; set; }

    /// <summary>تاریخ پرونده</summary>
    public DateTime? FileDate { get; set; }

    /// <summary>تاریخ تحویل</summary>
    public DateTime? DeliveryDate { get; set; }

    /// <summary>تاریخ خروج موقت</summary>
    public DateTime? TemporaryExitDate { get; set; }

    /// <summary>تاریخ ثبت پروژه</summary>
    public DateTime? ProjectRegistrationDate { get; set; }

    /// <summary>تاریخ مورد نیاز مشتری</summary>
    public DateTime? CustomerRequiredDate { get; set; }

    /// <summary>آیا پوشه دارد؟</summary>
    public bool? IsFolder { get; set; }

    /// <summary>جمع ساعات کار صرف‌شده روی پروژه (از گزارش‌های کار)</summary>
    public TimeSpan TotalSpentTime { get; set; }

    // ---------- داده‌های نمایشی (جوین) ----------
    public string? KarFarmaName { get; set; }
    public string? FactorTypeName { get; set; }
    public string? UserName { get; set; }
    public int AttachCount { get; set; }
    public int ReportWorkCount { get; set; }

    // ---------- گردش‌کار کارتابل (خروجی-فقط؛ تغییر فقط از مسیر API کارتابل) ----------
    /// <summary>وضعیت گردش‌کار: ۰=در انتظار تایید مدیر، ۱=در انتظار کارشناسی، ۲=رد شده، ۳=نهایی</summary>
    public int FlowStatus { get; set; }
    public string? ManagerNote { get; set; }
    public string? ExpertNote { get; set; }
    public DateTime? ManagerActionAt { get; set; }
    public DateTime? ExpertActionAt { get; set; }
}

/// <summary>اقدام کارتابل پروژه: تایید/رد مدیر یا اتمام کارشناسی (یادداشت اختیاری — برای رد الزامی)</summary>
public class ProjectFlowActionDto
{
    public string? Note { get; set; }
}

/// <summary>آیتم کارتابل پروژه (+نام ثبت‌کننده و مدت انتظار)</summary>
public class ProjectCartableCountsDto
{
    public int Manager { get; set; }
    public int Expert { get; set; }

    /// <summary>تعداد درخواست‌های ویرایش/حذف در انتظار تایید مدیر</summary>
    public int Changes { get; set; }
}

/// <summary>
/// ثبت/ویرایش تاریخ‌های چرخهٔ پروژه — فرم مجزا از منوی سطر (مثل فرم فاکتور).
/// هر فیلد اختیاری است؛ <c>null</c> یعنی «ثبت نشده / پاک شود».
/// </summary>
public class ProjectDatesDto
{
    /// <summary>تاریخ خروج (خروج نهایی پروژه از مجموعه)</summary>
    public DateTime? ExitDate { get; set; }

    /// <summary>تاریخ خروج موقت</summary>
    public DateTime? TemporaryExitDate { get; set; }

    /// <summary>تاریخ مورد نیاز مشتری</summary>
    public DateTime? CustomerRequiredDate { get; set; }

    /// <summary>تاریخ تحویل پروژه</summary>
    public DateTime? DeliveryDate { get; set; }

    /// <summary>تاریخ تحویل پرونده</summary>
    public DateTime? FileDate { get; set; }
}

/// <summary>ثبت/ویرایش اطلاعات فاکتور پروژه — فرم مجزا از منوی سطر (بعداً تکمیل می‌شود)</summary>
public class ProjectFactorDto
{
    /// <summary>شناسه نوع فاکتور (الزامی)</summary>
    public int FactorTypeId { get; set; }

    /// <summary>شماره فاکتور (اختیاری — خالی یعنی پاک شود)</summary>
    public string? FactorNumber { get; set; }
}

/// <summary>گزارش کار روی پروژه</summary>
public class ReportWorkDto
{
    public int Id { get; set; }

    /// <summary>تاریخ گزارش</summary>
    public DateTime ReportDate { get; set; } = DateTime.Today;

    /// <summary>شناسه کاربر لاگین — ثبت‌کنندهٔ گزارش</summary>
    public int UserId { get; set; }

    /// <summary>اپراتور — کسی که روی پروژه کار انجام داده (از لیست کاربران؛ اختیاری). با ثبت‌کننده فرق دارد.</summary>
    public int? OperatorId { get; set; }
    public string WorkDescription { get; set; } = "";
    public int ProjectId { get; set; }

    /// <summary>کد پروژه — موقع ثبت از پروژه کپی می‌شود (خروجی-فقط؛ گزارشات بر اساس کد پروژه هستند)</summary>
    public string CodeProject { get; set; } = "";

    public TimeOnly StartTime { get; set; } = new(8, 0);
    public TimeOnly EndTime { get; set; } = new(17, 0);

    /// <summary>مدت زمان صبحانه (ساعت:دقیقه)</summary>
    public TimeOnly BreakfastTime { get; set; }

    /// <summary>مدت زمان ناهار (ساعت:دقیقه)</summary>
    public TimeOnly LunchTime { get; set; }

    /// <summary>زمان صرف‌شده خالص = (پایان − شروع) − (صبحانه + ناهار)</summary>
    public TimeSpan SpentTime { get; set; }

    // ---------- نمایشی ----------
    public string? ProjectName { get; set; }
    public string? UserName { get; set; }
    public string? OperatorName { get; set; }
}

/// <summary>پیوست پروژه (فایل رمزنگاری‌شده روی سرور)</summary>
public class ProjectAttachDto
{
    public int Id { get; set; }

    /// <summary>نام اصلی فایل (بعد از رمزگشایی)</summary>
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public int Type { get; set; }
    public DateTime DateSabt { get; set; }
    public int ProjectId { get; set; }
    public string? UserName { get; set; }
}

/// <summary>آیتم‌های کمبو برای فرم‌های ماژول پروژه</summary>
public class ProjectLookups
{
    public List<LookupItem> Users { get; set; } = new();

    /// <summary>اپراتورها (انجام‌دهندگان کار) — همهٔ کاربران شامل غیرفعال‌ها/کاربران قدیمیِ مهاجرت‌شده، تا گزارش‌های قدیمی قابل فیلتر بمانند</summary>
    public List<LookupItem> Operators { get; set; } = new();
    public List<LookupItem> KarFarmas { get; set; } = new();
    public List<LookupItem> TypeFactors { get; set; } = new();
    public List<LookupItem> Projects { get; set; } = new();

    /// <summary>فقط پروژه‌های اصلی (بدون RE) — برای کمبوی «پروژه برگشتی»</summary>
    public List<LookupItem> BaseProjects { get; set; } = new();

    /// <summary>
    /// فقط پروژه‌های تاییدشدهٔ مدیر (FlowStatus ۱ یا ۳) — برای کمبوی «پروژه» در فرم ثبت گزارش کار.
    /// پروژه‌های «در انتظار تایید مدیر» و «رد شده» قابل گزارش‌دهی نیستند.
    /// </summary>
    public List<LookupItem> ReportableProjects { get; set; } = new();
}

// ============================================================
//  صفحه‌بندی سمت سرور (بک‌اند) — لیست پروژه‌ها و گزارش‌های کار
//  داده‌ها ۲۰تا۲۰تا (قابل تنظیم) از دیتابیس واکشی می‌شوند تا لود صفحه سبک بماند.
/// <summary>پارامترهای فیلتر/مرتب‌سازی/صفحه‌بندی لیست پروژه‌ها — یکسان بین کلاینت و سرور</summary>
public class ProjectListQuery
{
    // ---------- فیلترهای بالای صفحه ----------
    public string? Search { get; set; }
    public int? KarfarmaId { get; set; }
    public int? TypeFactorId { get; set; }
    public int? UserId { get; set; }
    public bool? Returned { get; set; }

    // ---------- فیلتر پیشرفته: وضعیت داده ----------
    // هر فیلد سه حالت دارد: «همه» (هر دو false)، «دارد» (Has*=true) و «ندارد» (Empty*=true).
    // ترتیب اعمال در سرور: Empty*=true یعنی فقط ردیف‌های بدون مقدار؛ در غیر این صورت
    // Has*=true یعنی فقط ردیف‌های دارای مقدار (و اگر بازهٔ تاریخ هم داده شده باشد،
    // همان بازه هم روی این ردیف‌های دارای تاریخ اعمال می‌شود).
    public bool HasSerial { get; set; }
    public bool HasEntry { get; set; }
    public bool HasExit { get; set; }
    public bool HasTemporaryExit { get; set; }
    public bool HasDelivery { get; set; }
    public bool HasFile { get; set; }
    public bool HasFactor { get; set; }
    public bool HasFactorType { get; set; }
    public bool HasKarshenasi { get; set; }
    public bool HasRegistration { get; set; }
    public bool HasCustomerNeed { get; set; }

    // ---------- فیلتر پیشرفته «بدون داده» ----------
    public bool EmptySerial { get; set; }
    public bool EmptyEntry { get; set; }
    public bool EmptyExit { get; set; }
    public bool EmptyTemporaryExit { get; set; }
    public bool EmptyDelivery { get; set; }
    public bool EmptyFile { get; set; }
    public bool EmptyFactor { get; set; }
    public bool EmptyFactorType { get; set; }
    public bool EmptyKarshenasi { get; set; }
    public bool EmptyRegistration { get; set; }
    public bool EmptyCustomerNeed { get; set; }

    // ---------- جستجوی سرستون‌ها ----------
    public string? FCode { get; set; }
    public string? FName { get; set; }
    public string? FSerial { get; set; }
    public string? FKarfarma { get; set; }
    public string? FEntry { get; set; }
    public string? FExit { get; set; }
    public string? FFactor { get; set; }
    public string? FFactorType { get; set; }
    public string? FKarshenasi { get; set; }
    public string? FSabt { get; set; }
    public string? FNeed { get; set; }
    public string? FSpent { get; set; }

    // ---------- بازه‌های دقیق تاریخ (شامل سطرهای دارای مقدار) ----------
    public DateTime? EntryFrom { get; set; }
    public DateTime? EntryTo { get; set; }
    public DateTime? ExitFrom { get; set; }
    public DateTime? ExitTo { get; set; }
    public DateTime? TemporaryExitFrom { get; set; }
    public DateTime? TemporaryExitTo { get; set; }
    public DateTime? RegistrationFrom { get; set; }
    public DateTime? RegistrationTo { get; set; }
    public DateTime? CustomerNeedFrom { get; set; }
    public DateTime? CustomerNeedTo { get; set; }
    public DateTime? DeliveryFrom { get; set; }
    public DateTime? DeliveryTo { get; set; }
    public DateTime? FileFrom { get; set; }
    public DateTime? FileTo { get; set; }

    /// <summary>پوشه: y = دارد، n = ندارد، خالی = همه</summary>
    public string? FFolder { get; set; }

    /// <summary>وضعیت گردش‌کار: 0..3 — خالی = همه</summary>
    public string? FStatus { get; set; }

    // ---------- مرتب‌سازی و صفحه‌بندی ----------
    public string? Sort { get; set; }
    public bool Desc { get; set; } = true;
    public int Page { get; set; } = 1;

    /// <summary>اندازه صفحه — ۰ یا منفی یعنی «همه» (برای خروجی اکسل/چاپ)</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>ساخت رشتهٔ کوئری برای فراخوانی API (فقط مقادیر پرشده)</summary>
    public string ToQueryString()
    {
        var parts = new List<string>();
        void Add(string k, string? v)
        {
            if (!string.IsNullOrWhiteSpace(v)) parts.Add($"{k}={Uri.EscapeDataString(v)}");
        }
        void AddDate(string k, DateTime? v) => Add(k, v?.ToString("yyyy-MM-dd"));
        Add("search", Search);
        if (KarfarmaId is > 0) Add("karfarmaId", KarfarmaId.ToString());
        if (TypeFactorId is > 0) Add("typeFactorId", TypeFactorId.ToString());
        if (UserId is > 0) Add("userId", UserId.ToString());
        if (Returned is true) Add("returned", "true");
        if (EmptySerial) Add("emptySerial", "true");
        if (EmptyEntry) Add("emptyEntry", "true");
        if (EmptyExit) Add("emptyExit", "true");
        if (EmptyTemporaryExit) Add("emptyTemporaryExit", "true");
        if (EmptyDelivery) Add("emptyDelivery", "true");
        if (EmptyFile) Add("emptyFile", "true");
        if (EmptyFactor) Add("emptyFactor", "true");
        if (EmptyFactorType) Add("emptyFactorType", "true");
        if (EmptyKarshenasi) Add("emptyKarshenasi", "true");
        if (EmptyRegistration) Add("emptyRegistration", "true");
        if (EmptyCustomerNeed) Add("emptyCustomerNeed", "true");
        Add("fCode", FCode);
        Add("fName", FName);
        Add("fSerial", FSerial);
        Add("fKarfarma", FKarfarma);
        Add("fEntry", FEntry);
        Add("fExit", FExit);
        Add("fFactor", FFactor);
        Add("fFactorType", FFactorType);
        Add("fKarshenasi", FKarshenasi);
        Add("fSabt", FSabt);
        Add("fNeed", FNeed);
        Add("fSpent", FSpent);
        AddDate("entryFrom", EntryFrom); AddDate("entryTo", EntryTo);
        AddDate("exitFrom", ExitFrom); AddDate("exitTo", ExitTo);
        AddDate("temporaryExitFrom", TemporaryExitFrom); AddDate("temporaryExitTo", TemporaryExitTo);
        AddDate("registrationFrom", RegistrationFrom); AddDate("registrationTo", RegistrationTo);
        AddDate("customerNeedFrom", CustomerNeedFrom); AddDate("customerNeedTo", CustomerNeedTo);
        AddDate("deliveryFrom", DeliveryFrom); AddDate("deliveryTo", DeliveryTo);
        AddDate("fileFrom", FileFrom); AddDate("fileTo", FileTo);
        Add("fFolder", FFolder);
        Add("fStatus", FStatus);
        Add("sort", Sort);
        Add("desc", Desc ? "true" : "false");
        Add("page", Page.ToString());
        Add("pageSize", PageSize.ToString());
        return string.Join("&", parts);
    }
}

/// <summary>پارامترهای فیلتر/مرتب‌سازی/صفحه‌بندی لیست گزارش‌های کار</summary>
public class ReportWorkListQuery
{
    // ---------- فیلترهای بالای صفحه ----------
    public int? ProjectId { get; set; }
    public int? UserId { get; set; }

    /// <summary>اپراتور گزارش (انجام‌دهندهٔ کار) — متفاوت از ثبت‌کننده</summary>
    public int? OperatorId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    // ---------- جستجوی سرستون‌ها ----------
    public string? FDate { get; set; }
    public string? FCode { get; set; }
    public string? FProject { get; set; }
    public string? FUser { get; set; }

    /// <summary>فیلتر متنی اپراتور (نام یا نام کاربری)</summary>
    public string? FOperator { get; set; }
    public string? FStart { get; set; }
    public string? FEnd { get; set; }
    public string? FRest { get; set; }
    public string? FSpent { get; set; }
    public string? FDesc { get; set; }

    // ---------- مرتب‌سازی و صفحه‌بندی ----------
    public string? Sort { get; set; }
    public bool Desc { get; set; } = true;
    public int Page { get; set; } = 1;

    /// <summary>اندازه صفحه — ۰ یا منفی یعنی «همه» (برای خروجی اکسل/چاپ)</summary>
    public int PageSize { get; set; } = 20;

    public string ToQueryString()
    {
        var parts = new List<string>();
        void Add(string k, string? v)
        {
            if (!string.IsNullOrWhiteSpace(v)) parts.Add($"{k}={Uri.EscapeDataString(v)}");
        }
        if (ProjectId is > 0) Add("projectId", ProjectId.ToString());
        if (UserId is > 0) Add("userId", UserId.ToString());
        if (OperatorId is > 0) Add("operatorId", OperatorId.ToString());
        if (From.HasValue) Add("from", From.Value.ToString("yyyy-MM-dd"));
        if (To.HasValue) Add("to", To.Value.ToString("yyyy-MM-dd"));
        Add("fDate", FDate);
        Add("fCode", FCode);
        Add("fProject", FProject);
        Add("fUser", FUser);
        Add("fOperator", FOperator);
        Add("fStart", FStart);
        Add("fEnd", FEnd);
        Add("fRest", FRest);
        Add("fSpent", FSpent);
        Add("fDesc", FDesc);
        Add("sort", Sort);
        Add("desc", Desc ? "true" : "false");
        Add("page", Page.ToString());
        Add("pageSize", PageSize.ToString());
        return string.Join("&", parts);
    }
}

/// <summary>
/// درخواست تغییر پروژه (ویرایش/حذف) که در انتظار تایید مدیر است — بند «تایید مدیر برای حذف و ویرایش».
/// </summary>
public class ProjectChangeRequestDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    /// <summary>۱ = ویرایش، ۲ = حذف</summary>
    public int Kind { get; set; }

    /// <summary>۰ = در انتظار مدیر، ۱ = تاییدشده و اعمال‌شده، ۲ = ردشده</summary>
    public int Status { get; set; }

    public string CodeProject { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string? KarFarmaName { get; set; }

    /// <summary>خلاصهٔ خوانای تغییرات (برای نمایش به مدیر)</summary>
    public string Summary { get; set; } = "";

    public string? RequestNote { get; set; }
    public string? ManagerNote { get; set; }
    public string? RequestedByName { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ManagerActionAt { get; set; }

    /// <summary>تعداد روز انتظار در کارتابل</summary>
    public int DaysWaiting { get; set; }
}

/// <summary>خروجی GET api/projects/{id}/returns — کدهای برگشتی (RE) یک پروژه</summary>
public class ProjectReturnsDto
{
    public string ParentCode { get; set; } = "";
    public string ParentName { get; set; } = "";
    public string? ParentReceiver { get; set; }
    public int KarFarmaId { get; set; }
    public List<string> Codes { get; set; } = new();
}

/// <summary>
/// نتیجهٔ ویرایش/حذف پروژه — اگر کاربر مدیر نباشد، عملیات فقط به‌صورت «درخواست»
/// ثبت می‌شود و <see cref="Pending"/> برابر true برمی‌گردد.
/// </summary>
public class ChangeActionResult
{
    public int Id { get; set; }
    public bool Ok { get; set; }
    public bool Pending { get; set; }
    public string? Message { get; set; }
}
