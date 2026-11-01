using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>موجودی انبار و اصلاح موجودی.</summary>
[Route("api/stock")]
[Inventory.Api.Infrastructure.ModulePermission("Stock")]
public class StockController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public StockController(IInventoryService service) => _service = service;

    /// <summary>فهرست موجودی با فیلتر انبار/جستجو و صفحه‌بندی.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<StockItem>>> GetAll(
        [FromQuery] int? warehouseId, [FromQuery] string? search, [FromQuery] bool? below,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _service.GetStockAsync(warehouseId, search, below == true, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    /// <summary>اصلاح (تعدیل) موجودی یک کالا در یک انبار.</summary>
    [HttpPost("adjust")]
    public async Task<IActionResult> Adjust([FromBody] AdjustmentCommand cmd)
    {
        await _service.AdjustStockAsync(cmd);
        return Ok(new { ok = true });
    }
}
