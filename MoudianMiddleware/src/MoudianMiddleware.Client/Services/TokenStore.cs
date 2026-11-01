namespace MoudianMiddleware.Client.Services;

/// <summary>Holds access and refresh tokens in memory only; a browser reload intentionally requires sign-in again.</summary>
public sealed class TokenStore
{
    public ClientTokenPair? Current { get; private set; }
    public void Set(ClientTokenPair tokens) => Current = tokens;
    public void Clear() => Current = null;
}

public sealed record ClientTokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt);
