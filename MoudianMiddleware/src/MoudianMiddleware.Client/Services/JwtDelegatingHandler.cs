using System.Net.Http.Headers;

namespace MoudianMiddleware.Client.Services;

public sealed class JwtDelegatingHandler(TokenStore tokens, RefreshCoordinator refresh) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.RequestUri?.ToString() ?? string.Empty;
        if (path.Contains("auth/login", StringComparison.OrdinalIgnoreCase) || path.Contains("auth/refresh", StringComparison.OrdinalIgnoreCase))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var before = tokens.Current?.AccessToken;
        var token = await refresh.RefreshIfNeededAsync(before, force: false, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized || string.IsNullOrWhiteSpace(before))
            return response;

        var replacement = await refresh.RefreshIfNeededAsync(before, force: true, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(replacement) || request.Method != HttpMethod.Get) return response;
        response.Dispose();
        using var retry = new HttpRequestMessage(HttpMethod.Get, request.RequestUri);
        foreach (var header in request.Headers)
            if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", replacement);
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }
}
