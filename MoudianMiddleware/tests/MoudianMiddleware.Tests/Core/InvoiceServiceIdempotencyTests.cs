using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.CoreLayer.Services.Invoices;
using MoudianMiddleware.CoreLayer.Validators.Common;
using MoudianMiddleware.CoreLayer.Validators.Invoices;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Repositories;
using MoudianMiddleware.DataLayer.UnitOfWork;
using MoudianMiddleware.Tests.TestHelpers;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class InvoiceServiceIdempotencyTests
{
    [Fact]
    public async Task CreateAsync_ReplaysTenantScopedExternalIdFromRelationalSqliteDatabase()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantScope = new TenantScope();
        using var tenantLease = tenantScope.Enter(41);
        await using var context = database.CreateContext(tenantId: null, tenantScope);

        var tenantProvider = new FakeTenantProvider(null);
        var service = new InvoiceService(
            new InvoiceRepository(context),
            new CustomerRepository(context),
            new UnitOfWork(context),
            new InvoiceCalculationService(),
            new InvoiceWriteRequestValidator(),
            new InvoiceListQueryValidator(),
            tenantProvider,
            tenantScope);

        var request = new InvoiceWriteRequest
        {
            InvoiceNumber = "INV-ERP-41",
            ExternalId = "ERP-INV-41",
            Type = InvoiceType.Original,
            PatternCode = 1,
            SubjectCode = 1,
            SettlementMethod = SettlementMethod.Cash,
            IssueDate = new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero),
            BuyerName = "Buyer",
            BuyerType = BuyerType.NaturalPerson,
            Items = new[]
            {
                new InvoiceItemWriteRequest
                {
                    Sstid = "item-1",
                    Description = "Item",
                    MeasuringUnitCode = "EA",
                    Quantity = 1m,
                    UnitPrice = 100m,
                    VatRate = 0m
                }
            }
        };

        var first = await service.CreateAsync(request);
        var replay = await service.CreateAsync(request);

        first.Id.Should().Be(replay.Id);
        replay.IsIdempotentReplay.Should().BeTrue();
        var persisted = await context.Invoices.ToListAsync();
        persisted.Should().ContainSingle();
        persisted[0].TenantId.Should().Be(41);
    }
}
