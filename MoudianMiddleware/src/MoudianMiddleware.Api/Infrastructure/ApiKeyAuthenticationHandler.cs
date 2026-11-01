using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Context;

namespace MoudianMiddleware.Api.Infrastructure;

public static class ApiKeyDefaults { public const string Scheme = "ApiKey"; }

public sealed class ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    MoudianDbContext db, TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var values) || values.Count != 1) return AuthenticateResult.NoResult();
        var key = values[0];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 24) return AuthenticateResult.Fail("Invalid API key.");
        var prefix = key[..8];
        var client = await db.ApiClients.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.KeyPrefix == prefix, Context.RequestAborted).ConfigureAwait(false);
        if (client is null || client.IsDeleted || !client.IsActive || client.ExpiresAt is { } expiry && expiry <= clock.GetUtcNow())
            return AuthenticateResult.Fail("Invalid API key.");
        byte[] expected;
        try { expected = Convert.FromBase64String(client.KeyHash); } catch (FormatException) { return AuthenticateResult.Fail("Invalid API key."); }
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var valid = expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        CryptographicOperations.ZeroMemory(expected); CryptographicOperations.ZeroMemory(actual);
        if (!valid) return AuthenticateResult.Fail("Invalid API key.");
        var claims = new List<Claim> { new(ApiClaims.TenantId, client.TenantId.ToString()), new(ApiClaims.ApiClientId, client.Id.ToString()),
            new(ApiClaims.RequestsPerMinute, Math.Clamp(client.RequestsPerMinute, 1, 10000).ToString()), new(ClaimTypes.Name, client.Name) };
        foreach (var scope in Enum.GetValues<ApiScope>().Where(scope => scope != ApiScope.None && client.Scopes.HasFlag(scope))) claims.Add(new Claim(ApiClaims.ApiScope, scope.ToString()));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }
}
