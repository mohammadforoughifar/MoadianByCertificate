using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// قراردادهای سرویس‌های ماژول خزانه‌داری (سمت کلاینت)
// =====================================================================

/// <summary>صندوق‌ها، حساب‌های بانکی، کارتخوان و تنخواه.</summary>
public interface ITrsAccountService
{
    Task<List<TrsAccount>> GetAllAsync(bool activeOnly = false, bool withBalances = false);
    Task<TrsAccount> GetAsync(int id);
    Task<List<LookupItem>> GetLookupsAsync(bool activeOnly = true);
    Task<TrsAccount> SaveAsync(TrsAccount account);
    Task DeleteAsync(int id);
}

/// <summary>اسناد دریافت، پرداخت و انتقال بین حساب‌ها.</summary>
public interface ITrsVoucherService
{
    Task<PagedResult<TrsVoucher>> GetAllAsync(TreasuryKind? kind = null, TreasuryStatus? status = null,
        int? partyId = null, int? trsAccountId = null, string? search = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 15);

    Task<TrsVoucher> GetAsync(int id);
    Task<TrsVoucher> NewAsync(TreasuryKind kind);

    /// <summary>سند تسویه آماده برای یک فاکتور قطعی، با ماندهٔ تسویه‌نشده</summary>
    Task<TrsVoucher> NewSettlementAsync(int invoiceId);
    /// <summary>فاکتورهای قطعیِ نسیه و دارای مانده برای طرف حساب و نوع دریافت/پرداخت</summary>
    Task<List<TrsSettlementInvoice>> GetOpenInvoicesAsync(int partyId, TreasuryKind kind);

    Task<TrsVoucher> SaveAsync(TrsVoucher voucher);
    Task<TrsVoucher> ConfirmAsync(int id);
    Task<TrsVoucher> UnconfirmAsync(int id);
    Task<TrsVoucher> CancelAsync(int id);
    Task DeleteAsync(int id);
}

/// <summary>چک‌های دریافتی و پرداختی.</summary>
public interface ITrsChequeService
{
    Task<PagedResult<TrsCheque>> GetAllAsync(ChequeKind? kind = null, ChequeStatus? status = null,
        int? partyId = null, int? trsAccountId = null, string? search = null,
        DateTime? from = null, DateTime? to = null, bool onlyOpen = false,
        int page = 1, int pageSize = 15);

    Task<TrsCheque> GetAsync(int id);
    Task<TrsCheque> SaveAsync(TrsCheque cheque);

    /// <summary>واگذاری، وصول، برگشت، خرج یا ابطال چک</summary>
    Task<TrsCheque> RunActionAsync(TrsChequeCommand cmd);

    Task DeleteAsync(int id);
}

/// <summary>پیکربندی حساب‌های سند خودکار خزانه.</summary>
public interface ITrsRuleService
{
    Task<List<TrsRule>> GetAllAsync();
    Task<TrsRule> SaveAsync(TrsRule rule);
}

/// <summary>گزارش‌های خزانه.</summary>
public interface ITrsReportService
{
    Task<TrsFlowResult> GetFlowAsync(int trsAccountId, DateTime? from = null, DateTime? to = null);
    Task<TrsDashboard> GetDashboardAsync(DateTime? from = null, DateTime? to = null);
}
