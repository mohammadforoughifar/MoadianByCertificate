using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// ================== آموزش و توسعه فروغ آریا (FaLms) — §۱۲، ماژول جدید و مستقل ==================
/// نیازسنجی (متصل به نمرات HrPerf)، دوره‌ها، ثبت‌نام، جلسات و حضور، آزمون تستی، گواهی داخلی، بودجه.
/// جدول‌ها با پیشوند FaLms یکتا هستند. اسکیما با FaLmsSchemaV1 (Ensure) ساخته می‌شود (نه EF Migration).
/// از جدول‌های ارزیابی عملکرد فقط خواندن می‌شود (بدون تغییر).
/// </summary>

public enum FaLmsCourseKind { Internal = 0, External = 1, Online = 2, Workshop = 3 }
public enum FaLmsCourseStatus { Draft = 0, Open = 1, Running = 2, Finished = 3, Cancelled = 4 }
public enum FaLmsNeedSource { Performance = 0, OrgGoal = 1, Manager = 2, Self = 3 }
public enum FaLmsNeedStatus { New = 0, Approved = 1, Converted = 2, Rejected = 3 }
public enum FaLmsEnrollStatus { Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3 }

/// <summary>دوره آموزشی (داخلی/خارجی/آنلاین/کارگاه) — تقویم سالانه از روی تاریخ شروع ساخته می‌شود</summary>
public class FaLmsCourse
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string Code { get; set; } = "";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public FaLmsCourseKind Kind { get; set; } = FaLmsCourseKind.Internal;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>مدت دوره (ساعت)</summary>
    public double DurationHours { get; set; }

    /// <summary>هزینه سرانه (ریال)</summary>
    public double CostPerPerson { get; set; }

    public int? MaxSeats { get; set; }

    [MaxLength(150)]
    public string? TrainerName { get; set; }

    /// <summary>مدرس (ارجاع به پرونده مدرس؛ TrainerName نمایشی/سازگار قدیم می‌ماند)</summary>
    public int? InstructorId { get; set; }

    /// <summary>محل برگزاری / لینک کلاس آنلاین</summary>
    [MaxLength(200)]
    public string? Location { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public FaLmsCourseStatus Status { get; set; } = FaLmsCourseStatus.Draft;

    public bool HasExam { get; set; } = true;

    /// <summary>نمره قبولی آزمون (از ۱۰۰)</summary>
    public double PassScore { get; set; } = 60;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>نیاز آموزشی پرسنل — با اتصال اختیاری به KPI ضعیف در ارزیابی عملکرد</summary>
public class FaLmsNeed
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    /// <summary>سال شمسی نیازسنجی</summary>
    public int Year { get; set; }

    public FaLmsNeedSource Source { get; set; } = FaLmsNeedSource.Self;

    public int? PerfPeriodId { get; set; }

    public int? PerfKpiId { get; set; }

    /// <summary>اسنپ‌شات نمره KPI در لحظه ثبت نیاز</summary>
    public double? PerfScore { get; set; }

    [MaxLength(200)]
    public string SkillTitle { get; set; } = "";

    /// <summary>اولویت: 0=کم، 1=متوسط، 2=زیاد</summary>
    public int Priority { get; set; } = 1;

    public FaLmsNeedStatus Status { get; set; } = FaLmsNeedStatus.New;

    public int? LinkedCourseId { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public int? RequestedByUserId { get; set; }

    [MaxLength(150)]
    public string? RequestedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [MaxLength(150)]
    public string? DecidedByName { get; set; }

    public DateTime? DecidedAt { get; set; }
}

/// <summary>ثبت‌نام پرسنل در دوره</summary>
public class FaLmsEnrollment
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    public int EmployeeId { get; set; }

    public FaLmsEnrollStatus Status { get; set; } = FaLmsEnrollStatus.Pending;

    public DateTime EnrolledAt { get; set; } = DateTime.Now;

    [MaxLength(150)]
    public string? DecidedByName { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>نمره نهایی آزمون (از ۱۰۰)</summary>
    public double? FinalScore { get; set; }

    public bool? Passed { get; set; }

    /// <summary>درصد حضور در جلسات (در پایان دوره محاسبه می‌شود)</summary>
    public double? AttendancePercent { get; set; }
}

/// <summary>جلسه دوره (برای حضوروغیاب آموزشی)</summary>
public class FaLmsSession
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    public DateTime SessionDate { get; set; } = DateTime.Today;

    public TimeSpan? StartTime { get; set; }

    public TimeSpan? EndTime { get; set; }

    [MaxLength(200)]
    public string? Topic { get; set; }

    /// <summary>سالن/محل برگزاری این جلسه (اگر خالی: محل دوره)</summary>
    [MaxLength(150)]
    public string? Location { get; set; }
}

/// <summary>حضور یک نفر در یک جلسه</summary>
public class FaLmsAttendance
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    public int EmployeeId { get; set; }

    public bool Present { get; set; }

    [MaxLength(200)]
    public string? Note { get; set; }
}

/// <summary>آزمون پایان دوره (تستی/تشریحی، چندتلاشه)</summary>
public class FaLmsExam
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public DateTime? ExamDate { get; set; }

    /// <summary>مدت آزمون (دقیقه) — خالی یعنی بدون محدودیت</summary>
    public int? DurationMinutes { get; set; }

    /// <summary>سقف تعداد تلاش (پیش‌فرض ۱)</summary>
    public int MaxAttempts { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>سؤال آزمون (تستی چهارگزینه‌ای یا تشریحی)</summary>
public class FaLmsQuestion
{
    public int Id { get; set; }

    public int ExamId { get; set; }

    [MaxLength(1000)]
    public string Text { get; set; } = "";

    [MaxLength(500)]
    public string? OptA { get; set; }

    [MaxLength(500)]
    public string? OptB { get; set; }

    [MaxLength(500)]
    public string? OptC { get; set; }

    [MaxLength(500)]
    public string? OptD { get; set; }

    /// <summary>گزینه صحیح: 0 تا 3</summary>
    public int CorrectIndex { get; set; }

    /// <summary>نوع سؤال: 0 تستی، 1 تشریحی</summary>
    public int Type { get; set; }

    public double Score { get; set; } = 1;

    public int SortOrder { get; set; }
}

/// <summary>تلاش یک نفر در آزمون — پاسخ‌های تستی CSV اندیس گزینه‌ها، تشریحی در جدول جدا</summary>
public class FaLmsAttempt
{
    public int Id { get; set; }

    public int ExamId { get; set; }

    public int EmployeeId { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.Now;

    public DateTime? SubmittedAt { get; set; }

    [MaxLength(1000)]
    public string? Answers { get; set; }

    /// <summary>شماره تلاش (از ۱)</summary>
    public int AttemptNo { get; set; } = 1;

    /// <summary>نمره از ۱۰۰ (خودکار + دستی؛ خالی یعنی در انتظار تصحیح تشریحی)</summary>
    public double? Score { get; set; }

    public bool? Passed { get; set; }
}

/// <summary>گواهی داخلی پایان دوره</summary>
public class FaLmsCertificate
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(30)]
    public string CertNo { get; set; } = "";

    public DateTime IssueDate { get; set; } = DateTime.Today;

    public double? Score { get; set; }

    /// <summary>کد رهگیری اصالت گواهی</summary>
    [MaxLength(20)]
    public string? VerifyCode { get; set; }
}

/// <summary>بودجه سالانه آموزش (یک ردیف برای هر سال شمسی)</summary>
public class FaLmsBudget
{
    public int Id { get; set; }

    public int Year { get; set; }

    /// <summary>مبلغ مصوب (ریال)</summary>
    public double Amount { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>بانک سؤال مشترک (§۵)</summary>
public class FaLmsBank
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>سؤال بانک مشترک — با کپی وارد آزمون می‌شود</summary>
public class FaLmsBankQuestion
{
    public int Id { get; set; }

    public int BankId { get; set; }

    [MaxLength(1000)]
    public string Text { get; set; } = "";

    [MaxLength(500)]
    public string? OptA { get; set; }

    [MaxLength(500)]
    public string? OptB { get; set; }

    [MaxLength(500)]
    public string? OptC { get; set; }

    [MaxLength(500)]
    public string? OptD { get; set; }

    public int CorrectIndex { get; set; }

    public double Score { get; set; } = 1;

    /// <summary>نوع سؤال: 0 تستی، 1 تشریحی</summary>
    public int Type { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>پاسخ تشریحی یک تلاش + نمره دستی مصحح</summary>
public class FaLmsTextAnswer
{
    public int Id { get; set; }

    public int AttemptId { get; set; }

    public int QuestionId { get; set; }

    public string? AnswerText { get; set; }

    public double? ManualScore { get; set; }
}

/// <summary>نوع مدرس: داخلی | خارجی</summary>
public enum FaLmsInstructorType { Internal = 0, External = 1 }

/// <summary>پرونده مدرس (داخلی/خارجی + نرخ حق‌التدریس ساعتی)</summary>
public class FaLmsInstructor
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = "";

    public FaLmsInstructorType Type { get; set; } = FaLmsInstructorType.Internal;

    /// <summary>حوزه تخصص</summary>
    [MaxLength(150)]
    public string? Field { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    /// <summary>نرخ حق‌التدریس ساعتی (ریال)</summary>
    public double FeePerHour { get; set; }

    /// <summary>پیوند به پرسنل (فقط مدرس داخلی)</summary>
    public int? EmployeeId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>سؤال نظرسنجی اثربخشی یک دوره (نمره ۱ تا ۵)</summary>
public class FaLmsSurveyQuestion
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    [MaxLength(300)]
    public string Text { get; set; } = "";

    public int SortOrder { get; set; }
}

/// <summary>پاسخ یک فراگیر به نظرسنجی دوره</summary>
public class FaLmsSurveyAnswer
{
    public int Id { get; set; }

    public int QuestionId { get; set; }

    public int EmployeeId { get; set; }

    /// <summary>نمره ۱ تا ۵</summary>
    public int Score { get; set; } = 5;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
