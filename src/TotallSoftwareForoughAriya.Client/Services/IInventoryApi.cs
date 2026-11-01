using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// اینترفیس‌های سرویس‌های سمت کلاینت — صفحات فقط با این قراردادها کار می‌کنند
// و هیچ صفحه‌ای مستقیماً URL یا HttpClient صدا نمی‌زند.
// =====================================================================

/// <summary>سرویس تنظیمات برنامه.</summary>
public interface ISettingsService
{
    Task<AppSettings> GetAsync();
    Task<AppSettings> SaveAsync(AppSettings settings);
}

/// <summary>پروفایل شرکت/فروشگاه (تعریف شرکت) + تنظیمات چاپ فاکتور.</summary>
public interface ICompanyProfileService
{
    Task<CompanyProfileDto> GetAsync();
    Task<CompanyProfileDto> SaveAsync(CompanyProfileDto profile);
}

/// <summary>سرویس معرف‌ها (بازاریاب‌ها) + کیف پول و پرداخت‌ها.</summary>
public interface IReferrerService
{
    Task<PagedResult<Referrer>> GetPagedAsync(int skip, int take, bool activeOnly = false, string? search = null);
    Task<List<Referrer>> GetAllAsync(bool activeOnly = false);
    Task<Referrer> SaveAsync(Referrer referrer);
    Task DeleteAsync(int id);

    /// <summary>کیف پول معرف‌ها با جستجو و مرتب‌سازی (sortBy: name|commission|paid|balance).</summary>
    Task<List<Referrer>> GetWalletsAsync(string? search = null, string sortBy = "name", bool desc = false);
    Task<List<ReferrerPayment>> GetPaymentsAsync(int? referrerId = null);
    Task<ReferrerPayment> AddPaymentAsync(ReferrerPayment payment);
    Task DeletePaymentAsync(int id);
}

/// <summary>سرویس گروه‌های کالا (درختی).</summary>
public interface ICategoryService
{
    Task<List<ProductCategory>> GetAllAsync(bool activeOnly = false);
    Task<ProductCategory> SaveAsync(ProductCategory category);
    Task DeleteAsync(int id);
}

/// <summary>سرویس واحدهای شمارش.</summary>
public interface IUnitService
{
    Task<PagedResult<MeasureUnit>> GetPagedAsync(int skip, int take, bool activeOnly = false, string? search = null);
    Task<List<MeasureUnit>> GetAllAsync(bool activeOnly = false);
    Task<MeasureUnit> SaveAsync(MeasureUnit unit);
    Task DeleteAsync(int id);
}

/// <summary>سرویس کالاها.</summary>
public interface IProductService
{
    Task<PagedResult<Product>> GetProductsAsync(string? search = null, bool belowOnly = false, int page = 1, int pageSize = 20, int? warehouseId = null);
    Task<List<LookupItem>> GetLookupsAsync();
    Task<Product?> GetByIdAsync(int id);
    Task<Product> SaveAsync(Product product);
    Task DeleteAsync(int id);

    /// <summary>ورود گروهی کالا از فایل اکسل.</summary>
    Task<ExcelImportResult> ImportExcelAsync(Stream fileStream, string fileName, int warehouseId);

    /// <summary>دانلود فایل اکسل نمونه (با توکن احراز هویت؛ مرورگر را از صفحه خارج نمی‌کند).</summary>
    Task<(byte[] Data, string FileName, string ContentType)> DownloadTemplateAsync();
}

/// <summary>سرویس انبارها.</summary>
public interface IWarehouseService
{
    Task<PagedResult<Warehouse>> GetPagedAsync(int skip, int take);
    Task<List<Warehouse>> GetAllAsync();
    Task<List<LookupItem>> GetLookupsAsync(bool activeOnly = false);
    Task<Warehouse> SaveAsync(Warehouse warehouse);
    Task DeleteAsync(int id);
}

/// <summary>سرویس طرف حساب‌ها.</summary>
public interface IPartyService
{
    Task<PagedResult<Party>> GetPagedAsync(PartyType type, int skip, int take);
    Task<List<Party>> GetAsync(PartyType type);
    Task<List<LookupItem>> GetLookupsAsync(PartyType type, bool activeOnly = false);
    Task<Party> SaveAsync(Party party);
    Task DeleteAsync(int id);
}

/// <summary>سرویس موجودی انبار.</summary>
public interface IStockService
{
    Task<PagedResult<StockItem>> GetStockAsync(int? warehouseId = null, string? search = null, bool belowOnly = false, int page = 1, int pageSize = 20);
    Task AdjustAsync(AdjustmentCommand cmd);
}

/// <summary>سرویس اسناد خرید و فروش.</summary>
public interface IOrderService
{
    Task<PagedResult<Order>> GetOrdersAsync(TransactionType type, DateTime? from = null, DateTime? to = null, int? partyId = null, int? warehouseId = null, int page = 1, int pageSize = 20);
    Task<OperationsOrderReportDto> GetReportSummaryAsync(TransactionType type, DateTime? from = null, DateTime? to = null,
        int? partyId = null, int? warehouseId = null);
    Task<Order?> GetAsync(int id);
    Task<Order> CreateAsync(OrderCommand cmd);
    Task<Order> UpdateAsync(int id, OrderCommand cmd);
    Task DeleteAsync(int id);
    Task<decimal> SuggestPriceAsync(int productId, TransactionType type);

    /// <summary>آخرین فاکتور خریدی که شامل این کالاست.</summary>
    Task<Order?> GetLastPurchaseAsync(int productId);
}

/// <summary>سرویس گزارش‌ها (کاردکس و نقطه سفارش).</summary>
public interface IReportService
{
    Task<List<KardexRow>> GetKardexAsync(int productId, int? warehouseId = null, DateTime? from = null, DateTime? to = null);
    Task<List<ReorderItem>> GetReorderAsync(int? warehouseId = null);
    Task<PagedResult<ReorderItem>> GetReorderPageAsync(int skip, int take, int? warehouseId = null);
}

/// <summary>سرویس داشبورد.</summary>
public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync();
    Task<OperationsDashboardDto> GetOperationsDashboardAsync(DateTime? from = null, DateTime? to = null);
    Task<List<RecentActivity>> GetRecentAsync(int count = 8);
    Task<AdminDashboard> GetAdminDashboardAsync();
    Task ClearChequeAsync(int chequeId);
    Task BounceChequeAsync(int chequeId, string? reason = null);
    Task ResetChequeAsync(int chequeId);
    Task PayInstallmentAsync(int installmentId);
    Task UnpayInstallmentAsync(int installmentId);
    Task SettleCreditAsync(int transactionId, decimal amount);

    // مدیریت اقساط و چک‌های فروشگاهی
    Task<ShopCreditSummary> GetShopCreditSummaryAsync();
    Task<List<ShopChequeItem>> GetShopChequesAsync(ChequeStatus? status = null, TransactionType? txnType = null, string? search = null);
    Task<List<ShopInstallmentItem>> GetShopInstallmentsAsync(bool? isPaid = null, string? search = null);
}

/// <summary>سرویس اعلان‌ها (Toast).</summary>
public interface IToastService
{
    IReadOnlyList<ToastMessage> Toasts { get; }
    event Action? OnChange;
    void Success(string message);
    void Error(string message);
    void Warning(string message);
    void Info(string message);
    void Remove(Guid id);
}

/// <summary>سرویس تعمیرات سمت کلاینت.</summary>
public interface IRepairService
{
    // تعمیرکارها
    Task<List<Technician>> GetTechniciansAsync(bool activeOnly = false);
    Task<Technician> SaveTechnicianAsync(Technician technician);
    Task DeleteTechnicianAsync(int id);

    // پذیرش‌ها
    Task<PagedResult<RepairOrderDto>> GetRepairsAsync(string? search, RepairStatus? status, int? technicianId, int page, int pageSize);
    Task<RepairOrderDto> GetRepairAsync(int id);
    Task<RepairOrderDto> SaveRepairAsync(RepairOrderDto repair);
    Task DeleteRepairAsync(int id);
    Task<RepairOrderDto> SetStatusAsync(int id, RepairStatus status);
    Task<RepairOrderDto> InvoiceAsync(int id, RepairInvoiceRequest request);
    Task<RepairOrderDto> InvoiceAsync(int id, int warehouseId);
}


/// <summary>سرویس هزینه‌ها سمت کلاینت.</summary>
public interface IExpenseService
{
    Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool activeOnly = false);
    Task<ExpenseCategoryDto> SaveCategoryAsync(ExpenseCategoryDto category);
    Task DeleteCategoryAsync(int id);

    Task<PagedResult<ExpenseDto>> GetExpensesAsync(string? search, int? categoryId, DateTime? from, DateTime? to, int page, int pageSize);
    Task<ExpenseDto> SaveExpenseAsync(ExpenseDto expense);
    Task DeleteExpenseAsync(int id);
}

/// <summary>سرویس حواله تحویل کالا — سند «فقط مقدار» (بدون قیمت/ریال).</summary>
public interface IGoodsIssueService
{
    Task<List<GoodsIssueDto>> GetAsync(int? partyId = null, DateTime? from = null, DateTime? to = null, string? search = null);
    Task<GoodsIssueDto?> GetAsync(int id);
    Task<GoodsIssueDto> SaveAsync(GoodsIssueCommand cmd);
    Task DeleteAsync(int id);
}
