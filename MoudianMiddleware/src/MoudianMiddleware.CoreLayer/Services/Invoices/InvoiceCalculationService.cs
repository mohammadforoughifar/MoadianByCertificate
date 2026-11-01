using FluentValidation;
using FluentValidation.Results;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Services.Invoices;

/// <summary>
/// محاسبه‌ی مرکزی صورتحساب. مبلغ واحد/تعداد decimal می‌مانند؛ فیلدهای مبلغ نهایی SDK/Entity long ریال‌اند.
/// </summary>
public sealed class InvoiceCalculationService : IInvoiceCalculationService
{
    /// <summary>ساخت اقلام صورتحساب و محاسبه‌ی جمع‌ها.</summary>
    /// <remarks>
    /// فرض اجرایی فعلی: مالیات‌ها پس از تخفیف و بر مبنای مبلغ ردیف محاسبه می‌شوند؛ این قاعده باید با دستورالعمل رسمی/سناریوهای مودی تأیید شود.
    /// برای تسویه‌ی ترکیبی، ردیف‌های پرداخت به‌عنوان مبلغ وصول‌شده تلقی می‌شوند و باقیمانده نسیه است.
    /// </remarks>
    public void Apply(Invoice invoice, IReadOnlyList<InvoiceItemWriteRequest> items)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(items);

        invoice.Items.Clear();
        long beforeDiscount = 0;
        long discountTotal = 0;
        long afterDiscountTotal = 0;
        long vatTotal = 0;
        long otherTotal = 0;
        var rowNumber = 0;

        foreach (var input in items)
        {
            rowNumber++;
            var gross = ToWholeRial(checked(input.Quantity * input.UnitPrice));
            if (input.DiscountAmount > gross)
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure($"Items[{rowNumber - 1}].DiscountAmount", "تخفیف هر قلم نمی‌تواند از مبلغ ناخالص همان قلم بیشتر باشد.")
                });
            }

            var afterDiscount = checked(gross - input.DiscountAmount);
            var vat = ToWholeRial(afterDiscount * input.VatRate / 100m);
            var otherTax = ToWholeRial(afterDiscount * (input.OtherTaxRate ?? 0m) / 100m);
            var otherLegal = ToWholeRial(afterDiscount * (input.OtherLegalFeeRate ?? 0m) / 100m);
            var rowTotal = checked(checked(afterDiscount + vat) + checked(otherTax + otherLegal));

            invoice.Items.Add(new InvoiceItem
            {
                RowNumber = rowNumber,
                Sstid = input.Sstid.Trim(),
                Description = input.Description.Trim(),
                MeasuringUnitCode = input.MeasuringUnitCode.Trim(),
                Quantity = input.Quantity,
                UnitPrice = input.UnitPrice,
                PriceBeforeDiscount = gross,
                DiscountAmount = input.DiscountAmount,
                PriceAfterDiscount = afterDiscount,
                VatRate = input.VatRate,
                VatAmount = vat,
                OtherTaxSubject = Normalize(input.OtherTaxSubject),
                OtherTaxRate = input.OtherTaxRate,
                OtherTaxAmount = otherTax,
                OtherLegalFeeSubject = Normalize(input.OtherLegalFeeSubject),
                OtherLegalFeeRate = input.OtherLegalFeeRate,
                OtherLegalFeeAmount = otherLegal,
                TotalAmount = rowTotal,
                ConstructionFee = input.ConstructionFee,
                SellerProfit = input.SellerProfit,
                BrokerFee = input.BrokerFee,
                NetWeight = input.NetWeight,
                HsCode = Normalize(input.HsCode)
            });

            beforeDiscount = checked(beforeDiscount + gross);
            discountTotal = checked(discountTotal + input.DiscountAmount);
            afterDiscountTotal = checked(afterDiscountTotal + afterDiscount);
            vatTotal = checked(vatTotal + vat);
            otherTotal = checked(otherTotal + otherTax + otherLegal);
        }

        invoice.TotalBeforeDiscount = beforeDiscount;
        invoice.TotalDiscount = discountTotal;
        invoice.TotalAfterDiscount = afterDiscountTotal;
        invoice.TotalVat = vatTotal;
        invoice.TotalOtherTaxes = otherTotal;
        invoice.TotalAmount = checked(checked(afterDiscountTotal + vatTotal) + otherTotal);

        ApplySettlementAmounts(invoice);
    }

    private static long ToWholeRial(decimal value) => checked((long)decimal.Round(value, 0, MidpointRounding.AwayFromZero));

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ApplySettlementAmounts(Invoice invoice)
    {
        switch (invoice.SettlementMethod)
        {
            case SettlementMethod.Cash:
                invoice.CashPayment = invoice.TotalAmount;
                invoice.CreditPayment = 0;
                break;
            case SettlementMethod.Credit:
                invoice.CashPayment = 0;
                invoice.CreditPayment = invoice.TotalAmount;
                break;
            case SettlementMethod.CashAndCredit:
                var cash = invoice.Payments.Aggregate(0L, (sum, payment) => checked(sum + payment.Amount));
                if (cash > invoice.TotalAmount)
                {
                    throw new ValidationException(new[]
                    {
                        new ValidationFailure(nameof(invoice.Payments), "جمع پرداخت‌های وصول‌شده از مبلغ صورتحساب بیشتر است.")
                    });
                }

                invoice.CashPayment = cash;
                invoice.CreditPayment = invoice.TotalAmount - cash;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invoice.SettlementMethod));
        }
    }
}
