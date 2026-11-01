using System.Net.Http.Json;
using System.Text.Json;
using MoudianMiddleware.Contracts.Common;

namespace MoudianMiddleware.Client.Services;

public sealed class BackendApi(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string route, CancellationToken ct = default) => await SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, route), ct).ConfigureAwait(false);
    public async Task<T> PostAsync<TRequest, T>(string route, TRequest body, CancellationToken ct = default) => await SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body, options: JsonOptions) }, ct).ConfigureAwait(false);
    public async Task<T> PutAsync<TRequest, T>(string route, TRequest body, CancellationToken ct = default) => await SendAsync<T>(new HttpRequestMessage(HttpMethod.Put, route) { Content = JsonContent.Create(body, options: JsonOptions) }, ct).ConfigureAwait(false);
    public async Task DeleteAsync(string route, CancellationToken ct = default) => _ = await SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Delete, route), ct).ConfigureAwait(false);

    public async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct = default)
    {
        using (request)
        using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            ApiResponse<T>? envelope;
            try { envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions, ct).ConfigureAwait(false); }
            catch (JsonException) { throw new ApiClientException("پاسخ سرور قابل خواندن نیست.", response.StatusCode); }
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.IsSuccess)
                throw new ApiClientException(envelope?.Message ?? "درخواست ناموفق بود.", response.StatusCode, envelope?.Errors);
            return envelope.Data!;
        }
    }
}

public sealed class ApiClientException(string message, System.Net.HttpStatusCode statusCode, IReadOnlyList<ApiError>? errors = null) : Exception(message)
{
    public System.Net.HttpStatusCode StatusCode { get; } = statusCode;
    public IReadOnlyList<ApiError> Errors { get; } = errors ?? Array.Empty<ApiError>();
}
