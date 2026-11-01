using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Shared.Contracts;
using RadisHr.Shared.Models;
using Paging = Inventory.Api.Services.Paging;

namespace RadisHr.Api.Controllers;

/// <summary>ذخیره‌سازی فایل — جایگزین سمت سرور برای IndexedDB (radisHrFilesV015)</summary>
[ApiController]
[Authorize]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private const long MaxBytes = 20 * 1024 * 1024;
    private readonly AppDbContext _db;
    public FilesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<object>> List([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok((await Paging.QueryAsync(_db.StoredFiles.AsNoTracking()
            .OrderByDescending(f => f.StoredAt)
            .Select(f => new { f.Id, f.Uid, f.Name, f.ContentType, f.Size, f.UploadedBy, f.StoredAt }), skip, take, 500)).Result());

    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<IdResponse>> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ApiMessage(false, "فایلی انتخاب نشده است."));
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new ApiMessage(false, "حجم فایل نباید بیش از ۲۰ مگابایت باشد."));

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);

        var stored = new StoredFile
        {
            Uid = Guid.NewGuid().ToString("N"),
            Name = file.FileName,
            ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Size = file.Length,
            Content = stream.ToArray(),
            UploadedBy = User.Identity?.Name ?? "نامشخص"
        };
        _db.StoredFiles.Add(stored);
        await _db.SaveChangesAsync();
        return new IdResponse(stored.Id, stored.Uid);
    }

    [HttpGet("{uid}")]
    public async Task<IActionResult> Download(string uid)
    {
        var file = await _db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Uid == uid);
        if (file == null) return NotFound(new ApiMessage(false, "فایل یافت نشد."));
        return File(file.Content, file.ContentType, file.Name);
    }

    [Authorize(Roles = "hr,ceo,hse")]
    [HttpDelete("{uid}")]
    public async Task<ActionResult<ApiMessage>> Delete(string uid)
    {
        var file = await _db.StoredFiles.FirstOrDefaultAsync(f => f.Uid == uid);
        if (file == null) return NotFound(new ApiMessage(false, "فایل یافت نشد."));
        _db.StoredFiles.Remove(file);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "فایل حذف شد.");
    }
}
