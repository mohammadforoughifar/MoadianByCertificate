using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// پیاده‌سازی سرویس‌های ماژول خزانه‌داری — تنها نقطه‌ی ساخت مسیرهای API
// =====================================================================

/// <summary>پیاده‌سازی سرویس صندوق و بانک.</summary>
public class TrsAccountService : ITrsAccountService
{
    private readonly IApiClient _api;
    public TrsAccountService(IApiClient api) => _api = api;

    public Task<List<TrsAccount>> GetAllAsync(bool activeOnly = false, bool withBalances = false)
        => _api.GetAsync<List<TrsAccount>>(
            $"api/trs/accounts?activeOnly={activeOnly.ToString().ToLower()}&withBalances={withBalances.ToString().ToLower()}");

    public Task<TrsAccount> GetAsync(int id)
        => _api.GetAsync<TrsAccount>($"api/trs/accounts/{id}");

    public Task<List<LookupItem>> GetLookupsAsync(bool activeOnly = true)
        => _api.GetAsync<List<LookupItem>>($"api/trs/accounts/lookups?activeOnly={activeOnly.ToString().ToLower()}");

    public Task<TrsAccount> SaveAsync(TrsAccount account)
        => _api.PostAsync<TrsAccount>("api/trs/accounts", account);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/trs/accounts/{id}");
}

/// <summary>پیاده‌سازی سرویس اسناد خزانه.</summary>
public class TrsVoucherService : ITrsVoucherService
{
    private readonly IApiClient _api;
    public TrsVoucherService(IApiClient api) => _api = api;

    public Task<PagedResult<TrsVoucher>> GetAllAsync(TreasuryKind? kind = null, TreasuryStatus? status = null,
        int? partyId = null, int? trsAccountId = null, string? search = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 15)
    {
        var q = $"api/trs/vouchers?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        if (kind is not null) q += $"&kind={kind}";
        if (status is not null) q += $"&status={status}";
        if (partyId is > 0) q += $"&partyId={partyId}";
        if (trsAccountId is > 0) q += $"&trsAccountId={trsAccountId}";
        if (from is not null) q += $"&from={from:yyyy-MM-dd}";
        if (to is not null) q += $"&to={to:yyyy-MM-dd}";
        return _api.GetAsync<PagedResult<TrsVoucher>>(q);
    }

    public Task<TrsVoucher> GetAsync(int id)
        => _api.GetAsync<TrsVoucher>($"api/trs/vouchers/{id}");

    public Task<TrsVoucher> NewAsync(TreasuryKind kind)
        => _api.GetAsync<TrsVoucher>($"api/trs/vouchers/new?kind={kind}");

    public Task<TrsVoucher> NewSettlementAsync(int invoiceId)
        => _api.GetAsync<TrsVoucher>($"api/trs/vouchers/settle/{invoiceId}");

    public Task<List<TrsSettlementInvoice>> GetOpenInvoicesAsync(int partyId, TreasuryKind kind)
        => _api.GetAsync<List<TrsSettlementInvoice>>(
            $"api/trs/vouchers/open-invoices?partyId={partyId}&kind={kind}");

    public Task<TrsVoucher> SaveAsync(TrsVoucher voucher)
        => _api.PostAsync<TrsVoucher>("api/trs/vouchers", voucher);

    public Task<TrsVoucher> ConfirmAsync(int id)
        => _api.PostAsync<TrsVoucher>($"api/trs/vouchers/{id}/confirm", new { });

    public Task<TrsVoucher> UnconfirmAsync(int id)
        => _api.PostAsync<TrsVoucher>($"api/trs/vouchers/{id}/unconfirm", new { });

    public Task<TrsVoucher> CancelAsync(int id)
        => _api.PostAsync<TrsVoucher>($"api/trs/vouchers/{id}/cancel", new { });

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/trs/vouchers/{id}");
}

/// <summary>پیاده‌سازی سرویس چک.</summary>
public class TrsChequeService : ITrsChequeService
{
    private readonly IApiClient _api;
    public TrsChequeService(IApiClient api) => _api = api;

    public Task<PagedResult<TrsCheque>> GetAllAsync(ChequeKind? kind = null, ChequeStatus? status = null,
        int? partyId = null, int? trsAccountId = null, string? search = null,
        DateTime? from = null, DateTime? to = null, bool onlyOpen = false,
        int page = 1, int pageSize = 15)
    {
        var q = $"api/trs/cheques?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        if (kind is not null) q += $"&kind={kind}";
        if (status is not null) q += $"&status={status}";
        if (partyId is > 0) q += $"&partyId={partyId}";
        if (trsAccountId is > 0) q += $"&trsAccountId={trsAccountId}";
        if (from is not null) q += $"&from={from:yyyy-MM-dd}";
        if (to is not null) q += $"&to={to:yyyy-MM-dd}";
        if (onlyOpen) q += "&onlyOpen=true";
        return _api.GetAsync<PagedResult<TrsCheque>>(q);
    }

    public Task<TrsCheque> GetAsync(int id)
        => _api.GetAsync<TrsCheque>($"api/trs/cheques/{id}");

    public Task<TrsCheque> SaveAsync(TrsCheque cheque)
        => _api.PostAsync<TrsCheque>("api/trs/cheques", cheque);

    public Task<TrsCheque> RunActionAsync(TrsChequeCommand cmd)
        => _api.PostAsync<TrsCheque>("api/trs/cheques/action", cmd);

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/trs/cheques/{id}");
}

/// <summary>پیاده‌سازی پیکربندی خزانه.</summary>
public class TrsRuleService : ITrsRuleService
{
    private readonly IApiClient _api;
    public TrsRuleService(IApiClient api) => _api = api;

    public Task<List<TrsRule>> GetAllAsync()
        => _api.GetAsync<List<TrsRule>>("api/trs/rules");

    public Task<TrsRule> SaveAsync(TrsRule rule)
        => _api.PostAsync<TrsRule>("api/trs/rules", rule);
}

/// <summary>پیاده‌سازی گزارش‌های خزانه.</summary>
public class TrsReportService : ITrsReportService
{
    private readonly IApiClient _api;
    public TrsReportService(IApiClient api) => _api = api;

    public Task<TrsFlowResult> GetFlowAsync(int trsAccountId, DateTime? from = null, DateTime? to = null)
    {
        var q = $"api/trs/reports/flow?trsAccountId={trsAccountId}";
        if (from is not null) q += $"&from={from:yyyy-MM-dd}";
        if (to is not null) q += $"&to={to:yyyy-MM-dd}";
        return _api.GetAsync<TrsFlowResult>(q);
    }

    public Task<TrsDashboard> GetDashboardAsync(DateTime? from = null, DateTime? to = null)
    {
        var q = "api/trs/reports/dashboard";
        var sep = "?";
        if (from is not null) { q += $"{sep}from={from:yyyy-MM-dd}"; sep = "&"; }
        if (to is not null) q += $"{sep}to={to:yyyy-MM-dd}";
        return _api.GetAsync<TrsDashboard>(q);
    }
}
