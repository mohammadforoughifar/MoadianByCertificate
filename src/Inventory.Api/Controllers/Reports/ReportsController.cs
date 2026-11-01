using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>گزارش کاردکس کالا.</summary>
[Route("api/kardex")]
[Inventory.Api.Infrastructure.ModulePermission("ReportPages", "Kardex")]
public class KardexController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public KardexController(IInventoryService service) => _service = service;

    /// <summary>گردش کالا (کاردکس) با فیلتر انبار و بازه تاریخ.</summary>
    [HttpGet]
    public async Task<ActionResult<List<KardexRow>>> Get(
        [FromQuery] int productId, [FromQuery] int? warehouseId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(pagination => _service.GetKardexAsync(productId, warehouseId, from, to, pagination), skip, take));
}

/// <summary>گزارش نقطه سفارش.</summary>
[Route("api/reorder")]
[Inventory.Api.Infrastructure.ModulePermission("ReportPages", "Reorder")]
public class ReorderController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public ReorderController(IInventoryService service) => _service = service;

    /// <summary>کالاهایی که موجودی‌شان به نقطه سفارش رسیده است.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ReorderItem>>> Get([FromQuery] int? warehouseId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(pagination => _service.GetReorderAsync(warehouseId, pagination), skip, take));
}
