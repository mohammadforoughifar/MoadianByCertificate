using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// ============================================================
//  ماژول مدیریت پروژه‌ها — ورود/خروج، گزارش کار، کارفرما، نوع فاکتور، پیوست
// ============================================================

/// <summary>کارفرما</summary>
public class KarFarma
{
    public int Id { get; set; }

    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(500)] public string? Address { get; set; }

    /// <summary>شماره تماس مدیرعامل</summary>
    [MaxLength(20)] public string? ModirAmelPhone { get; set; }

    [MaxLength(20)] public string? Telephone { get; set; }
    [MaxLength(20)] public string? Fax { get; set; }

    /// <summary>شماره ثبت شرکت</summary>
    [MaxLength(50)] public string? ShomareSabt { get; set; }

    public bool IsDelete { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<ProjectEntryExit> Projects { get; set; } = new List<ProjectEntryExit>();
}

/// <summary>نوع فاکتور</summary>
public class TypeFactor
{
    public int Id { get; set; }

    [MaxLength(150)] public string Name { get; set; } = "";

    public bool IsDelete { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<ProjectEntryExit> Projects { get; set; } = new List<ProjectEntryExit>();
}

/// <summary>لیست ورود و خروج پروژه‌ها</summary>
public class ProjectEntryExit
{
    public int Id { get; set; }

    /// <summary>کد پروژه — خودکار: «ورود جدید» = آخرین کد عددی + ۱ ؛ «برگشتی» = REn-کد مبدأ (مثل RE1-2001)</summary>
    [MaxLength(60)] public string CodeProject { get; set; } = "";

    /// <summary>عدد برگشتی (n در REn) — کاربر دستی وارد می‌کند؛ ۰ یعنی برگشتی نیست</summary>
    public int ReturnProjectId { get; set; }

    [MaxLength(50)] public string SerialNumber { get; set; } = "";
    [MaxLength(250)] public string ProjectName { get; set; } = "";

    /// <summary>شماره قبض خروج</summary>
    [MaxLength(50)] public string? GhabzExit { get; set; }

    /// <summary>شماره فاکتور</summary>
    [MaxLength(50)] public string? FactorNumber { get; set; }

    /// <summary>شماره/شرح کارشناسی اولیه (۱۰۰ کاراکتر — داده‌های قدیمی تا ۸۱ کاراکتر داشتند)</summary>
    [MaxLength(100)] public string? KarshenasiAvalie { get; set; }

    /// <summary>تحویل گیرنده پروژه</summary>
    [MaxLength(200)] public string ProjectReceiver { get; set; } = "";
    [MaxLength(1000)] public string? Description { get; set; }

    public int KarFarmaId { get; set; }

    /// <summary>نوع فاکتور (اختیاری — بعداً از فرم مجزای فاکتور تکمیل می‌شود)</summary>
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

    public bool IsDelete { get; set; }

    // ==================== گردش‌کار کارتابل (مدیر ← کارشناسی) ====================
    /// <summary>وضعیت گردش‌کار: ۰=در انتظار تایید مدیر، ۱=در انتظار کارشناسی، ۲=رد شده توسط مدیر، ۳=نهایی (کارشناسی انجام شد).
    /// پروژه‌های قدیمی در مایگریشن روی ۳ ست می‌شوند تا کارتابل‌ها خالی‌نشان‌دهندهٔ درستی داشته باشند.</summary>
    public int FlowStatus { get; set; }

    /// <summary>کاربر مدیری که تایید/رد کرد</summary>
    public int? ManagerActionById { get; set; }
    public DateTime? ManagerActionAt { get; set; }

    /// <summary>یادداشت مدیر (توضیح تایید یا دلیل رد)</summary>
    [MaxLength(500)] public string? ManagerNote { get; set; }

    /// <summary>کارشناسی که اتمام کارشناسی را ثبت کرد</summary>
    public int? ExpertActionById { get; set; }
    public DateTime? ExpertActionAt { get; set; }

    /// <summary>یادداشت/خلاصهٔ کارشناسی</summary>
    [MaxLength(500)] public string? ExpertNote { get; set; }

    /// <summary>جمع ساعات کار صرف‌شده (از گزارش‌های کار — به‌روزرسانی خودکار).
    /// در دیتابیس به‌صورت bigint (تیک) ذخیره می‌شود چون نوع <c>time</c> بیش از ۲۴ ساعت را نمی‌پذیرد
    /// (پروژه‌های واقعی صدها ساعت گزارش دارند).</summary>
    public TimeSpan TotalSpentTime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ---------- Navigation ----------
    public User? User { get; set; }
    public KarFarma? KarFarma { get; set; }
    public TypeFactor? TypeFactor { get; set; }
    public ICollection<ReportWork> ReportWorks { get; set; } = new List<ReportWork>();
    public ICollection<ProjectAttach> Attaches { get; set; } = new List<ProjectAttach>();
}

/// <summary>گزارش کار روی پروژه</summary>
public class ReportWork
{
    public int Id { get; set; }

    /// <summary>کد پروژه — موقع ثبت از پروژه کپی می‌شود؛ گزارشات روزانه بر اساس کد پروژه هستند نه Id جدول</summary>
    [MaxLength(60)] public string CodeProject { get; set; } = "";

    /// <summary>تاریخ گزارش</summary>
    public DateTime ReportDate { get; set; } = DateTime.Today;

    /// <summary>شناسه کاربر لاگین — ثبت‌کنندهٔ گزارش</summary>
    public int UserId { get; set; }

    /// <summary>اپراتور — کسی که عملاً روی پروژه کار انجام داده (از جدول کاربران؛ اختیاری).
    /// در سیستم قدیمی ستون <c>Operator</c> جدول ReportWork بود و با ثبت‌کننده (<c>UserId</c>) فرق دارد.</summary>
    public int? OperatorId { get; set; }

    [MaxLength(1000)] public string WorkDescription { get; set; } = "";

    public int ProjectId { get; set; }

    public TimeOnly StartTime { get; set; } = new(8, 0);
    public TimeOnly EndTime { get; set; } = new(17, 0);

    /// <summary>مدت زمان صبحانه</summary>
    public TimeOnly BreakfastTime { get; set; }

    /// <summary>مدت زمان ناهار</summary>
    public TimeOnly LunchTime { get; set; }

    /// <summary>زمان صرف‌شده خالص = (پایان − شروع) − (صبحانه + ناهار)</summary>
    public TimeSpan SpentTime { get; set; }

    public bool IsDelete { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ---------- Navigation ----------
    public User? User { get; set; }
    public User? Operator { get; set; }
    public ProjectEntryExit? Project { get; set; }
}

/// <summary>پیوست پروژه — فایل رمزنگاری‌شده روی دیسک سرور + نام اصلی رمزنگاری‌شده در دیتابیس</summary>
public class ProjectAttach
{
    public int Id { get; set; }

    /// <summary>نام اصلی فایل — رمزنگاری‌شده در دیتابیس</summary>
    public string OriginalFileNameEncrypted { get; set; } = "";

    /// <summary>نام فیزیکی فایل روی سرور (تصادفی)</summary>
    [MaxLength(100)] public string StoredFileName { get; set; } = "";

    [MaxLength(20)] public string Extension { get; set; } = "";

    public long FileSize { get; set; }

    public DateTime DateSabt { get; set; } = DateTime.Now;

    /// <summary>نوع پیوست (۱=قرارداد، ۲=مدارک فنی، ۳=تاییدیه تحویل، ۴=سایر)</summary>
    public int Type { get; set; }

    public bool IsDelete { get; set; }

    /// <summary>کاربر لاگین آپلودکننده</summary>
    public int UserId { get; set; }

    public int ProjectId { get; set; }

    // ---------- Navigation ----------
    public User? User { get; set; }
    public ProjectEntryExit? Project { get; set; }
}
