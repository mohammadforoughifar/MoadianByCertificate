using System.Threading.Channels;
using Inventory.Api.Data;
using Inventory.Api.Services.Chat;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// پاسخ‌گویی دستیار داخل پیام‌رسان داخلی (قابلیت ۴ — بخش مسنجر):
// وقتی کاربر در گفتگوی مستقیم با «فروغ آریا» پیام می‌فرستد، درخواست در
// این صف می‌رود و ورکر در پس‌زمینه جواب را تولید و ارسال می‌کند تا
// ارسال پیام کاربر هیچ‌وقت معطل مدل نماند.
// =====================================================================

public record AiReplyRequest(int ConversationId, int UserId, string Text);

public class AiReplyQueue
{
    private readonly Channel<AiReplyRequest> _channel = Channel.CreateUnbounded<AiReplyRequest>();

    public ChannelReader<AiReplyRequest> Reader => _channel.Reader;

    /// <summary>
    /// اگر این پیام در گفتگوی مستقیم با دستیار است، برای پاسخ در صف می‌رود.
    /// باید بعد از ذخیره پیام کاربر صدا زده شود. روی خودِ پیام‌های دستیار هیچ کاری نمی‌کند.
    /// </summary>
    public async Task EnqueueIfAiChatAsync(AppDbContext db, int senderUserId, int conversationId, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var senderUsername = await db.Users.AsNoTracking()
            .Where(u => u.Id == senderUserId).Select(u => u.Username).FirstOrDefaultAsync();
        if (senderUsername == null || senderUsername == AiSeeder.AiUsername) return;

        var memberIds = await db.ChatMembers.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .Select(m => m.UserId)
            .ToListAsync();
        if (memberIds.Count != 2 || !memberIds.Contains(senderUserId)) return;
        var otherId = memberIds.First(id => id != senderUserId);
        var otherUsername = await db.Users.AsNoTracking()
            .Where(u => u.Id == otherId).Select(u => u.Username).FirstOrDefaultAsync();
        if (otherUsername != AiSeeder.AiUsername) return;

        _channel.Writer.TryWrite(new AiReplyRequest(conversationId, senderUserId, text.Trim()));
    }
}

public class AiChatReplyWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AiReplyQueue _queue;
    private readonly ILogger<AiChatReplyWorker> _log;

    public AiChatReplyWorker(IServiceScopeFactory scopes, AiReplyQueue queue, ILogger<AiChatReplyWorker> log)
    {
        _scopes = scopes;
        _queue = queue;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var req in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var sp = scope.ServiceProvider;
                var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
                if (!options.Enabled || !options.MessengerEnabled) continue;

                var db = sp.GetRequiredService<AppDbContext>();
                var aiUser = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Username == AiSeeder.AiUsername, stoppingToken);
                var sender = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == req.UserId, stoppingToken);
                if (aiUser == null || sender == null) continue;

                var senderName = ((sender.FirstName ?? "") + " " + (sender.LastName ?? "")).Trim();
                if (senderName == "") senderName = sender.Username;

                // آیا فرستنده اجازه استفاده از دستیار را دارد؟
                if (!await AiAccessHelper.UserHasAsync(db, req.UserId, "AiAssistant", "Use", sender.Role, stoppingToken))
                    continue;

                var agent = sp.GetRequiredService<IAiAgentService>();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(TimeSpan.FromMinutes(5));
                var result = await agent.ChatAsync(req.UserId, senderName, false, null, req.Text, "messenger", cts.Token);

                var chat = sp.GetRequiredService<IChatService>();
                await chat.SendMessageAsync(aiUser.Id, AiSeeder.AiDisplayName, null, new SendChatMessageRequest
                {
                    ConversationId = req.ConversationId,
                    Text = result.Reply,
                });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "پاسخ دستیار در پیام‌رسان ناموفق بود (گفتگو {Conv}).", req.ConversationId);
            }
        }
    }
}

// =====================================================================
// بررسی دسترسی RBAC بیرون از کنترلر (همان منطق RbacControllerBase)
// برای ورکر پیام‌رسان و پاسخ‌گوی بله.
// =====================================================================
public static class AiAccessHelper
{
    public static async Task<bool> UserHasAsync(AppDbContext db, int userId, string module, string action,
        string? legacyRole, CancellationToken ct = default)
    {
        var hasRoles = await db.UserRoles
            .AnyAsync(ur => ur.UserId == userId && db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive), ct);
        if (!hasRoles)
            return legacyRole == "Admin";
        return await db.UserRoles
            .Where(ur => ur.UserId == userId && db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == module && p.Action == action, ct);
    }
}
