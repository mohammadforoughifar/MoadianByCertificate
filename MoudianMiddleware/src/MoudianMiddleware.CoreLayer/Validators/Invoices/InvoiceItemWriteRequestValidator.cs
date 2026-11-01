using FluentValidation;
using MoudianMiddleware.Contracts.ViewModels.Invoices;

namespace MoudianMiddleware.CoreLayer.Validators.Invoices;

/// <summary>اعتبارسنجی یک قلم صورتحساب.</summary>
public sealed class InvoiceItemWriteRequestValidator : AbstractValidator<InvoiceItemWriteRequest>
{
    private const decimal MaxDatabaseDecimal18Scale4 = 99_999_999_999_999.9999m;

    /// <summary>تعریف قواعد قلم و مقادیر پولی/نرخی آن.</summary>
    public InvoiceItemWriteRequestValidator()
    {
        RuleFor(x => x.Sstid).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.MeasuringUnitCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .LessThanOrEqualTo(MaxDatabaseDecimal18Scale4)
            .Must(value => HasAtMostScale(value, 4)).WithMessage("تعداد حداکثر چهار رقم اعشار دارد.");
        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(MaxDatabaseDecimal18Scale4)
            .Must(value => HasAtMostScale(value, 4)).WithMessage("مبلغ واحد حداکثر چهار رقم اعشار دارد.");
        RuleFor(x => x.DiscountAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.VatRate)
            .InclusiveBetween(0, 100)
            .Must(value => HasAtMostScale(value, 2)).WithMessage("نرخ VAT حداکثر دو رقم اعشار دارد.");
        RuleFor(x => x.OtherTaxRate)
            .InclusiveBetween(0, 100)
            .Must(value => !value.HasValue || HasAtMostScale(value.Value, 2))
            .When(x => x.OtherTaxRate.HasValue);
        RuleFor(x => x.OtherLegalFeeRate)
            .InclusiveBetween(0, 100)
            .Must(value => !value.HasValue || HasAtMostScale(value.Value, 2))
            .When(x => x.OtherLegalFeeRate.HasValue);
        RuleFor(x => x.OtherTaxSubject)
            .NotEmpty().When(x => x.OtherTaxRate.GetValueOrDefault() > 0)
            .WithMessage("برای نرخ سایر مالیات، عنوان مالیات لازم است.");
        RuleFor(x => x.OtherLegalFeeSubject)
            .NotEmpty().When(x => x.OtherLegalFeeRate.GetValueOrDefault() > 0)
            .WithMessage("برای نرخ سایر وجوه قانونی، عنوان لازم است.");
        RuleFor(x => x.ConstructionFee).GreaterThanOrEqualTo(0).When(x => x.ConstructionFee.HasValue);
        RuleFor(x => x.SellerProfit).GreaterThanOrEqualTo(0).When(x => x.SellerProfit.HasValue);
        RuleFor(x => x.BrokerFee).GreaterThanOrEqualTo(0).When(x => x.BrokerFee.HasValue);
        RuleFor(x => x.NetWeight).GreaterThanOrEqualTo(0).When(x => x.NetWeight.HasValue);
        RuleFor(x => x.HsCode).MaximumLength(30);
    }

    private static bool HasAtMostScale(decimal value, int scale) => decimal.Round(value, scale) == value;
}
