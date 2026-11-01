using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// قراردادهای سرویس‌های ماژول انبارگردانی و بارکد (سمت کلاینت)
// =====================================================================

/// <summary>بارکدهای کالا، اسکن و برچسب چاپی.</summary>
public interface IBcdBarcodeService
{
    Task<List<BcdBarcode>> GetAllAsync(int? productId = null, string? search = null);

    /// <summary>یافتن کالا از روی بارکد، کد کالا یا بارکد قدیمی کالا.</summary>
    Task<BcdScanResult> ScanAsync(string code, int warehouseId = 0);

    Task<BcdBarcode> SaveAsync(BcdBarcode barcode);

    /// <summary>تولید بارکد EAN-13 داخلی برای کالاهای بدون بارکد — تعداد ساخته‌شده را برمی‌گرداند.</summary>
    Task<int> GenerateAsync(List<int> productIds, string prefix = "200");

    Task<List<BcdLabel>> GetLabelsAsync(List<int> productIds);

    Task DeleteAsync(int id);
}

/// <summary>دوره‌های انبارگردانی، شمارش و گزارش مغایرت.</summary>
public interface IStkSessionService
{
    Task<PagedResult<StkSession>> GetAllAsync(int? warehouseId = null, StocktakeStatus? status = null,
        string? search = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 15);

    Task<StkSession> GetAsync(int id);
    Task<StkSession> NewAsync(int warehouseId = 0);
    Task<StkSession> SaveAsync(StkSession session);

    // ---------- چرخه وضعیت ----------
    Task<StkSession> StartAsync(int id);
    Task<StkSession> FinishAsync(int id);
    Task<StkSession> ReopenAsync(int id);
    Task<StkSession> ApplyAsync(int id);
    Task<StkSession> UnapplyAsync(int id);
    Task<StkSession> CancelAsync(int id);
    Task DeleteAsync(int id);

    // ---------- شمارش ----------
    Task<StkCountResult> CountAsync(StkCountCommand cmd);
    Task<StkCountResult> ClearLineAsync(int sessionId, int lineId);
    Task DeleteLineAsync(int sessionId, int lineId);

    // ---------- گزارش ----------
    Task<StkDiffResult> GetDiffAsync(int sessionId, bool onlyDiff = false);
}
