namespace Inventory.Shared.Dtos;

// ==================== آنبوردینگ / ترک‌کار ====================

public class HrTalentItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    /// <summary>0=کارگزینی، 1=مدیر، 2=فناوری اطلاعات، 3=خود پرسنل</summary>
    public int Owner { get; set; }
    /// <summary>0=باز، 1=انجام‌شده، 2=ردشده</summary>
    public int Status { get; set; }
    public string? DoneBy { get; set; }
    public DateTime? DoneAt { get; set; }
}

public class HrOnboardingDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTime StartDate { get; set; }
    /// <summary>0=در جریان، 1=تکمیل</summary>
    public int Status { get; set; }
    public string? Note { get; set; }
    /// <summary>درصد پیشرفت چک‌لیست</summary>
    public int Progress { get; set; }
    public List<HrTalentItemDto> Items { get; set; } = new();
}

public class HrExitCaseDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTime RequestDate { get; set; }
    public DateTime? LastWorkDate { get; set; }
    /// <summary>0=استعفا، 1=اخراج، 2=پایان قرارداد، 3=بازنشستگی، 4=توافقی</summary>
    public int Type { get; set; }
    public string? Reason { get; set; }
    public int Status { get; set; }
    public int Progress { get; set; }
    public List<HrTalentItemDto> Items { get; set; } = new();
}

public class HrTalentCaseSaveDto
{
    public int EmployeeId { get; set; }
    /// <summary>فقط ترک‌کار</summary>
    public int Type { get; set; }
    public DateTime? LastWorkDate { get; set; }
    public string? Note { get; set; }
}

public class HrTalentItemSaveDto
{
    public string Title { get; set; } = "";
    public int Owner { get; set; }
}

// ==================== سوابق شغلی ====================

public class HrJobHistoryDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    /// <summary>0=دستی، 1=حکم، 2=قرارداد</summary>
    public int Source { get; set; }
}

public class HrJobHistorySaveDto
{
    public int EmployeeId { get; set; }
    public DateTime FromDate { get; set; } = DateTime.Today;
    public DateTime? ToDate { get; set; }
    public string PostTitle { get; set; } = "";
    public string? OrgUnitName { get; set; }
    public int? EmploymentType { get; set; }
    public string? Note { get; set; }
}

// ==================== دوره آزمایشی ====================

public class HrTrialPeriodDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTime StartDate { get; set; }
    public int Months { get; set; }
    public DateTime EndDate { get; set; }
    /// <summary>0=فعال، 1=قبول، 2=مردود</summary>
    public int Result { get; set; }
    public int? DaysLeft { get; set; }
    public string? ResultNote { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
}

public class HrTrialSaveDto
{
    public int EmployeeId { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public int Months { get; set; } = 3;
}

public class HrTrialDecideDto
{
    public bool Pass { get; set; }
    public string? Note { get; set; }
    public string? DecidedBy { get; set; }
}

// ==================== ارزیابی عملکرد ====================

public class HrAppraisalDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Year { get; set; }
    /// <summary>0=سالانه، 1=شش‌ماهه اول، 2=شش‌ماهه دوم، 3=فصلی</summary>
    public int Period { get; set; }
    /// <summary>0=پیش‌نویس، 1=در جریان، 2=نهایی</summary>
    public int Status { get; set; }
    public int KpiCount { get; set; }
    public int ScoredCount { get; set; }
    public double WeightSum { get; set; }
}

public class HrAppraisalSaveDto
{
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public int Period { get; set; }
}

/// <summary>پیشنهاد صدور حکم (افزایش حقوق/ارتقا) از نتایج ارزیابی — برای نفرات برتر</summary>
public class HrAppraisalDecreeProposalDto
{
    public int AppraisalId { get; set; }
    /// <summary>درصد افزایش حقوق پایه (مثلاً 12 یعنی ۱۲٪)</summary>
    public double RaisePercent { get; set; } = 10;
    /// <summary>گرید B هم شامل شود؟ (پیش‌فرض فقط A)</summary>
    public bool IncludeGradeB { get; set; }
    /// <summary>نوع حکم: 1=ارتقا، 3=تغییر حقوق (پیش‌فرض)</summary>
    public int DecreeType { get; set; } = 3;
    public DateTime EffectiveDate { get; set; } = DateTime.Today;
    /// <summary>حداقل پوشش شاخص‌های نمره‌گذاری‌شده (درصد) — پایین‌تر از این، حکم صادر نمی‌شود</summary>
    public int MinCoveragePercent { get; set; } = 50;
    /// <summary>اگر true و تاریخ اجرا رسیده باشد، حکم بلافاصله روی پرونده اعمال می‌شود (وگرنه واچر روزانه اعمال می‌کند)</summary>
    public bool AutoApply { get; set; } = true;
}

/// <summary>نتیجه‌ی صدور گروهی حکم از ارزیابی</summary>
public class HrAppraisalDecreeProposalResultDto
{
    public int Created { get; set; }
    public List<string> EmployeeNames { get; set; } = new();
}

public class HrAppraisalKpiDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public double Weight { get; set; }
    public double MaxScore { get; set; }
}

public class HrAppraisalKpiSaveDto
{
    public string Title { get; set; } = "";
    public double Weight { get; set; } = 10;
    public double MaxScore { get; set; } = 100;
}

public class HrAppraisalScoreDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public int KpiId { get; set; }
    public double? ManagerScore { get; set; }
    public double? SelfScore { get; set; }
    public string? Note { get; set; }
}

public class HrAppraisalScoreSaveDto
{
    public int EmployeeId { get; set; }
    public int KpiId { get; set; }
    public double? ManagerScore { get; set; }
    public double? SelfScore { get; set; }
    public string? Note { get; set; }
}

public class HrAppraisalResultDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    /// <summary>نمره نهایی وزنی از ۱۰۰</summary>
    public double Total { get; set; }
    /// <summary>A | B | C | D</summary>
    public string Grade { get; set; } = "";
    public int ScoredKpis { get; set; }
    public int KpiCount { get; set; }
}

// ==================== مشاهده/درخواست ویرایش پرونده خود ====================

/// <summary>پرونده‌ی خود پرسنل — برای نمایش به خودِ فرد، بدون نیاز به مجوز کارگزینی</summary>
public class HrMyProfileDto
{
    public int EmployeeId { get; set; }
    public string Code { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string NationalCode { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Landline { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelation { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? PostTitle { get; set; }
    public string? OrgNodeName { get; set; }
    public string? PositionTitle { get; set; }
    public DateTime HireDate { get; set; }
    public int EmploymentType { get; set; }
    public int Status { get; set; }
    /// <summary>شناسه‌ی درخواست ویرایش در انتظار تأیید (در صورت وجود)</summary>
    public int? PendingRequestId { get; set; }
}

/// <summary>درخواست ویرایش فیلدهای تماس پرونده توسط خود پرسنل — فقط فیلدهای پرشده ارسال می‌شوند</summary>
public class HrProfileEditRequestSaveDto
{
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Landline { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelation { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Reason { get; set; }
}

public class HrProfileEditRequestDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string EmployeeCode { get; set; } = "";
    /// <summary>خط‌های «کلید=مقدار» — مقدارهای پیشنهادی پرسنل</summary>
    public string Fields { get; set; } = "";
    public string? Reason { get; set; }
    public int Status { get; set; }
    public string StatusName { get; set; } = "";
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecideNote { get; set; }
    public DateTime CreatedAt { get; set; }
}
