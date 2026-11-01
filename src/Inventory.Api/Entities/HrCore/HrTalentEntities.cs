using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>وضعیت آیتم چک‌لیست (آنبوردینگ/ترک‌کار)</summary>
public enum HrTalentItemStatus { Pending = 0, Done = 1, Skipped = 2 }

/// <summary>مسئول آیتم چک‌لیست: کارگزینی | مدیر | فناوری اطلاعات | خود پرسنل</summary>
public enum HrTalentOwner { Hr = 0, Manager = 1, It = 2, Self = 3 }

/// <summary>وضعیت پرونده آنبوردینگ/ترک‌کار/آزمایشی</summary>
public enum HrTalentCaseStatus { Open = 0, Completed = 1 }

/// <summary>نتیجه دوره آزمایشی</summary>
public enum HrTrialResult { Active = 0, Passed = 1, Failed = 2 }

/// <summary>نوع ترک‌کار: استعفا | اخراج | پایان قرارداد | بازنشستگی | توافقی</summary>
public enum HrExitType { Resign = 0, Dismiss = 1, ContractEnd = 2, Retire = 3, Mutual = 4 }

/// <summary>منبع رکورد سابقه شغلی: دستی | حکم | قرارداد</summary>
public enum HrHistorySource { Manual = 0, Decree = 1, Contract = 2 }

/// <summary>بازه ارزیابی عملکرد</summary>
public enum HrAppraisalPeriod { Yearly = 0, FirstHalf = 1, SecondHalf = 2, Quarterly = 3 }

/// <summary>وضعیت دوره ارزیابی</summary>
public enum HrAppraisalStatus { Draft = 0, Open = 1, Final = 2 }

// ==================== آنبوردینگ ====================

public class HrOnboarding
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public HrTalentCaseStatus Status { get; set; } = HrTalentCaseStatus.Open;
    [MaxLength(500)] public string? Note { get; set; }
    [MaxLength(150)] public string? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class HrOnboardingItem
{
    public int Id { get; set; }
    public int OnboardingId { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    public HrTalentOwner Owner { get; set; } = HrTalentOwner.Hr;
    public int SortOrder { get; set; }
    public HrTalentItemStatus Status { get; set; } = HrTalentItemStatus.Pending;
    [MaxLength(150)] public string? DoneBy { get; set; }
    public DateTime? DoneAt { get; set; }
}

// ==================== ترک‌کار ====================

public class HrExitCase
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime RequestDate { get; set; } = DateTime.Today;
    public DateTime? LastWorkDate { get; set; }
    public HrExitType Type { get; set; } = HrExitType.Resign;
    [MaxLength(500)] public string? Reason { get; set; }
    public HrTalentCaseStatus Status { get; set; } = HrTalentCaseStatus.Open;
    [MaxLength(150)] public string? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class HrExitItem
{
    public int Id { get; set; }
    public int ExitCaseId { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    public HrTalentOwner Owner { get; set; } = HrTalentOwner.Hr;
    public int SortOrder { get; set; }
    public HrTalentItemStatus Status { get; set; } = HrTalentItemStatus.Pending;
    [MaxLength(150)] public string? DoneBy { get; set; }
    public DateTime? DoneAt { get; set; }
}

// ==================== سوابق شغلی (دستی؛ احکام/قراردادها خودکار ترکیب می‌شوند) ====================

public class HrJobHistory
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime FromDate { get; set; } = DateTime.Today;
    public DateTime? ToDate { get; set; }
    [MaxLength(150)] public string PostTitle { get; set; } = "";
    [MaxLength(150)] public string? OrgUnitName { get; set; }
    public int? EmploymentType { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

// ==================== دوره آزمایشی (جدیدِ ماژول اصلی؛ مستقل از HrProbation قدیم) ====================

public class HrTrialPeriod
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    /// <summary>مدت به ماه: 1 | 3</summary>
    public int Months { get; set; } = 3;
    public DateTime EndDate { get; set; }
    public HrTrialResult Result { get; set; } = HrTrialResult.Active;
    [MaxLength(500)] public string? ResultNote { get; set; }
    [MaxLength(150)] public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

// ==================== ارزیابی عملکرد ====================

public class HrAppraisal
{
    public int Id { get; set; }
    [MaxLength(150)] public string Title { get; set; } = "";
    public int Year { get; set; }
    public HrAppraisalPeriod Period { get; set; } = HrAppraisalPeriod.Yearly;
    public HrAppraisalStatus Status { get; set; } = HrAppraisalStatus.Draft;
    [MaxLength(150)] public string? FinalizedBy { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class HrAppraisalKpi
{
    public int Id { get; set; }
    public int AppraisalId { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    /// <summary>وزن درصدی (مجموع باید ۱۰۰ شود)</summary>
    public double Weight { get; set; } = 10;
    /// <summary>سقف نمره هر شاخص (پیش‌فرض ۱۰۰)</summary>
    public double MaxScore { get; set; } = 100;
    public int SortOrder { get; set; }
}

public class HrAppraisalScore
{
    public int Id { get; set; }
    public int AppraisalId { get; set; }
    public int KpiId { get; set; }
    public int EmployeeId { get; set; }
    public double? ManagerScore { get; set; }
    public double? SelfScore { get; set; }
    [MaxLength(300)] public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

// ==================== مشاهده/درخواست ویرایش پرونده توسط خود پرسنل ====================

public enum HrProfileEditRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// درخواست پرسنل برای به‌روزرسانی پرونده‌ی خودش بدون نیاز به دسترسی مستقیم به ماژول کارگزینی.
/// فقط فیلدهای تماس مجازند (موبایل/ایمیل/آدرس/تلفن ثابت/تماس اضطراری) و پس از تأیید HR اعمال می‌شوند.
/// Fields به‌صورت خط‌های «کلید=مقدار» ذخیره می‌شود.
/// </summary>
public class HrProfileEditRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Fields { get; set; } = "";
    [MaxLength(500)] public string? Reason { get; set; }
    public HrProfileEditRequestStatus Status { get; set; } = HrProfileEditRequestStatus.Pending;
    [MaxLength(150)] public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    [MaxLength(500)] public string? DecideNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
