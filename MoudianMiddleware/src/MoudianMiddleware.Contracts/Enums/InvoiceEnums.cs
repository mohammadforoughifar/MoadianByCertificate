namespace MoudianMiddleware.Contracts.Enums;

/// <summary>
/// نوع صورتحساب (فیلد <c>inty</c> در سامانه‌ی مودیان).
/// </summary>
/// <remarks>
/// مقادیر بر اساس جدول رسمی مودیان است؛ در صورت تغییر، فقط Seed و این Enum به‌روزرسانی می‌شود.
/// </remarks>
public enum InvoiceType
{
    /// <summary>صورتحساب اصلی (فروش).</summary>
    Original = 1,

    /// <summary>صورتحساب اصلاحی (نیازمند شماره مالیاتی مرجع).</summary>
    Correction = 2,

    /// <summary>صورتحساب ابطالی (نیازمند شماره مالیاتی مرجع).</summary>
    Cancellation = 3,

    /// <summary>برگشت از فروش (نیازمند شماره مالیاتی مرجع).</summary>
    ReturnFromSale = 4
}

/// <summary>
/// نوع شخص خریدار (فیلد <c>tob</c>).
/// </summary>
public enum BuyerType
{
    /// <summary>شخص حقیقی — شناسه، کد ملی است.</summary>
    NaturalPerson = 1,

    /// <summary>شخص حقوقی — شناسه، شناسه‌ی ملی است.</summary>
    LegalPerson = 2
}

/// <summary>
/// روش تسویه‌ی صورتحساب (فیلد <c>setm</c>).
/// </summary>
public enum SettlementMethod
{
    /// <summary>نقدی.</summary>
    Cash = 1,

    /// <summary>نسیه.</summary>
    Credit = 2,

    /// <summary>نقدی و نسیه (ترکیبی).</summary>
    CashAndCredit = 3
}

/// <summary>
/// وضعیت چرخه‌ی عمر صورتحساب در نرم‌افزار واسط.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>پیش‌نویس؛ قابل ویرایش.</summary>
    Draft = 1,

    /// <summary>آماده‌ی ارسال (اعتبارسنجی محلی موفق).</summary>
    ReadyToSend = 2,

    /// <summary>در حال ارسال به سامانه.</summary>
    Sending = 3,

    /// <summary>ارسال شد و شماره‌ی پیگیری دریافت شد.</summary>
    Sent = 4,

    /// <summary>در صف اعتبارسنجی سامانه (IN_PROGRESS).</summary>
    InProgress = 5,

    /// <summary>تأیید و ثبت نهایی در کارپوشه (SUCCESS).</summary>
    Verified = 6,

    /// <summary>رد شده توسط سامانه (FAILED).</summary>
    Rejected = 7,

    /// <summary>خطای فنی/شبکه در ارسال؛ قابل تلاش مجدد.</summary>
    Failed = 8,

    /// <summary>ابطال‌شده در نرم‌افزار (قبل از ارسال).</summary>
    Cancelled = 9
}
