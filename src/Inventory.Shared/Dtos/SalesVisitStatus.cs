namespace Inventory.Shared;

/// <summary>
/// وضعیت سفارش ویزیتور — ماشین وضعیت صریح (به‌جای چند فیلد بولین پراکنده).
/// <para>
/// Draft → Submitted → Approved → Invoiced → Closed
/// مسیرهای فرعی: Returned (برگشت برای اصلاح)، Rejected (رد)، Canceled (ابطال)
/// </para>
/// </summary>
public enum VisOrderStatus
{
    /// <summary>پیش‌نویس — فقط برای ویزیتور، هیچ اثری روی موجودی/حساب ندارد</summary>
    Draft = 0,

    /// <summary>ارسال‌شده به مدیر — ویرایش ویزیتور قفل است</summary>
    Submitted = 1,

    /// <summary>تأیید مدیر — پیش‌نویس فاکتور ساخته شده/می‌شود</summary>
    Approved = 2,

    /// <summary>برگشت برای اصلاح (با دلیل اجباری)</summary>
    Returned = 3,

    /// <summary>رد‌شده (پایان مسیر)</summary>
    Rejected = 4,

    /// <summary>فاکتور قطعی شده — اثر روی انبار و حسابداری اعمال شد</summary>
    Invoiced = 5,

    /// <summary>تحویل و تسویه — پرونده بسته</summary>
    Closed = 6,

    /// <summary>ابطال‌شده</summary>
    Canceled = 7
}
