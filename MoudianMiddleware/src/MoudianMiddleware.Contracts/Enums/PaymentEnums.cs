namespace MoudianMiddleware.Contracts.Enums;

/// <summary>
/// روش پرداخت؛ هم برای اقلام پرداخت صورتحساب (فیلد <c>pmt</c>) و هم برای
/// ثبت پرداخت مستقل در سامانه استفاده می‌شود.
/// </summary>
/// <remarks>
/// نگاشت به سامانه: برای ثبت پرداخت مستقل، نام انگلیسی (UPPER_SNAKE) ارسال می‌شود
/// (CHEQUE, BARTER, CASH, POS, INTERNET, CARD, TRANSFER, OTHER) و برای قلم پرداخت
/// صورتحساب، کد عددی فیلد <c>pmt</c>. صحت کدهای عددی <c>pmt</c> «نیازمند تأیید با
/// دستورالعمل رسمی» است و در <c>PaymentMethodCode</c> (Lookup قابل‌ویرایش) نگه‌داری می‌شود.
/// </remarks>
public enum PaymentMethod
{
    /// <summary>چک.</summary>
    Cheque = 1,

    /// <summary>تهاتر.</summary>
    Barter = 2,

    /// <summary>وجه نقد.</summary>
    Cash = 3,

    /// <summary>دستگاه کارت‌خوان (POS).</summary>
    Pos = 4,

    /// <summary>درگاه پرداخت اینترنتی.</summary>
    Internet = 5,

    /// <summary>کارت به کارت.</summary>
    Card = 6,

    /// <summary>انتقال به حساب.</summary>
    Transfer = 7,

    /// <summary>سایر.</summary>
    Other = 8
}
