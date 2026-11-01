using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// جستجوی معنایی روی نامه‌ها و تیکت‌ها (§۲۲).
// بردارها با مدل لوکال bge-m3 ساخته می‌شوند (IAiEmbeddingClient) و در
// جدول AiDocEmbeddings ذخیره می‌شوند؛ اگر مدل در دسترس نبود، جستجوی
// کلیدواژه‌ای جایگزین می‌شود (الگوی AiKnowledgeService).
// فیلتر پایه: حذف‌شده‌ها و محرمانه/سری هرگز ایندکس نمی‌شوند.
// دیده‌بانی در لحظه جستجو اعمال می‌شود (نه موقع ایندکس).
// =====================================================================

public class AiDocMatch
{
    public string DocType { get; set; } = "";
    public int DocId { get; set; }
    public string Title { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string Meta { get; set; } = "";
    public string Link { get; set; } = "";
    public double Score { get; set; }
}

public class AiSearchService
{
    private readonly AppDbContext _db;
    private readonly IAiEmbeddingClient _embeddings;
    private readonly ILogger<AiSearchService> _log;

    public static readonly string[] DocTypes = { "inner_letter", "incoming_letter", "outgoing_letter", "ticket" };

    private const int MaxTextChars = 1500;
    private const int MaxCandidates = 2000;
    private const int LazyBuildPerSearch = 10;
    private const double MinSemanticScore = 0.2;
    private const double MinKeywordScore = 0.05;

    private record DocCand(string DocType, int DocId, string Title, string Body, string Meta, string Link);

    public AiSearchService(AppDbContext db, IAiEmbeddingClient embeddings, ILogger<AiSearchService> log)
    {
        _db = db;
        _embeddings = embeddings;
        _log = log;
    }

    public static string DocTypeFa(string t) => t switch
    {
        "inner_letter" => "نامه داخلی",
        "incoming_letter" => "نامه وارده",
        "outgoing_letter" => "نامه صادره",
        _ => "تیکت",
    };

    public static List<string> ResolveScope(string? scope)
    {
        var s = AiTextUtil.NormalizeFa(scope ?? "").Trim();
        if (s.Contains("تیکت") || s == "ticket" || s == "tickets") return new() { "ticket" };
        if (s.Contains("نامه") || s == "letter" || s == "letters")
            return new() { "inner_letter", "incoming_letter", "outgoing_letter" };
        if (DocTypes.Contains(s)) return new() { s };
        return DocTypes.ToList();
    }

    // ==================== جستجو ====================

    public async Task<(List<AiDocMatch> matches, bool semantic)> SearchAsync(
        int userId, string query, string? scope, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 10);
        var types = ResolveScope(scope);
        var cands = await LoadCandidatesAsync(userId, types, ct);
        if (cands.Count == 0)
            return (new(), false);

        float[]? qvec = null;
        try { qvec = await _embeddings.EmbedAsync(query, ct); }
        catch (Exception ex) { _log.LogDebug(ex, "امبدینگ پرس‌وجو ناموفق؛ حالت کلیدواژه‌ای."); }

        var scored = new List<AiDocMatch>();
        if (qvec != null)
        {
            var vecs = await LoadVectorsAsync(cands, lazyBuild: true, ct);
            foreach (var c in cands)
            {
                double score;
                if (vecs.TryGetValue((c.DocType, c.DocId), out var dv) && dv.Length == qvec.Length)
                    score = AiVectorMath.Cosine(qvec, dv);
                else
                    score = KeywordScore(query, c.Title + "\n" + c.Body) * 0.5;
                if (score > MinSemanticScore)
                    scored.Add(ToMatch(c, score));
            }
            return (scored.OrderByDescending(m => m.Score).Take(take).ToList(), true);
        }

        foreach (var c in cands)
        {
            var score = KeywordScore(query, c.Title + "\n" + c.Body);
            if (score > MinKeywordScore)
                scored.Add(ToMatch(c, score));
        }
        return (scored.OrderByDescending(m => m.Score).Take(take).ToList(), false);
    }

    private static AiDocMatch ToMatch(DocCand c, double score) => new()
    {
        DocType = c.DocType,
        DocId = c.DocId,
        Title = c.Title,
        Snippet = AiTextUtil.Truncate(AiTextUtil.StripHtml(c.Body), 140),
        Meta = c.Meta,
        Link = c.Link,
        Score = Math.Round(score, 3),
    };

    private static double KeywordScore(string query, string text)
    {
        var qt = AiTextUtil.Tokens(query);
        if (qt.Count == 0) return 0;
        var dt = AiTextUtil.Tokens(text);
        if (dt.Count == 0) return 0;
        var hit = qt.Count(t => dt.Contains(t));
        return (double)hit / qt.Count;
    }

    // ==================== ایندکس ====================

    /// <summary>ایندکس اسناد فاقد بردار یا تغییرکرده (بدون دیده‌بانی کاربر؛ ابزار ادمین و ورکر).</summary>
    public async Task<(int indexed, int failed, int missing)> IndexMissingAsync(
        string? scope, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 500);
        var types = ResolveScope(scope);
        var all = await LoadAllAsync(types, ct);
        if (all.Count == 0)
            return (0, 0, 0);

        var existing = await _db.AiDocEmbeddings.AsNoTracking()
            .Where(e => types.Contains(e.DocType))
            .Select(e => new { e.DocType, e.DocId, e.TextHash, HasVec = e.VectorJson != null && e.VectorJson != "" })
            .ToListAsync(ct);
        var have = existing.Where(e => e.HasVec).ToDictionary(e => (e.DocType, e.DocId), e => e.TextHash);

        var missing = all.Where(c => !have.TryGetValue((c.DocType, c.DocId), out var h) || h != TextHash(c)).Take(take).ToList();
        var indexed = 0;
        var failed = 0;
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
                _log.LogWarning(ex, "ایندکس {DocType} {DocId} ناموفق.", c.DocType, c.DocId);
                if (failed >= 5) break; // مدل احتمالاً خواب است؛ بقیه را به دور بعد بسپار
            }
        }
        return (indexed, failed, missing.Count);
    }

    private async Task<Dictionary<(string, int), float[]>> LoadVectorsAsync(
        List<DocCand> cands, bool lazyBuild, CancellationToken ct)
    {
        var map = new Dictionary<(string, int), float[]>();
        var types = cands.Select(c => c.DocType).Distinct().ToList();
        var want = cands.Select(c => (c.DocType, c.DocId)).ToHashSet();
        var rows = await _db.AiDocEmbeddings.AsNoTracking()
            .Where(e => types.Contains(e.DocType))
            .ToListAsync(ct);
        var byKey = rows.Where(e => want.Contains((e.DocType, e.DocId)))
            .ToDictionary(e => (e.DocType, e.DocId));

        var built = 0;
        foreach (var c in cands)
        {
            var key = (c.DocType, c.DocId);
            float[]? vec = null;
            if (byKey.TryGetValue(key, out var row) && !string.IsNullOrWhiteSpace(row.VectorJson))
            {
                if (row.TextHash == TextHash(c))
                    vec = TryParseVector(row.VectorJson);
            }
            if (vec == null && lazyBuild && built < LazyBuildPerSearch)
            {
                try
                {
                    var fresh = await _embeddings.EmbedAsync(EmbedText(c), ct);
                    if (fresh != null && fresh.Length > 0)
                    {
                        await UpsertAsync(c, JsonSerializer.Serialize(fresh), ct);
                        vec = fresh;
                        built++;
                    }
                }
                catch { break; }
            }
            if (vec != null)
                map[key] = vec;
        }
        return map;
    }

    private async Task UpsertAsync(DocCand c, string vectorJson, CancellationToken ct)
    {
        var hash = TextHash(c);
        var row = await _db.AiDocEmbeddings
            .FirstOrDefaultAsync(e => e.DocType == c.DocType && e.DocId == c.DocId, ct);
        if (row == null)
        {
            _db.AiDocEmbeddings.Add(new AiDocEmbedding
            {
                DocType = c.DocType,
                DocId = c.DocId,
                TextHash = hash,
                VectorJson = vectorJson,
                UpdatedAt = DateTime.Now,
            });
        }
        else
        {
            row.TextHash = hash;
            row.VectorJson = vectorJson;
            row.UpdatedAt = DateTime.Now;
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            // مسابقه ورکر/جستجو روی کلید یکتا — ضرری ندارد
            _log.LogDebug(ex, "Upsert بردار تکراری {DocType} {DocId}.", c.DocType, c.DocId);
            _db.ChangeTracker.Clear();
        }
    }

    private static string EmbedText(DocCand c)
        => AiTextUtil.Truncate(c.Title + "\n" + c.Body, MaxTextChars);

    private static string TextHash(DocCand c)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(EmbedText(c)));
        return Convert.ToHexString(bytes)[..16];
    }

    private static float[]? TryParseVector(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<float[]>(json); }
        catch { return null; }
    }

    // ==================== بارگذاری اسناد ====================

    /// <summary>همه اسناد قابل‌ایندکس (فیلتر پایه، بدون دیده‌بانی).</summary>
    private async Task<List<DocCand>> LoadAllAsync(List<string> types, CancellationToken ct)
    {
        var list = new List<DocCand>();
        if (types.Contains("inner_letter"))
        {
            var rows = await _db.InnerLetters.AsNoTracking()
                .Where(x => !x.IsDelete && x.Mahramanegi == "عادی")
                .OrderByDescending(x => x.Id).Take(MaxCandidates)
                .Select(x => new { x.Id, x.Title, x.Text, x.LetterNumber, x.DateSabt })
                .ToListAsync(ct);
            list.AddRange(rows.Select(x => new DocCand("inner_letter", x.Id,
                x.Title, x.Text ?? "", Meta(x.LetterNumber, x.DateSabt), $"/letters/view/{x.Id}")));
        }
        if (types.Contains("incoming_letter"))
        {
            var rows = await _db.IncomingLetters.AsNoTracking()
                .Where(x => !x.IsDelete && x.Mahramanegi == 0)
                .OrderByDescending(x => x.Id).Take(MaxCandidates)
                .Select(x => new { x.Id, x.Title, x.Description, x.Ferestande, x.LetterNumber, x.Date })
                .ToListAsync(ct);
            list.AddRange(rows.Select(x => new DocCand("incoming_letter", x.Id,
                x.Title, (x.Description ?? "") + "\nاز: " + x.Ferestande, Meta(x.LetterNumber, x.Date), "/incoming-letters")));
        }
        if (types.Contains("outgoing_letter"))
        {
            var rows = await _db.OutgoingLetters.AsNoTracking()
                .Where(x => !x.IsDelete && x.Mahramanegi == "عادی")
                .OrderByDescending(x => x.Id).Take(MaxCandidates)
                .Select(x => new { x.Id, x.Title, x.Text, x.ReceiverOrganization, x.LetterNumber, x.DateSabt })
                .ToListAsync(ct);
            list.AddRange(rows.Select(x => new DocCand("outgoing_letter", x.Id,
                x.Title, (x.Text ?? "") + "\nگیرنده: " + x.ReceiverOrganization, Meta(x.LetterNumber, x.DateSabt), $"/outgoing-letters/view/{x.Id}")));
        }
        if (types.Contains("ticket"))
        {
            var rows = await _db.FaComTickets.AsNoTracking()
                .OrderByDescending(x => x.Id).Take(MaxCandidates)
                .Select(x => new { x.Id, x.Subject, x.Body, x.CreatedAt })
                .ToListAsync(ct);
            list.AddRange(rows.Select(x => new DocCand("ticket", x.Id,
                x.Subject, x.Body, AiDateUtil.ToFaShort(x.CreatedAt), $"/fa-com/tickets/{x.Id}")));
        }
        return list;
    }

    /// <summary>اسناد قابل‌مشاهده برای کاربر (گیت ماژول + محدوده سطر، آینه کاتالوگ کاوش).</summary>
    private async Task<List<DocCand>> LoadCandidatesAsync(int userId, List<string> types, CancellationToken ct)
    {
        var list = new List<DocCand>();
        List<int>? letterIds = null;
        async Task<List<int>> LetterIdsAsync()
        {
            letterIds ??= await _db.Erjas.AsNoTracking()
                .Where(e => e.ReciverUserId == userId).Select(e => e.SourceId).ToListAsync(ct);
            return letterIds;
        }

        if (types.Contains("inner_letter") &&
            await AiAccessHelper.UserHasAsync(_db, userId, "InnerLetters", "Read", null, ct))
        {
            var all = await AiAccessHelper.UserHasAsync(_db, userId, "InnerLetters", "ViewAll", null, ct);
            var mine = all ? null : await LetterIdsAsync();
            var rows = await _db.InnerLetters.AsNoTracking()
                .Where(x => !x.IsDelete && x.Mahramanegi == "عادی" &&
                    (all || x.CreatorUserId == userId || (mine!.Contains(x.Id))))
                .OrderByDescending(x => x.Id).Take(MaxCandidates)
                .Select(x => new { x.Id, x.Title, x.Text, x.LetterNumber, x.DateSabt })
                .ToListAsync(ct);
            list.AddRange(rows.Select(x => new DocCand("inner_letter", x.Id,
                x.Title, x.Text ?? "", Meta(x.LetterNumber, x.DateSabt), $"/letters/view/{x.Id}")));
        }
        if (types.Contains("incoming_letter") &&
            await AiAccessHelper.UserHasAsync(_db, userId, "IncomingLetters", "Read", null, ct))
        {
            var all = await AiAccessHelper.UserHasAsync(_db, userId, "IncomingLetters", "ViewAll", null, ct);
            var mine = all ? null : await LetterIdsAsync();
            var rows = await _db.IncomingLetters.AsNoTracking()
                .Where(x => !x.IsDelete && x.Mahramanegi == 0 &&
                    (all || x.CreateUserId == userId || (mine!.Contains(x.Id))))
                .OrderByDescending(x => x.Id).Take(MaxCandidates)
                .Select(x => new { x.Id, x.Title, x.Description, x.Ferestande, x.LetterNumber, x.Date })
                .ToListAsync(ct);
            list.AddRange(rows.Select(x => new DocCand("incoming_letter", x.Id,
                x.Title, (x.Description ?? "") + "\nاز: " + x.Ferestande, Meta(x.LetterNumber, x.Date), "/incoming-letters")));
        }
        if (types.Contains("outgoing_letter") &&
            await AiAccessHelper.UserHasAsync(_db, userId, "OutgoingLetters", "Read", null, ct))
        {
            var all = await AiAccessHelper.UserHasAsync(_db, userId, "OutgoingLetters", "ViewAll", null, ct);
            var mine = all ? null : await LetterIdsAsync();
            var rows = await _db.OutgoingLetters.AsNoTracking()
                .Where(x => !x.IsDelete && x.Mahramanegi == "عادی" &&
                    (all || x.CreatorUserId == userId || (mine!.Contains(x.Id))))
                .OrderByDescending(x => x.Id).Take(MaxCandidates)
                .Select(x => new { x.Id, x.Title, x.Text, x.ReceiverOrganization, x.LetterNumber, x.DateSabt })
                .ToListAsync(ct);
            list.AddRange(rows.Select(x => new DocCand("outgoing_letter", x.Id,
                x.Title, (x.Text ?? "") + "\nگیرنده: " + x.ReceiverOrganization, Meta(x.LetterNumber, x.DateSabt), $"/outgoing-letters/view/{x.Id}")));
        }
        if (types.Contains("ticket") &&
            await AiAccessHelper.UserHasAsync(_db, userId, "FaCom", "Read", null, ct))
        {
            var manage = await AiAccessHelper.UserHasAsync(_db, userId, "FaCom", "Manage", null, ct);
            var empId = manage ? 0 : await _db.HrEmployees.AsNoTracking()
                .Where(e => e.SystemUserId == userId).Select(e => e.Id).FirstOrDefaultAsync(ct);
            if (manage || empId != 0)
            {
                var rows = await _db.FaComTickets.AsNoTracking()
                    .Where(x => manage || x.EmployeeId == empId)
                    .OrderByDescending(x => x.Id).Take(MaxCandidates)
                    .Select(x => new { x.Id, x.Subject, x.Body, x.CreatedAt })
                    .ToListAsync(ct);
                list.AddRange(rows.Select(x => new DocCand("ticket", x.Id,
                    x.Subject, x.Body, AiDateUtil.ToFaShort(x.CreatedAt), $"/fa-com/tickets/{x.Id}")));
            }
        }
        return list;
    }

    private static string Meta(string? number, DateTime date)
    {
        var n = (number ?? "").Trim();
        return (n == "" ? "" : n + " | ") + AiDateUtil.ToFaShort(date);
    }
}

/// <summary>ورکر ایندکس دوره‌ای اسناد (هر ۱۰ دقیقه، حداکثر ۳۰ سند در هر دور).</summary>
public class AiSearchWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AiSearchWorker> _log;

    public AiSearchWorker(IServiceProvider services, ILogger<AiSearchWorker> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var opts = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
                if (opts.Enabled && opts.SearchEnabled)
                {
                    var svc = scope.ServiceProvider.GetRequiredService<AiSearchService>();
                    var (indexed, failed, _) = await svc.IndexMissingAsync("all", 30, stoppingToken);
                    if (indexed > 0 || failed > 0)
                        _log.LogInformation("ایندکس جستجو: {Indexed} موفق، {Failed} ناموفق.", indexed, failed);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "خطای ورکر ایندکس جستجو."); }
            try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
