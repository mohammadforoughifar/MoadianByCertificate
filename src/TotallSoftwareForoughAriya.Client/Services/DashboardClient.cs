using System.Text.Json;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>نتیجهٔ کاتالوگ ویجت‌ها — همراه با اینکه آیا کاربر اجازهٔ طراحی دارد.</summary>
public class DashCatalogResult
{
    public bool CanDesign { get; set; }
    public List<WidgetDefDto> Widgets { get; set; } = new();
}

public interface IDashboardClient
{
    Task<DashCatalogResult> GetCatalogAsync();
    Task<WidgetDataDto?> GetDataAsync(string key, DashChartType? chartType, DashRange range,
        IReadOnlyDictionary<string, string>? config = null);
    Task<List<UserDashboardDto>> GetDashboardsAsync();
    Task CreateAsync(string name);
    Task SaveAsync(UserDashboardDto dashboard);
    Task SetDefaultAsync(int id);
    Task DeleteAsync(int id);
}

/// <summary>کلاینت «داشبورد شخصی» — همهٔ مسیرها زیر api/my-dashboards.</summary>
public class DashboardClient : IDashboardClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IApiClient _api;
    public DashboardClient(IApiClient api) => _api = api;

    public Task<DashCatalogResult> GetCatalogAsync() =>
        _api.GetAsync<DashCatalogResult>("api/my-dashboards/widget-catalog");

    public async Task<WidgetDataDto?> GetDataAsync(string key, DashChartType? chartType, DashRange range,
        IReadOnlyDictionary<string, string>? config = null)
    {
        var qs = new List<string> { $"range={(int)range}" };
        if (chartType is not null) qs.Add($"chartType={(int)chartType}");
        if (config is { Count: > 0 })
            qs.Add("cfg=" + Uri.EscapeDataString(JsonSerializer.Serialize(config, JsonOpts)));

        try
        {
            return await _api.GetAsync<WidgetDataDto>($"api/my-dashboards/widget-data/{key}?{string.Join("&", qs)}");
        }
        catch
        {
            // ۴۰۳ یا خطای شبکه — ویجت پیام خطا نشان می‌دهد، کل داشبورد نمی‌شکند
            return new WidgetDataDto { Key = key, Error = "دادهٔ این ویجت در دسترس نیست (دسترسی یا خطای سرور)." };
        }
    }

    public Task<List<UserDashboardDto>> GetDashboardsAsync() =>
        _api.GetAsync<List<UserDashboardDto>>("api/my-dashboards");

    public async Task CreateAsync(string name) =>
        await _api.PostAsync<object>("api/my-dashboards", new { name, isDefault = false, widgets = Array.Empty<object>() });

    public async Task SaveAsync(UserDashboardDto d) =>
        await _api.PutAsync<object>($"api/my-dashboards/{d.Id}",
            new { name = d.Name, isDefault = d.IsDefault, widgets = d.Widgets });

    public async Task SetDefaultAsync(int id) =>
        await _api.PostAsync<object>($"api/my-dashboards/{id}/default", null);

    public Task DeleteAsync(int id) => _api.DeleteAsync($"api/my-dashboards/{id}");
}
