using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// تعطیلی مجموعه — مدیر یک روز را برای کل پرسنل تعطیل می‌کند؛ یا «تعطیل رسمی کشور»
/// (IsOfficial=true — مثل نوروز و عید قربان که از بخش تعطیلات رسمی تقویم کاری وارد می‌شود).
/// در این روز هیچ کاربری کسری نمی‌خورد و در «کل تردد ماه» با وضعیت تعطیلی (سبز) نمایش داده می‌شود.
/// </summary>
public class CompanyHoliday
{
    public int Id { get; set; }

    /// <summary>تاریخ تعطیلی (بدون زمان)</summary>
    public DateTime HolidayDate { get; set; }

    /// <summary>عنوان/دلیل — مثلاً: ایام عزاداری، نشست سازمانی</summary>
    [MaxLength(100)]
    public string Name { get; set; } = "";

    /// <summary>true = تعطیل رسمی کشور (مثل نوروز، عید فطر، ۲۲ بهمن) — false = تعطیلی شرکتی</summary>
    public bool IsOfficial { get; set; }

    /// <summary>نام مدیری که این تعطیلی را ثبت کرده (اعمال گروهی برای کل پرسنل)</summary>
    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
