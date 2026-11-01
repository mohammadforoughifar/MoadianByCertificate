using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>
/// کلاینت ورود انبوه. کارهای سنگین (پیش‌نمایش و آپلود) را جاوااسکریپت مستقیم با API
/// انجام می‌دهد؛ اینجا فقط آدرس پایه، وضعیت پیکربندی و سابقهٔ نوبت‌ها می‌آید.
/// </summary>
public interface IDocImportClient
{
    /// <summary>آدرس پایهٔ API ورود انبوه — برای جاوااسکریپت.</summary>
    string ApiBase { get; }

    Task<DocImportConfigDto> GetConfigAsync();
    Task<List<DocImportBatchDto>> GetBatchesAsync(int take = 20);
    string ReportUrl(int batchId);
}

public class DocImportClient(IApiClient api) : IDocImportClient
{
    private const string Base = "api/doc-archive/import";

    public string ApiBase => api.BuildUrl(Base);

    public Task<DocImportConfigDto> GetConfigAsync()
        => api.GetAsync<DocImportConfigDto>($"{Base}/config");

    public Task<List<DocImportBatchDto>> GetBatchesAsync(int take = 20)
        => api.GetAsync<List<DocImportBatchDto>>($"{Base}/batches?take={take}");

    public string ReportUrl(int batchId) => api.BuildUrl($"{Base}/batches/{batchId}/report.xlsx");
}
