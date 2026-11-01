using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// بازخورد پاسخ‌ها (§۲۴): امتیاز 👍/👎 کاربر به هر پاسخ دستیار + آمار و
// فهرست بازبینی برای مدیر. رأی تکراریِ یکسان = پس‌گرفتن رأی.
// =====================================================================

public class AiFeedbackService
{
    private readonly AppDbContext _db;

    public static readonly string[] Reasons =
        { "اشتباه بود", "ناقص بود", "نامرتبط بود", "دیر جواب داد" };

    public AiFeedbackService(AppDbContext db) => _db = db;

    public async Task<(bool ok, string? error, bool removed)> SubmitAsync(
        int userId, int messageId, int rating, string? reason, string? comment, CancellationToken ct)
    {
        if (rating != 1 && rating != -1)
            return (false, "امتیاز باید ۱ یا ۱- باشد.", false);
        var msg = await _db.AiMessages.AsNoTracking()
            .Where(m => m.Id == messageId && m.Role == "assistant")
            .Select(m => new { m.Id, m.ConversationId })
            .FirstOrDefaultAsync(ct);
        if (msg == null)
            return (false, "پیام پیدا نشد.", false);
        var owns = await _db.AiConversations.AsNoTracking()
            .AnyAsync(c => c.Id == msg.ConversationId && c.UserId == userId, ct);
        if (!owns)
            return (false, "این پیام مال تو نیست.", false);

        var existing = await _db.AiFeedbacks
            .FirstOrDefaultAsync(f => f.MessageId == messageId && f.UserId == userId, ct);
        if (existing != null && existing.Rating == rating)
        {
            _db.AiFeedbacks.Remove(existing); // رأی یکسانِ دوباره = پس‌گرفتن
            await _db.SaveChangesAsync(ct);
            return (true, null, true);
        }
        var cleanReason = rating == -1 && reason != null && Reasons.Contains(reason.Trim()) ? reason.Trim() : null;
        var cleanComment = string.IsNullOrWhiteSpace(comment) ? null
            : comment.Trim()[..Math.Min(500, comment.Trim().Length)];
        if (existing == null)
        {
            _db.AiFeedbacks.Add(new AiFeedback
            {
                MessageId = messageId,
                ConversationId = msg.ConversationId,
                UserId = userId,
                Rating = rating,
                Reason = cleanReason,
                Comment = cleanComment,
            });
        }
        else
        {
            existing.Rating = rating;
            existing.Reason = cleanReason;
            existing.Comment = cleanComment;
            existing.UpdatedAt = DateTime.Now;
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return (false, "ثبت نشد؛ دوباره تلاش کن.", false);
        }
        return (true, null, false);
    }

    public async Task<AiFeedbackStats> StatsAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 90);
        var from = DateTime.Now.Date.AddDays(-days + 1);
        var rows = await _db.AiFeedbacks.AsNoTracking()
            .Where(f => f.CreatedAt >= from)
            .Select(f => new { f.Rating, f.Reason, f.CreatedAt, f.MessageId })
            .ToListAsync(ct);
        var up = rows.Count(r => r.Rating == 1);
        var down = rows.Count - up;
        var msgIds = rows.Where(r => r.Rating == -1).Select(r => r.MessageId).Distinct().ToList();
        var tools = new Dictionary<string, int>();
        if (msgIds.Count > 0)
        {
            var csv = await _db.AiMessages.AsNoTracking()
                .Where(m => msgIds.Contains(m.Id) && m.ToolsUsed != null)
                .Select(m => m.ToolsUsed!).ToListAsync(ct);
            foreach (var c in csv)
                foreach (var t in c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (t == "" || t == "offline") continue;
                    tools[t] = tools.TryGetValue(t, out var n) ? n + 1 : 1;
                }
        }
        return new AiFeedbackStats
        {
            Total = rows.Count,
            Up = up,
            Down = down,
            SatisfactionPct = rows.Count == 0 ? null : Math.Round(100.0 * up / rows.Count, 1),
            ByReason = rows.Where(r => r.Rating == -1 && r.Reason != null)
                .GroupBy(r => r.Reason!).OrderByDescending(g => g.Count())
                .Select(g => new AiFeedbackCount { Key = g.Key, Count = g.Count() }).ToList(),
            ByDay = rows.GroupBy(r => r.CreatedAt.Date).OrderBy(g => g.Key)
                .Select(g => new AiFeedbackDay
                {
                    Day = g.Key.ToString("yyyy-MM-dd"),
                    Up = g.Count(r => r.Rating == 1),
                    Down = g.Count(r => r.Rating == -1),
                }).ToList(),
            TopToolsDown = tools.OrderByDescending(kv => kv.Value).Take(5)
                .Select(kv => new AiFeedbackCount { Key = kv.Key, Count = kv.Value }).ToList(),
        };
    }

    public async Task<List<AiFeedbackItem>> ListAsync(int? rating, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 50);
        skip = Math.Max(0, skip);
        var q = _db.AiFeedbacks.AsNoTracking().AsQueryable();
        if (rating == 1 || rating == -1)
            q = q.Where(f => f.Rating == rating);
        var rows = await q.OrderByDescending(f => f.Id).Skip(skip).Take(take)
            .Select(f => new { f.Id, f.MessageId, f.UserId, f.Rating, f.Reason, f.Comment, f.CreatedAt })
            .ToListAsync(ct);
        if (rows.Count == 0) return new();
        var msgIds = rows.Select(r => r.MessageId).Distinct().ToList();
        var msgs = await _db.AiMessages.AsNoTracking()
            .Where(m => msgIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Content, m.ToolsUsed, m.UsedFallback })
            .ToDictionaryAsync(m => m.Id, ct);
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName })
            .ToDictionaryAsync(u => u.Id, ct);
        return rows.Select(r =>
        {
            msgs.TryGetValue(r.MessageId, out var m);
            users.TryGetValue(r.UserId, out var u);
            var name = (((u?.FirstName ?? "") + " " + (u?.LastName ?? "")).Trim());
            if (name == "") name = u?.Username ?? ("کاربر " + r.UserId);
            var content = m?.Content ?? "";
            return new AiFeedbackItem
            {
                Id = r.Id,
                MessageId = r.MessageId,
                User = name,
                Rating = r.Rating,
                Reason = r.Reason,
                Comment = r.Comment,
                Tools = (m?.ToolsUsed ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                UsedFallback = m?.UsedFallback ?? false,
                Excerpt = content.Length > 200 ? content[..200] + "…" : content,
                CreatedAt = r.CreatedAt,
            };
        }).ToList();
    }
}

public class AiFeedbackSubmitRequest
{
    public int MessageId { get; set; }
    public int Rating { get; set; }
    public string? Reason { get; set; }
    public string? Comment { get; set; }
}

public class AiFeedbackStats
{
    public int Total { get; set; }
    public int Up { get; set; }
    public int Down { get; set; }
    public double? SatisfactionPct { get; set; }
    public List<AiFeedbackCount> ByReason { get; set; } = new();
    public List<AiFeedbackDay> ByDay { get; set; } = new();
    public List<AiFeedbackCount> TopToolsDown { get; set; } = new();
}

public class AiFeedbackCount
{
    public string Key { get; set; } = "";
    public int Count { get; set; }
}

public class AiFeedbackDay
{
    public string Day { get; set; } = "";
    public int Up { get; set; }
    public int Down { get; set; }
}

public class AiFeedbackItem
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public string User { get; set; } = "";
    public int Rating { get; set; }
    public string? Reason { get; set; }
    public string? Comment { get; set; }
    public List<string> Tools { get; set; } = new();
    public bool UsedFallback { get; set; }
    public string Excerpt { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
