using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// پیاده‌سازی سرویس‌های ماژول حسابداری — تنها نقطه‌ی ساخت مسیرهای API
// =====================================================================

/// <summary>پیاده‌سازی سرویس سال مالی.</summary>
public class AccFiscalYearService : IAccFiscalYearService
{
    private readonly IApiClient _api;
    public AccFiscalYearService(IApiClient api) => _api = api;

    public Task<List<AccFiscalYear>> GetAllAsync()
        => _api.GetAsync<List<AccFiscalYear>>("api/acc/fiscal-years");

    public Task<AccFiscalYear> SaveAsync(AccFiscalYear year)
        => _api.PostAsync<AccFiscalYear>("api/acc/fiscal-years", year);

    public Task SetCurrentAsync(int id)
        => _api.PostAsync<object>($"api/acc/fiscal-years/{id}/set-current", new { });

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/acc/fiscal-years/{id}");
}

/// <summary>پیاده‌سازی سرویس کدینگ حساب‌ها.</summary>
public class AccAccountService : IAccAccountService
{
    private readonly IApiClient _api;
    public AccAccountService(IApiClient api) => _api = api;

    public Task<List<AccAccount>> GetTreeAsync(bool activeOnly = false, bool withBalances = false)
        => _api.GetAsync<List<AccAccount>>($"api/acc/accounts/tree?activeOnly={activeOnly}&withBalances={withBalances}");

    public Task<List<AccAccount>> GetFlatAsync(bool activeOnly = false, bool withBalances = false)
        => _api.GetAsync<List<AccAccount>>($"api/acc/accounts?activeOnly={activeOnly}&withBalances={withBalances}");

    public Task<List<LookupItem>> GetLookupsAsync(string? search = null)
        => _api.GetAsync<List<LookupItem>>($"api/acc/accounts/lookups?search={Uri.EscapeDataString(search ?? "")}");

    public Task<AccAccount> SaveAsync(AccAccount account)
        => _api.PostAsync<AccAccount>("api/acc/accounts", account);

    public Task MoveAsync(AccAccountMove cmd)
        => _api.PostAsync<object>("api/acc/accounts/move", cmd);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/acc/accounts/{id}");
}

/// <summary>پیاده‌سازی سرویس اسناد حسابداری.</summary>
public class AccVoucherService : IAccVoucherService
{
    private readonly IApiClient _api;
    public AccVoucherService(IApiClient api) => _api = api;

    public Task<PagedResult<AccVoucher>> GetAllAsync(int? fiscalYearId = null, VoucherStatus? status = null,
        VoucherSource? source = null, string? search = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 15)
    {
        var q = $"api/acc/vouchers?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        if (fiscalYearId is > 0) q += $"&fiscalYearId={fiscalYearId}";
        if (status is not null) q += $"&status={status}";
        if (source is not null) q += $"&source={source}";
        if (from is not null) q += $"&from={from:yyyy-MM-dd}";
        if (to is not null) q += $"&to={to:yyyy-MM-dd}";
        return _api.GetAsync<PagedResult<AccVoucher>>(q);
    }

    public Task<AccVoucher> GetAsync(int id)
        => _api.GetAsync<AccVoucher>($"api/acc/vouchers/{id}");

    public Task<AccVoucher> NewAsync()
        => _api.GetAsync<AccVoucher>("api/acc/vouchers/new");

    public Task<AccVoucher> SaveAsync(AccVoucher voucher)
        => _api.PostAsync<AccVoucher>("api/acc/vouchers", voucher);

    public Task<AccVoucher> ConfirmAsync(int id)
        => _api.PostAsync<AccVoucher>($"api/acc/vouchers/{id}/confirm", new { });

    public Task<AccVoucher> UnconfirmAsync(int id)
        => _api.PostAsync<AccVoucher>($"api/acc/vouchers/{id}/unconfirm", new { });

    public Task<AccVoucher> CancelAsync(int id)
        => _api.PostAsync<AccVoucher>($"api/acc/vouchers/{id}/cancel", new { });

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/acc/vouchers/{id}");
}

/// <summary>پیاده‌سازی گزارش‌های حسابداری.</summary>
public class AccReportService : IAccReportService
{
    private readonly IApiClient _api;
    public AccReportService(IApiClient api) => _api = api;

    public Task<AccLedgerResult> GetLedgerAsync(int accountId, DateTime? from = null, DateTime? to = null, bool includeChildren = true)
    {
        var q = $"api/acc/reports/ledger?accountId={accountId}&includeChildren={includeChildren}";
        if (from is not null) q += $"&from={from:yyyy-MM-dd}";
        if (to is not null) q += $"&to={to:yyyy-MM-dd}";
        return _api.GetAsync<AccLedgerResult>(q);
    }

    public Task<PagedResult<AccLedgerRow>> GetJournalAsync(DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 30)
    {
        var q = $"api/acc/reports/journal?page={page}&pageSize={pageSize}";
        if (from is not null) q += $"&from={from:yyyy-MM-dd}";
        if (to is not null) q += $"&to={to:yyyy-MM-dd}";
        return _api.GetAsync<PagedResult<AccLedgerRow>>(q);
    }

    public Task<AccTrialBalanceResult> GetTrialBalanceAsync(AccountLevel level = AccountLevel.General,
        DateTime? from = null, DateTime? to = null, bool hideZero = true)
    {
        var q = $"api/acc/reports/trial-balance?level={level}&hideZero={hideZero}";
        if (from is not null) q += $"&from={from:yyyy-MM-dd}";
        if (to is not null) q += $"&to={to:yyyy-MM-dd}";
        return _api.GetAsync<AccTrialBalanceResult>(q);
    }

    public Task<AccDashboard> GetDashboardAsync()
        => _api.GetAsync<AccDashboard>("api/acc/reports/dashboard");
}

/// <summary>پیاده‌سازی قواعد سند خودکار انبار.</summary>
public class AccInvRuleService : IAccInvRuleService
{
    private readonly IApiClient _api;
    public AccInvRuleService(IApiClient api) => _api = api;

    public Task<List<AccInvRule>> GetAllAsync()
        => _api.GetAsync<List<AccInvRule>>("api/acc/inv-rules");

    public Task<AccInvRule> SaveAsync(AccInvRule rule)
        => _api.PostAsync<AccInvRule>("api/acc/inv-rules", rule);

    public Task<AccVoucher> PostDocAsync(int invDocId)
        => _api.PostAsync<AccVoucher>($"api/acc/inv-rules/post-doc/{invDocId}", new { });
}

// ============================ ابعاد تحلیلی ============================

/// <summary>پیاده‌سازی سرویس ابعاد تحلیلی.</summary>
public class AccDimensionService : IAccDimensionService
{
    private readonly IApiClient _api;
    public AccDimensionService(IApiClient api) => _api = api;

    public Task<List<AccDimension>> GetDimensionsAsync()
        => _api.GetAsync<List<AccDimension>>("api/acc/dimensions");

    public Task<AccDimension> SaveDimensionAsync(AccDimension dim)
        => _api.PostAsync<AccDimension>("api/acc/dimensions", dim);

    public Task DeleteDimensionAsync(int id)
        => _api.DeleteAsync($"api/acc/dimensions/{id}");

    public Task<List<AccDimensionValue>> GetValuesAsync(int dimensionId, bool activeOnly = false)
        => _api.GetAsync<List<AccDimensionValue>>($"api/acc/dimensions/{dimensionId}/values?activeOnly={activeOnly}");

    public Task<AccDimensionValue> SaveValueAsync(AccDimensionValue value)
        => _api.PostAsync<AccDimensionValue>($"api/acc/dimensions/{value.DimensionId}/values", value);

    public Task DeleteValueAsync(int id)
        => _api.DeleteAsync($"api/acc/dimensions/values/{id}");

    public Task<List<LookupItem>> GetValueLookupsAsync(int dimensionId, string? search = null)
        => _api.GetAsync<List<LookupItem>>($"api/acc/dimensions/{dimensionId}/values/lookups?search={Uri.EscapeDataString(search ?? "")}");
}

// ============================ دارایی ثابت ============================

/// <summary>پیاده‌سازی سرویس گروه‌های دارایی.</summary>
public class FixedAssetCategoryService : IFixedAssetCategoryService
{
    private readonly IApiClient _api;
    public FixedAssetCategoryService(IApiClient api) => _api = api;

    public Task<List<FixedAssetCategory>> GetAllAsync()
        => _api.GetAsync<List<FixedAssetCategory>>("api/acc/fixed-assets/categories");

    public Task<FixedAssetCategory> SaveAsync(FixedAssetCategory dto)
        => _api.PostAsync<FixedAssetCategory>("api/acc/fixed-assets/categories", dto);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/acc/fixed-assets/categories/{id}");
}

/// <summary>پیاده‌سازی سرویس دارایی‌ها.</summary>
public class FixedAssetService : IFixedAssetService
{
    private readonly IApiClient _api;
    public FixedAssetService(IApiClient api) => _api = api;

    public Task<List<FixedAsset>> GetAllAsync(FixedAssetStatus? status = null, int? categoryId = null, string? search = null)
        => _api.GetAsync<List<FixedAsset>>(
            $"api/acc/fixed-assets?status={(int?)status}&categoryId={categoryId}&search={Uri.EscapeDataString(search ?? "")}");

    public Task<FixedAsset> GetAsync(int id)
        => _api.GetAsync<FixedAsset>($"api/acc/fixed-assets/{id}");

    public Task<FixedAsset> SaveAsync(FixedAsset dto)
        => _api.PostAsync<FixedAsset>("api/acc/fixed-assets", dto);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/acc/fixed-assets/{id}");
}

/// <summary>پیاده‌سازی سرویس اجرای استهلاک.</summary>
public class FixedAssetRunService : IFixedAssetRunService
{
    private readonly IApiClient _api;
    public FixedAssetRunService(IApiClient api) => _api = api;

    public Task<List<FixedAssetDepreciationRun>> GetAllAsync()
        => _api.GetAsync<List<FixedAssetDepreciationRun>>("api/acc/fixed-assets/runs");

    public Task<FixedAssetDepreciationRun> RunAsync(FixedAssetDepreciationRequest req)
        => _api.PostAsync<FixedAssetDepreciationRun>("api/acc/fixed-assets/runs", req);

    public Task<FixedAssetDepreciationRun?> PostToAccountingAsync(int id)
        => _api.PostAsync<FixedAssetDepreciationRun?>($"api/acc/fixed-assets/runs/{id}/post", new { });

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/acc/fixed-assets/runs/{id}");
}

// ============================ بودجه ============================

/// <summary>پیاده‌سازی سرویس بودجه.</summary>
public class BudgetService : IBudgetService
{
    private readonly IApiClient _api;
    public BudgetService(IApiClient api) => _api = api;

    public Task<List<Budget>> GetAllAsync(bool activeOnly = false)
        => _api.GetAsync<List<Budget>>($"api/acc/budgets?activeOnly={activeOnly}");

    public Task<Budget> GetAsync(int id)
        => _api.GetAsync<Budget>($"api/acc/budgets/{id}");

    public Task<Budget> SaveAsync(Budget dto)
        => _api.PostAsync<Budget>("api/acc/budgets", dto);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/acc/budgets/{id}");

    public Task<List<BudgetTransaction>> GetTransactionsAsync(int budgetId, int? budgetItemId = null)
        => _api.GetAsync<List<BudgetTransaction>>($"api/acc/budgets/{budgetId}/transactions?budgetItemId={budgetItemId}");

    public Task<BudgetTransaction> AddTransactionAsync(BudgetTransaction dto)
        => _api.PostAsync<BudgetTransaction>("api/acc/budgets/transactions", dto);

    public Task DeleteTransactionAsync(int id)
        => _api.DeleteAsync($"api/acc/budgets/transactions/{id}");

    public Task<BudgetDashboard> GetDashboardAsync()
        => _api.GetAsync<BudgetDashboard>("api/acc/budgets/dashboard");
}
