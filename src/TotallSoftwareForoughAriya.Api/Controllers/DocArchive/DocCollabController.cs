using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>نظرات/منشن + تشخیص تکراری + ردیاب نشت — 2025-10-09</summary>
[Route("api/doc-archive")]
public class DocCollabController : RbacControllerBase
{
    private readonly DocArchiveSemanticService _semantic;
    public DocCollabController(AppDbContext db, DocArchiveSemanticService semantic) : base(db) { _semantic = semantic; }
    private const string Mod = "DocArchive";

    // ==================== نظرات ====================
    [HttpGet("documents/{id:int}/comments")]
    public async Task<IActionResult> GetComments(int id, CancellationToken ct)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        try
        {
            return await GetCommentsCore(id, ct);
        }
        catch (Exception ex) when (ex.Message.Contains("Invalid object name") && ex.Message.Contains("DocComment"))
        {
            await EnsureCollabTablesAsync(ct);
            return await GetCommentsCore(id, ct);
        }
    }
    private async Task<IActionResult> GetCommentsCore(int id, CancellationToken ct)
    {
        var doc = await Db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });
        // چک دسترسی خواندن
        bool isManager = await IsManagerAsync(MyUserId, ct);
        if (!isManager)
        {
            var (level, _) = await new DocAccessService(Db).DocumentAccessAsync(MyUserId, isManager, id);
            if (level < DocAccessLevel.Read) return StatusCode(403, new { message = "دسترسی ندارید." });
        }
        var comments = await Db.DocComments.AsNoTracking().Where(c => c.DocumentId == id && !c.IsDeleted).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var mentions = await Db.DocCommentMentions.AsNoTracking().Where(m => comments.Select(c => c.Id).Contains(m.CommentId)).ToListAsync(ct);
        var byId = comments.ToDictionary(c => c.Id, c => new DocCommentDto
        {
            Id = c.Id, DocumentId = c.DocumentId, VersionId = c.VersionId, ParentId = c.ParentId,
            AuthorUserId = c.AuthorUserId, AuthorName = c.AuthorName, Body = c.Body, CreatedAt = c.CreatedAt, IsResolved = c.IsResolved,
            MentionedUserIds = mentions.Where(m => m.CommentId == c.Id).Select(m => m.MentionedUserId).ToList(),
            MentionedUserNames = mentions.Where(m => m.CommentId == c.Id).Select(m => m.MentionedUserName).ToList()
        });
        var roots = new List<DocCommentDto>();
        foreach (var c in byId.Values.OrderBy(x => x.CreatedAt))
        {
            if (c.ParentId is int pid && byId.TryGetValue(pid, out var p)) p.Replies.Add(c);
            else roots.Add(c);
        }
        return Ok(roots);
    }

    [HttpPost("documents/{id:int}/comments")]
    public async Task<IActionResult> AddComment(int id, [FromBody] DocCommentCreateDto dto, CancellationToken ct)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        await EnsureCollabTablesAsync(ct);
        if (dto == null || string.IsNullOrWhiteSpace(dto.Body)) return BadRequest(new { message = "متن نظر خالی است." });
        if (dto.Body.Length > 2000) return BadRequest(new { message = "متن تا ۲۰۰۰ حرف." });
        var doc = await Db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });
        bool isManager = await IsManagerAsync(MyUserId, ct);
        if (!isManager)
        {
            var (level2, _) = await new DocAccessService(Db).DocumentAccessAsync(MyUserId, isManager, id);
            if (level2 < DocAccessLevel.Read) return StatusCode(403, new { message = "دسترسی ندارید." });
        }
        if (dto.ParentId is int pid)
        {
            var parent = await Db.DocComments.FirstOrDefaultAsync(c => c.Id == pid && c.DocumentId == id, ct);
            if (parent == null) return BadRequest(new { message = "نظر والد یافت نشد." });
        }
        var me = await Db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId, ct);
        var name = string.IsNullOrWhiteSpace((me?.FirstName ?? "") + (me?.LastName ?? "")) ? (me?.Username ?? $"کاربر {MyUserId}") : $"{me!.FirstName} {me!.LastName}".Trim();
        var c = new DocComment { DocumentId = id, VersionId = dto.VersionId, ParentId = dto.ParentId, AuthorUserId = MyUserId, AuthorName = name, Body = dto.Body.Trim() };
        Db.DocComments.Add(c);
        await Db.SaveChangesAsync(ct);
        if (dto.MentionedUserIds != null && dto.MentionedUserIds.Count > 0)
        {
            var users = await Db.Users.AsNoTracking().Where(u => dto.MentionedUserIds.Contains(u.Id)).ToListAsync(ct);
            foreach (var u in users)
                Db.DocCommentMentions.Add(new DocCommentMention { CommentId = c.Id, MentionedUserId = u.Id, MentionedUserName = string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? "")) ? u.Username : $"{u.FirstName} {u.LastName}".Trim() });
            await Db.SaveChangesAsync(ct);
        }
        // لاگ
        Db.DocumentLogs.Add(new DocumentLog { DocumentId = id, Action = "Comment", UserId = MyUserId, UserName = name, CreatedAt = DateTime.Now, Detail = $"نظر #{c.Id}: {c.Body[..Math.Min(60, c.Body.Length)]}" });
        await Db.SaveChangesAsync(ct);
        return Ok(new { id = c.Id });
    }

    [HttpPost("documents/{id:int}/comments/{cid:int}/resolve")]
    public async Task<IActionResult> Resolve(int id, int cid, CancellationToken ct)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        await EnsureCollabTablesAsync(ct);
        var c = await Db.DocComments.FirstOrDefaultAsync(x => x.Id == cid && x.DocumentId == id, ct);
        if (c == null) return NotFound(new { message = "نظر یافت نشد." });
        c.IsResolved = !c.IsResolved; c.ResolvedByUserId = MyUserId; c.ResolvedAt = DateTime.Now;
        await Db.SaveChangesAsync(ct);
        return Ok(new { resolved = c.IsResolved });
    }

    [HttpDelete("documents/{id:int}/comments/{cid:int}")]
    public async Task<IActionResult> DeleteComment(int id, int cid, CancellationToken ct)
    {
        await EnsureCollabTablesAsync(ct);
        var c = await Db.DocComments.FirstOrDefaultAsync(x => x.Id == cid && x.DocumentId == id, ct);
        if (c == null) return NotFound(new { message = "نظر یافت نشد." });
        bool isManager = await IsManagerAsync(MyUserId, ct);
        if (c.AuthorUserId != MyUserId && !isManager) return StatusCode(403, new { message = "فقط نویسنده یا مدیر." });
        c.IsDeleted = true;
        await Db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    // ==================== تشخیص تکراری ====================
    [HttpPost("duplicate-check")]
    public async Task<IActionResult> DuplicateCheck([FromBody] DocDuplicateCheckRequest req, CancellationToken ct)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        await EnsureCollabTablesAsync(ct);
        if (req == null) return BadRequest(new { message = "درخواست خالی." });
        var hits = new List<DocDuplicateHitDto>();
        bool hashHit = false;

        // 1) هش دقیق — اگر فایل قبلا آپلود شده
        if (!string.IsNullOrWhiteSpace(req.FileHash))
        {
            var hash = req.FileHash.Trim().ToLower();
            var byHash = await Db.DocLeakTraces.AsNoTracking().Where(x => x.FileHash == hash).Select(x => x.DocumentId).Distinct().ToListAsync(ct);
            // همچنین از DocumentVersions اگر هش ذخیره شده بود — فعلا از LeakTraces
            if (byHash.Count > 0)
            {
                var docs = await Db.Documents.AsNoTracking().Where(d => byHash.Contains(d.Id)).ToListAsync(ct);
                var folders = await Db.DocFolders.AsNoTracking().ToListAsync(ct);
                foreach (var d in docs)
                {
                    var fn = folders.FirstOrDefault(f => f.Id == d.FolderId)?.Name ?? "";
                    hits.Add(new DocDuplicateHitDto { DocumentId = d.Id, Code = d.Code, Title = d.Title, FolderName = fn, Score = 1.0, Reason = "hash-exact", CreatedAt = d.CreatedAt });
                }
                hashHit = hits.Count > 0;
            }
        }

        // 2) معنایی — مقایسه عنوان/snippet با مدارک موجود (سریع، 20 تای اخیر)
        var q = $"{req.Title ?? ""} {req.Snippet ?? ""} {req.FileName ?? ""}".Trim();
        if (q.Length >= 4 && !hashHit)
        {
            try
            {
                var sem = await _semantic.SearchAsync(MyUserId, new DocSemanticSearchRequest { Query = q, Take = 8, IncludeSubfolders = true }, ct);
                foreach (var h in sem.Hits.Take(5))
                {
                    if (h.Score >= 0.52) // فقط خیلی شبیه
                        hits.Add(new DocDuplicateHitDto { DocumentId = h.DocumentId, Code = h.Code, Title = h.Title, FolderName = h.FolderName, Score = Math.Round(h.Score, 3), Reason = sem.Semantic ? "semantic" : "keyword", CreatedAt = DateTime.Now });
                }
            }
            catch { }
        }

        // 3) نام فایل دقیق
        if (!string.IsNullOrWhiteSpace(req.FileName) && hits.Count == 0)
        {
            var fn = req.FileName.Trim();
            var byName = await Db.Documents.AsNoTracking().Where(d => d.Title == fn || d.Code == fn).Take(3).ToListAsync(ct);
            var folders = await Db.DocFolders.AsNoTracking().ToListAsync(ct);
            foreach (var d in byName)
                hits.Add(new DocDuplicateHitDto { DocumentId = d.Id, Code = d.Code, Title = d.Title, FolderName = folders.FirstOrDefault(f => f.Id == d.FolderId)?.Name ?? "", Score = 0.85, Reason = "name", CreatedAt = d.CreatedAt });
        }

        var isDup = hits.Any(h => h.Score >= 0.78);
        return Ok(new DocDuplicateResultDto { Hits = hits.OrderByDescending(h => h.Score).Take(5).ToList(), IsDuplicate = isDup, Note = hashHit ? "هش دقیق یافت شد." : hits.Count > 0 ? "موارد مشابه یافت شد." : null });
    }

    // ==================== ردیاب نشت ====================
    [HttpGet("documents/{id:int}/leak-traces")]
    public async Task<IActionResult> LeakTraces(int id, CancellationToken ct)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        await EnsureCollabTablesAsync(ct);
        var traces = await Db.DocLeakTraces.AsNoTracking().Where(t => t.DocumentId == id).OrderByDescending(t => t.CreatedAt).Take(50).ToListAsync(ct);
        return Ok(traces.Select(t => new DocLeakTraceDto { Id = t.Id, DocumentId = t.DocumentId, UserName = t.UserName, VisibleWatermark = t.VisibleWatermark, Action = t.Action, CreatedAt = t.CreatedAt, FileHash = t.FileHash }));
    }

    private async Task<bool> IsManagerAsync(int userId, CancellationToken ct)
    {
        var roles = await Db.UserRoles.AsNoTracking().Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync(ct);
        if (roles.Count == 0) { var role = await Db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct); return role == "Admin"; }
        var perms = await Db.RolePermissions.AsNoTracking().Where(rp => roles.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToListAsync(ct);
        if (perms.Count == 0) return false;
        return await Db.Permissions.AsNoTracking().AnyAsync(p => perms.Contains(p.Id) && p.Module == Mod && p.Action == "Manage", ct);
    }

    private async Task EnsureCollabTablesAsync(CancellationToken ct)
    {
        try
        {
            // بدون FK تا حتی اگر Documents با نام متفاوت بود هم ساخته شود
            await Db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID('dbo.DocComments','U') IS NULL
CREATE TABLE dbo.DocComments(
  Id INT IDENTITY(1,1) PRIMARY KEY,
  DocumentId INT NOT NULL,
  VersionId INT NULL,
  ParentId INT NULL,
  AuthorUserId INT NOT NULL,
  AuthorName NVARCHAR(150) NOT NULL,
  Body NVARCHAR(2000) NOT NULL,
  CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
  EditedAt DATETIME2 NULL,
  IsResolved BIT NOT NULL DEFAULT 0,
  ResolvedByUserId INT NULL,
  ResolvedAt DATETIME2 NULL,
  IsDeleted BIT NOT NULL DEFAULT 0
);
IF OBJECT_ID('dbo.DocCommentMentions','U') IS NULL
CREATE TABLE dbo.DocCommentMentions(
  Id INT IDENTITY(1,1) PRIMARY KEY,
  CommentId INT NOT NULL,
  MentionedUserId INT NOT NULL,
  MentionedUserName NVARCHAR(150) NOT NULL
);
IF OBJECT_ID('dbo.DocLeakTraces','U') IS NULL
CREATE TABLE dbo.DocLeakTraces(
  Id INT IDENTITY(1,1) PRIMARY KEY,
  DocumentId INT NOT NULL,
  VersionId INT NULL,
  UserId INT NOT NULL,
  UserName NVARCHAR(150) NOT NULL,
  IpAddress NVARCHAR(60) NULL,
  VisibleWatermark NVARCHAR(300) NOT NULL,
  InvisiblePayload NVARCHAR(300) NOT NULL,
  FileHash NVARCHAR(64) NOT NULL DEFAULT '',
  CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
  [Action] NVARCHAR(30) NOT NULL DEFAULT 'Download'
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DocComments_DocumentId' AND object_id=OBJECT_ID('dbo.DocComments'))
  CREATE INDEX IX_DocComments_DocumentId ON dbo.DocComments(DocumentId, CreatedAt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DocLeakTraces_DocumentId' AND object_id=OBJECT_ID('dbo.DocLeakTraces'))
  CREATE INDEX IX_DocLeakTraces_DocumentId ON dbo.DocLeakTraces(DocumentId, CreatedAt);
", ct);
        }
        catch { /* اگر قبلاً ساخته شده یا SQLite بود، نادیده بگیر */ }
    }
}
