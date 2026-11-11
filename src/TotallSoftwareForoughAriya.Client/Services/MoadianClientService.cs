using System.Globalization;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IMoadianClientService
{
    Task<MdnDashboardDto> GetDashboardAsync();

    Task<MdnProfileDto> GetProfileAsync();
    Task<MdnProfileDto> SaveProfileAsync(MdnSaveProfileRequest req);
    Task<MdnProfileDto> UploadCertificateAsync(MdnUploadCertificateRequest req);
    Task<MdnProfileDto> ClearCertificateAsync();
    Task<MdnPkcs11DiscoverResultDto> DiscoverPkcs11Async(MdnPkcs11DiscoverRequest req);
    Task<MdnWindowsStoreDiscoverResultDto> DiscoverWindowsStoreAsync();
    Task<MdnConnectionDiagnosticsDto> TestConnectionAsync();
    Task<MdnTaxpayerLookupDto> LookupTaxpayerAsync(string economicCode);
    Task<MdnArticle6CheckResultDto> CheckArticle6Async(MdnArticle6CheckRequest req);

    Task<List<MdnBuyerDto>> GetBuyersAsync(string? search = null);
    Task<MdnBuyerDto> SaveBuyerAsync(MdnSaveBuyerRequest req);
    Task DeleteBuyerAsync(int id);
    Task<MdnSyncResultDto> SyncBuyersFromPartiesAsync();

    Task<List<MdnCatalogItemDto>> GetCatalogAsync(string? search = null);
    Task<MdnCatalogItemDto> SaveCatalogAsync(MdnSaveCatalogItemRequest req);
    Task DeleteCatalogAsync(int id);
    Task<MdnSyncResultDto> SyncCatalogFromProductsAsync();

    Task<List<MdnMeasureUnitDto>> GetUnitsAsync();
    Task<MdnMeasureUnitDto> SaveUnitAsync(MdnSaveMeasureUnitRequest req);

    Task<MdnInvoiceListResultDto> GetInvoicesAsync(MdnInvoiceFilterRequest filter);
    Task<MdnInvoiceDetailDto> GetInvoiceDetailAsync(int id);
    Task<MdnInvoiceDetailDto> SaveInvoiceAsync(MdnSaveInvoiceRequest req);
    Task DeleteInvoiceAsync(int id);
    Task<MdnInvoiceJsonPreviewDto> PreviewJsonAsync(int id);
    Task<MdnInvoiceDetailDto> CreateDerivativeAsync(int id, MdnCreateDerivativeRequest req);

    Task<List<MdnErpFacInvoicePickDto>> GetErpInvoicesAsync();
    Task<MdnInvoiceDetailDto> ImportErpInvoiceAsync(int facInvoiceId);

    Task<MdnBatchActionResultDto> SendInvoicesAsync(List<int> ids);
    Task<MdnBatchActionResultDto> InquirySubmissionsAsync(List<int> ids);
    Task<MdnBatchActionResultDto> InquiryFolderAsync(List<int> ids);
    Task<MdnBatchActionResultDto> RefreshAllAsync(List<int> ids);
    Task<List<MdnTimeRangeInquiryItemDto>> InquiryTimeRangeAsync(MdnTimeRangeInquiryRequest req);

    Task<MdnInvoicePaymentDto> RegisterPaymentAsync(int invoiceId, MdnRegisterPaymentRequest req);
    Task<MdnInvoicePaymentDto> RetryPaymentAsync(int invoiceId, int paymentId);
    Task DeletePaymentAsync(int invoiceId, int paymentId);
}

public sealed class MoadianClientService : IMoadianClientService
{
    private readonly IApiClient _api;

    public MoadianClientService(IApiClient api) => _api = api;

    public Task<MdnDashboardDto> GetDashboardAsync()
        => _api.GetAsync<MdnDashboardDto>("api/moadian/dashboard");

    public Task<MdnProfileDto> GetProfileAsync()
        => _api.GetAsync<MdnProfileDto>("api/moadian/profile");

    public Task<MdnProfileDto> SaveProfileAsync(MdnSaveProfileRequest req)
        => _api.PostAsync<MdnProfileDto>("api/moadian/profile", req);

    public Task<MdnProfileDto> UploadCertificateAsync(MdnUploadCertificateRequest req)
        => _api.PostAsync<MdnProfileDto>("api/moadian/profile/certificate", req);

    public Task<MdnProfileDto> ClearCertificateAsync()
        => _api.DeleteAsync<MdnProfileDto>("api/moadian/profile/certificate");

    public Task<MdnPkcs11DiscoverResultDto> DiscoverPkcs11Async(MdnPkcs11DiscoverRequest req)
        => _api.PostAsync<MdnPkcs11DiscoverResultDto>("api/moadian/pkcs11/discover", req);

    public Task<MdnWindowsStoreDiscoverResultDto> DiscoverWindowsStoreAsync()
        => _api.GetAsync<MdnWindowsStoreDiscoverResultDto>("api/moadian/windows-store/certificates");

    public Task<MdnConnectionDiagnosticsDto> TestConnectionAsync()
        => _api.PostAsync<MdnConnectionDiagnosticsDto>("api/moadian/diagnostics/connection");

    public Task<MdnTaxpayerLookupDto> LookupTaxpayerAsync(string economicCode)
        => _api.GetAsync<MdnTaxpayerLookupDto>($"api/moadian/diagnostics/taxpayer/{Uri.EscapeDataString(economicCode.Trim())}");

    public Task<MdnArticle6CheckResultDto> CheckArticle6Async(MdnArticle6CheckRequest req)
        => _api.PostAsync<MdnArticle6CheckResultDto>("api/moadian/diagnostics/article6", req);

    public Task<List<MdnBuyerDto>> GetBuyersAsync(string? search = null)
        => _api.GetAsync<List<MdnBuyerDto>>(string.IsNullOrWhiteSpace(search)
            ? "api/moadian/buyers"
            : $"api/moadian/buyers?search={Uri.EscapeDataString(search.Trim())}");

    public Task<MdnBuyerDto> SaveBuyerAsync(MdnSaveBuyerRequest req)
        => _api.PostAsync<MdnBuyerDto>("api/moadian/buyers", req);

    public Task DeleteBuyerAsync(int id)
        => _api.DeleteAsync($"api/moadian/buyers/{id}");

    public Task<MdnSyncResultDto> SyncBuyersFromPartiesAsync()
        => _api.PostAsync<MdnSyncResultDto>("api/moadian/buyers/sync-erp");

    public Task<List<MdnCatalogItemDto>> GetCatalogAsync(string? search = null)
        => _api.GetAsync<List<MdnCatalogItemDto>>(string.IsNullOrWhiteSpace(search)
            ? "api/moadian/catalog"
            : $"api/moadian/catalog?search={Uri.EscapeDataString(search.Trim())}");

    public Task<MdnCatalogItemDto> SaveCatalogAsync(MdnSaveCatalogItemRequest req)
        => _api.PostAsync<MdnCatalogItemDto>("api/moadian/catalog", req);

    public Task DeleteCatalogAsync(int id)
        => _api.DeleteAsync($"api/moadian/catalog/{id}");

    public Task<MdnSyncResultDto> SyncCatalogFromProductsAsync()
        => _api.PostAsync<MdnSyncResultDto>("api/moadian/catalog/sync-erp");

    public Task<List<MdnMeasureUnitDto>> GetUnitsAsync()
        => _api.GetAsync<List<MdnMeasureUnitDto>>("api/moadian/units");

    public Task<MdnMeasureUnitDto> SaveUnitAsync(MdnSaveMeasureUnitRequest req)
        => _api.PostAsync<MdnMeasureUnitDto>("api/moadian/units", req);

    public Task<MdnInvoiceListResultDto> GetInvoicesAsync(MdnInvoiceFilterRequest f)
    {
        var qs = new List<string>
        {
            $"page={f.Page}",
            $"pageSize={f.PageSize}"
        };
        if (!string.IsNullOrWhiteSpace(f.Search))
            qs.Add($"search={Uri.EscapeDataString(f.Search.Trim())}");
        if (f.SubmissionStatus.HasValue)
            qs.Add($"submissionStatus={(int)f.SubmissionStatus.Value}");
        if (f.FolderStatus.HasValue)
            qs.Add($"folderStatus={(int)f.FolderStatus.Value}");
        if (f.PaymentStatus.HasValue)
            qs.Add($"paymentStatus={(int)f.PaymentStatus.Value}");
        if (f.Subject.HasValue)
            qs.Add($"subject={(int)f.Subject.Value}");
        if (f.FromDate.HasValue)
            qs.Add($"fromDate={f.FromDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (f.ToDate.HasValue)
            qs.Add($"toDate={f.ToDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");

        return _api.GetAsync<MdnInvoiceListResultDto>("api/moadian/invoices?" + string.Join("&", qs));
    }

    public Task<MdnInvoiceDetailDto> GetInvoiceDetailAsync(int id)
        => _api.GetAsync<MdnInvoiceDetailDto>($"api/moadian/invoices/{id}");

    public Task<MdnInvoiceDetailDto> SaveInvoiceAsync(MdnSaveInvoiceRequest req)
        => _api.PostAsync<MdnInvoiceDetailDto>("api/moadian/invoices", req);

    public Task DeleteInvoiceAsync(int id)
        => _api.DeleteAsync($"api/moadian/invoices/{id}");

    public Task<MdnInvoiceJsonPreviewDto> PreviewJsonAsync(int id)
        => _api.GetAsync<MdnInvoiceJsonPreviewDto>($"api/moadian/invoices/{id}/preview-json");

    public Task<MdnInvoiceDetailDto> CreateDerivativeAsync(int id, MdnCreateDerivativeRequest req)
        => _api.PostAsync<MdnInvoiceDetailDto>($"api/moadian/invoices/{id}/derivative", req);

    public Task<List<MdnErpFacInvoicePickDto>> GetErpInvoicesAsync()
        => _api.GetAsync<List<MdnErpFacInvoicePickDto>>("api/moadian/erp-invoices");

    public Task<MdnInvoiceDetailDto> ImportErpInvoiceAsync(int facInvoiceId)
        => _api.PostAsync<MdnInvoiceDetailDto>($"api/moadian/erp-invoices/{facInvoiceId}/import");

    public Task<MdnBatchActionResultDto> SendInvoicesAsync(List<int> ids)
        => _api.PostAsync<MdnBatchActionResultDto>("api/moadian/invoices/send", new MdnBulkIdsRequest { InvoiceIds = ids });

    public Task<MdnBatchActionResultDto> InquirySubmissionsAsync(List<int> ids)
        => _api.PostAsync<MdnBatchActionResultDto>("api/moadian/invoices/inquiry-submission", new MdnBulkIdsRequest { InvoiceIds = ids });

    public Task<MdnBatchActionResultDto> InquiryFolderAsync(List<int> ids)
        => _api.PostAsync<MdnBatchActionResultDto>("api/moadian/invoices/inquiry-folder", new MdnBulkIdsRequest { InvoiceIds = ids });

    public Task<MdnBatchActionResultDto> RefreshAllAsync(List<int> ids)
        => _api.PostAsync<MdnBatchActionResultDto>("api/moadian/invoices/refresh-all", new MdnBulkIdsRequest { InvoiceIds = ids });

    public Task<List<MdnTimeRangeInquiryItemDto>> InquiryTimeRangeAsync(MdnTimeRangeInquiryRequest req)
        => _api.PostAsync<List<MdnTimeRangeInquiryItemDto>>("api/moadian/invoices/inquiry-time-range", req);

    public Task<MdnInvoicePaymentDto> RegisterPaymentAsync(int invoiceId, MdnRegisterPaymentRequest req)
        => _api.PostAsync<MdnInvoicePaymentDto>($"api/moadian/invoices/{invoiceId}/payments", req);

    public Task<MdnInvoicePaymentDto> RetryPaymentAsync(int invoiceId, int paymentId)
        => _api.PostAsync<MdnInvoicePaymentDto>($"api/moadian/invoices/{invoiceId}/payments/{paymentId}/retry");

    public Task DeletePaymentAsync(int invoiceId, int paymentId)
        => _api.DeleteAsync($"api/moadian/invoices/{invoiceId}/payments/{paymentId}");
}
