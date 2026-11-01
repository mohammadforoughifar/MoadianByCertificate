using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Api.Services;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.CoreLayer.Infrastructure.Moadian;
using MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;
using MoudianMiddleware.CoreLayer.Services.Moadian;
using MoudianMiddleware.CoreLayer.Services.Invoices;
using MoudianMiddleware.CoreLayer.Validators.Invoices;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.DataLayer.Repositories;
using MoudianMiddleware.DataLayer.UnitOfWork;
using MoudianMiddleware.Tests.TestHelpers;
using Moq;
using Xunit;

namespace MoudianMiddleware.Tests.Data;

public sealed class SqlServerConcurrencyTests
{
    [SqlServerFact]
    public async Task TwoConcurrentRefreshes_RotateOnceAndRevokeTheFamilyOnReplay()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedAuthAsync(database);
        var rawToken = WebToken();
        var now = DateTimeOffset.UtcNow;
        await using (var context = database.CreateContext(tenantId, new TenantScope()))
        {
            context.RefreshTokens.Add(new RefreshToken { UserId = 1, TenantId = tenantId, TokenHash = Hash(rawToken), CreatedAt = now, ExpiresAt = now.AddDays(10) });
            await context.SaveChangesAsync();
        }
        var key = RandomNumberGenerator.GetBytes(32);
        var options = new JwtOptions { Issuer = "integration", Audience = "integration-client", SigningKey = key };
        var results = await Task.WhenAll(
            CaptureRefreshAsync(database, options, rawToken),
            CaptureRefreshAsync(database, options, rawToken)).ConfigureAwait(false);
        results.Count(result => result is not null).Should().Be(1);
        await using var verify = database.CreateContext(tenantId, new TenantScope());
        (await verify.RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == 1 && x.RevokedAt == null).CountAsync()).Should().Be(0);
        CryptographicOperations.ZeroMemory(key);
    }

    [SqlServerFact]
    public async Task TwoConcurrentInvoiceSends_OnlyOneNetworkCallIsClaimed()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedSendableInvoiceAsync(database);
        var scope1 = new TenantScope(); var scope2 = new TenantScope();
        await using var context1 = database.CreateContext(tenantId, scope1);
        await using var context2 = database.CreateContext(tenantId, scope2);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<MoadianSendReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new Mock<IMoadianGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.SendInvoiceAsync(It.IsAny<MoadianSetting>(), It.IsAny<MoadianCredential>(), It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(() => { started.TrySetResult(true); return release.Task; });
        using var lease1 = scope1.Enter(tenantId); var service1 = new MoadianSubmissionService(context1, gateway.Object);
        using var lease2 = scope2.Enter(tenantId); var service2 = new MoadianSubmissionService(context2, gateway.Object);
        var first = service1.SubmitAsync(1);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = async () => await service2.SubmitAsync(1);
        await second.Should().ThrowAsync<InvalidOperationException>();
        release.TrySetResult(new MoadianSendReceipt("uid-concurrent", "ref-concurrent", "tax-concurrent"));
        (await first).Uid.Should().Be("uid-concurrent");
        gateway.Verify(x => x.SendInvoiceAsync(It.IsAny<MoadianSetting>(), It.IsAny<MoadianCredential>(), It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<TokenResult?> CaptureRefreshAsync(SqlServerTestDatabase database, JwtOptions options, string token)
    {
        try { return await RefreshAsync(database, options, token).ConfigureAwait(false); }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static async Task<TokenResult> RefreshAsync(SqlServerTestDatabase database, JwtOptions options, string token)
    {
        var scope = new TenantScope();
        await using var context = database.CreateContext(null, scope);
        return await new AuthService(context, new Pbkdf2PasswordHasher(), options, TimeProvider.System)
            .RefreshAsync(new RefreshRequest(token), "127.0.0.1", "integration-test", CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<int> SeedAuthAsync(SqlServerTestDatabase database)
    {
        var scope = new TenantScope(); await using var context = database.CreateContext(null, scope);
        var role = new Role { Name = "Reader", NormalizedName = "READER" };
        role.Permissions.Add(new RolePermission { Permission = MoudianMiddleware.Contracts.Constants.Permissions.InvoiceView });
        var tenant = new Tenant { Name = "Concurrency tenant" };
        var user = new User { UserName = "parallel-user", NormalizedUserName = "PARALLEL-USER", FullName = "Parallel", PasswordHash = "not-used" };
        user.UserTenants.Add(new UserTenant { Tenant = tenant, Role = role, IsActive = true });
        await context.AddRangeAsync(tenant, role, user); await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task<int> SeedSendableInvoiceAsync(SqlServerTestDatabase database)
    {
        var scope = new TenantScope(); await using var context = database.CreateContext(null, scope);
        var tenant = new Tenant { Name = "Send tenant" }; context.Tenants.Add(tenant); await context.SaveChangesAsync();
        using var lease = scope.Enter(tenant.Id);
        context.MoadianSettings.Add(new MoadianSetting { TenantId = tenant.Id, MemoryId = "MEM-1", EconomicCode = "12345678901" });
        context.MoadianCredentials.Add(new MoadianCredential { TenantId = tenant.Id });
        context.Invoices.Add(new Invoice { TenantId = tenant.Id, InvoiceNumber = "INV-CONCURRENT", BuyerName = "Buyer", IssueDate = DateTimeOffset.UtcNow, PatternCode = 1, SubjectCode = 1, TaxId = "tax-concurrent", Status = InvoiceStatus.ReadyToSend,
            Items = new List<InvoiceItem> { new() { TenantId = tenant.Id, RowNumber = 1, Sstid = "S1", Description = "Test", MeasuringUnitCode = "EA", Quantity = 1m, UnitPrice = 100m, PriceBeforeDiscount = 100, PriceAfterDiscount = 100, TotalAmount = 100 } } });
        await context.SaveChangesAsync(); return tenant.Id;
    }

    private static string WebToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
    private static string Hash(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
