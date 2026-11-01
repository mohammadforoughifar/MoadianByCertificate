using Inventory.Api.Services;
using Db = Inventory.Api.Data;
using Inventory.Api.Services.Accounting;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.Accounting;

// =====================================================================
// ابعاد تحلیلی — مراکز هزینه، شعبه و مقادیر آن‌ها
//   api/acc/dimensions            ابعاد (تعریف بُعد)
//   api/acc/dimensions/{id}/values  مقادیر هر بُعد
// =====================================================================

/// <summary>ابعاد تحلیلی (مرکز هزینه / شعبه).</summary>
[Route("api/acc/dimensions")]
public class AccDimensionsController : RbacControllerBase
{
    private readonly IAnalyticalDimensionService _svc;

    public AccDimensionsController(Db.AppDbContext db, IAnalyticalDimensionService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<AccDimension>>> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetDimensionsAsync(pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<AccDimension>> Save([FromBody] AccDimension dto)
    {
        if (await ForbiddenUnlessAsync("AccDimensions", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveDimensionAsync(dto));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("AccDimensions", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteDimensionAsync(id);
        return Ok(new { ok = true });
    }

    // -------------------- مقادیر --------------------
    [HttpGet("{dimensionId:int}/values")]
    public async Task<ActionResult<List<AccDimensionValue>>> GetValues(int dimensionId, [FromQuery] bool activeOnly = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetValuesAsync(dimensionId, activeOnly, pagination: pagination), skip, take));

    [HttpPost("{dimensionId:int}/values")]
    public async Task<ActionResult<AccDimensionValue>> SaveValue(int dimensionId, [FromBody] AccDimensionValue dto)
    {
        if (await ForbiddenUnlessAsync("AccDimensions", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        dto.DimensionId = dimensionId;
        return Ok(await _svc.SaveValueAsync(dto));
    }

    [HttpDelete("values/{id:int}")]
    public async Task<IActionResult> DeleteValue(int id)
    {
        if (await ForbiddenUnlessAsync("AccDimensions", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteValueAsync(id);
        return Ok(new { ok = true });
    }

    [HttpGet("{dimensionId:int}/values/lookups")]
    public async Task<ActionResult<List<LookupItem>>> GetValueLookups(int dimensionId, [FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetValueLookupsAsync(dimensionId, search, pagination: pagination), skip, take));
}
