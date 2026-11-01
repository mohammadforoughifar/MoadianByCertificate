using Inventory.Api.Services.Core;
using Inventory.Api.Services.SalesVisit;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// ماژول ویزیتوری — سفارش میدانی (پیش‌سند فروش) با چرخهٔ تأیید مدیر.
/// <para>
/// چرا از <see cref="ApiControllerBase"/> ارث نمی‌برد؟ چون آن کلاس فقط نقش‌های
/// Admin/Operator را می‌پذیرد و ویزیتور با نقش SalesVisitor هرگز نمی‌توانست
/// به این بخش دسترسی داشته باشد. کنترل دسترسی در همین کنترلر بر اساس
/// مجوزهای RBAC (Module.Action) انجام می‌شود و محدودهٔ داده در سرویس اعمال می‌گردد.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/sales-visit")]
public class SalesVisitController : ControllerBase
{
    private readonly IVisOrderService _svc;
    private readonly IEffectivePermissions _permissions;

    public SalesVisitController(IVisOrderService svc, IEffectivePermissions permissions)
    {
        _svc = svc;
        _permissions = permissions;
    }

    /// <summary>بررسی مجوز سمت سرور — منبع حقیقت، نه توکن کلاینت.</summary>
    private async Task<bool> HasAsync(string action)
    {
        if (await _permissions.IsLegacyAdminAsync(User)) return true;
        var perms = await _permissions.GetAsync(User);
        return perms.Contains($"SalesVisit.{action}");
    }

    /// <summary>آیا کاربر حداقل یکی از این اکشن‌ها را دارد؟ (fail-closed)</summary>
    private async Task<bool> HasAnyAsync(params string[] actions)
    {
        foreach (var a in actions)
            if (await HasAsync(a)) return true;
        return false;
    }

    // ==================== هویت کاربر ====================

    /// <summary>توان‌های کاربر جاری در ماژول (برای تصمیم‌گیری UI).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<VisActorInfo>> Me()
    {
        var actor = await _svc.ResolveActorAsync(User);
        var warehouses = await _svc.WarehousesAsync(actor);
        return Ok(new VisActorInfo
        {
            UserId = actor.UserId,
            UserName = actor.UserName,
            ReferrerId = actor.ReferrerId,
            ReferrerName = actor.ReferrerName,
            IsAdmin = actor.IsAdmin,
            CanCreate = actor.CanCreate,
            CanApprove = actor.CanApprove,
            CanFinance = actor.CanFinance,
            MaxDiscountPercent = actor.MaxDiscountPercent,
            CompanyId = actor.CompanyId,
            CompanyName = actor.CompanyName,
            WarehouseCount = warehouses.Count,
            ProductCount = (await _svc.ProductsAsync(actor, warehouses.FirstOrDefault()?.Id, null, 1)).Count,
            DefaultWarehouseId = warehouses.FirstOrDefault()?.Id ?? 0
        });
    }

    // ==================== خواندن ====================

    /// <summary>فهرست سفارش‌ها — ویزیتور فقط سفارش‌های خودش، مدیر/مالی همهٔ شرکت.</summary>
    [HttpGet]
    public async Task<ActionResult<List<VisOrderDto>>> GetAll(
        [FromQuery] VisOrderStatus? status, [FromQuery] int? partyId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search)
    {
        if (!await HasAnyAsync("View", "Create", "Approve", "FinanceView")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.GetListAsync(actor, status, partyId, from, to, search));
    }

    /// <summary>صف تأیید مدیر فروش همراه هشدار کسری موجودی.</summary>
    [HttpGet("approvals")]
    public async Task<ActionResult<List<VisApprovalItem>>> Approvals()
    {
        if (!await HasAnyAsync("Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.ApprovalsQueueAsync(actor));
    }

    /// <summary>صف مالی: سفارش‌های تأییدشده و فاکتورشده.</summary>
    [HttpGet("finance-queue")]
    public async Task<ActionResult<List<VisOrderDto>>> FinanceQueue()
    {
        if (!await HasAnyAsync("FinanceView", "Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.FinanceQueueAsync(actor));
    }

    /// <summary>آمار کارتابلی/داشبوردی.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<VisQueueStats>> Stats()
    {
        if (!await HasAnyAsync("View", "Create", "Approve", "FinanceView")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.StatsAsync(actor));
    }

    /// <summary>یک سفارش با سطرها و گردش کامل.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<VisOrderDto>> Get(int id)
    {
        if (!await HasAnyAsync("View", "Create", "Approve", "FinanceView")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        var order = await _svc.GetAsync(actor, id);
        return order is null
            ? NotFound(new { message = "سفارش یافت نشد یا به شما تعلق ندارد." })
            : Ok(order);
    }

    // ==================== دادهٔ کمکی فرم ====================

    /// <summary>مشتریان مجاز کاربر (پروندهٔ ویزیتور).</summary>
    [HttpGet("customers")]
    public async Task<ActionResult<List<VisCustomerLite>>> Customers([FromQuery] string? search)
    {
        if (!await HasAnyAsync("Create", "Approve", "View")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.CustomersAsync(actor, search));
    }

    /// <summary>ثبت سریع مشتری جدید توسط ویزیتور (تخصیص با تأیید مدیر در کارتابل).</summary>
    [HttpPost("customers")]
    public async Task<ActionResult<VisCustomerLite>> CreateCustomer([FromBody] VisCustomerLite input)
    {
        if (!await HasAnyAsync("Create")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.CreateCustomerAsync(actor, input.Name, input.Phone, input.Mobile, input.Address));
    }

    /// <summary>کالاها با قیمت و موجودی انبار انتخابی (جایگزین endpointهای «فقط اپراتور»).</summary>
    [HttpGet("products")]
    public async Task<ActionResult<List<VisProductLite>>> Products([FromQuery] int? warehouseId, [FromQuery] string? search)
    {
        if (!await HasAnyAsync("Create", "Approve", "View")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.ProductsAsync(actor, warehouseId, search));
    }

    /// <summary>انبارهای فعال.</summary>
    [HttpGet("warehouses")]
    public async Task<ActionResult<List<VisWarehouseLite>>> Warehouses()
    {
        if (!await HasAnyAsync("Create", "Approve", "View", "FinanceView")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.WarehousesAsync(actor));
    }

    // ==================== ثبت و چرخهٔ وضعیت ====================

    /// <summary>ثبت یا ویرایش پیش‌نویس سفارش (با گزینهٔ ارسال فوری).</summary>
    [HttpPost]
    public async Task<ActionResult<VisOrderDto>> Save([FromBody] VisOrderCommand cmd)
    {
        if (!await HasAnyAsync("Create", "Update", "Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.SaveAsync(actor, cmd));
    }

    [HttpPost("{id:int}/submit")]
    public async Task<ActionResult<VisOrderDto>> Submit(int id)
    {
        if (!await HasAnyAsync("Submit", "Create", "Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.SubmitAsync(actor, id));
    }

    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<VisOrderDto>> Approve(int id, [FromBody] VisOrderDecision? decision)
    {
        if (!await HasAnyAsync("Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        // دیگر پیش‌نویس فاکتور ساخته نمی‌شود: تأیید مدیر سفارش را به «کارتابل مالی» می‌برد و
        // مالی با یک اقدام، سند فروش فروشگاه (نقد/چک/اقساط) را ثبت می‌کند.
        return Ok(await _svc.ApproveAsync(actor, id, decision?.Comment,
            createInvoiceDraft: false, finalDeliveryDate: decision?.FinalDeliveryDate));
    }

    [HttpPost("{id:int}/return")]
    public async Task<ActionResult<VisOrderDto>> Return(int id, [FromBody] VisOrderDecision decision)
    {
        if (!await HasAnyAsync("Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.ReturnAsync(actor, id, decision?.Comment ?? ""));
    }

    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult<VisOrderDto>> Reject(int id, [FromBody] VisOrderDecision decision)
    {
        if (!await HasAnyAsync("Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.RejectAsync(actor, id, decision?.Comment ?? ""));
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<VisOrderDto>> Cancel(int id, [FromBody] VisOrderDecision? decision)
    {
        if (!await HasAnyAsync("Cancel", "Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.CancelAsync(actor, id, decision?.Comment));
    }

    /// <summary>گام مالی: ساخت پیش‌نویس فاکتور فروش از سفارش تأییدشده.</summary>
    [HttpPost("{id:int}/invoice-draft")]
    public async Task<ActionResult<VisOrderDto>> InvoiceDraft(int id)
    {
        if (!await HasAnyAsync("FinanceView", "Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.CreateInvoiceDraftAsync(actor, id));
    }

    /// <summary>
    /// تأیید مالی: ثبت فروش فروشگاه از سفارش تأییدشده + سند خودکار + چک/اقساط.
    /// این اقدام جای «ساخت پیش‌نویس فاکتور» و «همگام‌سازی» را گرفته است.
    /// </summary>
    [HttpPost("{id:int}/finance-approve")]
    public async Task<ActionResult<VisOrderDto>> FinanceApprove(int id, [FromBody] VisFinanceCommand cmd)
    {
        if (!await HasAnyAsync("FinanceView", "Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.FinanceApproveAsync(actor, id, cmd ?? new VisFinanceCommand()));
    }

    /// <summary>همگام‌سازی وضعیت سفارش با فاکتور (قطعی/ابطال).</summary>
    [HttpPost("{id:int}/sync-invoice")]
    public async Task<ActionResult<VisOrderDto>> SyncInvoice(int id)
    {
        if (!await HasAnyAsync("FinanceView", "Approve")) return Forbid();
        var actor = await _svc.ResolveActorAsync(User);
        return Ok(await _svc.SyncInvoiceStateAsync(actor, id));
    }

    /// <summary>پاک‌سازی سفارش‌های ارسال‌شدهٔ منقضی (اجرای دستی توسط مدیر).</summary>
    [HttpPost("expire-stale")]
    public async Task<ActionResult<object>> ExpireStale([FromQuery] int days = 7)
    {
        if (!await HasAnyAsync("Approve")) return Forbid();
        var count = await _svc.ExpireStaleAsync(days);
        return Ok(new { expired = count });
    }
}
