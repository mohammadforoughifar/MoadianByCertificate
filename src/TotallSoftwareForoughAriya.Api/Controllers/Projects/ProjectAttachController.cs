using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>پیوست‌های پروژه — رمزنگاری‌شده روی دیسک سرور. ماژول دسترسی: ProjectAttach</summary>
[Route("api/projectattach")]
public class ProjectAttachController : RbacControllerBase
{
    private const string Module = "ProjectAttach";
    private readonly IProjectFileProtection _protect;

    public ProjectAttachController(AppDbContext db, IProjectFileProtection protect) : base(db)
        => _protect = protect;

    private static string DisplayOf(User u)
    {
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    /// <summary>لیست پیوست‌های یک پروژه</summary>
    [HttpGet("project/{projectId:int}")]
    public async Task<IActionResult> GetForProject(int projectId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        var list = await Db.ProjectAttaches.AsNoTracking()
            .Include(a => a.User)
            .Where(a => a.ProjectId == projectId && !a.IsDelete)
            .OrderByDescending(a => a.Id)
            .ToPageListAsync(pagination);

        var result = list.Select(a => new ProjectAttachDto
        {
            Id = a.Id,
            FileName = SafeDecrypt(a.OriginalFileNameEncrypted),
            FileSize = a.FileSize,
            Type = a.Type,
            DateSabt = a.DateSabt,
            ProjectId = a.ProjectId,
            UserName = a.User is null ? null : DisplayOf(a.User)
        }).ToList();
        return Ok(pagination.Result(result));
    }

    /// <summary>آپلود یک یا چند فایل برای یک پروژه (multipart)</summary>
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    [DisableRequestSizeLimit]
    [HttpPost]
    public async Task<IActionResult> Upload(
        [FromForm] int projectId,
        [FromForm] int type,
        [FromForm] List<IFormFile> files,
        [FromServices] Hubs.INotifyService notify,
        CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;

        var project = await Db.ProjectEntryExits.AsNoTracking()
            .Where(p => p.Id == projectId && !p.IsDelete)
            .Select(p => new { p.Id, p.CodeProject })
            .FirstOrDefaultAsync(ct);
        if (projectId <= 0 || project is null)
            return BadRequest(new { message = "پروژه معتبر نیست." });
        if (files is null || files.Count == 0)
            return BadRequest(new { message = "هیچ فایلی ارسال نشده است." });
        if (type is < 0) type = 0;

        var uploaded = 0;
        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            var originalName = Path.GetFileName(file.FileName); // حذف مسیر احتمالی
            var ext = Path.GetExtension(originalName);

            // هر پروژه پوشهٔ خودش (کد پروژه) و داخل آن تصاویر از مستندات جدا — درخواست کاربر
            var subFolder = $"{ProjectFileProtection.SanitizeSegment(project.CodeProject)}/{ProjectFileProtection.KindFolderOf(ext)}";

            string stored;
            await using (var src = file.OpenReadStream())
                stored = await _protect.EncryptAndStoreAsync(src, subFolder, ct);

            Db.ProjectAttaches.Add(new ProjectAttach
            {
                OriginalFileNameEncrypted = _protect.EncryptString(originalName),
                StoredFileName = stored,
                Extension = ext,
                FileSize = file.Length,
                DateSabt = DateTime.Now,
                Type = type,
                UserId = MyUserId,
                ProjectId = projectId
            });
            uploaded++;
        }

        if (uploaded == 0)
            return BadRequest(new { message = "فایل‌های ارسالی خالی هستند." });

        await Db.SaveChangesAsync(ct);
        try { await notify.BroadcastChangedAsync("projects"); } catch { }
        return Ok(new { uploaded });
    }

    /// <summary>دانلود — رمزگشایی محتوا و بازگردانی با نام اصلی فایل</summary>
    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        var a = await Db.ProjectAttaches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete, ct);
        if (a is null) return NotFound(new { message = "پیوست پیدا نشد." });

        var fileName = SafeDecrypt(a.OriginalFileNameEncrypted);

        MemoryStream plain;
        try
        {
            plain = await _protect.LoadAndDecryptAsync(a.StoredFileName, ct);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "فایل روی سرور پیدا نشد." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "رمزگشایی فایل انجام نشد — فایل خراب است یا کلید رمز تغییر کرده است." });
        }

        return File(plain, ContentTypeOf(a.Extension), fileName);
    }

    /// <summary>حذف — رکورد نرم + حذف فیزیکی فایل رمزنگاری‌شده</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromServices] Hubs.INotifyService notify)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;

        var a = await Db.ProjectAttaches.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (a is null) return NotFound(new { message = "پیوست پیدا نشد." });

        a.IsDelete = true;
        try { _protect.Delete(a.StoredFileName); } catch { /* ممکن است از قبل حذف شده باشد */ }
        await Db.SaveChangesAsync();
        try { await notify.BroadcastChangedAsync("projects"); } catch { }
        return Ok(new { ok = true });
    }

    // ==================== کمکی ====================

    private string SafeDecrypt(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted)) return "بدون‌نام";
        try
        {
            var res = _protect.DecryptString(encrypted);
            return string.IsNullOrWhiteSpace(res) ? encrypted : res;
        }
        catch
        {
            return encrypted;
        }
    }

    private static string ContentTypeOf(string? ext) => (ext ?? "").ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".zip" => "application/zip",
        ".rar" => "application/vnd.rar",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".dwg" => "application/acad",
        _ => "application/octet-stream"
    };
}
