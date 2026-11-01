using System.Text.Json;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// جستجو در راهنمای سیستم — اول معنایی (امبدینگ)، اگر مدل نبود کلیدواژه‌ای.
// امبدینگ مقالات تنبل و خودکار ساخته و ذخیره می‌شود.
// =====================================================================
public class AiKnowledgeMatch
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Link { get; set; }
    public double Score { get; set; }
}

public class AiKnowledgeService
{
    private readonly AppDbContext _db;
    private readonly IAiEmbeddingClient _embeddings;

    public AiKnowledgeService(AppDbContext db, IAiEmbeddingClient embeddings)
    {
        _db = db;
        _embeddings = embeddings;
    }

    public async Task<List<AiKnowledgeMatch>> SearchAsync(string query, int topK = 4, CancellationToken ct = default)
    {
        var docs = await _db.AiKnowledgeDocs.AsNoTracking()
            .Where(d => d.IsActive)
            .ToListAsync(ct);
        if (docs.Count == 0) return new();

        // تلاش برای جستجوی معنایی
        float[]? queryVec = null;
        try { queryVec = await _embeddings.EmbedAsync(query, ct); }
        catch { /* نادیده — حالت کلیدواژه‌ای */ }

        var results = new List<AiKnowledgeMatch>();
        if (queryVec != null)
        {
            // امبدینگ‌های گمشده را تنبل بساز (حداکثر ۵ عدد در هر فراخوانی تا کند نشود)
            var missing = docs.Where(d => string.IsNullOrWhiteSpace(d.EmbeddingJson)).Take(5).ToList();
            foreach (var m in missing)
            {
                try
                {
                    var vec = await _embeddings.EmbedAsync(m.Title + "\n" + m.Content, ct);
                    if (vec != null)
                    {
                        var json = JsonSerializer.Serialize(vec);
                        await _db.AiKnowledgeDocs.Where(d => d.Id == m.Id)
                            .ExecuteUpdateAsync(s => s.SetProperty(d => d.EmbeddingJson, json), ct);
                        m.EmbeddingJson = json;
                    }
                }
                catch { break; }
            }

            foreach (var d in docs)
            {
                double score;
                var dv = TryParseVector(d.EmbeddingJson);
                if (dv != null && dv.Length == queryVec.Length)
                    score = AiVectorMath.Cosine(queryVec, dv);
                else
                    score = KeywordScore(query, d) * 0.5; // مقالات بدون امبدینگ هم شانس دارند
                results.Add(ToMatch(d, score));
            }
            return results.OrderByDescending(r => r.Score).Take(topK).Where(r => r.Score > 0.15).ToList();
        }

        // حالت کلیدواژه‌ای (بدون مدل)
        foreach (var d in docs)
            results.Add(ToMatch(d, KeywordScore(query, d)));
        return results.OrderByDescending(r => r.Score).Take(topK).Where(r => r.Score > 0.05).ToList();
    }

    private static AiKnowledgeMatch ToMatch(AiKnowledgeDoc d, double score) => new()
    {
        Title = d.Title,
        Content = d.Content,
        Category = d.Category,
        Link = d.Link,
        Score = Math.Round(score, 3),
    };

    private static float[]? TryParseVector(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<float[]>(json); }
        catch { return null; }
    }

    /// <summary>امتیاز کلیدواژه‌ای: پوشش توکن‌های پرسش در عنوان (وزن ۲) و متن.</summary>
    public static double KeywordScore(string query, AiKnowledgeDoc d)
    {
        var q = AiTextUtil.Tokens(query);
        if (q.Count == 0) return 0;
        var title = AiTextUtil.Tokens(d.Title + " " + d.Category);
        var body = AiTextUtil.Tokens(d.Content);
        double hit = 0;
        foreach (var t in q)
        {
            if (title.Contains(t)) hit += 2;
            else if (body.Contains(t)) hit += 1;
            else
            {
                // تطبیق تقریبی: شروع مشترک حداقل ۴ حرف (برای صرف فعل‌ها و جمع‌ها)
                if (t.Length >= 4 && (title.Any(x => x.StartsWith(t[..4])) || body.Any(x => x.StartsWith(t[..4]))))
                    hit += 0.5;
            }
        }
        return hit / (q.Count * 2);
    }
}
