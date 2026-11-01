using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.FaPay;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.FaPay;

/// <summary>
/// ================== حقوق و دستمزد فروغ آریا (FaPay) — §۸، ماژول مستقل ==================
/// مجوزها: FaPay.Read (مشاهده + فیش‌های من) / FaPay.Manage (مدیریت کامل دوره و محاسبات)
/// سیستم قدیمی حقوق (HrPay/RadisHr/payroll) دست نمی‌خورد.
/// </summary>
[Route("api/fa-pay")]
public class FaPayController : RbacControllerBase
{
    private const string Mod = "FaPay";
    private readonly IFaPayService _svc;

    public FaPayController(AppDbContext db, IFaPayService svc) : base(db) => _svc = svc;

    // ------------------- تنظیمات -------------------

    [HttpGet("settings")]
    public async Task<IActionResult> Settings()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.GetSettingsAsync());
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] FaPaySettingsSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveSettingsAsync(dto));
    }

    // ------------------- پلکان مالیاتی -------------------

    [HttpGet("brackets")]
    public async Task<IActionResult> Brackets([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListBracketsAsync(pagination: pagination), skip, take));
    }

    [HttpPost("brackets")]
    public async Task<IActionResult> CreateBracket([FromBody] FaPayTaxBracketSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveBracketAsync(null, dto));
    }

    [HttpPut("brackets/{id:int}")]
    public async Task<IActionResult> UpdateBracket(int id, [FromBody] FaPayTaxBracketSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveBracketAsync(id, dto));
    }

    [HttpDelete("brackets/{id:int}")]
    public async Task<IActionResult> DeleteBracket(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteBracketAsync(id);
        return Ok();
    }

    // ------------------- اقلام حقوقی -------------------

    [HttpGet("itemtypes")]
    public async Task<IActionResult> ItemTypes([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListItemTypesAsync(pagination: pagination), skip, take));
    }

    [HttpPost("itemtypes")]
    public async Task<IActionResult> CreateItemType([FromBody] FaPayItemTypeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveItemTypeAsync(null, dto));
    }

    [HttpPut("itemtypes/{id:int}")]
    public async Task<IActionResult> UpdateItemType(int id, [FromBody] FaPayItemTypeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveItemTypeAsync(id, dto));
    }

    [HttpDelete("itemtypes/{id:int}")]
    public async Task<IActionResult> DeleteItemType(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteItemTypeAsync(id);
        return Ok();
    }

    // ------------------- ثبت‌های ماهانه -------------------

    [HttpGet("adjustments")]
    public async Task<IActionResult> Adjustments([FromQuery] int? year, [FromQuery] int? month, [FromQuery] int? employeeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListAdjustmentsAsync(year, month, employeeId, pagination: pagination), skip, take));
    }

    [HttpPost("adjustments")]
    public async Task<IActionResult> CreateAdjustment([FromBody] FaPayAdjustmentSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveAdjustmentAsync(null, dto, MyUserId, MyUsername));
    }

    [HttpPut("adjustments/{id:int}")]
    public async Task<IActionResult> UpdateAdjustment(int id, [FromBody] FaPayAdjustmentSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveAdjustmentAsync(id, dto, MyUserId, MyUsername));
    }

    [HttpDelete("adjustments/{id:int}")]
    public async Task<IActionResult> DeleteAdjustment(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteAdjustmentAsync(id);
        return Ok();
    }

    // ------------------- دوره‌ها -------------------

    [HttpGet("runs")]
    public async Task<IActionResult> Runs([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListRunsAsync(pagination: pagination), skip, take));
    }

    [HttpGet("runs/{id:int}")]
    public async Task<IActionResult> Run(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var r = await _svc.GetRunAsync(id);
        return r == null ? NotFound() : Ok(r);
    }

    [HttpPost("runs")]
    public async Task<IActionResult> CreateRun([FromBody] FaPayRunSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.CreateRunAsync(dto, MyUserId, MyUsername));
    }

    [HttpPost("runs/{id:int}/calculate")]
    public async Task<IActionResult> CalculateRun(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(new { count = await _svc.CalculateRunAsync(id) });
    }

    [HttpPost("runs/{id:int}/finalize")]
    public async Task<IActionResult> FinalizeRun(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.FinalizeRunAsync(id));
    }

    [HttpPost("runs/{id:int}/reopen")]
    public async Task<IActionResult> ReopenRun(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.ReopenRunAsync(id));
    }

    [HttpDelete("runs/{id:int}")]
    public async Task<IActionResult> DeleteRun(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.DeleteRunAsync(id);
        return Ok();
    }

    [HttpGet("runs/{id:int}/slips")]
    public async Task<IActionResult> RunSlips(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.RunSlipsAsync(id, pagination: pagination), skip, take));
    }

    [HttpGet("runs/{id:int}/bank-check")]
    public async Task<IActionResult> BankCheck(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Runs") is { } f) return f;
        return Ok(await _svc.BankCheckAsync(id));
    }

    [HttpGet("runs/{id:int}/bank-file")]
    public async Task<IActionResult> BankFile(int id, [FromQuery] string? format)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Runs") is { } f) return f;
        var t = await _svc.BankFileAsync(id, format ?? "xlsx");
        return File(t.Data, t.ContentType, t.FileName);
    }

    [HttpPost("runs/{id:int}/send-all")]
    public async Task<IActionResult> SendRunEmails(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SendRunEmailsAsync(id, MyUserId));
    }

    // ------------------- فیش‌ها -------------------

    [HttpGet("slips/my")]
    public async Task<IActionResult> MySlips([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(pagination => _svc.MySlipsAsync(MyUserId, pagination), skip, take));
    }

    [HttpGet("slips/my/{id:int}")]
    public async Task<IActionResult> MySlip(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var s = await _svc.GetMySlipAsync(id, MyUserId);
        return s == null ? NotFound() : Ok(s);
    }

    [HttpGet("slips/my/{id:int}/pdf")]
    public async Task<IActionResult> MySlipPdf(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var s = await _svc.GetMySlipAsync(id, MyUserId);
        if (s == null) return NotFound();
        var bytes = await _svc.SlipPdfAsync(id);
        return File(bytes, "application/pdf", $"FaPaySlip-{id}.pdf");
    }

    [HttpGet("slips/{id:int}")]
    public async Task<IActionResult> Slip(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var s = await _svc.GetSlipAsync(id);
        return s == null ? NotFound() : Ok(s);
    }

    [HttpGet("runs/{id:int}/slips-pdf")]
    public async Task<IActionResult> RunSlipsPdf(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var bytes = await _svc.SlipsPdfAsync(id);
        return File(bytes, "application/pdf", $"FaPaySlips-{id}.pdf");
    }

    [HttpGet("slips/{id:int}/pdf")]
    public async Task<IActionResult> SlipPdf(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var bytes = await _svc.SlipPdfAsync(id);
        return File(bytes, "application/pdf", $"FaPaySlip-{id}.pdf");
    }

    [HttpPost("slips/{id:int}/paid")]
    public async Task<IActionResult> SetPaid(int id, [FromQuery] bool paid)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await _svc.SetPaidAsync(id, paid);
        return Ok(true);
    }

    [HttpPost("slips/{id:int}/send")]
    public async Task<IActionResult> SendSlip(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(new { mailId = await _svc.SendSlipEmailAsync(id, MyUserId) });
    }

    // ------------------- گزارش‌ها -------------------

    [HttpGet("reports/insurance")]
    public async Task<IActionResult> InsuranceReport([FromQuery] int year, [FromQuery] int month)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.InsuranceReportAsync(year, month));
    }

    [HttpGet("reports/tax")]
    public async Task<IActionResult> TaxReport([FromQuery] int year, [FromQuery] int month)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.TaxReportAsync(year, month));
    }

    [HttpGet("reports/unit-cost")]
    public async Task<IActionResult> UnitCostReport([FromQuery] int year, [FromQuery] int month)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.UnitCostReportAsync(year, month));
    }
}
