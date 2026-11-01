using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// =====================================================================
// ماژول تکمیلی حضوروغیاب و زمان‌بندی (HrTime)
// پوشش: تصویب چندمرحله‌ای، انواع مرخصی/موجودی، اضافه‌کاری، روستِر شیفت
// چرخشی، ایمپورت دستگاه حضوروغیاب، قوانین محاسباتی
// =====================================================================

/// <summary>مرحله‌ی تصویب یک درخواست (مرخصی/ماموریت/اضافه‌کاری) — RequestType: Leave|Overtime</summary>
public class HrRequestStep
{
    public int Id { get; set; }
    [MaxLength(20)] public string RequestType { get; set; } = "Leave";
    public int RequestId { get; set; }
    public int StepNo { get; set; }
    /// <summary>نقش تصویب‌کننده: Manager (مدیر مستقیم) | Hr (کارگزینی)</summary>
    [MaxLength(20)] public string Role { get; set; } = "Hr";
    public int? ApproverUserId { get; set; }
    [MaxLength(150)] public string? ApproverName { get; set; }
    /// <summary>Pending|Approved|Rejected|Skipped</summary>
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    [MaxLength(500)] public string? Note { get; set; }
    public int? DecidedByUserId { get; set; }
    [MaxLength(150)] public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>سهمیه‌ی سالانه‌ی یک نوع مرخصی برای یک کاربر — Category: Annual|Sick|Unpaid|Maternity</summary>
public class HrLeaveBalance
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Year { get; set; }
    [MaxLength(20)] public string Category { get; set; } = "Annual";
    public double TotalDays { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>درخواست اضافه‌کاری — Type: Normal|Holiday|Night (نوعِ درخواستی؛ محاسبه‌ی قطعی با موتور قوانین است)</summary>
public class HrOvertimeRequest
{
    public int Id { get; set; }
    [MaxLength(30)] public string Number { get; set; } = "";
    public int RequesterUserId { get; set; }
    [MaxLength(150)] public string RequesterName { get; set; } = "";
    public DateTime WorkDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int Minutes { get; set; }
    [MaxLength(20)] public string Type { get; set; } = "Normal";
    [MaxLength(500)] public string? Reason { get; set; }
    /// <summary>Pending|Approved|Rejected</summary>
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    public int? DecidedByUserId { get; set; }
    [MaxLength(150)] public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>روستِر شیفت: شیفتِ یک کاربر در یک روز مشخص (اولویت بالاتر از شیفت ثابت کاربر) — برای شیفت چرخشی</summary>
public class HrShiftRoster
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime Date { get; set; }
    public int ShiftGroupId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>تردد خامِ ایمپورت‌شده از دستگاه حضوروغیاب — Direction: In|Out|Auto</summary>
public class HrDevicePunch
{
    public int Id { get; set; }
    [MaxLength(50)] public string DeviceCode { get; set; } = "";
    [MaxLength(50)] public string UserCode { get; set; } = "";
    public DateTime PunchTime { get; set; }
    [MaxLength(10)] public string Direction { get; set; } = "Auto";
    public int? MappedUserId { get; set; }
    public int? AppliedRecordId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>نگاشت کد پرسنلی دستگاه به کاربر سیستم</summary>
public class HrDeviceUserMap
{
    public int Id { get; set; }
    [MaxLength(50)] public string? DeviceCode { get; set; }
    [MaxLength(50)] public string UserCode { get; set; } = "";
    public int SystemUserId { get; set; }
}

/// <summary>قوانین محاسباتی زمان‌بندی (کلید/مقدار) — شب‌کاری، ضرایب، حق‌ماموریت، سهمیه‌ها</summary>
public class HrTimeRule
{
    public int Id { get; set; }
    [MaxLength(60)] public string Key { get; set; } = "";
    [MaxLength(200)] public string Value { get; set; } = "";
    [MaxLength(150)] public string? Title { get; set; }
}

/// <summary>
/// افزونه‌ی اختصاصی ماژول زمان‌بندی روی درخواست مرخصی/ماموریت (کلید: شناسه درخواست) —
/// جدول LeaveRequest دست‌نخورده می‌ماند و همه‌ی داده‌ی جدید این‌جا ذخیره می‌شود.
/// Category: Annual=استحقاقی | Sick=استعلاجی | Unpaid=بدون حقوق | Maternity=زایمان
/// </summary>
public class HrLeaveExtra
{
    [Key] public int LeaveRequestId { get; set; }
    [MaxLength(20)] public string Category { get; set; } = "Annual";
    /// <summary>نوع ماموریت: Inner=داخل شهر | Outer=خارج شهر (فقط برای ماموریت)</summary>
    [MaxLength(10)] public string? MissionKind { get; set; }
    /// <summary>حق ماموریت محاسبه‌شده (ریال)</summary>
    public decimal AllowanceAmount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// افزونه‌ی روزانه‌ی ماژول زمان‌بندی (تفکیک اضافه‌کاری + مختصات ورود/خروج) —
/// جداول Attendance* دست‌نخورده می‌مانند؛ محاسبه فقط-خواندنی از روی آن‌هاست.
/// </summary>
public class HrDayExtra
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime WorkDate { get; set; }
    /// <summary>تفکیک اضافه‌کاری (دقیقه): عادی</summary>
    public int OtNormalMinutes { get; set; }
    /// <summary>تفکیک اضافه‌کاری (دقیقه): تعطیل‌کاری</summary>
    public int OtHolidayMinutes { get; set; }
    /// <summary>تفکیک اضافه‌کاری (دقیقه): شب‌کاری</summary>
    public int OtNightMinutes { get; set; }
    public double? EnterLat { get; set; }
    public double? EnterLng { get; set; }
    public double? ExitLat { get; set; }
    public double? ExitLng { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// پیوند کاربر ورود (Users) به پرونده پرسنلی (HrEmployees) — متعلق به ماژول جدید.
/// (فیلد SystemUserId پرونده به جدول دفتر پرسنلی SystemUsers اشاره می‌کند و دست نمی‌خورد.)
/// </summary>
public class HrUserLink
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int EmployeeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>دوره آزمایشی استخدام — ماده ۱۱ قانون کار (۱ ماه مشاغل ساده / ۳ ماه تخصصی‌وماهر)</summary>
public class HrProbation
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime StartDate { get; set; }
    /// <summary>مدت به ماه: 1 | 3</summary>
    public int Months { get; set; } = 3;
    /// <summary>Simple=ساده | Skilled=ماهر/تخصصی</summary>
    [MaxLength(10)] public string SkillLevel { get; set; } = "Skilled";
    public DateTime EndDate { get; set; }
    /// <summary>Active=در جریان | Passed=قبول | Failed=مردود (فسخ)</summary>
    [MaxLength(10)] public string Status { get; set; } = "Active";
    [MaxLength(500)] public string? ResultNote { get; set; }
    [MaxLength(150)] public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>حداقل مزد مصوب سالانه شورای عالی کار (نسخه‌بندی‌شده با تاریخ اجرا)</summary>
public class HrMinWage
{
    [Key] public int Year { get; set; }
    public decimal MonthlyWage { get; set; }
    public decimal DailyWage { get; set; }
    public DateTime EffectiveDate { get; set; }
    [MaxLength(300)] public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>مرخصی شیردهی مادران — ماده ۷۸ (روزانه ۲ ساعت تا ۲ سالگی فرزند، با مزد)</summary>
public class HrNursingBreak
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime ChildBirthDate { get; set; }
    /// <summary>ساعت ارفاق روزانه</summary>
    public double DailyHours { get; set; } = 2;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>تأیید اضافه‌کار محاسباتی یک روز — فقط مقادیر Approved وارد حقوق می‌شود. Source: Auto=از تردد | Manual=از درخواست دستی</summary>
public class HrOtApproval
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime WorkDate { get; set; }
    public int ReqNormalMin { get; set; }
    public int ReqHolidayMin { get; set; }
    public int ReqNightMin { get; set; }
    public int ApprNormalMin { get; set; }
    public int ApprHolidayMin { get; set; }
    public int ApprNightMin { get; set; }
    [MaxLength(10)] public string Source { get; set; } = "Auto";
    public int? OvertimeRequestId { get; set; }
    /// <summary>Pending|Approved|Rejected</summary>
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    public int? DecidedByUserId { get; set; }
    [MaxLength(150)] public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>بستن ماه کارکرد (سال/ماه شمسی) — پس از بستن، تأیید/رد اضافه‌کار و اعمال تردد در آن ماه مجاز نیست</summary>
public class HrAttendClose
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public bool IsClosed { get; set; }
    [MaxLength(150)] public string? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}
