using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services.Ai;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Services.DocArchive;

// =====================================================================
// جستجوی معنایی آرشیو اسناد (B1)
// بردارها با همان IAiEmbeddingClient (bge-m3) ساخته می‌شوند و در
// جدول مشترک AiDocEmbeddings با DocType="doc_archive" ذخیره می‌شوند.
// اگر مدل در دسترس نباشد، به جستجوی کلیدواژه‌ای fallback می‌شود.
// دیده‌بانی در لحظه جستجو اعمال می‌شود (فقط مدارک قابل‌خواندن کاربر).
// =====================================================================
public class DocArchiveSemanticService
{
    private readonly AppDbContext _db;
    private readonly IAiEmbeddingClient _embeddings;
    private readonly IDocAccessService _access;
    private readonly ILogger<DocArchiveSemanticService> _log;

    public const string DocType = "doc_archive";
    private const int MaxChars = 1500;
    private const int MaxCandidates = 2000;
    private const int LazyBuildPerSearch = 8;
    private const double MinSemanticScore = 0.22;
    private const double MinKeywordScore = 0.05;

    private record Cand(int DocId, string Title, string Body, string FolderName);

    public DocArchiveSemanticService(AppDbContext db, IAiEmbeddingClient embeddings, IDocAccessService access, ILogger<DocArchiveSemanticService> log)
    {
        _db = db; _embeddings = embeddings; _access = access; _log = log;
    }

    // ==================== جستجو ====================

    public async Task<DocSemanticSearchResultDto> SearchAsync(int userId, DocSemanticSearchRequest req, CancellationToken ct)
    {
        var query = (req.Query ?? "").Trim();
        if (query.Length < 2) return new DocSemanticSearchResultDto { Hits = new(), Semantic = false, Note = "عبارت جستجو خیلی کوتاه است." };
        var take = Math.Clamp(req.Take, 1, 20);
        var tagIds = req.TagIds ?? new List<int>();

        // مدیر آرشیو یا نه
        var isManager = await IsManagerAsync(userId, ct);
        var cands = await LoadCandidatesAsync(userId, isManager, req, ct);
        if (cands.Count == 0) return new DocSemanticSearchResultDto { Hits = new(), Semantic = false, TotalCandidates = 0 };

        float[]? qvec = null;
        try { qvec = await _embeddings.EmbedAsync(query, ct); } catch (Exception ex) { _log.LogDebug(ex, "امبدینگ پرس‌وجو ناموفق."); }

        var scored = new List<(Cand c, double score)>();
        if (qvec != null)
        {
            var vecs = await LoadVectorsAsync(cands, lazyBuild: true, ct);
            foreach (var c in cands)
            {
                double score;
                if (vecs.TryGetValue(c.DocId, out var dv) && dv.Length == qvec.Length)
                    score = AiVectorMath.Cosine(qvec, dv);
                else
                    score = KeywordScore(query, c.Title + "\n" + c.Body) * 0.5;
                if (score >= MinSemanticScore) scored.Add((c, score));
            }
            var top = scored.OrderByDescending(x => x.score).Take(take).ToList();
            var hits = await ToHitsAsync(top, userId, isManager, query, ct);
            return new DocSemanticSearchResultDto { Hits = hits, Semantic = true, TotalCandidates = cands.Count };
        }
        // fallback کلیدواژه‌ای
        foreach (var c in cands)
        {
            var s = KeywordScore(query, c.Title + "\n" + c.Body);
            if (s >= MinKeywordScore) scored.Add((c, s));
        }
        var topKw = scored.OrderByDescending(x => x.score).Take(take).ToList();
        var hitsKw = await ToHitsAsync(topKw, userId, isManager, query, ct);
        return new DocSemanticSearchResultDto { Hits = hitsKw, Semantic = false, Note = "مدل معنایی در دسترس نبود؛ جستجوی کلیدواژه‌ای انجام شد.", TotalCandidates = cands.Count };
    }

    private async Task<List<DocSemanticHitDto>> ToHitsAsync(List<(Cand c, double score)> top, int userId, bool isManager, string query, CancellationToken ct)
    {
        if (top.Count == 0) return new();
        var ids = top.Select(x => x.c.DocId).ToList();
        var docs = await _db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        var folders = await _db.DocFolders.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        var accessMap = await _access.DocumentAccessMapAsync(userId, isManager, ids);
        var tagMap = await LoadTagMapAsync(ids, ct);
        var list = new List<DocSemanticHitDto>();
        foreach (var (c, score) in top)
        {
            if (!docs.TryGetValue(c.DocId, out var d)) continue;
            if (!accessMap.TryGetValue(c.DocId, out var grant) || grant.Level == DocAccessLevel.None) continue;
            list.Add(new DocSemanticHitDto
            {
                DocumentId = c.DocId,
                Code = d.Code,
                Title = d.Title,
                FolderId = d.FolderId,
                FolderName = folders.TryGetValue(d.FolderId, out var fn) ? fn : c.FolderName,
                Snippet = BuildSnippet(query, c.Body),
                Score = Math.Round(score, 3),
                MyLevel = (DocAccessLevelDto)(int)grant.Level,
                MyCanDownload = grant.Download,
                Tags = tagMap.TryGetValue(c.DocId, out var tl) ? tl : new(),
                ExpireDate = d.ExpireDate,
                Link = $"/doc-archive/documents/{c.DocId}"
            });
        }
        return list;
    }

    private static string BuildSnippet(string query, string body)
    {
        var txt = AiTextUtil.StripHtml(body);
        if (txt.Length <= 180) return txt;
        // سعی کن اطراف اولین توکن پیدا شده را ببر
        var toks = AiTextUtil.Tokens(query).Take(3).ToList();
        foreach (var t in toks)
        {
            var idx = txt.IndexOf(t, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var start = Math.Max(0, idx - 60);
                var len = Math.Min(180, txt.Length - start);
                return (start > 0 ? "…" : "") + txt.Substring(start, len) + (start + len < txt.Length ? "…" : "");
            }
        }
        return txt[..180] + "…";
    }

    private static double KeywordScore(string query, string text)
    {
        var qt = AiTextUtil.Tokens(query);
        if (qt.Count == 0) return 0;
        var dt = AiTextUtil.Tokens(text);
        if (dt.Count == 0) return 0;
        var hit = qt.Count(t => dt.Contains(t));
        // امتیاز ساده + کمی وزن برای عنوان
        return (double)hit / qt.Count;
    }

    // ==================== ایندکس ====================

    public async Task<(int indexed, int failed, int total)> IndexMissingAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 500);
        var all = await LoadAllForIndexAsync(ct);
        if (all.Count == 0) return (0, 0, 0);
        var existing = await _db.AiDocEmbeddings.AsNoTracking()
            .Where(e => e.DocType == DocType)
            .Select(e => new { e.DocId, e.TextHash, HasVec = e.VectorJson != null && e.VectorJson != "" })
            .ToListAsync(ct);
        var have = existing.Where(e => e.HasVec).ToDictionary(e => e.DocId, e => e.TextHash);
        var missing = all.Where(c => !have.TryGetValue(c.DocId, out var h) || h != TextHash(c)).Take(take).ToList();
        int indexed = 0, failed = 0;
        foreach (var c in missing)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var vec = await _embeddings.EmbedAsync(EmbedText(c), ct);
                if (vec == null || vec.Length == 0) { failed++; continue; }
                await UpsertAsync(c, JsonSerializer.Serialize(vec), ct);
                indexed++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                _log.LogWarning(ex, "ایندکس معنایی آرشیو {DocId} ناموفق.", c.DocId);
                if (failed >= 5) break;
            }
        }
        return (indexed, failed, missing.Count);
    }

    private async Task<Dictionary<int, float[]>> LoadVectorsAsync(List<Cand> cands, bool lazyBuild, CancellationToken ct)
    {
        var map = new Dictionary<int, float[]>();
        var want = cands.Select(c => c.DocId).ToHashSet();
        var rows = await _db.AiDocEmbeddings.AsNoTracking().Where(e => e.DocType == DocType).ToListAsync(ct);
        var byId = rows.Where(e => want.Contains(e.DocId)).ToDictionary(e => e.DocId);
        int built = 0;
        foreach (var c in cands)
        {
            float[]? vec = null;
            if (byId.TryGetValue(c.DocId, out var row) && !string.IsNullOrWhiteSpace(row.VectorJson))
            {
                if (row.TextHash == TextHash(c)) vec = TryParseVector(row.VectorJson);
            }
            if (vec == null && lazyBuild && built < LazyBuildPerSearch)
            {
                try
                {
                    var fresh = await _embeddings.EmbedAsync(EmbedText(c), ct);
                    if (fresh != null && fresh.Length > 0)
                    {
                        await UpsertAsync(c, JsonSerializer.Serialize(fresh), ct);
                        vec = fresh; built++;
                    }
                }
                catch { break; }
            }
            if (vec != null) map[c.DocId] = vec;
        }
        return map;
    }

    private async Task UpsertAsync(Cand c, string vectorJson, CancellationToken ct)
    {
        var hash = TextHash(c);
        var row = await _db.AiDocEmbeddings.FirstOrDefaultAsync(e => e.DocType == DocType && e.DocId == c.DocId, ct);
        if (row == null)
        {
            _db.AiDocEmbeddings.Add(new AiDocEmbedding { DocType = DocType, DocId = c.DocId, TextHash = hash, VectorJson = vectorJson, UpdatedAt = DateTime.Now });
        }
        else
        {
            row.TextHash = hash; row.VectorJson = vectorJson; row.UpdatedAt = DateTime.Now;
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) { _log.LogDebug(ex, "Upsert تکراری doc_archive {DocId}", c.DocId); _db.ChangeTracker.Clear(); }
    }

    private static string EmbedText(Cand c) => AiTextUtil.Truncate(c.Title + "\n" + c.Body, MaxChars);
    private static string TextHash(Cand c)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(EmbedText(c)));
        return Convert.ToHexString(bytes)[..16];
    }
    private static float[]? TryParseVector(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<float[]>(json); } catch { return null; }
    }

    // ==================== بارگذاری ====================

    private async Task<List<Cand>> LoadCandidatesAsync(int userId, bool isManager, DocSemanticSearchRequest req, CancellationToken ct)
    {
        // فیلتر پایه + دیده‌بانی
        var q = await DocQuery.AccessibleAsync(_db, _access, userId, isManager, DocAccessLevel.Read);
        // وضعیت
        if (req.Status == "active") q = q.Where(d => d.IsActive && !d.IsDeleted);
        else if (req.Status == "inactive") q = q.Where(d => !d.IsActive && !d.IsDeleted);
        else q = q.Where(d => !d.IsDeleted);
        if (req.FolderId is > 0)
        {
            var folderIds = req.IncludeSubfolders ? await GetFolderAndDescendantsAsync(req.FolderId.Value, ct) : new HashSet<int> { req.FolderId.Value };
            q = q.Where(d => folderIds.Contains(d.FolderId));
        }
        if (req.TagIds != null && req.TagIds.Count > 0)
        {
            var tids = req.TagIds.Where(i => i > 0).Distinct().ToList();
            if (tids.Count > 0)
            {
                var docIdsWithTags = await _db.DocumentTags.AsNoTracking()
                    .Where(dt => tids.Contains(dt.TagId))
                    .Select(dt => dt.DocumentId).Distinct().ToListAsync(ct);
                q = q.Where(d => docIdsWithTags.Contains(d.Id));
            }
        }
        q = q.OrderByDescending(d => d.Id).Take(MaxCandidates);
        var docs = await q.Select(d => new { d.Id, d.Title, d.Description, d.FolderId }).ToListAsync(ct);
        if (docs.Count == 0) return new();
        var folders = await _db.DocFolders.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        var docIds = docs.Select(d => d.Id).ToList();
        var tagTexts = await LoadTagTextsAsync(docIds, ct);
        var extracted = await _db.DocExtractedTexts.AsNoTracking()
            .Where(e => docIds.Contains(e.DocumentId) && e.Status == "Indexed")
            .GroupBy(e => e.DocumentId)
            .Select(g => new { DocumentId = g.Key, Text = string.Join("\n", g.Select(x => x.ExtractedText).Take(2)) })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Text, ct);
        var list = new List<Cand>();
        foreach (var d in docs)
        {
            var tags = tagTexts.TryGetValue(d.Id, out var tt) ? tt : "";
            var ext = extracted.TryGetValue(d.Id, out var et) ? et : "";
            var body = (d.Description ?? "") + (tags.Length > 0 ? "\nبرچسب‌ها: " + tags : "") + (ext.Length > 0 ? "\n" + ext[..Math.Min(ext.Length, 1200)] : "");
            var fn = folders.TryGetValue(d.FolderId, out var fname) ? fname : "";
            list.Add(new Cand(d.Id, d.Title, body, fn));
        }
        return list;
    }

    private async Task<List<Cand>> LoadAllForIndexAsync(CancellationToken ct)
    {
        var docs = await _db.Documents.AsNoTracking()
            .Where(d => !d.IsDeleted)
            .OrderByDescending(d => d.Id).Take(MaxCandidates)
            .Select(d => new { d.Id, d.Title, d.Description }).ToListAsync(ct);
        if (docs.Count == 0) return new();
        var ids = docs.Select(d => d.Id).ToList();
        var tagTexts = await LoadTagTextsAsync(ids, ct);
        var extracted = await _db.DocExtractedTexts.AsNoTracking()
            .Where(e => ids.Contains(e.DocumentId) && e.Status == "Indexed")
            .GroupBy(e => e.DocumentId)
            .Select(g => new { DocumentId = g.Key, Text = string.Join("\n", g.Select(x => x.ExtractedText).Take(2)) })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Text, ct);
        return docs.Select(d =>
        {
            var tags = tagTexts.TryGetValue(d.Id, out var tt) ? tt : "";
            var ext = extracted.TryGetValue(d.Id, out var et) ? et : "";
            var body = (d.Description ?? "") + (tags.Length > 0 ? "\nبرچسب‌ها: " + tags : "") + (ext.Length > 0 ? "\n" + ext[..Math.Min(ext.Length, 1200)] : "");
            return new Cand(d.Id, d.Title, body, "");
        }).ToList();
    }

    private async Task<Dictionary<int, string>> LoadTagTextsAsync(List<int> docIds, CancellationToken ct)
    {
        var rows = await (from dt in _db.DocumentTags.AsNoTracking()
                          where docIds.Contains(dt.DocumentId)
                          join t in _db.DocTags.AsNoTracking() on dt.TagId equals t.Id
                          select new { dt.DocumentId, t.Name }).ToListAsync(ct);
        return rows.GroupBy(x => x.DocumentId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name)));
    }

    private async Task<Dictionary<int, List<DocTagDto>>> LoadTagMapAsync(List<int> ids, CancellationToken ct)
    {
        var rows = await (from dt in _db.DocumentTags.AsNoTracking()
                          where ids.Contains(dt.DocumentId)
                          join t in _db.DocTags.AsNoTracking() on dt.TagId equals t.Id
                          select new { dt.DocumentId, Tag = new DocTagDto { Id = t.Id, Name = t.Name, Color = t.Color, Description = t.Description, CreatedAt = t.CreatedAt } }).ToListAsync(ct);
        return rows.GroupBy(x => x.DocumentId).ToDictionary(g => g.Key, g => g.Select(x => x.Tag).ToList());
    }

    private async Task<HashSet<int>> GetFolderAndDescendantsAsync(int rootId, CancellationToken ct)
    {
        var all = await _db.DocFolders.AsNoTracking().Select(f => new { f.Id, f.ParentId }).ToListAsync(ct);
        var set = new HashSet<int> { rootId };
        void Collect(int pid) { foreach (var c in all.Where(x => x.ParentId == pid)) if (set.Add(c.Id)) Collect(c.Id); }
        Collect(rootId);
        return set;
    }

    private async Task<bool> IsManagerAsync(int userId, CancellationToken ct)
    {
        // همان منطق HasAsync("DocArchive","Manage") ولی بدون HttpContext
        var roles = await _db.UserRoles.AsNoTracking().Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync(ct);
        if (roles.Count == 0)
        {
            var user = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct);
            if (user == "Admin") return true;
            return false;
        }
        var perms = await _db.RolePermissions.AsNoTracking().Where(rp => roles.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToListAsync(ct);
        if (perms.Count == 0) return false;
        return await _db.Permissions.AsNoTracking().AnyAsync(p => perms.Contains(p.Id) && p.Module == "DocArchive" && p.Action == "Manage", ct);
    }
}

// ورکر ایندکس دوره‌ای آرشیو (هر ۱۵ دقیقه، ۲۰ سند)
public class DocArchiveSemanticWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DocArchiveSemanticWorker> _log;
    public DocArchiveSemanticWorker(IServiceProvider sp, ILogger<DocArchiveSemanticWorker> log) { _sp = sp; _log = log; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var opts = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
                if (opts.Enabled && opts.SearchEnabled)
                {
                    var svc = scope.ServiceProvider.GetRequiredService<DocArchiveSemanticService>();
                    var (idx, fail, _) = await svc.IndexMissingAsync(20, stoppingToken);
                    if (idx > 0 || fail > 0) _log.LogInformation("ایندکس معنایی آرشیو: {Idx} موفق، {Fail} ناموفق.", idx, fail);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "خطای ورکر معنایی آرشیو."); }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
