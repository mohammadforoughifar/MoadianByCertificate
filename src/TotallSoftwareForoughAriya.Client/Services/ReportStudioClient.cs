using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IReportStudioClient
{
    Task<RsCatalogDto> GetCatalogAsync();
    Task<RsResultDto> PreviewAsync(RsQueryDto query, List<RsPromptValueDto>? prompts = null,
        int page = 1, int pageSize = 100);
    Task<List<RsReportDto>> ListAsync();
    Task<RsReportDto?> GetAsync(int id);
    Task<int> CreateAsync(RsReportDto dto);
    Task UpdateAsync(int id, RsReportDto dto);
    Task DeleteAsync(int id);
    Task<RsResultDto> RunAsync(int id, List<RsPromptValueDto>? prompts = null,
        int page = 1, int pageSize = 100);
    Task<RsShareTargetsDto> GetShareTargetsAsync(string? search = null);
    Task SetSharesAsync(int id, RsReportDto dto);
    string ExportUrl(int id);
}

/// <summary>کلاینت گزارش‌ساز حرفه‌ای — همهٔ مسیرها زیر api/report-studio.</summary>
public class ReportStudioClient : IReportStudioClient
{
    private const string Base = "api/report-studio";
    private readonly IApiClient _api;

    public ReportStudioClient(IApiClient api) => _api = api;

    public Task<RsCatalogDto> GetCatalogAsync() =>
        _api.GetAsync<RsCatalogDto>($"{Base}/catalog");

    public async Task<RsResultDto> PreviewAsync(RsQueryDto query,
        List<RsPromptValueDto>? prompts = null, int page = 1, int pageSize = 100)
    {
        try
        {
            return await _api.PostAsync<RsResultDto>($"{Base}/preview",
                new RsRunRequestDto { Query = query, Prompts = prompts ?? new(), Page = page, PageSize = pageSize })
                ?? new RsResultDto { Ok = false, Error = "پاسخی از سرور دریافت نشد." };
        }
        catch (Exception ex)
        {
            // خطای شبکه هم باید به‌جای استثنا، پیام قابل‌فهم بدهد
            return new RsResultDto { Ok = false, Error = "ارتباط با سرور برقرار نشد: " + ex.Message };
        }
    }

    public Task<List<RsReportDto>> ListAsync() =>
        _api.GetAsync<List<RsReportDto>>($"{Base}/reports");

    public async Task<RsReportDto?> GetAsync(int id)
    {
        try { return await _api.GetAsync<RsReportDto>($"{Base}/reports/{id}"); }
        catch { return null; }
    }

    public async Task<int> CreateAsync(RsReportDto dto)
    {
        var res = await _api.PostAsync<CreateResult>($"{Base}/reports", dto);
        return res?.Id ?? 0;
    }

    public Task UpdateAsync(int id, RsReportDto dto) =>
        _api.PutAsync<object>($"{Base}/reports/{id}", dto);

    public Task DeleteAsync(int id) =>
        _api.DeleteAsync($"{Base}/reports/{id}");

    public async Task<RsResultDto> RunAsync(int id, List<RsPromptValueDto>? prompts = null,
        int page = 1, int pageSize = 100)
    {
        try
        {
            return await _api.PostAsync<RsResultDto>($"{Base}/reports/{id}/run",
                new RsRunRequestDto { Prompts = prompts ?? new(), Page = page, PageSize = pageSize })
                ?? new RsResultDto { Ok = false, Error = "پاسخی از سرور دریافت نشد." };
        }
        catch (Exception ex)
        {
            return new RsResultDto { Ok = false, Error = "اجرای گزارش ناموفق بود: " + ex.Message };
        }
    }

    public Task<RsShareTargetsDto> GetShareTargetsAsync(string? search = null) =>
        _api.GetAsync<RsShareTargetsDto>($"{Base}/share-targets" +
            (string.IsNullOrWhiteSpace(search) ? "" : $"?q={Uri.EscapeDataString(search)}"));

    public Task SetSharesAsync(int id, RsReportDto dto) =>
        _api.PutAsync<object>($"{Base}/reports/{id}/shares", dto);

    public string ExportUrl(int id) => $"{Base}/reports/{id}/export";

    private class CreateResult
    {
        public int Id { get; set; }
        public string? Message { get; set; }
    }
}
