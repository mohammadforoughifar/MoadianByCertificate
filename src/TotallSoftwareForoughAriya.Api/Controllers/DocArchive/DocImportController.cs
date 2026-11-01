using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ورود انبوه آرشیو. همهٔ مسیرها فقط با دسترسی «مدیریت» آرشیو.
/// شش مسیر، نه بیشتر: ساخت نوبت، پیش‌نمایش، ورود یک فایل، پایان، سابقه، گزارش اکسل.
/// </summary>
[ApiController]
[Route("api/doc-archive/import")]
public class DocImportController(AppDbContext db, IDocImportService import) : RbacControllerBase(db)
{
    private async Task<IActionResult?> GuardAsync()
        => await ForbiddenUnlessDocArchiveAsync("DocArchive", "Manage");

    /// <summary>وضعیت پیکربندی سرور.</summary>
    [HttpGet("config")]
    public async Task<IActionResult> Config()
    {
        if (await GuardAsync() is { } f) return f;
        return Ok(import.GetConfig());
    }

    /// <summary>ساخت یک نوبت ورود. هیچ فایلی هنوز ارسال نشده است.</summary>
    [HttpPost("batches")]
    public async Task<IActionResult> CreateBatch(DocImportBatchCreateDto dto)
    {
        if (await GuardAsync() is { } f) return f;
        try { return Ok(await import.CreateBatchAsync(dto ?? new DocImportBatchCreateDto(), MyUserId, MyUsername)); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// پیش‌نمایش: تحلیل نام فایل‌ها بدون نوشتن هیچ سندی.
    /// مرورگر فهرست مسیرها و حجم‌ها را می‌فرستد، نه خود فایل‌ها را.
    /// </summary>
    [HttpPost("batches/{id:int}/preview")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Preview(int id, DocImportPreviewRequestDto dto)
    {
        if (await GuardAsync() is { } f) return f;
        if (dto?.Items == null || dto.Items.Count == 0)
            return BadRequest(new { message = "فایلی برای بررسی ارسال نشد." });

        var items = dto.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.RelativePath))
            .Select(i => new DocImportPreviewItem(i.RelativePath.Trim(), Math.Max(0, i.SizeBytes)))
            .ToList();

        try { return Ok(await import.PreviewAsync(id, items, MyUserId, MyUsername)); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>ورود یک فایل: هم فایل ذخیره می‌شود، هم سند ساخته می‌شود.</summary>
    [HttpPost("batches/{id:int}/files")]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public async Task<IActionResult> UploadFile(int id, IFormFile file, [FromForm] string relativePath)
    {
        if (await GuardAsync() is { } f) return f;
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "فایلی ارسال نشد." });

        await using var stream = file.OpenReadStream();
        try
        {
            var outcome = await import.ImportFileAsync(id, relativePath ?? "", stream, file.Length, MyUserId, MyUsername);
            return Ok(new DocImportFileResultDto
            {
                Status = outcome.Status,
                Duplicate = outcome.Duplicate,
                MessageFa = outcome.MessageFa
            });
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>پایان نوبت: شمارش‌ها نهایی می‌شود و نوبت در سابقه «تمام شد» می‌خورد.</summary>
    [HttpPost("batches/{id:int}/finish")]
    public async Task<IActionResult> Finish(int id)
    {
        if (await GuardAsync() is { } f) return f;
        try { return Ok(await import.FinishAsync(id)); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>سابقهٔ نوبت‌های ورود.</summary>
    [HttpGet("batches")]
    public async Task<IActionResult> ListBatches([FromQuery] int take = 20)
    {
        if (await GuardAsync() is { } f) return f;
        return Ok(await import.ListBatchesAsync(take));
    }

    /// <summary>گزارش اکسل یک نوبت — هم بعد از پیش‌نمایش، هم بعد از ورود.</summary>
    [HttpGet("batches/{id:int}/report.xlsx")]
    public async Task<IActionResult> Report(int id)
    {
        if (await GuardAsync() is { } f) return f;
        try
        {
            var bytes = await import.BuildReportAsync(id);
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"import-report-{id}.xlsx");
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }
}
