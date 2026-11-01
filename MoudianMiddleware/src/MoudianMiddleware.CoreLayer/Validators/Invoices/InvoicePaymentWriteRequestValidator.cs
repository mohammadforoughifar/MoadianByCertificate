using FluentValidation;
using MoudianMiddleware.Contracts.ViewModels.Invoices;

namespace MoudianMiddleware.CoreLayer.Validators.Invoices;

/// <summary>اعتبارسنجی ردیف پرداخت صورتحساب.</summary>
public sealed class InvoicePaymentWriteRequestValidator : AbstractValidator<InvoicePaymentWriteRequest>
{
    /// <summary>تعریف قواعد اطلاعات پرداخت.</summary>
    public InvoicePaymentWriteRequestValidator()
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("مبلغ پرداخت باید بزرگ‌تر از صفر باشد.");
        RuleFor(x => x.ReferenceNumber).MaximumLength(100);
        RuleFor(x => x.TerminalNumber).MaximumLength(50);
        RuleFor(x => x.AcceptorNumber).MaximumLength(50);
        RuleFor(x => x.SwitchNumber).MaximumLength(50);
        RuleFor(x => x.PayerNationalId).MaximumLength(20);
        RuleFor(x => x.MaskedCardNumber).MaximumLength(30);
        RuleFor(x => x.VatPaymentId).MaximumLength(100);
        RuleFor(x => x.VatBillId).MaximumLength(100);
    }
}
