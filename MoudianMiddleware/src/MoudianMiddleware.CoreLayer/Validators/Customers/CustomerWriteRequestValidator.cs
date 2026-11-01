using FluentValidation;
using MoudianMiddleware.Contracts.ViewModels.Customers;

namespace MoudianMiddleware.CoreLayer.Validators.Customers;

/// <summary>اعتبارسنجی ورودی ایجاد/ویرایش مشتری.</summary>
public sealed class CustomerWriteRequestValidator : AbstractValidator<CustomerWriteRequest>
{
    /// <summary>تعریف قواعد اعتبارسنجی مشتری.</summary>
    public CustomerWriteRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).WithMessage("کد مشتری الزامی و حداکثر ۵۰ نویسه است.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300).WithMessage("نام مشتری الزامی و حداکثر ۳۰۰ نویسه است.");
        RuleFor(x => x.BuyerType).IsInEnum().WithMessage("نوع خریدار نامعتبر است.");
        RuleFor(x => x.NationalId).MaximumLength(20);
        RuleFor(x => x.EconomicCode).MaximumLength(20);
        RuleFor(x => x.PostalCode).MaximumLength(20);
        RuleFor(x => x.Address).MaximumLength(1000);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).MaximumLength(200);
        RuleFor(x => x.ExternalCode).MaximumLength(100);
    }
}
