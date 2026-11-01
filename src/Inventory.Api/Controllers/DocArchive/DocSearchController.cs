using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== جستجوی پیشرفته، تمام‌متن (Full-Text) و پردازش OCR ==================
/// </summary>
[Route("api/doc-archive")]
public class DocSearchController : RbacControllerBase
{
    private readonly IDocAccessService _access;
    private readonly IDocIndexService _indexService;
    private readonly IDocTextExtractorService _extractor;
    private readonly IDocDownloadConfirmService _confirm;

    private const string Mod = "DocArchive";

    public DocSearchController(
        AppDbContext db,
        IDocAccessService access,
        IDocIndexService indexService,
        IDocTextExtractorService extractor,
        IDocDownloadConfirmService confirm) : base(db)
    {
        _access = access;
        _indexService = indexService;
        _extractor = extractor;
        _confirm = confirm;
    }

    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    // =========================================================================
    // جستجوی ترکیبی و تمام‌متن
    // =========================================================================

    [HttpPost("search")]
    public Task<IActionResult> AdvancedSearch([FromBody] DocSearchFilterDto filter) => SearchCore(filter, false);

    [HttpPost("search-page")]
    public Task<IActionResult> SearchPage([FromBody] DocSearchFilterDto filter) => SearchCore(filter, true);

    private async Task<IActionResult> SearchCore(DocSearchFilterDto filter, bool paged)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        if (!string.IsNullOrWhiteSpace(filter.LinkedModule) || filter.LinkedEntityId.HasValue)
            return StatusCode(410, new { code = "DOC_ARCHIVE_ERP_DISABLED", message = "اتصال آرشیو به سامانه ERP غیرفعال شده است." });
        filter.TagIds ??= new(); filter.FileTypes ??= new();
        if ((filter.Search?.Length ?? 0) > 500 || (filter.ContentSearch?.Length ?? 0) > 500 || filter.TagIds.Count > 100 || filter.FileTypes.Count > 20)
            return BadRequest(new { message = "فیلتر جستجو بیش از حد طولانی است." });
        if (filter.CreatedTo?.Year == 9999 || filter.ExpiryTo?.Year == 9999 || (filter.CreatedFrom > filter.CreatedTo) || (filter.ExpiryFrom > filter.ExpiryTo))
            return BadRequest(new { message = "بازه تاریخ معتبر نیست." });
        var manager = await IsManagerAsync();
        var q = await DocQuery.AccessibleAsync(Db, _access, MyUserId, manager);
        q = DocQuery.ApplyFilters(Db, q, filter, DocClock.Today);
        if (filter.FolderId is > 0)
        {
            var folderIds = filter.IncludeSubfolders ? await GetFolderAndDescendantIdsAsync(filter.FolderId.Value) : new HashSet<int> { filter.FolderId.Value };
            q = q.Where(d => folderIds.Contains(d.FolderId));
        }
        var readable = await DocQuery.AccessibleAsync(Db, _access, MyUserId, manager, DocAccessLevel.Read);
        // A metadata-only grant must not reveal content matches; confidential text requires a live password confirmation.
        var confidentialIds = await readable.Where(d => d.RequireDownloadConfirm && !d.IsDeleted).Select(d => d.Id).ToListAsync();
        var confirmedIds = confidentialIds.Where(id => _confirm.IsConfirmed(MyUserId, id)).ToArray();
        var readableIds = readable.Where(d => !d.RequireDownloadConfirm || confirmedIds.Contains(d.Id)).Select(d => d.Id);
        IQueryable<DocExtractedText> Matches(string term)
        {
            var normalized = _extractor.Normalize(term);
            var query = Db.DocExtractedTexts.AsNoTracking().Where(e => e.Status == "Indexed" && readableIds.Contains(e.DocumentId));
            var tokens = _extractor.Tokenize(term).Take(12).ToArray();
            if (tokens.Length == 0) return query.Where(e => e.NormalizedText.Contains(normalized));
            foreach (var token in tokens) query = query.Where(e => e.NormalizedText.Contains(token));
            return query;
        }
        var searchStr = filter.Search?.Trim();
        var contentSearchStr = filter.ContentSearch?.Trim();
        if (!string.IsNullOrWhiteSpace(contentSearchStr))
        {
            var matchingIds = Matches(contentSearchStr).Select(e => e.DocumentId);
            q = q.Where(d => matchingIds.Contains(d.Id));
        }
        if (!string.IsNullOrWhiteSpace(searchStr))
        {
            var matchingIds = Matches(searchStr).Select(e => e.DocumentId);
            q = q.Where(d => d.Title.Contains(searchStr) || d.Code.Contains(searchStr) || (d.CustomerCode ?? "").Contains(searchStr) || (d.Description ?? "").Contains(searchStr) || matchingIds.Contains(d.Id));
        }
        var total = await q.CountAsync();
        var size = Math.Clamp(filter.PageSize, 10, 100);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (total + size - 1) / size));
        var ordered = q.OrderByDescending(d => d.Id);
        var docs = await (paged ? ordered.Skip((page - 1) * size).Take(size) : ordered).ToListAsync();
        Dictionary<int, (string Snippet, string FileName, string SourceType)> contentMatches = new();
        var term = string.IsNullOrWhiteSpace(contentSearchStr) ? searchStr : contentSearchStr;
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pageIds = docs.Select(d => d.Id).ToArray();
            // One bounded excerpt per document in a single SQL projection.
            var raw = term;
            var matches = Matches(term).Where(e => pageIds.Contains(e.DocumentId));
            var firstIds = matches.GroupBy(e => e.DocumentId).Select(g => g.Min(e => e.Id));
            var excerpts = await Db.DocExtractedTexts.AsNoTracking().Where(e => firstIds.Contains(e.Id))
                .Select(e => new { e.DocumentId, e.FileName, e.SourceType, Snippet = e.ExtractedText.Substring(e.ExtractedText.IndexOf(raw) >= 0 ? e.ExtractedText.IndexOf(raw) : 0, 240) }).ToListAsync();
            foreach (var match in excerpts) contentMatches[match.DocumentId] = (match.Snippet, match.FileName, match.SourceType);
        }
        var ids = docs.Select(d => d.Id).ToList();

        var folders = await Db.DocFolders.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        // منبع واحد: اولویت مجوزها باید در لیست، جزئیات و عملیات سرور یکسان باشد.
        var documentAccess = await _access.DocumentAccessMapAsync(MyUserId, manager, ids);
        var versions = await Db.DocumentVersions.AsNoTracking().Where(v => ids.Contains(v.DocumentId)).ToListAsync();
        var links = await Db.DocumentLinks.AsNoTracking().Where(l => ids.Contains(l.DocumentId))
            .GroupBy(l => l.DocumentId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);

        // Fetch tags for these documents
        var allDocTags = await (from dt in Db.DocumentTags.AsNoTracking()
                                where ids.Contains(dt.DocumentId)
                                join t in Db.DocTags.AsNoTracking() on dt.TagId equals t.Id
                                select new { dt.DocumentId, Tag = new DocTagDto
                                {
                                    Id = t.Id, Name = t.Name, Color = t.Color, Description = t.Description, CreatedAt = t.CreatedAt
                                }}).ToListAsync();
        var docTagMap = allDocTags.GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Tag).ToList());

        // Check which docs have indexed content
        var indexedDocIds = (await Db.DocExtractedTexts.AsNoTracking()
            .Where(e => ids.Contains(e.DocumentId) && e.Status == "Indexed")
            .Select(e => e.DocumentId)
            .Distinct().ToListAsync()).ToHashSet();

        var result = new List<DocumentListDto>();
        foreach (var d in docs)
        {
            var (level, dl) = documentAccess.TryGetValue(d.Id, out var grant)
                ? grant : (DocAccessLevel.None, false);
            if (level == DocAccessLevel.None) continue;

            var vs = versions.Where(v => v.DocumentId == d.Id).ToList();
            var last = vs.OrderByDescending(v => v.VersionNo).FirstOrDefault();
            var activeV = vs.Where(v => v.IsActive).OrderByDescending(v => v.VersionNo).FirstOrDefault();
            var expired = d.ExpireDate.HasValue && d.ExpireDate.Value.Date < DocClock.Today;
            int? daysLeft = d.ExpireDate.HasValue ? (d.ExpireDate.Value.Date - DocClock.Today).Days : null;
            var expiringSoon = daysLeft is >= 0 && daysLeft <= 60;

            // ExpiryStatus filter
            if (filter.ExpiryStatus == "expiring" && !expiringSoon) continue;
            if (filter.ExpiryStatus == "expired" && !expired) continue;
            if (filter.ExpiryStatus == "valid" && (expired || !d.ExpireDate.HasValue)) continue;

            string? snippet = null;
            string? matchFileName = null;
            string? matchSource = null;
            if (contentMatches.TryGetValue(d.Id, out var matchInfo))
            {
                snippet = matchInfo.Snippet;
                matchFileName = matchInfo.FileName;
                matchSource = matchInfo.SourceType;
            }

            result.Add(new DocumentListDto
            {
                Id = d.Id,
                FolderId = d.FolderId,
                FolderName = folders.TryGetValue(d.FolderId, out var fn) ? fn : "",
                Title = d.Title,
                Code = d.Code,
                CustomerCode = d.CustomerCode,
                ExpireDate = d.ExpireDate,
                IsExpired = expired,
                DaysToExpire = daysLeft,
                IsExpiringSoon = expiringSoon,
                AllowMultipleActiveVersions = d.AllowMultipleActiveVersions,
                IsPublic = d.IsPublic,
                RequireDownloadConfirm = d.RequireDownloadConfirm,
                WatermarkPreview = d.WatermarkPreview,
                CreatedByName = d.CreatedByName,
                CreatedAt = d.CreatedAt,
                IsActive = d.IsActive,
                DeactivatedAt = d.DeactivatedAt,
                DeactivatedByName = d.DeactivatedByName,
                DeactivateReason = d.DeactivateReason,
                IsDeleted = d.IsDeleted,
                DeletedAt = d.DeletedAt,
                DeletedByName = d.DeletedByName,
                VersionCount = vs.Count,
                ActiveVersionNo = activeV?.VersionNo ?? 0,
                LastVersionStatus = (DocVersionStatusDto)(int)(last?.Status ?? DocVersionStatus.Draft),
                LinkCount = links.TryGetValue(d.Id, out var lc) ? lc : 0,
                MyLevel = (DocAccessLevelDto)(int)level,
                MyCanDownload = dl,
                Tags = docTagMap.TryGetValue(d.Id, out var tlist) ? tlist : new(),
                HasIndexedContent = indexedDocIds.Contains(d.Id),
                ContentSnippet = snippet,
                MatchedAttachmentFileName = matchFileName,
                MatchedSourceType = matchSource,
            });
        }

        return paged ? Ok(new DocSearchPageDto { Items = result, Total = total, Page = page, PageSize = size }) : Ok(result);
    }

    // =========================================================================
    // مشاهده متن‌های استخراج‌شده و نتایج OCR یک مدرک
    // =========================================================================

    [HttpGet("documents/{id:int}/extracted-texts")]
    public async Task<IActionResult> GetExtractedTexts(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Read)
            return StatusCode(403, new { message = "مشاهده محتوای استخراج‌شده نیازمند دسترسی خواندن است." });

        var doc = await Db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (doc == null) return NotFound();
        if (doc.RequireDownloadConfirm && !_confirm.IsConfirmed(MyUserId, id))
            return StatusCode(403, new { message = "برای مشاهده متن، تأیید رمز لازم است.", code = "PASSWORD_CONFIRM_REQUIRED" });
        var texts = await Db.DocExtractedTexts.AsNoTracking()
            .Where(e => e.DocumentId == id)
            .OrderByDescending(e => e.IndexedAt).ThenBy(x => x.Id)
            .Select(e => new DocExtractedTextDto
            {
                Id = e.Id,
                DocumentId = e.DocumentId,
                VersionId = e.VersionId,
                AttachmentId = e.AttachmentId,
                FileName = e.FileName,
                ContentType = e.ContentType,
                SourceType = e.SourceType,
                ExtractedText = e.ExtractedText,
                Status = e.Status,
                ErrorMessage = e.ErrorMessage,
                CharacterCount = e.CharacterCount,
                IndexedAt = e.IndexedAt
            })
            .ToPageListAsync(pagination);

        return Ok(pagination.Result(texts));
    }

    // =========================================================================
    // اجرای دستی OCR و بازایندکس یک پیوست مشخص
    // =========================================================================

    [HttpPost("attachments/{attachmentId:int}/ocr")]
    public async Task<IActionResult> RunOcrOnAttachment(int attachmentId)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var att = await Db.AppAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.Module == "DocVersion");
        if (att == null) return NotFound(new { message = "پیوست یافت نشد." });

        var version = await Db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == att.RefId);
        if (version == null) return NotFound(new { message = "ورژن مدرک یافت نشد." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, version.DocumentId);
        if (lvl < DocAccessLevel.Write)
            return StatusCode(403, new { message = "اجرای OCR نیازمند دسترسی نوشتن است." });

        if (!await Db.Documents.AnyAsync(d => d.Id == version.DocumentId && !d.IsDeleted)) return NotFound();
        await _indexService.QueueAttachmentIndexingAsync(attachmentId);
        return Ok(new DocOcrRunResultDto { Success = true, Message = "فایل در صف پردازش قرار گرفت." });
    }

    // =========================================================================
    // متن استخراج‌شدهٔ یک پیوست (برای پیش‌نمایش فایل‌های Word/Excel داخل برنامه)
    // =========================================================================

    [HttpGet("attachments/{attachmentId:int}/text")]
    public async Task<IActionResult> GetAttachmentText(int attachmentId)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var att = await Db.AppAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.Module == "DocVersion");
        if (att == null) return NotFound(new { message = "پیوست یافت نشد." });

        var version = await Db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == att.RefId);
        if (version == null) return NotFound(new { message = "ورژن مدرک یافت نشد." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, version.DocumentId);
        if (lvl < DocAccessLevel.Read)
            return StatusCode(403, new { message = "پیش‌نمایش نیازمند دسترسی خواندن است." });

        // مدرک محرمانه: متن استخراج‌شده هم فقط با اعطای «تایید مجدد رمز» معتبر قابل مشاهده است
        var confidential = await Db.Documents.Where(d => d.Id == version.DocumentId)
            .Select(d => d.RequireDownloadConfirm).FirstOrDefaultAsync();
        if (confidential && !_confirm.IsConfirmed(MyUserId, version.DocumentId))
            return StatusCode(403, new
            {
                code = "PASSWORD_CONFIRM_REQUIRED",
                documentId = version.DocumentId,
                message = "این مدرک محرمانه است؛ برای مشاهده متن فایل‌ها تایید مجدد رمز لازم است."
            });

        var row = await Db.DocExtractedTexts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AttachmentId == attachmentId);

        return Ok(new
        {
            attachmentId,
            fileName = att.FileName,
            available = row != null && row.Status == "Indexed",
            status = row?.Status,
            sourceType = row?.SourceType,
            characterCount = row?.CharacterCount ?? 0,
            text = row?.Status == "Indexed" && row.ExtractedText.Length <= 400_000
                ? row.ExtractedText
                : (row?.Status == "Indexed" ? row.ExtractedText[..400_000] + "…" : ""),
            errorMessage = row?.Status == "Failed" ? row.ErrorMessage : null
        });
    }

    // =========================================================================
    // بازایندکس کلیه اسناد (فقط مدیر آرشیو)
    // =========================================================================

    [HttpPost("reindex")]
    public async Task<IActionResult> ReindexAll()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;

        var result = await _indexService.ReindexAllAsync();
        return Ok(result);
    }

    // =========================================================================
    // متد کمکی دریافت شناسه پوشه و همه زیرپوشه‌ها
    // =========================================================================

    private async Task<HashSet<int>> GetFolderAndDescendantIdsAsync(int rootFolderId)
    {
        var all = await Db.DocFolders.AsNoTracking().Select(f => new { f.Id, f.ParentId }).ToListAsync();
        var set = new HashSet<int> { rootFolderId };

        void Collect(int pid)
        {
            foreach (var c in all.Where(x => x.ParentId == pid))
            {
                if (set.Add(c.Id)) Collect(c.Id);
            }
        }
        Collect(rootFolderId);
        return set;
    }
}
