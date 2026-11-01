using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>مدیریت کالاها.</summary>
[Route("api/products")]
[Inventory.Api.Infrastructure.ModulePermission("Products")]
public class ProductsController : ApiControllerBase
{
    private readonly IInventoryService _service;
    private readonly FileStore _store;

    public ProductsController(IInventoryService service, FileStore store)
    {
        _service = service;
        _store = store;
    }

    /// <summary>فهرست کالاها با جستجو، فیلتر انبار و صفحه‌بندی.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<Product>>> GetAll(
        [FromQuery] string? search, [FromQuery] bool? below, [FromQuery] int? warehouseId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _service.GetProductsAsync(search, below == true, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize), warehouseId));

    /// <summary>فهرست خلاصه کالاها برای لیست‌های انتخاب.</summary>
    [HttpGet("lookups")]
    public async Task<ActionResult<List<LookupItem>>> GetLookups([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        return Ok(await Paging.ResultAsync(pagination => _service.GetProductLookupsAsync(pagination), skip, take));
    }

    /// <summary>دریافت یک کالا.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Product>> GetById(int id)
    {
        var product = await _service.GetProductAsync(id);
        return product is null ? NotFound(new { message = "کالا یافت نشد." }) : Ok(product);
    }

    /// <summary>ایجاد یا ویرایش کالا.</summary>
    [HttpPost]
    public async Task<ActionResult<Product>> Save([FromBody] Product product)
        => Ok(await _service.SaveProductAsync(product));

    /// <summary>حذف کالا.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteProductAsync(id);
        return Ok(new { ok = true });
    }

    /// <summary>ورود گروهی کالا از فایل اکسل (xlsx).</summary>
    [HttpPost("import")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<ExcelImportResult>> Import(IFormFile? file, [FromQuery] int? warehouseId = null)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل اکسل انتخاب نشده است." });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "فقط فایل با فرمت xlsx پشتیبانی می‌شود." });

        if (warehouseId is null or <= 0)
            return BadRequest(new { message = "انتخاب انبار برای ورود اکسل الزامی است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        // ذخیره‌سازی فایل اکسل ورودی در wwwroot/uploads/catalog/imports/
        await _store.SaveAsync("catalog", 0, ms, file.FileName, "imports");
        ms.Position = 0;

        return Ok(await _service.ImportProductsAsync(ms, warehouseId));
    }

    /// <summary>
    /// دانلود فایل اکسل نمونه برای ورود گروهی کالا.
    /// AllowAnonymous: این فایل دادهٔ محرمانه ندارد و لینک مستقیم مرورگر هدر JWT ندارد.
    /// </summary>
    [HttpGet("import/template")]
    [AllowAnonymous]
    public IActionResult DownloadTemplate()
    {
        var bytes = _service.BuildProductTemplate();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: "Products-Template.xlsx");
    }
}
