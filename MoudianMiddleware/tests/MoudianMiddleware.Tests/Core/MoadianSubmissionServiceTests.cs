using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.CoreLayer.Infrastructure.Moadian;
using MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;
using MoudianMiddleware.CoreLayer.Services.Moadian;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.Tests.TestHelpers;
using Moq;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class MoadianSubmissionServiceTests
{
    [Fact]
    public async Task SubmitAsync_ClaimsAndPersistsReceiptAndMetadataOnlyHistoryAsync()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantScope = new TenantScope();
        using var tenantLease = tenantScope.Enter(1);
        await using var context = database.CreateContext(tenantId: null, tenantScope);
        await SeedSendableInvoiceAsync(context);
        var gateway = new Mock<IMoadianGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.SendInvoiceAsync(
                It.IsAny<MoadianSetting>(),
                It.IsAny<MoadianCredential>(),
                It.IsAny<Invoice>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MoadianSendReceipt("uid-1", "ref-1", "tax-1"));
        var service = new MoadianSubmissionService(context, gateway.Object);

        var receipt = await service.SubmitAsync(1);

        receipt.Uid.Should().Be("uid-1");
        var invoice = await context.Invoices.SingleAsync(x => x.Id == 1);
        invoice.Status.Should().Be(InvoiceStatus.Sent);
        invoice.ReferenceNumber.Should().Be("ref-1");
        var submission = await context.InvoiceSubmissions.SingleAsync(x => x.InvoiceId == 1);
        submission.Status.Should().Be(SubmissionStatus.Succeeded);
        submission.ErrorMessage.Should().BeNull();
        gateway.VerifyAll();
    }

    [Fact]
    public async Task InquiryByTaxIdAsync_ReconcilesAnInvoiceWithoutReferenceNumberAsync()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantScope = new TenantScope();
        using var tenantLease = tenantScope.Enter(1);
        await using var context = database.CreateContext(tenantId: null, tenantScope);
        await SeedSendableInvoiceAsync(context);
        var invoice = await context.Invoices.SingleAsync();
        invoice.Status = InvoiceStatus.Sending;
        await context.SaveChangesAsync();

        var gateway = new Mock<IMoadianGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.InquiryByTaxIdsAsync(
                It.IsAny<MoadianSetting>(),
                It.IsAny<MoadianCredential>(),
                It.Is<IReadOnlyCollection<string>>(ids => ids.Single() == "tax-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new MoadianInvoiceStatusResult("tax-1", "SUCCESS", "SUCCESS") });
        var service = new MoadianSubmissionService(context, gateway.Object);

        var results = await service.InquiryByTaxIdAsync(1);

        results.Should().ContainSingle();
        (await context.Invoices.SingleAsync(x => x.Id == 1)).Status.Should().Be(InvoiceStatus.Verified);
        var submission = await context.InvoiceSubmissions.SingleAsync(x => x.InvoiceId == 1);
        submission.MoadianStatus.Should().Be("SUCCESS");
        submission.Status.Should().Be(SubmissionStatus.Succeeded);
        submission.ErrorMessage.Should().BeNull();
        gateway.VerifyAll();
    }

    private static async Task SeedSendableInvoiceAsync(MoudianDbContext context)
    {
        context.Tenants.Add(new Tenant { Name = "Test tenant" });
        await context.SaveChangesAsync();
        context.MoadianSettings.Add(new MoadianSetting
        {
            TenantId = 1,
            MemoryId = "A11216",
            EconomicCode = "12345678901"
        });
        context.MoadianCredentials.Add(new MoadianCredential { TenantId = 1 });
        context.Invoices.Add(new Invoice
        {
            TenantId = 1,
            InvoiceNumber = "INV-SEND-1",
            BuyerName = "Buyer",
            IssueDate = DateTimeOffset.UtcNow,
            PatternCode = 1,
            SubjectCode = 1,
            TaxId = "tax-1",
            Status = InvoiceStatus.ReadyToSend,
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
        });
        await context.SaveChangesAsync();
    }
}
