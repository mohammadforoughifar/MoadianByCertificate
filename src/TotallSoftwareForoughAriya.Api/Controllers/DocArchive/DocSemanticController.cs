using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>جستجوی معنایی آرشیو (B1) — بردار bge-m3 + fallback کلیدواژه‌ای</summary>
[Route("api/doc-archive")]
public class DocSemanticController : RbacControllerBase
{
    private readonly DocArchiveSemanticService _svc;
    private const string Mod = "DocArchive";
    public DocSemanticController(AppDbContext db, DocArchiveSemanticService svc) : base(db) { _svc = svc; }

    [HttpPost("semantic-search")]
    public async Task<IActionResult> SemanticSearch([FromBody] DocSemanticSearchRequest req, CancellationToken ct)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        if (req == null || string.IsNullOrWhiteSpace(req.Query) || req.Query.Trim().Length < 2)
            return BadRequest(new { message = "عبارت جستجو حداقل ۲ حرف باشد." });
        if (req.Query.Length > 500) return BadRequest(new { message = "عبارت جستجو خیلی طولانی است." });
        var res = await _svc.SearchAsync(MyUserId, req, ct);
        return Ok(res);
    }

    [HttpPost("semantic-reindex")]
    public async Task<IActionResult> Reindex([FromQuery] int take = 30, CancellationToken ct = default)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        take = Math.Clamp(take, 1, 200);
        var (idx, fail, total) = await _svc.IndexMissingAsync(take, ct);
        return Ok(new { indexed = idx, failed = fail, total, message = $"{idx} مدرک ایندکس شد، {fail} ناموفق." });
    }

    [HttpPost("suggest")]
    public async Task<IActionResult> Suggest([FromBody] DocAutoSuggestRequest req, [FromServices] DocArchiveAutoSuggestService suggestSvc, CancellationToken ct)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        if (req == null || string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { message = "عنوان را وارد کنید." });
        if (req.Title.Length > 500 || (req.Description?.Length ?? 0) > 2000) return BadRequest(new { message = "متن طولانی است." });
        var res = await suggestSvc.SuggestAsync(MyUserId, req, ct);
        return Ok(res);
    }
}
