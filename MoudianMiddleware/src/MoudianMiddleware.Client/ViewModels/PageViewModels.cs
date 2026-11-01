using MoudianMiddleware.Client.Services;
using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Customers;
using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.Contracts.ViewModels.Moadian;
using MoudianMiddleware.Contracts.ViewModels.Products;

namespace MoudianMiddleware.Client.ViewModels;

public sealed class DashboardViewModel(AdministrationService admin)
{
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public Dictionary<string, int> Counts { get; private set; } = new();
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true; Error = null;
        try { Counts = await admin.InvoiceSummaryAsync(ct).ConfigureAwait(false); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsLoading = false; }
    }
}

public sealed class InvoiceListViewModel(InvoiceService invoices)
{
    public PagedResult<InvoiceViewModel>? Result { get; private set; }
    public InvoiceStatus? Status { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public int Page { get; private set; } = 1;
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true; Error = null;
        try { Result = await invoices.ListAsync(new InvoiceListQuery { PageNumber = Page, PageSize = 20, Status = Status, FromDate = FromDate, ToDate = ToDate }, ct).ConfigureAwait(false); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsLoading = false; }
    }
    public async Task SetPageAsync(int page, CancellationToken ct = default) { Page = Math.Max(1, page); await LoadAsync(ct).ConfigureAwait(false); }
}

public sealed class InvoiceFormViewModel
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public DateTimeOffset? IssueDate { get; set; } = DateTimeOffset.Now;
    public List<InvoiceLineFormModel> Items { get; } = new() { new InvoiceLineFormModel() };
    public long Gross => Items.Aggregate(0L, (sum, item) => SaturatingAdd(sum, item.Gross));
    public long AfterDiscount => Items.Aggregate(0L, (sum, item) => SaturatingAdd(sum, item.AfterDiscount));
    public long Vat => Items.Aggregate(0L, (sum, item) => SaturatingAdd(sum, item.Vat));
    public long Total => SaturatingAdd(AfterDiscount, Vat);
    private static long SaturatingAdd(long left, long right) => right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;
    public void AddItem() => Items.Add(new InvoiceLineFormModel());
    public void RemoveItem(InvoiceLineFormModel item) { if (Items.Count > 1) Items.Remove(item); }
    public string? Error { get; private set; }
    public IReadOnlyList<ApiError> ValidationErrors { get; private set; } = Array.Empty<ApiError>();
    public void SetError(string? message) => Error = message;
    public void Load(InvoiceViewModel invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        InvoiceNumber = invoice.InvoiceNumber; BuyerName = invoice.BuyerName; IssueDate = invoice.IssueDate; Items.Clear();
        foreach (var item in invoice.Items) Items.Add(new InvoiceLineFormModel { Sstid=item.Sstid,Description=item.Description,MeasuringUnitCode=item.MeasuringUnitCode,Quantity=item.Quantity,UnitPrice=item.UnitPrice,Discount=item.DiscountAmount,VatRate=item.VatRate });
        if (Items.Count == 0) Items.Add(new InvoiceLineFormModel());
    }
    public async Task<InvoiceViewModel?> SaveAsync(InvoiceService service, int? id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        Error = null; ValidationErrors = Array.Empty<ApiError>();
        var request = new InvoiceWriteRequest
        {
            InvoiceNumber = InvoiceNumber,
            BuyerName = BuyerName,
            Type = InvoiceType.Original,
            PatternCode = 1,
            SubjectCode = 1,
            IssueDate = IssueDate ?? DateTimeOffset.UtcNow,
            Items = Items.Select(item => new InvoiceItemWriteRequest { Sstid=item.Sstid,Description=item.Description,MeasuringUnitCode=item.MeasuringUnitCode,Quantity=item.Quantity,UnitPrice=item.UnitPrice,DiscountAmount=item.Discount,VatRate=item.VatRate }).ToArray()
        };
        try { return id is null ? await service.CreateAsync(request, ct).ConfigureAwait(false) : await service.UpdateAsync(id.Value, request, ct).ConfigureAwait(false); }
        catch (ApiClientException ex) { Error = ex.Message; ValidationErrors = ex.Errors; return null; }
    }
}

public sealed class InvoiceLineFormModel
{
    public string Sstid { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MeasuringUnitCode { get; set; } = "EA";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public long Discount { get; set; }
    public decimal VatRate { get; set; }
    public long Gross
    {
        get { try { var amount = Math.Round(Math.Max(0m, Quantity * UnitPrice), 0, MidpointRounding.AwayFromZero); return amount >= long.MaxValue ? long.MaxValue : checked((long)amount); } catch (OverflowException) { return long.MaxValue; } }
    }
    public long AfterDiscount => Gross - Math.Clamp(Discount, 0, Gross);
    public long Vat
    {
        get { try { if (VatRate <= 0) return 0; var amount = Math.Round(AfterDiscount * VatRate / 100m, 0, MidpointRounding.AwayFromZero); return amount >= long.MaxValue ? long.MaxValue : checked((long)amount); } catch (OverflowException) { return long.MaxValue; } }
    }
}

public sealed class MasterDataViewModel(CustomerService customers, ProductService products)
{
    public PagedResult<CustomerViewModel>? Customers { get; private set; }
    public PagedResult<ProductViewModel>? Products { get; private set; }
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public async Task LoadCustomersAsync(int page = 1, string? search = null, CancellationToken ct = default)
    { IsLoading = true; Error = null; try { Customers = await customers.ListAsync(page, search, ct).ConfigureAwait(false); } catch (Exception ex) { Error = ex.Message; } finally { IsLoading = false; } }
    public async Task LoadProductsAsync(int page = 1, string? search = null, CancellationToken ct = default)
    { IsLoading = true; Error = null; try { Products = await products.ListAsync(page, search, ct).ConfigureAwait(false); } catch (Exception ex) { Error = ex.Message; } finally { IsLoading = false; } }
}

public sealed class MoadianSettingsViewModel(MoadianSettingsService service)
{
    public MoadianSettingViewModel? Settings { get; private set; }
    public MoadianCredentialViewModel? Credential { get; private set; }
    public string? Error { get; private set; }
    public bool IsLoading { get; private set; }
    public async Task LoadAsync(CancellationToken ct = default)
    { IsLoading = true; Error = null; try { Settings = await service.GetAsync(ct).ConfigureAwait(false); Credential = await service.GetCredentialAsync(ct).ConfigureAwait(false); } catch (Exception ex) { Error = ex.Message; } finally { IsLoading = false; } }
}

public sealed class AdministrationViewModel(AdministrationService service)
{
    public IReadOnlyList<ClientUserRow> Users { get; private set; } = Array.Empty<ClientUserRow>();
    public IReadOnlyList<ClientRoleRow> Roles { get; private set; } = Array.Empty<ClientRoleRow>();
    public IReadOnlyList<ClientApiKeyRow> ApiKeys { get; private set; } = Array.Empty<ClientApiKeyRow>();
    public string? OneTimeApiKey { get; private set; }
    public string? Error { get; private set; }
    public bool IsLoading { get; private set; }
    public async Task LoadAsync(CancellationToken ct = default)
    { IsLoading = true; Error = null; try { Users = await service.UsersAsync(ct).ConfigureAwait(false); Roles = await service.RolesAsync(ct).ConfigureAwait(false); ApiKeys = await service.ApiKeysAsync(ct).ConfigureAwait(false); } catch (Exception ex) { Error = ex.Message; } finally { IsLoading = false; } }
    public async Task CreateKeyAsync(ClientApiKeyRequest request, CancellationToken ct = default)
    { try { var created = await service.CreateApiKeyAsync(request, ct).ConfigureAwait(false); OneTimeApiKey = created.ApiKey; await LoadAsync(ct).ConfigureAwait(false); } catch (Exception ex) { Error = ex.Message; } }
    public async Task CreateUserAsync(ClientUserRequest request, CancellationToken ct = default)
    { try { await service.CreateUserAsync(request, ct).ConfigureAwait(false); await LoadAsync(ct).ConfigureAwait(false); } catch (Exception ex) { Error = ex.Message; } }
    public async Task CreateRoleAsync(ClientRoleRequest request, CancellationToken ct = default)
    { try { await service.CreateRoleAsync(request, ct).ConfigureAwait(false); await LoadAsync(ct).ConfigureAwait(false); } catch (Exception ex) { Error = ex.Message; } }
    public async Task RevokeKeyAsync(int id, CancellationToken ct = default)
    { try { await service.RevokeApiKeyAsync(id, ct).ConfigureAwait(false); await LoadAsync(ct).ConfigureAwait(false); } catch (Exception ex) { Error = ex.Message; } }
    public void DismissOneTimeKey() => OneTimeApiKey = null;
}

public sealed class ReportViewModel(ReportService service)
{
    public DateOnly From { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddMonths(-1));
    public DateOnly To { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public byte[]? File { get; private set; }
    public string? Error { get; private set; }
    public void ClearFile() { if (File is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(File); File = null; }
    public async Task DownloadAsync(CancellationToken ct = default)
    { Error = null; try { File = await service.DownloadInvoicesAsync(From, To, ct).ConfigureAwait(false); } catch (Exception ex) { Error = ex.Message; } }
}

public sealed record ClientUserRow(int Id, string UserName, string FullName, bool IsActive, string RoleName);
public sealed record ClientUserRequest(string UserName, string FullName, string Password, int RoleId);
public sealed record ClientRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);
public sealed record ClientRoleRow(int Id, string Name, IReadOnlyList<string> Permissions);
public sealed record ClientApiKeyRow(int Id, string Name, string KeyPrefix, ApiScope Scopes, bool IsActive, DateTimeOffset? ExpiresAt);
public sealed record ClientApiKeyRequest(string Name, ApiScope Scopes, int RequestsPerMinute, DateTimeOffset? ExpiresAt);
public sealed record ClientApiKeyCreated(ClientApiKeyRow Client, string ApiKey);
