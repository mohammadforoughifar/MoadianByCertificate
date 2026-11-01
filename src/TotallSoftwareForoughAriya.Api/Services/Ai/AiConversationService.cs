using Inventory.Api.Services;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// مدیریت گفتگوها و پیام‌های دستیار (وب / بله / پیام‌رسان داخلی)
// =====================================================================
public class AiConversationService
{
    private readonly AppDbContext _db;

    public AiConversationService(AppDbContext db) => _db = db;

    /// <summary>گفتگوی موجود کاربر یا ساخت گفتگوی تازه (با اعتبارسنجی مالکیت).</summary>
    public async Task<AiConversation> GetOrCreateAsync(int userId, string channel, int? conversationId)
    {
        channel = NormalizeChannel(channel);
        if (conversationId is > 0)
        {
            var existing = await _db.AiConversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);
            if (existing != null) return existing;
        }
        // برای بله/پیام‌رسان، یک گفتگوی ماندگار برای هر کاربر نگه می‌داریم تا حافظه حفظ شود.
        if (channel != "web")
        {
            var sticky = await _db.AiConversations
                .Where(c => c.UserId == userId && c.Channel == channel && !c.IsArchived)
                .OrderByDescending(c => c.LastMessageAtUtc)
                .FirstOrDefaultAsync();
            if (sticky != null) return sticky;
        }
        var conv = new AiConversation { UserId = userId, Channel = channel };
        _db.AiConversations.Add(conv);
        await _db.SaveChangesAsync();
        return conv;
    }

    public async Task<int> AddMessageAsync(int conversationId, string role, string content, string? toolsUsed = null, bool usedFallback = false)
    {
        var msg = new AiMessage
        {
            ConversationId = conversationId,
            Role = role,
            Content = content ?? "",
            ToolsUsed = toolsUsed,
            UsedFallback = usedFallback,
        };
        _db.AiMessages.Add(msg);
        await _db.AiConversations.Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastMessageAtUtc, DateTime.UtcNow));
        await _db.SaveChangesAsync();
        return msg.Id;
    }

    /// <summary>عنوان خودکار گفتگو از روی اولین پیام کاربر.</summary>
    public async Task AutoTitleAsync(int conversationId, string firstMessage)
    {
        var title = AiTextUtil.NormalizeFa(firstMessage);
        if (title.Length > 60) title = title[..60] + "…";
        if (string.IsNullOrWhiteSpace(title)) return;
        await _db.AiConversations.Where(c => c.Id == conversationId && c.Title == "گفتگوی جدید")
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Title, title));
    }

    public async Task<List<AiConversationDto>> ListAsync(int userId, string channel = "web", Paging.Request? pagination = null)
    {
        channel = NormalizeChannel(channel);
        return await _db.AiConversations.AsNoTracking()
            .Where(c => c.UserId == userId && c.Channel == channel && !c.IsArchived)
            .OrderByDescending(c => c.LastMessageAtUtc).ThenBy(x => x.Id)
            .Select(c => new AiConversationDto
            {
                Id = c.Id,
                Title = c.Title,
                Channel = c.Channel,
                LastMessageAtUtc = c.LastMessageAtUtc,
            })
            .ToPageListAsync(pagination, defaultCap: 30);
    }

    public async Task<AiConversationDto?> GetAsync(int userId, int id)
    {
        var conv = await _db.AiConversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (conv == null) return null;
        var ratings = await _db.AiFeedbacks.AsNoTracking()
            .Where(f => f.ConversationId == id && f.UserId == userId)
            .ToDictionaryAsync(f => f.MessageId, f => f.Rating);
        var messages = await _db.AiMessages.AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.Id)
            .Take(200)
            .ToListAsync();
        var dtos = messages.Select(m => new AiMessageDto
        {
            Id = m.Id,
            Role = m.Role,
            Content = m.Content,
            UsedFallback = m.UsedFallback,
            CreatedAtUtc = m.CreatedAtUtc,
            MyRating = ratings.TryGetValue(m.Id, out var r) ? r : null,
        }).ToList();
        return new AiConversationDto
        {
            Id = conv.Id,
            Title = conv.Title,
            Channel = conv.Channel,
            LastMessageAtUtc = conv.LastMessageAtUtc,
            Messages = dtos,
        };
    }

    public async Task DeleteAsync(int userId, int id)
    {
        var conv = await _db.AiConversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (conv == null) return;
        conv.IsArchived = true;
        await _db.SaveChangesAsync();
    }

    /// <summary>تعداد پیام‌های امروز کاربر (برای سقف مصرف).</summary>
    public async Task<int> CountTodayAsync(int userId)
    {
        var todayUtc = DateTime.UtcNow.Date;
        return await _db.AiMessages.AsNoTracking()
            .Where(m => m.Role == "user" && m.CreatedAtUtc >= todayUtc
                && _db.AiConversations.Any(c => c.Id == m.ConversationId && c.UserId == userId))
            .CountAsync();
    }

    private static string NormalizeChannel(string? channel) =>
        channel is "bale" or "messenger" ? channel : "web";
}
