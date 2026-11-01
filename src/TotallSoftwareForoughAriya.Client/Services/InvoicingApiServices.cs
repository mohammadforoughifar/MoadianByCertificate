using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// پیاده‌سازی سرویس‌های ماژول فاکتور — تنها نقطه‌ی ساخت مسیرهای API
// =====================================================================

/// <summary>پیاده‌سازی سرویس فاکتورها.</summary>
public class FacInvoiceService : IFacInvoiceService
{
    private readonly IApiClient _api;
    public FacInvoiceService(IApiClient api) => _api = api;

    public Task<PagedResult<FacInvoice>> GetAllAsync(InvoiceKind? kind = null, InvoiceStatus? status = null,
        int? partyId = null, int? warehouseId = null, string? search = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 15)
    {
        var q = $"api/fac/invoices?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        if (kind is not null) q += $"&kind={kind}";
        if (status is not null) q += $"&status={status}";
        if (partyId is > 0) q += $"&partyId={partyId}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        if (from is not null) q += $"&from={from:yyyy-MM-dd}";
        if (to is not null) q += $"&to={to:yyyy-MM-dd}";
        return _api.GetAsync<PagedResult<FacInvoice>>(q);
    }

    public Task<FacInvoice> GetAsync(int id)
        => _api.GetAsync<FacInvoice>($"api/fac/invoices/{id}");

    public Task<FacInvoice> NewAsync(InvoiceKind kind)
        => _api.GetAsync<FacInvoice>($"api/fac/invoices/new?kind={kind}");

    public Task<FacInvoiceLine> BuildLineAsync(int productId, InvoiceKind kind, int warehouseId)
        => _api.GetAsync<FacInvoiceLine>($"api/fac/invoices/line?productId={productId}&kind={kind}&warehouseId={warehouseId}");

    public Task<FacInvoice> SaveAsync(FacInvoice invoice)
        => _api.PostAsync<FacInvoice>("api/fac/invoices", invoice);

    public Task<FacInvoice> ConfirmAsync(int id)
        => _api.PostAsync<FacInvoice>($"api/fac/invoices/{id}/confirm", new { });

    public Task<FacInvoice> UnconfirmAsync(int id)
        => _api.PostAsync<FacInvoice>($"api/fac/invoices/{id}/unconfirm", new { });

    public Task<FacInvoice> CancelAsync(int id)
        => _api.PostAsync<FacInvoice>($"api/fac/invoices/{id}/cancel", new { });

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/fac/invoices/{id}");
}

/// <summary>پیاده‌سازی پیکربندی انواع فاکتور.</summary>
public class FacRuleService : IFacRuleService
{
    private readonly IApiClient _api;
    public FacRuleService(IApiClient api) => _api = api;

    public Task<List<FacRule>> GetAllAsync()
        => _api.GetAsync<List<FacRule>>("api/fac/rules");

    public Task<FacRule> SaveAsync(FacRule rule)
        => _api.PostAsync<FacRule>("api/fac/rules", rule);
}

/// <summary>پیاده‌سازی گزارش‌های فروش و خرید.</summary>
public class FacReportService : IFacReportService
{
    private readonly IApiClient _api;
    public FacReportService(IApiClient api) => _api = api;

    public Task<FacSummaryResult> GetSummaryAsync(InvoiceKind kind, string groupBy = "month",
        DateTime? from = null, DateTime? to = null)
    {
        var q = $"api/fac/reports/summary?kind={kind}&groupBy={groupBy}";
        if (from is not null) q += $"&from={from:yyyy-MM-dd}";
        if (to is not null) q += $"&to={to:yyyy-MM-dd}";
        return _api.GetAsync<FacSummaryResult>(q);
    }

    public Task<FacDashboard> GetDashboardAsync(DateTime? from = null, DateTime? to = null)
    {
        var q = "api/fac/reports/dashboard";
        var sep = "?";
        if (from is not null) { q += $"{sep}from={from:yyyy-MM-dd}"; sep = "&"; }
        if (to is not null) q += $"{sep}to={to:yyyy-MM-dd}";
        return _api.GetAsync<FacDashboard>(q);
    }
}
