using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.FaPay;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.FaPay;

/// <summary>
/// ================== حقوق و دستمزد فروغ آریا — افزونه‌های §۲ ==================
/// وام و مساعده، معوقات، عیدی و سنوات، تسویه پایان همکاری، فایل بیمه، مقایسه دوره‌ها.
/// مجوزها: FaPay.Read / FaPay.Manage — سیستم قدیمی (HrPay) دست نمی‌خورد.
/// </summary>
[Route("api/fa-pay")]
public class FaPayExtraController : RbacControllerBase
{
    private const string Mod = "FaPay";
    private readonly IFaPayExtraService _svc;

    public FaPayExtraController(AppDbContext db, IFaPayExtraService svc) : base(db) => _svc = svc;

    // ------------------- تنظیمات تکمیلی -------------------

    [HttpGet("extra-settings")]
    public async Task<IActionResult> ExtraSettings()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetExtraSettingsAsync());
    }

    [HttpPut("extra-settings")]
    public async Task<IActionResult> SaveExtraSettings([FromBody] FaPayExtraSettingsSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveExtraSettingsAsync(dto));
    }

    // ------------------- حداقل دستمزد قانونی (جدول اختصاصی FaPayMinWages) -------------------

    [HttpGet("min-wages")]
    public async Task<IActionResult> MinWages()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ListMinWagesAsync());
    }

    [HttpPost("min-wages")]
    public async Task<IActionResult> SaveMinWage([FromBody] FaPayMinWageSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { return Ok(await _svc.SaveMinWageAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("min-wages/{year:int}")]
    public async Task<IActionResult> DeleteMinWage(int year)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        try { await _svc.DeleteMinWageAsync(year); return Ok(new { ok = true }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ------------------- وام و مساعده -------------------

    [HttpGet("loans")]
    public async Task<IActionResult> Loans([FromQuery] int? employeeId, [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListLoansAsync(employeeId, status, pagination: pagination), skip, take));
    }

    [HttpGet("loans/{id:int}")]
    public async Task<IActionResult> Loan(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var l = await _svc.GetLoanAsync(id);
        return l == null ? NotFound() : Ok(l);
    }

    [HttpPost("loans")]
    public async Task<IActionResult> CreateLoan([FromBody] FaPayLoanSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.CreateLoanAsync(dto, MyUserId, MyUsername));
    }

    [HttpPost("loans/{id:int}/cancel")]
    public async Task<IActionResult> CancelLoan(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.CancelLoanAsync(id);
        return Ok(true);
    }

    [HttpDelete("loans/{id:int}")]
    public async Task<IActionResult> DeleteLoan(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteLoanAsync(id);
        return Ok();
    }

    // ------------------- معوقات -------------------

    [HttpGet("arrears")]
    public async Task<IActionResult> Arrears([FromQuery] int? employeeId, [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListArrearsAsync(employeeId, status, pagination: pagination), skip, take));
    }

    [HttpPost("arrears")]
    public async Task<IActionResult> CreateArrear([FromBody] FaPayArrearSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveArrearAsync(null, dto, MyUserId, MyUsername));
    }

    [HttpPut("arrears/{id:int}")]
    public async Task<IActionResult> UpdateArrear(int id, [FromBody] FaPayArrearSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveArrearAsync(id, dto, MyUserId, MyUsername));
    }

    [HttpDelete("arrears/{id:int}")]
    public async Task<IActionResult> DeleteArrear(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteArrearAsync(id);
        return Ok();
    }

    // ------------------- دوره پایان‌سال -------------------

    [HttpPost("runs/{id:int}/calculate-yearend")]
    public async Task<IActionResult> CalculateYearEnd(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(new { count = await _svc.CalculateYearEndAsync(id) });
    }

    // ------------------- تسویه پایان همکاری -------------------

    [HttpGet("settlements/preview")]
    public async Task<IActionResult> PreviewSettlement([FromQuery] int employeeId, [FromQuery] DateTime leaveDate,
        [FromQuery] int reason, [FromQuery] double otherEarnings, [FromQuery] double otherDeductions)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.PreviewSettlementAsync(employeeId, leaveDate, reason, otherEarnings, otherDeductions));
    }

    [HttpGet("settlements")]
    public async Task<IActionResult> Settlements([FromQuery] int? employeeId, [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListSettlementsAsync(employeeId, status, pagination: pagination), skip, take));
    }

    [HttpGet("settlements/{id:int}")]
    public async Task<IActionResult> Settlement(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var s = await _svc.GetSettlementAsync(id);
        return s == null ? NotFound() : Ok(s);
    }

    [HttpPost("settlements")]
    public async Task<IActionResult> CreateSettlement([FromBody] FaPaySettlementSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveSettlementAsync(null, dto, MyUserId, MyUsername));
    }

    [HttpPut("settlements/{id:int}")]
    public async Task<IActionResult> UpdateSettlement(int id, [FromBody] FaPaySettlementSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveSettlementAsync(id, dto, MyUserId, MyUsername));
    }

    [HttpPost("settlements/{id:int}/finalize")]
    public async Task<IActionResult> FinalizeSettlement(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.FinalizeSettlementAsync(id));
    }

    [HttpDelete("settlements/{id:int}")]
    public async Task<IActionResult> DeleteSettlement(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteSettlementAsync(id);
        return Ok();
    }

    [HttpGet("settlements/{id:int}/pdf")]
    public async Task<IActionResult> SettlementPdf(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var bytes = await _svc.SettlementPdfAsync(id);
        return File(bytes, "application/pdf", $"FaPaySettlement-{id}.pdf");
    }

    // ------------------- فایل بیمه و مقایسه -------------------

    [HttpGet("runs/{id:int}/insurance-check")]
    public async Task<IActionResult> InsuranceCheck(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.InsuranceCheckAsync(id));
    }

    [HttpGet("runs/{id:int}/insurance-file")]
    public async Task<IActionResult> InsuranceFile(int id, [FromQuery] string? format)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var t = await _svc.InsuranceFileAsync(id, format ?? "txt");
        return File(t.Data, t.ContentType, t.FileName);
    }

    [HttpGet("runs/compare")]
    public async Task<IActionResult> CompareRuns([FromQuery] int runAId, [FromQuery] int runBId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.CompareRunsAsync(runAId, runBId));
    }
}
