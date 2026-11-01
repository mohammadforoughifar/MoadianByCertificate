using Inventory.Shared.Dtos;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>سرویس کلاینت حقوق و دستمزد فروغ آریا — FaPay §۸ (ماژول مستقل).</summary>
public interface IFaPayClientService
{
    Task<FaPaySettingsDto> GetSettingsAsync();
    Task<FaPaySettingsDto> SaveSettingsAsync(FaPaySettingsSaveDto dto);

    Task<List<FaPayTaxBracketDto>> ListBracketsAsync();
    Task<FaPayTaxBracketDto> SaveBracketAsync(int? id, FaPayTaxBracketSaveDto dto);
    Task DeleteBracketAsync(int id);

    Task<List<FaPayItemTypeDto>> ListItemTypesAsync();
    Task<FaPayItemTypeDto> SaveItemTypeAsync(int? id, FaPayItemTypeSaveDto dto);
    Task DeleteItemTypeAsync(int id);

    Task<List<FaPayAdjustmentDto>> ListAdjustmentsAsync(int? year = null, int? month = null, int? employeeId = null);
    Task<FaPayAdjustmentDto> SaveAdjustmentAsync(int? id, FaPayAdjustmentSaveDto dto);
    Task DeleteAdjustmentAsync(int id);

    Task<List<FaPayRunDto>> ListRunsAsync();
    Task<FaPayRunDto> GetRunAsync(int id);
    Task<FaPayRunDto> CreateRunAsync(FaPayRunSaveDto dto);
    Task<int> CalculateRunAsync(int id);
    Task<FaPayRunDto> FinalizeRunAsync(int id);
    Task<FaPayRunDto> ReopenRunAsync(int id);
    Task DeleteRunAsync(int id);
    Task<FaPayMailResultDto> SendRunEmailsAsync(int id);

    Task<List<FaPaySlipDto>> RunSlipsAsync(int runId);
    Task<FaPayBankCheckDto> BankCheckAsync(int runId);
    Task DownloadBankFileAsync(int runId, string format);
    Task<FaPaySlipDto> GetSlipAsync(int id);
    Task<List<FaPaySlipDto>> MySlipsAsync();
    Task<FaPaySlipDto> GetMySlipAsync(int id);
    Task SetPaidAsync(int id, bool paid);
    Task SendSlipAsync(int id);
    Task DownloadSlipPdfAsync(int id, bool mine);

    Task<FaPayInsuranceReportDto> InsuranceReportAsync(int year, int month);
    Task<FaPayTaxReportDto> TaxReportAsync(int year, int month);
    Task<FaPayUnitCostReportDto> UnitCostReportAsync(int year, int month);

    Task DownloadRunSlipsPdfAsync(int runId);

    Task<FaPayExtraSettingsDto> GetExtraSettingsAsync();
    Task<FaPayExtraSettingsDto> SaveExtraSettingsAsync(FaPayExtraSettingsSaveDto dto);
    Task<List<FaPayMinWageDto>> ListMinWagesAsync();
    Task<FaPayMinWageDto> SaveMinWageAsync(FaPayMinWageSaveDto dto);
    Task DeleteMinWageAsync(int year);

    Task<List<FaPayLoanDto>> ListLoansAsync(int? employeeId = null, int? status = null);
    Task<FaPayLoanDto> GetLoanAsync(int id);
    Task<FaPayLoanDto> CreateLoanAsync(FaPayLoanSaveDto dto);
    Task CancelLoanAsync(int id);
    Task DeleteLoanAsync(int id);

    Task<List<FaPayArrearDto>> ListArrearsAsync(int? employeeId = null, int? status = null);
    Task<FaPayArrearDto> SaveArrearAsync(int? id, FaPayArrearSaveDto dto);
    Task DeleteArrearAsync(int id);
    Task<int> CalculateYearEndAsync(int id);

    Task<FaPaySettlementDto> PreviewSettlementAsync(int employeeId, DateTime leaveDate, int reason, double otherEarnings, double otherDeductions);
    Task<List<FaPaySettlementDto>> ListSettlementsAsync(int? employeeId = null, int? status = null);
    Task<FaPaySettlementDto> GetSettlementAsync(int id);
    Task<FaPaySettlementDto> SaveSettlementAsync(int? id, FaPaySettlementSaveDto dto);
    Task<FaPaySettlementDto> FinalizeSettlementAsync(int id);
    Task DeleteSettlementAsync(int id);
    Task DownloadSettlementPdfAsync(int id);

    Task<FaPayInsuranceFileCheckDto> InsuranceCheckAsync(int runId);
    Task DownloadInsuranceFileAsync(int runId, string format);
    Task<FaPayCompareDto> CompareRunsAsync(int runAId, int runBId);
}

public class FaPayService : IFaPayClientService
{
    private readonly IApiClient _api;
    private readonly IJSRuntime _js;
    public FaPayService(IApiClient api, IJSRuntime js) { _api = api; _js = js; }

    private const string Root = "api/fa-pay";

    public Task<FaPaySettingsDto> GetSettingsAsync()
        => _api.GetAsync<FaPaySettingsDto>($"{Root}/settings");

    public Task<FaPaySettingsDto> SaveSettingsAsync(FaPaySettingsSaveDto dto)
        => _api.PutAsync<FaPaySettingsDto>($"{Root}/settings", dto);

    public Task<List<FaPayTaxBracketDto>> ListBracketsAsync()
        => _api.GetAsync<List<FaPayTaxBracketDto>>($"{Root}/brackets");

    public Task<FaPayTaxBracketDto> SaveBracketAsync(int? id, FaPayTaxBracketSaveDto dto)
        => id == null ? _api.PostAsync<FaPayTaxBracketDto>($"{Root}/brackets", dto)
                      : _api.PutAsync<FaPayTaxBracketDto>($"{Root}/brackets/{id}", dto);

    public Task DeleteBracketAsync(int id)
        => _api.DeleteAsync($"{Root}/brackets/{id}");

    public Task<List<FaPayItemTypeDto>> ListItemTypesAsync()
        => _api.GetAsync<List<FaPayItemTypeDto>>($"{Root}/itemtypes");

    public Task<FaPayItemTypeDto> SaveItemTypeAsync(int? id, FaPayItemTypeSaveDto dto)
        => id == null ? _api.PostAsync<FaPayItemTypeDto>($"{Root}/itemtypes", dto)
                      : _api.PutAsync<FaPayItemTypeDto>($"{Root}/itemtypes/{id}", dto);

    public Task DeleteItemTypeAsync(int id)
        => _api.DeleteAsync($"{Root}/itemtypes/{id}");

    public Task<List<FaPayAdjustmentDto>> ListAdjustmentsAsync(int? year = null, int? month = null, int? employeeId = null)
        => _api.GetAsync<List<FaPayAdjustmentDto>>($"{Root}/adjustments?year={year}&month={month}&employeeId={employeeId}");

    public Task<FaPayAdjustmentDto> SaveAdjustmentAsync(int? id, FaPayAdjustmentSaveDto dto)
        => id == null ? _api.PostAsync<FaPayAdjustmentDto>($"{Root}/adjustments", dto)
                      : _api.PutAsync<FaPayAdjustmentDto>($"{Root}/adjustments/{id}", dto);

    public Task DeleteAdjustmentAsync(int id)
        => _api.DeleteAsync($"{Root}/adjustments/{id}");

    public Task<List<FaPayRunDto>> ListRunsAsync()
        => _api.GetAsync<List<FaPayRunDto>>($"{Root}/runs");

    public Task<FaPayRunDto> GetRunAsync(int id)
        => _api.GetAsync<FaPayRunDto>($"{Root}/runs/{id}");

    public Task<FaPayRunDto> CreateRunAsync(FaPayRunSaveDto dto)
        => _api.PostAsync<FaPayRunDto>($"{Root}/runs", dto);

    public async Task<int> CalculateRunAsync(int id)
    {
        var r = await _api.PostAsync<CalcResult>($"{Root}/runs/{id}/calculate");
        return r?.Count ?? 0;
    }

    public Task<FaPayRunDto> FinalizeRunAsync(int id)
        => _api.PostAsync<FaPayRunDto>($"{Root}/runs/{id}/finalize");

    public Task<FaPayRunDto> ReopenRunAsync(int id)
        => _api.PostAsync<FaPayRunDto>($"{Root}/runs/{id}/reopen");

    public Task DeleteRunAsync(int id)
        => _api.DeleteAsync($"{Root}/runs/{id}");

    public Task<FaPayMailResultDto> SendRunEmailsAsync(int id)
        => _api.PostAsync<FaPayMailResultDto>($"{Root}/runs/{id}/send-all");

    public Task<List<FaPaySlipDto>> RunSlipsAsync(int runId)
        => _api.GetAsync<List<FaPaySlipDto>>($"{Root}/runs/{runId}/slips");

    public Task<FaPayBankCheckDto> BankCheckAsync(int runId)
        => _api.GetAsync<FaPayBankCheckDto>($"{Root}/runs/{runId}/bank-check");

    public async Task DownloadBankFileAsync(int runId, string format)
    {
        var f = await _api.GetFileAsync($"{Root}/runs/{runId}/bank-file?format={format}");
        await _js.InvokeVoidAsync("saveAsFile", f.FileName, Convert.ToBase64String(f.Data));
    }

    public Task<FaPaySlipDto> GetSlipAsync(int id)
        => _api.GetAsync<FaPaySlipDto>($"{Root}/slips/{id}");

    public Task<List<FaPaySlipDto>> MySlipsAsync()
        => _api.GetAsync<List<FaPaySlipDto>>($"{Root}/slips/my");

    public Task<FaPaySlipDto> GetMySlipAsync(int id)
        => _api.GetAsync<FaPaySlipDto>($"{Root}/slips/my/{id}");

    public Task SetPaidAsync(int id, bool paid)
        => _api.PostAsync<bool>($"{Root}/slips/{id}/paid?paid={paid}");

    public Task SendSlipAsync(int id)
        => _api.PostAsync<MailResult>($"{Root}/slips/{id}/send");

    public async Task DownloadSlipPdfAsync(int id, bool mine)
    {
        var path = mine ? $"{Root}/slips/my/{id}/pdf" : $"{Root}/slips/{id}/pdf";
        var f = await _api.GetFileAsync(path);
        await _js.InvokeVoidAsync("saveAsFile", f.FileName, Convert.ToBase64String(f.Data));
    }

    public Task<FaPayInsuranceReportDto> InsuranceReportAsync(int year, int month)
        => _api.GetAsync<FaPayInsuranceReportDto>($"{Root}/reports/insurance?year={year}&month={month}");

    public Task<FaPayTaxReportDto> TaxReportAsync(int year, int month)
        => _api.GetAsync<FaPayTaxReportDto>($"{Root}/reports/tax?year={year}&month={month}");

    public Task<FaPayUnitCostReportDto> UnitCostReportAsync(int year, int month)
        => _api.GetAsync<FaPayUnitCostReportDto>($"{Root}/reports/unit-cost?year={year}&month={month}");

    public async Task DownloadRunSlipsPdfAsync(int runId)
    {
        var f = await _api.GetFileAsync($"{Root}/runs/{runId}/slips-pdf");
        await _js.InvokeVoidAsync("saveAsFile", f.FileName, Convert.ToBase64String(f.Data));
    }

    public Task<FaPayExtraSettingsDto> GetExtraSettingsAsync()
        => _api.GetAsync<FaPayExtraSettingsDto>($"{Root}/extra-settings");

    public Task<FaPayExtraSettingsDto> SaveExtraSettingsAsync(FaPayExtraSettingsSaveDto dto)
        => _api.PutAsync<FaPayExtraSettingsDto>($"{Root}/extra-settings", dto);

    // حداقل دستمزد قانونی — جدول اختصاصی همین ماژول (FaPayMinWages)
    public Task<List<FaPayMinWageDto>> ListMinWagesAsync()
        => _api.GetAsync<List<FaPayMinWageDto>>($"{Root}/min-wages");

    public Task<FaPayMinWageDto> SaveMinWageAsync(FaPayMinWageSaveDto dto)
        => _api.PostAsync<FaPayMinWageDto>($"{Root}/min-wages", dto);

    public Task DeleteMinWageAsync(int year)
        => _api.DeleteAsync($"{Root}/min-wages/{year}");

    public Task<List<FaPayLoanDto>> ListLoansAsync(int? employeeId = null, int? status = null)
        => _api.GetAsync<List<FaPayLoanDto>>($"{Root}/loans?employeeId={employeeId}&status={status}");

    public Task<FaPayLoanDto> GetLoanAsync(int id)
        => _api.GetAsync<FaPayLoanDto>($"{Root}/loans/{id}");

    public Task<FaPayLoanDto> CreateLoanAsync(FaPayLoanSaveDto dto)
        => _api.PostAsync<FaPayLoanDto>($"{Root}/loans", dto);

    public Task CancelLoanAsync(int id)
        => _api.PostAsync<bool>($"{Root}/loans/{id}/cancel");

    public Task DeleteLoanAsync(int id)
        => _api.DeleteAsync($"{Root}/loans/{id}");

    public Task<List<FaPayArrearDto>> ListArrearsAsync(int? employeeId = null, int? status = null)
        => _api.GetAsync<List<FaPayArrearDto>>($"{Root}/arrears?employeeId={employeeId}&status={status}");

    public Task<FaPayArrearDto> SaveArrearAsync(int? id, FaPayArrearSaveDto dto)
        => id == null ? _api.PostAsync<FaPayArrearDto>($"{Root}/arrears", dto)
                      : _api.PutAsync<FaPayArrearDto>($"{Root}/arrears/{id}", dto);

    public Task DeleteArrearAsync(int id)
        => _api.DeleteAsync($"{Root}/arrears/{id}");

    public async Task<int> CalculateYearEndAsync(int id)
    {
        var r = await _api.PostAsync<CalcResult>($"{Root}/runs/{id}/calculate-yearend");
        return r?.Count ?? 0;
    }

    public Task<FaPaySettlementDto> PreviewSettlementAsync(int employeeId, DateTime leaveDate, int reason, double otherEarnings, double otherDeductions)
        => _api.GetAsync<FaPaySettlementDto>($"{Root}/settlements/preview?employeeId={employeeId}&leaveDate={leaveDate:yyyy-MM-dd}&reason={reason}&otherEarnings={otherEarnings}&otherDeductions={otherDeductions}");

    public Task<List<FaPaySettlementDto>> ListSettlementsAsync(int? employeeId = null, int? status = null)
        => _api.GetAsync<List<FaPaySettlementDto>>($"{Root}/settlements?employeeId={employeeId}&status={status}");

    public Task<FaPaySettlementDto> GetSettlementAsync(int id)
        => _api.GetAsync<FaPaySettlementDto>($"{Root}/settlements/{id}");

    public Task<FaPaySettlementDto> SaveSettlementAsync(int? id, FaPaySettlementSaveDto dto)
        => id == null ? _api.PostAsync<FaPaySettlementDto>($"{Root}/settlements", dto)
                      : _api.PutAsync<FaPaySettlementDto>($"{Root}/settlements/{id}", dto);

    public Task<FaPaySettlementDto> FinalizeSettlementAsync(int id)
        => _api.PostAsync<FaPaySettlementDto>($"{Root}/settlements/{id}/finalize");

    public Task DeleteSettlementAsync(int id)
        => _api.DeleteAsync($"{Root}/settlements/{id}");

    public async Task DownloadSettlementPdfAsync(int id)
    {
        var f = await _api.GetFileAsync($"{Root}/settlements/{id}/pdf");
        await _js.InvokeVoidAsync("saveAsFile", f.FileName, Convert.ToBase64String(f.Data));
    }

    public Task<FaPayInsuranceFileCheckDto> InsuranceCheckAsync(int runId)
        => _api.GetAsync<FaPayInsuranceFileCheckDto>($"{Root}/runs/{runId}/insurance-check");

    public async Task DownloadInsuranceFileAsync(int runId, string format)
    {
        var f = await _api.GetFileAsync($"{Root}/runs/{runId}/insurance-file?format={format}");
        await _js.InvokeVoidAsync("saveAsFile", f.FileName, Convert.ToBase64String(f.Data));
    }

    public Task<FaPayCompareDto> CompareRunsAsync(int runAId, int runBId)
        => _api.GetAsync<FaPayCompareDto>($"{Root}/runs/compare?runAId={runAId}&runBId={runBId}");

    private sealed class CalcResult { public int Count { get; set; } }
    private sealed class MailResult { public int MailId { get; set; } }
}
