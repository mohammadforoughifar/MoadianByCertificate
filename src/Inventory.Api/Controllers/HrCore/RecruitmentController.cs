using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.HrCore;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.HrCore;

/// <summary>جذب و استخدام (§۴.۱): آگهی، متقاضی، مصاحبه.</summary>
[ApiController]
[Route("api/hr-core")]
public class RecruitmentController : RbacControllerBase
{
    private const string Mod = "HrCore";
    private const string Sec = "Recruitment";
    private readonly IHrRecruitmentService _svc;

    public RecruitmentController(AppDbContext db, IHrRecruitmentService svc) : base(db) => _svc = svc;

    // ------------------- آگهی -------------------

    [HttpGet("postings")]
    public async Task<IActionResult> Postings([FromQuery] bool? onlyOpen, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", Sec) is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListPostingsAsync(onlyOpen, pagination: pagination), skip, take));
    }

    [HttpGet("postings/{id:int}")]
    public async Task<IActionResult> Posting(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", Sec) is { } f) return f;
        var p = await _svc.GetPostingAsync(id);
        return p == null ? NotFound() : Ok(p);
    }

    [HttpPost("postings")]
    public async Task<IActionResult> CreatePosting([FromBody] HrJobPostingSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Create", Sec) is { } f) return f;
        try { return Ok(await _svc.SavePostingAsync(null, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("postings/{id:int}")]
    public async Task<IActionResult> UpdatePosting(int id, [FromBody] HrJobPostingSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", Sec) is { } f) return f;
        try { return Ok(await _svc.SavePostingAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("postings/{id:int}")]
    public async Task<IActionResult> DeletePosting(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Delete", Sec) is { } f) return f;
        try { await _svc.DeletePostingAsync(id); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ------------------- متقاضی -------------------

    [HttpGet("applicants")]
    public async Task<IActionResult> Applicants([FromQuery] int? postingId, [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", Sec) is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListApplicantsAsync(postingId, status, pagination: pagination), skip, take));
    }

    [HttpGet("applicants/{id:int}")]
    public async Task<IActionResult> Applicant(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", Sec) is { } f) return f;
        var a = await _svc.GetApplicantAsync(id);
        return a == null ? NotFound() : Ok(a);
    }

    [HttpPost("applicants")]
    public async Task<IActionResult> CreateApplicant([FromBody] HrApplicantSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Create", Sec) is { } f) return f;
        try { return Ok(await _svc.SaveApplicantAsync(null, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("applicants/{id:int}")]
    public async Task<IActionResult> UpdateApplicant(int id, [FromBody] HrApplicantSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", Sec) is { } f) return f;
        try { return Ok(await _svc.SaveApplicantAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("applicants/{id:int}")]
    public async Task<IActionResult> DeleteApplicant(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Delete", Sec) is { } f) return f;
        try { await _svc.DeleteApplicantAsync(id); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("applicants/{id:int}/move")]
    public async Task<IActionResult> MoveApplicant(int id, [FromQuery] int status)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", Sec) is { } f) return f;
        try { return Ok(await _svc.MoveApplicantAsync(id, status)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("applicants/{id:int}/convert")]
    public async Task<IActionResult> ConvertApplicant(int id, [FromBody] HrConvertRequestDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Create", Sec) is { } f) return f;
        try { return Ok(await _svc.ConvertToEmployeeAsync(id, dto.NationalCode, dto.HireDate)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ------------------- مصاحبه -------------------

    [HttpGet("applicants/{id:int}/interviews")]
    public async Task<IActionResult> Interviews(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", Sec) is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListInterviewsAsync(id, pagination: pagination), skip, take));
    }

    [HttpPost("interviews")]
    public async Task<IActionResult> CreateInterview([FromBody] HrInterviewSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Create", Sec) is { } f) return f;
        try { return Ok(await _svc.SaveInterviewAsync(null, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("interviews/{id:int}")]
    public async Task<IActionResult> UpdateInterview(int id, [FromBody] HrInterviewSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", Sec) is { } f) return f;
        try { return Ok(await _svc.SaveInterviewAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("interviews/{id:int}")]
    public async Task<IActionResult> DeleteInterview(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Delete", Sec) is { } f) return f;
        try { await _svc.DeleteInterviewAsync(id); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
