using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using MoudianMiddleware.Contracts.Constants;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.Api.Services;

public sealed record LoginRequest(string UserName, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record TenantMembership(int TenantId, string TenantName, IReadOnlyList<string> Permissions);
public sealed record TokenResult(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt);
public sealed record LoginResult(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt, IReadOnlyList<TenantMembership> Memberships);

public interface IAuthService
{
    Task<LoginResult> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct);
    Task<TokenResult> RefreshAsync(RefreshRequest request, string? ip, string? userAgent, CancellationToken ct);
    Task<TokenResult> SelectTenantAsync(int userId, int tenantId, string? ip, string? userAgent, CancellationToken ct);
}

public sealed class AuthService(MoudianDbContext db, IPasswordHasher passwords, JwtOptions options, TimeProvider clock) : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan _lockoutDuration = TimeSpan.FromMinutes(15);
    private const int RefreshBytes = 48;

    public async Task<LoginResult> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            throw new UnauthorizedAccessException("Invalid credentials.");
        var normalized = request.UserName.Trim().ToUpperInvariant();
        var user = await db.Users.IgnoreQueryFilters()
            .Include(x => x.UserTenants).ThenInclude(x => x.Tenant)
            .Include(x => x.UserTenants).ThenInclude(x => x.Role).ThenInclude(x => x!.Permissions)
            .SingleOrDefaultAsync(x => x.NormalizedUserName == normalized, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var passwordValid = user is not null && passwords.Verify(request.Password, user.PasswordHash);
        if (user is null || user.IsDeleted || !user.IsActive || (user.LockoutEndAt > now) || !passwordValid)
        {
            if (user is not null && !user.IsDeleted && user.IsActive && (user.LockoutEndAt is null || user.LockoutEndAt <= now))
            {
                user.AccessFailedCount++;
                if (user.AccessFailedCount >= MaxFailedAttempts) user.LockoutEndAt = now.Add(_lockoutDuration);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        var memberships = ActiveMemberships(user).ToArray();
        if (memberships.Length == 0) throw new UnauthorizedAccessException("No active tenant membership.");
        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        user.LastLoginAt = now;
        var first = memberships[0];
        var pair = CreateTokens(user, first.TenantId, first.Permissions, ip, userAgent);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LoginResult(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt, pair.RefreshTokenExpiresAt, memberships);
    }

    public async Task<TokenResult> SelectTenantAsync(int userId, int tenantId, string? ip, string? userAgent, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().Include(x => x.UserTenants).ThenInclude(x => x.Tenant)
            .Include(x => x.UserTenants).ThenInclude(x => x.Role).ThenInclude(x => x!.Permissions)
            .SingleOrDefaultAsync(x => x.Id == userId && !x.IsDeleted && x.IsActive, ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Inactive user.");
        var membership = ActiveMemberships(user).SingleOrDefault(x => x.TenantId == tenantId)
            ?? throw new UnauthorizedAccessException("Tenant membership not found.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await RevokeActiveFamilyAsync(userId, ip, ct).ConfigureAwait(false);
        var pair = CreateTokens(user, tenantId, membership.Permissions, ip, userAgent);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return pair;
    }

    public async Task<TokenResult> RefreshAsync(RefreshRequest request, string? ip, string? userAgent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) throw new UnauthorizedAccessException("Invalid refresh token.");
        var hash = Hash(request.RefreshToken);
        var stored = await db.RefreshTokens.IgnoreQueryFilters().Include(x => x.User).ThenInclude(x => x!.UserTenants).ThenInclude(x => x.Tenant)
            .Include(x => x.User).ThenInclude(x => x!.UserTenants).ThenInclude(x => x.Role).ThenInclude(x => x!.Permissions)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, ct).ConfigureAwait(false);
        if (stored is null) throw new UnauthorizedAccessException("Invalid refresh token.");
        if (stored.RevokedAt is not null)
        {
            await using var replayTransaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await RevokeActiveFamilyAsync(stored.UserId, ip, ct).ConfigureAwait(false);
            await replayTransaction.CommitAsync(ct).ConfigureAwait(false);
            throw new UnauthorizedAccessException("Refresh-token reuse detected; active token family revoked.");
        }
        var now = clock.GetUtcNow();
        if (stored.ExpiresAt <= now || stored.User is not { IsActive: true, IsDeleted: false })
            throw new UnauthorizedAccessException("Expired or inactive refresh token.");
        var membership = ActiveMemberships(stored.User).SingleOrDefault(x => x.TenantId == stored.TenantId)
            ?? throw new UnauthorizedAccessException("Refresh token tenant membership is no longer active.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var pair = CreateTokens(stored.User, membership.TenantId, membership.Permissions, ip, userAgent);
        var updated = await db.RefreshTokens.IgnoreQueryFilters().Where(x => x.Id == stored.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevokedByIp, ip)
                .SetProperty(x => x.ReplacedByTokenHash, Hash(pair.RefreshToken)), ct).ConfigureAwait(false);
        if (updated != 1)
        {
            await RevokeActiveFamilyAsync(stored.UserId, ip, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            throw new UnauthorizedAccessException("Refresh-token reuse detected; active token family revoked.");
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return pair;
    }

    private async Task RevokeActiveFamilyAsync(int userId, string? ip, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedByIp, ip), ct)
            .ConfigureAwait(false);
    }

    private TokenResult CreateTokens(User user, int tenantId, IEnumerable<string> permissions, string? ip, string? userAgent)
    {
        var now = clock.GetUtcNow();
        var accessExpiry = now.AddMinutes(options.AccessTokenMinutes);
        var refreshExpiry = now.AddDays(options.RefreshTokenDays);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.UserName), new("tenant_id", tenantId.ToString()) };
        claims.AddRange(permissions.Distinct(StringComparer.Ordinal).Select(permission => new Claim("permission", permission)));
        if (user.IsSuperAdmin) claims.Add(new Claim(ClaimTypes.Role, "SuperAdmin"));
        var jwt = new JwtSecurityToken(options.Issuer, options.Audience, claims, now.UtcDateTime, accessExpiry.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(options.SigningKey), SecurityAlgorithms.HmacSha256));
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(RefreshBytes));
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TenantId = tenantId, TokenHash = Hash(raw), CreatedAt = now, ExpiresAt = refreshExpiry, CreatedByIp = ip, UserAgent = userAgent });
        return new TokenResult(new JwtSecurityTokenHandler().WriteToken(jwt), raw, accessExpiry, refreshExpiry);
    }

    private static IEnumerable<TenantMembership> ActiveMemberships(User user)
        => user.UserTenants.Where(membership => membership.IsActive && !membership.IsDeleted && membership.Tenant is { IsActive: true, IsDeleted: false })
            .GroupBy(membership => new { membership.TenantId, TenantName = membership.Tenant!.Name })
            .OrderBy(group => group.Key.TenantId)
            .Select(group => new TenantMembership(group.Key.TenantId, group.Key.TenantName,
                user.IsSuperAdmin ? Permissions.All : group.SelectMany(membership => membership.Role?.Permissions.Select(permission => permission.Permission) ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray()));

    private static string Hash(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}

public sealed class JwtOptions
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required byte[] SigningKey { get; init; }
    public int AccessTokenMinutes { get; init; } = 10;
    public int RefreshTokenDays { get; init; } = 14;
    public static JwtOptions FromEnvironment()
    {
        var encoded = Environment.GetEnvironmentVariable("MOUDIAN__JWT_SIGNING_KEY") ?? throw new InvalidOperationException("MOUDIAN__JWT_SIGNING_KEY is required.");
        var key = Convert.FromBase64String(encoded);
        if (key.Length < 32) throw new InvalidOperationException("JWT signing key must contain at least 32 bytes.");
        return new JwtOptions { Issuer = Environment.GetEnvironmentVariable("MOUDIAN__JWT_ISSUER") ?? "MoudianMiddleware", Audience = Environment.GetEnvironmentVariable("MOUDIAN__JWT_AUDIENCE") ?? "MoudianMiddleware.Client", SigningKey = key };
    }
}

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 310_000;
    public string Hash(string password) { var salt=RandomNumberGenerator.GetBytes(16);var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);return $"PBKDF2-SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}"; }
    public bool Verify(string password,string encoded){ArgumentNullException.ThrowIfNull(encoded);if (password is null) return false;var parts=encoded.Split('$');if(parts.Length!=4||parts[0]!="PBKDF2-SHA256"||!int.TryParse(parts[1],out var rounds)||rounds<100000||rounds>2000000)return false;try{var salt=Convert.FromBase64String(parts[2]);var expected=Convert.FromBase64String(parts[3]);var actual=Rfc2898DeriveBytes.Pbkdf2(password,salt,rounds,HashAlgorithmName.SHA256,expected.Length);var valid=CryptographicOperations.FixedTimeEquals(actual,expected);CryptographicOperations.ZeroMemory(actual);CryptographicOperations.ZeroMemory(expected);return valid;}catch(FormatException){return false;}}
}
