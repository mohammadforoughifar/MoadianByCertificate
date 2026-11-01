using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// پنل حسابرسی (§۲۵): ردپای هر نوبت چت + آمار و فهرست بازبینی برای مدیر.
// اقدام‌های تأییدمحور (AiPendingActions) هم در آمار و «اقدام‌های اخیر» می‌آیند.
// =====================================================================

public class AiAuditService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AiAuditService> _log;

    public AiAuditService(AppDbContext db, ILogger<AiAuditService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>ثبت ردپا؛ هرگز استثنا بیرون نمی‌دهد تا چت را خراب نکند.</summary>
    public async Task LogAsync(int userId, int conversationId, string channel, string userMessage,
        string? toolsUsed, bool success, bool usedFallback, string? error, long durationMs, CancellationToken ct)
    {
        try
        {
            var msg = (userMessage ?? "").Trim();
            if (msg.Length > 500) msg = msg[..500];
            var err = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
            if (err != null && err.Length > 300) err = err[..300];
            _db.AiAuditLogs.Add(new AiAuditLog
            {
                UserId = userId,
                ConversationId = conversationId,
                Channel = channel ?? "web",
                UserMessage = msg,
                ToolsUsed = string.IsNullOrWhiteSpace(toolsUsed) ? null : toolsUsed.Trim(),
                Success = success,
                UsedFallback = usedFallback,
                Error = err,
                DurationMs = durationMs > int.MaxValue ? int.MaxValue : (int)Math.Max(0, durationMs),
                CreatedAt = DateTime.Now,
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "ثبت ردپای حسابرسی ناموفق بود.");
        }
    }

    public async Task<AiAuditStats> StatsAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 90);
        var from = DateTime.Now.Date.AddDays(-days + 1);
        var rows = await _db.AiAuditLogs.AsNoTracking()
            .Where(l => l.CreatedAt >= from)
            .Select(l => new { l.UserId, l.ToolsUsed, l.Success, l.UsedFallback, l.CreatedAt })
            .ToListAsync(ct);

        var tools = new Dictionary<string, int>();
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.ToolsUsed)) continue;
            foreach (var t in r.ToolsUsed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (t == "" || t == "offline") continue;
                tools[t] = tools.TryGetValue(t, out var n) ? n + 1 : 1;
            }
        }
        var topUserIds = rows.GroupBy(r => r.UserId).OrderByDescending(g => g.Count())
            .Take(5).Select(g => new { Id = g.Key, Count = g.Count() }).ToList();
        var names = await DisplayNamesAsync(topUserIds.Select(u => u.Id).ToList(), ct);

        var fromUtc = DateTime.UtcNow.Date.AddDays(-days + 1);
        var acts = await _db.AiPendingActions.AsNoTracking()
            .Where(a => a.CreatedAtUtc >= fromUtc)
            .Select(a => new { a.Id, a.UserId, a.Action, a.Summary, a.Status, a.CreatedAtUtc })
            .ToListAsync(ct);
        var actNames = await DisplayNamesAsync(acts.Select(a => a.UserId).Distinct().ToList(), ct);

        return new AiAuditStats
        {
            Total = rows.Count,
            Failed = rows.Count(r => !r.Success),
            Fallback = rows.Count(r => r.UsedFallback),
            SuccessPct = rows.Count == 0 ? null : Math.Round(100.0 * rows.Count(r => r.Success) / rows.Count, 1),
            ActionsExecuted = acts.Count(a => a.Status == 1),
            ActionsRejected = acts.Count(a => a.Status == 2),
            ActionsPending = acts.Count(a => a.Status == 0),
            TopTools = tools.OrderByDescending(kv => kv.Value).Take(8)
                .Select(kv => new AiAuditCount { Key = kv.Key, Count = kv.Value }).ToList(),
            TopUsers = topUserIds
                .Select(u => new AiAuditCount { Key = names.TryGetValue(u.Id, out var n) ? n : "کاربر " + u.Id, Count = u.Count }).ToList(),
            ByDay = rows.GroupBy(r => r.CreatedAt.Date).OrderBy(g => g.Key)
                .Select(g => new AiAuditDay
                {
                    Day = g.Key.ToString("yyyy-MM-dd"),
                    Total = g.Count(),
                    Failed = g.Count(r => !r.Success),
                }).ToList(),
            RecentActions = acts.OrderByDescending(a => a.Id).Take(10)
                .Select(a => new AiAuditAction
                {
                    Id = a.Id,
                    User = actNames.TryGetValue(a.UserId, out var n) ? n : "کاربر " + a.UserId,
                    Action = a.Action,
                    Summary = a.Summary.Length > 120 ? a.Summary[..120] + "…" : a.Summary,
                    Status = a.Status switch { 1 => "اجرا شده", 2 => "لغو/منقضی", _ => "در انتظار" },
                    CreatedAt = a.CreatedAtUtc.ToLocalTime(),
                }).ToList(),
        };
    }

    public async Task<List<AiAuditTurn>> ListAsync(
        int? userId, bool? success, string? search, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 50);
        skip = Math.Max(0, skip);
        var q = _db.AiAuditLogs.AsNoTracking().AsQueryable();
        if (userId is > 0) q = q.Where(l => l.UserId == userId);
        if (success != null) q = q.Where(l => l.Success == success);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l => l.UserMessage.Contains(s));
        }
        var rows = await q.OrderByDescending(l => l.Id).Skip(skip).Take(take)
            .Select(l => new { l.Id, l.UserId, l.Channel, l.UserMessage, l.ToolsUsed, l.Success, l.UsedFallback, l.Error, l.DurationMs, l.CreatedAt })
            .ToListAsync(ct);
        if (rows.Count == 0) return new();
        var names = await DisplayNamesAsync(rows.Select(r => r.UserId).Distinct().ToList(), ct);
        return rows.Select(r => new AiAuditTurn
        {
            Id = r.Id,
            User = names.TryGetValue(r.UserId, out var n) ? n : "کاربر " + r.UserId,
            Channel = r.Channel,
            Message = r.UserMessage.Length > 200 ? r.UserMessage[..200] + "…" : r.UserMessage,
            Tools = (r.ToolsUsed ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Success = r.Success,
            UsedFallback = r.UsedFallback,
            Error = r.Error,
            DurationMs = r.DurationMs,
            CreatedAt = r.CreatedAt,
        }).ToList();
    }

    private async Task<Dictionary<int, string>> DisplayNamesAsync(List<int> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName })
            .ToListAsync(ct);
        return users.ToDictionary(u => u.Id, u =>
        {
            var name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim();
            return name == "" ? u.Username : name;
        });
    }
}

public class AiAuditStats
{
    public int Total { get; set; }
    public int Failed { get; set; }
    public int Fallback { get; set; }
    public double? SuccessPct { get; set; }
    public int ActionsExecuted { get; set; }
    public int ActionsRejected { get; set; }
    public int ActionsPending { get; set; }
    public List<AiAuditCount> TopTools { get; set; } = new();
    public List<AiAuditCount> TopUsers { get; set; } = new();
    public List<AiAuditDay> ByDay { get; set; } = new();
    public List<AiAuditAction> RecentActions { get; set; } = new();
}

public class AiAuditCount
{
    public string Key { get; set; } = "";
    public int Count { get; set; }
}

public class AiAuditDay
{
    public string Day { get; set; } = "";
    public int Total { get; set; }
    public int Failed { get; set; }
}

public class AiAuditTurn
{
    public int Id { get; set; }
    public string User { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Message { get; set; } = "";
    public List<string> Tools { get; set; } = new();
    public bool Success { get; set; }
    public bool UsedFallback { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AiAuditAction
{
    public int Id { get; set; }
    public string User { get; set; } = "";
    public string Action { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
