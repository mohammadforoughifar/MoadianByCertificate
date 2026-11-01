using Inventory.Api.Services;
using Db = Inventory.Api.Data;
using Inventory.Api.Services.Stocktaking;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.Stocktaking;

// =====================================================================
// کنترلرهای ماژول انبارگردانی و بارکد
//   api/stk/barcodes   بارکدهای کالا، اسکن و برچسب
//   api/stk/sessions   دوره‌های انبارگردانی و چرخه وضعیت
//   api/stk/count      ثبت شمارش (اسکنر و ورود دستی)
//   api/stk/reports    گزارش مغایرت
// =====================================================================

/// <summary>بارکدهای کالا، اسکن و برچسب چاپی.</summary>
[Route("api/stk/barcodes")]
public class BcdBarcodesController : RbacControllerBase
{
    private readonly IStocktakingService _svc;

    public BcdBarcodesController(Db.AppDbContext db, IStocktakingService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<BcdBarcode>>> GetAll(
        [FromQuery] int? productId = null,
        [FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetBarcodesAsync(productId, search, pagination: pagination), skip, take));

    /// <summary>یافتن کالا از روی بارکد — قلب صفحه اسکنر.</summary>
    [HttpGet("scan")]
    public async Task<ActionResult<BcdScanResult>> Scan(
        [FromQuery] string code,
        [FromQuery] int warehouseId = 0)
        => Ok(await _svc.ScanAsync(code, warehouseId));

    [HttpPost]
    public async Task<ActionResult<BcdBarcode>> Save([FromBody] BcdBarcode dto)
    {
        if (await ForbiddenUnlessAsync("StkBarcodes", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveBarcodeAsync(dto));
    }

    /// <summary>تولید بارکد EAN-13 داخلی برای کالاهای بدون بارکد.</summary>
    [HttpPost("generate")]
    public async Task<ActionResult<int>> Generate([FromBody] BcdGenerateCommand cmd)
    {
        if (await ForbiddenUnlessAsync("StkBarcodes", "Create") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.GenerateBarcodesAsync(cmd.ProductIds, cmd.Prefix));
    }

    /// <summary>برچسب‌های آماده چاپ.</summary>
    [HttpPost("labels")]
    public async Task<ActionResult<List<BcdLabel>>> Labels([FromBody] List<int> productIds)
        => Ok(await _svc.GetLabelsAsync(productIds));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("StkBarcodes", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteBarcodeAsync(id);
        return NoContent();
    }
}

/// <summary>پارامتر تولید گروهی بارکد.</summary>
public class BcdGenerateCommand
{
    public List<int> ProductIds { get; set; } = new();
    public string Prefix { get; set; } = "200";
}

/// <summary>دوره‌های انبارگردانی و چرخه وضعیت آن‌ها.</summary>
[Route("api/stk/sessions")]
public class StkSessionsController : RbacControllerBase
{
    private readonly IStocktakingService _svc;

    public StkSessionsController(Db.AppDbContext db, IStocktakingService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<PagedResult<StkSession>>> GetAll(
        [FromQuery] int? warehouseId = null,
        [FromQuery] StocktakeStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _svc.GetSessionsAsync(warehouseId, status, search, from, to, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    [HttpGet("new")]
    public async Task<ActionResult<StkSession>> New([FromQuery] int warehouseId = 0)
        => Ok(await _svc.NewSessionAsync(warehouseId));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StkSession>> Get(int id)
    {
        var s = await _svc.GetSessionAsync(id);
        return s is null ? NotFound() : Ok(s);
    }

    [HttpPost]
    public async Task<ActionResult<StkSession>> Save([FromBody] StkSession dto)
    {
        if (await ForbiddenUnlessAsync("StkSessions", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveSessionAsync(dto, MyUsername));
    }

    /// <summary>قفل موجودی و ساخت لیست شمارش.</summary>
    [HttpPost("{id:int}/start")]
    public async Task<ActionResult<StkSession>> Start(int id)
    {
        if (await ForbiddenUnlessAsync("StkSessions", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.StartCountingAsync(id, MyUsername));
    }

    /// <summary>پایان شمارش و ورود به مرحله بررسی مغایرت.</summary>
    [HttpPost("{id:int}/finish")]
    public async Task<ActionResult<StkSession>> Finish(int id)
    {
        if (await ForbiddenUnlessAsync("StkSessions", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.FinishCountingAsync(id, MyUsername));
    }

    /// <summary>بازگشت از بررسی به شمارش.</summary>
    [HttpPost("{id:int}/reopen")]
    public async Task<ActionResult<StkSession>> Reopen(int id)
    {
        if (await ForbiddenUnlessAsync("StkSessions", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.ReopenCountingAsync(id, MyUsername));
    }

    /// <summary>صدور اسناد اصلاح موجودی.</summary>
    [HttpPost("{id:int}/apply")]
    public async Task<ActionResult<StkSession>> Apply(int id)
    {
        if (await ForbiddenUnlessAsync("StkSessions", "Apply") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.ApplyAsync(id, MyUsername));
    }

    /// <summary>برگشت اسناد اصلاح موجودی.</summary>
    [HttpPost("{id:int}/unapply")]
    public async Task<ActionResult<StkSession>> Unapply(int id)
    {
        if (await ForbiddenUnlessAsync("StkSessions", "Apply") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.UnapplyAsync(id, MyUsername));
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<StkSession>> Cancel(int id)
    {
        if (await ForbiddenUnlessAsync("StkSessions", "Cancel") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.CancelSessionAsync(id, MyUsername));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("StkSessions", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteSessionAsync(id);
        return NoContent();
    }

    // ---------- شمارش ----------

    /// <summary>ثبت یک شمارش — از اسکنر یا ورود دستی.</summary>
    [HttpPost("count")]
    public async Task<ActionResult<StkCountResult>> Count([FromBody] StkCountCommand cmd)
    {
        if (await ForbiddenUnlessAsync("StkSessions", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.CountAsync(cmd, MyUsername));
    }

    /// <summary>پاک کردن شمارش یک قلم.</summary>
    [HttpPost("{id:int}/lines/{lineId:int}/clear")]
    public async Task<ActionResult<StkCountResult>> ClearLine(int id, int lineId)
    {
        if (await ForbiddenUnlessAsync("StkSessions", "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.ClearLineAsync(id, lineId, MyUsername));
    }

    [HttpDelete("{id:int}/lines/{lineId:int}")]
    public async Task<IActionResult> DeleteLine(int id, int lineId)
    {
        if (await ForbiddenUnlessAsync("StkSessions", "Update") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteLineAsync(id, lineId);
        return NoContent();
    }
}

/// <summary>گزارش‌های انبارگردانی.</summary>
[Route("api/stk/reports")]
public class StkReportsController : RbacControllerBase
{
    private readonly IStocktakingService _svc;

    public StkReportsController(Db.AppDbContext db, IStocktakingService svc) : base(db) => _svc = svc;

    /// <summary>گزارش مغایرت یک دوره.</summary>
    [HttpGet("diff")]
    public async Task<ActionResult<StkDiffResult>> Diff(
        [FromQuery] int sessionId,
        [FromQuery] bool onlyDiff = false)
        => Ok(await _svc.GetDiffAsync(sessionId, onlyDiff));
}
