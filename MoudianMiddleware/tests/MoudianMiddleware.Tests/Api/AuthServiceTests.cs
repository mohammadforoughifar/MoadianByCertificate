using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Api.Services;
using MoudianMiddleware.Contracts.Constants;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.Tests.TestHelpers;
using Moq;
using Xunit;

namespace MoudianMiddleware.Tests.Api;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task RefreshReplay_RevokesEveryStillActiveTokenForTheUser()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        const string raw = "one-time-refresh-secret";
        var now = DateTimeOffset.UtcNow;
        await using (var seed = database.CreateContext(null))
        {
            var tenant = new Tenant { Name = "Auth test" };
            var role = new Role { Name = "Reader", NormalizedName = "READER" };
            role.Permissions.Add(new RolePermission { Permission = Permissions.InvoiceView });
            var user = new User { UserName = "auth-user", NormalizedUserName = "AUTH-USER", FullName = "Auth User", PasswordHash = "unused" };
            user.UserTenants.Add(new UserTenant { TenantId = 1, Tenant = tenant, Role = role, IsActive = true });
            user.RefreshTokens.Add(new RefreshToken { TenantId = 1, TokenHash = Hash(raw), CreatedAt = now, ExpiresAt = now.AddDays(7) });
            seed.AddRange(tenant, role, user);
            await seed.SaveChangesAsync();
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var options = new JwtOptions { Issuer = "test", Audience = "test", SigningKey = key };
        await using var context = database.CreateContext(null);
        var hasher = new Mock<MoudianMiddleware.DataLayer.Abstractions.IPasswordHasher>(MockBehavior.Strict);
        var auth = new AuthService(context, hasher.Object, options, TimeProvider.System);
        var first = await auth.RefreshAsync(new RefreshRequest(raw), null, null, CancellationToken.None);
        first.RefreshToken.Should().NotBe(raw);
        var replay = async () => await auth.RefreshAsync(new RefreshRequest(raw), null, null, CancellationToken.None);
        await replay.Should().ThrowAsync<UnauthorizedAccessException>();
        (await context.RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == 1 && x.RevokedAt == null).CountAsync()).Should().Be(0);
        CryptographicOperations.ZeroMemory(key);
    }

    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
