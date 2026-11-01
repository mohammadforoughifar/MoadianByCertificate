using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>ارتباط کلاینت با API ماژول ویزیتوری.</summary>
public interface ISalesVisitApi
{
    Task<VisActorInfo> MeAsync();
    Task<List<VisOrderDto>> ListAsync(VisOrderStatus? status = null, string? search = null,
        DateTime? from = null, DateTime? to = null);
    Task<VisOrderDto?> GetAsync(int id);
    Task<List<VisApprovalItem>> ApprovalsAsync();
    Task<List<VisOrderDto>> FinanceQueueAsync();
    Task<VisQueueStats> StatsAsync();

    Task<VisOrderDto> SaveAsync(VisOrderCommand cmd);
    Task<VisOrderDto> SubmitAsync(int id);
    /// <summary>تأیید سفارش با تاریخ نهایی تحویل (null = پذیرش تاریخ ویزیتور).</summary>
    Task<VisOrderDto> ApproveAsync(int id, string? comment, DateTime? finalDeliveryDate = null);
    Task<VisOrderDto> ReturnAsync(int id, string comment);
    Task<VisOrderDto> RejectAsync(int id, string comment);
    Task<VisOrderDto> CancelAsync(int id, string? comment);

    /// <summary>
    /// تأیید مالی: ثبت فروش فروشگاه از سفارش تأییدشده (نقد/چک/اقساط + سند خودکار).
    /// جایگزین «ساخت پیش‌نویس فاکتور» و «همگام‌سازی» شده است.
    /// </summary>
    Task<VisOrderDto> FinanceApproveAsync(int id, VisFinanceCommand cmd);

    Task<VisOrderDto> InvoiceDraftAsync(int id);
    Task<VisOrderDto> SyncInvoiceAsync(int id);

    Task<List<VisCustomerLite>> CustomersAsync(string? search);
    Task<VisCustomerLite> CreateCustomerAsync(VisCustomerLite input);
    Task<List<VisProductLite>> ProductsAsync(int? warehouseId, string? search);
    Task<List<VisWarehouseLite>> WarehousesAsync();
}

public class SalesVisitApi : ISalesVisitApi
{
    private readonly IApiClient _api;
    public SalesVisitApi(IApiClient api) => _api = api;

    public Task<VisActorInfo> MeAsync() => _api.GetAsync<VisActorInfo>("api/sales-visit/me");

    public Task<List<VisOrderDto>> ListAsync(VisOrderStatus? status = null, string? search = null,
        DateTime? from = null, DateTime? to = null)
    {
        var url = "api/sales-visit?";
        if (status is not null) url += $"status={(int)status}&";
        if (from.HasValue) url += $"from={from:yyyy-MM-dd}&";
        if (to.HasValue) url += $"to={to:yyyy-MM-dd}&";
        url += $"search={Uri.EscapeDataString(search ?? "")}";
        return _api.GetAsync<List<VisOrderDto>>(url);
    }

    public Task<VisOrderDto?> GetAsync(int id) => _api.GetAsync<VisOrderDto?>($"api/sales-visit/{id}");

    public Task<List<VisApprovalItem>> ApprovalsAsync() =>
        _api.GetAsync<List<VisApprovalItem>>("api/sales-visit/approvals");

    public Task<List<VisOrderDto>> FinanceQueueAsync() =>
        _api.GetAsync<List<VisOrderDto>>("api/sales-visit/finance-queue");

    public Task<VisQueueStats> StatsAsync() => _api.GetAsync<VisQueueStats>("api/sales-visit/stats");

    public Task<VisOrderDto> SaveAsync(VisOrderCommand cmd) =>
        _api.PostAsync<VisOrderDto>("api/sales-visit", cmd);

    public Task<VisOrderDto> SubmitAsync(int id) =>
        _api.PostAsync<VisOrderDto>($"api/sales-visit/{id}/submit");

    public Task<VisOrderDto> ApproveAsync(int id, string? comment, DateTime? finalDeliveryDate = null) =>
        _api.PostAsync<VisOrderDto>($"api/sales-visit/{id}/approve", new VisOrderDecision
        {
            Id = id,
            Comment = comment,
            FinalDeliveryDate = finalDeliveryDate?.Date
        });

    public Task<VisOrderDto> ReturnAsync(int id, string comment) =>
        _api.PostAsync<VisOrderDto>($"api/sales-visit/{id}/return", new VisOrderDecision { Id = id, Comment = comment });

    public Task<VisOrderDto> RejectAsync(int id, string comment) =>
        _api.PostAsync<VisOrderDto>($"api/sales-visit/{id}/reject", new VisOrderDecision { Id = id, Comment = comment });

    public Task<VisOrderDto> CancelAsync(int id, string? comment) =>
        _api.PostAsync<VisOrderDto>($"api/sales-visit/{id}/cancel", new VisOrderDecision { Id = id, Comment = comment });

    public Task<VisOrderDto> FinanceApproveAsync(int id, VisFinanceCommand cmd) =>
        _api.PostAsync<VisOrderDto>($"api/sales-visit/{id}/finance-approve", cmd);

    public Task<VisOrderDto> InvoiceDraftAsync(int id) =>
        _api.PostAsync<VisOrderDto>($"api/sales-visit/{id}/invoice-draft");

    public Task<VisOrderDto> SyncInvoiceAsync(int id) =>
        _api.PostAsync<VisOrderDto>($"api/sales-visit/{id}/sync-invoice");

    public Task<List<VisCustomerLite>> CustomersAsync(string? search) =>
        _api.GetAsync<List<VisCustomerLite>>($"api/sales-visit/customers?search={Uri.EscapeDataString(search ?? "")}");

    public Task<VisCustomerLite> CreateCustomerAsync(VisCustomerLite input) =>
        _api.PostAsync<VisCustomerLite>("api/sales-visit/customers", input);

    public Task<List<VisProductLite>> ProductsAsync(int? warehouseId, string? search) =>
        _api.GetAsync<List<VisProductLite>>(
            $"api/sales-visit/products?warehouseId={warehouseId ?? 0}&search={Uri.EscapeDataString(search ?? "")}");

    public Task<List<VisWarehouseLite>> WarehousesAsync() =>
        _api.GetAsync<List<VisWarehouseLite>>("api/sales-visit/warehouses");
}

/// <summary>کارتابل یکپارچه (سمت کلاینت).</summary>
public interface ICartableApi
{
    Task<CartableResponseDto> MineAsync();
    Task<int> CountAsync();
}

public class CartableApi : ICartableApi
{
    private readonly IApiClient _api;
    public CartableApi(IApiClient api) => _api = api;

    public Task<CartableResponseDto> MineAsync() => _api.GetAsync<CartableResponseDto>("api/cartable/me");

    public async Task<int> CountAsync()
    {
        var res = await _api.GetAsync<CartableCount>("api/cartable/me/count");
        return res?.Actionable ?? 0;
    }

    private class CartableCount { public int Actionable { get; set; } }
}

/// <summary>متن/رنگ‌های نمایشی مشترک ماژول ویزیتوری (یک منبع، تا در چند صفحه تکرار نشود).</summary>
public static class VisUi
{
    public static string StatusText(VisOrderStatus s) => s switch
    {
        VisOrderStatus.Draft => "پیش‌نویس",
        VisOrderStatus.Submitted => "در انتظار تأیید",
        VisOrderStatus.Approved => "تأییدشده",
        VisOrderStatus.Returned => "برگشتی برای اصلاح",
        VisOrderStatus.Rejected => "ردشده",
        VisOrderStatus.Invoiced => "فروش‌شده",
        VisOrderStatus.Closed => "بسته‌شده",
        VisOrderStatus.Canceled => "ابطال‌شده",
        _ => s.ToString()
    };

    public static string StatusColor(VisOrderStatus s) => s switch
    {
        VisOrderStatus.Draft => "secondary",
        VisOrderStatus.Submitted => "warning",
        VisOrderStatus.Approved => "primary",
        VisOrderStatus.Returned => "orange",
        VisOrderStatus.Rejected => "danger",
        VisOrderStatus.Invoiced => "success",
        VisOrderStatus.Closed => "dark",
        VisOrderStatus.Canceled => "secondary",
        _ => "secondary"
    };

    public static string PaymentText(PaymentMethod m) => m.ToString() switch
    {
        "Cash" => "نقدی",
        "Credit" => "نسیه",
        "Cheque" => "چک",
        "Installment" => "اقساطی",
        "Mixed" => "ترکیبی",
        _ => m.ToString()
    };

    /// <summary>کلاس نشان وضعیت — رنگ‌ها با CSS داخل صفحه‌ها تعریف می‌شوند.</summary>
    public static string BadgeCss(VisOrderStatus s) => $"vis-badge vis-{StatusColor(s)}";
}
