using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.HrCore;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace Inventory.Api.Controllers.HrCore;

/// <summary>
/// ================== هسته پرسنلی (کارگزینی) ==================
/// ماژول مستقل HrCore: پرونده پرسنل، ساختار سازمانی، قراردادها، احکام.
/// مجوزها: HrCore.Read / Create / Update / Delete / Manage
/// </summary>
[Route("api/hr-core")]
public class HrCoreController : RbacControllerBase
{
    private const string Mod = "HrCore";
    private readonly IHrCoreService _svc;
    private readonly FileStore _files;
    private readonly Inventory.Api.Services.HrReports.IHrReportService _reports;
    private readonly IConfiguration _config;

    public HrCoreController(AppDbContext db, IHrCoreService svc, FileStore files,
        Inventory.Api.Services.HrReports.IHrReportService reports, IConfiguration config) : base(db)
    {
        _svc = svc;
        _files = files;
        _reports = reports;
        _config = config;
    }

    /// <summary>
    /// دامنه‌ی دید تیمی: کاربر دارای HrCore.Manage همه را می‌بیند (null)؛ کاربر فقط-خواندنی
    /// به پرونده‌ی پرسنلِ گره سازمانی خودش + زیرمجموعه‌ها محدود می‌شود (دسترسی مبتنی به واحد).
    /// </summary>
    private async Task<HrTeamScopeDto?> TeamScopeAsync()
    {
        if (await HasAsync(Mod, "Manage")) return null;
        return await _svc.ResolveTeamScopeAsync(MyUserId);
    }

    // ------------------- پرسنل -------------------

    [HttpGet("employees")]
    public async Task<IActionResult> SearchEmployees([FromQuery] string? q, [FromQuery] int? orgUnitId,
        [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] int? hrMainNodeId = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var (items, total) = await _svc.SearchEmployeesAsync(q, orgUnitId, status, skip, take, hrMainNodeId, await TeamScopeAsync());
        return Ok(new { total, items });
    }

    [HttpGet("employees/{id:int}")]
    public async Task<IActionResult> GetEmployee(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var e = await _svc.GetEmployeeAsync(id, await TeamScopeAsync());
        return e is null ? NotFound(new { message = "پرسنل یافت نشد." }) : Ok(e);
    }

    [HttpGet("employees/next-code")]
    public async Task<IActionResult> NextCode()
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(new { code = await _svc.NextEmployeeCodeAsync() });
    }

    [HttpGet("employees/lite")]
    public async Task<IActionResult> EmployeeLite([FromQuery] bool onlyActive = true, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListEmployeeLiteAsync(onlyActive, await TeamScopeAsync(), pagination: pagination), skip, take));
    }

    [HttpGet("employees/import-template")]
    public async Task<IActionResult> EmployeeImportTemplate()
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        var (data, name) = await _svc.BuildEmployeeImportTemplateAsync();
        return File(data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    [HttpPost("employees/import")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> ImportEmployees(IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        if (file == null || file.Length == 0) return BadRequest("فایلی انتخاب نشده است.");
        try
        {
            await using var s = file.OpenReadStream();
            return Ok(await _svc.ImportEmployeesAsync(s));
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("employees")]
    public async Task<IActionResult> CreateEmployee([FromBody] HrEmployeeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.CreateEmployeeAsync(dto));
    }

    [HttpPut("employees/{id:int}")]
    public async Task<IActionResult> UpdateEmployee(int id, [FromBody] HrEmployeeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.UpdateEmployeeAsync(id, dto));
    }

    [HttpPost("employees/{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, [FromQuery] bool active)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.SetEmployeeActiveAsync(id, active);
        return Ok(new { ok = true });
    }

    // ------------------- ساختار سازمانی -------------------

    [HttpGet("org/tree")]
    public async Task<IActionResult> OrgTree([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(pagination => _svc.GetTreeAsync(pagination), skip, take));
    }

    [HttpGet("org/units")]
    public async Task<IActionResult> OrgUnits([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListUnitsAsync(pagination: pagination), skip, take));
    }

    [HttpPost("org/units")]
    public async Task<IActionResult> CreateUnit([FromBody] HrOrgUnitSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveUnitAsync(null, dto));
    }

    [HttpPut("org/units/{id:int}")]
    public async Task<IActionResult> UpdateUnit(int id, [FromBody] HrOrgUnitSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveUnitAsync(id, dto));
    }

    [HttpDelete("org/units/{id:int}")]
    public async Task<IActionResult> DeleteUnit(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteUnitAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- قراردادها -------------------

    [HttpGet("contracts")]
    public async Task<IActionResult> Contracts([FromQuery] bool? onlyActive, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListContractsAsync(onlyActive, pagination: pagination), skip, take));
    }

    [HttpGet("contracts/expiring")]
    public async Task<IActionResult> Expiring([FromQuery] int days = 30, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ExpiringContractsAsync(days, pagination: pagination), skip, take));
    }

    [HttpGet("employees/{id:int}/contracts")]
    public async Task<IActionResult> EmployeeContracts(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.EmployeeContractsAsync(id, pagination: pagination), skip, take));
    }

    [HttpPost("contracts")]
    public async Task<IActionResult> CreateContract([FromBody] HrContractSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveContractAsync(null, dto, MyUserId, MyUsername));
    }

    [HttpPost("contracts/bulk")]
    public async Task<IActionResult> CreateContractsBulk([FromBody] HrContractBulkDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveContractsBulkAsync(dto, MyUserId, MyUsername));
    }

    [HttpPut("contracts/{id:int}")]
    public async Task<IActionResult> UpdateContract(int id, [FromBody] HrContractSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveContractAsync(id, dto, MyUserId, MyUsername));
    }

    [HttpDelete("contracts/{id:int}")]
    public async Task<IActionResult> DeleteContract(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteContractAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- قالب‌های قرارداد (§۹) -------------------

    [HttpGet("contract-templates")]
    public async Task<IActionResult> Templates([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListTemplatesAsync(pagination: pagination), skip, take));
    }

    [HttpPost("contract-templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] HrContractTemplateSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveTemplateAsync(null, dto));
    }

    [HttpPut("contract-templates/{id:int}")]
    public async Task<IActionResult> UpdateTemplate(int id, [FromBody] HrContractTemplateSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveTemplateAsync(id, dto));
    }

    [HttpDelete("contract-templates/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteTemplateAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- امضا و نسخه‌های قرارداد (§۹) -------------------

    [HttpPost("contracts/{id:int}/submit-sign")]
    public async Task<IActionResult> SubmitForSign(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SubmitForSignAsync(id));
    }

    [HttpPost("contracts/{id:int}/sign-employee")]
    public async Task<IActionResult> SignEmployee(int id, [FromBody] HrSignNameDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SignEmployeeAsync(id, dto?.Name ?? ""));
    }

    [HttpPost("contracts/{id:int}/sign-employer")]
    public async Task<IActionResult> SignEmployer(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SignEmployerAsync(id, MyUserId, MyUsername));
    }

    [HttpGet("contracts/{id:int}/versions")]
    public async Task<IActionResult> ContractVersions(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListVersionsAsync(id, pagination: pagination), skip, take));
    }

    [HttpGet("contracts/{id:int}/pdf")]
    public async Task<IActionResult> ContractPdf(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Contracts") is { } f) return f;
        try { return File(await _svc.ContractPdfAsync(id), "application/pdf", $"HrContract-{id}.pdf"); }
        catch (InvalidOperationException) { return NotFound(); }
    }

    [HttpPost("contracts/{id:int}/renew")]
    public async Task<IActionResult> RenewContract(int id, [FromQuery] int months = 12)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { return Ok(await _svc.RenewContractAsync(id, months)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("documents/remind")]
    public async Task<IActionResult> RemindDocuments([FromQuery] int days = 30)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(new { count = await _svc.RemindExpiringDocumentsAsync(days) });
    }

    [HttpGet("employees/{id:int}/dossier-pdf")]
    public async Task<IActionResult> DossierPdf(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        try { return File(await _svc.DossierPdfAsync(id), "application/pdf", $"Dossier-{id}.pdf"); }
        catch (InvalidOperationException) { return NotFound(); }
    }

    /// <summary>صدور خودکار گواهی اشتغال به کار — با یک کلیک، بدون تایپ دستی</summary>
    [HttpGet("employees/{id:int}/employment-certificate")]
    public async Task<IActionResult> EmploymentCertificate(int id, [FromQuery] string? purpose)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        try { return File(await _svc.EmploymentCertificatePdfAsync(id, purpose), "application/pdf", $"EmploymentCertificate-{id}.pdf"); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("employees/export")]
    public async Task<IActionResult> ExportEmployees([FromQuery] string? q)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var (data, name) = await _svc.ExportEmployeesExcelAsync(q, await TeamScopeAsync());
        return File(data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    [HttpGet("contracts/export")]
    public async Task<IActionResult> ExportContracts()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var (data, name) = await _svc.ExportContractsExcelAsync();
        return File(data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    [HttpGet("decrees/export")]
    public async Task<IActionResult> ExportDecrees()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var (data, name) = await _svc.ExportDecreesExcelAsync();
        return File(data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    [HttpGet("org/export")]
    public async Task<IActionResult> ExportOrg()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var (data, name) = await _svc.ExportOrgExcelAsync();
        return File(data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    // ------------------- هشدارهای انقضای قرارداد (§۹) -------------------

    [HttpGet("contract-alerts")]
    public async Task<IActionResult> Alerts([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListAlertsAsync(pagination: pagination), skip, take));
    }

    [HttpPost("contract-alerts/check")]
    public async Task<IActionResult> CheckAlerts([FromQuery] int days = 30)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.CheckAlertsAsync(days));
    }

    [HttpDelete("contract-alerts/{id:int}")]
    public async Task<IActionResult> DismissAlert(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DismissAlertAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- احکام -------------------

    [HttpGet("decrees")]
    public async Task<IActionResult> Decrees([FromQuery] int? employeeId, [FromQuery] bool? onlyPending, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListDecreesAsync(employeeId, onlyPending, pagination: pagination), skip, take));
    }

    [HttpGet("employees/{id:int}/decrees")]
    public async Task<IActionResult> EmployeeDecrees(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.EmployeeDecreesAsync(id, pagination: pagination), skip, take));
    }

    [HttpGet("decrees/{id:int}/pdf")]
    public async Task<IActionResult> DecreePdf(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Decrees") is { } f) return f;
        try { return File(await _svc.DecreePdfAsync(id), "application/pdf", $"HrDecree-{id}.pdf"); }
        catch (InvalidOperationException) { return NotFound(); }
    }

    [HttpPost("decrees")]
    public async Task<IActionResult> CreateDecree([FromBody] HrDecreeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveDecreeAsync(null, dto, MyUserId, MyUsername));
    }

    [HttpPost("decrees/bulk")]
    public async Task<IActionResult> CreateDecreesBulk([FromBody] HrDecreeBulkDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveDecreesBulkAsync(dto, MyUserId, MyUsername));
    }

    [HttpPut("decrees/{id:int}")]
    public async Task<IActionResult> UpdateDecree(int id, [FromBody] HrDecreeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveDecreeAsync(id, dto, MyUserId, MyUsername));
    }

    [HttpPost("decrees/{id:int}/apply")]
    public async Task<IActionResult> ApplyDecree(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.ApplyDecreeAsync(id));
    }

    /// <summary>اجرای همه‌ی احکامِ اجرانشده‌ای که تاریخ اجرایشان رسیده است (واچر روزانه هم همین را صدا می‌زند).</summary>
    [HttpPost("decrees/apply-due")]
    public async Task<IActionResult> ApplyDueDecrees()
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        var n = await _svc.ApplyDueDecreesAsync();
        return Ok(new { applied = n });
    }

    [HttpDelete("decrees/{id:int}")]
    public async Task<IActionResult> DeleteDecree(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteDecreeAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- پرونده کارمندان -------------------

    [HttpGet("employees/{id:int}/dossier")]
    public async Task<IActionResult> Dossier(int id, [FromQuery] int expiringDays = 30)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetDossierAsync(id, expiringDays));
    }

    // ----- تحت‌تکفل -----

    [HttpGet("employees/{id:int}/dependents")]
    public async Task<IActionResult> Dependents(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListDependentsAsync(id, pagination: pagination), skip, take));
    }

    [HttpPost("employees/{id:int}/dependents")]
    public async Task<IActionResult> CreateDependent(int id, [FromBody] HrEmployeeDependentSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveDependentAsync(id, null, dto));
    }

    [HttpPut("employees/{id:int}/dependents/{depId:int}")]
    public async Task<IActionResult> UpdateDependent(int id, int depId, [FromBody] HrEmployeeDependentSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveDependentAsync(id, depId, dto));
    }

    [HttpDelete("dependents/{depId:int}")]
    public async Task<IActionResult> DeleteDependent(int depId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteDependentAsync(depId);
        return Ok(new { ok = true });
    }

    // ----- دوره‌های آموزشی -----

    [HttpGet("employees/{id:int}/courses")]
    public async Task<IActionResult> Courses(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListCoursesAsync(id, pagination: pagination), skip, take));
    }

    [HttpPost("employees/{id:int}/courses")]
    public async Task<IActionResult> CreateCourse(int id, [FromBody] HrEmployeeCourseSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveCourseAsync(id, null, dto));
    }

    [HttpPut("employees/{id:int}/courses/{courseId:int}")]
    public async Task<IActionResult> UpdateCourse(int id, int courseId, [FromBody] HrEmployeeCourseSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveCourseAsync(id, courseId, dto));
    }

    [HttpDelete("courses/{courseId:int}")]
    public async Task<IActionResult> DeleteCourse(int courseId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteCourseAsync(courseId);
        return Ok(new { ok = true });
    }

    // ----- مهارت‌ها -----

    [HttpGet("employees/{id:int}/skills")]
    public async Task<IActionResult> Skills(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListSkillsAsync(id, pagination: pagination), skip, take));
    }

    [HttpPost("employees/{id:int}/skills")]
    public async Task<IActionResult> CreateSkill(int id, [FromBody] HrEmployeeSkillSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveSkillAsync(id, null, dto));
    }

    [HttpPut("employees/{id:int}/skills/{skillId:int}")]
    public async Task<IActionResult> UpdateSkill(int id, int skillId, [FromBody] HrEmployeeSkillSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveSkillAsync(id, skillId, dto));
    }

    [HttpDelete("skills/{skillId:int}")]
    public async Task<IActionResult> DeleteSkill(int skillId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteSkillAsync(skillId);
        return Ok(new { ok = true });
    }

    // ----- زبان‌های خارجی -----

    [HttpGet("employees/{id:int}/languages")]
    public async Task<IActionResult> Languages(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListLanguagesAsync(id, pagination: pagination), skip, take));
    }

    [HttpPost("employees/{id:int}/languages")]
    public async Task<IActionResult> CreateLanguage(int id, [FromBody] HrEmployeeLanguageSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveLanguageAsync(id, null, dto));
    }

    [HttpPut("employees/{id:int}/languages/{langId:int}")]
    public async Task<IActionResult> UpdateLanguage(int id, int langId, [FromBody] HrEmployeeLanguageSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveLanguageAsync(id, langId, dto));
    }

    [HttpDelete("languages/{langId:int}")]
    public async Task<IActionResult> DeleteLanguage(int langId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteLanguageAsync(langId);
        return Ok(new { ok = true });
    }

    // ----- اسناد -----

    [HttpGet("employees/{id:int}/documents")]
    public async Task<IActionResult> Documents(int id, [FromQuery] int expiringDays = 30, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListDocumentsAsync(id, expiringDays, pagination: pagination), skip, take));
    }

    [HttpPost("employees/{id:int}/documents")]
    public async Task<IActionResult> CreateDocument(int id, [FromBody] HrEmployeeDocumentSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveDocumentAsync(id, null, dto));
    }

    [HttpPut("employees/{id:int}/documents/{docId:int}")]
    public async Task<IActionResult> UpdateDocument(int id, int docId, [FromBody] HrEmployeeDocumentSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveDocumentAsync(id, docId, dto));
    }

    [HttpDelete("documents/{docId:int}")]
    public async Task<IActionResult> DeleteDocument(int docId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        var doc = await _svc.GetDocumentAsync(docId);
        await _svc.DeleteDocumentAsync(docId);
        _files.Delete(doc?.FilePath);
        return Ok(new { ok = true });
    }

    [HttpGet("documents/expiring")]
    public async Task<IActionResult> ExpiringDocuments([FromQuery] int days = 30, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ExpiringDocumentsAsync(days, pagination: pagination), skip, take));
    }

    [HttpPost("documents/{docId:int}/file")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadDocumentFile(int docId, IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        if (file is null || file.Length == 0) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        if (file.Length > 20 * 1024 * 1024) return BadRequest(new { message = "حداکثر حجم فایل ۲۰ مگابایت است." });
        // محدودیت نوع/پسوند فایل برداشته شد — هر نوع فایلی مجاز است
        var doc = await _svc.GetDocumentAsync(docId);
        if (doc is null) return NotFound(new { message = "سند یافت نشد." });
        _files.Delete(doc.FilePath);
        await using var stream = file.OpenReadStream();
        var path = await _files.SaveAsync("hr/employee/documents", docId, stream, file.FileName);
        return Ok(await _svc.AttachDocumentFileAsync(docId, path, Path.GetFileName(file.FileName),
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType, file.Length));
    }

    [HttpGet("documents/{docId:int}/download")]
    public async Task<IActionResult> DownloadDocument(int docId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var doc = await _svc.GetDocumentAsync(docId);
        if (doc is null || string.IsNullOrWhiteSpace(doc.FilePath)) return NotFound(new { message = "فایل یافت نشد." });
        var bytes = _files.ReadBytes(doc.FilePath);
        if (bytes is null) return NotFound(new { message = "فایل روی دیسک موجود نیست." });
        return File(bytes, doc.ContentType ?? "application/octet-stream", doc.FileName ?? $"document-{docId}");
    }

    // ----- عکس پروفایل -----

    [HttpGet("employees/{id:int}/photo")]
    public async Task<IActionResult> EmployeePhoto(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var e = await _svc.GetEmployeeAsync(id, await TeamScopeAsync());
        if (e is null || string.IsNullOrWhiteSpace(e.PhotoPath)) return NotFound();
        var bytes = _files.ReadBytes(e.PhotoPath);
        if (bytes is null) return NotFound();
        return File(bytes, ContentTypeForPhoto(e.PhotoPath));
    }

    [HttpPost("employees/{id:int}/photo")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadEmployeePhoto(int id, IFormFile file)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        if (file is null || file.Length == 0) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        if (file.Length > 5 * 1024 * 1024) return BadRequest(new { message = "حداکثر حجم عکس ۵ مگابایت است." });
        // محدودیت نوع/پسوند فایل برداشته شد — هر نوع فایلی مجاز است
        var e = await _svc.GetEmployeeAsync(id);
        if (e is null) return NotFound(new { message = "پرسنل یافت نشد." });
        _files.Delete(e.PhotoPath);
        await using var stream = file.OpenReadStream();
        var path = await _files.SaveAsync("hr/employee/photos", id, stream, file.FileName);
        return Ok(await _svc.SetEmployeePhotoAsync(id, path));
    }

    [HttpDelete("employees/{id:int}/photo")]
    public async Task<IActionResult> DeleteEmployeePhoto(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        var e = await _svc.GetEmployeeAsync(id);
        if (e is null) return NotFound(new { message = "پرسنل یافت نشد." });
        _files.Delete(e.PhotoPath);
        return Ok(await _svc.SetEmployeePhotoAsync(id, null));
    }

    private static readonly HashSet<string> AllowedPhotoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly HashSet<string> BlockedDocExts = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".ps1", ".sh", ".dll", ".msi", ".com", ".scr", ".jar" };
    private static string ContentTypeForPhoto(string? path) =>
        Path.GetExtension(path ?? "").ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

    // ------------------- داشبورد -------------------

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] int expiringDays = 30)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.DashboardAsync(expiringDays));
    }

    // ------------------- تاریخچه عملیات HR -------------------

    [HttpGet("audit")]
    public async Task<IActionResult> SearchAudit([FromQuery] string? module, [FromQuery] string? action,
        [FromQuery] string? q, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int skip = 0, [FromQuery] int take = 25)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SearchHrAuditAsync(module, action, q, from, to, skip, take));
    }

    [HttpGet("audit/{id:long}")]
    public async Task<IActionResult> GetAudit(long id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        var r = await _svc.GetHrAuditAsync(id);
        return r == null ? NotFound() : Ok(r);
    }

    [HttpGet("insights")]
    public async Task<IActionResult> Insights([FromQuery] int year = 0)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.GetManagerDashboardAsync(year));
    }

    // ------------------- گزارش کیفیت داده پرسنل -------------------

    [HttpGet("data-quality")]
    public async Task<IActionResult> DataQuality()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.GetDataQualityReportAsync());
    }

    // ------------------- گزارش‌ساز سفارشی (§۶) -------------------

    [HttpGet("reports/meta")]
    public async Task<IActionResult> ReportMeta([FromQuery] string entity = "employee")
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(_reports.Meta(entity));
    }

    [HttpPost("reports/run")]
    public async Task<IActionResult> RunReport([FromBody] HrReportRunDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _reports.RunAsync(dto));
    }

    [HttpGet("reports/templates")]
    public async Task<IActionResult> ListTemplates([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await Paging.ResultAsync(pagination => _reports.ListTemplatesAsync(MyUserId, pagination), skip, take));
    }

    [HttpPost("reports/templates")]
    public async Task<IActionResult> SaveTemplate([FromBody] HrReportTemplateSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _reports.SaveTemplateAsync(MyUserId, dto));
    }

    [HttpDelete("reports/templates/{id:int}")]
    public async Task<IActionResult> DeleteReportTemplate(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _reports.DeleteTemplateAsync(MyUserId, id);
        return NoContent();
    }

    /// <summary>
    /// پاک‌سازی کامل داده‌های منابع انسانی (برای حذف داده‌های آزمایشی/فیک).
    /// فقط ادمین سامانه یا دارای مجوز Settings.Manage.
    /// داده‌های پایه (انواع مرخصی، شیفت‌ها، اقلام/تنظیمات حقوق، قالب قرارداد)، کاربران و نقش‌ها حفظ می‌شوند.
    /// </summary>
    [HttpPost("purge-demo-data")]
    public async Task<IActionResult> PurgeDemoData(CancellationToken ct)
    {
        if (!User.IsInRole("Admin") && !await HasAsync("Settings", "Manage"))
            return StatusCode(403, new { message = "تنها مدیر سامانه مجاز به پاک‌سازی داده‌های منابع انسانی است." });

        var result = await HrDemoPurger.PurgeAsync(Db, ct);
        var seedFlag = _config["Database:SeedDemoData"];
        var warning = string.Equals(seedFlag, "true", StringComparison.OrdinalIgnoreCase)
            ? "هشدار: فلگ Database:SeedDemoData=true است؛ با اولین ری‌استارتِ جداولِ خالی، داده‌ی نمونه دوباره ساخته می‌شود. این فلگ را false کنید (appsettings یا متغیر محیطی Database__SeedDemoData)."
            : null;
        return Ok(new HrPurgeResultDto(
            $"پاک‌سازی انجام شد — {result.Total} رکورد حذف شد.",
            result.Total, result.Areas, warning));
    }
}
