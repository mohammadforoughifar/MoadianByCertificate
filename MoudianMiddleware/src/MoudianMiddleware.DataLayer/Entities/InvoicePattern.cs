using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// الگوی صورتحساب (فیلد <c>inp</c>) به‌صورت Lookup قابل‌ویرایش؛ منبع اولیه از Seed تأمین می‌شود.
/// </summary>
/// <remarks>
/// چون جدول رسمی کامل در اختیار تیم نیست، مقادیر به‌عنوان داده (نه Magic Number در کد) نگه‌داری می‌شوند
/// تا اصلاح/تکمیل آن‌ها فقط نیازمند ویرایش داده باشد.
/// </remarks>
public class InvoicePattern : BaseEntity
{
    /// <summary>کد عددی الگو (همان مقدار ارسالی در <c>inp</c>).</summary>
    public int Code { get; set; }

    /// <summary>عنوان فارسی الگو.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>توضیح تکمیلی.</summary>
    public string? Description { get; set; }

    /// <summary>ترتیب نمایش.</summary>
    public int SortOrder { get; set; }

    /// <summary>آیا قابل انتخاب است؟</summary>
    public bool IsActive { get; set; } = true;
}
