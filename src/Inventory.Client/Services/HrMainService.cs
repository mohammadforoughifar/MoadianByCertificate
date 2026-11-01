using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>سرویس کلاینت «منابع انسانی اصلی — مدیریت پایه سازمانی» (HrMain)</summary>
public interface IHrMainService
{
    Task<HrMainOverviewDto> GetOverviewAsync();

    Task<HrMainCompanyDto> GetCompanyAsync();
    Task<HrMainCompanyDto> SaveCompanyAsync(HrMainCompanySaveDto dto);
    Task<HrMainCompanyDto> UploadLogoAsync(Stream stream, string fileName, string? contentType);
    Task DeleteLogoAsync();

    Task<List<HrMainBranchDto>> ListBranchesAsync();
    Task<HrMainBranchDto> SaveBranchAsync(int? id, HrMainBranchSaveDto dto);
    Task DeleteBranchAsync(int id);

    Task<List<HrMainOrgNodeDto>> GetOrgTreeAsync();
    Task<List<HrMainOrgNodeDto>> ListOrgNodesAsync();
    Task<HrMainOrgNodeDto> SaveOrgNodeAsync(int? id, HrMainOrgNodeSaveDto dto);
    Task DeleteOrgNodeAsync(int id);

    Task<List<HrMainPositionDto>> ListPositionsAsync(int? orgNodeId = null);
    Task<HrMainPositionDto> SavePositionAsync(int? id, HrMainPositionSaveDto dto);
    Task DeletePositionAsync(int id);

    Task<HrMainLocaleDto> GetLocaleAsync();
    Task<HrMainLocaleDto> SaveLocaleAsync(HrMainLocaleDto dto);

    Task<HrMainRulesDto> GetRulesAsync();
    Task<HrMainRulesDto> SaveRulesAsync(HrMainRulesDto dto);

    Task<List<HrMainHolidayDto>> ListHolidaysAsync(int? jalaliYear = null);
    Task<HrMainHolidayDto> SaveHolidayAsync(HrMainHolidaySaveDto dto);
    Task DeleteHolidayAsync(int id);

    Task<HrMainChangeLogListResult> SearchChangeLogAsync(string? entity, string? q, DateTime? from, DateTime? to, int skip, int take);
    Task<HrMainCompareResultDto> CompareStructureAsync(DateTime from, DateTime to);
}

public class HrMainService : IHrMainService
{
    private readonly IApiClient _api;
    public HrMainService(IApiClient api) => _api = api;

    private const string Root = "api/hr-main";

    public Task<HrMainOverviewDto> GetOverviewAsync()
        => _api.GetAsync<HrMainOverviewDto>($"{Root}/overview");

    public Task<HrMainCompanyDto> GetCompanyAsync()
        => _api.GetAsync<HrMainCompanyDto>($"{Root}/company");

    public Task<HrMainCompanyDto> SaveCompanyAsync(HrMainCompanySaveDto dto)
        => _api.PutAsync<HrMainCompanyDto>($"{Root}/company", dto);

    public Task<HrMainCompanyDto> UploadLogoAsync(Stream stream, string fileName, string? contentType)
        => _api.PostFileAsync<HrMainCompanyDto>($"{Root}/company/logo", stream, fileName, "file", contentType);

    public Task DeleteLogoAsync()
        => _api.DeleteAsync($"{Root}/company/logo");

    public Task<List<HrMainBranchDto>> ListBranchesAsync()
        => _api.GetAsync<List<HrMainBranchDto>>($"{Root}/branches");

    public Task<HrMainBranchDto> SaveBranchAsync(int? id, HrMainBranchSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrMainBranchDto>($"{Root}/branches/{id}", dto)
            : _api.PostAsync<HrMainBranchDto>($"{Root}/branches", dto);

    public Task DeleteBranchAsync(int id)
        => _api.DeleteAsync($"{Root}/branches/{id}");

    public Task<List<HrMainOrgNodeDto>> GetOrgTreeAsync()
        => _api.GetAsync<List<HrMainOrgNodeDto>>($"{Root}/org/tree");

    public Task<List<HrMainOrgNodeDto>> ListOrgNodesAsync()
        => _api.GetAsync<List<HrMainOrgNodeDto>>($"{Root}/org/nodes");

    public Task<HrMainOrgNodeDto> SaveOrgNodeAsync(int? id, HrMainOrgNodeSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrMainOrgNodeDto>($"{Root}/org/nodes/{id}", dto)
            : _api.PostAsync<HrMainOrgNodeDto>($"{Root}/org/nodes", dto);

    public Task DeleteOrgNodeAsync(int id)
        => _api.DeleteAsync($"{Root}/org/nodes/{id}");

    public Task<List<HrMainPositionDto>> ListPositionsAsync(int? orgNodeId = null)
        => _api.GetAsync<List<HrMainPositionDto>>(
            orgNodeId is > 0 ? $"{Root}/positions?orgNodeId={orgNodeId}" : $"{Root}/positions");

    public Task<HrMainPositionDto> SavePositionAsync(int? id, HrMainPositionSaveDto dto)
        => id is > 0
            ? _api.PutAsync<HrMainPositionDto>($"{Root}/positions/{id}", dto)
            : _api.PostAsync<HrMainPositionDto>($"{Root}/positions", dto);

    public Task DeletePositionAsync(int id)
        => _api.DeleteAsync($"{Root}/positions/{id}");

    public Task<HrMainLocaleDto> GetLocaleAsync()
        => _api.GetAsync<HrMainLocaleDto>($"{Root}/locale");

    public Task<HrMainLocaleDto> SaveLocaleAsync(HrMainLocaleDto dto)
        => _api.PutAsync<HrMainLocaleDto>($"{Root}/locale", dto);

    public Task<HrMainRulesDto> GetRulesAsync()
        => _api.GetAsync<HrMainRulesDto>($"{Root}/rules");

    public Task<HrMainRulesDto> SaveRulesAsync(HrMainRulesDto dto)
        => _api.PutAsync<HrMainRulesDto>($"{Root}/rules", dto);

    public Task<List<HrMainHolidayDto>> ListHolidaysAsync(int? jalaliYear = null)
        => _api.GetAsync<List<HrMainHolidayDto>>(
            jalaliYear is > 0 ? $"{Root}/holidays?year={jalaliYear}" : $"{Root}/holidays");

    public Task<HrMainHolidayDto> SaveHolidayAsync(HrMainHolidaySaveDto dto)
        => _api.PostAsync<HrMainHolidayDto>($"{Root}/holidays", dto);

    public Task DeleteHolidayAsync(int id)
        => _api.DeleteAsync($"{Root}/holidays/{id}");

    public Task<HrMainChangeLogListResult> SearchChangeLogAsync(string? entity, string? q, DateTime? from, DateTime? to, int skip, int take)
    {
        var qs = new List<string> { $"skip={skip}", $"take={take}" };
        if (!string.IsNullOrWhiteSpace(entity)) qs.Add($"entity={Uri.EscapeDataString(entity)}");
        if (!string.IsNullOrWhiteSpace(q)) qs.Add($"q={Uri.EscapeDataString(q)}");
        if (from != null) qs.Add($"from={from:yyyy-MM-dd}");
        if (to != null) qs.Add($"to={to:yyyy-MM-dd}");
        return _api.GetAsync<HrMainChangeLogListResult>($"{Root}/org/change-log?{string.Join("&", qs)}");
    }

    public Task<HrMainCompareResultDto> CompareStructureAsync(DateTime from, DateTime to)
        => _api.GetAsync<HrMainCompareResultDto>($"{Root}/org/compare?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
}
