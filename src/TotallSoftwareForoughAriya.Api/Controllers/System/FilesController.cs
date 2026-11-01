using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Services;

// نکته: این فایل پیش‌تر «namespace Inventory.Api.Controllers.System» داشت که باعث می‌شد
// نام «System» برای همهٔ فایل‌های داخل فضای نام Inventory.Api.Controllers سایه بیفتد
// (System.Text / System.Collections / System.Numerics و… پیدا نمی‌شدند → ۲۶ خطای کامپایل).
// مثل سایر فایل‌های همین پوشه، فضای نام روی Inventory.Api.Controllers تنظیم شد.
namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/files")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly FileStore _store;

    public FilesController(FileStore store)
    {
        _store = store;
    }

    [HttpPost("upload/{module}")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(string module, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "فایلی ارسال نشده است." });

        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم مجاز ۲۰ مگابایت است." });

        using var stream = file.OpenReadStream();
        var relPath = await _store.SaveAsync(module, 0, stream, file.FileName, "rte");
        var url = $"/uploads/{relPath.TrimStart('/')}";

        return Ok(new
        {
            url,
            fileName = file.FileName,
            size = file.Length,
            contentType = file.ContentType
        });
    }
}
