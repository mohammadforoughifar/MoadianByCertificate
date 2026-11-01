using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

// =====================================================================
// سرویس کلاینت — تنظیمات ساختار شماره نامه (اتوماسیون اداری)
// سه نوع نامه: داخلی (TypeForm=1) / صادره (2) / وارده (3)
// =====================================================================

public interface ILetterStructureService
{
    /// <summary>داده‌ی صفحه: ساختار هر سه نوع + سازمان‌های موجود</summary>
    Task<LetterStructurePageDto> GetPageAsync();

    /// <summary>ساختار یک نوع نامه</summary>
    Task<LetterStructureItemDto> GetAsync(int typeForm);

    /// <summary>جایگزینی ساختار یک نوع نامه</summary>
    Task<LetterStructureItemDto> UpdateAsync(int typeForm, List<string> parts);
}

public class LetterStructureService : ILetterStructureService
{
    private readonly IApiClient _api;

    public LetterStructureService(IApiClient api) => _api = api;

    public Task<LetterStructurePageDto> GetPageAsync() =>
        _api.GetAsync<LetterStructurePageDto>("api/letter-structures");

    public Task<LetterStructureItemDto> GetAsync(int typeForm) =>
        _api.GetAsync<LetterStructureItemDto>($"api/letter-structures/{typeForm}");

    public Task<LetterStructureItemDto> UpdateAsync(int typeForm, List<string> parts) =>
        _api.PutAsync<LetterStructureItemDto>($"api/letter-structures/{typeForm}", new UpdateLetterStructureDto { Parts = parts });
}
