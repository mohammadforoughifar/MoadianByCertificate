using FluentAssertions;
using FluentValidation.TestHelper;
using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.CoreLayer.Services.Invoices;
using MoudianMiddleware.CoreLayer.Validators.Invoices;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class InvoiceCalculationAndValidatorTests
{
    [Fact]
    public void Apply_RoundsMoneyToWholeRialAwayFromZeroAndRecalculatesTotals()
    {
        var invoice = new MoudianMiddleware.DataLayer.Entities.Invoice();
        var request = new InvoiceItemWriteRequest
        {
            Sstid = "item-1",
            Description = "نمونه",
            MeasuringUnitCode = "EA",
            Quantity = 1m,
            UnitPrice = 100.5m,
            VatRate = 10m
        };

        new InvoiceCalculationService().Apply(invoice, new[] { request });

        invoice.Items.Should().ContainSingle();
        invoice.Items.Single().PriceBeforeDiscount.Should().Be(101L);
        invoice.Items.Single().VatAmount.Should().Be(10L);
        invoice.TotalAmount.Should().Be(111L);
        invoice.CashPayment.Should().Be(111L);
    }

    [Fact]
    public void ItemValidator_RejectsPrecisionThatWouldBeRoundedByTheDatabase()
    {
        var request = new InvoiceItemWriteRequest
        {
            Sstid = "item-1",
            Description = "نمونه",
            MeasuringUnitCode = "EA",
            Quantity = 1.00001m,
            UnitPrice = 2.12345m,
            VatRate = 9.125m
        };

        var result = new InvoiceItemWriteRequestValidator().TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Quantity);
        result.ShouldHaveValidationErrorFor(x => x.UnitPrice);
        result.ShouldHaveValidationErrorFor(x => x.VatRate);
    }

    [Fact]
    public void ItemValidator_RequiresTaxLabelsWhenAdditionalRatesAreUsed()
    {
        var request = new InvoiceItemWriteRequest
        {
            Sstid = "item-1",
            Description = "نمونه",
            MeasuringUnitCode = "EA",
            Quantity = 1,
            UnitPrice = 10,
            VatRate = 0,
            OtherTaxRate = 3,
            OtherLegalFeeRate = 2
        };

        var result = new InvoiceItemWriteRequestValidator().TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.OtherTaxSubject);
        result.ShouldHaveValidationErrorFor(x => x.OtherLegalFeeSubject);
    }
}
