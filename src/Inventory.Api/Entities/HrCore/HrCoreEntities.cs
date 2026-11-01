using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// ================== ماژول هسته پرسنلی (کارگزینی) — HrCore ==================
/// کاملاً مستقل از دو ماژول موجود (RadisHr با کانتکست جدا + جداول Hr کانتکست اصلی).
/// نام جدول‌ها با پیشوند Hr در دیتابیس مشترک یکتا هستند و هیچ تداخلی ندارند:
/// HrOrgUnits، HrEmployees، HrContracts، HrDecrees.
/// مدارک پرسنلی در جدول عمومی پیوست‌ها با Module="HrEmployee" ذخیره می‌شود.
/// </summary>

/// <summary>نوع واحد سازمانی</summary>
public enum HrOrgUnitType
{
    Company = 0,
    Branch = 1,
    Department = 2,
    CostCenter = 3
}

/// <summary>نوع استخدام / قرارداد — مقادیر ۰ تا ۴ قدیمی و ۵ تا ۸ جدید (سازگار با داده موجود)</summary>
public enum HrEmploymentType
{
    Rasmi = 0,
    Gharardadi = 1,
    Peymani = 2,
    Saati = 3,
    Mashaverei = 4,
    TamamVaght = 5,
    PareVaght = 6,
    Projei = 7,
    Azmayeshi = 8
}

// ================== §۹: وضعیت امضای قرارداد ==================
/// <summary>وضعیت امضای الکترونیکی قرارداد — §۹</summary>
public enum HrContractSignStatus
{
    Draft = 0,
    PendingSign = 1,
    Signed = 2
}

/// <summary>وضعیت پرسنل</summary>
public enum HrEmployeeStatus
{
    Active = 0,
    OnLeave = 1,
    Suspended = 2,
    Terminated = 3,
    Retired = 4
}

/// <summary>نوع حکم کارگزینی</summary>
public enum HrDecreeType
{
    Estekhdam = 0,
    Erteqa = 1,
    Enteghal = 2,
    TaghirHoghugh = 3,
    Tashvigh = 4,
    Tanbih = 5,
    GhatHamkari = 6,
    Bazneshastegi = 7
}

/// <summary>واحد سازمانی — درخت شرکت/شعبه/دپارتمان/مرکز هزینه</summary>
public class HrOrgUnit
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public HrOrgUnitType Type { get; set; } = HrOrgUnitType.Department;

    [MaxLength(20)]
    public string Code { get; set; } = "";

    [MaxLength(150)]
    public string Name { get; set; } = "";

    /// <summary>مدیر واحد (ارجاع به پرسنل) — اختیاری</summary>
    public int? ManagerEmployeeId { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>پرونده پرسنل — یک ردیف به‌ازای هر نفر</summary>
public class HrEmployee
{
    public int Id { get; set; }

    /// <summary>کد پرسنلی یکتا — خودکار (۱۰۰۱، ۱۰۰۲، …) یا دستی</summary>
    [MaxLength(20)]
    public string Code { get; set; } = "";

    [MaxLength(80)]
    public string FirstName { get; set; } = "";

    [MaxLength(80)]
    public string LastName { get; set; } = "";

    /// <summary>کد ملی یکتا (۱۰ رقم)</summary>
    [MaxLength(10)]
    public string NationalCode { get; set; } = "";

    public DateTime? BirthDate { get; set; }

    /// <summary>0=مرد 1=زن</summary>
    public int Gender { get; set; }

    /// <summary>0=مجرد 1=متاهل</summary>
    public int MaritalStatus { get; set; }

    [MaxLength(20)]
    public string? Mobile { get; set; }

    /// <summary>تلفن ثابت</summary>
    [MaxLength(20)]
    public string? Landline { get; set; }

    /// <summary>مسیر نسبی عکس پروفایل از جذر wwwroot</summary>
    [MaxLength(300)]
    public string? PhotoPath { get; set; }

    /// <summary>تماس اضطراری: نام بستگان</summary>
    [MaxLength(100)]
    public string? EmergencyContactName { get; set; }

    /// <summary>تماس اضطراری: نسبت</summary>
    [MaxLength(50)]
    public string? EmergencyContactRelation { get; set; }

    /// <summary>تماس اضطراری: شماره تماس</summary>
    [MaxLength(20)]
    public string? EmergencyContactPhone { get; set; }

    /// <summary>محل کار (شعبه/ساختمان/سایت)</summary>
    [MaxLength(150)]
    public string? Workplace { get; set; }

    /// <summary>آخرین مدرک تحصیلی — مثلاً کارشناسی</summary>
    [MaxLength(50)]
    public string? Degree { get; set; }

    /// <summary>رشته تحصیلی</summary>
    [MaxLength(100)]
    public string? FieldOfStudy { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    public DateTime HireDate { get; set; } = DateTime.Today;

    public int? OrgUnitId { get; set; }

    [MaxLength(150)]
    public string? PostTitle { get; set; }

    /// <summary>گره ساختار سازمانی جدید (منابع انسانی اصلی — HrMainOrgNodes) — اختیاری</summary>
    public int? HrMainNodeId { get; set; }

    /// <summary>پست سازمانی جدید (منابع انسانی اصلی — HrMainPositions) — اختیاری</summary>
    public int? HrMainPositionId { get; set; }

    /// <summary>مدیر مستقیم (خودارجاع) — برای چارت و گردش تأییدها</summary>
    public int? ManagerId { get; set; }

    public HrEmploymentType EmploymentType { get; set; } = HrEmploymentType.Gharardadi;

    public HrEmployeeStatus Status { get; set; } = HrEmployeeStatus.Active;

    /// <summary>حساب کاربری ورود به سیستم (اختیاری)</summary>
    public int? SystemUserId { get; set; }

    /// <summary>حقوق پایه حکمی فعلی (ریال)</summary>
    public decimal BaseSalary { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>شماره شبای بانکی (نرمال‌شده: IR + ۲۴ رقم)</summary>
    [MaxLength(29)]
    public string? Sheba { get; set; }

    /// <summary>نام بانک (برای تفکیک فایل پرداخت)</summary>
    [MaxLength(60)]
    public string? BankName { get; set; }

    /// <summary>شماره بیمه تأمین اجتماعی (۸ تا ۱۰ رقم) — برای فایل لیست بیمه §۲</summary>
    [MaxLength(12)]
    public string? InsuranceNo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>قرارداد پرسنل — هر نفر می‌تواند چند قرارداد دوره‌ای داشته باشد</summary>
public class HrContract
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(30)]
    public string ContractNo { get; set; } = "";

    public HrEmploymentType Type { get; set; } = HrEmploymentType.Gharardadi;

    public DateTime StartDate { get; set; } = DateTime.Today;

    /// <summary>خالی = دائمی/رسمی بدون پایان</summary>
    public DateTime? EndDate { get; set; }

    public decimal BaseSalary { get; set; }

    [MaxLength(150)]
    public string? JobTitle { get; set; }

    public int? OrgUnitId { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>قالب مبنای ایجاد (§۹) — خالی یعنی بدون قالب</summary>
    public int? TemplateId { get; set; }

    /// <summary>وضعیت امضای الکترونیکی (§۹)</summary>
    public HrContractSignStatus SignStatus { get; set; } = HrContractSignStatus.Draft;

    [MaxLength(150)]
    public string? EmployeeSignedBy { get; set; }

    public DateTime? EmployeeSignedAt { get; set; }

    public int? EmployerSignedByUserId { get; set; }

    [MaxLength(150)]
    public string? EmployerSignedByName { get; set; }

    public DateTime? EmployerSignedAt { get; set; }

}

/// <summary>حکم کارگزینی — تغییرات رسمی وضعیت/پست/حقوق/واحد پرسنل با قابلیت «اجرا»</summary>
public class HrDecree
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(30)]
    public string DecreeNo { get; set; } = "";

    public HrDecreeType Type { get; set; } = HrDecreeType.Estekhdam;

    public DateTime EffectiveDate { get; set; } = DateTime.Today;

    [MaxLength(150)]
    public string? NewPostTitle { get; set; }

    public int? NewOrgUnitId { get; set; }

    public decimal? NewBaseSalary { get; set; }

    public HrEmployeeStatus? NewStatus { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>آیا روی پرونده پرسنل اعمال شده است؟ (یک‌بار)</summary>
    public bool IsApplied { get; set; }

    public DateTime? AppliedAt { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

// ================== §۹: قالب، نسخه و هشدار قرارداد ==================
/// <summary>قالب آماده قرارداد (§۹) — ایجاد سریع قرارداد موقت/دائم/پروژه‌ای</summary>
public class HrContractTemplate
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";

    public HrEmploymentType Type { get; set; } = HrEmploymentType.Gharardadi;

    /// <summary>مدت پیش‌فرض (ماه) — خالی یعنی بدون پایان (دائم)</summary>
    public int? DurationMonths { get; set; }

    [MaxLength(150)]
    public string? JobTitle { get; set; }

    [MaxLength(2000)]
    public string? Terms { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>نسخه آرشیوی قرارداد (§۹) — اسنپ‌شات شرایط در هر ویرایش</summary>
public class HrContractVersion
{
    public int Id { get; set; }

    public int ContractId { get; set; }

    public int VersionNo { get; set; }

    [MaxLength(30)]
    public string ContractNo { get; set; } = "";

    public HrEmploymentType Type { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    /// <summary>حقوق پایه اسنپ‌شات (ریال)</summary>
    public double BaseSalary { get; set; }

    [MaxLength(150)]
    public string? JobTitle { get; set; }

    public int? OrgUnitId { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public int? ChangedByUserId { get; set; }

    [MaxLength(150)]
    public string? ChangedByName { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    [MaxLength(300)]
    public string? ChangeNote { get; set; }
}

/// <summary>رد هشدارهای انقضای قرارداد (§۹) — هر آستانه برای هر تاریخ پایان فقط یک‌بار</summary>
public class HrContractExpiryAlert
{
    public int Id { get; set; }

    public int ContractId { get; set; }

    public int ThresholdDays { get; set; }

    public DateTime ExpireDate { get; set; }

    public int NotifiedCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>آگهی استخدام (§۴.۱)</summary>
public class HrJobPosting
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Title { get; set; } = "";

    public int? OrgUnitId { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? Requirements { get; set; }

    public int? Headcount { get; set; }

    /// <summary>0 پیش‌نویس، 1 منتشرشده، 2 بسته</summary>
    public int Status { get; set; }

    public DateTime? PublishDate { get; set; }

    public DateTime? ExpireDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>متقاضی استخدام (§۴.۱)</summary>
public class HrApplicant
{
    public int Id { get; set; }

    public int JobPostingId { get; set; }

    [MaxLength(100)]
    public string FirstName { get; set; } = "";

    [MaxLength(100)]
    public string LastName { get; set; } = "";

    [MaxLength(15)]
    public string? Mobile { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    /// <summary>0 جدید، 1 بررسی، 2 دعوت به مصاحبه، 3 مصاحبه‌شده، 4 پذیرفته، 5 رد، 6 استخدام‌شده</summary>
    public int Status { get; set; }

    public int? Score { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public int? EmployeeId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>مصاحبه استخدام (§۴.۱)</summary>
public class HrInterview
{
    public int Id { get; set; }

    public int ApplicantId { get; set; }

    public DateTime InterviewDate { get; set; } = DateTime.Today;

    [MaxLength(150)]
    public string? InterviewerName { get; set; }

    public int? Score { get; set; }

    /// <summary>0 نامشخص، 1 قبول، 2 رد، 3 ذخیره</summary>
    public int Result { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
