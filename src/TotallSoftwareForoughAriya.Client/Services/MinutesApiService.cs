using System.Net.Http.Json;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>کلاینت API ماژول «فرم‌های متفرقه — صورتجلسه»</summary>
public interface IMinutesApiService
{
    Task<List<MinutesListItemDto>> GetListAsync(string? search, string? status);
    Task<MinutesDetailDto?> GetDetailAsync(int id);
    Task<int> SaveAsync(SaveMinutesDto dto);
    Task SubmitAsync(int id, bool includeAbsentees);
    Task DeleteAsync(int id);
    Task<MinutesItemDto> AddItemAsync(int minutesId, SaveMinutesItemDto dto);
    Task<MinutesItemDto> UpdateItemAsync(int itemId, SaveMinutesItemDto dto);
    Task DeleteItemAsync(int itemId);
    Task<MinutesItemDto> RespDecisionAsync(int itemId, bool approved, string? note);
    Task<MinutesItemDto> FollowUpDecisionAsync(int itemId, string decision, string? note);
    Task SignAsync(int minutesId, string signatureBase64);
    Task RemoveSignatureAsync(int minutesId, int participantUserId);
    Task<int> ToInnerLetterAsync(int minutesId, MinutesToInnerLetterDto dto);
    Task<int> ToOutgoingLetterAsync(int minutesId, MinutesToOutgoingLetterDto dto);
    Task<int> SendEmailAsync(int minutesId, MinutesSendEmailDto dto);
    /// <summary>آدرس دانلود PDF چاپی با سربرگ</summary>
    string PrintUrl(int id);
}

public class MinutesApiService : IMinutesApiService
{
    private readonly IApiClient _api;

    public MinutesApiService(IApiClient api)
    {
        _api = api;
    }

    public Task<List<MinutesListItemDto>> GetListAsync(string? search, string? status)
        => _api.GetAsync<List<MinutesListItemDto>>(
            $"api/meeting-minutes?search={Uri.EscapeDataString(search ?? "")}&status={status ?? ""}");

    public Task<MinutesDetailDto?> GetDetailAsync(int id)
        => _api.GetAsync<MinutesDetailDto>($"api/meeting-minutes/{id}");

    public async Task<int> SaveAsync(SaveMinutesDto dto)
    {
        var r = await _api.PostAsync<object>("api/meeting-minutes", dto);
        return ExtractId(r);
    }

    public Task SubmitAsync(int id, bool includeAbsentees)
        => _api.PostAsync<object>($"api/meeting-minutes/{id}/submit",
            new SubmitMinutesDto { IncludeAbsentees = includeAbsentees });

    public Task DeleteAsync(int id)
        => _api.DeleteAsync($"api/meeting-minutes/{id}");

    public Task<MinutesItemDto> AddItemAsync(int minutesId, SaveMinutesItemDto dto)
        => _api.PostAsync<MinutesItemDto>($"api/meeting-minutes/{minutesId}/items", dto);

    public Task<MinutesItemDto> UpdateItemAsync(int itemId, SaveMinutesItemDto dto)
        => _api.PutAsync<MinutesItemDto>($"api/meeting-minutes/items/{itemId}", dto);

    public Task DeleteItemAsync(int itemId)
        => _api.DeleteAsync($"api/meeting-minutes/items/{itemId}");

    public Task<MinutesItemDto> RespDecisionAsync(int itemId, bool approved, string? note)
        => _api.PostAsync<MinutesItemDto>($"api/meeting-minutes/items/{itemId}/resp-decision",
            new MinutesRespDecisionDto { Approved = approved, Note = note });

    public Task<MinutesItemDto> FollowUpDecisionAsync(int itemId, string decision, string? note)
        => _api.PostAsync<MinutesItemDto>($"api/meeting-minutes/items/{itemId}/followup-decision",
            new MinutesFollowUpDecisionDto { Decision = decision, Note = note });

    public Task SignAsync(int minutesId, string signatureBase64)
        => _api.PostAsync<object>($"api/meeting-minutes/{minutesId}/sign",
            new MinutesSignDto { SignatureBase64 = signatureBase64 });

    public Task RemoveSignatureAsync(int minutesId, int participantUserId)
        => _api.DeleteAsync($"api/meeting-minutes/{minutesId}/participants/{participantUserId}/signature");

    public async Task<int> ToInnerLetterAsync(int minutesId, MinutesToInnerLetterDto dto)
    {
        var r = await _api.PostAsync<object>(
            $"api/meeting-minutes/{minutesId}/to-inner-letter", dto);
        return ExtractId(r);
    }

    public async Task<int> ToOutgoingLetterAsync(int minutesId, MinutesToOutgoingLetterDto dto)
    {
        var r = await _api.PostAsync<object>(
            $"api/meeting-minutes/{minutesId}/to-outgoing-letter", dto);
        return ExtractId(r);
    }

    public async Task<int> SendEmailAsync(int minutesId, MinutesSendEmailDto dto)
    {
        var r = await _api.PostAsync<object>(
            $"api/meeting-minutes/{minutesId}/send-email", dto);
        return ExtractId(r);
    }

    public string PrintUrl(int id) => _api.BuildUrl($"api/meeting-minutes/{id}/print");

    private static int ExtractId(object? r)
    {
        if (r is null) return 0;
        var j = System.Text.Json.JsonSerializer.Serialize(r);
        using var doc = System.Text.Json.JsonDocument.Parse(j);
        if (doc.RootElement.TryGetProperty("id", out var id) && id.TryGetInt32(out var v)) return v;
        if (doc.RootElement.TryGetProperty("letterId", out var lid) && lid.TryGetInt32(out var lv)) return lv;
        return 0;
    }
}
