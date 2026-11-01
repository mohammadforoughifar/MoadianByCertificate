namespace Inventory.Shared.Dtos;

// ==================== FaLms (§۱۲ آموزش و توسعه) ====================

public class FaLmsCourseDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Kind { get; set; }
    public string? Description { get; set; }
    public double DurationHours { get; set; }
    public double CostPerPerson { get; set; }
    public int? MaxSeats { get; set; }
    public string? TrainerName { get; set; }
    public int? InstructorId { get; set; }
    public string? Location { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Status { get; set; }
    public bool HasExam { get; set; }
    public double PassScore { get; set; }
    public bool IsActive { get; set; }
    public int EnrolledCount { get; set; }
    public int SessionsCount { get; set; }
}

public class FaLmsCourseSaveDto
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Kind { get; set; }
    public string? Description { get; set; }
    public double DurationHours { get; set; }
    public double CostPerPerson { get; set; }
    public int? MaxSeats { get; set; }
    public string? TrainerName { get; set; }
    public int? InstructorId { get; set; }
    public string? Location { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Status { get; set; } = 1;
    public bool HasExam { get; set; } = true;
    public double PassScore { get; set; } = 60;
    public bool IsActive { get; set; } = true;
}

public class FaLmsNeedDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public int Year { get; set; }
    public int Source { get; set; }
    public int? PerfPeriodId { get; set; }
    public string? PerfPeriodTitle { get; set; }
    public int? PerfKpiId { get; set; }
    public string? PerfKpiTitle { get; set; }
    public double? PerfScore { get; set; }
    public string SkillTitle { get; set; } = "";
    public int Priority { get; set; }
    public int Status { get; set; }
    public int? LinkedCourseId { get; set; }
    public string? LinkedCourseTitle { get; set; }
    public string? Note { get; set; }
    public string? RequestedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
}

public class FaLmsNeedSaveDto
{
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public int Source { get; set; } = 3;
    public int? PerfPeriodId { get; set; }
    public int? PerfKpiId { get; set; }
    public string SkillTitle { get; set; } = "";
    public int Priority { get; set; } = 1;
    public int? LinkedCourseId { get; set; }
    public string? Note { get; set; }
}

public class FaLmsEnrollmentDto
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string? CourseTitle { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public int Status { get; set; }
    public DateTime EnrolledAt { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public double? FinalScore { get; set; }
    public bool? Passed { get; set; }
    public double? AttendancePercent { get; set; }
}

public class FaLmsSessionDto
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public DateTime SessionDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string? Topic { get; set; }
    public string? Location { get; set; }
    public int PresentCount { get; set; }
}

public class FaLmsSessionSaveDto
{
    public int CourseId { get; set; }
    public DateTime SessionDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string? Topic { get; set; }
    public string? Location { get; set; }
}

public class FaLmsAttendanceDto
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public bool Present { get; set; }
    public string? Note { get; set; }
}

public class FaLmsExamDto
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string? CourseTitle { get; set; }
    public string Title { get; set; } = "";
    public DateTime? ExamDate { get; set; }
    public int? DurationMinutes { get; set; }
    public int MaxAttempts { get; set; }
    public bool IsActive { get; set; }
    public int QuestionsCount { get; set; }
    public double TotalScore { get; set; }
}

public class FaLmsExamSaveDto
{
    public int CourseId { get; set; }
    public string Title { get; set; } = "";
    public DateTime? ExamDate { get; set; }
    public int? DurationMinutes { get; set; }
    public int MaxAttempts { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public class FaLmsQuestionDto
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public string Text { get; set; } = "";
    public string? OptA { get; set; }
    public string? OptB { get; set; }
    public string? OptC { get; set; }
    public string? OptD { get; set; }
    public int CorrectIndex { get; set; }
    public double Score { get; set; }
    public int Type { get; set; }
    public int SortOrder { get; set; }
}

public class FaLmsQuestionSaveDto
{
    public int ExamId { get; set; }
    public string Text { get; set; } = "";
    public string? OptA { get; set; }
    public string? OptB { get; set; }
    public string? OptC { get; set; }
    public string? OptD { get; set; }
    public int CorrectIndex { get; set; }
    public double Score { get; set; } = 1;
    public int Type { get; set; }
    public int SortOrder { get; set; }
}

public class FaLmsAttemptDto
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? Answers { get; set; }
    public double? Score { get; set; }
    public bool? Passed { get; set; }
    public int AttemptNo { get; set; }
    public bool NeedsGrading { get; set; }
}

public class FaLmsCertificateDto
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string? CourseTitle { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string CertNo { get; set; } = "";
    public DateTime IssueDate { get; set; }
    public double? Score { get; set; }
    public string? VerifyCode { get; set; }
}

public class FaLmsBudgetDto
{
    public int Id { get; set; }
    public int Year { get; set; }
    public double Amount { get; set; }
    public double SpentAmount { get; set; }
    public string? Note { get; set; }
}

public class FaLmsBudgetSaveDto
{
    public int Year { get; set; }
    public double Amount { get; set; }
    public string? Note { get; set; }
}

// ---------- ثبت‌نام ----------
public class FaLmsEnrollSaveDto
{
    public int CourseId { get; set; }
    public int? EmployeeId { get; set; } // خالی = خود کاربر
}

public class FaLmsNeedDecideDto
{
    public int Status { get; set; }
    public int? LinkedCourseId { get; set; }
}

// ---------- حضور ----------
public class FaLmsAttendanceItemDto
{
    public int EmployeeId { get; set; }
    public bool Present { get; set; }
    public string? Note { get; set; }
}

public class FaLmsAttendanceSaveDto
{
    public int SessionId { get; set; }
    public List<FaLmsAttendanceItemDto> Items { get; set; } = new();
}

// ---------- آزمون (شرکت‌کننده پاسخ صحیح را نمی‌بیند) ----------
public class FaLmsPlayQuestionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public string? OptA { get; set; }
    public string? OptB { get; set; }
    public string? OptC { get; set; }
    public string? OptD { get; set; }
    public double Score { get; set; }
    public int Type { get; set; }
    public int SortOrder { get; set; }
}

public class FaLmsExamPlayDto
{
    public int AttemptId { get; set; }
    public int ExamId { get; set; }
    public string Title { get; set; } = "";
    public int? DurationMinutes { get; set; }
    public DateTime StartedAt { get; set; }
    public int AttemptNo { get; set; }
    public int MaxAttempts { get; set; }
    public List<FaLmsPlayQuestionDto> Questions { get; set; } = new();
}

public class FaLmsAnswerDto
{
    public int QuestionId { get; set; }
    public int Selected { get; set; }
}

public class FaLmsSubmitDto
{
    public int AttemptId { get; set; }
    public List<FaLmsAnswerDto> Answers { get; set; } = new();
    public List<FaLmsTextDto> TextAnswers { get; set; } = new();
}

public class FaLmsTextDto
{
    public int QuestionId { get; set; }
    public string? Text { get; set; }
}

// ---------- تقویم / نیازسنجی از عملکرد ----------
public class FaLmsCalendarItemDto
{
    public DateTime Date { get; set; }
    public int Kind { get; set; } // 0=دوره 1=جلسه 2=آزمون
    public string Title { get; set; } = "";
    public int CourseId { get; set; }
    public string? CourseTitle { get; set; }
    public int RefId { get; set; }
}

public class FaLmsWeakScoreDto
{
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public int PeriodId { get; set; }
    public string? PeriodTitle { get; set; }
    public int KpiId { get; set; }
    public string? KpiTitle { get; set; }
    public double Score { get; set; }
    public double MaxScore { get; set; }
}

public class FaLmsSuggestDto
{
    public int PeriodId { get; set; }
    public double Threshold { get; set; } = 60;
    public int Year { get; set; }
}

public class FaLmsPerfPeriodDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public string Status { get; set; } = "";
}

// ---------- گواهی ----------
public class FaLmsIssueDto
{
    public int CourseId { get; set; }
    public int EmployeeId { get; set; }
}

public class FaLmsVerifyResultDto
{
    public bool Found { get; set; }
    public string? CertNo { get; set; }
    public string? CourseTitle { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime? IssueDate { get; set; }
    public double? Score { get; set; }
}

// ---------- گزارش‌ها ----------
public class FaLmsCourseReportDto
{
    public int CourseId { get; set; }
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";
    public int Status { get; set; }
    public int EnrolledCount { get; set; }
    public int ApprovedCount { get; set; }
    public int SessionsCount { get; set; }
    public double? AvgScore { get; set; }
    public int PassCount { get; set; }
    public double PassRate { get; set; }
    public double? AvgAttendance { get; set; }
    public int IssuedCerts { get; set; }
}

public class FaLmsEmployeeRowDto
{
    public int CourseId { get; set; }
    public string? CourseTitle { get; set; }
    public int Year { get; set; }
    public double Hours { get; set; }
    public int Status { get; set; }
    public double? Score { get; set; }
    public bool? Passed { get; set; }
    public double? Attendance { get; set; }
    public bool HasCert { get; set; }
}

public class FaLmsEmployeeReportDto
{
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public int CoursesCount { get; set; }
    public double HoursTotal { get; set; }
    public double? AvgScore { get; set; }
    public int CertsCount { get; set; }
    public double? AvgAttendance { get; set; }
    public List<FaLmsEmployeeRowDto> Rows { get; set; } = new();
}

public class FaLmsBudgetReportRowDto
{
    public int CourseId { get; set; }
    public string? CourseTitle { get; set; }
    public int ApprovedCount { get; set; }
    public double CostPerPerson { get; set; }
    public double TotalCost { get; set; }
}

public class FaLmsBudgetReportDto
{
    public int Year { get; set; }
    public double BudgetAmount { get; set; }
    public double SpentAmount { get; set; }
    public double Remaining => BudgetAmount - SpentAmount;
    public List<FaLmsBudgetReportRowDto> Rows { get; set; } = new();
}

public class FaLmsDashboardDto
{
    public int Year { get; set; }
    public int OpenCourses { get; set; }
    public int RunningCourses { get; set; }
    public int FinishedThisYear { get; set; }
    public int PendingNeeds { get; set; }
    public int PendingEnrolls { get; set; }
    public int UpcomingSessions { get; set; }
    public double BudgetAmount { get; set; }
    public double SpentAmount { get; set; }
    public List<FaLmsCertificateDto> RecentCerts { get; set; } = new();
}

// ---------- بانک سؤال مشترک (§۵) ----------
public class FaLmsBankDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public int QuestionsCount { get; set; }
}

public class FaLmsBankSaveDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
}

public class FaLmsBankQuestionDto
{
    public int Id { get; set; }
    public int BankId { get; set; }
    public string Text { get; set; } = "";
    public string? OptA { get; set; }
    public string? OptB { get; set; }
    public string? OptC { get; set; }
    public string? OptD { get; set; }
    public int CorrectIndex { get; set; }
    public double Score { get; set; }
    public int Type { get; set; }
    public int SortOrder { get; set; }
}

public class FaLmsBankQuestionSaveDto
{
    public int BankId { get; set; }
    public string Text { get; set; } = "";
    public string? OptA { get; set; }
    public string? OptB { get; set; }
    public string? OptC { get; set; }
    public string? OptD { get; set; }
    public int CorrectIndex { get; set; }
    public double Score { get; set; } = 1;
    public int Type { get; set; }
    public int SortOrder { get; set; }
}

public class FaLmsCopyFromBankDto
{
    public List<int> BankQuestionIds { get; set; } = new();
}

// ---------- تصحیح دستی تشریحی (§۵) ----------
public class FaLmsGradeItemDto
{
    public int QuestionId { get; set; }
    public double? ManualScore { get; set; }
}

public class FaLmsGradeSaveDto
{
    public List<FaLmsGradeItemDto> Items { get; set; } = new();
}

public class FaLmsTextAnswerDto
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public int QuestionId { get; set; }
    public string QuestionText { get; set; } = "";
    public double QuestionScore { get; set; }
    public string? AnswerText { get; set; }
    public double? ManualScore { get; set; }
}

public class FaLmsCopyResultDto
{
    public int Count { get; set; }
}

// ==================== مدرس‌ها / نظرسنجی / تداخل (§۴ آموزش) ====================

public class FaLmsInstructorDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>0=داخلی، 1=خارجی</summary>
    public int Type { get; set; }
    public string? Field { get; set; }
    public string? Phone { get; set; }
    public double FeePerHour { get; set; }
    public int? EmployeeId { get; set; }
    public bool IsActive { get; set; }
    public int CourseCount { get; set; }
    /// <summary>جمع حق‌التدریس دوره‌ها (ساعت × نرخ)</summary>
    public double TotalFee { get; set; }
}

public class FaLmsInstructorSaveDto
{
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public string? Field { get; set; }
    public string? Phone { get; set; }
    public double FeePerHour { get; set; }
    public int? EmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FaLmsSurveyQuestionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public double AvgScore { get; set; }
    public int AnswerCount { get; set; }
    public int? MyScore { get; set; }
}

public class FaLmsSurveyQuestionSaveDto
{
    public int CourseId { get; set; }
    public string Text { get; set; } = "";
}

public class FaLmsSurveyAnswerSaveDto
{
    public int QuestionId { get; set; }
    public int EmployeeId { get; set; }
    public int Score { get; set; }
}

public class FaLmsSurveyResultDto
{
    public int QuestionCount { get; set; }
    public int RespondentCount { get; set; }
    /// <summary>میانگین کلی اثربخشی از ۵</summary>
    public double OverallAvg { get; set; }
    public List<FaLmsSurveyQuestionDto> Questions { get; set; } = new();
}

public class FaLmsConflictDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTime SessionDate { get; set; }
    public string? Topic { get; set; }
    /// <summary>Leave | Mission | Shift</summary>
    public string Kind { get; set; } = "";
    public string Detail { get; set; } = "";
}

/// <summary>پاسخ‌نامه در انتظار تصحیح مدرس</summary>
public class FaLmsPendingGradeDto
{
    public int AttemptId { get; set; }
    public int CourseId { get; set; }
    public string CourseTitle { get; set; } = "";
    public int ExamId { get; set; }
    public string ExamTitle { get; set; } = "";
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int QuestionCount { get; set; }
    public int TextCount { get; set; }
    public int? InstructorId { get; set; }
    public string? InstructorName { get; set; }
}

/// <summary>کارتابل تصحیح + شناسه مدرسی کاربر جاری (اگر مدرس باشد)</summary>
public class FaLmsGradingInboxDto
{
    public int? MyInstructorId { get; set; }
    public List<FaLmsPendingGradeDto> Items { get; set; } = new();
}
