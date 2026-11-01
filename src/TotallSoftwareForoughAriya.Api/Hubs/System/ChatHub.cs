using System.Collections.Concurrent;
using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services.Core;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Hubs;

/// <summary>اتصال چت فقط با JWT معتبر؛ هویت از userId ارسالی مرورگر پذیرفته نمی‌شود.</summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly AppDbContext _db;
    private readonly IEffectivePermissions _permissions;
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> Online = new();
    private static readonly ConcurrentDictionary<int, DateTime> LastSeen = new();

    public ChatHub(AppDbContext db, IEffectivePermissions permissions)
    {
        _db = db;
        _permissions = permissions;
    }
    public static bool IsUserOnline(int userId) => Online.TryGetValue(userId, out var sessions) && !sessions.IsEmpty;
    public static DateTime? GetLastSeen(int userId) => LastSeen.TryGetValue(userId, out var seen) ? seen : null;
    public static List<int> GetOnlineUserIds() => Online.Where(p => !p.Value.IsEmpty).Select(p => p.Key).ToList();
    private int UserId => int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0
        ? id : throw new HubException("نشست ورود معتبر نیست.");

    public override async Task OnConnectedAsync()
    {
        var uid = UserId;
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == uid && u.IsActive))
            throw new HubException("حساب کاربری فعال نیست.");
        if (Context.User is null || !await _permissions.CanSeeModuleAsync(Context.User, "Chat"))
            throw new HubException("شما مجوز استفاده از پیام‌رسان سازمانی را ندارید.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Chat_User_{uid}");
        var sessions = Online.GetOrAdd(uid, _ => new());
        sessions.TryAdd(Context.ConnectionId, 0);
        LastSeen[uid] = DateTime.UtcNow;
        if (sessions.Count == 1)
            await Clients.Others.SendAsync("UserStatusChanged", uid, true, DateTime.UtcNow);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) && Online.TryGetValue(uid, out var sessions))
        {
            sessions.TryRemove(Context.ConnectionId, out _);
            if (sessions.IsEmpty)
            {
                LastSeen[uid] = DateTime.UtcNow;
                await Clients.Others.SendAsync("UserStatusChanged", uid, false, LastSeen[uid]);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    private async Task<Inventory.Api.Entities.Chat.ChatMember> RequireMembershipAsync(int conversationId)
    {
        var uid = UserId;
        var member = await _db.ChatMembers.AsNoTracking().FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == uid);
        if (member == null || !await _db.Users.AsNoTracking().AnyAsync(u => u.Id == uid && u.IsActive))
            throw new HubException("دسترسی به این گفتگو مجاز نیست.");
        return member;
    }

    public async Task JoinConversation(int conversationId)
    {
        await RequireMembershipAsync(conversationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Chat_Room_{conversationId}");
    }

    public Task LeaveConversation(int conversationId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Chat_Room_{conversationId}");
    public Task SendTyping(int conversationId) => BroadcastTypingAsync(conversationId, true);
    public Task StopTyping(int conversationId) => BroadcastTypingAsync(conversationId, false);

    private async Task BroadcastTypingAsync(int conversationId, bool isTyping)
    {
        var member = await RequireMembershipAsync(conversationId);
        var key = $"typing:{conversationId}";
        if (isTyping && Context.Items.TryGetValue(key, out var value) && value is DateTime previous && DateTime.UtcNow - previous < TimeSpan.FromMilliseconds(700))
            return;
        Context.Items[key] = isTyping ? DateTime.UtcNow : DateTime.MinValue;
        var recipients = await _db.ChatMembers.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.UserId != member.UserId).Select(m => m.UserId).ToListAsync();
        var name = isTyping ? (string.IsNullOrWhiteSpace(member.UserDisplayName) ? member.UserName : member.UserDisplayName) : "";
        await Clients.Groups(recipients.Select(id => $"Chat_User_{id}").ToList())
            .SendAsync("UserTyping", conversationId, member.UserId, name);
    }
}

public interface IChatRealtimeNotifier
{
    Task NotifyMessageReceivedAsync(int conversationId, IEnumerable<int> recipientUserIds, ChatMessageDto message);
    Task NotifyMessageUpdatedAsync(int conversationId, IEnumerable<int> recipientUserIds, ChatMessageDto message);
    Task NotifyMessageDeletedAsync(int conversationId, IEnumerable<int> recipientUserIds, int messageId);
    Task NotifyReactionAsync(int conversationId, IEnumerable<int> recipientUserIds, int messageId, Dictionary<string, List<ChatReactionUserDto>> reactions);
    Task NotifyMessagesReadAsync(int conversationId, int readerUserId, int lastReadMessageId, IEnumerable<int> peerUserIds);
    Task NotifyConversationUpdatedAsync(IEnumerable<int> recipientUserIds, ChatConversationDto conversation);
}

public class ChatRealtimeNotifier : IChatRealtimeNotifier
{
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<ChatRealtimeNotifier> _logger;
    public ChatRealtimeNotifier(IHubContext<ChatHub> hub, ILogger<ChatRealtimeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    // فقط کانال خصوصی هر کاربر، نه هم‌زمان اتاق گفتگو + کانال خصوصی (عامل رویداد تکراری).
    private async Task SendAsync(IEnumerable<int> users, string method, Func<int, object?[]> arguments)
    {
        foreach (var uid in users.Distinct())
        {
            try { await _hub.Clients.Group($"Chat_User_{uid}").SendCoreAsync(method, arguments(uid)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Chat notification {Event} failed for user {UserId}; data is already saved.", method, uid); }
        }
    }

    public Task NotifyMessageReceivedAsync(int id, IEnumerable<int> users, ChatMessageDto message) =>
        SendAsync(users, "ReceiveMessage", uid => new object?[] { message.ForUser(uid) });
    public Task NotifyMessageUpdatedAsync(int id, IEnumerable<int> users, ChatMessageDto message) =>
        SendAsync(users, "MessageUpdated", uid => new object?[] { message.ForUser(uid) });
    public Task NotifyMessageDeletedAsync(int id, IEnumerable<int> users, int messageId) =>
        SendAsync(users, "MessageDeleted", _ => new object?[] { id, messageId });
    public Task NotifyReactionAsync(int id, IEnumerable<int> users, int messageId, Dictionary<string, List<ChatReactionUserDto>> reactions) =>
        SendAsync(users, "MessageReactionUpdated", _ => new object?[] { id, messageId, reactions });
    public Task NotifyMessagesReadAsync(int id, int reader, int messageId, IEnumerable<int> users) =>
        SendAsync(users.Append(reader), "MessagesRead", _ => new object?[] { id, reader, messageId });
    // DTO گفتگو شامل وضعیت شخصی درخواست‌کننده است؛ فقط شناسه را برای بازخوانی اختصاصی ارسال می‌کنیم.
    public Task NotifyConversationUpdatedAsync(IEnumerable<int> users, ChatConversationDto conversation) =>
        SendAsync(users, "ConversationChanged", _ => new object?[] { conversation.Id });
}
