using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== مدیریت تگ‌ها و برچسب‌های آرشیو اسناد ==================
/// </summary>
[Route("api/doc-archive/tags")]
public class DocTagsController : RbacControllerBase
{
    private const string Mod = "DocArchive";
    private readonly IDocAccessService _access;

    // ⚠️ دلیل خطای 500 روی GET api/doc-archive/tags: وجود دو سازندهٔ عمومی،
    // ActivatorUtilities را با «Multiple constructors accepting all given argument types»
    // متوقف می‌کرد و پاسخ 500 (بدون عبور از فیلتر خطای MVC) برمی‌گشت.
    // با [ActivatorUtilitiesConstructor] سازندهٔ کامل صریحاً انتخاب می‌شود.
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public DocTagsController(AppDbContext db, IDocAccessService access) : base(db) { _access = access; }

    /// <summary>سازگاری با تست‌های قدیمی که فقط DbContext می‌دهند (توسط DI استفاده نمی‌شود).</summary>
    public DocTagsController(AppDbContext db) : base(db) { _access = new Inventory.Api.Services.DocArchive.DocAccessService(db); }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var counts = await Db.DocumentTags.AsNoTracking()
            .GroupBy(t => t.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TagId, x => x.Count);

        var tags = await Db.DocTags.AsNoTracking()
            .OrderBy(t => t.Name).ThenBy(x => x.Id)
            .ToPageListAsync(pagination);

        return Ok(pagination.Result(tags.Select(t => new DocTagDto
        {
            Id = t.Id,
            Name = t.Name,
            Color = t.Color,
            Description = t.Description,
            CreatedAt = t.CreatedAt,
            DocumentCount = counts.TryGetValue(t.Id, out var c) ? c : 0
        })));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DocTagSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;

        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام تگ اجباری است." });

        if (await Db.DocTags.AnyAsync(t => t.Name == name))
            return BadRequest(new { message = $"تگ با عنوان «{name}» قبلاً ثبت شده است." });

        var tag = new DocTag
        {
            Name = name,
            Color = string.IsNullOrWhiteSpace(dto.Color) ? "#4f46e5" : dto.Color.Trim(),
            Description = dto.Description?.Trim(),
            CreatedAt = DateTime.Now
        };

        Db.DocTags.Add(tag);
        await Db.SaveChangesAsync();

        return Ok(new DocTagDto
        {
            Id = tag.Id,
            Name = tag.Name,
            Color = tag.Color,
            Description = tag.Description,
            CreatedAt = tag.CreatedAt,
            DocumentCount = 0
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] DocTagSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;

        var tag = await Db.DocTags.FindAsync(id);
        if (tag == null) return NotFound(new { message = "تگ یافت نشد." });

        var name = (dto.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام تگ اجباری است." });

        if (await Db.DocTags.AnyAsync(t => t.Name == name && t.Id != id))
            return BadRequest(new { message = $"تگ با عنوان «{name}» از قبل وجود دارد." });

        tag.Name = name;
        tag.Color = string.IsNullOrWhiteSpace(dto.Color) ? "#4f46e5" : dto.Color.Trim();
        tag.Description = dto.Description?.Trim();

        await Db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;

        var tag = await Db.DocTags.FindAsync(id);
        if (tag == null) return NotFound(new { message = "تگ یافت نشد." });

        // Remove tag associations
        var docTags = await Db.DocumentTags.Where(dt => dt.TagId == id).ToListAsync();
        Db.DocumentTags.RemoveRange(docTags);

        Db.DocTags.Remove(tag);
        await Db.SaveChangesAsync();

        return Ok(new { message = "تگ با موفقیت حذف شد." });
    }

    [HttpPost("/api/doc-archive/documents/{docId:int}/tags")]
    public async Task<IActionResult> SetDocumentTags(int docId, [FromBody] List<int> tagIds)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var doc = await Db.Documents.FindAsync(docId);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });

        // اصلاح امنیتی M1: تغییر تگ نیازمند دسترسی نوشتن است (نه فقط خواندن)
        var manager = await HasAsync(Mod, "Manage");
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, docId);
        if (lvl < DocAccessLevel.Write)
            return StatusCode(403, new { message = "تغییر برچسب مدرک نیازمند دسترسی نوشتن است." });

        var old = await Db.DocumentTags.Where(t => t.DocumentId == docId).ToListAsync();
        Db.DocumentTags.RemoveRange(old);

        var validTagIds = await Db.DocTags.Where(t => tagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();
        foreach (var tid in validTagIds)
        {
            Db.DocumentTags.Add(new DocumentTag { DocumentId = docId, TagId = tid });
        }

        await Db.SaveChangesAsync();
        return Ok(new { message = "تگ‌های مدرک با موفقیت ذخیره شدند." });
    }
}
