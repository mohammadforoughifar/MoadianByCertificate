using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// یک قلم صورتحساب (Body) با مبالغ و نرخ‌های محاسبه‌شده.
/// </summary>
/// <remarks>
/// محاسبات با <c>InvoiceCalculationService</c> در CoreLayer انجام می‌شود؛ این کلاس فقط داده است.
/// </remarks>
public class InvoiceItem : BaseEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>شناسه‌ی صورتحساب.</summary>
    public int InvoiceId { get; set; }

    /// <summary>شماره‌ی ردیف (از ۱).</summary>
    public int RowNumber { get; set; }

    /// <summary>شناسه‌ی کالا/خدمت (فیلد <c>sstid</c>).</summary>
    public string Sstid { get; set; } = string.Empty;

    /// <summary>شرح کالا/خدمت (فیلد <c>sstt</c>).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>کد واحد اندازه‌گیری (فیلد <c>mu</c>).</summary>
    public string MeasuringUnitCode { get; set; } = string.Empty;

    /// <summary>تعداد/مقدار (فیلد <c>am</c>).</summary>
    public decimal Quantity { get; set; }

    /// <summary>مبلغ واحد (فیلد <c>fee</c>).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>مبلغ قبل از تخفیف (فیلد <c>prdis</c>).</summary>
    public long PriceBeforeDiscount { get; set; }

    /// <summary>مبلغ تخفیف (فیلد <c>dis</c>).</summary>
    public long DiscountAmount { get; set; }

    /// <summary>مبلغ بعد از تخفیف (فیلد <c>adis</c>).</summary>
    public long PriceAfterDiscount { get; set; }

    /// <summary>نرخ مالیات بر ارزش افزوده به درصد (فیلد <c>vra</c>).</summary>
    public decimal VatRate { get; set; }

    /// <summary>مبلغ مالیات بر ارزش افزوده (فیلد <c>vam</c>).</summary>
    public long VatAmount { get; set; }

    /// <summary>موضوع سایر مالیات و عوارض (فیلد <c>odt</c>).</summary>
    public string? OtherTaxSubject { get; set; }

    /// <summary>نرخ سایر مالیات و عوارض (فیلد <c>odr</c>).</summary>
    public decimal? OtherTaxRate { get; set; }

    /// <summary>مبلغ سایر مالیات و عوارض (فیلد <c>odam</c>).</summary>
    public long OtherTaxAmount { get; set; }

    /// <summary>موضوع سایر وجوه قانونی (فیلد <c>olt</c>).</summary>
    public string? OtherLegalFeeSubject { get; set; }

    /// <summary>نرخ سایر وجوه قانونی (فیلد <c>olr</c>).</summary>
    public decimal? OtherLegalFeeRate { get; set; }

    /// <summary>مبلغ سایر وجوه قانونی (فیلد <c>olam</c>).</summary>
    public long OtherLegalFeeAmount { get; set; }

    /// <summary>مبلغ کل این قلم (فیلد <c>tsstam</c>).</summary>
    public long TotalAmount { get; set; }

    /// <summary>اجرت ساخت (فیلد <c>consfee</c>، اختیاری).</summary>
    public long? ConstructionFee { get; set; }

    /// <summary>سود فروشنده (فیلد <c>spro</c>، اختیاری).</summary>
    public long? SellerProfit { get; set; }

    /// <summary>حق‌العمل (فیلد <c>bros</c>، اختیاری).</summary>
    public long? BrokerFee { get; set; }

    /// <summary>وزن خالص (فیلد <c>nw</c>، اختیاری).</summary>
    public decimal? NetWeight { get; set; }

    /// <summary>کد HS گمرکی (فیلد <c>hs</c>، اختیاری).</summary>
    public string? HsCode { get; set; }

    /// <summary>صورتحساب مربوطه.</summary>
    public Invoice? Invoice { get; set; }
}
