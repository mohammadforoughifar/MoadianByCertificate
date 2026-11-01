using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IIncomingLetterService
{
    Task<PagedResult<IncomingLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null, int page = 1, int pageSize = 15, bool? starredOnly = null,
        bool? attachOnly = null, bool? workOrderOnly = null);
    Task<PagedResult<IncomingLetterListItemDto>> GetSentAsync(string? search = null, int page = 1, int pageSize = 15,
        bool? starredOnly = null, bool? attachOnly = null);
    Task<PagedResult<IncomingLetterListItemDto>> GetArchiveAsync(string? search = null, int page = 1, int pageSize = 15);
    Task<IncomingLetterCartableStatsDto> GetStatsAsync();
    Task<IncomingLetterDetailDto> GetDetailAsync(int letterId);
    Task<int> AddAsync(AddIncomingLetterDto dto);

    /// <summary>
    /// نهایی‌سازی نامه وارده بعد از آپلود موفق «همهٔ» پیوست‌ها — اعلان گیرندگان
    /// اولیه فقط از این مسیر و فقط یک‌بار ارسال می‌شود. خروجی: تعداد اعلان‌های ارسال‌شده.
    /// </summary>
    Task<int> FinalizeAsync(int letterId);
    /// <summary>ویرایش نامهٔ وارده؛ خروجی = تعداد گیرندگانی که نامه را دیده بودند و اعلان ویرایش گرفتند.</summary>
    Task<int> EditAsync(int letterId, EditIncomingLetterDto dto);
    Task DeleteAsync(int letterId);
    Task<bool> ToggleLetterNeshanAsync(int letterId);
    Task<bool> ToggleNeshanAsync(int erjaId);
    Task<bool> ToggleBayeganiAsync(int erjaId);
    Task<bool> ToggleLetterBayeganiAsync(int letterId);
    Task<int> BatchBayeganiAsync(List<int> erjaIds, string? description = null);
    /// <summary>دانلود کلی پیوست‌های نامهٔ وارده به‌صورت ZIP — پیاده‌سازی آن در همین سرویس وجود داشت و اعلان اینترفیس جا افتاده بود.</summary>
    Task<byte[]> DownloadAttachmentsZipAsync(int letterId);
    Task AddErjaAsync(AddErjaDto dto);
    Task AnswerAsync(int erjaId, AnswerErjaDto dto);
    Task MarkReadAsync(int erjaId);
    Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId);
    Task<PagedResult<IncomingLetterPickDto>> PickAsync(string? search = null, int page = 1, int pageSize = 15);
    Task<List<LetterNumberReservationDto>> GetReservationsAsync(int typeForm = 3);
    Task<List<LetterNumberReservationDto>> ReserveNumberAsync(int typeForm = 3, int count = 1);

    /// <summary>کاربران فعال برای انتخاب گیرنده ارجاع (کمبوی مشترک اتوماسیون)</summary>
    Task<List<LetterReciverDto>> GetReciversAsync();

    /// <summary>گروه‌های گیرندگان فعال (انتخاب گروهی در ارجاع)</summary>
    Task<List<LetterGroupDto>> GetGroupsAsync();

    /// <summary>عملگرهای ارجاع</summary>
    Task<List<AmalgarDto>> GetAmalgarsAsync();

    /// <summary>خروج کامل نامهٔ وارده از بایگانی (پاک‌کردن گره درختی و همهٔ پرچم‌ها)</summary>
    Task UnarchiveLetterAsync(int letterId);

    /// <summary>افزودن عملگر ارجاع تازه (دکمهٔ «+» در فرم ارجاع)</summary>
    Task<AmalgarDto> CreateAmalgarAsync(string title);
    Task<List<LetterAttachmentDto>> GetAttachmentsAsync(int letterId);
    Task UploadAttachmentAsync(int letterId, Stream stream, string fileName, string contentType);
    Task DeleteAttachmentAsync(int attachmentId);
    string AttachmentDownloadUrl(int attachmentId);
}

public class IncomingLetterService : IIncomingLetterService
{
    private readonly IApiClient _api;
    private readonly HttpClient _http;
    private readonly IAuthState _auth;

    public IncomingLetterService(IApiClient api, HttpClient http, IAuthState auth)
    {
        _api = api;
        _http = http;
        _auth = auth;
    }

    private class IdResponse { public int Id { get; set; } }
    private class NeshanResponse { public bool IsNeshan { get; set; } }
    private class BayeganiResponse { public bool IsBayegani { get; set; } }
    private class BatchBayeganiRes { public int Count { get; set; } }
    private class FinalizeResponse { public int Notified { get; set; } }

    public Task<PagedResult<IncomingLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null, int page = 1, int pageSize = 15, bool? starredOnly = null,
        bool? attachOnly = null, bool? workOrderOnly = null)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (unreadOnly == true) qs.Add("unreadOnly=true");
        if (starredOnly == true) qs.Add("starredOnly=true");
        if (attachOnly == true) qs.Add("attachOnly=true");
        if (workOrderOnly == true) qs.Add("workOrderOnly=true");
        return ListOrPaged.GetPagedAsync<IncomingLetterListItemDto>(_api, $"api/incoming-letters/inbox?{string.Join("&", qs)}");
    }

    public Task<PagedResult<IncomingLetterListItemDto>> GetSentAsync(string? search = null, int page = 1, int pageSize = 15,
        bool? starredOnly = null, bool? attachOnly = null)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (starredOnly == true) qs.Add("starredOnly=true");
        if (attachOnly == true) qs.Add("attachOnly=true");
        return ListOrPaged.GetPagedAsync<IncomingLetterListItemDto>(_api, $"api/incoming-letters/sent?{string.Join("&", qs)}");
    }

    public Task<PagedResult<IncomingLetterListItemDto>> GetArchiveAsync(string? search = null, int page = 1, int pageSize = 15)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        return ListOrPaged.GetPagedAsync<IncomingLetterListItemDto>(_api, $"api/incoming-letters/archive?{string.Join("&", qs)}");
    }

    public Task<IncomingLetterCartableStatsDto> GetStatsAsync() =>
        _api.GetAsync<IncomingLetterCartableStatsDto>("api/incoming-letters/stats");

    public Task<IncomingLetterDetailDto> GetDetailAsync(int letterId) =>
        _api.GetAsync<IncomingLetterDetailDto>($"api/incoming-letters/{letterId}");

    public async Task<int> AddAsync(AddIncomingLetterDto dto) =>
        (await _api.PostAsync<IdResponse>("api/incoming-letters", dto)).Id;

    public async Task<int> FinalizeAsync(int letterId) =>
        (await _api.PostAsync<FinalizeResponse>($"api/incoming-letters/{letterId}/finalize")).Notified;

    public async Task<int> EditAsync(int letterId, EditIncomingLetterDto dto) =>
        (await _api.PutAsync<FinalizeResponse>($"api/incoming-letters/{letterId}", dto)).Notified;

    public Task DeleteAsync(int letterId) =>
        _api.DeleteAsync($"api/incoming-letters/{letterId}");

    public async Task<bool> ToggleLetterNeshanAsync(int letterId) =>
        (await _api.PostAsync<NeshanResponse>($"api/incoming-letters/{letterId}/neshan")).IsNeshan;

    public async Task<bool> ToggleNeshanAsync(int erjaId) =>
        (await _api.PostAsync<NeshanResponse>($"api/incoming-letters/erja/{erjaId}/neshan")).IsNeshan;

    public async Task<bool> ToggleBayeganiAsync(int erjaId) =>
        (await _api.PostAsync<BayeganiResponse>($"api/incoming-letters/erja/{erjaId}/bayegani")).IsBayegani;

    public async Task<bool> ToggleLetterBayeganiAsync(int letterId) =>
        (await _api.PostAsync<BayeganiResponse>($"api/incoming-letters/{letterId}/bayegani")).IsBayegani;

    public async Task<int> BatchBayeganiAsync(List<int> erjaIds, string? description = null)
    {
        var res = await _api.PostAsync<BatchBayeganiRes>("api/incoming-letters/erja/batch-bayegani", new { erjaIds, description });
        return res?.Count ?? erjaIds.Count;
    }

    public Task AddErjaAsync(AddErjaDto dto) =>
        _api.PostAsync<object>("api/incoming-letters/erja", dto);

    public Task AnswerAsync(int erjaId, AnswerErjaDto dto) =>
        _api.PostAsync<object>($"api/incoming-letters/erja/{erjaId}/answer", dto);

    public Task MarkReadAsync(int erjaId) =>
        _api.PostAsync<object>($"api/incoming-letters/erja/{erjaId}/read");

    public Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId) =>
        ListOrPaged.GetAsync<ErjaTreeNodeDto>(_api, $"api/incoming-letters/{letterId}/gardesh");

    public Task<PagedResult<IncomingLetterPickDto>> PickAsync(string? search = null, int page = 1, int pageSize = 15)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        return ListOrPaged.GetPagedAsync<IncomingLetterPickDto>(_api, $"api/incoming-letters/pick?{string.Join("&", qs)}");
    }

    public Task<List<LetterNumberReservationDto>> GetReservationsAsync(int typeForm = 3) =>
        ListOrPaged.GetAsync<LetterNumberReservationDto>(_api, $"api/incoming-letters/reservations?typeForm={typeForm}");

    public Task<List<LetterNumberReservationDto>> ReserveNumberAsync(int typeForm = 3, int count = 1) =>
        _api.PostAsync<List<LetterNumberReservationDto>>("api/incoming-letters/reserve-number", new { typeForm, count });

    public Task<List<LetterReciverDto>> GetReciversAsync() =>
        ListOrPaged.GetAsync<LetterReciverDto>(_api, "api/incoming-letters/recivers");

    public Task<List<LetterGroupDto>> GetGroupsAsync() =>
        ListOrPaged.GetAsync<LetterGroupDto>(_api, "api/incoming-letters/groups?withMembers=true");

    public Task<List<AmalgarDto>> GetAmalgarsAsync() =>
        ListOrPaged.GetAsync<AmalgarDto>(_api, "api/incoming-letters/amalgars");

    /// <summary>خروج کامل نامهٔ وارده از بایگانی (گره درختی + همهٔ پرچم‌های همین کاربر).</summary>
    public async Task UnarchiveLetterAsync(int letterId) =>
        await _api.PostAsync<object>($"api/incoming-letters/{letterId}/unarchive");

    public Task<AmalgarDto> CreateAmalgarAsync(string title) =>
        _api.PostAsync<AmalgarDto>("api/incoming-letters/amalgars", new CreateAmalgarDto { Title = title });

    public Task<List<LetterAttachmentDto>> GetAttachmentsAsync(int letterId) =>
        ListOrPaged.GetAsync<LetterAttachmentDto>(_api, $"api/incoming-letters/{letterId}/attachments");

    public Task UploadAttachmentAsync(int letterId, Stream stream, string fileName, string contentType) =>
        UploadCoreAsync($"api/incoming-letters/{letterId}/attachments", stream, fileName, contentType);

    public Task DeleteAttachmentAsync(int attachmentId) =>
        _api.DeleteAsync($"api/incoming-letters/attachments/{attachmentId}");

    /// <summary>دانلود کلی همهٔ پیوست‌های نامه وارده به صورت فایل ZIP</summary>
    public async Task<byte[]> DownloadAttachmentsZipAsync(int letterId)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, _api.BuildUrl($"api/incoming-letters/{letterId}/attachments/download-zip"));
        if (!string.IsNullOrWhiteSpace(_auth.Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _auth.Token);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(
            ApiClient.DescribeBody(await resp.Content.ReadAsStringAsync()));
        return await resp.Content.ReadAsByteArrayAsync();
    }

    public string AttachmentDownloadUrl(int attachmentId)
    {
        var url = _api.BuildUrl($"api/incoming-letters/attachments/{attachmentId}/download");
        return string.IsNullOrWhiteSpace(_auth.Token)
            ? url
            : $"{url}?access_token={Uri.EscapeDataString(_auth.Token)}";
    }

    private async Task UploadCoreAsync(string path, Stream stream, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        var sc = new StreamContent(stream);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        content.Add(sc, "file", fileName);

        var req = new HttpRequestMessage(HttpMethod.Post, _api.BuildUrl(path))
        { Content = content };
        if (!string.IsNullOrWhiteSpace(_auth.Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _auth.Token);

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync();
            string msg = "بارگذاری پیوست ناموفق بود.";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m)) msg = m.GetString() ?? msg;
            }
            catch { }
            throw new ApiException(msg);
        }
    }
}
