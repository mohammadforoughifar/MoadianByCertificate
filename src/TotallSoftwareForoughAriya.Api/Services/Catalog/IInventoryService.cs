using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services;

/// <summary>
/// قرارداد (اینترفیس) سرویس اصلی برنامه: کالا، انبار، طرف حساب، خرید/فروش،
/// موجودی، کاردکس، نقطه سفارش و داشبورد.
/// کنترلرها فقط با این اینترفیس کار می‌کنند، نه با پیاده‌سازی.
/// </summary>
public interface IInventoryService
{
    // ---------------- تنظیمات ----------------
    Task<AppSettings> GetSettingsAsync();
    Task<AppSettings> SaveSettingsAsync(AppSettings dto);

    // ---------------- معرف (بازاریاب) ----------------
    Task<List<Referrer>> GetReferrersAsync(bool activeOnly = false, Paging.Request? pagination = null, string? search = null);
    Task<Referrer> SaveReferrerAsync(Referrer dto);
    Task DeleteReferrerAsync(int id);

    // ---------------- کیف پول و پرداخت معرف ----------------
    /// <summary>فهرست کیف پول معرف‌ها با فیلتر و مرتب‌سازی (sortBy: name|commission|paid|balance، desc)</summary>
    Task<List<Referrer>> GetReferrerWalletsAsync(string? search, string? sortBy, bool desc, Paging.Request? pagination = null);
    Task<List<ReferrerPayment>> GetReferrerPaymentsAsync(int? referrerId, Paging.Request? pagination = null);
    Task<ReferrerPayment> AddReferrerPaymentAsync(ReferrerPayment dto);
    Task DeleteReferrerPaymentAsync(int id);

    // ---------------- گروه کالا (درختی) ----------------
    Task<List<ProductCategory>> GetCategoriesAsync(bool activeOnly = false, Paging.Request? pagination = null);
    Task<ProductCategory> SaveCategoryAsync(ProductCategory dto);
    Task DeleteCategoryAsync(int id);

    // ---------------- واحد شمارش ----------------
    Task<List<MeasureUnit>> GetUnitsAsync(bool activeOnly = false, Paging.Request? pagination = null, string? search = null);
    Task<MeasureUnit> SaveUnitAsync(MeasureUnit dto);
    Task DeleteUnitAsync(int id);

    // ---------------- ورود اکسل کالا ----------------
    Task<ExcelImportResult> ImportProductsAsync(Stream excelStream, int? warehouseId = null);
    byte[] BuildProductTemplate();

    // ---------------- کالا ----------------
    /// <summary>فهرست کالاها؛ warehouseId = فقط کالاهای آن انبار (کالاهای بدون انبار و خدمات همیشه می‌آیند).</summary>
    Task<PagedResult<Product>> GetProductsAsync(string? search, bool belowReorderOnly, int page, int pageSize, int? warehouseId = null);
    Task<List<LookupItem>> GetProductLookupsAsync(Paging.Request? pagination = null);
    Task<Product?> GetProductAsync(int id);
    Task<Product> SaveProductAsync(Product dto);
    Task DeleteProductAsync(int id);

    // ---------------- انبار ----------------
    Task<List<Warehouse>> GetWarehousesAsync(Paging.Request? pagination = null);
    Task<Warehouse> SaveWarehouseAsync(Warehouse dto);
    Task DeleteWarehouseAsync(int id);

    // ---------------- طرف حساب ----------------
    /// <param name="referrerScope">محدودکردن به پروندهٔ یک معرف (ویزیتور بدون مجوز دیدن کل مشتریان).</param>
    Task<List<Party>> GetPartiesAsync(PartyType type, Paging.Request? pagination = null, int? referrerScope = null);
    Task<Party> SavePartyAsync(Party dto);
    Task DeletePartyAsync(int id);

    // ---------------- موجودی ----------------
    Task<PagedResult<StockItem>> GetStockAsync(int? warehouseId, string? search, bool belowOnly, int page, int pageSize);
    Task AdjustStockAsync(AdjustmentCommand cmd);

    // ---------------- خرید و فروش ----------------
    Task<Order> CreateOrderAsync(OrderCommand cmd);
    Task<Order> UpdateOrderAsync(int id, OrderCommand cmd);
    Task<Order?> GetOrderAsync(int id);

    // ---------------- داشبورد ادمین و پرداخت ----------------
    Task<AdminDashboard> GetAdminDashboardAsync();
    Task ClearChequeAsync(int chequeId);
    Task BounceChequeAsync(int chequeId, string? reason);
    Task ResetChequeAsync(int chequeId);
    Task PayInstallmentAsync(int installmentId);
    Task UnpayInstallmentAsync(int installmentId);
    Task SettleCreditAsync(int transactionId, decimal amount);

    // ---------------- مدیریت چک و اقساط فروشگاهی ----------------
    Task<ShopCreditSummary> GetShopCreditSummaryAsync();
    Task<List<ShopChequeItem>> GetShopChequesAsync(ChequeStatus? status, TransactionType? txnType, string? search);
    Task<List<ShopInstallmentItem>> GetShopInstallmentsAsync(bool? isPaid, string? search);

    /// <summary>کالاهای موجود برای پنل معرف (نیازمند دسترسی CanViewProducts).</summary>
    Task<List<ReferrerProductItem>> GetReferrerProductsAsync(int referrerId, string? search, bool bypassFlag = false, Paging.Request? pagination = null);
    Task<PagedResult<Order>> GetOrdersAsync(TransactionType type, DateTime? from, DateTime? to, int? partyId, int? warehouseId, int page, int pageSize);
    Task DeleteOrderAsync(int id);
    Task<decimal> SuggestPriceAsync(int productId, TransactionType type);

    /// <summary>آخرین سند خریدی که شامل این کالاست (برای نمایش در فرم فروش).</summary>
    Task<Order?> GetLastPurchaseAsync(int productId);

    // ---------------- گزارش‌ها ----------------
    Task<List<KardexRow>> GetKardexAsync(int productId, int? warehouseId, DateTime? from, DateTime? to, Paging.Request? pagination = null);
    Task<List<ReorderItem>> GetReorderAsync(int? warehouseId, Paging.Request? pagination = null);

    // ---------------- داشبورد ----------------
    Task<DashboardSummary> GetDashboardAsync();
    Task<List<RecentActivity>> GetRecentAsync(int count);
}
