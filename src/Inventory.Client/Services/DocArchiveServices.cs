using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
//  سرویس‌های ماژول آرشیو اسناد و مدارک (سمت کلاینت)
// =====================================================================

public interface IDocArchiveService
{
    Task<DocSearchPageDto> SearchPageAsync(DocSearchFilterDto filter);
    Task<DocArchiveStatsDto> GetStatsAsync();
    Task<List<DocTemporaryGrantDto>> GetTemporaryGrantsAsync(int id);
    Task SaveTemporaryGrantAsync(int id, DocTemporaryGrantDto dto);
    Task RevokeTemporaryGrantAsync(int id, int grantId);
    Task<DocRenewalPolicyDto> GetRenewalAsync(int id);
    Task SaveRenewalAsync(int id, DocRenewalPolicyDto dto);
    Task<DocIndexQueueDto> GetIndexQueueAsync();
    Task RetryIndexAsync(int attachmentId);
    Task<List<LookupItem>> GetCompareFilesAsync(int id, int versionId);
    Task<DocContentCompareDto> CompareContentAsync(int id, DocContentCompareRequest dto);

    // پوشه‌ها
    Task<List<DocFolderDto>> GetFoldersAsync();
    Task<DocFolderDto> GetFolderAsync(int id);
    Task<int> CreateFolderAsync(DocFolderDto dto);
    Task UpdateFolderAsync(int id, DocFolderDto dto);
    Task SaveFolderPermissionsAsync(int id, DocPermissionsSaveDto dto);
    Task DeleteFolderAsync(int id);

    // مدارک
    Task<List<DocumentListDto>> GetDocumentsAsync(int? folderId = null, string? search = null,
        bool onlyExpiring = false, string status = "active", string? expiry = null);

    /// <summary>شمارش مدارک منقضی‌شده و رو به انقضا برای بج‌های درخت.</summary>
    Task<DocExpirySummaryDto> GetExpirySummaryAsync();

    /// <summary>اجرای دستی بررسی انقضا (نیازمند دسترسی مدیریت).</summary>
    Task<string> RunExpiryCheckAsync();

    /// <summary>مقایسه دو ورژن یک مدرک.</summary>
    Task<DocVersionCompareDto> CompareVersionsAsync(int documentId, int from, int to);

    /// <summary>بازگرداندن مدرک از سطل بازیافت.</summary>
    Task<string> RestoreDocumentAsync(int id);

    /// <summary>حذف قطعی یک مدرک از سطل بازیافت.</summary>
    Task<string> PurgeDocumentAsync(int id);

    /// <summary>خالی کردن کل سطل بازیافت.</summary>
    Task<string> EmptyTrashAsync();
    Task<DocumentDto> GetDocumentAsync(int id);
    Task<int> CreateDocumentAsync(DocumentDto dto);
    Task UpdateDocumentAsync(int id, DocumentDto dto);
    Task SaveDocumentPermissionsAsync(int id, DocPermissionsSaveDto dto);
    Task DeleteDocumentAsync(int id);

    /// <summary>تایید مجدد رمز برای باز کردن قفل دانلود/مشاهده فایل‌های مدرک محرمانه (۱۵ دقیقه اعتبار).</summary>
    Task ConfirmDownloadAsync(int id, string password);

    /// <summary>گزارش مشاهده/دانلود فایل‌های مدرک — فقط دسترسی کامل.</summary>
    Task<List<DocAttachmentAccessLogDto>> GetAccessLogsAsync(int id);

    /// <summary>لاگ چاپ فایل‌های مدرک — فقط دسترسی کامل.</summary>
    Task<List<DocPrintLogDto>> GetPrintLogsAsync(int id);

    // درخواست دسترسی
    /// <summary>ثبت درخواست دسترسی به مدرک (کاربر بدون دسترسی).</summary>
    Task RequestAccessAsync(int id, DocAccessRequestSaveDto dto);
    /// <summary>درخواست‌های دسترسی یک مدرک — فقط مدیران مدرک.</summary>
    Task<List<DocAccessRequestDto>> GetAccessRequestsAsync(int id);
    /// <summary>درخواست‌های من برای این مدرک.</summary>
    Task<List<DocAccessRequestDto>> GetMyAccessRequestsAsync(int id);
    /// <summary>پذیرش درخواست دسترسی (مدیر مدرک).</summary>
    Task ApproveAccessRequestAsync(int requestId, DocAccessRequestApproveDto dto);
    /// <summary>رد درخواست دسترسی.</summary>
    Task RejectAccessRequestAsync(int requestId, string? note);
    /// <summary>لغو درخواست توسط خودِ درخواست‌کننده.</summary>
    Task CancelAccessRequestAsync(int requestId);

    // شماره‌گذار خودکار کد مدرک
    /// <summary>آیا کد مدرک قبلاً ثبت شده است؟ (هشدار زنده در فرم)</summary>
    Task<bool> CheckCodeExistsAsync(string code, int excludingId = 0);
    /// <summary>تنظیمات شماره‌گذار خودکار — مدیر آرشیو.</summary>
    Task<DocCodeSettingsDto> GetCodeSettingsAsync();
    Task<DocCodeSettingsDto> SaveCodeSettingsAsync(DocCodeSettingsDto dto);
    /// <summary>اجرای یک‌باره بازشمارش مدارک موجود.</summary>
    Task<DocCodeBackfillResultDto> RunCodeBackfillAsync();

    // ورژن و گردش
    Task<int> CreateVersionAsync(DocVersionCreateDto dto);
    Task SetVersionActiveAsync(int versionId, bool active);
    Task ApprovalAsync(DocApprovalActionDto dto);

    // لینک
    Task LinkAsync(int documentId, int linkedDocumentId, string? note);
    /// <summary>لینک چندتایی مدارک مرتبط</summary>
    Task LinkManyAsync(DocLinkSaveDto dto);
    Task UnlinkAsync(int documentId, int linkedDocumentId);

    /// <summary>فعال/غیرفعال کردن مدرک</summary>
    Task SetDocumentActiveAsync(int id, DocSetActiveDto dto);

    // کارتابل و داده کمکی
    Task<List<DocCartableItemDto>> GetCartableAsync(bool includeDone = false);
    Task CloseTaskAsync(int id);
    Task<DocArchiveLookups> GetLookupsAsync();

    // جستجوی پیشرفته، تمام‌متن (Full-Text) و تگ‌ها
    Task<List<DocumentListDto>> SearchDocumentsAsync(DocSearchFilterDto filter);
    Task<List<DocTagDto>> GetTagsAsync();
    Task<DocTagDto> CreateTagAsync(DocTagSaveDto dto);
    Task UpdateTagAsync(int id, DocTagSaveDto dto);
    Task DeleteTagAsync(int id);
    Task SetDocumentTagsAsync(int docId, List<int> tagIds);
    Task<List<DocExtractedTextDto>> GetExtractedTextsAsync(int docId);
    Task<DocOcrRunResultDto> RunOcrAsync(int attachmentId);
    Task<DocReindexResultDto> ReindexAllAsync();

    // دانلود درختی ZIP
    string GetFolderZipExportUrl(int? folderId = null, bool includeSubfolders = true, bool onlyActiveVersions = true, bool includeManifest = true);
    /// <summary>دانلود باینری بسته ZIP (با هدر احراز هویت) به‌همراه نام پیشنهادی سرور.</summary>
    Task<(byte[] Data, string FileName, string ContentType)> DownloadFolderZipAsync(int? folderId = null, bool includeSubfolders = true, bool onlyActiveVersions = true, bool includeManifest = true);

    // علاقه‌مندی و اخیراً دیده‌شده
    Task<bool> ToggleFavoriteAsync(int documentId);
    Task<List<DocFavoriteDto>> GetFavoritesAsync(int skip = 0, int? take = null);
    Task<Dictionary<int, bool>> GetFavoriteStatusAsync(List<int> documentIds);
    Task<List<DocRecentlyViewedDto>> GetRecentlyViewedAsync(int take = 10);

    // عملیات گروهی
    Task<DocBulkResultDto> BulkMoveAsync(DocBulkMoveDto dto);
    Task<DocBulkResultDto> BulkTagsAsync(DocBulkTagsDto dto);
    Task<DocBulkResultDto> BulkExpireAsync(DocBulkExpireDto dto);
    Task<DocBulkResultDto> BulkDeleteAsync(List<int> documentIds);

    // جستجوی معنایی B1
    Task<DocSemanticSearchResultDto> SemanticSearchAsync(DocSemanticSearchRequest req);
    Task<string> SemanticReindexAsync(int take = 30);

    // پیشنهاد خودکار B2
    Task<DocAutoSuggestResultDto> SuggestAsync(DocAutoSuggestRequest req);

    // نظرات و ردیاب (2025-10-09)
    Task<List<DocCommentDto>> GetCommentsAsync(int documentId);
    Task<int> AddCommentAsync(DocCommentCreateDto dto);
    Task ToggleCommentResolveAsync(int documentId, int commentId);
    Task DeleteCommentAsync(int documentId, int commentId);
    Task<DocDuplicateResultDto> CheckDuplicateAsync(DocDuplicateCheckRequest req);
    Task<List<DocLeakTraceDto>> GetLeakTracesAsync(int documentId);

    /// <summary>گفتگو با سند — پرسش آزاد از متن استخراج‌شدهٔ مدرک.</summary>
    Task<DocChatResponse> ChatWithDocumentAsync(int id, DocChatRequest req);
}

public class DocArchiveService : IDocArchiveService
{
    private readonly IApiClient _api;
    public DocArchiveService(IApiClient api) => _api = api;

    private const string Root = "api/doc-archive";

    public Task<DocSearchPageDto> SearchPageAsync(DocSearchFilterDto filter) => _api.PostAsync<DocSearchPageDto>($"{Root}/search-page", filter);
    public Task<DocArchiveStatsDto> GetStatsAsync() => _api.GetAsync<DocArchiveStatsDto>($"{Root}/stats");
    public Task<List<DocTemporaryGrantDto>> GetTemporaryGrantsAsync(int id) => _api.GetAsync<List<DocTemporaryGrantDto>>($"{Root}/documents/{id}/temporary-grants");
    public Task SaveTemporaryGrantAsync(int id, DocTemporaryGrantDto dto) => _api.PostAsync<object>($"{Root}/documents/{id}/temporary-grants", dto);
    public Task RevokeTemporaryGrantAsync(int id, int grantId) => _api.DeleteAsync($"{Root}/documents/{id}/temporary-grants/{grantId}");
    public Task<DocRenewalPolicyDto> GetRenewalAsync(int id) => _api.GetAsync<DocRenewalPolicyDto>($"{Root}/documents/{id}/renewal");
    public Task SaveRenewalAsync(int id, DocRenewalPolicyDto dto) => _api.PutAsync<object>($"{Root}/documents/{id}/renewal", dto);
    public Task<DocIndexQueueDto> GetIndexQueueAsync() => _api.GetAsync<DocIndexQueueDto>($"{Root}/index-queue");
    public Task RetryIndexAsync(int attachmentId) => _api.PostAsync<object>($"{Root}/index-queue/{attachmentId}/retry", new {});
    public Task<List<LookupItem>> GetCompareFilesAsync(int id, int versionId) => _api.GetAsync<List<LookupItem>>($"{Root}/documents/{id}/compare-files/{versionId}");
    public Task<DocContentCompareDto> CompareContentAsync(int id, DocContentCompareRequest dto) => _api.PostAsync<DocContentCompareDto>($"{Root}/documents/{id}/compare-content", dto);

    // ---------- پوشه‌ها ----------
    public Task<List<DocFolderDto>> GetFoldersAsync()
        => _api.GetAsync<List<DocFolderDto>>($"{Root}/folders");

    public Task<DocFolderDto> GetFolderAsync(int id)
        => _api.GetAsync<DocFolderDto>($"{Root}/folders/{id}");

    public async Task<int> CreateFolderAsync(DocFolderDto dto)
        => (await _api.PostAsync<IdResponse>($"{Root}/folders", dto)).Id;

    public Task UpdateFolderAsync(int id, DocFolderDto dto)
        => _api.PutAsync<object>($"{Root}/folders/{id}", dto);

    public Task SaveFolderPermissionsAsync(int id, DocPermissionsSaveDto dto)
        => _api.PutAsync<object>($"{Root}/folders/{id}/permissions", dto);

    public Task DeleteFolderAsync(int id)
        => _api.DeleteAsync($"{Root}/folders/{id}");

    // ---------- مدارک ----------
    public Task<List<DocumentListDto>> GetDocumentsAsync(int? folderId = null, string? search = null,
        bool onlyExpiring = false, string status = "active", string? expiry = null)
    {
        var url = $"{Root}/documents?search={Uri.EscapeDataString(search ?? "")}&status={status}";
        if (folderId is > 0) url += $"&folderId={folderId}";
        if (onlyExpiring) url += "&onlyExpiring=true";
        if (!string.IsNullOrEmpty(expiry)) url += $"&expiry={expiry}";
        return _api.GetAsync<List<DocumentListDto>>(url);
    }

    public Task<DocExpirySummaryDto> GetExpirySummaryAsync()
        => _api.GetAsync<DocExpirySummaryDto>("api/doc-archive/expiry-summary");

    public async Task<string> RunExpiryCheckAsync()
    {
        var r = await _api.PostAsync<DocExpiryRunResultDto>("api/doc-archive/expiry-check", new { });
        return r?.Message ?? "انجام شد.";
    }

    public Task<DocVersionCompareDto> CompareVersionsAsync(int documentId, int from, int to) =>
        _api.GetAsync<DocVersionCompareDto>($"{Root}/documents/{documentId}/compare?from={from}&to={to}");

    public async Task<string> RestoreDocumentAsync(int id)
    {
        var r = await _api.PutAsync<DocMessageDto>($"{Root}/documents/{id}/restore", new { });
        return r?.Message ?? "مدرک بازگردانده شد.";
    }

    public async Task<string> PurgeDocumentAsync(int id)
    {
        await _api.DeleteAsync($"{Root}/documents/{id}/purge");
        return "مدرک برای همیشه حذف شد.";
    }

    public async Task<string> EmptyTrashAsync()
    {
        await _api.DeleteAsync("api/doc-archive/trash");
        return "سطل بازیافت خالی شد.";
    }

    public Task<DocumentDto> GetDocumentAsync(int id)
        => _api.GetAsync<DocumentDto>($"{Root}/documents/{id}");

    public async Task<int> CreateDocumentAsync(DocumentDto dto)
        => (await _api.PostAsync<IdResponse>($"{Root}/documents", dto)).Id;

    public Task UpdateDocumentAsync(int id, DocumentDto dto)
        => _api.PutAsync<object>($"{Root}/documents/{id}", dto);

    public Task SaveDocumentPermissionsAsync(int id, DocPermissionsSaveDto dto)
        => _api.PutAsync<object>($"{Root}/documents/{id}/permissions", dto);

    public Task ConfirmDownloadAsync(int id, string password)
        => _api.PostAsync<object>($"{Root}/documents/{id}/confirm-download", new { Password = password });

    public Task<List<DocAttachmentAccessLogDto>> GetAccessLogsAsync(int id)
        => _api.GetAsync<List<DocAttachmentAccessLogDto>>($"{Root}/documents/{id}/access-logs");

    public Task<List<DocPrintLogDto>> GetPrintLogsAsync(int id)
        => _api.GetAsync<List<DocPrintLogDto>>($"{Root}/print/logs/{id}");

    public Task RequestAccessAsync(int id, DocAccessRequestSaveDto dto)
        => _api.PostAsync<object>($"{Root}/documents/{id}/request-access", dto);

    public Task<List<DocAccessRequestDto>> GetAccessRequestsAsync(int id)
        => _api.GetAsync<List<DocAccessRequestDto>>($"{Root}/documents/{id}/access-requests");

    public Task<List<DocAccessRequestDto>> GetMyAccessRequestsAsync(int id)
        => _api.GetAsync<List<DocAccessRequestDto>>($"{Root}/documents/{id}/access-requests/mine");

    public Task ApproveAccessRequestAsync(int requestId, DocAccessRequestApproveDto dto)
        => _api.PostAsync<object>($"{Root}/access-requests/{requestId}/approve", dto);

    public Task RejectAccessRequestAsync(int requestId, string? note)
        => _api.PostAsync<object>($"{Root}/access-requests/{requestId}/reject", new { Note = note });

    public Task CancelAccessRequestAsync(int requestId)
        => _api.PostAsync<object>($"{Root}/access-requests/{requestId}/cancel", new { });

    public async Task<bool> CheckCodeExistsAsync(string code, int excludingId = 0)
    {
        var res = await _api.GetAsync<CodeCheckResult>($"{Root}/documents/check-code?code={Uri.EscapeDataString(code)}&excludingId={excludingId}");
        return res?.Exists == true;
    }
    private class CodeCheckResult { public bool Exists { get; set; } }

    public Task<DocCodeSettingsDto> GetCodeSettingsAsync()
        => _api.GetAsync<DocCodeSettingsDto>($"{Root}/settings/code-numbering");

    public Task<DocCodeSettingsDto> SaveCodeSettingsAsync(DocCodeSettingsDto dto)
        => _api.PutAsync<DocCodeSettingsDto>($"{Root}/settings/code-numbering", dto);

    public Task<DocCodeBackfillResultDto> RunCodeBackfillAsync()
        => _api.PostAsync<DocCodeBackfillResultDto>($"{Root}/settings/code-numbering/run-once", new { });

    public Task DeleteDocumentAsync(int id)
        => _api.DeleteAsync($"{Root}/documents/{id}");

    // ---------- ورژن و گردش ----------
    public async Task<int> CreateVersionAsync(DocVersionCreateDto dto)
        => (await _api.PostAsync<IdResponse>($"{Root}/documents/versions", dto)).Id;

    public Task SetVersionActiveAsync(int versionId, bool active)
        => _api.PutAsync<object>($"{Root}/documents/versions/{versionId}/active?active={active.ToString().ToLower()}");

    public Task ApprovalAsync(DocApprovalActionDto dto)
        => _api.PostAsync<object>($"{Root}/documents/approval", dto);

    // ---------- لینک ----------
    public Task LinkAsync(int documentId, int linkedDocumentId, string? note)
        => _api.PostAsync<object>($"{Root}/documents/links",
            new { documentId, linkedDocumentId, note });

    public Task LinkManyAsync(DocLinkSaveDto dto)
        => _api.PostAsync<object>($"{Root}/documents/links/many", dto);

    public Task SetDocumentActiveAsync(int id, DocSetActiveDto dto)
        => _api.PutAsync<object>($"{Root}/documents/{id}/active", dto);

    public Task UnlinkAsync(int documentId, int linkedDocumentId)
        => _api.DeleteAsync($"{Root}/documents/links?documentId={documentId}&linkedDocumentId={linkedDocumentId}");

    // ---------- کارتابل ----------
    public Task<List<DocCartableItemDto>> GetCartableAsync(bool includeDone = false)
        => _api.GetAsync<List<DocCartableItemDto>>($"{Root}/cartable?includeDone={includeDone.ToString().ToLower()}");

    public Task CloseTaskAsync(int id)
        => _api.PutAsync<object>($"{Root}/cartable/{id}/done");

    public Task<DocArchiveLookups> GetLookupsAsync()
        => _api.GetAsync<DocArchiveLookups>($"{Root}/lookups");

    // ---------- جستجوی پیشرفته، تمام‌متن و OCR ----------
    public Task<List<DocumentListDto>> SearchDocumentsAsync(DocSearchFilterDto filter)
        => _api.PostAsync<List<DocumentListDto>>($"{Root}/search", filter);

    public Task<List<DocTagDto>> GetTagsAsync()
        => _api.GetAsync<List<DocTagDto>>($"{Root}/tags");

    public Task<DocTagDto> CreateTagAsync(DocTagSaveDto dto)
        => _api.PostAsync<DocTagDto>($"{Root}/tags", dto);

    public Task UpdateTagAsync(int id, DocTagSaveDto dto)
        => _api.PutAsync<object>($"{Root}/tags/{id}", dto);

    public Task DeleteTagAsync(int id)
        => _api.DeleteAsync($"{Root}/tags/{id}");

    public Task SetDocumentTagsAsync(int docId, List<int> tagIds)
        => _api.PostAsync<object>($"{Root}/documents/{docId}/tags", tagIds);

    public Task<List<DocExtractedTextDto>> GetExtractedTextsAsync(int docId)
        => _api.GetAsync<List<DocExtractedTextDto>>($"{Root}/documents/{docId}/extracted-texts");

    public Task<DocOcrRunResultDto> RunOcrAsync(int attachmentId)
        => _api.PostAsync<DocOcrRunResultDto>($"{Root}/attachments/{attachmentId}/ocr", new { });

    public Task<DocReindexResultDto> ReindexAllAsync()
        => _api.PostAsync<DocReindexResultDto>($"{Root}/reindex", new { });

    // ---------- دانلود درختی ZIP ----------
    public string GetFolderZipExportUrl(int? folderId = null, bool includeSubfolders = true, bool onlyActiveVersions = true, bool includeManifest = true)
    {
        if (folderId.HasValue && folderId.Value > 0)
        {
            return $"api/doc-archive/folders/{folderId.Value}/export-zip?includeSubfolders={includeSubfolders.ToString().ToLower()}&onlyActiveVersions={onlyActiveVersions.ToString().ToLower()}&includeManifest={includeManifest.ToString().ToLower()}";
        }
        return $"api/doc-archive/export-zip?includeSubfolders={includeSubfolders.ToString().ToLower()}&onlyActiveVersions={onlyActiveVersions.ToString().ToLower()}&includeManifest={includeManifest.ToString().ToLower()}";
    }

    public Task<(byte[] Data, string FileName, string ContentType)> DownloadFolderZipAsync(int? folderId = null, bool includeSubfolders = true, bool onlyActiveVersions = true, bool includeManifest = true)
        => _api.GetFileAsync(GetFolderZipExportUrl(folderId, includeSubfolders, onlyActiveVersions, includeManifest));

    // علاقه‌مندی و اخیراً دیده‌شده
    public async Task<bool> ToggleFavoriteAsync(int documentId)
    {
        var r = await _api.PostAsync<ToggleResult>($"{Root}/favorites/{documentId}/toggle", new { });
        return r?.IsFavorite ?? false;
    }
    private class ToggleResult { public bool IsFavorite { get; set; } }
    public Task<List<DocFavoriteDto>> GetFavoritesAsync(int skip = 0, int? take = null)
        => _api.GetAsync<List<DocFavoriteDto>>($"{Root}/favorites?skip={skip}&take={take ?? 50}");
    public Task<Dictionary<int, bool>> GetFavoriteStatusAsync(List<int> documentIds)
        => _api.PostAsync<Dictionary<int, bool>>($"{Root}/favorites/status", documentIds);
    public Task<List<DocRecentlyViewedDto>> GetRecentlyViewedAsync(int take = 10)
        => _api.GetAsync<List<DocRecentlyViewedDto>>($"{Root}/recently-viewed?take={take}");

    // عملیات گروهی
    public Task<DocBulkResultDto> BulkMoveAsync(DocBulkMoveDto dto) => _api.PostAsync<DocBulkResultDto>($"{Root}/bulk/move", dto);
    public Task<DocBulkResultDto> BulkTagsAsync(DocBulkTagsDto dto) => _api.PostAsync<DocBulkResultDto>($"{Root}/bulk/tags", dto);
    public Task<DocBulkResultDto> BulkExpireAsync(DocBulkExpireDto dto) => _api.PostAsync<DocBulkResultDto>($"{Root}/bulk/expire", dto);
    public Task<DocBulkResultDto> BulkDeleteAsync(List<int> documentIds) => _api.PostAsync<DocBulkResultDto>($"{Root}/bulk/delete", documentIds);

    // جستجوی معنایی B1
    public Task<DocSemanticSearchResultDto> SemanticSearchAsync(DocSemanticSearchRequest req) => _api.PostAsync<DocSemanticSearchResultDto>($"{Root}/semantic-search", req);
    public async Task<string> SemanticReindexAsync(int take = 30)
    {
        var r = await _api.PostAsync<ReindexMsg>($"{Root}/semantic-reindex?take={take}", new { });
        return r?.message ?? "ایندکس شد.";
    }
    private class ReindexMsg { public string message { get; set; } = ""; }

    // پیشنهاد خودکار B2
    public Task<DocAutoSuggestResultDto> SuggestAsync(DocAutoSuggestRequest req) => _api.PostAsync<DocAutoSuggestResultDto>($"{Root}/suggest", req);

    // نظرات و ردیاب
    public Task<List<DocCommentDto>> GetCommentsAsync(int documentId) => _api.GetAsync<List<DocCommentDto>>($"{Root}/documents/{documentId}/comments");
    public async Task<int> AddCommentAsync(DocCommentCreateDto dto)
    {
        var r = await _api.PostAsync<IdResponse>($"{Root}/documents/{dto.DocumentId}/comments", dto);
        return r.Id;
    }
    public Task ToggleCommentResolveAsync(int documentId, int commentId) => _api.PostAsync<object>($"{Root}/documents/{documentId}/comments/{commentId}/resolve", new { });
    public Task DeleteCommentAsync(int documentId, int commentId) => _api.DeleteAsync($"{Root}/documents/{documentId}/comments/{commentId}");
    public Task<DocDuplicateResultDto> CheckDuplicateAsync(DocDuplicateCheckRequest req) => _api.PostAsync<DocDuplicateResultDto>($"{Root}/duplicate-check", req);
    public Task<List<DocLeakTraceDto>> GetLeakTracesAsync(int documentId) => _api.GetAsync<List<DocLeakTraceDto>>($"{Root}/documents/{documentId}/leak-traces");

    public Task<DocChatResponse> ChatWithDocumentAsync(int id, DocChatRequest req) => _api.PostAsync<DocChatResponse>($"{Root}/documents/{id}/chat", req);

    private class IdResponse { public int Id { get; set; } }
}
