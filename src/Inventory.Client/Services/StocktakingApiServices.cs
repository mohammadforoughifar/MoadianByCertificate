using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// پیاده‌سازی سرویس‌های ماژول انبارگردانی و بارکد
// تنها نقطه‌ی ساخت مسیرهای api/stk/…
// =====================================================================

/// <summary>پیاده‌سازی سرویس بارکد.</summary>
public class BcdBarcodeService : IBcdBarcodeService
{
    private readonly IApiClient _api;
    public BcdBarcodeService(IApiClient api) => _api = api;

    public Task<List<BcdBarcode>> GetAllAsync(int? productId = null, string? search = null)
    {
        var q = $"api/stk/barcodes?search={Uri.EscapeDataString(search ?? "")}";
        if (productId is > 0) q += $"&productId={productId}";
        return _api.GetAsync<List<BcdBarcode>>(q);
    }

    public Task<BcdScanResult> ScanAsync(string code, int warehouseId = 0)
        => _api.GetAsync<BcdScanResult>(
            $"api/stk/barcodes/scan?code={Uri.EscapeDataString(code ?? "")}&warehouseId={warehouseId}");

    public Task<BcdBarcode> SaveAsync(BcdBarcode barcode)
        => _api.PostAsync<BcdBarcode>("api/stk/barcodes", barcode);

    public Task<int> GenerateAsync(List<int> productIds, string prefix = "200")
        => _api.PostAsync<int>("api/stk/barcodes/generate",
            new { ProductIds = productIds, Prefix = prefix });

    public Task<List<BcdLabel>> GetLabelsAsync(List<int> productIds)
        => _api.PostAsync<List<BcdLabel>>("api/stk/barcodes/labels", productIds);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/stk/barcodes/{id}");
}

/// <summary>پیاده‌سازی سرویس دوره انبارگردانی.</summary>
public class StkSessionService : IStkSessionService
{
    private readonly IApiClient _api;
    public StkSessionService(IApiClient api) => _api = api;

    public Task<PagedResult<StkSession>> GetAllAsync(int? warehouseId = null, StocktakeStatus? status = null,
        string? search = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 15)
    {
        var q = $"api/stk/sessions?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        if (warehouseId is > 0) q += $"&warehouseId={warehouseId}";
        if (status is not null) q += $"&status={status}";
        if (from is not null) q += $"&from={from:yyyy-MM-dd}";
        if (to is not null) q += $"&to={to:yyyy-MM-dd}";
        return _api.GetAsync<PagedResult<StkSession>>(q);
    }

    public Task<StkSession> GetAsync(int id)
        => _api.GetAsync<StkSession>($"api/stk/sessions/{id}");

    public Task<StkSession> NewAsync(int warehouseId = 0)
        => _api.GetAsync<StkSession>($"api/stk/sessions/new?warehouseId={warehouseId}");

    public Task<StkSession> SaveAsync(StkSession session)
        => _api.PostAsync<StkSession>("api/stk/sessions", session);

    // ---------- چرخه وضعیت ----------
    public Task<StkSession> StartAsync(int id)
        => _api.PostAsync<StkSession>($"api/stk/sessions/{id}/start", new { });

    public Task<StkSession> FinishAsync(int id)
        => _api.PostAsync<StkSession>($"api/stk/sessions/{id}/finish", new { });

    public Task<StkSession> ReopenAsync(int id)
        => _api.PostAsync<StkSession>($"api/stk/sessions/{id}/reopen", new { });

    public Task<StkSession> ApplyAsync(int id)
        => _api.PostAsync<StkSession>($"api/stk/sessions/{id}/apply", new { });

    public Task<StkSession> UnapplyAsync(int id)
        => _api.PostAsync<StkSession>($"api/stk/sessions/{id}/unapply", new { });

    public Task<StkSession> CancelAsync(int id)
        => _api.PostAsync<StkSession>($"api/stk/sessions/{id}/cancel", new { });

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/stk/sessions/{id}");

    // ---------- شمارش ----------
    public Task<StkCountResult> CountAsync(StkCountCommand cmd)
        => _api.PostAsync<StkCountResult>("api/stk/sessions/count", cmd);

    public Task<StkCountResult> ClearLineAsync(int sessionId, int lineId)
        => _api.PostAsync<StkCountResult>($"api/stk/sessions/{sessionId}/lines/{lineId}/clear", new { });

    public Task DeleteLineAsync(int sessionId, int lineId)
        => _api.DeleteAsync($"api/stk/sessions/{sessionId}/lines/{lineId}");

    // ---------- گزارش ----------
    public Task<StkDiffResult> GetDiffAsync(int sessionId, bool onlyDiff = false)
        => _api.GetAsync<StkDiffResult>(
            $"api/stk/reports/diff?sessionId={sessionId}&onlyDiff={onlyDiff.ToString().ToLower()}");
}
