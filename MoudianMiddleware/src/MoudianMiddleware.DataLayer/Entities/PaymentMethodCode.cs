using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// نگاشت روش پرداخت به کد عددی فیلد <c>pmt</c> در سامانه‌ی مودیان.
/// </summary>
/// <remarks>
/// مقدار عددی <c>pmt</c> در سند موجود صریحاً ذکر نشده است؛ بنابراین نگاشت در دیتابیس و
/// به‌صورت قابل‌ویرایش نگه‌داری می‌شود تا پس از تأیید رسمی فقط داده اصلاح شود.
/// </remarks>
public class PaymentMethodCode : BaseEntity
{
    /// <summary>کد داخلی روش پرداخت (مطابق <c>PaymentMethod</c> در Contracts).</summary>
    public int MethodCode { get; set; }

    /// <summary>عنوان فارسی روش پرداخت.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>کد ارسالی به سامانه در فیلد <c>pmt</c>.</summary>
    public int MoadianCode { get; set; }

    /// <summary>آیا نگاشت تأییدشده است؟ (تا تأیید، در UI هشدار نمایش داده می‌شود)</summary>
    public bool IsVerified { get; set; }

    /// <summary>آیا فعال است؟</summary>
    public bool IsActive { get; set; } = true;
}
