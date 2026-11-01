using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== علاقه‌مندی و اخیراً دیده‌شده ==================
/// ستاره (Favorite) + تب اخیرها — هر دو فقط روی مدارک قابل‌دسترس کاربر
/// </summary>
[Route("api/doc-archive")]
public class DocFavoritesController : RbacControllerBase
{
    private readonly IDocAccessService _access;
    private const string Mod = "DocArchive";
    public DocFavoritesController(AppDbContext db, IDocAccessService access) : base(db) { _access = access; }

    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    /// <summary>تغییر وضعیت ستاره — اگر ستاره بود برداشته می‌شود، اگر نبود اضافه می‌شود.</summary>
    [HttpPost("favorites/{documentId:int}/toggle")]
    public async Task<IActionResult> Toggle(int documentId)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, documentId);
        if (lvl == DocAccessLevel.None)
            return StatusCode(403, new { message = "به این مدرک دسترسی ندارید." });

        var existing = await Db.DocFavorites.FirstOrDefaultAsync(x => x.UserId == MyUserId && x.DocumentId == documentId);
        bool nowFav;
        if (existing != null)
        {
            Db.DocFavorites.Remove(existing);
            nowFav = false;
        }
        else
        {
            // اطمینان از وجود مدرک و عدم حذف
            var exists = await Db.Documents.AnyAsync(d => d.Id == documentId && !d.IsDeleted);
            if (!exists) return NotFound(new { message = "مدرک یافت نشد." });
            Db.DocFavorites.Add(new DocFavorite { UserId = MyUserId, DocumentId = documentId });
            nowFav = true;
        }
        await Db.SaveChangesAsync();
        return Ok(new { isFavorite = nowFav });
    }

    /// <summary>فهرست علاقه‌مندی‌های من — فقط مدارکی که هنوز قابل‌دسترس هستند.</summary>
    [HttpGet("favorites")]
    public async Task<IActionResult> ListFavorites([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        var manager = await IsManagerAsync();
        var normSkip = Math.Max(0, skip);
        var normTake = take is > 0 ? Math.Clamp(take.Value, 1, 100) : 50;

        var favIds = await Db.DocFavorites.AsNoTracking()
            .Where(x => x.UserId == MyUserId)
            .OrderByDescending(x => x.Id)
            .Select(x => x.DocumentId)
            .ToListAsync();

        if (favIds.Count == 0) return Ok(new List<DocFavoriteDto>());

        var q = await DocQuery.AccessibleAsync(Db, _access, MyUserId, manager);
        var docs = await q.Where(d => favIds.Contains(d.Id) && !d.IsDeleted)
            .ToDictionaryAsync(d => d.Id, d => d);

        var folders = await Db.DocFolders.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Name);
        var favRows = await Db.DocFavorites.AsNoTracking()
            .Where(x => x.UserId == MyUserId && docs.Keys.Contains(x.DocumentId))
            .ToDictionaryAsync(x => x.DocumentId, x => x.CreatedAt);

        var map = await _access.DocumentAccessMapAsync(MyUserId, manager, docs.Keys);

        var result = new List<DocFavoriteDto>();
        foreach (var id in favIds)
        {
            if (!docs.TryGetValue(id, out var d)) continue;
            if (!map.TryGetValue(id, out var grant) || grant.Level == DocAccessLevel.None) continue;
            result.Add(new DocFavoriteDto
            {
                DocumentId = d.Id,
                Code = d.Code,
                Title = d.Title,
                FolderId = d.FolderId,
                FolderName = folders.TryGetValue(d.FolderId, out var fn) ? fn : "",
                FavoritedAt = favRows.TryGetValue(id, out var at) ? at : DateTime.Now,
                IsFavorite = true
            });
        }

        // صفحه‌بندی در حافظه (چون ترتیب علاقه‌مندی مهم است)
        var paged = result.Skip(normSkip).Take(normTake).ToList();
        return Ok(paged);
    }

    /// <summary>اخیراً دیده‌شده — ۱۰ مدرک آخر که کاربر باز کرده (از DocumentLogs).</summary>
    [HttpGet("recently-viewed")]
    public async Task<IActionResult> RecentlyViewed([FromQuery] int take = 10)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        var manager = await IsManagerAsync();
        take = Math.Clamp(take, 1, 30);

        // لاگ بازدید: هر بار که کاربر جزئیات مدرک را باز می‌کند یک View ثبت می‌کنیم (در DocumentsController.Get)
        // اینجا آخرین Viewهای یکتا را برمی‌گردانیم
        var viewedIds = await Db.DocumentLogs.AsNoTracking()
            .Where(l => l.UserId == MyUserId && (l.Action == "View" || l.Action == "Read"))
            .OrderByDescending(l => l.Id)
            .Select(l => l.DocumentId)
            .Distinct()
            .Take(take * 2) // کمی بیشتر بگیر تا پس از فیلتر دسترسی ۱۰ تا بماند
            .ToListAsync();

        if (viewedIds.Count == 0) return Ok(new List<DocRecentlyViewedDto>());

        var q = await DocQuery.AccessibleAsync(Db, _access, MyUserId, manager);
        var docs = await q.Where(d => viewedIds.Contains(d.Id) && !d.IsDeleted)
            .ToDictionaryAsync(d => d.Id, d => d);

        var folders = await Db.DocFolders.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Name);
        var times = await Db.DocumentLogs.AsNoTracking()
            .Where(l => l.UserId == MyUserId && viewedIds.Contains(l.DocumentId) && (l.Action == "View" || l.Action == "Read"))
            .GroupBy(l => l.DocumentId)
            .Select(g => new { DocumentId = g.Key, At = g.Max(x => x.CreatedAt) })
            .ToDictionaryAsync(x => x.DocumentId, x => x.At);

        var ordered = viewedIds.Where(id => docs.ContainsKey(id)).Take(take)
            .Select(id => new DocRecentlyViewedDto
            {
                DocumentId = id,
                Code = docs[id].Code,
                Title = docs[id].Title,
                FolderId = docs[id].FolderId,
                FolderName = folders.TryGetValue(docs[id].FolderId, out var fn) ? fn : "",
                ViewedAt = times.TryGetValue(id, out var at) ? at : DateTime.Now,
                Action = "View"
            }).ToList();

        return Ok(ordered);
    }

    /// <summary>وضعیت ستاره برای چند مدرک (برای نمایش آیکون در لیست).</summary>
    [HttpPost("favorites/status")]
    public async Task<IActionResult> Status([FromBody] List<int> documentIds)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        var ids = documentIds.Where(i => i > 0).Distinct().Take(100).ToList();
        var favs = await Db.DocFavorites.AsNoTracking()
            .Where(x => x.UserId == MyUserId && ids.Contains(x.DocumentId))
            .Select(x => x.DocumentId)
            .ToListAsync();
        var set = favs.ToHashSet();
        return Ok(ids.ToDictionary(id => id, id => set.Contains(id)));
    }
}
