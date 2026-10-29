using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// موضوع صورتحساب (فیلد <c>ins</c>) به‌صورت Lookup قابل‌ویرایش.
/// </summary>
public class InvoiceSubject : BaseEntity
{
    /// <summary>کد عددی موضوع (همان مقدار ارسالی در <c>ins</c>).</summary>
    public int Code { get; set; }

    /// <summary>عنوان فارسی.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>توضیح تکمیلی.</summary>
    public string? Description { get; set; }

    /// <summary>ترتیب نمایش.</summary>
    public int SortOrder { get; set; }

    /// <summary>آیا قابل انتخاب است؟</summary>
    public bool IsActive { get; set; } = true;
}
