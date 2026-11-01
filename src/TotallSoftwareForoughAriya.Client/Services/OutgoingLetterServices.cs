using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IOutgoingLetterService
{
    /// <summary>جایگزینی کامل فهرست رونوشت‌گیرندگان نامه صادره</summary>
    Task<List<OutgoingLetterCopyToDto>> ReplaceCopyTosAsync(int letterId, List<SaveOutgoingLetterCopyToDto> items);
    Task<PagedResult<OutgoingLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null, int page = 1, int pageSize = 15,
        bool? starredOnly = null, bool? attachOnly = null);
    Task<PagedResult<OutgoingLetterListItemDto>> GetSentAsync(string? search = null, int page = 1, int pageSize = 15,
        bool? starredOnly = null, bool? attachOnly = null);
    Task<PagedResult<OutgoingLetterListItemDto>> GetArchiveAsync(string? search = null, int page = 1, int pageSize = 15);
    Task<OutgoingLetterCartableStatsDto> GetStatsAsync();
    Task<OutgoingLetterDetailDto> GetDetailAsync(int letterId);
    Task<int> SendAsync(AddOutgoingLetterDto dto);
    Task DeleteAsync(int letterId);
    Task<List<OutgoingLetterPickDto>> PickAsync(string? search = null);
    Task UpdateStatusAsync(int letterId, int status);
    Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId);
    Task AddErjaAsync(AddErjaDto dto);
    Task AnswerAsync(int erjaId, AnswerErjaDto dto);
    Task MarkReadAsync(int erjaId);

    /// <summary>«خوانده شد» برای کل نامه: ارجاع‌های من + ردیف امضای من (بدون نیاز به امضا).</summary>
    Task MarkLetterReadAsync(int letterId);
    Task<bool> ToggleNeshanAsync(int erjaId);

    /// <summary>نشان‌کردن (ستاره) نامه صادره ارسالی توسط فرستنده — روی خود نامه</summary>
    Task<bool> ToggleLetterNeshanAsync(int letterId);
    Task<bool> ToggleBayeganiAsync(int erjaId);
    Task<int> BatchBayeganiAsync(List<int> erjaIds, string? description = null);
    Task<List<AmalgarDto>> GetAmalgarsAsync();

    /// <summary>افزودن عملگر ارجاع تازه (دکمهٔ «+» در فرم ارجاع)</summary>
    Task<AmalgarDto> CreateAmalgarAsync(string title);
    Task<PagedResult<OutgoingPishnevisDto>> GetPishnevisPageAsync(int skip, int take, string? search = null);
    Task<List<OutgoingPishnevisDto>> GetPishnevisListAsync(string? search = null);
    Task<OutgoingPishnevisDto> GetPishnevisAsync(int id);
    Task<int> SavePishnevisAsync(OutgoingPishnevisDto dto);
    Task DeletePishnevisAsync(int id);
    Task<List<LetterReciverDto>> GetReciversAsync();
    Task EditAsync(int letterId, EditOutgoingLetterDto dto);
    Task<List<LetterGroupDto>> GetGroupsAsync();
    Task<PagedResult<OutgoingLetterListItemDto>> GetSigningInboxAsync(string? search = null, bool? unsignedOnly = null, int page = 1, int pageSize = 15);
    Task<List<OutgoingSignerDto>> GetSignersAsync(int letterId);
    Task SignAsync(int letterId, string? note = null);
    Task<List<LetterReciverDto>> GetAvailableSignersAsync(string? search = null);
    Task<List<LetterAttachmentDto>> GetAttachmentsAsync(int letterId);
    Task UploadAttachmentAsync(int letterId, Stream stream, string fileName, string contentType);
    Task<List<LetterAttachmentDto>> GetPishnevisAttachmentsAsync(int pishnevisId);
    Task UploadPishnevisAttachmentAsync(int pishnevisId, Stream stream, string fileName, string contentType);
    Task DeleteAttachmentAsync(int attachmentId);
    string AttachmentDownloadUrl(int attachmentId);

    // ==================== دبیرخانه نامه صادره ====================
    Task<List<DabirkhaneListItemDto>> GetDabirkhaneAsync(DabirkhaneSearchDto? filter = null);

    /// <summary>فهرست دبیرخانهٔ نامهٔ صادره با صفحه‌بندی سمت سرور (skip/take).</summary>
    Task<PagedResult<DabirkhaneListItemDto>> GetDabirkhanePageAsync(DabirkhaneSearchDto? filter = null, int page = 1, int pageSize = 15);
    Task<DabirkhaneStatsDto> GetDabirkhaneStatsAsync();
    Task DabirkhaneRegisterAsync(int letterId, DabirkhaneRegisterDto dto);
    Task<List<LetterCompanyDto>> GetCompaniesAsync();

    /// <summary>شرکت فعال کاربر جاری — برای انتخاب سربرگ نامه صادره</summary>
    Task<LetterCompanyDto?> GetActiveCompanyAsync();

    /// <summary>
    /// تشخیص چاپ: کدام شرکت/طرح برای این نامه انتخاب می‌شود و اگر طرح شرکت خوانده
    /// نمی‌شود چرا. فقط خواندن؛ برای پشتیبانی طراحی شده است.
    /// </summary>
    Task<OutgoingLetterPrintDiagnosticsDto?> GetPrintDiagnosticsAsync(int letterId, string size = "A4");

    Task<List<LetterReciverDto>> GetDabirkhaneCreatorsAsync();

    // ==================== بایگانی دبیرخانه ====================
    Task<List<BayeganiNodeDto>> GetDabirkhaneBayeganiTreeAsync();
    Task<BayeganiNodeDto> AddDabirkhaneMainCategoryAsync(SaveBayeganiFolderDto dto);
    Task<BayeganiNodeDto> AddDabirkhaneSubCategoryAsync(SaveBayeganiFolderDto dto);
    Task<BayeganiNodeDto> EditDabirkhaneFolderAsync(int id, SaveBayeganiFolderDto dto);
    Task<BayeganiNodeDto> MoveDabirkhaneFolderAsync(int id, int newParentId);
    Task DeleteDabirkhaneBayeganiAsync(int id);
    Task ArchiveDabirkhaneLettersAsync(ArchiveOutgoingLettersDto dto);
    Task UnarchiveDabirkhaneLetterAsync(int letterId);
    Task<BayeganiNodeDto> MoveDabirkhaneLetterAsync(MoveArchivedLetterDto dto);

    /// <summary>
    /// دریافت PDF چاپ نامه روی سربرگ — size: A4 یا A5
    /// withCopy: true = نسخهٔ «با رونوشت»، false = نسخهٔ «بدون رونوشت»
    /// </summary>
    Task<byte[]> GetPrintPdfAsync(int letterId, string size, bool withCopy = true, bool withMargin = true, bool withLetterhead = true, CancellationToken cancellationToken = default);
    Task<string?> GetUserSignatureDataUrlAsync(int userId);
    /// <summary>دانلود کلی پیوست‌های نامهٔ صادره به‌صورت ZIP — پیاده‌سازی آن در همین سرویس وجود داشت و اعلان اینترفیس جا افتاده بود.</summary>
    Task<byte[]> DownloadAttachmentsZipAsync(int letterId);
}

public class OutgoingLetterService : IOutgoingLetterService
{
    private readonly IApiClient _api;
    private readonly HttpClient _http;
    private readonly IAuthState _auth;

    public OutgoingLetterService(IApiClient api, HttpClient http, IAuthState auth)
    {
        _api = api;
        _http = http;
        _auth = auth;
    }

    private class IdResponse { public int Id { get; set; } }
    private class NeshanResponse { public bool IsNeshan { get; set; } }
    private class BayeganiResponse { public bool IsBayegani { get; set; } }

    /// <summary>جایگزینی کامل فهرست رونوشت‌گیرندگان نامه صادره</summary>
    public Task<List<OutgoingLetterCopyToDto>> ReplaceCopyTosAsync(int letterId, List<SaveOutgoingLetterCopyToDto> items) =>
        _api.PutAsync<List<OutgoingLetterCopyToDto>>(
            $"api/outgoing-letters/{letterId}/copy-tos",
            new ReplaceOutgoingLetterCopyTosDto { Items = items ?? new List<SaveOutgoingLetterCopyToDto>() });

    public Task<PagedResult<OutgoingLetterListItemDto>> GetInboxAsync(string? search = null, bool? unreadOnly = null, int page = 1, int pageSize = 15,
        bool? starredOnly = null, bool? attachOnly = null)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (unreadOnly == true) qs.Add("unreadOnly=true");
        if (starredOnly == true) qs.Add("starredOnly=true");
        if (attachOnly == true) qs.Add("attachOnly=true");
        return ListOrPaged.GetPagedAsync<OutgoingLetterListItemDto>(_api, $"api/outgoing-letters/inbox?{string.Join("&", qs)}");
    }

    public Task<PagedResult<OutgoingLetterListItemDto>> GetSentAsync(string? search = null, int page = 1, int pageSize = 15,
        bool? starredOnly = null, bool? attachOnly = null)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (starredOnly == true) qs.Add("starredOnly=true");
        if (attachOnly == true) qs.Add("attachOnly=true");
        return ListOrPaged.GetPagedAsync<OutgoingLetterListItemDto>(_api, $"api/outgoing-letters/sent?{string.Join("&", qs)}");
    }

    public Task<PagedResult<OutgoingLetterListItemDto>> GetArchiveAsync(string? search = null, int page = 1, int pageSize = 15)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        return ListOrPaged.GetPagedAsync<OutgoingLetterListItemDto>(_api, $"api/outgoing-letters/archive?{string.Join("&", qs)}");
    }

    public Task<OutgoingLetterCartableStatsDto> GetStatsAsync() =>
        _api.GetAsync<OutgoingLetterCartableStatsDto>("api/outgoing-letters/stats");

    public Task<OutgoingLetterDetailDto> GetDetailAsync(int letterId) =>
        _api.GetAsync<OutgoingLetterDetailDto>($"api/outgoing-letters/{letterId}");

    public async Task<int> SendAsync(AddOutgoingLetterDto dto) =>
        (await _api.PostAsync<IdResponse>("api/outgoing-letters", dto)).Id;

    public Task DeleteAsync(int letterId) => _api.DeleteAsync($"api/outgoing-letters/{letterId}");

    public Task UpdateStatusAsync(int letterId, int status) =>
        _api.PostAsync<object>($"api/outgoing-letters/{letterId}/status", new { status });

    public Task<List<OutgoingLetterPickDto>> PickAsync(string? search = null) =>
        ListOrPaged.GetAsync<OutgoingLetterPickDto>(_api,
            $"api/outgoing-letters/pick{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<List<ErjaTreeNodeDto>> GetGardeshAsync(int letterId) =>
        ListOrPaged.GetAsync<ErjaTreeNodeDto>(_api, $"api/outgoing-letters/{letterId}/gardesh");

    public Task AddErjaAsync(AddErjaDto dto) => _api.PostAsync<object>("api/outgoing-letters/erja", dto);

    public Task AnswerAsync(int erjaId, AnswerErjaDto dto) =>
        _api.PostAsync<object>($"api/outgoing-letters/erja/{erjaId}/answer", dto);

    public Task MarkReadAsync(int erjaId) =>
        _api.PostAsync<object>($"api/outgoing-letters/erja/{erjaId}/read");

    public Task MarkLetterReadAsync(int letterId) =>
        _api.PostAsync<object>($"api/outgoing-letters/{letterId}/read");

    public async Task<bool> ToggleNeshanAsync(int erjaId) =>
        (await _api.PostAsync<NeshanResponse>($"api/outgoing-letters/erja/{erjaId}/neshan")).IsNeshan;

    /// <summary>نشان‌کردن نامه صادره ارسالی (سمت فرستنده) — مشابه نامه داخلی</summary>
    public async Task<bool> ToggleLetterNeshanAsync(int letterId) =>
        (await _api.PostAsync<NeshanResponse>($"api/outgoing-letters/{letterId}/neshan")).IsNeshan;

    public async Task<bool> ToggleBayeganiAsync(int erjaId) =>
        (await _api.PostAsync<BayeganiResponse>($"api/outgoing-letters/erja/{erjaId}/bayegani")).IsBayegani;

    public async Task<int> BatchBayeganiAsync(List<int> erjaIds, string? description = null)
    {
        var res = await _api.PostAsync<BatchBayeganiRes>("api/outgoing-letters/erja/batch-bayegani", new { erjaIds, description });
        return res?.Count ?? erjaIds.Count;
    }

    private class BatchBayeganiRes { public int Count { get; set; } }

    public Task<List<AmalgarDto>> GetAmalgarsAsync() =>
        ListOrPaged.GetAsync<AmalgarDto>(_api, "api/outgoing-letters/amalgars");

    public Task<AmalgarDto> CreateAmalgarAsync(string title) =>
        _api.PostAsync<AmalgarDto>("api/outgoing-letters/amalgars", new CreateAmalgarDto { Title = title });

    public Task<PagedResult<OutgoingPishnevisDto>> GetPishnevisPageAsync(int skip, int take, string? search = null)
        => ListOrPaged.GetPagedAsync<OutgoingPishnevisDto>(_api, $"api/outgoing-letters/pishnevis?skip={skip}&take={take}&search={Uri.EscapeDataString(search ?? "")}");

    public Task<List<OutgoingPishnevisDto>> GetPishnevisListAsync(string? search = null) =>
        ListOrPaged.GetAsync<OutgoingPishnevisDto>(_api,
            $"api/outgoing-letters/pishnevis{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<OutgoingPishnevisDto> GetPishnevisAsync(int id) =>
        _api.GetAsync<OutgoingPishnevisDto>($"api/outgoing-letters/pishnevis/{id}");

    public async Task<int> SavePishnevisAsync(OutgoingPishnevisDto dto) =>
        (await _api.PostAsync<IdResponse>("api/outgoing-letters/pishnevis", dto)).Id;

    public Task DeletePishnevisAsync(int id) => _api.DeleteAsync($"api/outgoing-letters/pishnevis/{id}");

    public Task<List<LetterReciverDto>> GetReciversAsync() =>
        ListOrPaged.GetAsync<LetterReciverDto>(_api, "api/outgoing-letters/recivers");

    public Task EditAsync(int letterId, EditOutgoingLetterDto dto) =>
        _api.PutAsync<object>($"api/outgoing-letters/{letterId}", dto);

    public Task<List<LetterGroupDto>> GetGroupsAsync() =>
        ListOrPaged.GetAsync<LetterGroupDto>(_api, "api/outgoing-letters/groups?withMembers=true");

    public Task<PagedResult<OutgoingLetterListItemDto>> GetSigningInboxAsync(string? search = null, bool? unsignedOnly = null, int page = 1, int pageSize = 15)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (unsignedOnly == true) qs.Add("unsignedOnly=true");
        return ListOrPaged.GetPagedAsync<OutgoingLetterListItemDto>(_api, $"api/outgoing-letters/signing-inbox?{string.Join("&", qs)}");
    }

    public Task<List<OutgoingSignerDto>> GetSignersAsync(int letterId) =>
        ListOrPaged.GetAsync<OutgoingSignerDto>(_api, $"api/outgoing-letters/{letterId}/signers");

    public Task SignAsync(int letterId, string? note = null) =>
        _api.PostAsync<object>($"api/outgoing-letters/{letterId}/sign", new { signNote = note });

    public Task<List<LetterReciverDto>> GetAvailableSignersAsync(string? search = null) =>
        ListOrPaged.GetAsync<LetterReciverDto>(_api,
            $"api/outgoing-letters/available-signers{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<List<LetterAttachmentDto>> GetAttachmentsAsync(int letterId) =>
        ListOrPaged.GetAsync<LetterAttachmentDto>(_api, $"api/outgoing-letters/{letterId}/attachments");

    public Task UploadAttachmentAsync(int letterId, Stream stream, string fileName, string contentType) =>
        UploadCoreAsync($"api/outgoing-letters/{letterId}/attachments", stream, fileName, contentType);

    public Task<List<LetterAttachmentDto>> GetPishnevisAttachmentsAsync(int pishnevisId) =>
        ListOrPaged.GetAsync<LetterAttachmentDto>(_api, $"api/outgoing-letters/pishnevis/{pishnevisId}/attachments");

    public Task UploadPishnevisAttachmentAsync(int pishnevisId, Stream stream, string fileName, string contentType) =>
        UploadCoreAsync($"api/outgoing-letters/pishnevis/{pishnevisId}/attachments", stream, fileName, contentType);

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

    public Task DeleteAttachmentAsync(int attachmentId) =>
        _api.DeleteAsync($"api/outgoing-letters/attachments/{attachmentId}");

    /// <summary>دانلود کلی همهٔ پیوست‌های نامه صادره به صورت فایل ZIP</summary>
    public async Task<byte[]> DownloadAttachmentsZipAsync(int letterId)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, _api.BuildUrl($"api/outgoing-letters/{letterId}/attachments/download-zip"));
        if (!string.IsNullOrWhiteSpace(_auth.Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _auth.Token);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(
            ApiClient.DescribeBody(await resp.Content.ReadAsStringAsync()));
        return await resp.Content.ReadAsByteArrayAsync();
    }

    public string AttachmentDownloadUrl(int attachmentId)
    {
        var url = _api.BuildUrl($"api/outgoing-letters/attachments/{attachmentId}/download");
        return string.IsNullOrWhiteSpace(_auth.Token)
            ? url
            : $"{url}?access_token={Uri.EscapeDataString(_auth.Token)}";
    }

    // ==================== دبیرخانه نامه صادره ====================

    /// <summary>لیست دبیرخانه با جستجوی پیشرفته — فیلترها به‌صورت پارامتر کوئری ارسال می‌شوند</summary>
    public Task<List<DabirkhaneListItemDto>> GetDabirkhaneAsync(DabirkhaneSearchDto? filter = null)
        => ListOrPaged.GetAsync<DabirkhaneListItemDto>(_api, $"api/outgoing-letters/dabirkhane{BuildDabirkhaneQuery(filter)}");

    public Task<PagedResult<DabirkhaneListItemDto>> GetDabirkhanePageAsync(DabirkhaneSearchDto? filter = null, int page = 1, int pageSize = 15)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;
        var q = BuildDabirkhaneQuery(filter);
        var sep = q.Length > 0 ? "&" : "?";
        return ListOrPaged.GetPagedAsync<DabirkhaneListItemDto>(_api,
            $"api/outgoing-letters/dabirkhane{q}{sep}skip={(page - 1) * pageSize}&take={pageSize}");
    }

    /// <summary>ساخت رشتهٔ کوئری فیلترهای جستجوی پیشرفتهٔ دبیرخانه (مشترک بین فهرست کامل و صفحه‌بندی‌شده).</summary>
    private static string BuildDabirkhaneQuery(DabirkhaneSearchDto? filter)
    {
        var qs = new List<string>();
        if (filter != null)
        {
            if (!string.IsNullOrWhiteSpace(filter.Text))
                qs.Add($"search={Uri.EscapeDataString(filter.Text)}");
            if (filter.RegisteredOnly != null)
                qs.Add($"registeredOnly={(filter.RegisteredOnly == true ? "true" : "false")}");
            if (filter.ArchivedOnly != null)
                qs.Add($"archivedOnly={(filter.ArchivedOnly == true ? "true" : "false")}");
            if (!string.IsNullOrWhiteSpace(filter.SendMethod))
                qs.Add($"sendMethod={Uri.EscapeDataString(filter.SendMethod)}");
            if (filter.CreatorUserId is > 0)
                qs.Add($"creatorUserId={filter.CreatorUserId.Value}");
            if (filter.CompanyId is > 0)
                qs.Add($"companyId={filter.CompanyId.Value}");
            if (!string.IsNullOrWhiteSpace(filter.ReceiverOrganization))
                qs.Add($"receiverOrganization={Uri.EscapeDataString(filter.ReceiverOrganization)}");
            if (!string.IsNullOrWhiteSpace(filter.Mahramanegi))
                qs.Add($"mahramanegi={Uri.EscapeDataString(filter.Mahramanegi)}");
            if (!string.IsNullOrWhiteSpace(filter.Foriat))
                qs.Add($"foriat={Uri.EscapeDataString(filter.Foriat)}");
            if (filter.HasAttachment != null)
                qs.Add($"hasAttachment={(filter.HasAttachment == true ? "true" : "false")}");
            if (filter.FromDate != null)
                qs.Add($"fromDate={Uri.EscapeDataString(filter.FromDate.Value.ToString("yyyy-MM-ddTHH:mm:ss"))}");
            if (filter.ToDate != null)
                qs.Add($"toDate={Uri.EscapeDataString(filter.ToDate.Value.ToString("yyyy-MM-ddTHH:mm:ss"))}");
        }

        return qs.Count > 0 ? "?" + string.Join("&", qs) : "";
    }

    public Task<DabirkhaneStatsDto> GetDabirkhaneStatsAsync() =>
        _api.GetAsync<DabirkhaneStatsDto>("api/outgoing-letters/dabirkhane/stats");

    public Task DabirkhaneRegisterAsync(int letterId, DabirkhaneRegisterDto dto) =>
        _api.PostAsync<object>($"api/outgoing-letters/{letterId}/dabirkhane", dto);

    public Task<List<LetterCompanyDto>> GetCompaniesAsync() =>
        ListOrPaged.GetAsync<LetterCompanyDto>(_api, "api/outgoing-letters/companies");

    /// <summary>شرکت فعال کاربر جاری — برای انتخاب سربرگ نامه صادره</summary>
    public Task<LetterCompanyDto?> GetActiveCompanyAsync() =>
        _api.GetAsync<LetterCompanyDto?>("api/outgoing-letters/companies/active");

    /// <summary>تشخیص چاپ نامه — مسیر سربرگ/طرح و علت خوانده‌نشدن فایل شرکت.</summary>
    public Task<OutgoingLetterPrintDiagnosticsDto?> GetPrintDiagnosticsAsync(int letterId, string size = "A4") =>
        _api.GetAsync<OutgoingLetterPrintDiagnosticsDto?>(
            $"api/outgoing-letters/{letterId}/print/diagnose?size={Uri.EscapeDataString(size)}");

    public Task<List<LetterReciverDto>> GetDabirkhaneCreatorsAsync() =>
        _api.GetAsync<List<LetterReciverDto>>("api/outgoing-letters/dabirkhane/creators");

    // ==================== بایگانی دبیرخانه ====================

    public Task<List<BayeganiNodeDto>> GetDabirkhaneBayeganiTreeAsync() =>
        _api.GetAsync<List<BayeganiNodeDto>>("api/outgoing-letters/dabirkhane/bayegani/tree");

    public Task<BayeganiNodeDto> AddDabirkhaneMainCategoryAsync(SaveBayeganiFolderDto dto) =>
        _api.PostAsync<BayeganiNodeDto>("api/outgoing-letters/dabirkhane/bayegani/main-category", dto);

    public Task<BayeganiNodeDto> AddDabirkhaneSubCategoryAsync(SaveBayeganiFolderDto dto) =>
        _api.PostAsync<BayeganiNodeDto>("api/outgoing-letters/dabirkhane/bayegani/sub-category", dto);

    public Task<BayeganiNodeDto> EditDabirkhaneFolderAsync(int id, SaveBayeganiFolderDto dto) =>
        _api.PutAsync<BayeganiNodeDto>($"api/outgoing-letters/dabirkhane/bayegani/folder/{id}", dto);

    public Task<BayeganiNodeDto> MoveDabirkhaneFolderAsync(int id, int newParentId) =>
        _api.PostAsync<BayeganiNodeDto>($"api/outgoing-letters/dabirkhane/bayegani/folder/{id}/move?newParentId={newParentId}");

    public Task DeleteDabirkhaneBayeganiAsync(int id) =>
        _api.DeleteAsync($"api/outgoing-letters/dabirkhane/bayegani/{id}");

    public Task ArchiveDabirkhaneLettersAsync(ArchiveOutgoingLettersDto dto) =>
        _api.PostAsync<object>("api/outgoing-letters/dabirkhane/bayegani/letters", dto);

    public Task UnarchiveDabirkhaneLetterAsync(int letterId) =>
        _api.DeleteAsync($"api/outgoing-letters/dabirkhane/bayegani/letter/{letterId}");

    public Task<BayeganiNodeDto> MoveDabirkhaneLetterAsync(MoveArchivedLetterDto dto) =>
        _api.PostAsync<BayeganiNodeDto>("api/outgoing-letters/dabirkhane/bayegani/move-letter", dto);

    /// <summary>دریافت PDF چاپ نامه روی سربرگ شرکت (A4/A5) — با توکن ورود و نسخهٔ چاپ</summary>
    public async Task<string?> GetUserSignatureDataUrlAsync(int userId)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, _api.BuildUrl($"api/auth/users/signature/{userId}"));
        if (!string.IsNullOrWhiteSpace(_auth.Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _auth.Token);
        using var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        return bytes.Length == 0 ? null : $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    public async Task<byte[]> GetPrintPdfAsync(int letterId, string size, bool withCopy = true, bool withMargin = true, bool withLetterhead = true, CancellationToken cancellationToken = default)
    {
        var url = $"api/outgoing-letters/{letterId}/print?size={Uri.EscapeDataString(size)}&withCopy={(withCopy ? "true" : "false")}&withMargin={(withMargin ? "true" : "false")}&withLetterhead={(withLetterhead ? "true" : "false")}";
        using var req = new HttpRequestMessage(HttpMethod.Get, _api.BuildUrl(url));
        if (!string.IsNullOrWhiteSpace(_auth.Token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _auth.Token);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(cancellationToken);
            string msg = "دریافت فایل چاپ ناموفق بود.";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m)) msg = m.GetString() ?? msg;
            }
            catch { }
            throw new ApiException(msg);
        }
        return await resp.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
