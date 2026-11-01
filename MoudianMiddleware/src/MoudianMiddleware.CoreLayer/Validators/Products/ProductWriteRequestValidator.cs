using FluentValidation;
using MoudianMiddleware.Contracts.ViewModels.Products;

namespace MoudianMiddleware.CoreLayer.Validators.Products;

/// <summary>اعتبارسنجی ورودی ایجاد/ویرایش کالا.</summary>
public sealed class ProductWriteRequestValidator : AbstractValidator<ProductWriteRequest>
{
    private const decimal MaxDatabaseDecimal18Scale4 = 99_999_999_999_999.9999m;

    /// <summary>تعریف قواعد اعتبارسنجی کالا.</summary>
    public ProductWriteRequestValidator()
    {
        RuleFor(x => x.Sstid).NotEmpty().MaximumLength(20).WithMessage("شناسه‌ی کالا/خدمت الزامی و حداکثر ۲۰ نویسه است.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300).WithMessage("عنوان کالا/خدمت الزامی و حداکثر ۳۰۰ نویسه است.");
        RuleFor(x => x.MeasuringUnitId).GreaterThan(0).WithMessage("واحد اندازه‌گیری معتبر انتخاب کنید.");
        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0).WithMessage("مبلغ واحد نمی‌تواند منفی باشد.")
            .LessThanOrEqualTo(MaxDatabaseDecimal18Scale4)
            .Must(value => decimal.Round(value, 4) == value).WithMessage("مبلغ واحد حداکثر چهار رقم اعشار دارد.");
        RuleFor(x => x.VatRate)
            .InclusiveBetween(0, 100).WithMessage("نرخ مالیات باید بین صفر و صد باشد.")
            .Must(value => decimal.Round(value, 2) == value).WithMessage("نرخ مالیات حداکثر دو رقم اعشار دارد.");
        RuleFor(x => x.HsCode).MaximumLength(30);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
