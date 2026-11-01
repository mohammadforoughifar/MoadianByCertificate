using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>مدیریت واحدهای شمارش کالا.</summary>
[Route("api/units")]
[Inventory.Api.Infrastructure.ModulePermission("Products")]
public class UnitsController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public UnitsController(IInventoryService service) => _service = service;

    /// <summary>فهرست واحدهای شمارش (با تعداد کالای هر واحد).</summary>
    [HttpGet]
    public async Task<ActionResult<List<MeasureUnit>>> GetAll([FromQuery] bool activeOnly = false, [FromQuery] int skip = 0, [FromQuery] int? take = null, [FromQuery] string? search = null)
        => Ok(await Paging.ResultAsync(async pagination => await _service.GetUnitsAsync(activeOnly, pagination: pagination, search: search), skip, take));

    /// <summary>ایجاد یا ویرایش واحد شمارش.</summary>
    [HttpPost]
    public async Task<ActionResult<MeasureUnit>> Save([FromBody] MeasureUnit unit)
        => Ok(await _service.SaveUnitAsync(unit));

    /// <summary>حذف واحد شمارش.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteUnitAsync(id);
        return Ok(new { ok = true });
    }
}
