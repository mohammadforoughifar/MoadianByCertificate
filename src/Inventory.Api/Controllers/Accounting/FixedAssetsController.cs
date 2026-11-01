using Inventory.Api.Services;
using Db = Inventory.Api.Data;
using Inventory.Api.Services.Accounting;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.Accounting;

// =====================================================================
// دارایی ثابت و استهلاک
//   api/acc/fixed-assets/categories   گروه‌های دارایی
//   api/acc/fixed-assets              دارایی‌ها
//   api/acc/fixed-assets/runs         اجراهای استهلاک
// =====================================================================

/// <summary>گروه‌های دارایی ثابت.</summary>
[Route("api/acc/fixed-assets/categories")]
public class FixedAssetCategoriesController : RbacControllerBase
{
    private readonly IFixedAssetService _svc;
    public FixedAssetCategoriesController(Db.AppDbContext db, IFixedAssetService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<FixedAssetCategory>>> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null) => Ok(await Paging.ResultAsync(async pagination => await _svc.GetCategoriesAsync(pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<FixedAssetCategory>> Save([FromBody] FixedAssetCategory dto)
    {
        if (await ForbiddenUnlessAsync("FixedAssets", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveCategoryAsync(dto));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("FixedAssets", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteCategoryAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>دارایی‌های ثابت.</summary>
[Route("api/acc/fixed-assets")]
public class FixedAssetsController : RbacControllerBase
{
    private readonly IFixedAssetService _svc;
    public FixedAssetsController(Db.AppDbContext db, IFixedAssetService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<FixedAsset>>> GetAll(
        [FromQuery] FixedAssetStatus? status = null, [FromQuery] int? categoryId = null, [FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetAssetsAsync(status, categoryId, search, pagination: pagination), skip, take));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FixedAsset>> Get(int id) => Ok(await _svc.GetAssetAsync(id));

    [HttpPost]
    public async Task<ActionResult<FixedAsset>> Save([FromBody] FixedAsset dto)
    {
        if (await ForbiddenUnlessAsync("FixedAssets", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveAssetAsync(dto));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("FixedAssets", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteAssetAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>اجراهای استهلاک دارایی‌ها.</summary>
[Route("api/acc/fixed-assets/runs")]
public class FixedAssetRunsController : RbacControllerBase
{
    private readonly IFixedAssetService _svc;
    public FixedAssetRunsController(Db.AppDbContext db, IFixedAssetService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<FixedAssetDepreciationRun>>> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetRunsAsync(pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<FixedAssetDepreciationRun>> Run(
        [FromBody] FixedAssetDepreciationRequest req, [FromQuery] string? user = null)
    {
        if (await ForbiddenUnlessAsync("FixedAssets", "Create") is ObjectResult forbidden) return forbidden;
        var me = string.IsNullOrWhiteSpace(user) ? MyUsername : user;
        return Ok(await _svc.RunDepreciationAsync(req, me));
    }

    [HttpPost("{id:int}/post")]
    public async Task<ActionResult<FixedAssetDepreciationRun?>> PostToAccounting(int id)
    {
        if (await ForbiddenUnlessAsync("FixedAssets", "Confirm") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.PostRunToAccountingAsync(id, MyUsername));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("FixedAssets", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteRunAsync(id);
        return Ok(new { ok = true });
    }
}
