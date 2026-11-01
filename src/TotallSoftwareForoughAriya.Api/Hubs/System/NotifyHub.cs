using System.Security.Claims;
using Inventory.Api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Hubs;

/// <summary>هاب اعلان‌های بلادرنگ — تفکیک و ایزولاسیون بر اساس شناسه کاربری (u{id}) و نقش (r_{role}).</summary>
[Microsoft.AspNetCore.Authorization.Authorize]
[Microsoft.AspNetCore.Authorization.Authorize]
public class NotifyHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var uid = 0;
        var role = "";

        if (int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id1) && id1 > 0)
        {
            uid = id1;
            role = Context.User?.FindFirstValue(ClaimTypes.Role) ?? "";
        }

        if (uid > 0)
        {
            // ایزولاسیون کامل: کاربر فقط در گروه اختصاصی خودش عضو می‌شود
            await Groups.AddToGroupAsync(Context.ConnectionId, $"u{uid}");

            if (!string.IsNullOrWhiteSpace(role))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"r_{role.Trim().ToLowerInvariant()}");
            }
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>سرویس ارسال اعلان: تفکیک دقیق پیام‌ها بر اساس شناسه، نقش و دسترسی کاربران.</summary>
public interface INotifyService
{
    Task SendAsync(int userId, string title, string? body, string fromName, string formName, string? link);
    Task SendManyAsync(IEnumerable<int> userIds, string title, string? body, string fromName, string formName, string? link);
    Task SendToRoleAsync(string roleName, string title, string? body, string fromName, string formName, string? link);
    Task BroadcastChangedAsync(string scope);
}

public class NotifyService : INotifyService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotifyHub> _hub;
    private readonly Inventory.Api.Services.IMessengerService _messenger;
    private readonly Inventory.Api.Services.IPushService _push;

    public NotifyService(AppDbContext db, IHubContext<NotifyHub> hub, Inventory.Api.Services.IMessengerService messenger, Inventory.Api.Services.IPushService push)
    {
        _db = db;
        _hub = hub;
        _messenger = messenger;
        _push = push;
    }

    public async Task SendAsync(int userId, string title, string? body, string fromName, string formName, string? link)
    {
        if (userId <= 0) return;
        var n = new AppNotification
        {
            UserId = userId,
            Title = title,
            Body = body,
            FromName = fromName,
            FormName = formName,
            Link = link
        };
        _db.AppNotifications.Add(n);
        await Inventory.Api.Services.PushQueue.StageAsync(_db, new[] { userId }, title, body, link);
        await _db.SaveChangesAsync();

        try
        {
            // ارسال بلادرنگ فقط به گروه اختصاصی کاربر مقصد
            await _hub.Clients.Group($"u{userId}").SendAsync("notify", new
            {
                n.Id, n.Title, n.Body, n.FromName, n.FormName, n.Link, n.CreatedAt
            });
        }
        catch { /* عدم اتصال کاربر مانع ذخیره اعلان نمی‌شود */ }

        // ================== ارسال به پیام‌رسان بله و ایتا ==================
        try { await _messenger.SendToUserAsync(userId, title, body); } catch { }

        // ================== نوتیفیکیشن گوشی/تبلت (Web Push) ==================
        // Durable delivery is staged in the same SaveChanges as the notification.
    }

    public async Task SendManyAsync(IEnumerable<int> userIds, string title, string? body, string fromName, string formName, string? link)
    {
        foreach (var id in userIds.Distinct())
        {
            if (id > 0)
                await SendAsync(id, title, body, fromName, formName, link);
        }
    }

    public async Task SendToRoleAsync(string roleName, string title, string? body, string fromName, string formName, string? link)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return;
        var role = roleName.Trim();
        var targetUserIds = await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == role && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();

        if (targetUserIds.Count > 0)
        {
            await SendManyAsync(targetUserIds, title, body, fromName, formName, link);
        }
    }

    public async Task BroadcastChangedAsync(string scope)
    {
        try { await _hub.Clients.All.SendAsync("datachanged", scope); } catch { }
    }
}
