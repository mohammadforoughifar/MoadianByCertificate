using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MoudianMiddleware.Contracts.Common;

namespace MoudianMiddleware.Client.Services;

public sealed class AuthSessionService(IHttpClientFactory clients, TokenStore store, ClientAuthenticationStateProvider authState, NavigationManager navigation)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public IReadOnlyList<TenantMembership> Memberships { get; private set; } = Array.Empty<TenantMembership>();
    public string? Error { get; private set; }

    public async Task<bool> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        Error = null;
        try
        {
            var client = clients.CreateClient("Refresh");
            using var response = await client.PostAsJsonAsync("auth/login", new { userName, password }, JsonOptions, cancellationToken).ConfigureAwait(false);
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResult>>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || envelope?.Data is not { } login)
            {
                Error = envelope?.Message ?? "نام کاربری یا گذرواژه نادرست است.";
                return false;
            }
            Memberships = login.Memberships;
            store.Set(new ClientTokenPair(login.AccessToken, login.RefreshToken, login.AccessTokenExpiresAt, login.RefreshTokenExpiresAt));
            authState.NotifyChanged();
            navigation.NavigateTo(Memberships.Count > 1 ? "/tenant-selection" : "/dashboard");
            return true;
        }
        catch (HttpRequestException) { Error = "ارتباط با سرور برقرار نشد."; return false; }
        catch (JsonException) { Error = "پاسخ سرور قابل خواندن نیست."; return false; }
    }

    public async Task<bool> SelectTenantAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        Error = null;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"auth/select-tenant/{tenantId}");
        if (store.Current is { } current) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", current.AccessToken);
        try
        {
            using var response = await clients.CreateClient("Refresh").SendAsync(request, cancellationToken).ConfigureAwait(false);
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<SelectedTenantResult>>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || envelope?.Data is not { } result)
            {
                Error = envelope?.Message ?? "انتخاب مودی انجام نشد.";
                return false;
            }
            store.Set(new ClientTokenPair(result.AccessToken, result.RefreshToken, result.AccessTokenExpiresAt, result.RefreshTokenExpiresAt));
            authState.NotifyChanged();
            navigation.NavigateTo("/dashboard");
            return true;
        }
        catch (HttpRequestException) { Error = "ارتباط با سرور برقرار نشد."; return false; }
    }

    public void SignOut()
    {
        store.Clear();
        Memberships = Array.Empty<TenantMembership>();
        authState.NotifyChanged();
        navigation.NavigateTo("/login", forceLoad: false);
    }

    public sealed record TenantMembership(int TenantId, string TenantName, IReadOnlyList<string> Permissions);
    public sealed record LoginResult(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt, IReadOnlyList<TenantMembership> Memberships);
    public sealed record SelectedTenantResult(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt);
}
