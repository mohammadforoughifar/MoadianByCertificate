namespace Inventory.Shared;

/// <summary>
/// ابزار مشترک «پرداخت ترکیبی» — سرور و کلاینت هر دو از همین قاعده استفاده می‌کنند تا
/// نمایش و اعتبارسنجی یکی باشد.
///
/// قاعدهٔ محاسبهٔ باقی‌ماندهٔ تجمعی:
/// <code>
/// باقی‌ماندهٔ سطر ۱ = مبلغ سند − (نقد + جمع سطرهای قبلی)
/// باقی‌ماندهٔ نهایی   = مبلغ سند − (نقد + چک‌ها + اقساط)  → همان مبلغ نسیه
/// </code>
/// </summary>
public static class PaymentMix
{
    /// <summary>تلورانس یک ریالی برای خطای گرد کردن.</summary>
    public const decimal Tolerance = 1m;

    public static string Fa(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "نقدی",
        PaymentMethod.Credit => "نسیه",
        PaymentMethod.Cheque => "چک",
        PaymentMethod.Installment => "اقساطی",
        PaymentMethod.Mixed => "ترکیبی",
        _ => "—"
    };

    /// <summary>
    /// روش پرداخت مؤثر را از اجزای واقعی پرداخت استخراج می‌کند:
    /// تنها نقد ⇒ نقدی، تنها چک ⇒ چک، … و بیش از یک جزء ⇒ ترکیبی.
    /// </summary>
    public static PaymentMethod Derive(decimal cash, decimal chequeSum, decimal installmentSum, decimal creditRemainder)
    {
        var parts = 0;
        if (cash > Tolerance / 2) parts++;
        if (chequeSum > Tolerance / 2) parts++;
        if (installmentSum > Tolerance / 2) parts++;
        if (creditRemainder > Tolerance / 2) parts++;

        if (parts == 0) return PaymentMethod.Cash;
        if (parts > 1) return PaymentMethod.Mixed;
        if (cash > Tolerance / 2) return PaymentMethod.Cash;
        if (chequeSum > Tolerance / 2) return PaymentMethod.Cheque;
        if (installmentSum > Tolerance / 2) return PaymentMethod.Installment;
        return PaymentMethod.Credit;
    }

    /// <summary>خلاصهٔ یک‌خطی پرداخت برای نمایش در فهرست‌ها و پیام‌ها.</summary>
    public static string Describe(PaymentMethod method, decimal cash, decimal chequeSum, decimal installmentSum, decimal creditRemainder)
    {
        if (method != PaymentMethod.Mixed) return Fa(method);

        var parts = new List<string>();
        if (cash > Tolerance / 2) parts.Add($"نقد {cash:#,0}");
        if (chequeSum > Tolerance / 2) parts.Add($"چک {chequeSum:#,0}");
        if (installmentSum > Tolerance / 2) parts.Add($"اقساط {installmentSum:#,0}");
        if (creditRemainder > Tolerance / 2) parts.Add($"نسیه {creditRemainder:#,0}");
        return parts.Count == 0 ? Fa(method) : "ترکیبی (" + string.Join(" + ", parts) + ")";
    }
}
