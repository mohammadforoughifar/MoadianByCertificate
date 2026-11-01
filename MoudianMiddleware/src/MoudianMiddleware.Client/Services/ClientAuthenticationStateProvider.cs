using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace MoudianMiddleware.Client.Services;

public sealed class ClientAuthenticationStateProvider(TokenStore tokens) : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var principal = tokens.Current is { } current ? ParsePrincipal(current.AccessToken) : Anonymous;
        return Task.FromResult(new AuthenticationState(principal));
    }

    public void NotifyChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public static ClaimsPrincipal ParsePrincipal(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return Anonymous;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            var claims = new List<Claim>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                    claims.AddRange(property.Value.EnumerateArray().Select(value => new Claim(property.Name, value.ToString())));
                else if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                    claims.Add(new Claim(property.Name, property.Value.ToString()));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer", "name", ClaimTypes.Role));
        }
        catch (FormatException) { return Anonymous; }
        catch (JsonException) { return Anonymous; }
    }
}
