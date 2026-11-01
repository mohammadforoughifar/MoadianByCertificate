using Inventory.Api.Services;
using Db = Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.Invoicing;

// =====================================================================
// کنترلرهای ماژول فاکتور
//   api/fac/invoices    فاکتور خرید/فروش و برگشتی‌ها
//   api/fac/rules       پیکربندی اتصال هر نوع فاکتور به انبار و حسابداری
//   api/fac/reports     گزارش فروش/خرید و داشبورد
// =====================================================================

/// <summary>فاکتورها.</summary>
[Route("api/fac/invoices")]
public class FacInvoicesController : RbacControllerBase
{
    private readonly IInvoicingService _svc;

    public FacInvoicesController(Db.AppDbContext db, IInvoicingService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<PagedResult<FacInvoice>>> GetAll(
        [FromQuery] InvoiceKind? kind = null,
        [FromQuery] InvoiceStatus? status = null,
        [FromQuery] int? partyId = null,
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _svc.GetInvoicesAsync(kind, status, partyId, warehouseId, search, from, to, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    /// <summary>سطر آماده برای یک کالا (قیمت، نرخ مالیات، کد مالیاتی و موجودی).</summary>
    [HttpGet("line")]
    public async Task<ActionResult<FacInvoiceLine>> Line(
        [FromQuery] int productId,
        [FromQuery] InvoiceKind kind = InvoiceKind.Sale,
        [FromQuery] int warehouseId = 0)
        => Ok(await _svc.BuildLineAsync(productId, kind, warehouseId));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FacInvoice>> Get(int id)
    {
        var inv = await _svc.GetInvoiceAsync(id);
        return inv is null ? NotFound() : Ok(inv);
    }

    [HttpPost]
    public async Task<ActionResult<FacInvoice>> Save([FromBody] FacInvoice dto)
    {
        if (await ForbiddenUnlessAsync("FacInvoices", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveInvoiceAsync(dto, MyUsername));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<ActionResult<FacInvoice>> Confirm(int id)
    {
        if (await ForbiddenUnlessAsync("FacInvoices", "Confirm") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.ConfirmInvoiceAsync(id, MyUsername));
    }

    [HttpPost("{id:int}/unconfirm")]
    public async Task<ActionResult<FacInvoice>> Unconfirm(int id)
    {
        if (await ForbiddenUnlessAsync("FacInvoices", "Confirm") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.UnconfirmInvoiceAsync(id, MyUsername));
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<FacInvoice>> Cancel(int id)
    {
        if (await ForbiddenUnlessAsync("FacInvoices", "Cancel") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.CancelInvoiceAsync(id, MyUsername));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("FacInvoices", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteInvoiceAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>پیکربندی انواع فاکتور.</summary>
[Route("api/fac/rules")]
public class FacRulesController : RbacControllerBase
{
    private readonly IInvoicingService _svc;

    public FacRulesController(Db.AppDbContext db, IInvoicingService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<FacRule>>> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetRulesAsync(pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<FacRule>> Save([FromBody] FacRule dto)
    {
        if (await ForbiddenUnlessAsync("FacInvoices", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveRuleAsync(dto));
    }
}

/// <summary>گزارش‌های فروش و خرید.</summary>
[Route("api/fac/reports")]
public class FacReportsController : RbacControllerBase
{
    private readonly IInvoicingService _svc;

    public FacReportsController(Db.AppDbContext db, IInvoicingService svc) : base(db) => _svc = svc;

    /// <summary>گزارش تجمیعی: groupBy = month | day | party | product</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<FacSummaryResult>> Summary(
        [FromQuery] InvoiceKind kind = InvoiceKind.Sale,
        [FromQuery] string groupBy = "month",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
        => Ok(await _svc.GetSummaryAsync(kind, groupBy, from, to));

    [HttpGet("dashboard")]
    public async Task<ActionResult<FacDashboard>> Dashboard(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
        => Ok(await _svc.GetDashboardAsync(from, to));
}
