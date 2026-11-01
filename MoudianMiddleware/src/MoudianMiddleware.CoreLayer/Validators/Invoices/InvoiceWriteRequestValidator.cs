using FluentValidation;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Invoices;

namespace MoudianMiddleware.CoreLayer.Validators.Invoices;

/// <summary>اعتبارسنجی پیش‌نویس صورتحساب و همه‌ی فرزندان آن.</summary>
public sealed class InvoiceWriteRequestValidator : AbstractValidator<InvoiceWriteRequest>
{
    /// <summary>تعریف قواعد هدر، نوع سند، اقلام و پرداخت‌ها.</summary>
    public InvoiceWriteRequestValidator()
    {
        RuleFor(x => x.InvoiceNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ExternalId).MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.PatternCode).GreaterThan(0);
        RuleFor(x => x.SubjectCode).GreaterThan(0);
        RuleFor(x => x.SettlementMethod).IsInEnum();
        RuleFor(x => x.IssueDate).Must(x => x != default).WithMessage("زمان صدور صورتحساب الزامی است.");
        RuleFor(x => x.CustomerId).GreaterThan(0).When(x => x.CustomerId.HasValue);
        RuleFor(x => x.BuyerName).NotEmpty().MaximumLength(300);
        RuleFor(x => x.BuyerType).IsInEnum();
        RuleFor(x => x.BuyerNationalId).MaximumLength(20);
        RuleFor(x => x.BuyerEconomicCode).MaximumLength(20);
        RuleFor(x => x.BuyerPostalCode).MaximumLength(20);
        RuleFor(x => x.BuyerAddress).MaximumLength(1000);
        RuleFor(x => x.BuyerBranchCode).MaximumLength(50);
        RuleFor(x => x.ReferenceTaxId)
            .NotEmpty()
            .WithMessage("صورتحساب اصلاحی، ابطالی یا برگشت از فروش باید شناسه‌ی مالیاتی مرجع داشته باشد.")
            .When(x => x.Type != InvoiceType.Original);
        RuleFor(x => x.Items).NotNull().NotEmpty().WithMessage("صورتحساب باید دست‌کم یک قلم داشته باشد.");
        RuleFor(x => x.Payments).NotNull().WithMessage("فهرست پرداخت‌ها نباید null باشد.");
        RuleForEach(x => x.Items).SetValidator(new InvoiceItemWriteRequestValidator()).When(x => x.Items is not null);
        RuleForEach(x => x.Payments).SetValidator(new InvoicePaymentWriteRequestValidator()).When(x => x.Payments is not null);
    }
}
