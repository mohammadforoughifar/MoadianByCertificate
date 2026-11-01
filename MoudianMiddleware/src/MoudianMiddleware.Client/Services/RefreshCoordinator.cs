using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MoudianMiddleware.Contracts.Common;

namespace MoudianMiddleware.Client.Services;

/// <summary>Serializes refreshes so concurrent 401s rotate the refresh token only once.</summary>
public sealed class RefreshCoordinator(IHttpClientFactory clients, TokenStore store, ClientAuthenticationStateProvider authState)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> RefreshIfNeededAsync(string? observedAccessToken, bool force, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = store.Current;
            if (current is null) return null;
            if (!force && current.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(45)) return current.AccessToken;
            if (observedAccessToken is not null && !string.Equals(observedAccessToken, current.AccessToken, StringComparison.Ordinal))
                return current.AccessToken;

            try
            {
                var client = clients.CreateClient("Refresh");
                using var response = await client.PostAsJsonAsync("auth/refresh", new { refreshToken = current.RefreshToken }, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized || !response.IsSuccessStatusCode)
                {
                    store.Clear();
                    authState.NotifyChanged();
                    return null;
                }

                var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<RefreshedTokens>>(JsonOptions, cancellationToken).ConfigureAwait(false);
                if (envelope?.Data is not { } pair || string.IsNullOrWhiteSpace(pair.AccessToken) || string.IsNullOrWhiteSpace(pair.RefreshToken))
                {
                    store.Clear();
                    authState.NotifyChanged();
                    return null;
                }
                store.Set(new ClientTokenPair(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt, pair.RefreshTokenExpiresAt));
                authState.NotifyChanged();
                return pair.AccessToken;
            }
            catch (HttpRequestException)
            {
                // Network failures do not silently prolong a session. Sign out to avoid retry loops with an invalid token.
                store.Clear();
                authState.NotifyChanged();
                return null;
            }
            catch (JsonException)
            {
                store.Clear();
                authState.NotifyChanged();
                return null;
            }
        }
        finally { _gate.Release(); }
    }

    public sealed record RefreshedTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt);
}
