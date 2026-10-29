using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// یک ردیف پرداخت صورتحساب (Payments) با اطلاعات پایانه/پیگیری در صورت پرداخت الکترونیکی.
/// </summary>
public class InvoicePayment : BaseEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>شناسه‌ی صورتحساب.</summary>
    public int InvoiceId { get; set; }

    /// <summary>روش پرداخت (فیلد <c>pmt</c>).</summary>
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

    /// <summary>مبلغ پرداختی (فیلد <c>pv</c>).</summary>
    public long Amount { get; set; }

    /// <summary>تاریخ و زمان پرداخت (فیلد <c>pdt</c>).</summary>
    public DateTimeOffset? PaymentDate { get; set; }

    /// <summary>شماره‌ی پیگیری پرداخت (فیلد <c>trn</c>).</summary>
    public string? ReferenceNumber { get; set; }

    /// <summary>شماره‌ی پایانه (فیلد <c>trmn</c>).</summary>
    public string? TerminalNumber { get; set; }

    /// <summary>شماره‌ی پذیرنده‌ی فروشگاهی (فیلد <c>acn</c>).</summary>
    public string? AcceptorNumber { get; set; }

    /// <summary>شماره‌ی سوییچ پرداخت (فیلد <c>iinn</c>).</summary>
    public string? SwitchNumber { get; set; }

    /// <summary>کد ملی/شناسه‌ی فراگیر پرداخت‌کننده (فیلد <c>pid</c>).</summary>
    public string? PayerNationalId { get; set; }

    /// <summary>شماره‌ی کارت ماسک‌شده (فیلد <c>pcn</c>؛ فقط چهار رقم آخر نگه‌داری می‌شود).</summary>
    public string? MaskedCardNumber { get; set; }

    /// <summary>شناسه‌ی پرداخت بورس (فیلد <c>vatpid</c>).</summary>
    public string? VatPaymentId { get; set; }

    /// <summary>شناسه‌ی قبض پرداخت بورس (فیلد <c>vatbid</c>).</summary>
    public string? VatBillId { get; set; }

    /// <summary>صورتحساب مربوطه.</summary>
    public Invoice? Invoice { get; set; }
}
