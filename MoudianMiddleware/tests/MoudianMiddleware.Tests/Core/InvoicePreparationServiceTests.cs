using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.CoreLayer.Services.Invoices;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.Tests.TestHelpers;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class InvoicePreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_UsesSdkTaxIdProviderAndIsIdempotent()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantScope = new TenantScope();
        using var tenantLease = tenantScope.Enter(1);
        await using var context = database.CreateContext(tenantId: null, tenantScope);
        context.Tenants.Add(new Tenant { Name = "Test tenant" });
        await context.SaveChangesAsync();
        context.MoadianSettings.Add(new MoadianSetting
        {
            TenantId = 1,
            MemoryId = "A11216",
            EconomicCode = "12345678901"
        });
        var invoice = new Invoice
        {
            TenantId = 1,
            InvoiceNumber = "INV-PREP-1",
            BuyerName = "Buyer",
            IssueDate = new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero),
            PatternCode = 1,
            SubjectCode = 1,
            Type = InvoiceType.Original,
            Status = InvoiceStatus.Draft,
            Items = new List<InvoiceItem>
            {
                new()
                {
                    TenantId = 1,
                    RowNumber = 1,
                    Sstid = "item-1",
                    Description = "Item",
                    MeasuringUnitCode = "EA",
                    Quantity = 1m,
                    UnitPrice = 100m,
                    PriceBeforeDiscount = 100,
                    PriceAfterDiscount = 100,
                    TotalAmount = 100
                }
            }
        };
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
        var service = new InvoicePreparationService(context);

        var first = await service.PrepareAsync(invoice.Id);
        var second = await service.PrepareAsync(invoice.Id);

        first.InvoiceId.Should().Be(invoice.Id);
        first.Status.Should().Be(InvoiceStatus.ReadyToSend);
        first.TaxId.Should().HaveLength(22);
        first.Serial.Should().Be(invoice.Id.ToString("X10"));
        second.TaxId.Should().Be(first.TaxId);
        second.Serial.Should().Be(first.Serial);
        (await context.Invoices.SingleAsync(x => x.Id == invoice.Id)).Status.Should().Be(InvoiceStatus.ReadyToSend);
    }
}
