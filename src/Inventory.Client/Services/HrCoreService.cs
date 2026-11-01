using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IHrCoreService
{
    Task<HrEmployeeListResult> SearchEmployeesAsync(string? q, int? orgUnitId, int? status, int skip, int take, int? hrMainNodeId = null);
    Task<List<HrEmployeeLiteDto>> ListEmployeeLiteAsync(bool onlyActive = true);
    Task<(byte[] Data, string FileName, string ContentType)> GetEmployeeImportTemplateAsync();
    Task<HrEmployeeImportResultDto> ImportEmployeesAsync(Stream stream, string fileName);
    Task<HrEmployeeDto> GetEmployeeAsync(int id);
    Task<HrEmployeeDto> SaveEmployeeAsync(int? id, HrEmployeeSaveDto dto);
    Task SetEmployeeActiveAsync(int id, bool active);
    Task<string> NextEmployeeCodeAsync();

    Task<List<HrOrgUnitDto>> GetOrgTreeAsync();
    Task<List<HrOrgUnitDto>> GetOrgUnitsAsync();
    Task<HrOrgUnitDto> SaveOrgUnitAsync(int? id, HrOrgUnitSaveDto dto);
    Task DeleteOrgUnitAsync(int id);

    Task<List<HrContractDto>> GetContractsAsync(bool? onlyActive = null);
    Task<List<HrContractDto>> GetExpiringContractsAsync(int days = 30);
    Task<List<HrContractDto>> GetEmployeeContractsAsync(int employeeId);
    Task<HrContractDto> SaveContractAsync(int? id, HrContractSaveDto dto);
    Task<HrBulkResultDto> SaveContractsBulkAsync(HrContractBulkDto dto);
    Task<HrContractDto> RenewContractAsync(int id, int months);
    Task<int> RemindExpiringDocumentsAsync(int days);
    Task<(byte[] Data, string FileName, string ContentType)> GetDossierPdfAsync(int employeeId);
    Task<(byte[] Data, string FileName, string ContentType)> GetEmploymentCertificateAsync(int employeeId, string? purpose);
    Task<(byte[] Data, string FileName, string ContentType)> ExportEmployeesExcelAsync(string? q);
    Task<(byte[] Data, string FileName, string ContentType)> ExportContractsExcelAsync();
    Task<(byte[] Data, string FileName, string ContentType)> ExportDecreesExcelAsync();
    Task<(byte[] Data, string FileName, string ContentType)> ExportOrgExcelAsync();
    Task<HrAuditListResult> SearchHrAuditAsync(string? module, string? action, string? q,
        DateTime? from, DateTime? to, int skip, int take);
    Task<HrAuditLogDto> GetHrAuditAsync(long id);
    Task<HrManagerDashboardDto> GetManagerDashboardAsync(int year);
    Task<HrDataQualityReportDto> GetDataQualityReportAsync();
    Task<List<HrReportColumnDto>> GetReportMetaAsync(string entity);
    Task<HrReportResultDto> RunReportAsync(HrReportRunDto dto);
    Task<List<HrReportTemplateDto>> ListReportTemplatesAsync();
    Task<HrReportTemplateDto> SaveReportTemplateAsync(HrReportTemplateSaveDto dto);
    Task DeleteReportTemplateAsync(int id);
    Task DeleteContractAsync(int id);

    Task<List<HrContractTemplateDto>> GetTemplatesAsync();
    Task<HrContractTemplateDto> SaveTemplateAsync(int? id, HrContractTemplateSaveDto dto);
    Task DeleteTemplateAsync(int id);

    Task<HrContractDto> SubmitForSignAsync(int id);
    Task<HrContractDto> SignEmployeeAsync(int id, string name);
    Task<HrContractDto> SignEmployerAsync(int id);
    Task<List<HrContractVersionDto>> GetVersionsAsync(int contractId);

    Task<List<HrContractAlertDto>> GetAlertsAsync();
    Task<int> CheckAlertsAsync(int days = 30);
    Task DismissAlertAsync(int id);

    Task<List<HrDecreeDto>> GetDecreesAsync(int? employeeId = null, bool? onlyPending = null);
    Task<List<HrDecreeDto>> GetEmployeeDecreesAsync(int employeeId);
    Task<HrDecreeDto> SaveDecreeAsync(int? id, HrDecreeSaveDto dto);
    Task<HrBulkResultDto> SaveDecreesBulkAsync(HrDecreeBulkDto dto);
    Task<HrDecreeDto> ApplyDecreeAsync(int id);
    Task<int> ApplyDueDecreesAsync();
    Task DeleteDecreeAsync(int id);
    Task<(byte[] Data, string FileName, string ContentType)> GetDecreePdfAsync(int id);
    Task<(byte[] Data, string FileName, string ContentType)> GetContractPdfAsync(int id);

    Task<HrEmployeeDossierDto> GetDossierAsync(int employeeId, int expiringDays = 30);

    Task<List<HrEmployeeDependentDto>> GetDependentsAsync(int employeeId);
    Task<HrEmployeeDependentDto> SaveDependentAsync(int employeeId, int? id, HrEmployeeDependentSaveDto dto);
    Task DeleteDependentAsync(int id);

    Task<List<HrEmployeeCourseDto>> GetCoursesAsync(int employeeId);
    Task<HrEmployeeCourseDto> SaveCourseAsync(int employeeId, int? id, HrEmployeeCourseSaveDto dto);
    Task DeleteCourseAsync(int id);

    Task<List<HrEmployeeSkillDto>> GetSkillsAsync(int employeeId);
    Task<HrEmployeeSkillDto> SaveSkillAsync(int employeeId, int? id, HrEmployeeSkillSaveDto dto);
    Task DeleteSkillAsync(int id);

    Task<List<HrEmployeeLanguageDto>> GetLanguagesAsync(int employeeId);
    Task<HrEmployeeLanguageDto> SaveLanguageAsync(int employeeId, int? id, HrEmployeeLanguageSaveDto dto);
    Task DeleteLanguageAsync(int id);

    Task<List<HrEmployeeDocumentDto>> GetDocumentsAsync(int employeeId, int expiringDays = 30);
    Task<HrEmployeeDocumentDto> SaveDocumentAsync(int employeeId, int? id, HrEmployeeDocumentSaveDto dto);
    Task DeleteDocumentAsync(int id);
    Task<List<HrEmployeeDocumentDto>> GetExpiringDocumentsAsync(int days = 30);
    Task<HrEmployeeDocumentDto> UploadDocumentFileAsync(int docId, Stream stream, string fileName, string? contentType = null);
    Task<(byte[] Data, string FileName, string ContentType)> DownloadDocumentAsync(int docId);

    Task<(byte[] Data, string FileName, string ContentType)> GetEmployeePhotoAsync(int employeeId);
    Task<HrEmployeeDto> UploadEmployeePhotoAsync(int employeeId, Stream stream, string fileName, string? contentType = null);
    Task<HrEmployeeDto> DeleteEmployeePhotoAsync(int employeeId);

    Task<HrDashboardDto> GetDashboardAsync();

    Task<List<HrJobPostingDto>> GetPostingsAsync(bool? onlyOpen = null);
    Task<HrJobPostingDto> SavePostingAsync(int? id, HrJobPostingSaveDto dto);
    Task DeletePostingAsync(int id);

    Task<List<HrApplicantDto>> GetApplicantsAsync(int? postingId = null, int? status = null);
    Task<HrApplicantDto> SaveApplicantAsync(int? id, HrApplicantSaveDto dto);
    Task<HrApplicantDto> MoveApplicantAsync(int id, int status);
    Task DeleteApplicantAsync(int id);
    Task<HrApplicantDto> ConvertApplicantAsync(int id, string nationalCode, DateTime? hireDate);

    Task<List<HrInterviewDto>> GetInterviewsAsync(int applicantId);
    Task<HrInterviewDto> SaveInterviewAsync(int? id, HrInterviewSaveDto dto);
    Task DeleteInterviewAsync(int id);

    /// <summary>پاک‌سازی کامل داده‌های منابع انسانی (حذف داده‌های آزمایشی) — فقط ادمین</summary>
    Task<HrPurgeResultDto> PurgeDemoDataAsync();
}

public class HrEmployeeListResult
{
    public int Total { get; set; }
    public List<HrEmployeeDto> Items { get; set; } = new();
}

public class HrCoreService : IHrCoreService
{
    private readonly IApiClient _api;
    public HrCoreService(IApiClient api) => _api = api;

    private const string Root = "api/hr-core";

    public Task<HrEmployeeListResult> SearchEmployeesAsync(string? q, int? orgUnitId, int? status, int skip, int take, int? hrMainNodeId = null)
    {
        var qs = $"?skip={skip}&take={take}";
        if (!string.IsNullOrWhiteSpace(q)) qs += $"&q={Uri.EscapeDataString(q)}";
        if (hrMainNodeId is > 0) qs += $"&hrMainNodeId={hrMainNodeId}";
        if (orgUnitId is > 0) qs += $"&orgUnitId={orgUnitId}";
        if (status is >= 0) qs += $"&status={status}";
        return _api.GetAsync<HrEmployeeListResult>($"{Root}/employees{qs}");
    }

    public Task<HrEmployeeDto> GetEmployeeAsync(int id)
        => _api.GetAsync<HrEmployeeDto>($"{Root}/employees/{id}");

    public Task<HrEmployeeDto> SaveEmployeeAsync(int? id, HrEmployeeSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrEmployeeDto>($"{Root}/employees/{id}", dto)
            : _api.PostAsync<HrEmployeeDto>($"{Root}/employees", dto);

    public Task SetEmployeeActiveAsync(int id, bool active)
        => _api.PostAsync<object>($"{Root}/employees/{id}/active?active={active}", null);

    public async Task<string> NextEmployeeCodeAsync()
    {
        var r = await _api.GetAsync<Dictionary<string, string>>($"{Root}/employees/next-code");
        return r.TryGetValue("code", out var c) ? c : "";
    }

    public Task<List<HrOrgUnitDto>> GetOrgTreeAsync()
        => _api.GetAsync<List<HrOrgUnitDto>>($"{Root}/org/tree");

    public Task<List<HrOrgUnitDto>> GetOrgUnitsAsync()
        => _api.GetAsync<List<HrOrgUnitDto>>($"{Root}/org/units");

    public Task<HrOrgUnitDto> SaveOrgUnitAsync(int? id, HrOrgUnitSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrOrgUnitDto>($"{Root}/org/units/{id}", dto)
            : _api.PostAsync<HrOrgUnitDto>($"{Root}/org/units", dto);

    public Task DeleteOrgUnitAsync(int id)
        => _api.DeleteAsync($"{Root}/org/units/{id}");

    public Task<List<HrContractDto>> GetContractsAsync(bool? onlyActive = null)
        => _api.GetAsync<List<HrContractDto>>($"{Root}/contracts{(onlyActive == true ? "?onlyActive=true" : "")}");

    public Task<List<HrContractDto>> GetExpiringContractsAsync(int days = 30)
        => _api.GetAsync<List<HrContractDto>>($"{Root}/contracts/expiring?days={days}");

    public Task<List<HrContractDto>> GetEmployeeContractsAsync(int employeeId)
        => _api.GetAsync<List<HrContractDto>>($"{Root}/employees/{employeeId}/contracts");

    public Task<HrContractDto> SaveContractAsync(int? id, HrContractSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrContractDto>($"{Root}/contracts/{id}", dto)
            : _api.PostAsync<HrContractDto>($"{Root}/contracts", dto);

    public Task<HrBulkResultDto> SaveContractsBulkAsync(HrContractBulkDto dto)
        => _api.PostAsync<HrBulkResultDto>($"{Root}/contracts/bulk", dto);

    public Task<HrBulkResultDto> SaveDecreesBulkAsync(HrDecreeBulkDto dto)
        => _api.PostAsync<HrBulkResultDto>($"{Root}/decrees/bulk", dto);

    public Task<List<HrEmployeeLiteDto>> ListEmployeeLiteAsync(bool onlyActive = true)
        => _api.GetAsync<List<HrEmployeeLiteDto>>($"{Root}/employees/lite?onlyActive={onlyActive}");

    public Task<(byte[] Data, string FileName, string ContentType)> GetEmployeeImportTemplateAsync()
        => _api.GetFileAsync($"{Root}/employees/import-template");

    public Task<HrEmployeeImportResultDto> ImportEmployeesAsync(Stream stream, string fileName)
        => _api.PostFileAsync<HrEmployeeImportResultDto>($"{Root}/employees/import", stream, fileName);

    public Task<HrContractDto> RenewContractAsync(int id, int months)
        => _api.PostAsync<HrContractDto>($"{Root}/contracts/{id}/renew?months={months}", null);

    public async Task<int> RemindExpiringDocumentsAsync(int days)
    {
        var r = await _api.PostAsync<Dictionary<string, int>>($"{Root}/documents/remind?days={days}", null);
        return r.TryGetValue("count", out var c) ? c : 0;
    }

    public Task<(byte[] Data, string FileName, string ContentType)> GetDossierPdfAsync(int employeeId)
        => _api.GetFileAsync($"{Root}/employees/{employeeId}/dossier-pdf");

    public Task<(byte[] Data, string FileName, string ContentType)> GetEmploymentCertificateAsync(int employeeId, string? purpose)
        => _api.GetFileAsync($"{Root}/employees/{employeeId}/employment-certificate?purpose={Uri.EscapeDataString(purpose ?? "")}");

    public Task<(byte[] Data, string FileName, string ContentType)> ExportEmployeesExcelAsync(string? q)
        => _api.GetFileAsync($"{Root}/employees/export?q={Uri.EscapeDataString(q ?? "")}");

    public Task<(byte[] Data, string FileName, string ContentType)> ExportContractsExcelAsync()
        => _api.GetFileAsync($"{Root}/contracts/export");

    public Task<(byte[] Data, string FileName, string ContentType)> ExportDecreesExcelAsync()
        => _api.GetFileAsync($"{Root}/decrees/export");

    public Task<(byte[] Data, string FileName, string ContentType)> ExportOrgExcelAsync()
        => _api.GetFileAsync($"{Root}/org/export");

    public Task<HrAuditListResult> SearchHrAuditAsync(string? module, string? action, string? q,
        DateTime? from, DateTime? to, int skip, int take)
        => _api.GetAsync<HrAuditListResult>($"{Root}/audit?module={module}&action={action}&q={Uri.EscapeDataString(q ?? "")}" +
            $"&from={(from == null ? "" : from.Value.ToString("yyyy-MM-dd"))}&to={(to == null ? "" : to.Value.ToString("yyyy-MM-dd"))}" +
            $"&skip={skip}&take={take}");

    public Task<HrAuditLogDto> GetHrAuditAsync(long id)
        => _api.GetAsync<HrAuditLogDto>($"{Root}/audit/{id}");

    public Task<HrManagerDashboardDto> GetManagerDashboardAsync(int year)
        => _api.GetAsync<HrManagerDashboardDto>($"{Root}/insights?year={year}");

    public Task<HrDataQualityReportDto> GetDataQualityReportAsync()
        => _api.GetAsync<HrDataQualityReportDto>($"{Root}/data-quality");

    public Task<List<HrReportColumnDto>> GetReportMetaAsync(string entity)
        => _api.GetAsync<List<HrReportColumnDto>>($"{Root}/reports/meta?entity={entity}");

    public Task<HrReportResultDto> RunReportAsync(HrReportRunDto dto)
        => _api.PostAsync<HrReportResultDto>($"{Root}/reports/run", dto);

    public Task<List<HrReportTemplateDto>> ListReportTemplatesAsync()
        => _api.GetAsync<List<HrReportTemplateDto>>($"{Root}/reports/templates");

    public Task<HrReportTemplateDto> SaveReportTemplateAsync(HrReportTemplateSaveDto dto)
        => _api.PostAsync<HrReportTemplateDto>($"{Root}/reports/templates", dto);

    public Task DeleteReportTemplateAsync(int id)
        => _api.DeleteAsync($"{Root}/reports/templates/{id}");

    public Task DeleteContractAsync(int id)
        => _api.DeleteAsync($"{Root}/contracts/{id}");

    public Task<List<HrContractTemplateDto>> GetTemplatesAsync()
        => _api.GetAsync<List<HrContractTemplateDto>>($"{Root}/contract-templates");

    public Task<HrContractTemplateDto> SaveTemplateAsync(int? id, HrContractTemplateSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrContractTemplateDto>($"{Root}/contract-templates/{id}", dto)
            : _api.PostAsync<HrContractTemplateDto>($"{Root}/contract-templates", dto);

    public Task DeleteTemplateAsync(int id)
        => _api.DeleteAsync($"{Root}/contract-templates/{id}");

    public Task<HrContractDto> SubmitForSignAsync(int id)
        => _api.PostAsync<HrContractDto>($"{Root}/contracts/{id}/submit-sign", null);

    public Task<HrContractDto> SignEmployeeAsync(int id, string name)
        => _api.PostAsync<HrContractDto>($"{Root}/contracts/{id}/sign-employee", new HrSignNameDto { Name = name });

    public Task<HrContractDto> SignEmployerAsync(int id)
        => _api.PostAsync<HrContractDto>($"{Root}/contracts/{id}/sign-employer", null);

    public Task<List<HrContractVersionDto>> GetVersionsAsync(int contractId)
        => _api.GetAsync<List<HrContractVersionDto>>($"{Root}/contracts/{contractId}/versions");

    public Task<List<HrContractAlertDto>> GetAlertsAsync()
        => _api.GetAsync<List<HrContractAlertDto>>($"{Root}/contract-alerts");

    public Task<int> CheckAlertsAsync(int days = 30)
        => _api.PostAsync<int>($"{Root}/contract-alerts/check?days={days}", null);

    public Task DismissAlertAsync(int id)
        => _api.DeleteAsync($"{Root}/contract-alerts/{id}");

    public Task<List<HrDecreeDto>> GetDecreesAsync(int? employeeId = null, bool? onlyPending = null)
    {
        var qs = new List<string>();
        if (employeeId is > 0) qs.Add($"employeeId={employeeId}");
        if (onlyPending == true) qs.Add("onlyPending=true");
        var s = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<HrDecreeDto>>($"{Root}/decrees{s}");
    }

    public Task<List<HrDecreeDto>> GetEmployeeDecreesAsync(int employeeId)
        => _api.GetAsync<List<HrDecreeDto>>($"{Root}/employees/{employeeId}/decrees");

    public Task<HrDecreeDto> SaveDecreeAsync(int? id, HrDecreeSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrDecreeDto>($"{Root}/decrees/{id}", dto)
            : _api.PostAsync<HrDecreeDto>($"{Root}/decrees", dto);

    public Task<HrDecreeDto> ApplyDecreeAsync(int id)
        => _api.PostAsync<HrDecreeDto>($"{Root}/decrees/{id}/apply", null);

    /// <summary>اجرای همه‌ی احکام اجرانشده‌ای که تاریخ اجرایشان رسیده است — خروجی: تعداد اجراشده</summary>
    public Task<int> ApplyDueDecreesAsync()
        => _api.PostAsync<int>($"{Root}/decrees/apply-due", null);

    public Task DeleteDecreeAsync(int id)
        => _api.DeleteAsync($"{Root}/decrees/{id}");

    public Task<(byte[] Data, string FileName, string ContentType)> GetDecreePdfAsync(int id)
        => _api.GetFileAsync($"{Root}/decrees/{id}/pdf");

    public Task<(byte[] Data, string FileName, string ContentType)> GetContractPdfAsync(int id)
        => _api.GetFileAsync($"{Root}/contracts/{id}/pdf");

    public Task<HrEmployeeDossierDto> GetDossierAsync(int employeeId, int expiringDays = 30)
        => _api.GetAsync<HrEmployeeDossierDto>($"{Root}/employees/{employeeId}/dossier?expiringDays={expiringDays}");

    public Task<List<HrEmployeeDependentDto>> GetDependentsAsync(int employeeId)
        => _api.GetAsync<List<HrEmployeeDependentDto>>($"{Root}/employees/{employeeId}/dependents");

    public Task<HrEmployeeDependentDto> SaveDependentAsync(int employeeId, int? id, HrEmployeeDependentSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrEmployeeDependentDto>($"{Root}/employees/{employeeId}/dependents/{id}", dto)
            : _api.PostAsync<HrEmployeeDependentDto>($"{Root}/employees/{employeeId}/dependents", dto);

    public Task DeleteDependentAsync(int id)
        => _api.DeleteAsync($"{Root}/dependents/{id}");

    public Task<List<HrEmployeeCourseDto>> GetCoursesAsync(int employeeId)
        => _api.GetAsync<List<HrEmployeeCourseDto>>($"{Root}/employees/{employeeId}/courses");

    public Task<HrEmployeeCourseDto> SaveCourseAsync(int employeeId, int? id, HrEmployeeCourseSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrEmployeeCourseDto>($"{Root}/employees/{employeeId}/courses/{id}", dto)
            : _api.PostAsync<HrEmployeeCourseDto>($"{Root}/employees/{employeeId}/courses", dto);

    public Task DeleteCourseAsync(int id)
        => _api.DeleteAsync($"{Root}/courses/{id}");

    public Task<List<HrEmployeeSkillDto>> GetSkillsAsync(int employeeId)
        => _api.GetAsync<List<HrEmployeeSkillDto>>($"{Root}/employees/{employeeId}/skills");

    public Task<HrEmployeeSkillDto> SaveSkillAsync(int employeeId, int? id, HrEmployeeSkillSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrEmployeeSkillDto>($"{Root}/employees/{employeeId}/skills/{id}", dto)
            : _api.PostAsync<HrEmployeeSkillDto>($"{Root}/employees/{employeeId}/skills", dto);

    public Task DeleteSkillAsync(int id)
        => _api.DeleteAsync($"{Root}/skills/{id}");

    public Task<List<HrEmployeeLanguageDto>> GetLanguagesAsync(int employeeId)
        => _api.GetAsync<List<HrEmployeeLanguageDto>>($"{Root}/employees/{employeeId}/languages");

    public Task<HrEmployeeLanguageDto> SaveLanguageAsync(int employeeId, int? id, HrEmployeeLanguageSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrEmployeeLanguageDto>($"{Root}/employees/{employeeId}/languages/{id}", dto)
            : _api.PostAsync<HrEmployeeLanguageDto>($"{Root}/employees/{employeeId}/languages", dto);

    public Task DeleteLanguageAsync(int id)
        => _api.DeleteAsync($"{Root}/languages/{id}");

    public Task<List<HrEmployeeDocumentDto>> GetDocumentsAsync(int employeeId, int expiringDays = 30)
        => _api.GetAsync<List<HrEmployeeDocumentDto>>($"{Root}/employees/{employeeId}/documents?expiringDays={expiringDays}");

    public Task<HrEmployeeDocumentDto> SaveDocumentAsync(int employeeId, int? id, HrEmployeeDocumentSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrEmployeeDocumentDto>($"{Root}/employees/{employeeId}/documents/{id}", dto)
            : _api.PostAsync<HrEmployeeDocumentDto>($"{Root}/employees/{employeeId}/documents", dto);

    public Task DeleteDocumentAsync(int id)
        => _api.DeleteAsync($"{Root}/documents/{id}");

    public Task<List<HrEmployeeDocumentDto>> GetExpiringDocumentsAsync(int days = 30)
        => _api.GetAsync<List<HrEmployeeDocumentDto>>($"{Root}/documents/expiring?days={days}");

    public Task<HrEmployeeDocumentDto> UploadDocumentFileAsync(int docId, Stream stream, string fileName, string? contentType = null)
        => _api.PostFileAsync<HrEmployeeDocumentDto>($"{Root}/documents/{docId}/file", stream, fileName, "file", contentType);

    public Task<(byte[] Data, string FileName, string ContentType)> DownloadDocumentAsync(int docId)
        => _api.GetFileAsync($"{Root}/documents/{docId}/download");

    public Task<(byte[] Data, string FileName, string ContentType)> GetEmployeePhotoAsync(int employeeId)
        => _api.GetFileAsync($"{Root}/employees/{employeeId}/photo");

    public Task<HrEmployeeDto> UploadEmployeePhotoAsync(int employeeId, Stream stream, string fileName, string? contentType = null)
        => _api.PostFileAsync<HrEmployeeDto>($"{Root}/employees/{employeeId}/photo", stream, fileName, "file", contentType);

    public Task<HrEmployeeDto> DeleteEmployeePhotoAsync(int employeeId)
        => _api.DeleteAsync<HrEmployeeDto>($"{Root}/employees/{employeeId}/photo");

    public Task<HrDashboardDto> GetDashboardAsync()
        => _api.GetAsync<HrDashboardDto>($"{Root}/dashboard");

    public Task<List<HrJobPostingDto>> GetPostingsAsync(bool? onlyOpen = null)
        => _api.GetAsync<List<HrJobPostingDto>>($"{Root}/postings{(onlyOpen == true ? "?onlyOpen=true" : "")}");

    public Task<HrJobPostingDto> SavePostingAsync(int? id, HrJobPostingSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrJobPostingDto>($"{Root}/postings/{id}", dto)
            : _api.PostAsync<HrJobPostingDto>($"{Root}/postings", dto);

    public Task DeletePostingAsync(int id)
        => _api.DeleteAsync($"{Root}/postings/{id}");

    public Task<List<HrApplicantDto>> GetApplicantsAsync(int? postingId = null, int? status = null)
    {
        var qs = new List<string>();
        if (postingId is > 0) qs.Add($"postingId={postingId.Value}");
        if (status is >= 0) qs.Add($"status={status.Value}");
        var q = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<HrApplicantDto>>($"{Root}/applicants{q}");
    }

    public Task<HrApplicantDto> SaveApplicantAsync(int? id, HrApplicantSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrApplicantDto>($"{Root}/applicants/{id}", dto)
            : _api.PostAsync<HrApplicantDto>($"{Root}/applicants", dto);

    public Task<HrApplicantDto> MoveApplicantAsync(int id, int status)
        => _api.PostAsync<HrApplicantDto>($"{Root}/applicants/{id}/move?status={status}", new { });

    public Task DeleteApplicantAsync(int id)
        => _api.DeleteAsync($"{Root}/applicants/{id}");

    public Task<HrApplicantDto> ConvertApplicantAsync(int id, string nationalCode, DateTime? hireDate)
        => _api.PostAsync<HrApplicantDto>($"{Root}/applicants/{id}/convert",
            new HrConvertRequestDto { NationalCode = nationalCode, HireDate = hireDate });

    public Task<List<HrInterviewDto>> GetInterviewsAsync(int applicantId)
        => _api.GetAsync<List<HrInterviewDto>>($"{Root}/applicants/{applicantId}/interviews");

    public Task<HrInterviewDto> SaveInterviewAsync(int? id, HrInterviewSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrInterviewDto>($"{Root}/interviews/{id}", dto)
            : _api.PostAsync<HrInterviewDto>($"{Root}/interviews", dto);

    public Task DeleteInterviewAsync(int id)
        => _api.DeleteAsync($"{Root}/interviews/{id}");

    public Task<HrPurgeResultDto> PurgeDemoDataAsync()
        => _api.PostAsync<HrPurgeResultDto>($"{Root}/purge-demo-data", null);
}
