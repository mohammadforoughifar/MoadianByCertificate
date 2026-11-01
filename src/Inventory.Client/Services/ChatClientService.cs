using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Inventory.Client.Services;

public interface IChatClientService
{
    Task<List<ChatConversationDto>> GetConversationsAsync(string? search = null, ChatTypeDto? type = null, bool onlyUnread = false);
    Task<ChatConversationDto> GetConversationAsync(int id);
    Task<ChatConversationDto> CreateDirectAsync(int targetUserId);
    Task<ChatConversationDto> CreateGroupAsync(CreateGroupChatRequest req);
    Task<List<ChatMessageDto>> GetMessagesAsync(int conversationId, int? beforeId = null, int pageSize = 50, string? search = null);
    Task<ChatMessageDto> SendMessageAsync(int conversationId, SendChatMessageRequest req);
    Task<ChatMessageDto> ForwardAsync(int messageId, int conversationId);
    Task<ChatMessageDto> EditMessageAsync(int messageId, string newText);
    Task DeleteMessageAsync(int messageId);
    Task<Dictionary<string, List<ChatReactionUserDto>>> ReactAsync(int messageId, string emoji);
    Task<ChatMessageDto> TogglePinMessageAsync(int messageId);
    Task MarkReadAsync(int conversationId, int? lastReadMessageId = null);
    Task TogglePinConversationAsync(int conversationId);
    Task ToggleMuteConversationAsync(int conversationId);
    Task<List<ChatMemberDto>> GetMembersAsync(int conversationId);
    Task AddMembersAsync(int conversationId, List<int> userIds);
    Task RemoveMemberAsync(int conversationId, int userId);
    Task<List<ChatUserDto>> GetSoftwareUsersAsync(string? search = null);
    Task<ChatSummaryDto> GetSummaryAsync();
    Task<ChatUploadResultDto> UploadAttachmentAsync(int conversationId, IBrowserFile file);
    Task<ChatUploadResultDto> UploadVoiceNoteAsync(int conversationId, byte[] data, string fileName, string contentType);
    /// <summary>تبدیل پیام صوتی به متن؛ force=true رونویسی قبلی را دوباره انجام می‌دهد.</summary>
    Task<ChatMessageDto> TranscribeVoiceMessageAsync(int messageId, bool force = false);
    string GetFileUrl(int messageId, bool preview = false);
    Task EnsureFileTokenAsync();
    bool IsConnected { get; }
    Task StopAsync();

    // اتصال SignalR بلادرنگ
    Task EnsureHubConnectedAsync();
    Task JoinConversationHubAsync(int conversationId);
    Task LeaveConversationHubAsync(int conversationId);
    Task SendTypingHubAsync(int conversationId, bool isTyping = true);

    // رویدادهای زنده
    event Action<ChatMessageDto>? OnMessageReceived;
    event Action<ChatMessageDto>? OnMessageUpdated;
    event Action<int, int>? OnMessageDeleted;
    event Action<int, int, Dictionary<string, List<ChatReactionUserDto>>>? OnReactionUpdated;
    event Action<int, int, int>? OnMessagesRead;
    event Action<int, int, string>? OnUserTyping;
    event Action<int, bool, DateTime>? OnUserStatusChanged;
    event Action<int>? OnConversationChanged;
    event Action? OnConnectionChanged;
    event Action? OnSyncRequested;
}

public class ChatClientService : IChatClientService, IAsyncDisposable
{
    private readonly IApiClient _api;
    private readonly IAuthState _auth;
    private readonly ApiOptions _opts;
    private readonly HttpClient _http;
    private HubConnection? _hub;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<int> _rooms = new();
    private readonly HashSet<int> _receivedIds = new();
    private readonly Queue<int> _receivedOrder = new();
    private Task? _maintenance;
    private int _hubUserId;
    private string? _hubToken;
    private bool _connected;
    private bool _disposed;
    private DateTime _lastSync;
    public bool IsConnected => _connected && _hub?.State == HubConnectionState.Connected && _auth.IsLoggedIn && _hubUserId == _auth.UserId;


    public event Action<ChatMessageDto>? OnMessageReceived;
    public event Action<ChatMessageDto>? OnMessageUpdated;
    public event Action<int, int>? OnMessageDeleted;
    public event Action<int, int, Dictionary<string, List<ChatReactionUserDto>>>? OnReactionUpdated;
    public event Action<int, int, int>? OnMessagesRead;
    public event Action<int, int, string>? OnUserTyping;
    public event Action<int, bool, DateTime>? OnUserStatusChanged;
    public event Action<int>? OnConversationChanged;
    public event Action? OnConnectionChanged;
    public event Action? OnSyncRequested;

    public ChatClientService(IApiClient api, IAuthState auth, ApiOptions opts, HttpClient http)
    {
        _api = api;
        _auth = auth;
        _opts = opts;
        _http = http;
        _auth.Changed += AuthChanged;
    }

    public async Task<List<ChatConversationDto>> GetConversationsAsync(string? search = null, ChatTypeDto? type = null, bool onlyUnread = false)
    {
        var url = $"api/chat/conversations?onlyUnread={onlyUnread}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (type.HasValue) url += $"&type={(int)type.Value}";
        return await _api.GetAsync<List<ChatConversationDto>>(url) ?? new();
    }

    public async Task<ChatConversationDto> GetConversationAsync(int id)
    {
        return await _api.GetAsync<ChatConversationDto>($"api/chat/conversations/{id}");
    }

    public async Task<ChatConversationDto> CreateDirectAsync(int targetUserId)
    {
        return await _api.PostAsync<ChatConversationDto>("api/chat/conversations/direct", new CreateDirectChatRequest { TargetUserId = targetUserId });
    }

    public async Task<ChatConversationDto> CreateGroupAsync(CreateGroupChatRequest req)
    {
        return await _api.PostAsync<ChatConversationDto>("api/chat/conversations/group", req);
    }

    public async Task<List<ChatMessageDto>> GetMessagesAsync(int conversationId, int? beforeId = null, int pageSize = 50, string? search = null)
    {
        var url = $"api/chat/conversations/{conversationId}/messages?pageSize={pageSize}";
        if (beforeId.HasValue) url += $"&beforeId={beforeId.Value}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        await EnsureFileTokenAsync();   // قبل از رندر لینک‌های دانلود/پیش‌نمایش
        return await _api.GetAsync<List<ChatMessageDto>>(url) ?? new();
    }

    public async Task<ChatMessageDto> SendMessageAsync(int conversationId, SendChatMessageRequest req)
    {
        return await _api.PostAsync<ChatMessageDto>($"api/chat/conversations/{conversationId}/messages", req);
    }

    public async Task<ChatMessageDto> ForwardAsync(int messageId, int conversationId)
    {
        return await _api.PostAsync<ChatMessageDto>($"api/chat/messages/{messageId}/forward",
            new ForwardChatMessageRequest { ConversationId = conversationId });
    }

    public async Task<ChatMessageDto> EditMessageAsync(int messageId, string newText)
    {
        return await _api.PutAsync<ChatMessageDto>($"api/chat/messages/{messageId}", new EditChatMessageRequest { NewText = newText });
    }

    public async Task DeleteMessageAsync(int messageId)
    {
        await _api.DeleteAsync($"api/chat/messages/{messageId}");
    }

    public async Task<Dictionary<string, List<ChatReactionUserDto>>> ReactAsync(int messageId, string emoji)
    {
        return await _api.PostAsync<Dictionary<string, List<ChatReactionUserDto>>>($"api/chat/messages/{messageId}/react", new ReactChatMessageRequest { Emoji = emoji });
    }

    public async Task<ChatMessageDto> TogglePinMessageAsync(int messageId)
    {
        return await _api.PostAsync<ChatMessageDto>($"api/chat/messages/{messageId}/pin", new { });
    }

    public async Task MarkReadAsync(int conversationId, int? lastReadMessageId = null)
    {
        await _api.PostAsync<object>($"api/chat/conversations/{conversationId}/read",
            new MarkChatReadRequest { LastReadMessageId = lastReadMessageId });
    }

    public async Task TogglePinConversationAsync(int conversationId)
    {
        await _api.PostAsync<object>($"api/chat/conversations/{conversationId}/pin", new { });
    }

    public async Task ToggleMuteConversationAsync(int conversationId)
    {
        await _api.PostAsync<object>($"api/chat/conversations/{conversationId}/mute", new { });
    }

    public async Task<List<ChatMemberDto>> GetMembersAsync(int conversationId)
    {
        return await _api.GetAsync<List<ChatMemberDto>>($"api/chat/conversations/{conversationId}/members") ?? new();
    }

    public async Task AddMembersAsync(int conversationId, List<int> userIds)
    {
        await _api.PostAsync<object>($"api/chat/conversations/{conversationId}/members", userIds);
    }

    public async Task RemoveMemberAsync(int conversationId, int userId)
    {
        await _api.DeleteAsync($"api/chat/conversations/{conversationId}/members/{userId}");
    }

    public async Task<List<ChatUserDto>> GetSoftwareUsersAsync(string? search = null)
    {
        var url = "api/chat/users";
        if (!string.IsNullOrWhiteSpace(search)) url += $"?search={Uri.EscapeDataString(search)}";
        return await _api.GetAsync<List<ChatUserDto>>(url) ?? new();
    }

    public async Task<ChatSummaryDto> GetSummaryAsync()
    {
        return await _api.GetAsync<ChatSummaryDto>("api/chat/summary") ?? new();
    }

    public async Task<ChatUploadResultDto> UploadAttachmentAsync(int conversationId, IBrowserFile file)
    {
        if (file.Size <= 0) throw new ApiException("فایل خالی قابل ارسال نیست.");
        const long maxFileBytes = 50L * 1024L * 1024L;
        if (file.Size > maxFileBytes) throw new ApiException("حداکثر حجم هر فایل ۵۰ مگابایت است.");
        using var stream = file.OpenReadStream(maxFileBytes);
        return await _api.PostFileAsync<ChatUploadResultDto>($"api/chat/conversations/{conversationId}/attachments",
            stream, file.Name, "file", file.ContentType);
    }

    /// <summary>آپلود پیام صوتی ضبط‌شده در مرورگر (بایت خام MediaRecorder).</summary>
    /// <summary>
    /// تبدیل ویس به متن: رونویسی سمت سرور انجام و روی همان پیام ذخیره می‌شود؛
    /// پیام به‌روزشده برمی‌گردد (شامل Transcript) تا فوراً در حباب نمایش داده شود.
    /// </summary>
    public Task<ChatMessageDto> TranscribeVoiceMessageAsync(int messageId, bool force = false) =>
        _api.PostLongAsync<ChatMessageDto>($"api/chat/messages/{messageId}/transcribe" + (force ? "?force=true" : ""), new { });

    public async Task<ChatUploadResultDto> UploadVoiceNoteAsync(int conversationId, byte[] data, string fileName, string contentType)
    {
        if (data == null || data.Length == 0) throw new ApiException("فایل صوتی خالی است.");
        const long maxFileBytes = 50L * 1024L * 1024L;
        if (data.Length > maxFileBytes) throw new ApiException("حداکثر حجم هر فایل ۵۰ مگابایت است.");
        using var stream = new MemoryStream(data, writable: false);
        return await _api.PostFileAsync<ChatUploadResultDto>($"api/chat/conversations/{conversationId}/attachments",
            stream, fileName, "file", contentType);
    }

    // همیشه مبدأ API؛ در استقرار دو سروره هم فایل از سرور Client درخواست نمی‌شود.
    // تگ‌های img/audio/video نمی‌توانند هدر Bearer بفرستند؛ endpoint فقط برای دانلود/preview JWT را از query می‌خواند.
    // ⚠️ JWT کامل این سیستم همهٔ مجوزها را دارد (≈۱۰KB برای مدیر) و در URL باعث «414 URI Too Long»
    // می‌شد؛ نتیجه: هیچ فایلی دانلود نمی‌شد. به‌جای آن توکن کوتاه ≈۷۰ نویسه‌ای فایل (ft) می‌رود.
    // فقط اگر هنوز دریافت نشده بود به روش قدیمی برمی‌گردیم.
    private string? _fileToken;
    private DateTime _fileTokenExpiresUtc = DateTime.MinValue;
    private int _fileTokenUserId;

    private sealed class FileTokenResult
    {
        public string Token { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }

    public async Task EnsureFileTokenAsync()
    {
        if (!_auth.IsLoggedIn) { _fileToken = null; return; }
        if (_fileToken != null && _fileTokenUserId == _auth.UserId &&
            _fileTokenExpiresUtc > DateTime.UtcNow.AddHours(2)) return;
        try
        {
            var r = await _api.GetAsync<FileTokenResult>("api/chat/file-token");
            if (r == null || string.IsNullOrWhiteSpace(r.Token)) return;
            _fileToken = r.Token;
            _fileTokenExpiresUtc = r.ExpiresAt.Kind == DateTimeKind.Utc ? r.ExpiresAt : r.ExpiresAt.ToUniversalTime();
            _fileTokenUserId = _auth.UserId;
        }
        catch { /* دانلود با روش قدیمی ادامه می‌یابد؛ خطای شبکه نباید لود پیام‌ها را خراب کند */ }
    }

    public string GetFileUrl(int messageId, bool preview = false)
    {
        var url = _api.BuildUrl($"api/chat/messages/{messageId}/{(preview ? "preview" : "download")}");
        return _fileToken != null && _fileTokenUserId == _auth.UserId
            ? $"{url}?ft={Uri.EscapeDataString(_fileToken)}"
            : $"{url}?access_token={Uri.EscapeDataString(_auth.Token ?? "")}";
    }

    public async Task EnsureHubConnectedAsync()
    {
        if (_disposed) return;
        _maintenance ??= MaintainConnectionAsync();
        await _connectionGate.WaitAsync(_lifetime.Token);
        try
        {
            if (_disposed) return;
            if (string.IsNullOrWhiteSpace(_auth.Token)) { await DisposeHubAsync(); return; }
            if (_hub != null && (_hubUserId != _auth.UserId || _hubToken != _auth.Token))
            {
                if (_hubUserId != _auth.UserId) { lock (_rooms) _rooms.Clear(); }
                await DisposeHubAsync();
            }
            if (_hub == null) CreateHub();
            var hub = _hub!;
            if (hub.State != HubConnectionState.Disconnected) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await hub.StartAsync(timeout.Token);
                await RestoreRoomsAsync(hub);
                SetConnected(true);
                OnSyncRequested?.Invoke();
            }
            catch (Exception) when (!_disposed) { SetConnected(false); }
        }
        finally { _connectionGate.Release(); }
    }

    private void CreateHub()
    {
        _hubUserId = _auth.UserId;
        _hubToken = _auth.Token;
        var uid = _hubUserId;
        var hub = new HubConnectionBuilder()
            .WithUrl(_api.BuildUrl("hubs/chat"), options => options.AccessTokenProvider = () => Task.FromResult(_auth.Token))
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();
        _hub = hub;
        bool Current() => !_disposed && ReferenceEquals(_hub, hub) && _auth.UserId == uid && _auth.IsLoggedIn;
        void Receive(ChatMessageDto message)
        {
            if (!Current() || !_receivedIds.Add(message.Id)) return;
            _receivedOrder.Enqueue(message.Id);
            if (_receivedOrder.Count > 2048) _receivedIds.Remove(_receivedOrder.Dequeue());
            OnMessageReceived?.Invoke(message.ForUser(uid));
        }
        hub.On<ChatMessageDto>("ReceiveMessage", Receive);
        hub.On<int, ChatMessageDto>("NewMessageNotification", (_, message) => Receive(message)); // نسخهٔ قدیمی، بدون دوباره‌شماری
        hub.On<ChatMessageDto>("MessageUpdated", message => { if (Current()) OnMessageUpdated?.Invoke(message.ForUser(uid)); });
        hub.On<int, int>("MessageDeleted", (cid, mid) => { if (Current()) OnMessageDeleted?.Invoke(cid, mid); });
        hub.On<int, int, Dictionary<string, List<ChatReactionUserDto>>>("MessageReactionUpdated", (cid, mid, reactions) => { if (Current()) OnReactionUpdated?.Invoke(cid, mid, reactions); });
        hub.On<int, int, int>("MessagesRead", (cid, reader, last) => { if (Current()) OnMessagesRead?.Invoke(cid, reader, last); });
        hub.On<int, int, string>("UserTyping", (cid, user, name) => { if (Current()) OnUserTyping?.Invoke(cid, user, name); });
        hub.On<int, bool, DateTime>("UserStatusChanged", (user, online, seen) => { if (Current()) OnUserStatusChanged?.Invoke(user, online, seen); });
        hub.On<int>("ConversationChanged", cid => { if (Current()) OnConversationChanged?.Invoke(cid); });
        hub.On<ChatConversationDto>("ConversationUpdated", c => { if (Current()) OnConversationChanged?.Invoke(c.Id); });
        hub.Reconnecting += _ => { if (Current()) SetConnected(false); return Task.CompletedTask; };
        hub.Closed += _ => { if (Current()) SetConnected(false); return Task.CompletedTask; };
        hub.Reconnected += async _ =>
        {
            if (!Current()) return;
            await RestoreRoomsAsync(hub);
            SetConnected(true);
            OnSyncRequested?.Invoke(); // پیام‌های زمان قطع اتصال را از API بازخوانی کن.
        };
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        OnConnectionChanged?.Invoke();
    }

    private async Task MaintainConnectionAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), _lifetime.Token);
                if (!_auth.IsLoggedIn) continue;
                try { await EnsureHubConnectedAsync(); } catch (OperationCanceledException) { break; }
                if (DateTime.UtcNow - _lastSync >= TimeSpan.FromSeconds(IsConnected ? 30 : 5))
                {
                    _lastSync = DateTime.UtcNow;
                    OnSyncRequested?.Invoke();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RestoreRoomsAsync(HubConnection hub)
    {
        int[] rooms;
        lock (_rooms) rooms = _rooms.ToArray();
        foreach (var id in rooms)
        {
            try { await hub.InvokeAsync("JoinConversation", id); }
            catch (HubException) { lock (_rooms) _rooms.Remove(id); }
            catch (Exception) { break; }
        }
    }

    public async Task JoinConversationHubAsync(int conversationId)
    {
        lock (_rooms) _rooms.Add(conversationId);
        await EnsureHubConnectedAsync();
        if (_hub?.State == HubConnectionState.Connected)
        {
            try { await _hub.InvokeAsync("JoinConversation", conversationId); } catch (Exception) { }
        }
    }

    public async Task LeaveConversationHubAsync(int conversationId)
    {
        lock (_rooms) _rooms.Remove(conversationId);
        if (_hub?.State == HubConnectionState.Connected)
        {
            try { await _hub.InvokeAsync("LeaveConversation", conversationId); } catch (Exception) { }
        }
    }

    public async Task SendTypingHubAsync(int conversationId, bool isTyping = true)
    {
        if (_hub?.State == HubConnectionState.Connected)
        {
            try { await _hub.InvokeAsync(isTyping ? "SendTyping" : "StopTyping", conversationId); } catch (Exception) { }
        }
    }

    private void AuthChanged() => _ = RefreshAuthAsync();
    private async Task RefreshAuthAsync()
    {
        try
        {
            if (_auth.IsLoggedIn) await EnsureHubConnectedAsync();
            else await StopAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async Task DisposeHubAsync()
    {
        var hub = _hub;
        _hub = null;
        SetConnected(false);
        _receivedIds.Clear();
        _receivedOrder.Clear();
        if (hub != null) await hub.DisposeAsync();
    }

    public async Task StopAsync()
    {
        await _connectionGate.WaitAsync();
        try { lock (_rooms) _rooms.Clear(); await DisposeHubAsync(); }
        finally { _connectionGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _auth.Changed -= AuthChanged;
        _lifetime.Cancel();
        await StopAsync();
        if (_maintenance != null) await _maintenance;
        _lifetime.Dispose();
    }
}
