using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IHrTalentService
{
    Task<List<HrOnboardingDto>> GetOnboardingAsync(bool? onlyOpen = null);
    Task<HrOnboardingDto> CreateOnboardingAsync(HrTalentCaseSaveDto dto);
    Task<HrOnboardingDto> SetOnboardingItemAsync(int itemId, int status, string? doneBy);
    Task<HrOnboardingDto> AddOnboardingItemAsync(int caseId, HrTalentItemSaveDto dto);
    Task DeleteOnboardingItemAsync(int itemId);
    Task<HrOnboardingDto> CompleteOnboardingAsync(int id, string? by);

    Task<List<HrExitCaseDto>> GetExitCasesAsync(bool? onlyOpen = null);
    Task<HrExitCaseDto> CreateExitCaseAsync(HrTalentCaseSaveDto dto);
    Task<HrExitCaseDto> SetExitItemAsync(int itemId, int status, string? doneBy);
    Task<HrExitCaseDto> AddExitItemAsync(int caseId, HrTalentItemSaveDto dto);
    Task DeleteExitItemAsync(int itemId);
    Task<HrExitCaseDto> CompleteExitCaseAsync(int id, string? by);

    Task<List<HrJobHistoryDto>> GetHistoryAsync(int employeeId);
    Task<HrJobHistoryDto> SaveHistoryAsync(HrJobHistorySaveDto dto);
    Task DeleteHistoryAsync(int id);

    Task<List<HrTrialPeriodDto>> GetTrialsAsync(bool? onlyActive = null);
    Task<HrTrialPeriodDto> SaveTrialAsync(HrTrialSaveDto dto);
    Task<HrTrialPeriodDto> DecideTrialAsync(int id, HrTrialDecideDto dto);
    Task DeleteTrialAsync(int id);
    Task<int> AutoCreateTrialsAsync();

    Task<List<HrAppraisalDto>> GetAppraisalsAsync();
    Task<HrAppraisalDto> SaveAppraisalAsync(int? id, HrAppraisalSaveDto dto);
    Task DeleteAppraisalAsync(int id);
    Task<List<HrAppraisalKpiDto>> GetKpisAsync(int appraisalId);
    Task<HrAppraisalKpiDto> SaveKpiAsync(int appraisalId, int? kpiId, HrAppraisalKpiSaveDto dto);
    Task DeleteKpiAsync(int kpiId);
    Task SaveScoreAsync(HrAppraisalScoreSaveDto dto);
    Task<List<HrAppraisalScoreDto>> GetScoresAsync(int appraisalId, int? employeeId = null);
    Task<List<HrAppraisalResultDto>> GetResultsAsync(int appraisalId);
    Task<HrAppraisalDecreeProposalResultDto> ProposeDecreesAsync(HrAppraisalDecreeProposalDto dto);
    Task<HrAppraisalDto> SetAppraisalStatusAsync(int id, int status, string? by);

    // پرونده‌ی خود پرسنل
    Task<HrMyProfileDto?> GetMyProfileAsync();
    Task SubmitMyProfileEditAsync(HrProfileEditRequestSaveDto dto);
    Task<List<HrProfileEditRequestDto>> ListProfileRequestsAsync(bool? onlyPending = null);
    Task<HrProfileEditRequestDto> DecideProfileRequestAsync(int id, bool approve, string? note);
}

public class HrTalentService : IHrTalentService
{
    private readonly IApiClient _api;
    private const string Root = "api/hr-talent";

    public HrTalentService(IApiClient api) => _api = api;

    public Task<List<HrOnboardingDto>> GetOnboardingAsync(bool? onlyOpen = null)
        => _api.GetAsync<List<HrOnboardingDto>>($"{Root}/onboarding?onlyOpen={onlyOpen}");

    public Task<HrOnboardingDto> CreateOnboardingAsync(HrTalentCaseSaveDto dto)
        => _api.PostAsync<HrOnboardingDto>($"{Root}/onboarding", dto);

    public Task<HrOnboardingDto> SetOnboardingItemAsync(int itemId, int status, string? doneBy)
        => _api.PostAsync<HrOnboardingDto>($"{Root}/onboarding/items/{itemId}?status={status}&doneBy={Uri.EscapeDataString(doneBy ?? "")}", null);

    public Task<HrOnboardingDto> AddOnboardingItemAsync(int caseId, HrTalentItemSaveDto dto)
        => _api.PostAsync<HrOnboardingDto>($"{Root}/onboarding/{caseId}/items", dto);

    public Task DeleteOnboardingItemAsync(int itemId)
        => _api.DeleteAsync($"{Root}/onboarding/items/{itemId}");

    public Task<HrOnboardingDto> CompleteOnboardingAsync(int id, string? by)
        => _api.PostAsync<HrOnboardingDto>($"{Root}/onboarding/{id}/complete?by={Uri.EscapeDataString(by ?? "")}", null);

    public Task<List<HrExitCaseDto>> GetExitCasesAsync(bool? onlyOpen = null)
        => _api.GetAsync<List<HrExitCaseDto>>($"{Root}/exit?onlyOpen={onlyOpen}");

    public Task<HrExitCaseDto> CreateExitCaseAsync(HrTalentCaseSaveDto dto)
        => _api.PostAsync<HrExitCaseDto>($"{Root}/exit", dto);

    public Task<HrExitCaseDto> SetExitItemAsync(int itemId, int status, string? doneBy)
        => _api.PostAsync<HrExitCaseDto>($"{Root}/exit/items/{itemId}?status={status}&doneBy={Uri.EscapeDataString(doneBy ?? "")}", null);

    public Task<HrExitCaseDto> AddExitItemAsync(int caseId, HrTalentItemSaveDto dto)
        => _api.PostAsync<HrExitCaseDto>($"{Root}/exit/{caseId}/items", dto);

    public Task DeleteExitItemAsync(int itemId)
        => _api.DeleteAsync($"{Root}/exit/items/{itemId}");

    public Task<HrExitCaseDto> CompleteExitCaseAsync(int id, string? by)
        => _api.PostAsync<HrExitCaseDto>($"{Root}/exit/{id}/complete?by={Uri.EscapeDataString(by ?? "")}", null);

    public Task<List<HrJobHistoryDto>> GetHistoryAsync(int employeeId)
        => _api.GetAsync<List<HrJobHistoryDto>>($"{Root}/history/{employeeId}");

    public Task<HrJobHistoryDto> SaveHistoryAsync(HrJobHistorySaveDto dto)
        => _api.PostAsync<HrJobHistoryDto>($"{Root}/history", dto);

    public Task DeleteHistoryAsync(int id)
        => _api.DeleteAsync($"{Root}/history/{id}");

    public Task<List<HrTrialPeriodDto>> GetTrialsAsync(bool? onlyActive = null)
        => _api.GetAsync<List<HrTrialPeriodDto>>($"{Root}/trials?onlyActive={onlyActive}");

    public Task<HrTrialPeriodDto> SaveTrialAsync(HrTrialSaveDto dto)
        => _api.PostAsync<HrTrialPeriodDto>($"{Root}/trials", dto);

    public Task<HrTrialPeriodDto> DecideTrialAsync(int id, HrTrialDecideDto dto)
        => _api.PostAsync<HrTrialPeriodDto>($"{Root}/trials/{id}/decide", dto);

    public Task DeleteTrialAsync(int id)
        => _api.DeleteAsync($"{Root}/trials/{id}");

    public async Task<int> AutoCreateTrialsAsync()
    {
        var r = await _api.PostAsync<Dictionary<string, int>>($"{Root}/trials/auto", null);
        return r.TryGetValue("count", out var c) ? c : 0;
    }

    public Task<List<HrAppraisalDto>> GetAppraisalsAsync()
        => _api.GetAsync<List<HrAppraisalDto>>($"{Root}/appraisals");

    public Task<HrAppraisalDto> SaveAppraisalAsync(int? id, HrAppraisalSaveDto dto)
        => _api.PostAsync<HrAppraisalDto>($"{Root}/appraisals?id={id}", dto);

    public Task DeleteAppraisalAsync(int id)
        => _api.DeleteAsync($"{Root}/appraisals/{id}");

    public Task<List<HrAppraisalKpiDto>> GetKpisAsync(int appraisalId)
        => _api.GetAsync<List<HrAppraisalKpiDto>>($"{Root}/appraisals/{appraisalId}/kpis");

    public Task<HrAppraisalKpiDto> SaveKpiAsync(int appraisalId, int? kpiId, HrAppraisalKpiSaveDto dto)
        => _api.PostAsync<HrAppraisalKpiDto>($"{Root}/appraisals/{appraisalId}/kpis?kpiId={kpiId}", dto);

    public Task DeleteKpiAsync(int kpiId)
        => _api.DeleteAsync($"{Root}/appraisals/kpis/{kpiId}");

    public Task SaveScoreAsync(HrAppraisalScoreSaveDto dto)
        => _api.PostAsync<object>($"{Root}/appraisals/scores", dto);

    public Task<List<HrAppraisalScoreDto>> GetScoresAsync(int appraisalId, int? employeeId = null)
        => _api.GetAsync<List<HrAppraisalScoreDto>>($"{Root}/appraisals/{appraisalId}/scores?employeeId={employeeId}");

    public Task<List<HrAppraisalResultDto>> GetResultsAsync(int appraisalId)
        => _api.GetAsync<List<HrAppraisalResultDto>>($"{Root}/appraisals/{appraisalId}/results");

    public Task<HrAppraisalDecreeProposalResultDto> ProposeDecreesAsync(HrAppraisalDecreeProposalDto dto)
        => _api.PostAsync<HrAppraisalDecreeProposalResultDto>($"{Root}/appraisals/{dto.AppraisalId}/propose-decrees", dto);

    public Task<HrAppraisalDto> SetAppraisalStatusAsync(int id, int status, string? by)
        => _api.PostAsync<HrAppraisalDto>($"{Root}/appraisals/{id}/status?status={status}&by={Uri.EscapeDataString(by ?? "")}", null);

    // ==================== پرونده‌ی خود پرسنل ====================

    public async Task<HrMyProfileDto?> GetMyProfileAsync()
    {
        try { return await _api.GetAsync<HrMyProfileDto>($"{Root}/my/profile"); }
        catch { return null; }
    }

    public Task SubmitMyProfileEditAsync(HrProfileEditRequestSaveDto dto)
        => _api.PostAsync<object>($"{Root}/my/profile-requests", dto);

    public Task<List<HrProfileEditRequestDto>> ListProfileRequestsAsync(bool? onlyPending = null)
        => _api.GetAsync<List<HrProfileEditRequestDto>>($"{Root}/profile-requests?onlyPending={onlyPending}");

    public Task<HrProfileEditRequestDto> DecideProfileRequestAsync(int id, bool approve, string? note)
        => _api.PostAsync<HrProfileEditRequestDto>($"{Root}/profile-requests/{id}/decide?approve={approve}&note={Uri.EscapeDataString(note ?? "")}", null);
}
