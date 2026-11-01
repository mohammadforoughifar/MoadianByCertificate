using Inventory.Api.Services;
using Db = Inventory.Api.Data;
using Inventory.Api.Services.Accounting;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.Accounting;

// =====================================================================
// کنترلرهای ماژول حسابداری
//   api/acc/fiscal-years   سال مالی
//   api/acc/accounts       کدینگ حساب‌ها (درختی)
//   api/acc/vouchers       اسناد حسابداری
//   api/acc/reports        دفاتر، تراز آزمایشی، داشبورد
//   api/acc/inv-rules      قواعد صدور خودکار سند از اسناد انبار
// =====================================================================

/// <summary>سال (دوره) مالی.</summary>
[Route("api/acc/fiscal-years")]
public class AccFiscalYearsController : RbacControllerBase
{
    private readonly IAccountingService _svc;

    public AccFiscalYearsController(Db.AppDbContext db, IAccountingService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<AccFiscalYear>>> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetFiscalYearsAsync(pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<AccFiscalYear>> Save([FromBody] AccFiscalYear dto)
    {
        if (await ForbiddenUnlessAsync("AccAccounts", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveFiscalYearAsync(dto));
    }

    [HttpPost("{id:int}/set-current")]
    public async Task<IActionResult> SetCurrent(int id)
    {
        if (await ForbiddenUnlessAsync("AccAccounts", "Update") is ObjectResult forbidden) return forbidden;
        await _svc.SetCurrentFiscalYearAsync(id);
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("AccAccounts", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteFiscalYearAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>کدینگ حساب‌ها — درخت گروه ← کل ← معین ← تفصیلی.</summary>
[Route("api/acc/accounts")]
public class AccAccountsController : RbacControllerBase
{
    private readonly IAccountingService _svc;

    public AccAccountsController(Db.AppDbContext db, IAccountingService svc) : base(db) => _svc = svc;

    [HttpGet("tree")]
    public async Task<ActionResult<List<AccAccount>>> GetTree(
        [FromQuery] bool activeOnly = false, [FromQuery] bool withBalances = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(pagination => _svc.GetAccountTreeAsync(activeOnly, withBalances, pagination), skip, take));

    [HttpGet]
    public async Task<ActionResult<List<AccAccount>>> GetFlat(
        [FromQuery] bool activeOnly = false, [FromQuery] bool withBalances = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(pagination => _svc.GetAccountsFlatAsync(activeOnly, withBalances, pagination), skip, take));

    [HttpGet("lookups")]
    public async Task<ActionResult<List<LookupItem>>> Lookups([FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetPostableAccountLookupsAsync(search, pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<AccAccount>> Save([FromBody] AccAccount dto)
    {
        if (await ForbiddenUnlessAsync("AccAccounts", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveAccountAsync(dto));
    }

    [HttpPost("move")]
    public async Task<IActionResult> Move([FromBody] AccAccountMove cmd)
    {
        if (await ForbiddenUnlessAsync("AccAccounts", "Update") is ObjectResult forbidden) return forbidden;
        await _svc.MoveAccountAsync(cmd);
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("AccAccounts", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteAccountAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>اسناد حسابداری.</summary>
[Route("api/acc/vouchers")]
public class AccVouchersController : RbacControllerBase
{
    private readonly IAccountingService _svc;

    public AccVouchersController(Db.AppDbContext db, IAccountingService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<PagedResult<AccVoucher>>> GetAll(
        [FromQuery] int? fiscalYearId = null,
        [FromQuery] VoucherStatus? status = null,
        [FromQuery] VoucherSource? source = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _svc.GetVouchersAsync(fiscalYearId, status, source, search, from, to, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    [HttpGet("new")]
    public async Task<ActionResult<AccVoucher>> New()
        => Ok(await _svc.NewVoucherAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AccVoucher>> Get(int id)
    {
        var v = await _svc.GetVoucherAsync(id);
        return v is null ? NotFound() : Ok(v);
    }

    [HttpPost]
    public async Task<ActionResult<AccVoucher>> Save([FromBody] AccVoucher dto)
    {
        if (await ForbiddenUnlessAsync("AccVouchers", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveVoucherAsync(dto, MyUsername));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<ActionResult<AccVoucher>> Confirm(int id)
    {
        if (await ForbiddenUnlessAsync("AccVouchers", "Confirm") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.ConfirmVoucherAsync(id, MyUsername));
    }

    [HttpPost("{id:int}/unconfirm")]
    public async Task<ActionResult<AccVoucher>> Unconfirm(int id)
    {
        if (await ForbiddenUnlessAsync("AccVouchers", "Confirm") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.UnconfirmVoucherAsync(id, MyUsername));
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<AccVoucher>> Cancel(int id)
    {
        if (await ForbiddenUnlessAsync("AccVouchers", "Cancel") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.CancelVoucherAsync(id, MyUsername));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("AccVouchers", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteVoucherAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>گزارش‌های حسابداری: دفتر حساب، دفتر روزنامه، تراز آزمایشی و داشبورد.</summary>
[Route("api/acc/reports")]
public class AccReportsController : RbacControllerBase
{
    private readonly IAccountingService _svc;

    public AccReportsController(Db.AppDbContext db, IAccountingService svc) : base(db) => _svc = svc;

    [HttpGet("ledger")]
    public async Task<ActionResult<AccLedgerResult>> Ledger(
        [FromQuery] int accountId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool includeChildren = true)
        => Ok(await _svc.GetLedgerAsync(accountId, from, to, includeChildren));

    [HttpGet("journal")]
    public async Task<ActionResult<PagedResult<AccLedgerRow>>> Journal(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _svc.GetJournalAsync(from, to, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    [HttpGet("trial-balance")]
    public async Task<ActionResult<AccTrialBalanceResult>> TrialBalance(
        [FromQuery] AccountLevel level = AccountLevel.General,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool hideZero = true)
        => Ok(await _svc.GetTrialBalanceAsync(level, from, to, hideZero));

    [HttpGet("dashboard")]
    public async Task<ActionResult<AccDashboard>> Dashboard()
        => Ok(await _svc.GetDashboardAsync());
}

/// <summary>قواعد صدور خودکار سند حسابداری از روی اسناد انبار.</summary>
[Route("api/acc/inv-rules")]
public class AccInvRulesController : RbacControllerBase
{
    private readonly IAccountingService _svc;

    public AccInvRulesController(Db.AppDbContext db, IAccountingService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<AccInvRule>>> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetInvRulesAsync(pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<AccInvRule>> Save([FromBody] AccInvRule dto)
    {
        if (await ForbiddenUnlessAsync("AccAccounts", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveInvRuleAsync(dto));
    }

    /// <summary>صدور دستی سند حسابداری برای یک سند انبارِ قطعی‌شده.</summary>
    [HttpPost("post-doc/{invDocId:int}")]
    public async Task<ActionResult<AccVoucher>> PostDoc(int invDocId)
    {
        if (await ForbiddenUnlessAsync("AccVouchers", "Create") is ObjectResult forbidden) return forbidden;
        var v = await _svc.PostInventoryDocAsync(invDocId, MyUsername);
        return v is null
            ? Ok(new AccVoucher { Description = "قاعده‌ی فعالی برای این نوع سند تعریف نشده است." })
            : Ok(v);
    }
}
