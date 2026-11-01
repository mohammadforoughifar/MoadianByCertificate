using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.FaCom;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Inventory.Shared;
using Inventory.Api.Services.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.FaLms;

/// <summary>
/// ================== آموزش و توسعه فروغ آریا (FaLms) — §۱۲، ماژول مستقل ==================
/// نیازسنجی (با اتصال خواندنی به ارزیابی عملکرد همین ماژول — HrAppraisals)، دوره‌ها و تقویم سالانه، ثبت‌نام با تأیید و ظرفیت،
/// جلسات و حضور، آزمون تستی تک‌تلاشه با تصحیح خودکار، گواهی داخلی با کد رهگیری، بودجه و اثربخشی.
/// اعلان رویدادها از مسیر FaCom (سیستمی + پوش) انجام می‌شود. از HrAppraisals فقط خوانده می‌شود (ارزیابی «منابع انسانی فروغ آریا» — HrPerf* — دیگر خوانده نمی‌شود).
/// </summary>
public interface IFaLmsService
{
    // دوره‌ها
    Task<List<FaLmsCourseDto>> ListCoursesAsync(int? year, int? status, int? kind, string? q, Paging.Request? pagination = null);
    Task<FaLmsCourseDto?> GetCourseAsync(int id);
    Task<FaLmsCourseDto> SaveCourseAsync(int? id, FaLmsCourseSaveDto dto);
    Task DeleteCourseAsync(int id);
    Task<FaLmsCourseDto> SetCourseStatusAsync(int id, int status, int byUserId);
    Task<List<FaLmsCalendarItemDto>> GetCalendarAsync(int year, Paging.Request? pagination = null);
    Task<FaLmsCourseReportDto?> GetCourseReportAsync(int id);
    // نیازسنجی
    Task<List<FaLmsNeedDto>> ListNeedsAsync(int? year, int? status, int? source, int? employeeId, Paging.Request? pagination = null);
    Task<List<FaLmsNeedDto>> MyNeedsAsync(int userId, Paging.Request? pagination = null);
    Task<FaLmsNeedDto> SaveNeedAsync(int? id, FaLmsNeedSaveDto dto, int byUserId, string byName);
    Task<FaLmsNeedDto> DecideNeedAsync(int id, int status, int? linkedCourseId, string byName);
    Task DeleteNeedAsync(int id);
    Task<List<FaLmsWeakScoreDto>> WeakScoresAsync(int periodId, double threshold, Paging.Request? pagination = null);
    Task<int> SuggestFromPerfAsync(int periodId, double threshold, int year, int byUserId, string byName);
    // ثبت‌نام
    Task<List<FaLmsEnrollmentDto>> ListEnrollmentsAsync(int? courseId, int? status, Paging.Request? pagination = null);
    Task<List<FaLmsEnrollmentDto>> MyEnrollmentsAsync(int userId, Paging.Request? pagination = null);
    Task<FaLmsEnrollmentDto> EnrollAsync(int courseId, int? employeeId, int userId, string userName);
    Task<FaLmsEnrollmentDto> DecideEnrollmentAsync(int id, bool approve, int byUserId, string byName);
    Task CancelEnrollmentAsync(int id, int userId, bool isHr);
    Task DeleteEnrollmentAsync(int id);
    // جلسات و حضور
    Task<List<FaLmsSessionDto>> ListSessionsAsync(int courseId, Paging.Request? pagination = null);
    Task<FaLmsSessionDto> SaveSessionAsync(int? id, FaLmsSessionSaveDto dto);
    Task DeleteSessionAsync(int id);
    Task<List<FaLmsAttendanceDto>> GetAttendanceAsync(int sessionId, Paging.Request? pagination = null);
    Task SaveAttendanceAsync(FaLmsAttendanceSaveDto dto);
    // آزمون
    Task<List<FaLmsExamDto>> ListExamsAsync(int courseId, Paging.Request? pagination = null);
    Task<FaLmsExamDto> SaveExamAsync(int? id, FaLmsExamSaveDto dto, int byUserId);
    Task DeleteExamAsync(int id);
    Task<List<FaLmsQuestionDto>> ListQuestionsAsync(int examId, Paging.Request? pagination = null);
    Task<FaLmsQuestionDto> SaveQuestionAsync(int? id, FaLmsQuestionSaveDto dto);
    Task DeleteQuestionAsync(int id);
    Task<FaLmsExamPlayDto> StartAttemptAsync(int examId, int userId);
    Task<FaLmsAttemptDto> SubmitAttemptAsync(int examId, FaLmsSubmitDto dto, int userId);
    Task<List<FaLmsAttemptDto>> ListAttemptsAsync(int examId, Paging.Request? pagination = null);
    Task<FaLmsGradingInboxDto> GradingInboxAsync(int userId, Paging.Request? pagination = null);
    Task<List<FaLmsBankDto>> ListBanksAsync(Paging.Request? pagination = null);
    Task<FaLmsBankDto> SaveBankAsync(int? id, FaLmsBankSaveDto dto);
    Task DeleteBankAsync(int id);
    Task<List<FaLmsBankQuestionDto>> ListBankQuestionsAsync(int bankId, Paging.Request? pagination = null);
    Task<FaLmsBankQuestionDto> SaveBankQuestionAsync(int? id, FaLmsBankQuestionSaveDto dto);
    Task DeleteBankQuestionAsync(int id);
    Task<int> CopyFromBankAsync(int examId, List<int> bankQuestionIds);
    Task<List<FaLmsTextAnswerDto>> ListTextAnswersAsync(int attemptId, Paging.Request? pagination = null);
    Task<FaLmsAttemptDto> GradeAttemptAsync(int attemptId, FaLmsGradeSaveDto dto, int byUserId);
    // گواهی
    Task<List<FaLmsCertificateDto>> ListCertificatesAsync(int? courseId, int? employeeId, int? year, Paging.Request? pagination = null);
    Task<List<FaLmsCertificateDto>> MyCertificatesAsync(int userId, Paging.Request? pagination = null);
    Task<FaLmsCertificateDto> IssueCertificateAsync(int courseId, int employeeId, int byUserId, string byName);
    Task<int> IssueMissingAsync(int courseId, int byUserId, string byName);
    Task<FaLmsVerifyResultDto> VerifyAsync(string certNo, string code);
    Task<byte[]> CertificatePdfAsync(int id);
    // مدرس‌ها
    Task<List<FaLmsInstructorDto>> ListInstructorsAsync(bool? onlyActive, Paging.Request? pagination = null);
    Task<FaLmsInstructorDto> SaveInstructorAsync(int? id, FaLmsInstructorSaveDto dto);
    Task DeleteInstructorAsync(int id);
    // نظرسنجی اثربخشی
    Task<List<FaLmsSurveyQuestionDto>> ListSurveyQuestionsAsync(int courseId, int? employeeId, Paging.Request? pagination = null);
    Task<FaLmsSurveyQuestionDto> SaveSurveyQuestionAsync(FaLmsSurveyQuestionSaveDto dto);
    Task DeleteSurveyQuestionAsync(int id);
    Task SaveSurveyAnswerAsync(FaLmsSurveyAnswerSaveDto dto);
    Task<FaLmsSurveyResultDto> SurveyResultsAsync(int courseId, Paging.Request? pagination = null);
    // تداخل‌یابی
    Task<List<FaLmsConflictDto>> CheckConflictsAsync(int courseId, int? employeeId, Paging.Request? pagination = null);
    Task DeleteCertificateAsync(int id);
    // بودجه
    Task<List<FaLmsBudgetDto>> ListBudgetsAsync(Paging.Request? pagination = null);
    Task<FaLmsBudgetDto> SaveBudgetAsync(int? id, FaLmsBudgetSaveDto dto);
    Task DeleteBudgetAsync(int id);
    Task<FaLmsBudgetReportDto> BudgetReportAsync(int year, Paging.Request? pagination = null);
    // گزارش‌ها
    Task<FaLmsDashboardDto> GetDashboardAsync(int year, Paging.Request? pagination = null);
    Task<FaLmsEmployeeReportDto?> EmployeeReportAsync(int employeeId, Paging.Request? pagination = null);
    Task<FaLmsEmployeeReportDto?> MyReportAsync(int userId, Paging.Request? pagination = null);
}

public class FaLmsService : IFaLmsService
{
    private readonly AppDbContext _db;
    private readonly IFaComService _com;
    private static readonly PersianCalendar Pc = new();

    public FaLmsService(AppDbContext db, IFaComService com) { _db = db; _com = com; }

    private static int Jy(DateTime d) { try { return Pc.GetYear(d); } catch { return d.Year; } }

    // Inclusive end avoids overflow at the final year supported by PersianCalendar.
    private static (DateTime Start, DateTime End) YearRange(int year)
    {
        var lastYear = Pc.GetYear(Pc.MaxSupportedDateTime);
        if (year < 1 || year > lastYear) return (DateTime.MaxValue, DateTime.MinValue);
        return (Pc.ToDateTime(year, 1, 1, 0, 0, 0, 0), year == lastYear
            ? Pc.MaxSupportedDateTime : Pc.ToDateTime(year + 1, 1, 1, 0, 0, 0, 0).AddTicks(-1));
    }

    private Task<Dictionary<int, string>> EmpNamesAsync()
        => _db.HrEmployees.ToDictionaryAsync(e => e.Id, e => (e.FirstName + " " + e.LastName).Trim());

    private Task<HrEmployee?> MyEmployeeAsync(int userId)
        => _db.HrEmployees.FirstOrDefaultAsync(e => e.SystemUserId == userId);

    private async Task<List<int>> UserIdsAsync(IEnumerable<int> employeeIds)
    {
        var ids = employeeIds.Distinct().ToList();
        return await _db.HrEmployees.Where(e => ids.Contains(e.Id) && e.SystemUserId != null)
            .Select(e => e.SystemUserId!.Value).ToListAsync();
    }

    private Task NotifyAsync(IEnumerable<int> userIds, string title, string? body, string? link, int byUserId)
        => _com.NotifyManyAsync(userIds, title, body, link, email: false, push: true, sms: false, byUserId);

    // ==================== دوره‌ها ====================

    public async Task<List<FaLmsCourseDto>> ListCoursesAsync(int? year, int? status, int? kind, string? q, Paging.Request? pagination = null)
    {
        var query = _db.FaLmsCourses.AsQueryable();
        if (status != null) query = query.Where(c => (int)c.Status == status.Value);
        if (kind != null) query = query.Where(c => (int)c.Kind == kind.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(c => c.Title.Contains(s) || c.Code.Contains(s));
        }
        if (year != null)
        {
            var start = Pc.ToDateTime(year.Value, 1, 1, 0, 0, 0, 0);
            var end = Pc.ToDateTime(year.Value + 1, 1, 1, 0, 0, 0, 0);
            query = query.Where(c => c.StartDate >= start && c.StartDate < end);
        }
        var list = await query.OrderByDescending(c => c.Id).ToPageListAsync(pagination);
        var ids = list.Select(c => c.Id).ToList();
        var ecounts = ids.Count == 0 ? new Dictionary<int, int>()
            : await _db.FaLmsEnrollments.Where(e => ids.Contains(e.CourseId))
                .GroupBy(e => e.CourseId).ToDictionaryAsync(g => g.Key, g => g.Count());
        var scounts = ids.Count == 0 ? new Dictionary<int, int>()
            : await _db.FaLmsSessions.Where(s => ids.Contains(s.CourseId))
                .GroupBy(s => s.CourseId).ToDictionaryAsync(g => g.Key, g => g.Count());
        return list.Select(c => ToDto(c,
            ecounts.TryGetValue(c.Id, out var e) ? e : 0,
            scounts.TryGetValue(c.Id, out var s2) ? s2 : 0)).ToList();
    }

    public async Task<FaLmsCourseDto?> GetCourseAsync(int id)
    {
        var c = await _db.FaLmsCourses.FindAsync(id);
        if (c == null) return null;
        var e = await _db.FaLmsEnrollments.CountAsync(x => x.CourseId == id);
        var s = await _db.FaLmsSessions.CountAsync(x => x.CourseId == id);
        return ToDto(c, e, s);
    }

    public async Task<FaLmsCourseDto> SaveCourseAsync(int? id, FaLmsCourseSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new InvalidOperationException("کد دوره الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان دوره الزامی است.");
        if (dto.StartDate != null && dto.EndDate != null && dto.EndDate < dto.StartDate)
            throw new InvalidOperationException("تاریخ پایان نباید قبل از تاریخ شروع باشد.");
        if (dto.PassScore < 0 || dto.PassScore > 100) throw new InvalidOperationException("نمره قبولی باید بین ۰ تا ۱۰۰ باشد.");
        if (dto.DurationHours < 0 || dto.CostPerPerson < 0) throw new InvalidOperationException("مدت و هزینه سرانه نباید منفی باشد.");
        var code = dto.Code.Trim();
        if (await _db.FaLmsCourses.AnyAsync(c => c.Code == code && c.Id != (id ?? 0)))
            throw new InvalidOperationException("کد دوره تکراری است.");
        FaLmsCourse c;
        if (id == null)
        {
            c = new FaLmsCourse { CreatedAt = DateTime.Now };
            _db.FaLmsCourses.Add(c);
        }
        else
        {
            c = await _db.FaLmsCourses.FindAsync(id.Value)
                ?? throw new InvalidOperationException("دوره یافت نشد.");
        }
        c.Code = code; c.Title = dto.Title.Trim();
        c.Kind = (FaLmsCourseKind)dto.Kind; c.Description = dto.Description;
        c.DurationHours = dto.DurationHours; c.CostPerPerson = dto.CostPerPerson;
        c.MaxSeats = dto.MaxSeats; c.TrainerName = dto.TrainerName; c.InstructorId = dto.InstructorId; c.Location = dto.Location;
        c.StartDate = dto.StartDate; c.EndDate = dto.EndDate;
        c.Status = (FaLmsCourseStatus)dto.Status; c.HasExam = dto.HasExam;
        c.PassScore = dto.PassScore; c.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return (await GetCourseAsync(c.Id))!;
    }

    public async Task DeleteCourseAsync(int id)
    {
        var c = await _db.FaLmsCourses.FindAsync(id)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (await _db.FaLmsEnrollments.AnyAsync(e => e.CourseId == id))
            throw new InvalidOperationException("این دوره ثبت‌نام دارد و قابل حذف نیست.");
        if (await _db.FaLmsSessions.AnyAsync(s => s.CourseId == id))
            throw new InvalidOperationException("این دوره جلسه دارد و قابل حذف نیست.");
        if (await _db.FaLmsExams.AnyAsync(e => e.CourseId == id))
            throw new InvalidOperationException("این دوره آزمون دارد و قابل حذف نیست.");
        _db.FaLmsCourses.Remove(c);
        await _db.SaveChangesAsync();
    }

    public async Task<FaLmsCourseDto> SetCourseStatusAsync(int id, int status, int byUserId)
    {
        var c = await _db.FaLmsCourses.FindAsync(id)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        c.Status = (FaLmsCourseStatus)status;
        await _db.SaveChangesAsync();
        var approved = await _db.FaLmsEnrollments
            .Where(e => e.CourseId == id && e.Status == FaLmsEnrollStatus.Approved).ToListAsync();
        // اتمام دوره: محاسبه درصد حضور هر نفر
        if (c.Status == FaLmsCourseStatus.Finished)
        {
            var sids = await _db.FaLmsSessions.Where(s => s.CourseId == id).Select(s => s.Id).ToListAsync();
            foreach (var en in approved)
            {
                double pct = 0;
                if (sids.Count > 0)
                {
                    var present = await _db.FaLmsAttendances.CountAsync(a =>
                        sids.Contains(a.SessionId) && a.EmployeeId == en.EmployeeId && a.Present);
                    pct = Math.Round(present * 100.0 / sids.Count, 1);
                }
                en.AttendancePercent = pct;
            }
            await _db.SaveChangesAsync();
        }
        // آغاز دوره: اطلاع به ثبت‌نام‌شدگان
        if (c.Status == FaLmsCourseStatus.Running && approved.Count > 0)
            await NotifyAsync(await UserIdsAsync(approved.Select(e => e.EmployeeId)),
                $"دوره «{c.Title}» آغاز شد.", null, "fa-lms/my", byUserId);
        return (await GetCourseAsync(id))!;
    }

    public async Task<List<FaLmsCalendarItemDto>> GetCalendarAsync(int year, Paging.Request? pagination = null)
    {
        var (start, end) = YearRange(year);
        // UNION ALL keys avoids set operations after client-only DTO formatting.
        var keys = await _db.FaLmsCourses.Where(c => c.StartDate >= start && c.StartDate <= end)
            .Select(c => new { Date = c.StartDate!.Value, Kind = 0, c.Id })
            .Concat(_db.FaLmsSessions.Where(x => x.SessionDate >= start && x.SessionDate <= end)
                .Select(x => new { Date = x.SessionDate, Kind = 1, x.Id }))
            .Concat(_db.FaLmsExams.Where(x => x.ExamDate >= start && x.ExamDate <= end)
                .Select(x => new { Date = x.ExamDate!.Value, Kind = 2, x.Id }))
            .OrderBy(x => x.Date).ThenBy(x => x.Kind).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var courseIds = keys.Where(x => x.Kind == 0).Select(x => x.Id).ToList();
        var sessionIds = keys.Where(x => x.Kind == 1).Select(x => x.Id).ToList();
        var examIds = keys.Where(x => x.Kind == 2).Select(x => x.Id).ToList();
        var sessions = await _db.FaLmsSessions.AsNoTracking().Where(x => sessionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var exams = await _db.FaLmsExams.AsNoTracking().Where(x => examIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        courseIds.AddRange(sessions.Values.Select(x => x.CourseId));
        courseIds.AddRange(exams.Values.Select(x => x.CourseId));
        var courses = await _db.FaLmsCourses.AsNoTracking().Where(x => courseIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title);
        return keys.Select(x =>
        {
            var courseId = x.Kind == 0 ? x.Id : x.Kind == 1 ? sessions[x.Id].CourseId : exams[x.Id].CourseId;
            courses.TryGetValue(courseId, out var courseTitle);
            var title = x.Kind == 0 ? $"شروع دوره: {courseTitle}" : x.Kind == 2 ? $"آزمون: {exams[x.Id].Title}"
                : string.IsNullOrWhiteSpace(sessions[x.Id].Topic) ? "جلسه آموزشی" : $"جلسه: {sessions[x.Id].Topic}";
            return new FaLmsCalendarItemDto { Date = x.Date, Kind = x.Kind, Title = title,
                CourseId = courseId, CourseTitle = courseTitle, RefId = x.Id };
        }).ToList();
    }

    public async Task<FaLmsCourseReportDto?> GetCourseReportAsync(int id)
    {
        var c = await _db.FaLmsCourses.FindAsync(id);
        if (c == null) return null;
        var enrolls = await _db.FaLmsEnrollments.Where(e => e.CourseId == id).ToListAsync();
        var approved = enrolls.Where(e => e.Status == FaLmsEnrollStatus.Approved).ToList();
        var graded = approved.Where(e => e.FinalScore != null).ToList();
        var pass = graded.Count(e => e.Passed == true);
        var atts = approved.Where(e => e.AttendancePercent != null).Select(e => e.AttendancePercent!.Value).ToList();
        return new FaLmsCourseReportDto
        {
            CourseId = id, Title = c.Title, Code = c.Code, Status = (int)c.Status,
            EnrolledCount = enrolls.Count, ApprovedCount = approved.Count,
            SessionsCount = await _db.FaLmsSessions.CountAsync(s => s.CourseId == id),
            AvgScore = graded.Count > 0 ? Math.Round(graded.Average(e => e.FinalScore!.Value), 1) : null,
            PassCount = pass,
            PassRate = graded.Count > 0 ? Math.Round(pass * 100.0 / graded.Count, 1) : 0,
            AvgAttendance = atts.Count > 0 ? Math.Round(atts.Average(), 1) : null,
            IssuedCerts = await _db.FaLmsCertificates.CountAsync(x => x.CourseId == id)
        };
    }

    private static FaLmsCourseDto ToDto(FaLmsCourse c, int enrolled, int sessions) => new()
    {
        Id = c.Id, Code = c.Code, Title = c.Title, Kind = (int)c.Kind, Description = c.Description,
        DurationHours = c.DurationHours, CostPerPerson = c.CostPerPerson, MaxSeats = c.MaxSeats,
        TrainerName = c.TrainerName, InstructorId = c.InstructorId, Location = c.Location, StartDate = c.StartDate, EndDate = c.EndDate,
        Status = (int)c.Status, HasExam = c.HasExam, PassScore = c.PassScore, IsActive = c.IsActive,
        EnrolledCount = enrolled, SessionsCount = sessions
    };

    // ==================== نیازسنجی ====================

    public async Task<List<FaLmsNeedDto>> ListNeedsAsync(int? year, int? status, int? source, int? employeeId, Paging.Request? pagination = null)
    {
        var q = _db.FaLmsNeeds.AsQueryable();
        if (year != null) q = q.Where(n => n.Year == year.Value);
        if (status != null) q = q.Where(n => (int)n.Status == status.Value);
        if (source != null) q = q.Where(n => (int)n.Source == source.Value);
        if (employeeId != null) q = q.Where(n => n.EmployeeId == employeeId.Value);
        var list = await q.OrderByDescending(n => n.Id).ToPageListAsync(pagination);
        return await MapNeedsAsync(list);
    }

    public async Task<List<FaLmsNeedDto>> MyNeedsAsync(int userId, Paging.Request? pagination = null)
    {
        var me = await MyEmployeeAsync(userId);
        if (me == null) return new();
        var list = await _db.FaLmsNeeds.Where(n => n.EmployeeId == me.Id)
            .OrderByDescending(n => n.Id).ToPageListAsync(pagination);
        return await MapNeedsAsync(list);
    }

    private async Task<List<FaLmsNeedDto>> MapNeedsAsync(List<FaLmsNeed> list)
    {
        var names = await EmpNamesAsync();
        // ارزیابی عملکردِ خودِ منابع انسانی اصلی (HrAppraisals) — جدا از ارزیابی «منابع انسانی فروغ آریا»
        var periods = await _db.HrAppraisals.ToDictionaryAsync(p => p.Id, p => p.Title);
        var kpis = await _db.HrAppraisalKpis.ToDictionaryAsync(k => k.Id, k => k.Title);
        var courses = await _db.FaLmsCourses.ToDictionaryAsync(c => c.Id, c => c.Title);
        return list.Select(n => new FaLmsNeedDto
        {
            Id = n.Id, EmployeeId = n.EmployeeId,
            EmployeeName = names.TryGetValue(n.EmployeeId, out var nm) ? nm : null,
            Year = n.Year, Source = (int)n.Source,
            PerfPeriodId = n.PerfPeriodId,
            PerfPeriodTitle = n.PerfPeriodId != null && periods.TryGetValue(n.PerfPeriodId.Value, out var pt) ? pt : null,
            PerfKpiId = n.PerfKpiId,
            PerfKpiTitle = n.PerfKpiId != null && kpis.TryGetValue(n.PerfKpiId.Value, out var kt) ? kt : null,
            PerfScore = n.PerfScore, SkillTitle = n.SkillTitle, Priority = n.Priority,
            Status = (int)n.Status, LinkedCourseId = n.LinkedCourseId,
            LinkedCourseTitle = n.LinkedCourseId != null && courses.TryGetValue(n.LinkedCourseId.Value, out var ct) ? ct : null,
            Note = n.Note, RequestedByName = n.RequestedByName, CreatedAt = n.CreatedAt,
            DecidedByName = n.DecidedByName, DecidedAt = n.DecidedAt
        }).ToList();
    }

    public async Task<FaLmsNeedDto> SaveNeedAsync(int? id, FaLmsNeedSaveDto dto, int byUserId, string byName)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId))
            throw new InvalidOperationException("پرسنل انتخاب‌شده معتبر نیست.");
        if (dto.Year < 1300 || dto.Year > 1500) throw new InvalidOperationException("سال نیازسنجی معتبر نیست.");
        if (string.IsNullOrWhiteSpace(dto.SkillTitle)) throw new InvalidOperationException("عنوان مهارت الزامی است.");
        FaLmsNeed n;
        if (id == null)
        {
            n = new FaLmsNeed { CreatedAt = DateTime.Now, RequestedByUserId = byUserId, RequestedByName = byName };
            _db.FaLmsNeeds.Add(n);
        }
        else
        {
            n = await _db.FaLmsNeeds.FindAsync(id.Value)
                ?? throw new InvalidOperationException("نیاز آموزشی یافت نشد.");
            if (n.Status == FaLmsNeedStatus.Converted)
                throw new InvalidOperationException("نیاز تبدیل‌شده به دوره قابل ویرایش نیست.");
        }
        n.EmployeeId = dto.EmployeeId; n.Year = dto.Year;
        n.Source = (FaLmsNeedSource)dto.Source;
        n.PerfPeriodId = dto.PerfPeriodId; n.PerfKpiId = dto.PerfKpiId;
        n.SkillTitle = dto.SkillTitle.Trim();
        n.Priority = Math.Clamp(dto.Priority, 0, 2);
        n.LinkedCourseId = dto.LinkedCourseId; n.Note = dto.Note;
        // اسنپ‌شات نمره KPI در لحظه ثبت
        n.PerfScore = null;
        if (dto.PerfPeriodId != null && dto.PerfKpiId != null)
        {
            var s = await _db.HrAppraisalScores.FirstOrDefaultAsync(x =>
                x.AppraisalId == dto.PerfPeriodId.Value && x.EmployeeId == dto.EmployeeId && x.KpiId == dto.PerfKpiId.Value);
            n.PerfScore = s?.ManagerScore ?? s?.SelfScore;
        }
        await _db.SaveChangesAsync();
        return (await MapNeedsAsync(new List<FaLmsNeed> { n }))[0];
    }

    public async Task<FaLmsNeedDto> DecideNeedAsync(int id, int status, int? linkedCourseId, string byName)
    {
        if (status is < 1 or > 3) throw new InvalidOperationException("وضعیت تصمیم معتبر نیست.");
        var n = await _db.FaLmsNeeds.FindAsync(id)
            ?? throw new InvalidOperationException("نیاز آموزشی یافت نشد.");
        if (status == (int)FaLmsNeedStatus.Converted)
        {
            if (linkedCourseId == null || !await _db.FaLmsCourses.AnyAsync(c => c.Id == linkedCourseId.Value))
                throw new InvalidOperationException("برای تبدیل به دوره، انتخاب دوره الزامی است.");
            n.LinkedCourseId = linkedCourseId;
        }
        n.Status = (FaLmsNeedStatus)status;
        n.DecidedByName = byName; n.DecidedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        if (n.RequestedByUserId != null)
            await NotifyAsync(new[] { n.RequestedByUserId.Value },
                $"نیاز آموزشی «{n.SkillTitle}»: " + (status == 3 ? "رد شد." : status == 2 ? "به دوره تبدیل شد." : "تأیید شد."),
                null, "fa-lms/needs", 0);
        return (await MapNeedsAsync(new List<FaLmsNeed> { n }))[0];
    }

    public async Task DeleteNeedAsync(int id)
    {
        var n = await _db.FaLmsNeeds.FindAsync(id)
            ?? throw new InvalidOperationException("نیاز آموزشی یافت نشد.");
        if (n.Status is FaLmsNeedStatus.Approved or FaLmsNeedStatus.Converted)
            throw new InvalidOperationException("نیاز تأیید/تبدیل‌شده قابل حذف نیست.");
        _db.FaLmsNeeds.Remove(n);
        await _db.SaveChangesAsync();
    }

    public async Task<List<FaLmsWeakScoreDto>> WeakScoresAsync(int periodId, double threshold, Paging.Request? pagination = null)
    {
        var period = await _db.HrAppraisals.FindAsync(periodId)
            ?? throw new InvalidOperationException("دوره ارزیابی یافت نشد.");
        // نمرهٔ مدیر ملاک است؛ اگر ثبت نشده باشد خودارزیابی
        var scores = await _db.HrAppraisalScores
            .Where(s => s.AppraisalId == periodId && (s.ManagerScore ?? s.SelfScore) != null
                        && (s.ManagerScore ?? s.SelfScore) < threshold)
            .OrderBy(s => s.ManagerScore ?? s.SelfScore).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var kpis = await _db.HrAppraisalKpis.Where(k => k.AppraisalId == periodId)
            .ToDictionaryAsync(k => k.Id);
        var names = await EmpNamesAsync();
        return scores.Select(s => new FaLmsWeakScoreDto
        {
            EmployeeId = s.EmployeeId,
            EmployeeName = names.TryGetValue(s.EmployeeId, out var nm) ? nm : null,
            PeriodId = periodId, PeriodTitle = period.Title,
            KpiId = s.KpiId,
            KpiTitle = kpis.TryGetValue(s.KpiId, out var k) ? k.Title : null,
            Score = s.ManagerScore ?? s.SelfScore ?? 0, MaxScore = kpis.TryGetValue(s.KpiId, out var k2) ? k2.MaxScore : 100
        }).ToList();
    }

    public async Task<int> SuggestFromPerfAsync(int periodId, double threshold, int year, int byUserId, string byName)
    {
        if (year < 1300 || year > 1500) throw new InvalidOperationException("سال نیازسنجی معتبر نیست.");
        var weak = await WeakScoresAsync(periodId, threshold);
        var existing = await _db.FaLmsNeeds
            .Where(n => n.Year == year && n.PerfPeriodId == periodId && n.Status != FaLmsNeedStatus.Rejected)
            .Select(n => n.EmployeeId + ":" + n.PerfKpiId).ToListAsync();
        var set = new HashSet<string>(existing);
        int created = 0;
        foreach (var w in weak)
        {
            if (set.Contains(w.EmployeeId + ":" + w.KpiId)) continue;
            _db.FaLmsNeeds.Add(new FaLmsNeed
            {
                EmployeeId = w.EmployeeId, Year = year, Source = FaLmsNeedSource.Performance,
                PerfPeriodId = periodId, PerfKpiId = w.KpiId, PerfScore = w.Score,
                SkillTitle = w.KpiTitle ?? "مهارت مرتبط با ارزیابی",
                Priority = w.Score < threshold - 20 ? 2 : 1,
                Status = FaLmsNeedStatus.New,
                RequestedByUserId = byUserId, RequestedByName = byName,
                CreatedAt = DateTime.Now
            });
            set.Add(w.EmployeeId + ":" + w.KpiId);
            created++;
        }
        await _db.SaveChangesAsync();
        return created;
    }

    // ==================== ثبت‌نام ====================

    public async Task<List<FaLmsEnrollmentDto>> ListEnrollmentsAsync(int? courseId, int? status, Paging.Request? pagination = null)
    {
        var q = _db.FaLmsEnrollments.AsQueryable();
        if (courseId != null) q = q.Where(e => e.CourseId == courseId.Value);
        if (status != null) q = q.Where(e => (int)e.Status == status.Value);
        var list = await q.OrderByDescending(e => e.Id).ToPageListAsync(pagination);
        return await MapEnrollmentsAsync(list);
    }

    public async Task<List<FaLmsEnrollmentDto>> MyEnrollmentsAsync(int userId, Paging.Request? pagination = null)
    {
        var me = await MyEmployeeAsync(userId);
        if (me == null) return new();
        var list = await _db.FaLmsEnrollments.Where(e => e.EmployeeId == me.Id)
            .OrderByDescending(e => e.Id).ToPageListAsync(pagination);
        return await MapEnrollmentsAsync(list);
    }

    private async Task<List<FaLmsEnrollmentDto>> MapEnrollmentsAsync(List<FaLmsEnrollment> list)
    {
        var names = await EmpNamesAsync();
        var courses = await _db.FaLmsCourses.ToDictionaryAsync(c => c.Id, c => c.Title);
        return list.Select(e => new FaLmsEnrollmentDto
        {
            Id = e.Id, CourseId = e.CourseId,
            CourseTitle = courses.TryGetValue(e.CourseId, out var t) ? t : null,
            EmployeeId = e.EmployeeId,
            EmployeeName = names.TryGetValue(e.EmployeeId, out var nm) ? nm : null,
            Status = (int)e.Status, EnrolledAt = e.EnrolledAt,
            DecidedByName = e.DecidedByName, DecidedAt = e.DecidedAt,
            FinalScore = e.FinalScore, Passed = e.Passed, AttendancePercent = e.AttendancePercent
        }).ToList();
    }

    public async Task<FaLmsEnrollmentDto> EnrollAsync(int courseId, int? employeeId, int userId, string userName)
    {
        var c = await _db.FaLmsCourses.FindAsync(courseId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (!c.IsActive || (c.Status != FaLmsCourseStatus.Open && c.Status != FaLmsCourseStatus.Running))
            throw new InvalidOperationException("این دوره در حال حاضر پذیرش ندارد.");
        int empId;
        if (employeeId != null)
        {
            if (!await _db.HrEmployees.AnyAsync(e => e.Id == employeeId.Value))
                throw new InvalidOperationException("پرسنل انتخاب‌شده معتبر نیست.");
            empId = employeeId.Value;
        }
        else
        {
            var me = await MyEmployeeAsync(userId)
                ?? throw new InvalidOperationException("حساب کاربری شما به پرونده پرسنلی متصل نیست.");
            empId = me.Id;
        }
        if (await _db.FaLmsEnrollments.AnyAsync(e => e.CourseId == courseId && e.EmployeeId == empId
            && (e.Status == FaLmsEnrollStatus.Pending || e.Status == FaLmsEnrollStatus.Approved)))
            throw new InvalidOperationException("این نفر قبلاً در این دوره ثبت‌نام شده است.");
        var en = new FaLmsEnrollment
        {
            CourseId = courseId, EmployeeId = empId, Status = FaLmsEnrollStatus.Pending,
            EnrolledAt = DateTime.Now
        };
        _db.FaLmsEnrollments.Add(en);
        await _db.SaveChangesAsync();
        return (await MapEnrollmentsAsync(new List<FaLmsEnrollment> { en }))[0];
    }

    public async Task<FaLmsEnrollmentDto> DecideEnrollmentAsync(int id, bool approve, int byUserId, string byName)
    {
        var en = await _db.FaLmsEnrollments.FindAsync(id)
            ?? throw new InvalidOperationException("ثبت‌نام یافت نشد.");
        var c = await _db.FaLmsCourses.FindAsync(en.CourseId);
        if (approve)
        {
            if (c == null || !c.IsActive || (c.Status != FaLmsCourseStatus.Open && c.Status != FaLmsCourseStatus.Running))
                throw new InvalidOperationException("دوره فعال نیست و تأیید ثبت‌نام ممکن نیست.");
            if (c.MaxSeats != null)
            {
                var approved = await _db.FaLmsEnrollments.CountAsync(e =>
                    e.CourseId == en.CourseId && e.Status == FaLmsEnrollStatus.Approved && e.Id != en.Id);
                if (approved >= c.MaxSeats.Value)
                    throw new InvalidOperationException("ظرفیت دوره تکمیل است.");
            }
            en.Status = FaLmsEnrollStatus.Approved;
        }
        else
        {
            en.Status = FaLmsEnrollStatus.Rejected;
        }
        en.DecidedByName = byName; en.DecidedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        var uids = await UserIdsAsync(new[] { en.EmployeeId });
        if (uids.Count > 0)
            await NotifyAsync(uids,
                approve ? $"ثبت‌نام شما در دوره «{c?.Title}» تأیید شد." : $"ثبت‌نام شما در دوره «{c?.Title}» رد شد.",
                null, "fa-lms/my", byUserId);
        return (await MapEnrollmentsAsync(new List<FaLmsEnrollment> { en }))[0];
    }

    public async Task CancelEnrollmentAsync(int id, int userId, bool isHr)
    {
        var en = await _db.FaLmsEnrollments.FindAsync(id)
            ?? throw new InvalidOperationException("ثبت‌نام یافت نشد.");
        if (!isHr)
        {
            var me = await MyEmployeeAsync(userId);
            if (me == null || me.Id != en.EmployeeId)
                throw new InvalidOperationException("این ثبت‌نام متعلق به شما نیست.");
            if (en.Status != FaLmsEnrollStatus.Pending)
                throw new InvalidOperationException("فقط درخواست در انتظار قابل انصراف است.");
        }
        en.Status = FaLmsEnrollStatus.Cancelled;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteEnrollmentAsync(int id)
    {
        var en = await _db.FaLmsEnrollments.FindAsync(id)
            ?? throw new InvalidOperationException("ثبت‌نام یافت نشد.");
        if (await _db.FaLmsCertificates.AnyAsync(x => x.CourseId == en.CourseId && x.EmployeeId == en.EmployeeId))
            throw new InvalidOperationException("برای این ثبت‌نام گواهی صادر شده و قابل حذف نیست.");
        _db.FaLmsEnrollments.Remove(en);
        await _db.SaveChangesAsync();
    }

    // ==================== جلسات و حضور ====================

    public async Task<List<FaLmsSessionDto>> ListSessionsAsync(int courseId, Paging.Request? pagination = null)
    {
        var list = await _db.FaLmsSessions.Where(s => s.CourseId == courseId)
            .OrderBy(s => s.SessionDate).ThenBy(s => s.Id).ToPageListAsync(pagination);
        var ids = list.Select(s => s.Id).ToList();
        var counts = ids.Count == 0 ? new Dictionary<int, int>()
            : await _db.FaLmsAttendances.Where(a => ids.Contains(a.SessionId) && a.Present)
                .GroupBy(a => a.SessionId).ToDictionaryAsync(g => g.Key, g => g.Count());
        return list.Select(s => new FaLmsSessionDto
        {
            Id = s.Id, CourseId = s.CourseId, SessionDate = s.SessionDate,
            StartTime = s.StartTime, EndTime = s.EndTime, Topic = s.Topic, Location = s.Location,
            PresentCount = counts.TryGetValue(s.Id, out var n) ? n : 0
        }).ToList();
    }

    public async Task<FaLmsSessionDto> SaveSessionAsync(int? id, FaLmsSessionSaveDto dto)
    {
        if (!await _db.FaLmsCourses.AnyAsync(c => c.Id == dto.CourseId))
            throw new InvalidOperationException("دوره یافت نشد.");
        FaLmsSession s;
        if (id == null)
        {
            s = new FaLmsSession();
            _db.FaLmsSessions.Add(s);
        }
        else
        {
            s = await _db.FaLmsSessions.FindAsync(id.Value)
                ?? throw new InvalidOperationException("جلسه یافت نشد.");
        }
        s.CourseId = dto.CourseId; s.SessionDate = dto.SessionDate.Date;
        s.StartTime = dto.StartTime; s.EndTime = dto.EndTime; s.Topic = dto.Topic; s.Location = dto.Location?.Trim();
        await _db.SaveChangesAsync();
        var present = await _db.FaLmsAttendances.CountAsync(a => a.SessionId == s.Id && a.Present);
        return new FaLmsSessionDto
        {
            Id = s.Id, CourseId = s.CourseId, SessionDate = s.SessionDate,
            StartTime = s.StartTime, EndTime = s.EndTime, Topic = s.Topic, Location = s.Location, PresentCount = present
        };
    }

    public async Task DeleteSessionAsync(int id)
    {
        var s = await _db.FaLmsSessions.FindAsync(id)
            ?? throw new InvalidOperationException("جلسه یافت نشد.");
        var marks = await _db.FaLmsAttendances.Where(a => a.SessionId == id).ToListAsync();
        _db.FaLmsAttendances.RemoveRange(marks);
        _db.FaLmsSessions.Remove(s);
        await _db.SaveChangesAsync();
    }

    public async Task<List<FaLmsAttendanceDto>> GetAttendanceAsync(int sessionId, Paging.Request? pagination = null)
    {
        var s = await _db.FaLmsSessions.FindAsync(sessionId)
            ?? throw new InvalidOperationException("جلسه یافت نشد.");
        var empIds = await _db.FaLmsEnrollments
            .Where(e => e.CourseId == s.CourseId && e.Status == FaLmsEnrollStatus.Approved)
            .OrderBy(e => e.EmployeeId).ThenBy(e => e.Id)
            .Select(e => e.EmployeeId).ToPageListAsync(pagination);
        var marks = await _db.FaLmsAttendances.Where(a => a.SessionId == sessionId)
            .ToDictionaryAsync(a => a.EmployeeId);
        var names = await EmpNamesAsync();
        return empIds.Select(empId => new FaLmsAttendanceDto
        {
            Id = marks.TryGetValue(empId, out var m) ? m.Id : 0,
            SessionId = sessionId, EmployeeId = empId,
            EmployeeName = names.TryGetValue(empId, out var nm) ? nm : null,
            Present = marks.TryGetValue(empId, out var m2) && m2.Present,
            Note = marks.TryGetValue(empId, out var m3) ? m3.Note : null
        }).ToList();
    }

    public async Task SaveAttendanceAsync(FaLmsAttendanceSaveDto dto)
    {
        var s = await _db.FaLmsSessions.FindAsync(dto.SessionId)
            ?? throw new InvalidOperationException("جلسه یافت نشد.");
        var enrolled = await _db.FaLmsEnrollments
            .Where(e => e.CourseId == s.CourseId && e.Status == FaLmsEnrollStatus.Approved)
            .Select(e => e.EmployeeId).ToListAsync();
        var set = new HashSet<int>(enrolled);
        var names = await EmpNamesAsync();
        foreach (var item in dto.Items)
        {
            if (!set.Contains(item.EmployeeId))
                throw new InvalidOperationException(
                    $"«{(names.TryGetValue(item.EmployeeId, out var nm) ? nm : "نفر انتخاب‌شده")}» در این دوره ثبت‌نام تأییدشده ندارد.");
            var m = await _db.FaLmsAttendances.FirstOrDefaultAsync(a =>
                a.SessionId == dto.SessionId && a.EmployeeId == item.EmployeeId);
            if (m == null)
            {
                m = new FaLmsAttendance { SessionId = dto.SessionId, EmployeeId = item.EmployeeId };
                _db.FaLmsAttendances.Add(m);
            }
            m.Present = item.Present; m.Note = item.Note;
        }
        await _db.SaveChangesAsync();
    }

    // ==================== آزمون ====================

    public async Task<List<FaLmsExamDto>> ListExamsAsync(int courseId, Paging.Request? pagination = null)
    {
        var list = await _db.FaLmsExams.Where(e => e.CourseId == courseId)
            .OrderByDescending(e => e.Id).ToPageListAsync(pagination);
        var ids = list.Select(e => e.Id).ToList();
        var qs = ids.Count == 0 ? new List<FaLmsQuestion>()
            : await _db.FaLmsQuestions.Where(q => ids.Contains(q.ExamId)).ToListAsync();
        var titles = await _db.FaLmsCourses.Where(c => c.Id == courseId)
            .ToDictionaryAsync(c => c.Id, c => c.Title);
        return list.Select(e => new FaLmsExamDto
        {
            Id = e.Id, CourseId = e.CourseId,
            CourseTitle = titles.TryGetValue(e.CourseId, out var t) ? t : null,
            Title = e.Title, ExamDate = e.ExamDate, DurationMinutes = e.DurationMinutes,
            MaxAttempts = e.MaxAttempts < 1 ? 1 : e.MaxAttempts, IsActive = e.IsActive,
            QuestionsCount = qs.Count(q => q.ExamId == e.Id),
            TotalScore = qs.Where(q => q.ExamId == e.Id).Sum(q => q.Score)
        }).ToList();
    }

    public async Task<FaLmsExamDto> SaveExamAsync(int? id, FaLmsExamSaveDto dto, int byUserId)
    {
        var c = await _db.FaLmsCourses.FindAsync(dto.CourseId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان آزمون الزامی است.");
        bool isNew = id == null;
        FaLmsExam e;
        if (isNew)
        {
            e = new FaLmsExam { CreatedAt = DateTime.Now };
            _db.FaLmsExams.Add(e);
        }
        else
        {
            e = await _db.FaLmsExams.FindAsync(id!.Value)
                ?? throw new InvalidOperationException("آزمون یافت نشد.");
        }
        e.CourseId = dto.CourseId; e.Title = dto.Title.Trim();
        e.ExamDate = dto.ExamDate; e.DurationMinutes = dto.DurationMinutes; e.MaxAttempts = Math.Max(1, Math.Min(10, dto.MaxAttempts)); e.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        if (isNew)
        {
            var empIds = await _db.FaLmsEnrollments
                .Where(x => x.CourseId == dto.CourseId && x.Status == FaLmsEnrollStatus.Approved)
                .Select(x => x.EmployeeId).ToListAsync();
            var uids = await UserIdsAsync(empIds);
            if (uids.Count > 0)
                await NotifyAsync(uids, $"آزمون «{e.Title}» برای دوره «{c.Title}» فعال شد.", null, "fa-lms/my", byUserId);
        }
        return (await ListExamsAsync(dto.CourseId)).First(x => x.Id == e.Id);
    }

    public async Task DeleteExamAsync(int id)
    {
        var e = await _db.FaLmsExams.FindAsync(id)
            ?? throw new InvalidOperationException("آزمون یافت نشد.");
        if (await _db.FaLmsAttempts.AnyAsync(a => a.ExamId == id && a.SubmittedAt != null))
            throw new InvalidOperationException("این آزمون پاسخ‌نامه ثبت‌شده دارد و قابل حذف نیست.");
        var qs = await _db.FaLmsQuestions.Where(q => q.ExamId == id).ToListAsync();
        var atts = await _db.FaLmsAttempts.Where(a => a.ExamId == id).ToListAsync();
        var attIds = atts.Select(a => a.Id).ToList();
        _db.FaLmsTextAnswers.RemoveRange(_db.FaLmsTextAnswers.Where(x => attIds.Contains(x.AttemptId)));
        _db.FaLmsQuestions.RemoveRange(qs);
        _db.FaLmsAttempts.RemoveRange(atts);
        _db.FaLmsExams.Remove(e);
        await _db.SaveChangesAsync();
    }

    public async Task<List<FaLmsQuestionDto>> ListQuestionsAsync(int examId, Paging.Request? pagination = null)
        => (await _db.FaLmsQuestions.Where(q => q.ExamId == examId)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id).ToPageListAsync(pagination))
            .Select(q => new FaLmsQuestionDto
            {
                Id = q.Id, ExamId = q.ExamId, Text = q.Text, OptA = q.OptA, OptB = q.OptB,
                OptC = q.OptC, OptD = q.OptD, CorrectIndex = q.CorrectIndex, Score = q.Score, Type = q.Type, SortOrder = q.SortOrder
            }).ToList();

    public async Task<FaLmsQuestionDto> SaveQuestionAsync(int? id, FaLmsQuestionSaveDto dto)
    {
        if (!await _db.FaLmsExams.AnyAsync(e => e.Id == dto.ExamId))
            throw new InvalidOperationException("آزمون یافت نشد.");
        if (string.IsNullOrWhiteSpace(dto.Text)) throw new InvalidOperationException("متن سؤال الزامی است.");
        if (dto.Type is < 0 or > 1) throw new InvalidOperationException("نوع سؤال نامعتبر است.");
        if (dto.Type == 0 && dto.CorrectIndex is < 0 or > 3) throw new InvalidOperationException("گزینه صحیح باید بین ۱ تا ۴ باشد.");
        if (await _db.FaLmsAttempts.AnyAsync(a => a.ExamId == dto.ExamId && a.SubmittedAt != null))
            throw new InvalidOperationException("آزمون پاسخ‌نامه ثبت‌شده دارد؛ سؤال‌ها قابل تغییر نیستند.");
        FaLmsQuestion q;
        if (id == null)
        {
            q = new FaLmsQuestion();
            _db.FaLmsQuestions.Add(q);
        }
        else
        {
            q = await _db.FaLmsQuestions.FindAsync(id.Value)
                ?? throw new InvalidOperationException("سؤال یافت نشد.");
        }
        q.ExamId = dto.ExamId; q.Text = dto.Text.Trim();
        q.OptA = dto.OptA; q.OptB = dto.OptB; q.OptC = dto.OptC; q.OptD = dto.OptD;
        q.CorrectIndex = dto.CorrectIndex; q.Score = dto.Score; q.Type = dto.Type; q.SortOrder = dto.SortOrder;
        await _db.SaveChangesAsync();
        return (await ListQuestionsAsync(dto.ExamId)).First(x => x.Id == q.Id);
    }

    public async Task DeleteQuestionAsync(int id)
    {
        var q = await _db.FaLmsQuestions.FindAsync(id)
            ?? throw new InvalidOperationException("سؤال یافت نشد.");
        if (await _db.FaLmsAttempts.AnyAsync(a => a.ExamId == q.ExamId && a.SubmittedAt != null))
            throw new InvalidOperationException("آزمون پاسخ‌نامه ثبت‌شده دارد؛ سؤال قابل حذف نیست.");
        _db.FaLmsQuestions.Remove(q);
        await _db.SaveChangesAsync();
    }

    public async Task<FaLmsExamPlayDto> StartAttemptAsync(int examId, int userId)
    {
        var e = await _db.FaLmsExams.FindAsync(examId)
            ?? throw new InvalidOperationException("آزمون یافت نشد.");
        if (!e.IsActive) throw new InvalidOperationException("این آزمون فعال نیست.");
        var me = await MyEmployeeAsync(userId)
            ?? throw new InvalidOperationException("حساب کاربری شما به پرونده پرسنلی متصل نیست.");
        if (!await _db.FaLmsEnrollments.AnyAsync(x => x.CourseId == e.CourseId
            && x.EmployeeId == me.Id && x.Status == FaLmsEnrollStatus.Approved))
            throw new InvalidOperationException("ابتدا باید در این دوره ثبت‌نام و تأیید شده باشید.");
        var maxAtt = e.MaxAttempts < 1 ? 1 : e.MaxAttempts;
        var doneCount = await _db.FaLmsAttempts.CountAsync(a => a.ExamId == examId && a.EmployeeId == me.Id && a.SubmittedAt != null);
        if (doneCount >= maxAtt)
            throw new InvalidOperationException($"سقف تلاش این آزمون ({maxAtt} بار) به پایان رسیده است.");
        var att = await _db.FaLmsAttempts.FirstOrDefaultAsync(a => a.ExamId == examId && a.EmployeeId == me.Id && a.SubmittedAt == null);
        if (att == null)
        {
            att = new FaLmsAttempt { ExamId = examId, EmployeeId = me.Id, StartedAt = DateTime.Now, AttemptNo = doneCount + 1 };
            _db.FaLmsAttempts.Add(att);
            await _db.SaveChangesAsync();
        }
        var qs = await _db.FaLmsQuestions.Where(q => q.ExamId == examId)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id).ToListAsync();
        return new FaLmsExamPlayDto
        {
            AttemptId = att.Id, ExamId = examId, Title = e.Title,
            DurationMinutes = e.DurationMinutes, StartedAt = att.StartedAt,
            AttemptNo = att.AttemptNo, MaxAttempts = maxAtt,
            Questions = qs.Select(q => new FaLmsPlayQuestionDto
            {
                Id = q.Id, Text = q.Text, OptA = q.OptA, OptB = q.OptB,
                OptC = q.OptC, OptD = q.OptD, Score = q.Score, Type = q.Type, SortOrder = q.SortOrder
            }).ToList()
        };
    }

    public async Task<FaLmsAttemptDto> SubmitAttemptAsync(int examId, FaLmsSubmitDto dto, int userId)
    {
        var e = await _db.FaLmsExams.FindAsync(examId)
            ?? throw new InvalidOperationException("آزمون یافت نشد.");
        var me = await MyEmployeeAsync(userId)
            ?? throw new InvalidOperationException("حساب کاربری شما به پرونده پرسنلی متصل نیست.");
        var att = await _db.FaLmsAttempts.FindAsync(dto.AttemptId)
            ?? throw new InvalidOperationException("تلاش آزمون یافت نشد.");
        if (att.ExamId != examId || att.EmployeeId != me.Id)
            throw new InvalidOperationException("این پاسخ‌نامه متعلق به شما نیست.");
        if (att.SubmittedAt != null)
            throw new InvalidOperationException("این پاسخ‌نامه قبلاً ثبت شده است.");
        if (e.DurationMinutes != null && att.StartedAt.AddMinutes(e.DurationMinutes.Value) < DateTime.Now)
            throw new InvalidOperationException("مهلت پاسخ‌گویی به پایان رسیده است.");
        var qs = await _db.FaLmsQuestions.Where(q => q.ExamId == examId)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id).ToListAsync();
        var answers = dto.Answers.ToDictionary(a => a.QuestionId, a => a.Selected);
        var texts = dto.TextAnswers.ToDictionary(x => x.QuestionId, x => x.Text);
        double earned = 0, total = 0;
        var csv = new List<string>();
        var hasDesc = false;
        foreach (var q in qs)
        {
            total += q.Score;
            if (q.Type == 1)
            {
                hasDesc = true;
                csv.Add("T");
                texts.TryGetValue(q.Id, out var txt);
                _db.FaLmsTextAnswers.Add(new FaLmsTextAnswer { AttemptId = att.Id, QuestionId = q.Id, AnswerText = txt?.Trim() });
                continue;
            }
            answers.TryGetValue(q.Id, out var sel);
            csv.Add(sel.ToString());
            if (sel == q.CorrectIndex) earned += q.Score;
        }
        att.Answers = string.Join(",", csv);
        att.SubmittedAt = DateTime.Now;
        double? score = null;
        bool? passed = null;
        var c = await _db.FaLmsCourses.FindAsync(e.CourseId);
        if (!hasDesc)
        {
            score = total > 0 ? Math.Round(earned * 100.0 / total, 1) : 0;
            passed = score >= (c?.PassScore ?? 60);
            att.Score = score; att.Passed = passed;
            await ApplyEnrollmentResultAsync(e.CourseId, me.Id, score.Value, passed.Value);
        }
        await _db.SaveChangesAsync();
        var uids = await UserIdsAsync(new[] { me.Id });
        if (uids.Count > 0)
            await NotifyAsync(uids,
                hasDesc ? $"پاسخ‌نامه «{e.Title}» ثبت شد و در انتظار تصحیح است."
                        : $"نتیجه آزمون «{e.Title}»: {(passed!.Value ? "قبول" : "مردود")} — نمره {score} از ۱۰۰.",
                null, "fa-lms/my", userId);
        var names = await EmpNamesAsync();
        return new FaLmsAttemptDto
        {
            Id = att.Id, ExamId = att.ExamId, EmployeeId = att.EmployeeId,
            EmployeeName = names.TryGetValue(att.EmployeeId, out var nm) ? nm : null,
            StartedAt = att.StartedAt, SubmittedAt = att.SubmittedAt,
            Answers = att.Answers, Score = att.Score, Passed = att.Passed,
            AttemptNo = att.AttemptNo, NeedsGrading = hasDesc && att.Score == null
        };
    }

    public async Task<List<FaLmsAttemptDto>> ListAttemptsAsync(int examId, Paging.Request? pagination = null)
    {
        var list = await _db.FaLmsAttempts.Where(a => a.ExamId == examId)
            .OrderByDescending(a => a.Id).ToPageListAsync(pagination);
        var names = await EmpNamesAsync();
        var hasDesc = await _db.FaLmsQuestions.AnyAsync(q => q.ExamId == examId && q.Type == 1);
        return list.Select(a => new FaLmsAttemptDto
        {
            Id = a.Id, ExamId = a.ExamId, EmployeeId = a.EmployeeId,
            EmployeeName = names.TryGetValue(a.EmployeeId, out var nm) ? nm : null,
            StartedAt = a.StartedAt, SubmittedAt = a.SubmittedAt,
            Answers = a.Answers, Score = a.Score, Passed = a.Passed,
            AttemptNo = a.AttemptNo, NeedsGrading = hasDesc && a.SubmittedAt != null && a.Score == null
        }).ToList();
    }

    private async Task ApplyEnrollmentResultAsync(int courseId, int employeeId, double score, bool passed)
    {
        var en = await _db.FaLmsEnrollments.FirstOrDefaultAsync(x =>
            x.CourseId == courseId && x.EmployeeId == employeeId && x.Status == FaLmsEnrollStatus.Approved);
        if (en == null) return;
        if (en.FinalScore == null || score > en.FinalScore.Value) en.FinalScore = score;
        if (passed) en.Passed = true;
    }

    public async Task<FaLmsGradingInboxDto> GradingInboxAsync(int userId, Paging.Request? pagination = null)
    {
        var result = new FaLmsGradingInboxDto();
        var empId = await _db.HrEmployees.Where(e => e.SystemUserId == userId).Select(e => (int?)e.Id).FirstOrDefaultAsync();
        if (empId is > 0)
            result.MyInstructorId = await _db.FaLmsInstructors.Where(i => i.EmployeeId == empId && i.IsActive).Select(i => (int?)i.Id).FirstOrDefaultAsync();
        var atts = await _db.FaLmsAttempts.AsNoTracking()
            .Where(a => a.SubmittedAt != null && a.Score == null)
            .OrderBy(a => a.SubmittedAt).ThenBy(a => a.Id).ToPageListAsync(pagination);
        if (atts.Count == 0) return result;
        var examIds = atts.Select(a => a.ExamId).Distinct().ToList();
        var exams = await _db.FaLmsExams.AsNoTracking().Where(e => examIds.Contains(e.Id)).ToListAsync();
        var courseIds = exams.Select(e => e.CourseId).Distinct().ToList();
        var courses = await _db.FaLmsCourses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToListAsync();
        var instructorIds = courses.Where(c => c.InstructorId.HasValue).Select(c => c.InstructorId!.Value).ToList();
        var instructors = await _db.FaLmsInstructors.AsNoTracking().Where(i => instructorIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name);
        var qMap = (await _db.FaLmsQuestions.AsNoTracking().Where(q => examIds.Contains(q.ExamId))
            .GroupBy(q => q.ExamId).Select(g => new { ExamId = g.Key, Total = g.Count(), Texts = g.Count(q => q.Type == 1) }).ToListAsync())
            .ToDictionary(x => x.ExamId);
        var employeeIds = atts.Select(a => a.EmployeeId).ToList();
        var names = await _db.HrEmployees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => (e.FirstName + " " + e.LastName).Trim());
        foreach (var a in atts)
        {
            var e = exams.FirstOrDefault(x => x.Id == a.ExamId);
            var c = e == null ? null : courses.FirstOrDefault(x => x.Id == e.CourseId);
            qMap.TryGetValue(a.ExamId, out var qc);
            result.Items.Add(new FaLmsPendingGradeDto
            {
                AttemptId = a.Id,
                CourseId = c?.Id ?? 0,
                CourseTitle = c?.Title ?? "—",
                ExamId = a.ExamId,
                ExamTitle = e?.Title ?? "—",
                EmployeeId = a.EmployeeId,
                EmployeeName = names.TryGetValue(a.EmployeeId, out var nm) ? nm : null,
                SubmittedAt = a.SubmittedAt,
                QuestionCount = qc?.Total ?? 0,
                TextCount = qc?.Texts ?? 0,
                InstructorId = c?.InstructorId,
                InstructorName = c?.InstructorId is > 0 && instructors.TryGetValue(c.InstructorId.Value, out var inm) ? inm : null
            });
        }
        return result;
    }

    public async Task<List<FaLmsTextAnswerDto>> ListTextAnswersAsync(int attemptId, Paging.Request? pagination = null)
    {
        var att = await _db.FaLmsAttempts.FindAsync(attemptId)
            ?? throw new InvalidOperationException("پاسخ‌نامه یافت نشد.");
        var qs = await _db.FaLmsQuestions.Where(q => q.ExamId == att.ExamId && q.Type == 1)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id).ToPageListAsync(pagination);
        var tas = await _db.FaLmsTextAnswers.Where(x => x.AttemptId == attemptId).ToListAsync();
        return qs.Select(q =>
        {
            var ta = tas.FirstOrDefault(x => x.QuestionId == q.Id);
            return new FaLmsTextAnswerDto
            {
                Id = ta?.Id ?? 0, AttemptId = attemptId, QuestionId = q.Id,
                QuestionText = q.Text, QuestionScore = q.Score,
                AnswerText = ta?.AnswerText, ManualScore = ta?.ManualScore
            };
        }).ToList();
    }

    public async Task<FaLmsAttemptDto> GradeAttemptAsync(int attemptId, FaLmsGradeSaveDto dto, int byUserId)
    {
        var att = await _db.FaLmsAttempts.FindAsync(attemptId)
            ?? throw new InvalidOperationException("پاسخ‌نامه یافت نشد.");
        if (att.SubmittedAt == null) throw new InvalidOperationException("این پاسخ‌نامه هنوز ثبت نشده است.");
        if (att.Score != null) throw new InvalidOperationException("این پاسخ‌نامه قبلاً تصحیح شده است.");
        var e = await _db.FaLmsExams.FindAsync(att.ExamId)
            ?? throw new InvalidOperationException("آزمون یافت نشد.");
        var qs = await _db.FaLmsQuestions.Where(q => q.ExamId == att.ExamId)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id).ToListAsync();
        var grades = dto.Items.ToDictionary(g => g.QuestionId, g => g.ManualScore);
        var csv = (att.Answers ?? "").Split(',', StringSplitOptions.None);
        double earned = 0, total = 0;
        for (var i = 0; i < qs.Count; i++)
        {
            var q = qs[i];
            total += q.Score;
            if (q.Type == 1)
            {
                grades.TryGetValue(q.Id, out var ms);
                var clamped = Math.Max(0, Math.Min(q.Score, ms ?? 0));
                earned += clamped;
                var ta = await _db.FaLmsTextAnswers.FirstOrDefaultAsync(x => x.AttemptId == att.Id && x.QuestionId == q.Id);
                if (ta == null)
                {
                    ta = new FaLmsTextAnswer { AttemptId = att.Id, QuestionId = q.Id };
                    _db.FaLmsTextAnswers.Add(ta);
                }
                ta.ManualScore = clamped;
            }
            else if (i < csv.Length && int.TryParse(csv[i], out var sel) && sel == q.CorrectIndex)
                earned += q.Score;
        }
        var score = total > 0 ? Math.Round(earned * 100.0 / total, 1) : 0;
        var c = await _db.FaLmsCourses.FindAsync(e.CourseId);
        var passed = score >= (c?.PassScore ?? 60);
        att.Score = score; att.Passed = passed;
        await ApplyEnrollmentResultAsync(e.CourseId, att.EmployeeId, score, passed);
        await _db.SaveChangesAsync();
        var uids = await UserIdsAsync(new[] { att.EmployeeId });
        if (uids.Count > 0)
            await NotifyAsync(uids,
                $"نتیجه آزمون «{e.Title}»: {(passed ? "قبول" : "مردود")} — نمره {score} از ۱۰۰.",
                null, "fa-lms/my", byUserId);
        var names = await EmpNamesAsync();
        return new FaLmsAttemptDto
        {
            Id = att.Id, ExamId = att.ExamId, EmployeeId = att.EmployeeId,
            EmployeeName = names.TryGetValue(att.EmployeeId, out var nm) ? nm : null,
            StartedAt = att.StartedAt, SubmittedAt = att.SubmittedAt,
            Answers = att.Answers, Score = att.Score, Passed = att.Passed,
            AttemptNo = att.AttemptNo, NeedsGrading = false
        };
    }

    // ==================== بانک سؤال مشترک ====================

    public async Task<List<FaLmsBankDto>> ListBanksAsync(Paging.Request? pagination = null)
    {
        var list = await _db.FaLmsBanks.OrderByDescending(b => b.Id).ToPageListAsync(pagination);
        var counts = await _db.FaLmsBankQuestions.GroupBy(q => q.BankId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
        return list.Select(b => new FaLmsBankDto
        {
            Id = b.Id, Title = b.Title, Description = b.Description, CreatedAt = b.CreatedAt,
            QuestionsCount = counts.TryGetValue(b.Id, out var n) ? n : 0
        }).ToList();
    }

    public async Task<FaLmsBankDto> SaveBankAsync(int? id, FaLmsBankSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان بانک الزامی است.");
        FaLmsBank b;
        if (id == null)
        {
            b = new FaLmsBank();
            _db.FaLmsBanks.Add(b);
        }
        else
        {
            b = await _db.FaLmsBanks.FindAsync(id.Value)
                ?? throw new InvalidOperationException("بانک یافت نشد.");
        }
        b.Title = dto.Title.Trim();
        b.Description = dto.Description?.Trim();
        await _db.SaveChangesAsync();
        return (await ListBanksAsync()).First(x => x.Id == b.Id);
    }

    public async Task DeleteBankAsync(int id)
    {
        var b = await _db.FaLmsBanks.FindAsync(id)
            ?? throw new InvalidOperationException("بانک یافت نشد.");
        _db.FaLmsBankQuestions.RemoveRange(_db.FaLmsBankQuestions.Where(q => q.BankId == id));
        _db.FaLmsBanks.Remove(b);
        await _db.SaveChangesAsync();
    }

    public async Task<List<FaLmsBankQuestionDto>> ListBankQuestionsAsync(int bankId, Paging.Request? pagination = null)
        => (await _db.FaLmsBankQuestions.Where(q => q.BankId == bankId)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id).ToPageListAsync(pagination))
            .Select(q => new FaLmsBankQuestionDto
            {
                Id = q.Id, BankId = q.BankId, Text = q.Text, OptA = q.OptA, OptB = q.OptB,
                OptC = q.OptC, OptD = q.OptD, CorrectIndex = q.CorrectIndex,
                Score = q.Score, Type = q.Type, SortOrder = q.SortOrder
            }).ToList();

    public async Task<FaLmsBankQuestionDto> SaveBankQuestionAsync(int? id, FaLmsBankQuestionSaveDto dto)
    {
        if (!await _db.FaLmsBanks.AnyAsync(x => x.Id == dto.BankId))
            throw new InvalidOperationException("بانک یافت نشد.");
        if (string.IsNullOrWhiteSpace(dto.Text)) throw new InvalidOperationException("متن سؤال الزامی است.");
        if (dto.Type is < 0 or > 1) throw new InvalidOperationException("نوع سؤال نامعتبر است.");
        if (dto.Type == 0 && dto.CorrectIndex is < 0 or > 3)
            throw new InvalidOperationException("گزینه صحیح باید بین ۱ تا ۴ باشد.");
        FaLmsBankQuestion q;
        if (id == null)
        {
            q = new FaLmsBankQuestion();
            _db.FaLmsBankQuestions.Add(q);
        }
        else
        {
            q = await _db.FaLmsBankQuestions.FindAsync(id.Value)
                ?? throw new InvalidOperationException("سؤال یافت نشد.");
        }
        q.BankId = dto.BankId; q.Text = dto.Text.Trim();
        q.OptA = dto.OptA; q.OptB = dto.OptB; q.OptC = dto.OptC; q.OptD = dto.OptD;
        q.CorrectIndex = dto.CorrectIndex; q.Score = dto.Score; q.Type = dto.Type; q.SortOrder = dto.SortOrder;
        await _db.SaveChangesAsync();
        return (await ListBankQuestionsAsync(dto.BankId)).First(x => x.Id == q.Id);
    }

    public async Task DeleteBankQuestionAsync(int id)
    {
        var q = await _db.FaLmsBankQuestions.FindAsync(id)
            ?? throw new InvalidOperationException("سؤال یافت نشد.");
        _db.FaLmsBankQuestions.Remove(q);
        await _db.SaveChangesAsync();
    }

    public async Task<int> CopyFromBankAsync(int examId, List<int> bankQuestionIds)
    {
        var e = await _db.FaLmsExams.FindAsync(examId)
            ?? throw new InvalidOperationException("آزمون یافت نشد.");
        if (await _db.FaLmsAttempts.AnyAsync(a => a.ExamId == examId && a.SubmittedAt != null))
            throw new InvalidOperationException("آزمون پاسخ‌نامه ثبت‌شده دارد؛ سؤال‌ها قابل تغییر نیستند.");
        if (bankQuestionIds == null || bankQuestionIds.Count == 0)
            throw new InvalidOperationException("سؤالی انتخاب نشده است.");
        var src = await _db.FaLmsBankQuestions.Where(q => bankQuestionIds.Contains(q.Id))
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id).ToListAsync();
        if (src.Count == 0) throw new InvalidOperationException("سؤالی انتخاب نشده است.");
        var order = await _db.FaLmsQuestions.Where(q => q.ExamId == examId)
            .Select(q => (int?)q.SortOrder).MaxAsync() ?? 0;
        foreach (var s in src)
            _db.FaLmsQuestions.Add(new FaLmsQuestion
            {
                ExamId = examId, Text = s.Text, OptA = s.OptA, OptB = s.OptB,
                OptC = s.OptC, OptD = s.OptD, CorrectIndex = s.CorrectIndex,
                Score = s.Score, Type = s.Type, SortOrder = ++order
            });
        await _db.SaveChangesAsync();
        return src.Count;
    }

    // ==================== گواهی ====================

    public async Task<List<FaLmsCertificateDto>> ListCertificatesAsync(int? courseId, int? employeeId, int? year, Paging.Request? pagination = null)
    {
        var q = _db.FaLmsCertificates.AsQueryable();
        if (courseId != null) q = q.Where(x => x.CourseId == courseId.Value);
        if (employeeId != null) q = q.Where(x => x.EmployeeId == employeeId.Value);
        if (year != null)
        {
            var start = Pc.ToDateTime(year.Value, 1, 1, 0, 0, 0, 0);
            var end = Pc.ToDateTime(year.Value + 1, 1, 1, 0, 0, 0, 0);
            q = q.Where(x => x.IssueDate >= start && x.IssueDate < end);
        }
        var list = await q.OrderByDescending(x => x.Id).ToPageListAsync(pagination);
        return await MapCertsAsync(list);
    }

    public async Task<List<FaLmsCertificateDto>> MyCertificatesAsync(int userId, Paging.Request? pagination = null)
    {
        var me = await MyEmployeeAsync(userId);
        if (me == null) return new();
        return await ListCertificatesAsync(null, me.Id, null, pagination: pagination);
    }

    private async Task<List<FaLmsCertificateDto>> MapCertsAsync(List<FaLmsCertificate> list)
    {
        var names = await EmpNamesAsync();
        var courses = await _db.FaLmsCourses.ToDictionaryAsync(c => c.Id, c => c.Title);
        return list.Select(x => new FaLmsCertificateDto
        {
            Id = x.Id, CourseId = x.CourseId,
            CourseTitle = courses.TryGetValue(x.CourseId, out var t) ? t : null,
            EmployeeId = x.EmployeeId,
            EmployeeName = names.TryGetValue(x.EmployeeId, out var nm) ? nm : null,
            CertNo = x.CertNo, IssueDate = x.IssueDate, Score = x.Score, VerifyCode = x.VerifyCode
        }).ToList();
    }

    public async Task<FaLmsCertificateDto> IssueCertificateAsync(int courseId, int employeeId, int byUserId, string byName)
    {
        var c = await _db.FaLmsCourses.FindAsync(courseId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        var en = await _db.FaLmsEnrollments.FirstOrDefaultAsync(x =>
            x.CourseId == courseId && x.EmployeeId == employeeId && x.Status == FaLmsEnrollStatus.Approved);
        if (en == null) throw new InvalidOperationException("این نفر ثبت‌نام تأییدشده در دوره ندارد.");
        if (c.HasExam && en.Passed != true)
            throw new InvalidOperationException("صدور گواهی فقط برای قبول‌شدگان آزمون ممکن است.");
        if (await _db.FaLmsCertificates.AnyAsync(x => x.CourseId == courseId && x.EmployeeId == employeeId))
            throw new InvalidOperationException("برای این نفر در این دوره قبلاً گواهی صادر شده است.");
        var jy = Jy(DateTime.Today);
        var seq = await _db.FaLmsCertificates.CountAsync() + 1;
        string no;
        do { no = $"FA-{jy}-{seq++:0000}"; }
        while (await _db.FaLmsCertificates.AnyAsync(x => x.CertNo == no));
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rnd = new Random();
        string code;
        do { code = new string(Enumerable.Range(0, 8).Select(_ => alphabet[rnd.Next(alphabet.Length)]).ToArray()); }
        while (await _db.FaLmsCertificates.AnyAsync(x => x.VerifyCode == code));
        var cert = new FaLmsCertificate
        {
            CourseId = courseId, EmployeeId = employeeId, CertNo = no,
            IssueDate = DateTime.Today, Score = en.FinalScore, VerifyCode = code
        };
        _db.FaLmsCertificates.Add(cert);
        await _db.SaveChangesAsync();
        var uids = await UserIdsAsync(new[] { employeeId });
        if (uids.Count > 0)
            await NotifyAsync(uids, $"گواهی دوره «{c.Title}» برای شما صادر شد. شماره: {no}",
                null, "fa-lms/my", byUserId);
        return (await MapCertsAsync(new List<FaLmsCertificate> { cert }))[0];
    }

    public async Task<int> IssueMissingAsync(int courseId, int byUserId, string byName)
    {
        var c = await _db.FaLmsCourses.FindAsync(courseId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        var eligible = await _db.FaLmsEnrollments
            .Where(x => x.CourseId == courseId && x.Status == FaLmsEnrollStatus.Approved
                && (!c.HasExam || x.Passed == true)).ToListAsync();
        var have = await _db.FaLmsCertificates.Where(x => x.CourseId == courseId)
            .Select(x => x.EmployeeId).ToListAsync();
        var set = new HashSet<int>(have);
        int n = 0;
        foreach (var en in eligible.Where(x => !set.Contains(x.EmployeeId)))
        {
            await IssueCertificateAsync(courseId, en.EmployeeId, byUserId, byName);
            n++;
        }
        return n;
    }

    public async Task<FaLmsVerifyResultDto> VerifyAsync(string certNo, string code)
    {
        var no = (certNo ?? "").Trim();
        var cd = (code ?? "").Trim().ToUpperInvariant();
        var x = await _db.FaLmsCertificates.FirstOrDefaultAsync(c => c.CertNo == no);
        if (x == null || (x.VerifyCode ?? "").ToUpperInvariant() != cd)
            return new FaLmsVerifyResultDto { Found = false };
        var list = await MapCertsAsync(new List<FaLmsCertificate> { x });
        var d = list[0];
        return new FaLmsVerifyResultDto
        {
            Found = true, CertNo = d.CertNo, CourseTitle = d.CourseTitle,
            EmployeeName = d.EmployeeName, IssueDate = d.IssueDate, Score = d.Score
        };
    }

    public async Task DeleteCertificateAsync(int id)
    {
        var x = await _db.FaLmsCertificates.FindAsync(id)
            ?? throw new InvalidOperationException("گواهی یافت نشد.");
        _db.FaLmsCertificates.Remove(x);
        await _db.SaveChangesAsync();
    }

    // ==================== بودجه ====================

    public async Task<List<FaLmsBudgetDto>> ListBudgetsAsync(Paging.Request? pagination = null)
    {
        var list = await _db.FaLmsBudgets.OrderByDescending(b => b.Year).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var out_ = new List<FaLmsBudgetDto>();
        foreach (var b in list)
            out_.Add(new FaLmsBudgetDto
            {
                Id = b.Id, Year = b.Year, Amount = b.Amount,
                SpentAmount = await SpendAsync(b.Year), Note = b.Note
            });
        return out_;
    }

    public async Task<FaLmsBudgetDto> SaveBudgetAsync(int? id, FaLmsBudgetSaveDto dto)
    {
        if (dto.Year < 1300 || dto.Year > 1500) throw new InvalidOperationException("سال بودجه معتبر نیست.");
        if (dto.Amount < 0) throw new InvalidOperationException("مبلغ بودجه نباید منفی باشد.");
        if (await _db.FaLmsBudgets.AnyAsync(b => b.Year == dto.Year && b.Id != (id ?? 0)))
            throw new InvalidOperationException("برای این سال قبلاً بودجه ثبت شده است.");
        FaLmsBudget b;
        if (id == null)
        {
            b = new FaLmsBudget { CreatedAt = DateTime.Now };
            _db.FaLmsBudgets.Add(b);
        }
        else
        {
            b = await _db.FaLmsBudgets.FindAsync(id.Value)
                ?? throw new InvalidOperationException("بودجه یافت نشد.");
        }
        b.Year = dto.Year; b.Amount = dto.Amount; b.Note = dto.Note;
        await _db.SaveChangesAsync();
        return new FaLmsBudgetDto
        {
            Id = b.Id, Year = b.Year, Amount = b.Amount,
            SpentAmount = await SpendAsync(b.Year), Note = b.Note
        };
    }

    public async Task DeleteBudgetAsync(int id)
    {
        var b = await _db.FaLmsBudgets.FindAsync(id)
            ?? throw new InvalidOperationException("بودجه یافت نشد.");
        _db.FaLmsBudgets.Remove(b);
        await _db.SaveChangesAsync();
    }

    private Task<double> SpendAsync(int year)
    {
        var (start, end) = YearRange(year);
        return (from e in _db.FaLmsEnrollments
                join c in _db.FaLmsCourses on e.CourseId equals c.Id
                where e.Status == FaLmsEnrollStatus.Approved && c.StartDate >= start && c.StartDate <= end
                select c.CostPerPerson).SumAsync();
    }

    public async Task<FaLmsBudgetReportDto> BudgetReportAsync(int year, Paging.Request? pagination = null)
    {
        var (start, end) = YearRange(year);
        var b = await _db.FaLmsBudgets.AsNoTracking().FirstOrDefaultAsync(x => x.Year == year);
        var courses = await _db.FaLmsCourses.AsNoTracking().Where(c => c.StartDate >= start && c.StartDate <= end)
            .OrderBy(c => c.Id).Select(c => new
            {
                c.Id, c.Title, c.CostPerPerson,
                ApprovedCount = _db.FaLmsEnrollments.Count(e => e.CourseId == c.Id && e.Status == FaLmsEnrollStatus.Approved)
            }).ToPageListAsync(pagination);
        return new FaLmsBudgetReportDto
        {
            Year = year, BudgetAmount = b?.Amount ?? 0, SpentAmount = await SpendAsync(year),
            Rows = courses.Select(c => new FaLmsBudgetReportRowDto
            {
                CourseId = c.Id, CourseTitle = c.Title, ApprovedCount = c.ApprovedCount,
                CostPerPerson = c.CostPerPerson, TotalCost = c.ApprovedCount * c.CostPerPerson
            }).ToList()
        };
    }

    // ==================== گزارش‌ها ====================

    public async Task<FaLmsDashboardDto> GetDashboardAsync(int year, Paging.Request? pagination = null)
    {
        var courses = _db.FaLmsCourses.AsNoTracking();
        var (start, end) = YearRange(year);
        var today = DateTime.Today;
        var upcoming = await _db.FaLmsSessions
            .CountAsync(s => s.SessionDate >= today && s.SessionDate <= today.AddDays(7));
        var b = await _db.FaLmsBudgets.FirstOrDefaultAsync(x => x.Year == year);
        var recent = await _db.FaLmsCertificates.OrderByDescending(x => x.Id).ToPageListAsync(pagination, defaultCap: 5);
        return new FaLmsDashboardDto
        {
            Year = year,
            OpenCourses = await courses.CountAsync(c => c.Status == FaLmsCourseStatus.Open && c.IsActive),
            RunningCourses = await courses.CountAsync(c => c.Status == FaLmsCourseStatus.Running),
            FinishedThisYear = await courses.CountAsync(c => c.Status == FaLmsCourseStatus.Finished
                && c.StartDate >= start && c.StartDate <= end),
            PendingNeeds = await _db.FaLmsNeeds.CountAsync(n => n.Status == FaLmsNeedStatus.New),
            PendingEnrolls = await _db.FaLmsEnrollments.CountAsync(e => e.Status == FaLmsEnrollStatus.Pending),
            UpcomingSessions = upcoming,
            BudgetAmount = b?.Amount ?? 0,
            SpentAmount = await SpendAsync(year),
            RecentCerts = await MapCertsAsync(recent)
        };
    }

    public async Task<FaLmsEmployeeReportDto?> EmployeeReportAsync(int employeeId, Paging.Request? pagination = null)
    {
        var emp = await _db.HrEmployees.FindAsync(employeeId);
        if (emp == null) return null;
        var query = _db.FaLmsEnrollments.AsNoTracking().Where(e => e.EmployeeId == employeeId);
        var approved = query.Where(e => e.Status == FaLmsEnrollStatus.Approved);
        var coursesCount = await approved.CountAsync();
        var hours = await (from e in approved join c in _db.FaLmsCourses on e.CourseId equals c.Id
                           select c.DurationHours).SumAsync();
        var avgScore = await approved.AverageAsync(e => e.FinalScore);
        var avgAttendance = await approved.AverageAsync(e => e.AttendancePercent);
        var certQuery = _db.FaLmsCertificates.AsNoTracking().Where(x => x.EmployeeId == employeeId);
        var certCount = await certQuery.CountAsync();
        var enrolls = await query.OrderByDescending(e => e.Id).ToPageListAsync(pagination);
        var courseIds = enrolls.Select(e => e.CourseId).ToList();
        var courses = await _db.FaLmsCourses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        var certSet = (await certQuery.Where(x => courseIds.Contains(x.CourseId)).Select(x => x.CourseId).ToListAsync()).ToHashSet();
        var rows = enrolls.Select(e =>
        {
            courses.TryGetValue(e.CourseId, out var c);
            return new FaLmsEmployeeRowDto
            {
                CourseId = e.CourseId, CourseTitle = c?.Title,
                Year = c?.StartDate != null ? Jy(c.StartDate.Value) : Jy(e.EnrolledAt),
                Hours = c?.DurationHours ?? 0, Status = (int)e.Status,
                Score = e.FinalScore, Passed = e.Passed, Attendance = e.AttendancePercent,
                HasCert = certSet.Contains(e.CourseId)
            };
        }).ToList();
        return new FaLmsEmployeeReportDto
        {
            EmployeeId = employeeId,
            EmployeeName = (emp.FirstName + " " + emp.LastName).Trim(),
            CoursesCount = coursesCount,
            HoursTotal = hours,
            AvgScore = avgScore.HasValue ? Math.Round(avgScore.Value, 1) : null,
            CertsCount = certCount,
            AvgAttendance = avgAttendance.HasValue ? Math.Round(avgAttendance.Value, 1) : null,
            Rows = rows
        };
    }

    public async Task<FaLmsEmployeeReportDto?> MyReportAsync(int userId, Paging.Request? pagination = null)
    {
        var me = await MyEmployeeAsync(userId);
        if (me == null) return null;
        return await EmployeeReportAsync(me.Id, pagination);
    }

    // ==================== چاپ رسمی گواهی ====================

    public async Task<byte[]> CertificatePdfAsync(int id)
    {
        var x = await _db.FaLmsCertificates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new InvalidOperationException("گواهی یافت نشد.");
        var course = await _db.FaLmsCourses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == x.CourseId);
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == x.EmployeeId);
        var co = await _db.HrMainCompanies.AsNoTracking().FirstOrDefaultAsync();
        HrPdf.EnsureFonts();
        var empName = emp == null ? "—" : (emp.FirstName + " " + emp.LastName).Trim();
        var doc = Document.Create(c =>
        {
            c.Page(pg =>
            {
                pg.Size(PageSizes.A4.Landscape());
                pg.Margin(30);
                pg.ContentFromRightToLeft();
                pg.DefaultTextStyle(x => x.FontFamily(HrPdf.Font).FontSize(11));
                pg.Content().Column(col =>
                {
                    col.Item().Border(3).BorderColor(QuestPDF.Helpers.Colors.Blue.Darken2).Padding(18).Column(inner =>
                    {
                        inner.Item().Border(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten1).Padding(16).Column(box =>
                        {
                            box.Item().Text(co?.Name ?? "").FontFamily(HrPdf.FontBold).FontSize(16).AlignCenter();
                            box.Item().PaddingTop(2).Text("گواهی پایان دوره آموزشی").FontFamily(HrPdf.FontBold).FontSize(24).AlignCenter();
                            box.Item().PaddingTop(10).Text("گواهی می‌شود").FontSize(12).AlignCenter();
                            box.Item().PaddingTop(4).Text(empName).FontFamily(HrPdf.FontBold).FontSize(20).AlignCenter();
                            box.Item().PaddingTop(4).Text($"دوره «{course?.Title ?? "—"}» را به مدت {Fa.Digits((course?.DurationHours ?? 0).ToString("0.#"))} ساعت با موفقیت گذرانده است.").FontSize(12).AlignCenter();
                            if (x.Score != null)
                                box.Item().PaddingTop(4).Text($"نمره نهایی: {Fa.Digits(x.Score.Value.ToString("0.#"))} از ۱۰۰").FontSize(12).AlignCenter();
                            box.Item().PaddingTop(10).Row(r =>
                            {
                                r.RelativeItem().Text($"شماره گواهی: {Fa.Digits(x.CertNo)}").FontSize(10);
                                r.RelativeItem().AlignCenter().Text($"تاریخ صدور: {PersianDate.ToShortFa(x.IssueDate)}").FontSize(10);
                                r.RelativeItem().AlignLeft().Text($"کد رهگیری: {x.VerifyCode ?? "—"}").FontSize(10);
                            });
                            box.Item().PaddingTop(24).Row(r =>
                            {
                                r.RelativeItem().Column(s =>
                                {
                                    s.Item().Text("مدیر آموزش").FontSize(10).AlignCenter();
                                    s.Item().PaddingTop(24).Text("امضا و مهر").FontSize(9).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1).AlignCenter();
                                });
                                r.RelativeItem().Column(s =>
                                {
                                    s.Item().Text("مدیر منابع انسانی").FontSize(10).AlignCenter();
                                    s.Item().PaddingTop(24).Text("امضا و مهر").FontSize(9).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1).AlignCenter();
                                });
                            });
                        });
                    });
                });
            });
        });
        return doc.GeneratePdf();
    }

    // ==================== مدرس‌ها ====================

    public async Task<List<FaLmsInstructorDto>> ListInstructorsAsync(bool? onlyActive, Paging.Request? pagination = null)
    {
        var q = _db.FaLmsInstructors.AsNoTracking().AsQueryable();
        if (onlyActive == true) q = q.Where(x => x.IsActive);
        var rows = await q.OrderBy(x => x.Name).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 500);
        var ids = rows.Select(x => x.Id).ToList();
        var courses = ids.Count == 0 ? new List<FaLmsCourse>()
            : await _db.FaLmsCourses.AsNoTracking().Where(c => c.InstructorId != null && ids.Contains(c.InstructorId.Value)).ToListAsync();
        return rows.Select(x =>
        {
            var mine = courses.Where(c => c.InstructorId == x.Id).ToList();
            return new FaLmsInstructorDto
            {
                Id = x.Id, Name = x.Name, Type = (int)x.Type, Field = x.Field, Phone = x.Phone,
                FeePerHour = x.FeePerHour, EmployeeId = x.EmployeeId, IsActive = x.IsActive,
                CourseCount = mine.Count,
                TotalFee = Math.Round(mine.Sum(c => c.DurationHours) * x.FeePerHour)
            };
        }).ToList();
    }

    private async Task<FaLmsInstructorDto> GetInstructorAsync(int id)
        => (await ListInstructorsAsync(null)).FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("مدرس یافت نشد.");

    public async Task<FaLmsInstructorDto> SaveInstructorAsync(int? id, FaLmsInstructorSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("نام مدرس خالی است.");
        FaLmsInstructor x;
        if (id is > 0)
        {
            x = await _db.FaLmsInstructors.FirstOrDefaultAsync(i => i.Id == id.Value)
                ?? throw new InvalidOperationException("مدرس یافت نشد.");
        }
        else { x = new FaLmsInstructor(); _db.FaLmsInstructors.Add(x); }
        x.Name = dto.Name.Trim();
        x.Type = (FaLmsInstructorType)Math.Clamp(dto.Type, 0, 1);
        x.Field = dto.Field?.Trim();
        x.Phone = dto.Phone?.Trim();
        x.FeePerHour = Math.Max(0, dto.FeePerHour);
        x.EmployeeId = dto.EmployeeId is > 0 ? dto.EmployeeId : null;
        x.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return await GetInstructorAsync(x.Id);
    }

    public async Task DeleteInstructorAsync(int id)
    {
        var x = await _db.FaLmsInstructors.FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("مدرس یافت نشد.");
        if (await _db.FaLmsCourses.AnyAsync(c => c.InstructorId == id))
            throw new InvalidOperationException("این مدرس به دوره‌ای متصل است و قابل حذف نیست.");
        _db.FaLmsInstructors.Remove(x);
        await _db.SaveChangesAsync();
    }

    // ==================== نظرسنجی اثربخشی ====================

    public async Task<List<FaLmsSurveyQuestionDto>> ListSurveyQuestionsAsync(int courseId, int? employeeId, Paging.Request? pagination = null)
    {
        var qs = await _db.FaLmsSurveyQuestions.AsNoTracking()
            .Where(q => q.CourseId == courseId).OrderBy(q => q.SortOrder).ThenBy(q => q.Id).ToPageListAsync(pagination);
        if (qs.Count == 0) return new();
        var qids = qs.Select(q => q.Id).ToList();
        var answers = await _db.FaLmsSurveyAnswers.AsNoTracking()
            .Where(a => qids.Contains(a.QuestionId)).ToListAsync();
        return qs.Select(q =>
        {
            var mine = answers.Where(a => a.QuestionId == q.Id).ToList();
            return new FaLmsSurveyQuestionDto
            {
                Id = q.Id, Text = q.Text,
                AvgScore = mine.Count == 0 ? 0 : Math.Round(mine.Average(a => a.Score), 1),
                AnswerCount = mine.Count,
                MyScore = employeeId is > 0 ? mine.FirstOrDefault(a => a.EmployeeId == employeeId.Value)?.Score : null
            };
        }).ToList();
    }

    public async Task<FaLmsSurveyQuestionDto> SaveSurveyQuestionAsync(FaLmsSurveyQuestionSaveDto dto)
    {
        if (dto.CourseId <= 0) throw new InvalidOperationException("دوره مشخص نیست.");
        if (string.IsNullOrWhiteSpace(dto.Text)) throw new InvalidOperationException("متن سؤال خالی است.");
        var max = await _db.FaLmsSurveyQuestions.Where(q => q.CourseId == dto.CourseId)
            .Select(q => (int?)q.SortOrder).MaxAsync() ?? -1;
        var q = new FaLmsSurveyQuestion { CourseId = dto.CourseId, Text = dto.Text.Trim(), SortOrder = max + 1 };
        _db.FaLmsSurveyQuestions.Add(q);
        await _db.SaveChangesAsync();
        return (await ListSurveyQuestionsAsync(dto.CourseId, null)).First(x => x.Id == q.Id);
    }

    public async Task DeleteSurveyQuestionAsync(int id)
    {
        var q = await _db.FaLmsSurveyQuestions.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("سؤال یافت نشد.");
        _db.FaLmsSurveyAnswers.RemoveRange(_db.FaLmsSurveyAnswers.Where(a => a.QuestionId == id));
        _db.FaLmsSurveyQuestions.Remove(q);
        await _db.SaveChangesAsync();
    }

    public async Task SaveSurveyAnswerAsync(FaLmsSurveyAnswerSaveDto dto)
    {
        if (dto.EmployeeId <= 0 || dto.QuestionId <= 0) throw new InvalidOperationException("فراگیر/سؤال مشخص نیست.");
        var score = Math.Clamp(dto.Score, 1, 5);
        var a = await _db.FaLmsSurveyAnswers.FirstOrDefaultAsync(x =>
            x.QuestionId == dto.QuestionId && x.EmployeeId == dto.EmployeeId);
        if (a == null)
        {
            _db.FaLmsSurveyAnswers.Add(new FaLmsSurveyAnswer
                { QuestionId = dto.QuestionId, EmployeeId = dto.EmployeeId, Score = score });
        }
        else a.Score = score;
        await _db.SaveChangesAsync();
    }

    public async Task<FaLmsSurveyResultDto> SurveyResultsAsync(int courseId, Paging.Request? pagination = null)
    {
        var questionQuery = _db.FaLmsSurveyQuestions.AsNoTracking().Where(q => q.CourseId == courseId);
        var questionIds = questionQuery.Select(q => q.Id);
        var answerQuery = _db.FaLmsSurveyAnswers.AsNoTracking().Where(a => questionIds.Contains(a.QuestionId));
        var respondents = await answerQuery.Select(a => a.EmployeeId).Distinct().CountAsync();
        // This is aggregate metadata for the global summary, never question/answer rows.
        // Round each mean as before, rather than changing to an unrounded global AVG.
        var aggregates = await answerQuery.GroupBy(a => a.QuestionId)
            .Select(g => new { Count = g.Count(), Average = g.Average(a => (double)a.Score) }).ToListAsync();
        var answerCount = aggregates.Sum(g => g.Count);
        var questionCount = await questionQuery.CountAsync();
        var qs = await ListSurveyQuestionsAsync(courseId, null, pagination);
        return new FaLmsSurveyResultDto
        {
            QuestionCount = questionCount, RespondentCount = respondents,
            OverallAvg = answerCount == 0 ? 0 : Math.Round(aggregates.Sum(g => Math.Round(g.Average, 1) * g.Count) / answerCount, 1),
            Questions = qs
        };
    }

    // ==================== تداخل‌یابی آموزشی ====================

    public async Task<List<FaLmsConflictDto>> CheckConflictsAsync(int courseId, int? employeeId, Paging.Request? pagination = null)
    {
        var sessions = await _db.FaLmsSessions.AsNoTracking()
            .Where(s => s.CourseId == courseId).OrderBy(s => s.SessionDate).ThenBy(s => s.Id).Take(200).ToListAsync();
        if (sessions.Count == 0) return new();
        List<int> empIds;
        if (employeeId is > 0) empIds = new List<int> { employeeId.Value };
        else empIds = await _db.FaLmsEnrollments.AsNoTracking()
            .Where(e => e.CourseId == courseId && e.Status == FaLmsEnrollStatus.Approved)
            .Select(e => e.EmployeeId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
        if (empIds.Count == 0) return new();
        var names = await _db.HrEmployees.AsNoTracking().Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName);
        var minD = sessions.Min(s => s.SessionDate).Date;
        var maxD = sessions.Max(s => s.SessionDate).Date;
        var leaves = await _db.FaAttLeaves.AsNoTracking()
            .Where(l => empIds.Contains(l.EmployeeId) && l.Status != FaAttRequestStatus.Rejected
                && l.FromDate.Date <= maxD && l.ToDate.Date >= minD).OrderBy(l => l.Id).Take(2000).ToListAsync();
        var missions = await _db.FaAttMissions.AsNoTracking()
            .Where(m => empIds.Contains(m.EmployeeId) && m.Status != FaAttRequestStatus.Rejected
                && m.FromDate.Date <= maxD && m.ToDate.Date >= minD).OrderBy(m => m.Id).Take(1000).ToListAsync();
        var assigns = await _db.FaAttShiftAssigns.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId)
                && a.FromDate.Date <= maxD && (a.ToDate == null || a.ToDate.Value.Date >= minD))
            .OrderBy(a => a.Id).Take(2000).ToListAsync();
        var shiftIds = assigns.Select(a => a.ShiftId).Distinct().ToList();
        var shifts = shiftIds.Count == 0 ? new Dictionary<int, FaAttShift>()
            : await _db.FaAttShifts.AsNoTracking().Where(s => shiftIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s);
        var out_ = new List<FaLmsConflictDto>();
        string Nm(int id) => names.TryGetValue(id, out var n) ? n : "—";
        foreach (var s in sessions)
        {
            var d = s.SessionDate.Date;
            var sStart = s.StartTime ?? TimeSpan.Zero;
            var sEnd = s.EndTime ?? new TimeSpan(23, 59, 59);
            foreach (var e in empIds)
            {
                var lv = leaves.FirstOrDefault(l => l.EmployeeId == e && l.FromDate.Date <= d && l.ToDate.Date >= d);
                if (lv != null) out_.Add(new FaLmsConflictDto
                {
                    EmployeeId = e, EmployeeName = Nm(e), SessionDate = d, Topic = s.Topic,
                    Kind = "Leave", Detail = lv.HoursPerDay != null ? "مرخصی ساعتی در همان روز" : "مرخصی روزانه"
                });
                var ms = missions.FirstOrDefault(m => m.EmployeeId == e && m.FromDate.Date <= d && m.ToDate.Date >= d);
                if (ms != null) out_.Add(new FaLmsConflictDto
                {
                    EmployeeId = e, EmployeeName = Nm(e), SessionDate = d, Topic = s.Topic,
                    Kind = "Mission", Detail = string.IsNullOrWhiteSpace(ms.Destination) ? "مأموریت" : $"مأموریت: {ms.Destination}"
                });
                var a = assigns.FirstOrDefault(x => x.EmployeeId == e
                    && x.FromDate.Date <= d && (x.ToDate == null || x.ToDate.Value.Date >= d));
                if (a != null && shifts.TryGetValue(a.ShiftId, out var sh))
                {
                    if (sh.StartTime < sEnd && sStart < sh.EndTime)
                        out_.Add(new FaLmsConflictDto
                        {
                            EmployeeId = e, EmployeeName = Nm(e), SessionDate = d, Topic = s.Topic,
                            Kind = "Shift", Detail = $"هم‌پوشانی با شیفت {sh.Name}"
                        });
                }
            }
        }

        // ترتیب پایدار نتیجه: تاریخ جلسه، پرسنل، نوع تداخل.
        out_ = out_.OrderBy(c => c.SessionDate).ThenBy(c => c.EmployeeId).ThenBy(c => c.Kind)
            .ThenBy(c => c.Topic ?? "").ToList();

        // صفحه‌بندی روی «نتیجهٔ نهایی تداخل‌ها»؛ سقف‌های ورودی (200/500/…) مستقل از total
        // و صفحه‌بندی‌اند. در حالت legacy بدون صفحه‌بندی، سقف ایمنی ۲۰۰ سطری خروجی حفظ می‌شود.
        if (pagination is { IsPaged: true })
        {
            pagination.SetTotal(out_.Count);
            if (pagination.Skip > 0) out_ = out_.Skip(pagination.Skip).ToList();
            if (pagination.Take is > 0) out_ = out_.Take(pagination.Take.Value).ToList();
        }
        else if (out_.Count > 200)
        {
            out_ = out_.Take(200).ToList();
        }
        return out_;
    }
}
