using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== عملیات گروهی (Bulk) ==================
/// انتخاب چند مدرک و اجرای یک عمل روی همه — با چک دسترسی جداگانه برای هر مدرک
/// </summary>
[Route("api/doc-archive/bulk")]
public class DocBulkController : RbacControllerBase
{
    private readonly IDocAccessService _access;
    private const string Mod = "DocArchive";
    public DocBulkController(AppDbContext db, IDocAccessService access) : base(db) { _access = access; }
    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    private List<int> NormalizeIds(IEnumerable<int> ids) => ids.Where(i => i > 0).Distinct().Take(100).ToList();

    // ----------------- جابجایی گروهی پوشه -----------------
    [HttpPost("move")]
    public async Task<IActionResult> Move([FromBody] DocBulkMoveDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        var ids = NormalizeIds(dto.DocumentIds);
        if (ids.Count == 0) return BadRequest(new { message = "هیچ مدرکی انتخاب نشده." });
        if (dto.TargetFolderId <= 0) return BadRequest(new { message = "پوشه مقصد معتبر نیست." });
        var folderExists = await Db.DocFolders.AnyAsync(x => x.Id == dto.TargetFolderId);
        if (!folderExists) return BadRequest(new { message = "پوشه مقصد یافت نشد." });

        var manager = await IsManagerAsync();
        var (targetLevel, _) = await _access.FolderAccessAsync(MyUserId, manager, dto.TargetFolderId);
        if (targetLevel < DocAccessLevel.Write)
            return StatusCode(403, new { message = "برای انتقال به پوشه مقصد، دسترسی نوشتن روی آن پوشه لازم است." });

        var docs = await Db.Documents.Where(d => ids.Contains(d.Id) && !d.IsDeleted).ToListAsync();
        var result = new DocBulkResultDto { Requested = ids.Count };
        foreach (var d in docs)
        {
            var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, d.Id);
            if (lvl < DocAccessLevel.Write)
            {
                result.Errors.Add($"{d.Code}: دسترسی نوشتن ندارید.");
                continue;
            }
            if (!d.IsActive)
            {
                result.Errors.Add($"{d.Code}: مدرک غیرفعال است.");
                continue;
            }
            // اگر ورژنی در گردش است جابجایی همان مدرک را رد کن ولی بقیه را ادامه بده
            if (await Db.DocumentVersions.AnyAsync(v => v.DocumentId == d.Id && v.Status == DocVersionStatus.InReview))
            {
                result.Errors.Add($"{d.Code}: ورژنی در گردش دارد.");
                continue;
            }
            var oldFolder = d.FolderId;
            d.FolderId = dto.TargetFolderId;
            Db.DocumentLogs.Add(new DocumentLog { DocumentId = d.Id, Action = "BulkMove", Detail = $"انتقال گروهی: پوشه {oldFolder} → {dto.TargetFolderId}", UserId = MyUserId, UserName = MyUsername });
            result.Succeeded++;
        }
        result.Failed = result.Requested - result.Succeeded;
        result.Message = $"{result.Succeeded} مدرک منتقل شد." + (result.Failed > 0 ? $" {result.Failed} مورد خطا." : "");
        await Db.SaveChangesAsync();
        return Ok(result);
    }

    // ----------------- برچسب گروهی -----------------
    [HttpPost("tags")]
    public async Task<IActionResult> Tags([FromBody] DocBulkTagsDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        var ids = NormalizeIds(dto.DocumentIds);
        if (ids.Count == 0) return BadRequest(new { message = "هیچ مدرکی انتخاب نشده." });
        var tagIds = dto.TagIds.Where(i => i > 0).Distinct().ToList();
        var validTagIds = tagIds.Count == 0 ? new List<int>() : await Db.DocTags.Where(t => tagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();
        var isReplace = string.Equals(dto.Mode, "replace", StringComparison.OrdinalIgnoreCase);
        var manager = await IsManagerAsync();
        var docs = await Db.Documents.Where(d => ids.Contains(d.Id) && !d.IsDeleted).ToListAsync();
        var result = new DocBulkResultDto { Requested = ids.Count };
        foreach (var d in docs)
        {
            var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, d.Id);
            if (lvl < DocAccessLevel.Write)
            {
                result.Errors.Add($"{d.Code}: دسترسی نوشتن ندارید.");
                continue;
            }
            if (!d.IsActive)
            {
                result.Errors.Add($"{d.Code}: غیرفعال.");
                continue;
            }
            if (isReplace)
            {
                var old = await Db.DocumentTags.Where(t => t.DocumentId == d.Id).ToListAsync();
                Db.DocumentTags.RemoveRange(old);
            }
            var existing = isReplace ? new HashSet<int>() : (await Db.DocumentTags.Where(t => t.DocumentId == d.Id).Select(t => t.TagId).ToListAsync()).ToHashSet();
            foreach (var tid in validTagIds.Where(t => !existing.Contains(t)))
                Db.DocumentTags.Add(new DocumentTag { DocumentId = d.Id, TagId = tid });
            Db.DocumentLogs.Add(new DocumentLog { DocumentId = d.Id, Action = "BulkTag", Detail = $"برچسب گروهی: {(isReplace ? "جایگزینی" : "افزودن")} {validTagIds.Count} تگ", UserId = MyUserId, UserName = MyUsername });
            result.Succeeded++;
        }
        if (isReplace && validTagIds.Count == 0)
        {
            // حالت پاک کردن همه تگ‌ها هم با replace و لیست خالی پشتیبانی می‌شود
        }
        result.Failed = result.Requested - result.Succeeded;
        result.Message = $"{result.Succeeded} مدرک برچسب‌گذاری شد.";
        await Db.SaveChangesAsync();
        return Ok(result);
    }

    // ----------------- تمدید گروهی انقضا -----------------
    [HttpPost("expire")]
    public async Task<IActionResult> Expire([FromBody] DocBulkExpireDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        var ids = NormalizeIds(dto.DocumentIds);
        if (ids.Count == 0) return BadRequest(new { message = "هیچ مدرکی انتخاب نشده." });
        var manager = await IsManagerAsync();
        var docs = await Db.Documents.Where(d => ids.Contains(d.Id) && !d.IsDeleted).ToListAsync();
        var result = new DocBulkResultDto { Requested = ids.Count };
        foreach (var d in docs)
        {
            var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, d.Id);
            if (lvl < DocAccessLevel.Write)
            {
                result.Errors.Add($"{d.Code}: دسترسی نوشتن ندارید.");
                continue;
            }
            if (!d.IsActive)
            {
                result.Errors.Add($"{d.Code}: غیرفعال.");
                continue;
            }
            d.ExpireDate = dto.ExpireDate;
            Db.DocumentLogs.Add(new DocumentLog { DocumentId = d.Id, Action = "BulkExpire", Detail = dto.ExpireDate.HasValue ? $"تمدید گروهی تا {dto.ExpireDate:yyyy/MM/dd}" : "حذف تاریخ انقضا (گروهی)", UserId = MyUserId, UserName = MyUsername });
            result.Succeeded++;
        }
        result.Failed = result.Requested - result.Succeeded;
        result.Message = $"{result.Succeeded} مدرک به‌روزرسانی شد.";
        await Db.SaveChangesAsync();
        return Ok(result);
    }

    // ----------------- حذف گروهی (به سطل بازیافت) -----------------
    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] List<int> documentIds)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Delete") is { } f) return f;
        var ids = NormalizeIds(documentIds);
        if (ids.Count == 0) return BadRequest(new { message = "هیچ مدرکی انتخاب نشده." });
        var manager = await IsManagerAsync();
        var docs = await Db.Documents.Where(d => ids.Contains(d.Id) && !d.IsDeleted).ToListAsync();
        var result = new DocBulkResultDto { Requested = ids.Count };
        foreach (var d in docs)
        {
            var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, d.Id);
            if (lvl < DocAccessLevel.Full)
            {
                result.Errors.Add($"{d.Code}: نیاز به دسترسی کامل.");
                continue;
            }
            if (await Db.DocumentVersions.AnyAsync(v => v.DocumentId == d.Id && v.Status == DocVersionStatus.InReview))
            {
                result.Errors.Add($"{d.Code}: در گردش است.");
                continue;
            }
            d.IsDeleted = true;
            d.DeletedAt = DateTime.Now;
            d.DeletedByName = MyUsername;
            var tasks = await Db.DocCartableTasks.Where(t => t.DocumentId == d.Id && t.Status == 0).ToListAsync();
            foreach (var t in tasks) { t.Status = 1; t.DoneAt = DateTime.Now; }
            Db.DocumentLogs.Add(new DocumentLog { DocumentId = d.Id, Action = "BulkDelete", Detail = "حذف گروهی به سطل بازیافت", UserId = MyUserId, UserName = MyUsername });
            result.Succeeded++;
        }
        result.Failed = result.Requested - result.Succeeded;
        result.Message = $"{result.Succeeded} مدرک به سطل بازیافت منتقل شد.";
        await Db.SaveChangesAsync();
        return Ok(result);
    }
}
