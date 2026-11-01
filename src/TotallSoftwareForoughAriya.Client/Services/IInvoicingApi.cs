using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// قراردادهای سرویس‌های ماژول فاکتور (سمت کلاینت)
// =====================================================================

/// <summary>فاکتور خرید / فروش / برگشتی‌ها.</summary>
public interface IFacInvoiceService
{
    Task<PagedResult<FacInvoice>> GetAllAsync(InvoiceKind? kind = null, InvoiceStatus? status = null,
        int? partyId = null, int? warehouseId = null, string? search = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 15);

    Task<FacInvoice> GetAsync(int id);
    Task<FacInvoice> NewAsync(InvoiceKind kind);

    /// <summary>سطر آماده برای یک کالا: قیمت، نرخ مالیات، کد مالیاتی و موجودی</summary>
    Task<FacInvoiceLine> BuildLineAsync(int productId, InvoiceKind kind, int warehouseId);

    Task<FacInvoice> SaveAsync(FacInvoice invoice);
    Task<FacInvoice> ConfirmAsync(int id);
    Task<FacInvoice> UnconfirmAsync(int id);
    Task<FacInvoice> CancelAsync(int id);
    Task DeleteAsync(int id);
}

/// <summary>پیکربندی انواع فاکتور (اتصال به انبار و حسابداری).</summary>
public interface IFacRuleService
{
    Task<List<FacRule>> GetAllAsync();
    Task<FacRule> SaveAsync(FacRule rule);
}

/// <summary>گزارش‌های فروش و خرید.</summary>
public interface IFacReportService
{
    Task<FacSummaryResult> GetSummaryAsync(InvoiceKind kind, string groupBy = "month",
        DateTime? from = null, DateTime? to = null);
    Task<FacDashboard> GetDashboardAsync(DateTime? from = null, DateTime? to = null);
}
