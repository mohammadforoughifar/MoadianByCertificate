using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IFaLmsService
{
    Task<List<FaLmsCourseDto>> ListCoursesAsync(int? year = null, int? status = null, int? kind = null, string? q = null);
    Task<FaLmsCourseDto> GetCourseAsync(int id);
    Task<FaLmsCourseDto> SaveCourseAsync(int? id, FaLmsCourseSaveDto dto);
    Task DeleteCourseAsync(int id);
    Task<FaLmsCourseDto> SetCourseStatusAsync(int id, int status);
    Task<List<FaLmsCalendarItemDto>> GetCalendarAsync(int year);
    Task<FaLmsCourseReportDto> GetCourseReportAsync(int id);

    Task<List<FaLmsNeedDto>> ListNeedsAsync(int? year = null, int? status = null, int? source = null, int? employeeId = null);
    Task<List<FaLmsNeedDto>> MyNeedsAsync();
    Task<FaLmsNeedDto> SaveNeedAsync(int? id, FaLmsNeedSaveDto dto);
    Task<FaLmsNeedDto> DecideNeedAsync(int id, int status, int? linkedCourseId);
    Task DeleteNeedAsync(int id);
    Task<List<FaLmsPerfPeriodDto>> PerfPeriodsAsync();
    Task<List<FaLmsWeakScoreDto>> WeakScoresAsync(int periodId, double threshold);
    Task<int> SuggestFromPerfAsync(int periodId, double threshold, int year);

    Task<List<FaLmsEnrollmentDto>> ListEnrollmentsAsync(int? courseId = null, int? status = null);
    Task<List<FaLmsEnrollmentDto>> MyEnrollmentsAsync();
    Task<FaLmsEnrollmentDto> EnrollAsync(int courseId, int? employeeId = null);
    Task<FaLmsEnrollmentDto> DecideEnrollmentAsync(int id, bool approve);
    Task CancelEnrollmentAsync(int id);
    Task DeleteEnrollmentAsync(int id);

    Task<List<FaLmsSessionDto>> ListSessionsAsync(int courseId);
    Task<FaLmsSessionDto> SaveSessionAsync(int? id, FaLmsSessionSaveDto dto);
    Task DeleteSessionAsync(int id);
    Task<List<FaLmsAttendanceDto>> GetAttendanceAsync(int sessionId);
    Task SaveAttendanceAsync(int sessionId, List<FaLmsAttendanceItemDto> items);

    Task<List<FaLmsExamDto>> ListExamsAsync(int courseId);
    Task<FaLmsExamDto> SaveExamAsync(int? id, FaLmsExamSaveDto dto);
    Task DeleteExamAsync(int id);
    Task<List<FaLmsQuestionDto>> ListQuestionsAsync(int examId);
    Task<FaLmsQuestionDto> SaveQuestionAsync(int? id, int examId, FaLmsQuestionSaveDto dto);
    Task DeleteQuestionAsync(int id);
    Task<FaLmsExamPlayDto> StartExamAsync(int examId);
    Task<FaLmsAttemptDto> SubmitExamAsync(int examId, int attemptId, List<FaLmsAnswerDto> answers, List<FaLmsTextDto>? texts = null);
    Task<List<FaLmsAttemptDto>> ListAttemptsAsync(int examId);
    Task<FaLmsGradingInboxDto> GradingInboxAsync();
    Task<List<FaLmsBankDto>> ListBanksAsync();
    Task<FaLmsBankDto> SaveBankAsync(int? id, FaLmsBankSaveDto dto);
    Task DeleteBankAsync(int id);
    Task<List<FaLmsBankQuestionDto>> ListBankQuestionsAsync(int bankId);
    Task<FaLmsBankQuestionDto> SaveBankQuestionAsync(int? id, int bankId, FaLmsBankQuestionSaveDto dto);
    Task DeleteBankQuestionAsync(int id);
    Task<int> CopyFromBankAsync(int examId, List<int> bankQuestionIds);
    Task<List<FaLmsTextAnswerDto>> ListTextAnswersAsync(int attemptId);
    Task<FaLmsAttemptDto> GradeAttemptAsync(int attemptId, List<FaLmsGradeItemDto> items);

    Task<List<FaLmsCertificateDto>> ListCertificatesAsync(int? courseId = null, int? employeeId = null, int? year = null);
    Task<List<FaLmsCertificateDto>> MyCertificatesAsync();
    Task<FaLmsCertificateDto> IssueAsync(int courseId, int employeeId);
    Task<int> IssueMissingAsync(int courseId);
    Task<FaLmsVerifyResultDto> VerifyAsync(string no, string code);
    Task<(byte[] Data, string FileName, string ContentType)> GetCertificatePdfAsync(int id);
    Task<List<FaLmsInstructorDto>> ListInstructorsAsync(bool? onlyActive = null);
    Task<FaLmsInstructorDto> SaveInstructorAsync(int? id, FaLmsInstructorSaveDto dto);
    Task DeleteInstructorAsync(int id);
    Task<List<FaLmsSurveyQuestionDto>> ListSurveyQuestionsAsync(int courseId, int? employeeId = null);
    Task<FaLmsSurveyQuestionDto> SaveSurveyQuestionAsync(int courseId, string text);
    Task DeleteSurveyQuestionAsync(int qid);
    Task SaveSurveyAnswerAsync(int questionId, int employeeId, int score);
    Task<FaLmsSurveyResultDto> SurveyResultsAsync(int courseId);
    Task<List<FaLmsConflictDto>> CheckConflictsAsync(int courseId, int? employeeId = null);
    Task DeleteCertificateAsync(int id);

    Task<List<FaLmsBudgetDto>> ListBudgetsAsync();
    Task<FaLmsBudgetDto> SaveBudgetAsync(int? id, FaLmsBudgetSaveDto dto);
    Task DeleteBudgetAsync(int id);
    Task<FaLmsBudgetReportDto> BudgetReportAsync(int year);

    Task<FaLmsDashboardDto> GetDashboardAsync(int year);
    Task<FaLmsEmployeeReportDto> EmployeeReportAsync(int employeeId);
    Task<FaLmsEmployeeReportDto> MyReportAsync();
}

public class FaLmsService : IFaLmsService
{
    private readonly IApiClient _api;
    public FaLmsService(IApiClient api) => _api = api;

    private const string Root = "api/fa-lms";

    public Task<List<FaLmsCourseDto>> ListCoursesAsync(int? year = null, int? status = null, int? kind = null, string? q = null)
        => _api.GetAsync<List<FaLmsCourseDto>>($"{Root}/courses?year={year}&status={status}&kind={kind}&q={Uri.EscapeDataString(q ?? "")}");

    public Task<FaLmsCourseDto> GetCourseAsync(int id)
        => _api.GetAsync<FaLmsCourseDto>($"{Root}/courses/{id}");

    public Task<FaLmsCourseDto> SaveCourseAsync(int? id, FaLmsCourseSaveDto dto)
        => id == null ? _api.PostAsync<FaLmsCourseDto>($"{Root}/courses", dto)
                      : _api.PutAsync<FaLmsCourseDto>($"{Root}/courses/{id}", dto);

    public Task DeleteCourseAsync(int id)
        => _api.DeleteAsync($"{Root}/courses/{id}");

    public Task<FaLmsCourseDto> SetCourseStatusAsync(int id, int status)
        => _api.PostAsync<FaLmsCourseDto>($"{Root}/courses/{id}/status?status={status}");

    public Task<List<FaLmsCalendarItemDto>> GetCalendarAsync(int year)
        => _api.GetAsync<List<FaLmsCalendarItemDto>>($"{Root}/calendar?year={year}");

    public Task<FaLmsCourseReportDto> GetCourseReportAsync(int id)
        => _api.GetAsync<FaLmsCourseReportDto>($"{Root}/courses/{id}/report");

    public Task<List<FaLmsNeedDto>> ListNeedsAsync(int? year = null, int? status = null, int? source = null, int? employeeId = null)
        => _api.GetAsync<List<FaLmsNeedDto>>($"{Root}/needs?year={year}&status={status}&source={source}&employeeId={employeeId}");

    public Task<List<FaLmsNeedDto>> MyNeedsAsync()
        => _api.GetAsync<List<FaLmsNeedDto>>($"{Root}/needs/my");

    public Task<FaLmsNeedDto> SaveNeedAsync(int? id, FaLmsNeedSaveDto dto)
        => id == null ? _api.PostAsync<FaLmsNeedDto>($"{Root}/needs", dto)
                      : _api.PutAsync<FaLmsNeedDto>($"{Root}/needs/{id}", dto);

    public Task<FaLmsNeedDto> DecideNeedAsync(int id, int status, int? linkedCourseId)
        => _api.PostAsync<FaLmsNeedDto>($"{Root}/needs/{id}/decide",
            new FaLmsNeedDecideDto { Status = status, LinkedCourseId = linkedCourseId });

    public Task DeleteNeedAsync(int id)
        => _api.DeleteAsync($"{Root}/needs/{id}");

    public Task<List<FaLmsPerfPeriodDto>> PerfPeriodsAsync()
        => _api.GetAsync<List<FaLmsPerfPeriodDto>>($"{Root}/perf/periods");

    public Task<List<FaLmsWeakScoreDto>> WeakScoresAsync(int periodId, double threshold)
        => _api.GetAsync<List<FaLmsWeakScoreDto>>($"{Root}/perf/weak?periodId={periodId}&threshold={threshold}");

    public Task<int> SuggestFromPerfAsync(int periodId, double threshold, int year)
        => _api.PostAsync<int>($"{Root}/perf/suggest",
            new FaLmsSuggestDto { PeriodId = periodId, Threshold = threshold, Year = year });

    public Task<List<FaLmsEnrollmentDto>> ListEnrollmentsAsync(int? courseId = null, int? status = null)
        => _api.GetAsync<List<FaLmsEnrollmentDto>>($"{Root}/enrollments?courseId={courseId}&status={status}");

    public Task<List<FaLmsEnrollmentDto>> MyEnrollmentsAsync()
        => _api.GetAsync<List<FaLmsEnrollmentDto>>($"{Root}/enrollments/my");

    public Task<FaLmsEnrollmentDto> EnrollAsync(int courseId, int? employeeId = null)
        => _api.PostAsync<FaLmsEnrollmentDto>($"{Root}/enrollments",
            new FaLmsEnrollSaveDto { CourseId = courseId, EmployeeId = employeeId });

    public Task<FaLmsEnrollmentDto> DecideEnrollmentAsync(int id, bool approve)
        => _api.PostAsync<FaLmsEnrollmentDto>($"{Root}/enrollments/{id}/decide?approve={approve}");

    public Task CancelEnrollmentAsync(int id)
        => _api.PostAsync<object>($"{Root}/enrollments/{id}/cancel");

    public Task DeleteEnrollmentAsync(int id)
        => _api.DeleteAsync($"{Root}/enrollments/{id}");

    public Task<List<FaLmsSessionDto>> ListSessionsAsync(int courseId)
        => _api.GetAsync<List<FaLmsSessionDto>>($"{Root}/sessions?courseId={courseId}");

    public Task<FaLmsSessionDto> SaveSessionAsync(int? id, FaLmsSessionSaveDto dto)
        => id == null ? _api.PostAsync<FaLmsSessionDto>($"{Root}/sessions", dto)
                      : _api.PutAsync<FaLmsSessionDto>($"{Root}/sessions/{id}", dto);

    public Task DeleteSessionAsync(int id)
        => _api.DeleteAsync($"{Root}/sessions/{id}");

    public Task<List<FaLmsAttendanceDto>> GetAttendanceAsync(int sessionId)
        => _api.GetAsync<List<FaLmsAttendanceDto>>($"{Root}/sessions/{sessionId}/attendance");

    public Task SaveAttendanceAsync(int sessionId, List<FaLmsAttendanceItemDto> items)
        => _api.PostAsync<object>($"{Root}/sessions/{sessionId}/attendance",
            new FaLmsAttendanceSaveDto { SessionId = sessionId, Items = items });

    public Task<List<FaLmsExamDto>> ListExamsAsync(int courseId)
        => _api.GetAsync<List<FaLmsExamDto>>($"{Root}/exams?courseId={courseId}");

    public Task<FaLmsExamDto> SaveExamAsync(int? id, FaLmsExamSaveDto dto)
        => id == null ? _api.PostAsync<FaLmsExamDto>($"{Root}/exams", dto)
                      : _api.PutAsync<FaLmsExamDto>($"{Root}/exams/{id}", dto);

    public Task DeleteExamAsync(int id)
        => _api.DeleteAsync($"{Root}/exams/{id}");

    public Task<List<FaLmsQuestionDto>> ListQuestionsAsync(int examId)
        => _api.GetAsync<List<FaLmsQuestionDto>>($"{Root}/exams/{examId}/questions");

    public Task<FaLmsQuestionDto> SaveQuestionAsync(int? id, int examId, FaLmsQuestionSaveDto dto)
    {
        dto.ExamId = examId;
        return id == null ? _api.PostAsync<FaLmsQuestionDto>($"{Root}/exams/{examId}/questions", dto)
                          : _api.PutAsync<FaLmsQuestionDto>($"{Root}/questions/{id}", dto);
    }

    public Task DeleteQuestionAsync(int id)
        => _api.DeleteAsync($"{Root}/questions/{id}");

    public Task<FaLmsExamPlayDto> StartExamAsync(int examId)
        => _api.PostAsync<FaLmsExamPlayDto>($"{Root}/exams/{examId}/start");

    public Task<FaLmsAttemptDto> SubmitExamAsync(int examId, int attemptId, List<FaLmsAnswerDto> answers, List<FaLmsTextDto>? texts = null)
        => _api.PostAsync<FaLmsAttemptDto>($"{Root}/exams/{examId}/submit",
            new FaLmsSubmitDto { AttemptId = attemptId, Answers = answers, TextAnswers = texts ?? new() });

    public Task<FaLmsGradingInboxDto> GradingInboxAsync()
        => _api.GetAsync<FaLmsGradingInboxDto>($"{Root}/grading/pending");

    public Task<List<FaLmsAttemptDto>> ListAttemptsAsync(int examId)
        => _api.GetAsync<List<FaLmsAttemptDto>>($"{Root}/exams/{examId}/attempts");

    public Task<List<FaLmsBankDto>> ListBanksAsync()
        => _api.GetAsync<List<FaLmsBankDto>>($"{Root}/banks");

    public Task<FaLmsBankDto> SaveBankAsync(int? id, FaLmsBankSaveDto dto)
        => id == null ? _api.PostAsync<FaLmsBankDto>($"{Root}/banks", dto)
                      : _api.PutAsync<FaLmsBankDto>($"{Root}/banks/{id}", dto);

    public Task DeleteBankAsync(int id)
        => _api.DeleteAsync($"{Root}/banks/{id}");

    public Task<List<FaLmsBankQuestionDto>> ListBankQuestionsAsync(int bankId)
        => _api.GetAsync<List<FaLmsBankQuestionDto>>($"{Root}/banks/{bankId}/questions");

    public Task<FaLmsBankQuestionDto> SaveBankQuestionAsync(int? id, int bankId, FaLmsBankQuestionSaveDto dto)
    {
        dto.BankId = bankId;
        return id == null ? _api.PostAsync<FaLmsBankQuestionDto>($"{Root}/banks/{bankId}/questions", dto)
                          : _api.PutAsync<FaLmsBankQuestionDto>($"{Root}/bank-questions/{id}", dto);
    }

    public Task DeleteBankQuestionAsync(int id)
        => _api.DeleteAsync($"{Root}/bank-questions/{id}");

    public async Task<int> CopyFromBankAsync(int examId, List<int> bankQuestionIds)
    {
        var r = await _api.PostAsync<FaLmsCopyResultDto>($"{Root}/exams/{examId}/copy-from-bank",
            new FaLmsCopyFromBankDto { BankQuestionIds = bankQuestionIds });
        return r.Count;
    }

    public Task<List<FaLmsTextAnswerDto>> ListTextAnswersAsync(int attemptId)
        => _api.GetAsync<List<FaLmsTextAnswerDto>>($"{Root}/attempts/{attemptId}/texts");

    public Task<FaLmsAttemptDto> GradeAttemptAsync(int attemptId, List<FaLmsGradeItemDto> items)
        => _api.PostAsync<FaLmsAttemptDto>($"{Root}/attempts/{attemptId}/grade",
            new FaLmsGradeSaveDto { Items = items });

    public Task<List<FaLmsCertificateDto>> ListCertificatesAsync(int? courseId = null, int? employeeId = null, int? year = null)
        => _api.GetAsync<List<FaLmsCertificateDto>>($"{Root}/certificates?courseId={courseId}&employeeId={employeeId}&year={year}");

    public Task<List<FaLmsCertificateDto>> MyCertificatesAsync()
        => _api.GetAsync<List<FaLmsCertificateDto>>($"{Root}/certificates/my");

    public Task<FaLmsCertificateDto> IssueAsync(int courseId, int employeeId)
        => _api.PostAsync<FaLmsCertificateDto>($"{Root}/certificates/issue",
            new FaLmsIssueDto { CourseId = courseId, EmployeeId = employeeId });

    public Task<int> IssueMissingAsync(int courseId)
        => _api.PostAsync<int>($"{Root}/certificates/issue-missing",
            new FaLmsIssueDto { CourseId = courseId });

    public Task<FaLmsVerifyResultDto> VerifyAsync(string no, string code)
        => _api.GetAsync<FaLmsVerifyResultDto>(
            $"{Root}/certificates/verify?no={Uri.EscapeDataString(no)}&code={Uri.EscapeDataString(code)}");

    public Task DeleteCertificateAsync(int id)
        => _api.DeleteAsync($"{Root}/certificates/{id}");

    public Task<(byte[] Data, string FileName, string ContentType)> GetCertificatePdfAsync(int id)
        => _api.GetFileAsync($"{Root}/certificates/{id}/pdf");

    public Task<List<FaLmsInstructorDto>> ListInstructorsAsync(bool? onlyActive = null)
        => _api.GetAsync<List<FaLmsInstructorDto>>($"{Root}/instructors?onlyActive={onlyActive}");

    public Task<FaLmsInstructorDto> SaveInstructorAsync(int? id, FaLmsInstructorSaveDto dto)
        => _api.PostAsync<FaLmsInstructorDto>($"{Root}/instructors?id={id}", dto);

    public Task DeleteInstructorAsync(int id)
        => _api.DeleteAsync($"{Root}/instructors/{id}");

    public Task<List<FaLmsSurveyQuestionDto>> ListSurveyQuestionsAsync(int courseId, int? employeeId = null)
        => _api.GetAsync<List<FaLmsSurveyQuestionDto>>($"{Root}/courses/{courseId}/survey?employeeId={employeeId}");

    public Task<FaLmsSurveyQuestionDto> SaveSurveyQuestionAsync(int courseId, string text)
        => _api.PostAsync<FaLmsSurveyQuestionDto>($"{Root}/courses/{courseId}/survey",
            new FaLmsSurveyQuestionSaveDto { CourseId = courseId, Text = text });

    public Task DeleteSurveyQuestionAsync(int qid)
        => _api.DeleteAsync($"{Root}/survey/{qid}");

    public Task SaveSurveyAnswerAsync(int questionId, int employeeId, int score)
        => _api.PostAsync<object>($"{Root}/survey/answer",
            new FaLmsSurveyAnswerSaveDto { QuestionId = questionId, EmployeeId = employeeId, Score = score });

    public Task<FaLmsSurveyResultDto> SurveyResultsAsync(int courseId)
        => _api.GetAsync<FaLmsSurveyResultDto>($"{Root}/courses/{courseId}/survey-results");

    public Task<List<FaLmsConflictDto>> CheckConflictsAsync(int courseId, int? employeeId = null)
        => _api.GetAsync<List<FaLmsConflictDto>>($"{Root}/courses/{courseId}/conflicts?employeeId={employeeId}");

    public Task<List<FaLmsBudgetDto>> ListBudgetsAsync()
        => _api.GetAsync<List<FaLmsBudgetDto>>($"{Root}/budgets");

    public Task<FaLmsBudgetDto> SaveBudgetAsync(int? id, FaLmsBudgetSaveDto dto)
        => id == null ? _api.PostAsync<FaLmsBudgetDto>($"{Root}/budgets", dto)
                      : _api.PutAsync<FaLmsBudgetDto>($"{Root}/budgets/{id}", dto);

    public Task DeleteBudgetAsync(int id)
        => _api.DeleteAsync($"{Root}/budgets/{id}");

    public Task<FaLmsBudgetReportDto> BudgetReportAsync(int year)
        => _api.GetAsync<FaLmsBudgetReportDto>($"{Root}/budgets/report?year={year}");

    public Task<FaLmsDashboardDto> GetDashboardAsync(int year)
        => _api.GetAsync<FaLmsDashboardDto>($"{Root}/dashboard?year={year}");

    public Task<FaLmsEmployeeReportDto> EmployeeReportAsync(int employeeId)
        => _api.GetAsync<FaLmsEmployeeReportDto>($"{Root}/reports/employee/{employeeId}");

    public Task<FaLmsEmployeeReportDto> MyReportAsync()
        => _api.GetAsync<FaLmsEmployeeReportDto>($"{Root}/reports/employee/my");
}
