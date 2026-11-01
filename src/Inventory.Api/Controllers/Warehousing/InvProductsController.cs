using Db = Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.Warehousing;

/// <summary>تعریف کالا — فرم چندمرحله‌ای (اطلاعات پایه، مالیات، انبار، ویژگی‌ها).</summary>
[Route("api/inv/products")]
public class InvProductsController : RbacControllerBase
{
    private readonly IWarehousingService _svc;

    public InvProductsController(Db.AppDbContext db, IWarehousingService svc) : base(db) => _svc = svc;

    /// <summary>فهرست کالاها با فیلتر گروه، انبار و نقطه سفارش.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<InvProduct>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] int? warehouseId = null,
        [FromQuery] bool below = false,
        [FromQuery] bool activeOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _svc.GetProductsAsync(search, categoryId, warehouseId, below, activeOnly, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    /// <summary>یک کالا با همه‌ی جزئیات و مقادیر ویژگی‌ها.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<InvProduct>> Get(int id)
    {
        var p = await _svc.GetProductAsync(id);
        return p is null ? NotFound(new { message = "کالا یافت نشد." }) : Ok(p);
    }

    /// <summary>قالب کالای جدید (کد پیشنهادی + ویژگی‌های گروه انتخابی).</summary>
    [HttpGet("new")]
    public async Task<ActionResult<InvProduct>> New([FromQuery] int? categoryId = null)
        => Ok(await _svc.NewProductAsync(categoryId));

    /// <summary>فهرست سبک کالاها برای انتخاب در فرم‌ها.</summary>
    [HttpGet("lookups")]
    public async Task<ActionResult<List<LookupItem>>> Lookups(
        [FromQuery] string? search = null, [FromQuery] int? warehouseId = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetProductLookupsAsync(search, warehouseId, pagination: pagination), skip, take));

    /// <summary>ایجاد یا ویرایش کالا.</summary>
    [HttpPost]
    public async Task<ActionResult<InvProduct>> Save([FromBody] InvProduct dto)
    {
        if (await ForbiddenUnlessAsync("Products", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveProductAsync(dto));
    }

    /// <summary>حذف کالا.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("Products", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteProductAsync(id);
        return Ok(new { ok = true });
    }
}
