using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Accounting;

/// <summary>
/// قرارداد سرویس حسابداری: سال مالی، کدینگ حساب‌ها، سند حسابداری،
/// دفاتر، تراز آزمایشی و صدور خودکار سند از روی اسناد انبار.
/// </summary>
public interface IAccountingService
{
    // ---------- سال مالی ----------
    Task<List<AccFiscalYear>> GetFiscalYearsAsync(Paging.Request? pagination = null);
    Task<AccFiscalYear> SaveFiscalYearAsync(AccFiscalYear dto);
    Task SetCurrentFiscalYearAsync(int id);
    Task DeleteFiscalYearAsync(int id);

    // ---------- حساب‌ها ----------
    Task<List<AccAccount>> GetAccountsFlatAsync(bool activeOnly = false, bool withBalances = false, Paging.Request? pagination = null);
    Task<List<AccAccount>> GetAccountTreeAsync(bool activeOnly = false, bool withBalances = false, Paging.Request? pagination = null);
    Task<List<LookupItem>> GetPostableAccountLookupsAsync(string? search = null, Paging.Request? pagination = null);
    Task<AccAccount> SaveAccountAsync(AccAccount dto);
    Task MoveAccountAsync(AccAccountMove cmd);
    Task DeleteAccountAsync(int id);

    // ---------- اسناد ----------
    Task<PagedResult<AccVoucher>> GetVouchersAsync(int? fiscalYearId, VoucherStatus? status, VoucherSource? source,
        string? search, DateTime? from, DateTime? to, int page, int pageSize);
    Task<AccVoucher?> GetVoucherAsync(int id);
    Task<AccVoucher> NewVoucherAsync();
    Task<AccVoucher> SaveVoucherAsync(AccVoucher dto, string? user);
    Task<AccVoucher> ConfirmVoucherAsync(int id, string? user);
    Task<AccVoucher> UnconfirmVoucherAsync(int id, string? user);
    Task<AccVoucher> CancelVoucherAsync(int id, string? user);
    Task DeleteVoucherAsync(int id);

    // ---------- گزارش‌ها ----------
    Task<AccLedgerResult> GetLedgerAsync(int accountId, DateTime? from, DateTime? to, bool includeChildren);
    Task<PagedResult<AccLedgerRow>> GetJournalAsync(DateTime? from, DateTime? to, int page, int pageSize);
    Task<AccTrialBalanceResult> GetTrialBalanceAsync(AccountLevel level, DateTime? from, DateTime? to, bool hideZero);
    Task<AccDashboard> GetDashboardAsync();

    // ---------- سند خودکار انبار ----------
    Task<List<AccInvRule>> GetInvRulesAsync(Paging.Request? pagination = null);
    Task<AccInvRule> SaveInvRuleAsync(AccInvRule dto);
    Task DeleteInvRuleAsync(int id);

    /// <summary>صدور (یا به‌روزرسانی) سند حسابداری متناظر با یک سند انبارِ قطعی‌شده.</summary>
    Task<AccVoucher?> PostInventoryDocAsync(int invDocId, string? user);

    /// <summary>حذف سند حسابداری متناظر با سند انبار (هنگام برگشت از قطعی یا ابطال).</summary>
    Task UnpostInventoryDocAsync(int invDocId);
}
