using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Treasury;

/// <summary>
/// قرارداد سرویس خزانه‌داری: صندوق و بانک، اسناد دریافت و پرداخت،
/// چک و چرخه عمر آن، انتقال بین حساب‌ها و گزارش‌های خزانه.
/// </summary>
public interface ITreasuryService
{
    // ---------- صندوق / بانک ----------
    Task<List<TrsAccount>> GetAccountsAsync(bool activeOnly = false, bool withBalances = false, Paging.Request? pagination = null);
    Task<TrsAccount?> GetAccountAsync(int id);
    Task<List<LookupItem>> GetAccountLookupsAsync(bool activeOnly = true, Paging.Request? pagination = null);
    Task<TrsAccount> SaveAccountAsync(TrsAccount dto);
    Task DeleteAccountAsync(int id);

    // ---------- اسناد دریافت و پرداخت ----------
    Task<PagedResult<TrsVoucher>> GetVouchersAsync(TreasuryKind? kind, TreasuryStatus? status, int? partyId,
        int? trsAccountId, string? search, DateTime? from, DateTime? to, int page, int pageSize);
    Task<TrsVoucher?> GetVoucherAsync(int id);
    Task<TrsVoucher> NewVoucherAsync(TreasuryKind kind);
    Task<TrsVoucher> SaveVoucherAsync(TrsVoucher dto, string? user);
    Task<TrsVoucher> ConfirmVoucherAsync(int id, string? user);
    Task<TrsVoucher> UnconfirmVoucherAsync(int id, string? user);
    Task<TrsVoucher> CancelVoucherAsync(int id, string? user);
    Task DeleteVoucherAsync(int id);

    /// <summary>ماندهٔ باز فاکتور و پیشنهاد سند تسویه برای آن</summary>
    Task<TrsVoucher> NewSettlementAsync(int invoiceId);
    /// <summary>فهرست فاکتورهای نسیهٔ قطعی با مانده، برای تخصیص به سند خزانه</summary>
    Task<List<TrsSettlementInvoice>> GetOpenInvoicesAsync(int partyId, TreasuryKind kind);

    // ---------- چک ----------
    Task<PagedResult<TrsCheque>> GetChequesAsync(ChequeKind? kind, ChequeStatus? status, int? partyId,
        int? trsAccountId, string? search, DateTime? from, DateTime? to, bool onlyOpen,
        int page, int pageSize);
    Task<TrsCheque?> GetChequeAsync(int id);
    Task<TrsCheque> SaveChequeAsync(TrsCheque dto, string? user);
    Task<TrsCheque> RunChequeActionAsync(TrsChequeCommand cmd, string? user);
    Task DeleteChequeAsync(int id);

    // ---------- قواعد ----------
    Task<List<TrsRule>> GetRulesAsync(Paging.Request? pagination = null);
    Task<TrsRule> SaveRuleAsync(TrsRule dto);

    // ---------- گزارش‌ها ----------
    Task<TrsFlowResult> GetFlowAsync(int trsAccountId, DateTime? from, DateTime? to);
    Task<TrsDashboard> GetDashboardAsync(DateTime? from, DateTime? to);
}
