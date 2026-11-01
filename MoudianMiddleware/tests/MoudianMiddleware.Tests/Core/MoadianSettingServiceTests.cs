using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Moadian;
using MoudianMiddleware.CoreLayer.Services.Moadian;
using MoudianMiddleware.CoreLayer.Validators.Moadian;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.Tests.TestHelpers;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class MoadianSettingServiceTests
{
    [Fact]
    public async Task SaveAsync_UpsertsOnlyTheCurrentTenantSetting()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantScope = new TenantScope();
        using var tenantLease = tenantScope.Enter(1);
        await using var context = database.CreateContext(tenantId: null, tenantScope);
        context.Tenants.Add(new Tenant { Name = "Test tenant" });
        await context.SaveChangesAsync();
        var service = new MoadianSettingService(
            context,
            new MoadianSettingWriteRequestValidator(),
            new FakeTenantProvider(null),
            tenantScope);

        var request = new MoadianSettingWriteRequest
        {
            MemoryId = "a11216",
            EconomicCode = "12345678901",
            ActiveEnvironment = MoadianEnvironment.Production,
            ProductionBaseUrl = "https://tax.example.test/api",
            SignatureMode = MoadianSignatureMode.Pkcs8Certificate
        };

        var first = await service.SaveAsync(request);
        var updated = await service.SaveAsync(new MoadianSettingWriteRequest
        {
            MemoryId = "A11216",
            EconomicCode = "12345678901",
            BranchCode = "001",
            ActiveEnvironment = MoadianEnvironment.Production,
            ProductionBaseUrl = "https://tax.example.test/api",
            SignatureMode = MoadianSignatureMode.Pkcs8Certificate
        });

        first.TenantId.Should().Be(1);
        updated.BranchCode.Should().Be("001");
        updated.MemoryId.Should().Be("A11216");
        (await context.MoadianSettings.CountAsync()).Should().Be(1);
    }
}
