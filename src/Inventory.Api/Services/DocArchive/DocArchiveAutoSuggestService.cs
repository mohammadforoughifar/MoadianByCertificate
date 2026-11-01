using Inventory.Api.Data;
using Inventory.Api.Services.Ai;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Services.DocArchive;

// =====================================================================
// پیشنهاد خودکار برچسب و پوشه (B2) — بر اساس همان bge-m3
// اگر امبدینگ در دسترس بود معنایی، وگرنه کلیدواژه‌ای
// =====================================================================
public class DocArchiveAutoSuggestService
{
    private readonly AppDbContext _db;
    private readonly IAiEmbeddingClient _embed;
    private readonly IDocAccessService _access;
    private readonly ILogger<DocArchiveAutoSuggestService> _log;
    private const double MinTagScore = 0.26;
    private const double MinFolderScore = 0.24;

    public DocArchiveAutoSuggestService(AppDbContext db, IAiEmbeddingClient embed, IDocAccessService access, ILogger<DocArchiveAutoSuggestService> log)
    {
        _db = db; _embed = embed; _access = access; _log = log;
    }

    public async Task<DocAutoSuggestResultDto> SuggestAsync(int userId, DocAutoSuggestRequest req, CancellationToken ct)
    {
        var title = (req.Title ?? "").Trim();
        var desc = (req.Description ?? "").Trim();
        var snippet = (req.ContentSnippet ?? "").Trim();
        var queryText = AiTextUtil.Truncate($"{title}\n{desc}\n{snippet}", 1200).Trim();
        if (queryText.Length < 3)
            return new DocAutoSuggestResultDto { TagSuggestions = new(), Semantic = false, Note = "عنوان خیلی کوتاه است." };

        var tags = await _db.DocTags.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var folders = await _db.DocFolders.AsNoTracking().Where(f => f.IsActive).ToListAsync(ct);
        if (tags.Count == 0 && folders.Count == 0)
            return new DocAutoSuggestResultDto { TagSuggestions = new(), Semantic = false, Note = "تگ و پوشه‌ای تعریف نشده." };

        // تلاش معنایی
        float[]? qvec = null;
        try { qvec = await _embed.EmbedAsync(queryText, ct); } catch (Exception ex) { _log.LogDebug(ex, "امبدینگ پیشنهاد ناموفق."); }

        bool isManager = await IsManagerAsync(userId, ct);
        // برای پوشه‌ها فقط پوشه‌های قابل نوشتن را پیشنهاد بده
        var folderAccess = new Dictionary<int, (DocAccessLevel Level, bool Download)>();
        if (folders.Count > 0)
        {
            var fids = folders.Select(f => f.Id).ToList();
            folderAccess = await _access.DocumentAccessMapAsync(userId, isManager, fids);
            // اگر کاربر مدیر نیست، فقط پوشه‌های با Write را نگه دار برای پیشنهاد، ولی بقیه را هم با CanWrite=false نشان بده
        }

        if (qvec != null)
        {
            var tagScores = new List<(DocTag tag, double score)>();
            foreach (var t in tags)
            {
                var tText = $"{t.Name} {t.Description ?? ""}".Trim();
                float[]? tvec = null;
                try { tvec = await _embed.EmbedAsync(tText, ct); } catch { continue; }
                if (tvec == null || tvec.Length != qvec.Length) continue;
                var s = AiVectorMath.Cosine(qvec, tvec);
                if (s >= MinTagScore) tagScores.Add((t, s));
            }
            // اگر هیچ تگی معنایی نخورد، fallback کلیدواژه‌ای برای تگ‌ها
            List<DocTagSuggestionDto> tagSugs;
            if (tagScores.Count > 0)
            {
                tagSugs = tagScores.OrderByDescending(x => x.score).Take(5)
                    .Select(x => new DocTagSuggestionDto { TagId = x.tag.Id, Name = x.tag.Name, Color = x.tag.Color, Score = Math.Round(x.score, 3), Reason = "معنایی" }).ToList();
            }
            else
            {
                tagSugs = KeywordTagSuggestions(queryText, tags).Take(5).ToList();
            }

            // پوشه — معنایی
            DocFolderSuggestionDto? folderSug = null;
            double bestFolderScore = 0;
            DocFolder? bestFolder = null;
            foreach (var f in folders)
            {
                var fText = $"{f.Name} {f.Description ?? ""} {GetFolderPath(f, folders)}".Trim();
                float[]? fvec = null;
                try { fvec = await _embed.EmbedAsync(fText, ct); } catch { continue; }
                if (fvec == null || fvec.Length != qvec.Length) continue;
                var s = AiVectorMath.Cosine(qvec, fvec);
                if (s > bestFolderScore) { bestFolderScore = s; bestFolder = f; }
            }
            if (bestFolder != null && bestFolderScore >= MinFolderScore)
            {
                var canWrite = folderAccess.TryGetValue(bestFolder.Id, out var g) ? g.Level >= DocAccessLevel.Write : false;
                folderSug = new DocFolderSuggestionDto
                {
                    FolderId = bestFolder.Id,
                    Name = bestFolder.Name,
                    Path = GetFolderPath(bestFolder, folders),
                    Score = Math.Round(bestFolderScore, 3),
                    CanWrite = canWrite || isManager
                };
            }
            else
            {
                folderSug = KeywordFolderSuggestion(queryText, folders, folderAccess, isManager);
            }

            return new DocAutoSuggestResultDto { TagSuggestions = tagSugs, FolderSuggestion = folderSug, Semantic = true };
        }

        // fallback کلیدواژه‌ای کامل
        var kwTags = KeywordTagSuggestions(queryText, tags).Take(5).ToList();
        var kwFolder = KeywordFolderSuggestion(queryText, folders, folderAccess, isManager);
        return new DocAutoSuggestResultDto { TagSuggestions = kwTags, FolderSuggestion = kwFolder, Semantic = false, Note = "مدل معنایی در دسترس نبود؛ پیشنهاد کلیدواژه‌ای." };
    }

    private static List<DocTagSuggestionDto> KeywordTagSuggestions(string query, List<DocTag> tags)
    {
        var qTokens = AiTextUtil.Tokens(query);
        var list = new List<(DocTag t, double s)>();
        foreach (var t in tags)
        {
            var tTokens = AiTextUtil.Tokens(t.Name + " " + (t.Description ?? ""));
            if (tTokens.Count == 0) continue;
            var hit = qTokens.Count(tok => tTokens.Contains(tok));
            var score = qTokens.Count > 0 ? (double)hit / Math.Max(1, Math.Max(qTokens.Count, tTokens.Count)) : 0;
            // امتیاز کوچک برای تطابق جزئی
            if (AiTextUtil.NormalizeFa(query).Contains(AiTextUtil.NormalizeFa(t.Name), StringComparison.OrdinalIgnoreCase)) score = Math.Max(score, 0.5);
            if (score >= 0.15) list.Add((t, score));
        }
        return list.OrderByDescending(x => x.s).Select(x => new DocTagSuggestionDto { TagId = x.t.Id, Name = x.t.Name, Color = x.t.Color, Score = Math.Round(x.s, 3), Reason = "کلیدواژه" }).ToList();
    }

    private static DocFolderSuggestionDto? KeywordFolderSuggestion(string query, List<DocFolder> folders, Dictionary<int, (DocAccessLevel Level, bool Download)> access, bool isManager)
    {
        var qTokens = AiTextUtil.Tokens(query);
        DocFolder? best = null; double bestScore = 0;
        foreach (var f in folders)
        {
            var fTokens = AiTextUtil.Tokens(f.Name + " " + (f.Description ?? ""));
            if (fTokens.Count == 0) continue;
            var hit = qTokens.Count(tok => fTokens.Contains(tok));
            var score = qTokens.Count > 0 ? (double)hit / qTokens.Count : 0;
            if (AiTextUtil.NormalizeFa(query).Contains(AiTextUtil.NormalizeFa(f.Name), StringComparison.OrdinalIgnoreCase)) score = Math.Max(score, 0.5);
            if (score > bestScore) { bestScore = score; best = f; }
        }
        if (best != null && bestScore >= 0.15)
        {
            var canWrite = access.TryGetValue(best.Id, out var g) ? g.Level >= DocAccessLevel.Write : false;
            return new DocFolderSuggestionDto { FolderId = best.Id, Name = best.Name, Path = GetFolderPath(best, folders), Score = Math.Round(bestScore, 3), CanWrite = canWrite || isManager };
        }
        return null;
    }

    private static string GetFolderPath(DocFolder f, List<DocFolder> all)
    {
        var parts = new List<string> { f.Name };
        var cur = f.ParentId;
        int guard = 0;
        while (cur is int pid && guard++ < 10)
        {
            var p = all.FirstOrDefault(x => x.Id == pid);
            if (p == null) break;
            parts.Insert(0, p.Name);
            cur = p.ParentId;
        }
        return string.Join(" / ", parts);
    }

    private async Task<bool> IsManagerAsync(int userId, CancellationToken ct)
    {
        var roles = await _db.UserRoles.AsNoTracking().Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync(ct);
        if (roles.Count == 0)
        {
            var role = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct);
            return role == "Admin";
        }
        var perms = await _db.RolePermissions.AsNoTracking().Where(rp => roles.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToListAsync(ct);
        if (perms.Count == 0) return false;
        return await _db.Permissions.AsNoTracking().AnyAsync(p => perms.Contains(p.Id) && p.Module == "DocArchive" && p.Action == "Manage", ct);
    }
}
