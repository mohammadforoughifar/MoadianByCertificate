using System.Net.Http.Json;
using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.ViewModels.Customers;
using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.Contracts.ViewModels.Moadian;
using MoudianMiddleware.Contracts.ViewModels.Products;
using MoudianMiddleware.Client.ViewModels;

namespace MoudianMiddleware.Client.Services;

public sealed class InvoiceService(BackendApi api)
{
    public Task<PagedResult<InvoiceViewModel>> ListAsync(InvoiceListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return api.GetAsync<PagedResult<InvoiceViewModel>>($"invoices?pageNumber={query.PageNumber}&pageSize={query.PageSize}&status={query.Status}&fromDate={Uri.EscapeDataString(query.FromDate?.ToString("O") ?? string.Empty)}&toDate={Uri.EscapeDataString(query.ToDate?.ToString("O") ?? string.Empty)}", ct);
    }
    public Task<InvoiceViewModel> GetAsync(int id, CancellationToken ct = default) => api.GetAsync<InvoiceViewModel>($"invoices/{id}", ct);
    public Task<InvoiceViewModel> CreateAsync(InvoiceWriteRequest request, CancellationToken ct = default) => api.PostAsync<InvoiceWriteRequest, InvoiceViewModel>("invoices", request, ct);
    public Task<InvoiceViewModel> UpdateAsync(int id, InvoiceWriteRequest request, CancellationToken ct = default) => api.PutAsync<InvoiceWriteRequest, InvoiceViewModel>($"invoices/{id}/draft", request, ct);
    public Task<object> SendAsync(int id, CancellationToken ct = default) => api.PostAsync<object, object>($"invoices/{id}/send", new { }, ct);
    public Task<object> InquiryAsync(int id, CancellationToken ct = default) => api.PostAsync<object, object>($"invoices/{id}/inquiry", new { }, ct);
    public Task<object> ResendAsync(int id, CancellationToken ct = default) => api.PostAsync<object, object>($"invoices/{id}/resend", new { }, ct);
}

public sealed class CustomerService(BackendApi api)
{
    public Task<PagedResult<CustomerViewModel>> ListAsync(int page, string? search, CancellationToken ct = default)
        => api.GetAsync<PagedResult<CustomerViewModel>>($"customers?pageNumber={page}&pageSize=20&search={Uri.EscapeDataString(search ?? string.Empty)}", ct);
    public Task<CustomerViewModel> SaveAsync(CustomerWriteRequest item, int? id, CancellationToken ct = default)
        => id is null ? api.PostAsync<CustomerWriteRequest, CustomerViewModel>("customers", item, ct) : api.PutAsync<CustomerWriteRequest, CustomerViewModel>($"customers/{id}", item, ct);
    public Task DeleteAsync(int id, CancellationToken ct = default) => api.DeleteAsync($"customers/{id}", ct);
}

public sealed class ProductService(BackendApi api)
{
    public Task<PagedResult<ProductViewModel>> ListAsync(int page, string? search, CancellationToken ct = default)
        => api.GetAsync<PagedResult<ProductViewModel>>($"products?pageNumber={page}&pageSize=20&search={Uri.EscapeDataString(search ?? string.Empty)}", ct);
    public Task<ProductViewModel> SaveAsync(ProductWriteRequest item, int? id, CancellationToken ct = default)
        => id is null ? api.PostAsync<ProductWriteRequest, ProductViewModel>("products", item, ct) : api.PutAsync<ProductWriteRequest, ProductViewModel>($"products/{id}", item, ct);
    public Task DeleteAsync(int id, CancellationToken ct = default) => api.DeleteAsync($"products/{id}", ct);
}

public sealed class MoadianSettingsService(BackendApi api)
{
    public Task<MoadianSettingViewModel?> GetAsync(CancellationToken ct = default) => api.GetAsync<MoadianSettingViewModel?>("tenants/current/moadian-settings", ct);
    public Task<MoadianSettingViewModel> SaveAsync(MoadianSettingWriteRequest request, CancellationToken ct = default) => api.PutAsync<MoadianSettingWriteRequest, MoadianSettingViewModel>("tenants/current/moadian-settings", request, ct);
    public Task<MoadianCredentialViewModel?> GetCredentialAsync(CancellationToken ct = default) => api.GetAsync<MoadianCredentialViewModel?>("tenants/current/moadian-settings/credential", ct);
    public Task<MoadianCredentialViewModel> SaveCredentialAsync(SaveMoadianCredentialRequest request, CancellationToken ct = default) => api.PutAsync<SaveMoadianCredentialRequest, MoadianCredentialViewModel>("tenants/current/moadian-settings/credential", request, ct);
}

public sealed class AdministrationService(BackendApi api)
{
    public Task<IReadOnlyList<ClientUserRow>> UsersAsync(CancellationToken ct = default) => api.GetAsync<IReadOnlyList<ClientUserRow>>("users", ct);
    public Task<ClientUserRow> CreateUserAsync(ClientUserRequest request, CancellationToken ct = default) => api.PostAsync<ClientUserRequest, ClientUserRow>("users", request, ct);
    public Task<IReadOnlyList<ClientRoleRow>> RolesAsync(CancellationToken ct = default) => api.GetAsync<IReadOnlyList<ClientRoleRow>>("roles", ct);
    public Task<ClientRoleRow> CreateRoleAsync(ClientRoleRequest request, CancellationToken ct = default) => api.PostAsync<ClientRoleRequest, ClientRoleRow>("roles", request, ct);
    public Task<IReadOnlyList<ClientApiKeyRow>> ApiKeysAsync(CancellationToken ct = default) => api.GetAsync<IReadOnlyList<ClientApiKeyRow>>("api-clients", ct);
    public Task<ClientApiKeyCreated> CreateApiKeyAsync(ClientApiKeyRequest request, CancellationToken ct = default) => api.PostAsync<ClientApiKeyRequest, ClientApiKeyCreated>("api-clients", request, ct);
    public Task RevokeApiKeyAsync(int id, CancellationToken ct = default) => api.DeleteAsync($"api-clients/{id}", ct);
    public Task<Dictionary<string, int>> InvoiceSummaryAsync(CancellationToken ct = default) => api.GetAsync<Dictionary<string, int>>("reports/invoice-summary", ct);
}

public sealed class ReportService(IHttpClientFactory clients)
{
    public async Task<byte[]> DownloadInvoicesAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        using var response = await clients.CreateClient("AuthenticatedApi").GetAsync($"reports/invoices.xlsx?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new ApiClientException("دریافت فایل گزارش ناموفق بود.", response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }
}
