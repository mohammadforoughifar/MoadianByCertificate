using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.HrTalent;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.HrTalent;

/// <summary>
/// ================== استعداد و ارزیابی (HrTalent) ==================
/// آنبوردینگ، ترک‌کار، سوابق شغلی، دوره آزمایشی، ارزیابی عملکرد.
/// مجوزها: مشترک با کارگزینی (HrCore.Read / Create / Update / Delete)
/// </summary>
[Route("api/hr-talent")]
public class HrTalentController : RbacControllerBase
{
    private const string Mod = "HrCore";
    private readonly IHrTalentService _svc;

    public HrTalentController(AppDbContext db, IHrTalentService svc) : base(db) => _svc = svc;

    // ------------------- پرونده‌ی خود پرسنل (بدون نیاز به مجوز کارگزینی) -------------------

    /// <summary>مشاهده‌ی پرونده‌ی خود — هر کاربر واردشده‌ای که پرونده‌ی پرسنلی متصل دارد</summary>
    [HttpGet("my/profile")]
    public async Task<IActionResult> MyProfile()
    {
        var p = await _svc.GetMyProfileAsync(MyUserId);
        return p is null ? NotFound(new { message = "پرونده‌ی پرسنلی برای حساب شما ثبت نشده است." }) : Ok(p);
    }

    /// <summary>ثبت درخواست ویرایش فیلدهای تماس پرونده — پس از تأیید HR اعمال می‌شود</summary>
    [HttpPost("my/profile-requests")]
    public async Task<IActionResult> SubmitMyProfileEdit([FromBody] HrProfileEditRequestSaveDto dto)
    {
        var id = await _svc.SubmitMyProfileEditAsync(MyUserId, dto);
        return Ok(new { id });
    }

    [HttpGet("profile-requests")]
    public async Task<IActionResult> ProfileRequests([FromQuery] bool? onlyPending, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListProfileRequestsAsync(onlyPending, pagination: pagination), skip, take));
    }

    /// <summary>تأیید (و اعمال روی پرونده) یا رد درخواست ویرایش</summary>
    [HttpPost("profile-requests/{id:int}/decide")]
    public async Task<IActionResult> DecideProfileRequest(int id, [FromQuery] bool approve, [FromQuery] string? note)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.DecideProfileRequestAsync(id, approve, MyUsername, note));
    }

    // ------------------- آنبوردینگ -------------------

    [HttpGet("onboarding")]
    public async Task<IActionResult> Onboarding([FromQuery] bool? onlyOpen, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListOnboardingAsync(onlyOpen, pagination: pagination), skip, take));
    }

    [HttpPost("onboarding")]
    public async Task<IActionResult> CreateOnboarding([FromBody] HrTalentCaseSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        try { return Ok(await _svc.CreateOnboardingAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("onboarding/items/{itemId:int}")]
    public async Task<IActionResult> SetOnboardingItem(int itemId, [FromQuery] int status, [FromQuery] string? doneBy)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { return Ok(await _svc.SetOnboardingItemAsync(itemId, status, doneBy)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("onboarding/{id:int}/items")]
    public async Task<IActionResult> AddOnboardingItem(int id, [FromBody] HrTalentItemSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { return Ok(await _svc.AddOnboardingItemAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("onboarding/items/{itemId:int}")]
    public async Task<IActionResult> DeleteOnboardingItem(int itemId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        try { await _svc.DeleteOnboardingItemAsync(itemId); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("onboarding/{id:int}/complete")]
    public async Task<IActionResult> CompleteOnboarding(int id, [FromQuery] string? by)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { return Ok(await _svc.CompleteOnboardingAsync(id, by)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    // ------------------- ترک‌کار -------------------

    [HttpGet("exit")]
    public async Task<IActionResult> ExitCases([FromQuery] bool? onlyOpen, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListExitCasesAsync(onlyOpen, pagination: pagination), skip, take));
    }

    [HttpPost("exit")]
    public async Task<IActionResult> CreateExit([FromBody] HrTalentCaseSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        try { return Ok(await _svc.CreateExitCaseAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("exit/items/{itemId:int}")]
    public async Task<IActionResult> SetExitItem(int itemId, [FromQuery] int status, [FromQuery] string? doneBy)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { return Ok(await _svc.SetExitItemAsync(itemId, status, doneBy)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("exit/{id:int}/items")]
    public async Task<IActionResult> AddExitItem(int id, [FromBody] HrTalentItemSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { return Ok(await _svc.AddExitItemAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("exit/items/{itemId:int}")]
    public async Task<IActionResult> DeleteExitItem(int itemId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        try { await _svc.DeleteExitItemAsync(itemId); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("exit/{id:int}/complete")]
    public async Task<IActionResult> CompleteExit(int id, [FromQuery] string? by)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { return Ok(await _svc.CompleteExitCaseAsync(id, by)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    // ------------------- سوابق شغلی -------------------

    [HttpGet("history/{employeeId:int}")]
    public async Task<IActionResult> History(int employeeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(pagination => _svc.EmployeeHistoryAsync(employeeId, pagination), skip, take));
    }

    [HttpPost("history")]
    public async Task<IActionResult> SaveHistory([FromBody] HrJobHistorySaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        try { return Ok(await _svc.SaveHistoryAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("history/{id:int}")]
    public async Task<IActionResult> DeleteHistory(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        try { await _svc.DeleteHistoryAsync(id); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    // ------------------- دوره آزمایشی -------------------

    [HttpGet("trials")]
    public async Task<IActionResult> Trials([FromQuery] bool? onlyActive, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListTrialsAsync(onlyActive, pagination: pagination), skip, take));
    }

    [HttpPost("trials")]
    public async Task<IActionResult> SaveTrial([FromBody] HrTrialSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        try { return Ok(await _svc.SaveTrialAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("trials/{id:int}")]
    public async Task<IActionResult> DeleteTrial(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        try { await _svc.DeleteTrialAsync(id); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("trials/{id:int}/decide")]
    public async Task<IActionResult> DecideTrial(int id, [FromBody] HrTrialDecideDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { return Ok(await _svc.DecideTrialAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("trials/auto")]
    public async Task<IActionResult> AutoTrials()
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(new { count = await _svc.AutoCreateTrialsAsync() });
    }

    // ------------------- ارزیابی عملکرد -------------------

    [HttpGet("appraisals")]
    public async Task<IActionResult> Appraisals([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListAppraisalsAsync(pagination: pagination), skip, take));
    }

    [HttpPost("appraisals")]
    public async Task<IActionResult> SaveAppraisal([FromQuery] int? id, [FromBody] HrAppraisalSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, id is > 0 ? "Update" : "Create") is { } f) return f;
        try { return Ok(await _svc.SaveAppraisalAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("appraisals/{id:int}")]
    public async Task<IActionResult> DeleteAppraisal(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        try { await _svc.DeleteAppraisalAsync(id); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("appraisals/{id:int}/kpis")]
    public async Task<IActionResult> Kpis(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListKpisAsync(id, pagination: pagination), skip, take));
    }

    [HttpPost("appraisals/{id:int}/kpis")]
    public async Task<IActionResult> SaveKpi(int id, [FromQuery] int? kpiId, [FromBody] HrAppraisalKpiSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { return Ok(await _svc.SaveKpiAsync(id, kpiId, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("appraisals/kpis/{kpiId:int}")]
    public async Task<IActionResult> DeleteKpi(int kpiId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        try { await _svc.DeleteKpiAsync(kpiId); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("appraisals/scores")]
    public async Task<IActionResult> SaveScore([FromBody] HrAppraisalScoreSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { await _svc.SaveScoreAsync(dto); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("appraisals/{id:int}/scores")]
    public async Task<IActionResult> Scores(int id, [FromQuery] int? employeeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListScoresAsync(id, employeeId, pagination: pagination), skip, take));
    }

    [HttpGet("appraisals/{id:int}/results")]
    public async Task<IActionResult> Results(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(pagination => _svc.AppraisalResultsAsync(id, pagination), skip, take));
    }

    /// <summary>صدور حکم افزایش حقوق/ارتقا برای نفرات برتر این ارزیابی (گرید A یا A+B)</summary>
    [HttpPost("appraisals/{id:int}/propose-decrees")]
    public async Task<IActionResult> ProposeDecrees(int id, [FromBody] HrAppraisalDecreeProposalDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        dto.AppraisalId = id;
        return Ok(await _svc.ProposeDecreesFromAppraisalAsync(dto, MyUserId, MyUsername));
    }

    [HttpPost("appraisals/{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromQuery] int status, [FromQuery] string? by)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        try { return Ok(await _svc.SetAppraisalStatusAsync(id, status, by)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }
}
