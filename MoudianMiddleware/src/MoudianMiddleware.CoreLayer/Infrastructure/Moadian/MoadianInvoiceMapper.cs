using TaxCollectData.Library.Dto;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian;

/// <summary>
/// تنها نقطه‌ی تبدیل Entity به DTO عددی SDK 2.0.29؛ longهای ریالی بدون cast و decimalهای تعداد/نرخ مستقیم منتقل می‌شوند.
/// </summary>
internal static class MoadianInvoiceMapper
{
    public static InvoiceDto Map(MoadianSetting setting, Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(invoice);
        if (string.IsNullOrWhiteSpace(invoice.TaxId))
        {
            throw new InvalidOperationException("شماره‌ی مالیاتی صورتحساب پیش از ارسال باید تولید شود.");
        }

        var header = new HeaderDto
        {
            taxid = invoice.TaxId,
            indatim = invoice.IssueDate.ToUnixTimeMilliseconds(),
            ins = invoice.SubjectCode,
            inty = (int)invoice.Type,
            inno = invoice.Serial,
            irtaxid = invoice.ReferenceTaxId,
            setm = (int)invoice.SettlementMethod,
            tins = setting.EconomicCode,
            cap = invoice.CashPayment,
            bid = invoice.BuyerNationalId,
            insp = invoice.CreditPayment,
            tvop = invoice.VatShareFromPayment == 0 ? null : invoice.VatShareFromPayment,
            bpc = invoice.BuyerPostalCode ?? string.Empty,
            tax17 = invoice.Tax17 == 0 ? null : invoice.Tax17,
            inp = invoice.PatternCode,
            tprdis = invoice.TotalBeforeDiscount,
            tdis = invoice.TotalDiscount,
            tadis = invoice.TotalAfterDiscount,
            tvam = invoice.TotalVat,
            todam = invoice.TotalOtherTaxes,
            tbill = invoice.TotalAmount,
            tob = (int)invoice.BuyerType,
            tinb = invoice.BuyerEconomicCode,
            bbc = invoice.BuyerBranchCode,
            sbc = setting.BranchCode,
            sg = new List<ShippingGoodDto>()
        };

        var body = invoice.Items.OrderBy(x => x.RowNumber).Select(item => new BodyItemDto
        {
            sstid = item.Sstid,
            sstt = item.Description,
            mu = item.MeasuringUnitCode,
            am = item.Quantity,
            fee = item.UnitPrice,
            // Entity prices are Rial-denominated. `cut` is documented by the SDK repository as conditional
            // on foreign-currency patterns; null is used here. SDK JSON null emission is a known uncertainty.
            cut = null!,
            prdis = item.PriceBeforeDiscount,
            dis = item.DiscountAmount,
            adis = item.PriceAfterDiscount,
            vra = item.VatRate,
            vam = item.VatAmount,
            odt = item.OtherTaxSubject ?? null!,
            odr = item.OtherTaxRate,
            odam = item.OtherTaxAmount == 0 ? null : item.OtherTaxAmount,
            olt = item.OtherLegalFeeSubject ?? null!,
            olr = item.OtherLegalFeeRate,
            olam = item.OtherLegalFeeAmount == 0 ? null : item.OtherLegalFeeAmount,
            consfee = item.ConstructionFee,
            spro = item.SellerProfit,
            bros = item.BrokerFee,
            vop = null,
            tsstam = item.TotalAmount,
            nw = item.NetWeight,
            hs = item.HsCode ?? null!
        }).ToList();

        var payments = invoice.Payments.Select(payment => new PaymentItemDto
        {
            iinn = payment.SwitchNumber ?? null!,
            acn = payment.AcceptorNumber ?? null!,
            trmn = payment.TerminalNumber ?? null!,
            trn = payment.ReferenceNumber ?? null!,
            pcn = payment.MaskedCardNumber ?? null!,
            pid = payment.PayerNationalId ?? null!,
            vatpid = payment.VatPaymentId ?? null!,
            vatbid = payment.VatBillId ?? null!,
            pdt = payment.PaymentDate?.ToUnixTimeMilliseconds(),
            pmt = (int)payment.Method,
            pv = payment.Amount
        }).ToList();

        return new InvoiceDto
        {
            Header = header,
            Body = body,
            Payments = payments,
            Extension = new List<ExtensionItemDto>()
        };
    }
}
