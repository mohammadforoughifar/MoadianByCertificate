using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>مدیریت انبارها.</summary>
[Route("api/warehouses")]
[Inventory.Api.Infrastructure.ModulePermission("Warehouses")]
public class WarehousesController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public WarehousesController(IInventoryService service) => _service = service;

    /// <summary>فهرست انبارها.</summary>
    [HttpGet]
    public async Task<ActionResult<List<Warehouse>>> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _service.GetWarehousesAsync(pagination: pagination), skip, take));

    /// <summary>ایجاد یا ویرایش انبار.</summary>
    [HttpPost]
    public async Task<ActionResult<Warehouse>> Save([FromBody] Warehouse warehouse)
        => Ok(await _service.SaveWarehouseAsync(warehouse));

    /// <summary>حذف انبار.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteWarehouseAsync(id);
        return Ok(new { ok = true });
    }
}
