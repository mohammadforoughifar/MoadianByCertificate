using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// ================== منابع انسانی اصلی — مدیریت پایه سازمانی (HrMain) ==================
/// ستون فقرات ماژول منابع انسانی: اطلاعاتی که سایر ماژول‌ها (زمان‌بندی، حقوق، کارگزینی)
/// به آن وابسته هستند. جدول‌ها با پیشوند HrMain یکتا هستند و با جداول قبلی تداخلی ندارند:
/// HrMainCompanies، HrMainBranches، HrMainOrgNodes، HrMainPositions، HrMainLocales، HrMainRules.
/// تعطیلات رسمی از جدول موجود CompanyHoliday استفاده می‌کند (بدون جدول تکراری).
/// اسکیما با HrMainSchemaV1 به‌صورت Ensure ساخته می‌شود (نه EF Migration) تا دیتابیس‌های
/// موجود هم بدون مشکل ارتقا یابند — مشابه Organizations/Semats.
/// </summary>

/// <summary>سطح گره در ساختار سازمانی: شرکت ← واحد ← دپارتمان ← تیم</summary>
public enum HrMainOrgLevel
{
    Company = 0,
    Unit = 1,
    Department = 2,
    Team = 3
}

/// <summary>نوع شعبه/دفتر</summary>
public enum HrMainBranchType
{
    Branch = 0,
    Office = 1,
    Agency = 2
}

/// <summary>پروفایل شرکت — همیشه یک ردیف (نخستین ردیف جدول مبناست)</summary>
public class HrMainCompany
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = "";

    /// <summary>مسیر نسبی لوگو از جذر wwwroot — مثلاً uploads/hr-main/logo/1/xxx.png</summary>
    [MaxLength(300)]
    public string? LogoPath { get; set; }

    [MaxLength(400)]
    public string? Address { get; set; }

    /// <summary>کد اقتصادی</summary>
    [MaxLength(30)]
    public string? EconomicCode { get; set; }

    /// <summary>شماره ثبت</summary>
    [MaxLength(30)]
    public string? RegistrationNo { get; set; }

    /// <summary>شناسه ملی شرکت</summary>
    [MaxLength(30)]
    public string? NationalId { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(150)]
    public string? Website { get; set; }

    /// <summary>نام مدیرعامل / بالاترین مقام</summary>
    [MaxLength(150)]
    public string? ManagerName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>شعب و دفاتر شرکت</summary>
public class HrMainBranch
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string Code { get; set; } = "";

    [MaxLength(150)]
    public string Name { get; set; } = "";

    public HrMainBranchType Type { get; set; } = HrMainBranchType.Branch;

    [MaxLength(80)]
    public string? City { get; set; }

    [MaxLength(400)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(150)]
    public string? ManagerName { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>گره ساختار سازمانی — درخت شرکت ← واحدها ← دپارتمان‌ها ← تیم‌ها</summary>
public class HrMainOrgNode
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public HrMainOrgLevel Level { get; set; } = HrMainOrgLevel.Unit;

    [MaxLength(20)]
    public string Code { get; set; } = "";

    [MaxLength(150)]
    public string Name { get; set; } = "";

    /// <summary>شعبه محل استقرار این گره (اختیاری)</summary>
    public int? BranchId { get; set; }

    /// <summary>عنوان مدیر گره (متن آزاد — مثلاً «مدیر مالی»)</summary>
    [MaxLength(150)]
    public string? ManagerTitle { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>پست سازمانی + شرح وظایف</summary>
public class HrMainPosition
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string? Code { get; set; }

    [MaxLength(150)]
    public string Title { get; set; } = "";

    /// <summary>گره سازمانی محل این پست (اختیاری)</summary>
    public int? OrgNodeId { get; set; }

    /// <summary>رتبه/گرید شغلی — مثلاً «کارشناس ارشد»</summary>
    [MaxLength(50)]
    public string? Grade { get; set; }

    /// <summary>شرح وظایف پست (Job Description)</summary>
    public string? JobDescription { get; set; }

    /// <summary>شرایط احراز (تحصیلات، سابقه، مهارت‌ها)</summary>
    public string? Requirements { get; set; }

    /// <summary>تعداد پست مصوب</summary>
    public int HeadCount { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>تنظیمات زبان و تقویم — همیشه یک ردیف (نخستین ردیف جدول مبناست)</summary>
public class HrMainLocale
{
    public int Id { get; set; }

    /// <summary>fa یا en</summary>
    [MaxLength(10)]
    public string Language { get; set; } = "fa";

    /// <summary>jalali یا gregorian</summary>
    [MaxLength(10)]
    public string Calendar { get; set; } = "jalali";

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// قوانین پیش‌فرض منابع انسانی — همیشه یک ردیف (نخستین ردیف جدول مبناست).
/// مبنای محاسبات ماژول‌های مرخصی، تأخیر/غیبت و اضافه‌کاری (طبق قانون کار).
/// </summary>
public class HrMainRules
{
    public int Id { get; set; }

    // ---------- مرخصی ----------
    /// <summary>مرخصی استحقاقی سالانه (روز) — قانون کار: ۲۶ روز</summary>
    public int AnnualLeaveDays { get; set; } = 26;

    /// <summary>حداکثر مرخصی پیوسته (روز)</summary>
    public int MaxConsecutiveLeaveDays { get; set; } = 9;

    /// <summary>حداکثر روزهای قابل انتقال به سال بعد</summary>
    public int MaxCarryOverDays { get; set; } = 9;

    /// <summary>مرخصی بدون حقوق مجاز است؟</summary>
    public bool UnpaidLeaveAllowed { get; set; } = true;

    // ---------- تأخیر ----------
    /// <summary>ارفاق تأخیر ورود (دقیقه)</summary>
    public int LateGraceMinutes { get; set; } = 10;

    /// <summary>سقف تأخیر مجاز ماهانه (دقیقه)</summary>
    public int MonthlyAllowedLateMinutes { get; set; } = 60;

    /// <summary>حداکثر تأخیر بدون برگه مرخصی (دقیقه)</summary>
    public int MaxLateWithoutLeaveMinutes { get; set; } = 120;

    /// <summary>ضریب کسرکار تأخیر (۱ = یک‌به‌یک)</summary>
    public decimal LateDeductionFactor { get; set; } = 1.0m;

    // ---------- غیبت ----------
    /// <summary>ضریب کسر حقوق هر روز غیبت (۱ = یک روز حقوق)</summary>
    public decimal AbsenceDailyDeductionFactor { get; set; } = 1.0m;

    /// <summary>اخطار کتبی بعد از چند غیبت غیرموجه متوالی؟</summary>
    public int UnexcusedAbsenceWarningAfter { get; set; } = 3;

    // ---------- اضافه‌کاری ----------
    /// <summary>ضریب اضافه‌کاری — قانون کار: ۱.۴ (۴۰٪ بیشتر از مزد عادی)</summary>
    public decimal OvertimeFactor { get; set; } = 1.4m;

    /// <summary>سقف اضافه‌کاری ماهانه (ساعت)</summary>
    public int MaxMonthlyOvertimeHours { get; set; } = 60;

    /// <summary>اضافه‌کاری نیاز به تأیید مدیر دارد؟</summary>
    public bool OvertimeNeedsApproval { get; set; } = true;

    /// <summary>آستانه پیش‌فرض هشدار انقضای قرارداد (روز)</summary>
    public int ContractAlertDays { get; set; } = 30;

    // ---------- ساعت کاری پیش‌فرض ----------
    // این مقادیر فقط پیش‌فرض سازمانی‌اند (برای پست/گره‌ای که شیفت اختصاصی FaAtt ندارد)؛
    // شیفت‌های تعریف‌شده در ماژول حضور و غیاب (FaAttShift) در محاسبه واقعی اولویت دارند.
    /// <summary>ساعت شروع کار پیش‌فرض</summary>
    public TimeSpan DefaultWorkStartTime { get; set; } = new(8, 0, 0);

    /// <summary>ساعت پایان کار پیش‌فرض</summary>
    public TimeSpan DefaultWorkEndTime { get; set; } = new(17, 0, 0);

    /// <summary>روزهای تعطیل هفتگی پیش‌فرض با کاما — اعداد DayOfWeek (پیش‌فرض 5=جمعه)</summary>
    [MaxLength(20)]
    public string? DefaultWeeklyOffDays { get; set; } = "5";

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>نوع موجودیتِ تغییریافته — برای تاریخچه تغییرات ساختار سازمانی</summary>
public enum HrMainChangeEntity
{
    OrgNode = 0,
    Position = 1
}

/// <summary>نوع عملیات ثبت‌شده در تاریخچه</summary>
public enum HrMainChangeAction
{
    Create = 0,
    Update = 1,
    Delete = 2
}

/// <summary>
/// تاریخچه تغییرات ساختار سازمانی (گره‌ها و پست‌ها) — «چه کسی، کِی، چه فیلدی را از چه مقداری
/// به چه مقداری تغییر داد». مستقل از AuditLog سراسری است چون این یکی فیلد‌به‌فیلد و
/// خوانا برای گزارش «تاریخچه» و «مقایسه دو تاریخ» طراحی شده، نه صرفاً بدنه JSON خام.
/// </summary>
public class HrMainChangeLog
{
    public long Id { get; set; }

    public DateTime At { get; set; } = DateTime.Now;

    public HrMainChangeEntity Entity { get; set; }

    public HrMainChangeAction Action { get; set; }

    /// <summary>شناسه رکورد تغییریافته (گره یا پست)</summary>
    public int EntityId { get; set; }

    /// <summary>نام/عنوان رکورد در لحظه ثبت (برای نمایش حتی پس از حذف)</summary>
    [MaxLength(150)]
    public string EntityName { get; set; } = "";

    /// <summary>نام فیلد تغییریافته — خالی یعنی کل رکورد ایجاد/حذف شده</summary>
    [MaxLength(60)]
    public string? FieldName { get; set; }

    /// <summary>مقدار قبلی (متن خوانا)</summary>
    [MaxLength(300)]
    public string? OldValue { get; set; }

    /// <summary>مقدار جدید (متن خوانا)</summary>
    [MaxLength(300)]
    public string? NewValue { get; set; }

    public int? ByUserId { get; set; }

    [MaxLength(100)]
    public string ByUsername { get; set; } = "";
}

/// <summary>
/// تعطیلات «منابع انسانی اصلی» — جدول اختصاصی HrMainHolidays.
/// پیش‌تر این ماژول از جدول CompanyHolidays (متعلق به «منابع انسانی فروغ آریا») می‌خواند و می‌نوشت؛
/// برای تفکیک کامل دو ماژول، داده‌ها یک‌بار کپی شدند و از این به بعد هر ماژول جدول خودش را دارد.
/// </summary>
public class HrMainHoliday
{
    public int Id { get; set; }
    public DateTime HolidayDate { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    public bool IsOfficial { get; set; }
    [MaxLength(150)] public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
