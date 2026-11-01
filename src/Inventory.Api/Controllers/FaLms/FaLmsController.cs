using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.FaLms;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.FaLms;

/// <summary>
/// ================== آموزش و توسعه فروغ آریا (ماژول جدید و مستقل) ==================
/// دوره‌ها و تقویم، نیازسنجی، ثبت‌نام، جلسات و حضور، آزمون، گواهی، بودجه و اثربخشی.
/// مجوزها: FaLms.Read (مشاهده/سلف‌سرویس) / FaLms.Create (ثبت درخواست) / FaLms.Manage (مدیریت کامل)
/// </summary>
[Route("api/fa-lms")]
public class FaLmsController : RbacControllerBase
{
    private const string Mod = "FaLms";
    private readonly IFaLmsService _svc;

    public FaLmsController(AppDbContext db, IFaLmsService svc) : base(db) => _svc = svc;

    // ------------------- دوره‌ها -------------------

    [HttpGet("courses")]
    public async Task<IActionResult> Courses([FromQuery] int? year, [FromQuery] int? status,
        [FromQuery] int? kind, [FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListCoursesAsync(year, status, kind, q, pagination: pagination), skip, take));
    }

    [HttpGet("courses/{id:int}")]
    public async Task<IActionResult> Course(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var c = await _svc.GetCourseAsync(id);
        return c == null ? NotFound() : Ok(c);
    }

    [HttpPost("courses")]
    public async Task<IActionResult> CreateCourse([FromBody] FaLmsCourseSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveCourseAsync(null, dto));
    }

    [HttpPut("courses/{id:int}")]
    public async Task<IActionResult> UpdateCourse(int id, [FromBody] FaLmsCourseSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveCourseAsync(id, dto));
    }

    [HttpDelete("courses/{id:int}")]
    public async Task<IActionResult> DeleteCourse(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteCourseAsync(id);
        return Ok();
    }

    [HttpPost("courses/{id:int}/status")]
    public async Task<IActionResult> CourseStatus(int id, [FromQuery] int status)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SetCourseStatusAsync(id, status, MyUserId));
    }

    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar([FromQuery] int year, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(pagination => _svc.GetCalendarAsync(year, pagination), skip, take));
    }

    [HttpGet("courses/{id:int}/report")]
    public async Task<IActionResult> CourseReport(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        var r = await _svc.GetCourseReportAsync(id);
        return r == null ? NotFound() : Ok(r);
    }

    // ------------------- نیازسنجی -------------------

    [HttpGet("needs")]
    public async Task<IActionResult> Needs([FromQuery] int? year, [FromQuery] int? status,
        [FromQuery] int? source, [FromQuery] int? employeeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListNeedsAsync(year, status, source, employeeId, pagination: pagination), skip, take));
    }

    [HttpGet("needs/my")]
    public async Task<IActionResult> MyNeeds([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.MyNeedsAsync(MyUserId, pagination: pagination), skip, take));
    }

    [HttpPost("needs")]
    public async Task<IActionResult> CreateNeed([FromBody] FaLmsNeedSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveNeedAsync(null, dto, MyUserId, MyUsername));
    }

    [HttpPut("needs/{id:int}")]
    public async Task<IActionResult> UpdateNeed(int id, [FromBody] FaLmsNeedSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveNeedAsync(id, dto, MyUserId, MyUsername));
    }

    [HttpPost("needs/{id:int}/decide")]
    public async Task<IActionResult> DecideNeed(int id, [FromBody] FaLmsNeedDecideDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.DecideNeedAsync(id, dto.Status, dto.LinkedCourseId, MyUsername));
    }

    [HttpDelete("needs/{id:int}")]
    public async Task<IActionResult> DeleteNeed(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteNeedAsync(id);
        return Ok();
    }

    [HttpGet("perf/periods")]
    public async Task<IActionResult> PerfPeriods([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        // دوره‌های ارزیابی عملکردِ خودِ «منابع انسانی اصلی» (HrAppraisals) — نه ارزیابی فروغ آریا (HrPerf*)
        var rows = await Db.HrAppraisals.AsNoTracking().OrderByDescending(p => p.Id)
            .ToPageListAsync(pagination);
        var list = rows.Select(p => new FaLmsPerfPeriodDto { Id = p.Id, Title = p.Title, Year = p.Year, Status = p.Status.ToString() }).ToList();
        return Ok(pagination.Result(list));
    }

    [HttpGet("perf/weak")]
    public async Task<IActionResult> WeakScores([FromQuery] int periodId, [FromQuery] double threshold = 60, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.WeakScoresAsync(periodId, threshold, pagination: pagination), skip, take));
    }

    [HttpPost("perf/suggest")]
    public async Task<IActionResult> Suggest([FromBody] FaLmsSuggestDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SuggestFromPerfAsync(dto.PeriodId, dto.Threshold, dto.Year, MyUserId, MyUsername));
    }

    // ------------------- ثبت‌نام -------------------

    [HttpGet("enrollments")]
    public async Task<IActionResult> Enrollments([FromQuery] int? courseId, [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListEnrollmentsAsync(courseId, status, pagination: pagination), skip, take));
    }

    [HttpGet("enrollments/my")]
    public async Task<IActionResult> MyEnrollments([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.MyEnrollmentsAsync(MyUserId, pagination: pagination), skip, take));
    }

    [HttpPost("enrollments")]
    public async Task<IActionResult> Enroll([FromBody] FaLmsEnrollSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.EnrollAsync(dto.CourseId, dto.EmployeeId, MyUserId, MyUsername));
    }

    [HttpPost("enrollments/{id:int}/decide")]
    public async Task<IActionResult> DecideEnrollment(int id, [FromQuery] bool approve)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.DecideEnrollmentAsync(id, approve, MyUserId, MyUsername));
    }

    [HttpPost("enrollments/{id:int}/cancel")]
    public async Task<IActionResult> CancelEnrollment(int id)
    {
        bool isHr = await ForbiddenUnlessAsync(Mod, "Manage") == null;
        if (!isHr && await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        await _svc.CancelEnrollmentAsync(id, MyUserId, isHr);
        return Ok();
    }

    [HttpDelete("enrollments/{id:int}")]
    public async Task<IActionResult> DeleteEnrollment(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteEnrollmentAsync(id);
        return Ok();
    }

    // ------------------- جلسات و حضور -------------------

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions([FromQuery] int courseId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListSessionsAsync(courseId, pagination: pagination), skip, take));
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] FaLmsSessionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveSessionAsync(null, dto));
    }

    [HttpPut("sessions/{id:int}")]
    public async Task<IActionResult> UpdateSession(int id, [FromBody] FaLmsSessionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveSessionAsync(id, dto));
    }

    [HttpDelete("sessions/{id:int}")]
    public async Task<IActionResult> DeleteSession(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteSessionAsync(id);
        return Ok();
    }

    [HttpGet("sessions/{id:int}/attendance")]
    public async Task<IActionResult> Attendance(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.GetAttendanceAsync(id, pagination: pagination), skip, take));
    }

    [HttpPost("sessions/{id:int}/attendance")]
    public async Task<IActionResult> SaveAttendance(int id, [FromBody] FaLmsAttendanceSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        dto.SessionId = id;
        await _svc.SaveAttendanceAsync(dto);
        return Ok();
    }

    // ------------------- آزمون -------------------

    [HttpGet("exams")]
    public async Task<IActionResult> Exams([FromQuery] int courseId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListExamsAsync(courseId, pagination: pagination), skip, take));
    }

    [HttpPost("exams")]
    public async Task<IActionResult> CreateExam([FromBody] FaLmsExamSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveExamAsync(null, dto, MyUserId));
    }

    [HttpPut("exams/{id:int}")]
    public async Task<IActionResult> UpdateExam(int id, [FromBody] FaLmsExamSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveExamAsync(id, dto, MyUserId));
    }

    [HttpDelete("exams/{id:int}")]
    public async Task<IActionResult> DeleteExam(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteExamAsync(id);
        return Ok();
    }

    [HttpGet("exams/{examId:int}/questions")]
    public async Task<IActionResult> Questions(int examId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListQuestionsAsync(examId, pagination: pagination), skip, take));
    }

    [HttpPost("exams/{examId:int}/questions")]
    public async Task<IActionResult> CreateQuestion(int examId, [FromBody] FaLmsQuestionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        dto.ExamId = examId;
        return Ok(await _svc.SaveQuestionAsync(null, dto));
    }

    [HttpPut("questions/{id:int}")]
    public async Task<IActionResult> UpdateQuestion(int id, [FromBody] FaLmsQuestionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveQuestionAsync(id, dto));
    }

    [HttpDelete("questions/{id:int}")]
    public async Task<IActionResult> DeleteQuestion(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteQuestionAsync(id);
        return Ok();
    }

    [HttpPost("exams/{examId:int}/start")]
    public async Task<IActionResult> StartExam(int examId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.StartAttemptAsync(examId, MyUserId));
    }

    [HttpPost("exams/{examId:int}/submit")]
    public async Task<IActionResult> SubmitExam(int examId, [FromBody] FaLmsSubmitDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.SubmitAttemptAsync(examId, dto, MyUserId));
    }

    [HttpGet("exams/{examId:int}/attempts")]
    public async Task<IActionResult> Attempts(int examId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListAttemptsAsync(examId, pagination: pagination), skip, take));
    }

    // ------------------- بانک سؤال مشترک -------------------

    [HttpGet("banks")]
    public async Task<IActionResult> Banks([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListBanksAsync(pagination: pagination), skip, take));
    }

    [HttpPost("banks")]
    public async Task<IActionResult> CreateBank([FromBody] FaLmsBankSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(await _svc.SaveBankAsync(null, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("banks/{id:int}")]
    public async Task<IActionResult> UpdateBank(int id, [FromBody] FaLmsBankSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(await _svc.SaveBankAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("banks/{id:int}")]
    public async Task<IActionResult> DeleteBank(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { await _svc.DeleteBankAsync(id); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("banks/{bankId:int}/questions")]
    public async Task<IActionResult> BankQuestions(int bankId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListBankQuestionsAsync(bankId, pagination: pagination), skip, take));
    }

    [HttpPost("banks/{bankId:int}/questions")]
    public async Task<IActionResult> CreateBankQuestion(int bankId, [FromBody] FaLmsBankQuestionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        dto.BankId = bankId;
        try { return Ok(await _svc.SaveBankQuestionAsync(null, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("bank-questions/{id:int}")]
    public async Task<IActionResult> UpdateBankQuestion(int id, [FromBody] FaLmsBankQuestionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(await _svc.SaveBankQuestionAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("bank-questions/{id:int}")]
    public async Task<IActionResult> DeleteBankQuestion(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { await _svc.DeleteBankQuestionAsync(id); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("exams/{examId:int}/copy-from-bank")]
    public async Task<IActionResult> CopyFromBank(int examId, [FromBody] FaLmsCopyFromBankDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(new { count = await _svc.CopyFromBankAsync(examId, dto.BankQuestionIds) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ------------------- تصحیح تشریحی -------------------

    [HttpGet("grading/pending")]
    public async Task<IActionResult> GradingPending([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        var pagination = new Paging.Request(skip, take);
        var r = await _svc.GradingInboxAsync(MyUserId, pagination);
        if (Paging.Requested(skip, take))
            return Ok(new { r.MyInstructorId, total = pagination.Total, items = r.Items });
        return Ok(r);
    }

    [HttpGet("attempts/{id:int}/texts")]
    public async Task<IActionResult> AttemptTexts(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(await Paging.ResultAsync(async pagination => await _svc.ListTextAnswersAsync(id, pagination: pagination), skip, take)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("attempts/{id:int}/grade")]
    public async Task<IActionResult> GradeAttempt(int id, [FromBody] FaLmsGradeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(await _svc.GradeAttemptAsync(id, dto, MyUserId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ------------------- گواهی -------------------

    [HttpGet("certificates")]
    public async Task<IActionResult> Certificates([FromQuery] int? courseId, [FromQuery] int? employeeId, [FromQuery] int? year, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListCertificatesAsync(courseId, employeeId, year, pagination: pagination), skip, take));
    }

    [HttpGet("certificates/my")]
    public async Task<IActionResult> MyCertificates([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.MyCertificatesAsync(MyUserId, pagination: pagination), skip, take));
    }

    [HttpPost("certificates/issue")]
    public async Task<IActionResult> Issue([FromBody] FaLmsIssueDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.IssueCertificateAsync(dto.CourseId, dto.EmployeeId, MyUserId, MyUsername));
    }

    [HttpPost("certificates/issue-missing")]
    public async Task<IActionResult> IssueMissing([FromBody] FaLmsIssueDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.IssueMissingAsync(dto.CourseId, MyUserId, MyUsername));
    }

    [HttpGet("certificates/verify")]
    public async Task<IActionResult> Verify([FromQuery] string no, [FromQuery] string code)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.VerifyAsync(no, code));
    }

    [HttpGet("certificates/{id:int}/pdf")]
    public async Task<IActionResult> CertificatePdf(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        try { return File(await _svc.CertificatePdfAsync(id), "application/pdf", $"FaLmsCert-{id}.pdf"); }
        catch (InvalidOperationException) { return NotFound(); }
    }

    // ------------------- مدرس‌ها -------------------

    [HttpGet("instructors")]
    public async Task<IActionResult> Instructors([FromQuery] bool? onlyActive, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListInstructorsAsync(onlyActive, pagination: pagination), skip, take));
    }

    [HttpPost("instructors")]
    public async Task<IActionResult> SaveInstructor([FromQuery] int? id, [FromBody] FaLmsInstructorSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(await _svc.SaveInstructorAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("instructors/{id:int}")]
    public async Task<IActionResult> DeleteInstructor(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { await _svc.DeleteInstructorAsync(id); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    // ------------------- نظرسنجی اثربخشی -------------------

    [HttpGet("courses/{id:int}/survey")]
    public async Task<IActionResult> SurveyQuestions(int id, [FromQuery] int? employeeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListSurveyQuestionsAsync(id, employeeId, pagination: pagination), skip, take));
    }

    [HttpPost("courses/{id:int}/survey")]
    public async Task<IActionResult> SaveSurveyQuestion(int id, [FromBody] FaLmsSurveyQuestionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try
        {
            dto.CourseId = id;
            return Ok(await _svc.SaveSurveyQuestionAsync(dto));
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("survey/{qid:int}")]
    public async Task<IActionResult> DeleteSurveyQuestion(int qid)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { await _svc.DeleteSurveyQuestionAsync(qid); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("survey/answer")]
    public async Task<IActionResult> SaveSurveyAnswer([FromBody] FaLmsSurveyAnswerSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        try { await _svc.SaveSurveyAnswerAsync(dto); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("courses/{id:int}/survey-results")]
    public async Task<IActionResult> SurveyResults(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var pagination = new Paging.Request(skip, take);
        var r = await _svc.SurveyResultsAsync(id, pagination);
        if (Paging.Requested(skip, take))
            return Ok(new { r.QuestionCount, r.RespondentCount, r.OverallAvg, total = pagination.Total, questions = r.Questions });
        return Ok(r);
    }

    // ------------------- تداخل‌یابی -------------------

    [HttpGet("courses/{id:int}/conflicts")]
    public async Task<IActionResult> Conflicts(int id, [FromQuery] int? employeeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(pagination => _svc.CheckConflictsAsync(id, employeeId, pagination), skip, take));
    }

    [HttpDelete("certificates/{id:int}")]
    public async Task<IActionResult> DeleteCertificate(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteCertificateAsync(id);
        return Ok();
    }

    // ------------------- بودجه -------------------

    [HttpGet("budgets")]
    public async Task<IActionResult> Budgets([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListBudgetsAsync(pagination: pagination), skip, take));
    }

    [HttpPost("budgets")]
    public async Task<IActionResult> CreateBudget([FromBody] FaLmsBudgetSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveBudgetAsync(null, dto));
    }

    [HttpPut("budgets/{id:int}")]
    public async Task<IActionResult> UpdateBudget(int id, [FromBody] FaLmsBudgetSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveBudgetAsync(id, dto));
    }

    [HttpDelete("budgets/{id:int}")]
    public async Task<IActionResult> DeleteBudget(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteBudgetAsync(id);
        return Ok();
    }

    [HttpGet("budgets/report")]
    public async Task<IActionResult> BudgetReport([FromQuery] int year, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        var pagination = new Paging.Request(skip, take);
        var r = await _svc.BudgetReportAsync(year, pagination);
        if (Paging.Requested(skip, take))
            return Ok(new { r.Year, r.BudgetAmount, r.SpentAmount, r.Remaining, total = pagination.Total, rows = r.Rows });
        return Ok(r);
    }

    // ------------------- گزارش‌ها -------------------

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] int year, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var pagination = new Paging.Request(skip, take);
        var r = await _svc.GetDashboardAsync(year, pagination);
        if (r is null || !Paging.Requested(skip, take)) return Ok(r);
        return Ok(new
        {
            r.Year, r.OpenCourses, r.RunningCourses, r.FinishedThisYear,
            r.PendingNeeds, r.PendingEnrolls, r.UpcomingSessions,
            r.BudgetAmount, r.SpentAmount,
            total = pagination.Total, recentCerts = r.RecentCerts
        });
    }

    [HttpGet("reports/employee/{employeeId:int}")]
    public async Task<IActionResult> EmployeeReport(int employeeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        var pagination = new Paging.Request(skip, take);
        var r = await _svc.EmployeeReportAsync(employeeId, pagination);
        if (r == null) return NotFound();
        if (Paging.Requested(skip, take))
            return Ok(new { r.EmployeeId, r.EmployeeName, r.CoursesCount, r.HoursTotal, r.AvgScore, r.CertsCount, r.AvgAttendance, total = pagination.Total, rows = r.Rows });
        return Ok(r);
    }

    [HttpGet("reports/employee/my")]
    public async Task<IActionResult> MyReport([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var pagination = new Paging.Request(skip, take);
        var r = await _svc.MyReportAsync(MyUserId, pagination);
        if (r == null) return NotFound();
        if (Paging.Requested(skip, take))
            return Ok(new { r.EmployeeId, r.EmployeeName, r.CoursesCount, r.HoursTotal, r.AvgScore, r.CertsCount, r.AvgAttendance, total = pagination.Total, rows = r.Rows });
        return Ok(r);
    }
}
