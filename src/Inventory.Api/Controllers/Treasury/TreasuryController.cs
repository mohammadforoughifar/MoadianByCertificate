using Inventory.Api.Services;
using Db = Inventory.Api.Data;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.Treasury;

// =====================================================================
// کنترلرهای ماژول خزانه‌داری
//   api/trs/accounts   صندوق، بانک، کارتخوان و تنخواه
//   api/trs/vouchers   اسناد دریافت، پرداخت و انتقال
//   api/trs/cheques    چک‌ها و عملیات آن‌ها
//   api/trs/rules      پیکربندی حساب‌های سند خودکار خزانه
//   api/trs/reports    گردش خزانه و داشبورد
// =====================================================================

/// <summary>صندوق‌ها و حساب‌های بانکی.</summary>
[Route("api/trs/accounts")]
public class TrsAccountsController : RbacControllerBase
{
    private readonly ITreasuryService _svc;

    public TrsAccountsController(Db.AppDbContext db, ITreasuryService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<TrsAccount>>> GetAll(
        [FromQuery] bool activeOnly = false,
        [FromQuery] bool withBalances = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetAccountsAsync(activeOnly, withBalances, pagination: pagination), skip, take));

    [HttpGet("lookups")]
    public async Task<ActionResult<List<LookupItem>>> Lookups([FromQuery] bool activeOnly = true, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetAccountLookupsAsync(activeOnly, pagination: pagination), skip, take));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TrsAccount>> Get(int id)
    {
        var a = await _svc.GetAccountAsync(id);
        return a is null ? NotFound() : Ok(a);
    }

    [HttpPost]
    public async Task<ActionResult<TrsAccount>> Save([FromBody] TrsAccount dto)
    {
        if (await ForbiddenUnlessAsync("TrsAccounts", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveAccountAsync(dto));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("TrsAccounts", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteAccountAsync(id);
        return NoContent();
    }
}

/// <summary>اسناد دریافت، پرداخت و انتقال بین حساب‌ها.</summary>
[Route("api/trs/vouchers")]
public class TrsVouchersController : RbacControllerBase
{
    private readonly ITreasuryService _svc;

    public TrsVouchersController(Db.AppDbContext db, ITreasuryService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<PagedResult<TrsVoucher>>> GetAll(
        [FromQuery] TreasuryKind? kind = null,
        [FromQuery] TreasuryStatus? status = null,
        [FromQuery] int? partyId = null,
        [FromQuery] int? trsAccountId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _svc.GetVouchersAsync(kind, status, partyId, trsAccountId, search, from, to, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    [HttpGet("new")]
    public async Task<ActionResult<TrsVoucher>> New([FromQuery] TreasuryKind kind = TreasuryKind.Receipt)
        => Ok(await _svc.NewVoucherAsync(kind));

    /// <summary>سند تسویه آماده برای یک فاکتور قطعی (با ماندهٔ تسویه‌نشده).</summary>
    [HttpGet("settle/{invoiceId:int}")]
    public async Task<ActionResult<TrsVoucher>> Settle(int invoiceId)
        => Ok(await _svc.NewSettlementAsync(invoiceId));

    /// <summary>فاکتورهای باز و قابل تسویهٔ یک طرف حساب</summary>
    [HttpGet("open-invoices")]
    public async Task<ActionResult<List<TrsSettlementInvoice>>> OpenInvoices(
        [FromQuery] int partyId, [FromQuery] TreasuryKind kind = TreasuryKind.Receipt)
        => Ok(await _svc.GetOpenInvoicesAsync(partyId, kind));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TrsVoucher>> Get(int id)
    {
        var v = await _svc.GetVoucherAsync(id);
        return v is null ? NotFound() : Ok(v);
    }

    [HttpPost]
    public async Task<ActionResult<TrsVoucher>> Save([FromBody] TrsVoucher dto)
    {
        if (await ForbiddenUnlessAsync("TrsVouchers", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveVoucherAsync(dto, MyUsername));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<ActionResult<TrsVoucher>> Confirm(int id)
    {
        if (await ForbiddenUnlessAsync("TrsVouchers", "Confirm") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.ConfirmVoucherAsync(id, MyUsername));
    }

    [HttpPost("{id:int}/unconfirm")]
    public async Task<ActionResult<TrsVoucher>> Unconfirm(int id)
    {
        if (await ForbiddenUnlessAsync("TrsVouchers", "Confirm") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.UnconfirmVoucherAsync(id, MyUsername));
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<TrsVoucher>> Cancel(int id)
    {
        if (await ForbiddenUnlessAsync("TrsVouchers", "Cancel") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.CancelVoucherAsync(id, MyUsername));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("TrsVouchers", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteVoucherAsync(id);
        return NoContent();
    }
}

/// <summary>چک‌های دریافتی و پرداختی و عملیات چرخه عمر آن‌ها.</summary>
[Route("api/trs/cheques")]
public class TrsChequesController : RbacControllerBase
{
    private readonly ITreasuryService _svc;

    public TrsChequesController(Db.AppDbContext db, ITreasuryService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<PagedResult<TrsCheque>>> GetAll(
        [FromQuery] ChequeKind? kind = null,
        [FromQuery] ChequeStatus? status = null,
        [FromQuery] int? partyId = null,
        [FromQuery] int? trsAccountId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool onlyOpen = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _svc.GetChequesAsync(kind, status, partyId, trsAccountId, search, from, to, onlyOpen, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TrsCheque>> Get(int id)
    {
        var c = await _svc.GetChequeAsync(id);
        return c is null ? NotFound() : Ok(c);
    }

    [HttpPost]
    public async Task<ActionResult<TrsCheque>> Save([FromBody] TrsCheque dto)
    {
        if (await ForbiddenUnlessAsync("TrsCheques", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveChequeAsync(dto, MyUsername));
    }

    /// <summary>اجرای عملیات چک: واگذاری، وصول، برگشت، خرج یا ابطال.</summary>
    [HttpPost("action")]
    public async Task<ActionResult<TrsCheque>> RunAction([FromBody] TrsChequeCommand cmd)
    {
        if (await ForbiddenUnlessAsync("TrsCheques", "Confirm") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.RunChequeActionAsync(cmd, MyUsername));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("TrsCheques", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteChequeAsync(id);
        return NoContent();
    }
}

/// <summary>پیکربندی حساب‌های سند خودکار خزانه.</summary>
[Route("api/trs/rules")]
public class TrsRulesController : RbacControllerBase
{
    private readonly ITreasuryService _svc;

    public TrsRulesController(Db.AppDbContext db, ITreasuryService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<TrsRule>>> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null) => Ok(await Paging.ResultAsync(async pagination => await _svc.GetRulesAsync(pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<TrsRule>> Save([FromBody] TrsRule dto)
    {
        if (await ForbiddenUnlessAsync("TrsAccounts", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveRuleAsync(dto));
    }
}

/// <summary>گزارش‌های خزانه.</summary>
[Route("api/trs/reports")]
public class TrsReportsController : RbacControllerBase
{
    private readonly ITreasuryService _svc;

    public TrsReportsController(Db.AppDbContext db, ITreasuryService svc) : base(db) => _svc = svc;

    /// <summary>گردش یک صندوق/بانک با مانده تجمعی.</summary>
    [HttpGet("flow")]
    public async Task<ActionResult<TrsFlowResult>> Flow(
        [FromQuery] int trsAccountId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
        => Ok(await _svc.GetFlowAsync(trsAccountId, from, to));

    [HttpGet("dashboard")]
    public async Task<ActionResult<TrsDashboard>> Dashboard(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
        => Ok(await _svc.GetDashboardAsync(from, to));
}
