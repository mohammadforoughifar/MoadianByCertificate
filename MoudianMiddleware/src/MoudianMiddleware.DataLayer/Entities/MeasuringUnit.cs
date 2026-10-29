using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// واحد اندازه‌گیری (فیلد <c>mu</c> در سامانه‌ی مودیان)؛ داده‌ی مرجع سراسری و مشترک بین همه‌ی مودیان.
/// </summary>
public class MeasuringUnit : AuditableEntity
{
    /// <summary>کد واحد در سامانه‌ی مودیان (مثلاً 164 برای «عدد»).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>عنوان فارسی واحد.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>نماد نمایشی (اختیاری).</summary>
    public string? Symbol { get; set; }

    /// <summary>ترتیب نمایش در فهرست.</summary>
    public int SortOrder { get; set; }

    /// <summary>آیا قابل انتخاب است؟</summary>
    public bool IsActive { get; set; } = true;
}
