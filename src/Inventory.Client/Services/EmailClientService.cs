using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>فایل پیوست ارسال ایمیل</summary>
public record EmailOutAttachment(string FileName, string ContentType, byte[] Data);

/// <summary>کلاینت ایمیل سازمانی — حساب‌ها، صندوق، ارسال/دریافت، بایگانی</summary>
public interface IEmailClientService
{
    // ---------- حساب‌ها ----------
    Task<List<EmailAccountDto>> GetAccountsAsync();
    Task<List<EmailAccountDto>> GetDabirkhaneAccountsAsync();
    Task<int> SaveAccountAsync(SaveEmailAccountDto dto);
    Task DeleteAccountAsync(int emailId);
    Task<EmailTestResultDto> TestAccountAsync(SaveEmailAccountDto dto);

    // ---------- پیام‌ها ----------
    /// <summary>همگام‌سازیِ یک حساب و یک صندوق (Inbox | Sent | Junk)</summary>
    Task<EmailSyncResultDto> SyncAsync(int emailId, string box = "Inbox");

    /// <summary>همگام‌سازیِ افزایشیِ صندوق دریافتیِ همه‌ی حساب‌ها.
    /// personal=true → صفحهٔ «ایمیل شخصی»: فقط حساب‌های شخصیِ کاربر</summary>
    Task<EmailSyncResultDto> SyncInboxAsync(bool personal = false);

    /// <summary>همگام‌سازیِ افزایشیِ صندوق ارسالیِ همه‌ی حساب‌ها</summary>
    Task<EmailSyncResultDto> SyncSentAsync(bool personal = false);
    /// <summary>همگام‌سازیِ افزایشیِ پوشه‌ی هرزنامه (Junk/Spam) روی سرورِ IMAP</summary>
    Task<EmailSyncResultDto> SyncJunkAsync(bool personal = false);

    /// <summary>صندوقِ دریافتی — صفحه‌بندی کاملاً سمت سرور</summary>
    Task<EmailListPageDto> GetInboxAsync(int? emailId = null, string? search = null, bool? unreadOnly = null,
        int? folderId = null, int page = 1, int pageSize = 25, bool spam = false, bool starred = false, bool personal = false,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null, bool? isRead = null);

    /// <summary>صندوقِ ارسالی — صفحه‌بندی کاملاً سمت سرور</summary>
    Task<EmailListPageDto> GetSentAsync(int? emailId = null, string? search = null, int? folderId = null,
        int page = 1, int pageSize = 25, bool starred = false, bool personal = false,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null, bool? isRead = null);

    /// <summary>بایگانی (دریافتی + ارسالیِ داخل پوشه‌ها) — صفحه‌بندی کاملاً سمت سرور</summary>
    Task<EmailListPageDto> GetArchiveAsync(int? emailId = null, string? search = null,
        int page = 1, int pageSize = 25, bool personal = false,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null, bool? unreadOnly = null,
        bool? isRead = null);

    Task<EmailMessageDetailDto> GetMessageAsync(string box, int id);
    Task<bool> ToggleNeshanAsync(string box, int id);

    /// <summary>
    /// علامت‌زدنِ ایمیل به‌عنوان «خوانده‌شده» روی سرور.
    /// جدا از گرفتنِ جزئیات است تا اگر دریافتِ جزئیات به هر دلیلی شکست خورد،
    /// وضعیتِ خوانده‌شدن باز هم روی سرور ثبت شود و ایمیل بعد از «دریافتِ» دوباره
    /// به‌صورت خوانده‌نشده (بولد) برنگردد.
    /// </summary>
    Task MarkReadAsync(string box, int id);

    /// <summary>نشان‌کردنِ ایمیل به‌عنوان هرزنامه / یا بازگرداندن به صندوقِ دریافتی</summary>
    Task<bool> MarkSpamAsync(string box, int id, bool spam);
    Task ArchiveAsync(string box, int id, int folderId);
    Task SendAsync(int emailAccountId, string to, string? cc, string subject, string body, List<EmailOutAttachment> attachments);
    string AttachmentDownloadUrl(int attachmentId);
    Task<(byte[] Data, string FileName)> DownloadAttachmentAsync(int attachmentId);

    // ---------- پوشه‌ها ----------
    Task<List<EmailFolderDto>> GetFoldersAsync();
    Task<int> SaveFolderAsync(SaveEmailFolderDto dto);
    Task DeleteFolderAsync(int folderId);
}

public class EmailClientService : IEmailClientService
{
    private readonly IApiClient _api;
    private readonly HttpClient _http;
    private readonly ApiOptions _opts;
    private readonly IAuthState _auth;

    public EmailClientService(IApiClient api, HttpClient http, ApiOptions opts, IAuthState auth)
    {
        _api = api;
        _http = http;
        _opts = opts;
        _auth = auth;
    }

    /// <summary>ارسال ایمیل با پیوست — multipart/form-data</summary>
    public async Task SendAsync(int emailAccountId, string to, string? cc, string subject, string body, List<EmailOutAttachment> attachments)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(emailAccountId.ToString()), "emailAccountId");
        content.Add(new StringContent(to ?? ""), "to");
        content.Add(new StringContent(cc ?? ""), "cc");
        content.Add(new StringContent(subject ?? ""), "subject");
        content.Add(new StringContent(body ?? ""), "body");
        foreach (var a in attachments)
        {
            var bc = new ByteArrayContent(a.Data);
            bc.Headers.ContentType = MediaTypeHeaderValue.TryParse(a.ContentType, out var mime)
                ? mime : new MediaTypeHeaderValue("application/octet-stream");
            content.Add(bc, "files", a.FileName);
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl.TrimEnd('/')}/api/email/send") { Content = content };
        if (!string.IsNullOrEmpty(_auth.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);

        using var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            var msg = "ارسال ایمیل ناموفق بود.";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String)
                    msg = m.GetString() ?? msg;
            }
            catch { }
            throw new ApiException(msg);
        }
    }

    private class IdResponse { public int Id { get; set; } }
    private class MsgResponse { public string? Message { get; set; } }
    private class NeshanResponse { public bool IsNeshan { get; set; } }
    private class SpamResponse { public bool IsSpam { get; set; } }

    // ==================== حساب‌ها ====================

    public Task<List<EmailAccountDto>> GetAccountsAsync() =>
        _api.GetAsync<List<EmailAccountDto>>("api/email/accounts");

    public Task<List<EmailAccountDto>> GetDabirkhaneAccountsAsync() =>
        _api.GetAsync<List<EmailAccountDto>>("api/email/dabirkhane-accounts");

    public async Task<int> SaveAccountAsync(SaveEmailAccountDto dto) =>
        (await _api.PostAsync<IdResponse>("api/email/accounts", dto)).Id;

    public Task DeleteAccountAsync(int emailId) =>
        _api.DeleteAsync($"api/email/accounts/{emailId}");

    public Task<EmailTestResultDto> TestAccountAsync(SaveEmailAccountDto dto) =>
        _api.PostAsync<EmailTestResultDto>("api/email/test", dto);

    // ==================== پیام‌ها ====================

    public Task<EmailSyncResultDto> SyncAsync(int emailId, string box = "Inbox") =>
        _api.PostLongAsync<EmailSyncResultDto>(
            $"api/email/accounts/{emailId}/sync?box={Uri.EscapeDataString(string.IsNullOrWhiteSpace(box) ? "Inbox" : box)}");

    public Task<EmailSyncResultDto> SyncInboxAsync(bool personal = false) =>
        _api.PostLongAsync<EmailSyncResultDto>($"api/email/sync-inbox{(personal ? "?personal=true" : "")}");

    public Task<EmailSyncResultDto> SyncSentAsync(bool personal = false) =>
        _api.PostLongAsync<EmailSyncResultDto>($"api/email/sync-sent{(personal ? "?personal=true" : "")}");

    public Task<EmailSyncResultDto> SyncJunkAsync(bool personal = false) =>
        _api.PostLongAsync<EmailSyncResultDto>($"api/email/sync-junk{(personal ? "?personal=true" : "")}");

    /// <summary>تاریخ همیشه با قالبِ ثابتِ میلادی (yyyy-MM-dd) و بدونِ وابستگی به کالچر فرستاده می‌شود</summary>
    private static string DateArg(DateTime d) =>
        d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public Task<EmailListPageDto> GetInboxAsync(int? emailId = null, string? search = null, bool? unreadOnly = null,
        int? folderId = null, int page = 1, int pageSize = 25, bool spam = false, bool starred = false, bool personal = false,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null, bool? isRead = null)
    {
        var q = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (emailId is > 0) q.Add($"emailId={emailId}");
        if (!string.IsNullOrWhiteSpace(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        // هر دو حالتِ خوانده/خوانده‌نشده فرستاده می‌شود (قبلاً «خوانده‌شده» هیچ پارامتری نداشت و بی‌اثر بود)
        if (unreadOnly.HasValue) q.Add($"unreadOnly={(unreadOnly.Value ? "true" : "false")}");
        if (isRead.HasValue) q.Add($"isRead={(isRead.Value ? "true" : "false")}");
        if (folderId != null) q.Add($"folderId={folderId.Value}"); // 0=بایگانی‌نشده، -1=بایگانی‌شده، n=پوشه n
        if (spam) q.Add("spam=true");
        if (starred) q.Add("starred=true");
        if (personal) q.Add("personal=true");
        if (dateFrom.HasValue) q.Add($"dateFrom={DateArg(dateFrom.Value)}");
        if (dateTo.HasValue) q.Add($"dateTo={DateArg(dateTo.Value)}");
        if (hasAttachment.HasValue) q.Add($"hasAttachment={(hasAttachment.Value ? "true" : "false")}");
        return _api.GetAsync<EmailListPageDto>($"api/email/inbox?{string.Join("&", q)}");
    }

    public Task<EmailListPageDto> GetSentAsync(int? emailId = null, string? search = null, int? folderId = null,
        int page = 1, int pageSize = 25, bool starred = false, bool personal = false,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null, bool? isRead = null)
    {
        var q = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (emailId is > 0) q.Add($"emailId={emailId}");
        if (!string.IsNullOrWhiteSpace(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        if (folderId != null) q.Add($"folderId={folderId.Value}"); // 0=بایگانی‌نشده، -1=بایگانی‌شده، n=پوشه n
        if (starred) q.Add("starred=true");
        if (personal) q.Add("personal=true");
        if (isRead.HasValue) q.Add($"isRead={(isRead.Value ? "true" : "false")}");
        if (dateFrom.HasValue) q.Add($"dateFrom={DateArg(dateFrom.Value)}");
        if (dateTo.HasValue) q.Add($"dateTo={DateArg(dateTo.Value)}");
        if (hasAttachment.HasValue) q.Add($"hasAttachment={(hasAttachment.Value ? "true" : "false")}");
        return _api.GetAsync<EmailListPageDto>($"api/email/sent?{string.Join("&", q)}");
    }

    public Task<EmailListPageDto> GetArchiveAsync(int? emailId = null, string? search = null,
        int page = 1, int pageSize = 25, bool personal = false,
        DateTime? dateFrom = null, DateTime? dateTo = null, bool? hasAttachment = null, bool? unreadOnly = null,
        bool? isRead = null)
    {
        var q = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (emailId is > 0) q.Add($"emailId={emailId}");
        if (!string.IsNullOrWhiteSpace(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        if (personal) q.Add("personal=true");
        if (dateFrom.HasValue) q.Add($"dateFrom={DateArg(dateFrom.Value)}");
        if (dateTo.HasValue) q.Add($"dateTo={DateArg(dateTo.Value)}");
        if (hasAttachment.HasValue) q.Add($"hasAttachment={(hasAttachment.Value ? "true" : "false")}");
        if (unreadOnly.HasValue) q.Add($"unreadOnly={(unreadOnly.Value ? "true" : "false")}");
        if (isRead.HasValue) q.Add($"isRead={(isRead.Value ? "true" : "false")}");
        return _api.GetAsync<EmailListPageDto>($"api/email/archive?{string.Join("&", q)}");
    }

    public Task<EmailMessageDetailDto> GetMessageAsync(string box, int id) =>
        _api.GetAsync<EmailMessageDetailDto>($"api/email/message/{box}/{id}");

    public async Task MarkReadAsync(string box, int id)
    {
        try { await _api.PostAsync<object>($"api/email/message/{box}/{id}/read"); }
        catch { /* وضعیتِ خوانده‌شدن در سمتِ کلاینت نگه داشته می‌شود */ }
    }

    public async Task<bool> ToggleNeshanAsync(string box, int id) =>
        (await _api.PostAsync<NeshanResponse>($"api/email/message/{box}/{id}/neshan")).IsNeshan;

    public async Task<bool> MarkSpamAsync(string box, int id, bool spam) =>
        (await _api.PostAsync<SpamResponse>($"api/email/message/{box}/{id}/spam",
            new EmailSpamDto { Spam = spam })).IsSpam;

    public Task ArchiveAsync(string box, int id, int folderId) =>
        _api.PostAsync<MsgResponse>("api/email/archive", new EmailArchiveDto { Box = box, Id = id, FolderId = folderId });

    public string AttachmentDownloadUrl(int attachmentId)
    {
        var url = _api.BuildUrl($"api/email/attachments/{attachmentId}/download");
        return string.IsNullOrWhiteSpace(_auth.Token)
            ? url
            : $"{url}?access_token={Uri.EscapeDataString(_auth.Token)}";
    }

    public async Task<(byte[] Data, string FileName)> DownloadAttachmentAsync(int attachmentId)
    {
        var (data, name, _) = await _api.GetFileAsync($"api/email/attachments/{attachmentId}/download");
        return (data, string.IsNullOrWhiteSpace(name) ? "attachment" : name);
    }

    // ==================== پوشه‌ها ====================

    public Task<List<EmailFolderDto>> GetFoldersAsync() =>
        _api.GetAsync<List<EmailFolderDto>>("api/email/folders");

    public async Task<int> SaveFolderAsync(SaveEmailFolderDto dto) =>
        (await _api.PostAsync<IdResponse>("api/email/folders", dto)).Id;

    public Task DeleteFolderAsync(int folderId) =>
        _api.DeleteAsync($"api/email/folders/{folderId}");
}
