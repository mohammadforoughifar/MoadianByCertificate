using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Stocktaking;

/// <summary>
/// قرارداد سرویس انبارگردانی و بارکد: مدیریت بارکدها، اسکن،
/// دوره‌های شمارش، ثبت شمارش و اعمال مغایرت روی موجودی.
/// </summary>
public interface IStocktakingService
{
    // ---------- بارکد ----------
    Task<List<BcdBarcode>> GetBarcodesAsync(int? productId = null, string? search = null, Paging.Request? pagination = null);
    Task<BcdBarcode> SaveBarcodeAsync(BcdBarcode dto);
    Task DeleteBarcodeAsync(int id);

    /// <summary>ساخت بارکد EAN-13 داخلی برای کالاهایی که بارکد ندارند.</summary>
    Task<int> GenerateBarcodesAsync(List<int> productIds, string prefix);

    /// <summary>یافتن کالا از روی بارکد (یا کد/نام کالا به‌عنوان جایگزین).</summary>
    Task<BcdScanResult> ScanAsync(string code, int warehouseId);

    /// <summary>برچسب‌های آماده چاپ برای مجموعه‌ای از کالاها.</summary>
    Task<List<BcdLabel>> GetLabelsAsync(List<int> productIds);

    // ---------- دوره انبارگردانی ----------
    Task<PagedResult<StkSession>> GetSessionsAsync(int? warehouseId, StocktakeStatus? status,
        string? search, DateTime? from, DateTime? to, int page, int pageSize);
    Task<StkSession?> GetSessionAsync(int id, bool withLines = true);
    Task<StkSession> NewSessionAsync(int warehouseId);
    Task<StkSession> SaveSessionAsync(StkSession dto, string? user);

    /// <summary>قفل کردن موجودی سیستم و ساخت لیست شمارش — ورود به مرحله «در حال شمارش».</summary>
    Task<StkSession> StartCountingAsync(int id, string? user);

    /// <summary>پایان شمارش — ورود به مرحله «بررسی مغایرت».</summary>
    Task<StkSession> FinishCountingAsync(int id, string? user);

    /// <summary>بازگشت از بررسی به شمارش.</summary>
    Task<StkSession> ReopenCountingAsync(int id, string? user);

    /// <summary>صدور و قطعی کردن اسناد اصلاح موجودی (رسید اضافی و حواله کسری).</summary>
    Task<StkSession> ApplyAsync(int id, string? user);

    /// <summary>برگشت اعمال — حذف اسناد اصلاح و بازگشت به مرحله بررسی.</summary>
    Task<StkSession> UnapplyAsync(int id, string? user);

    Task<StkSession> CancelSessionAsync(int id, string? user);
    Task DeleteSessionAsync(int id);

    // ---------- شمارش ----------
    Task<StkCountResult> CountAsync(StkCountCommand cmd, string? user);

    /// <summary>پاک کردن شمارش یک قلم (بازگشت به حالت «شمرده نشده»).</summary>
    Task<StkCountResult> ClearLineAsync(int sessionId, int lineId, string? user);

    /// <summary>حذف یک قلم از لیست شمارش (فقط اقلامی که اسکن‌محور اضافه شده‌اند).</summary>
    Task DeleteLineAsync(int sessionId, int lineId);

    // ---------- گزارش ----------
    Task<StkDiffResult> GetDiffAsync(int sessionId, bool onlyDiff);
}
