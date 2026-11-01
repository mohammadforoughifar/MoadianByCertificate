using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

/// <summary>
/// نامه وارده — بر اساس ساختار جدول Oto_TBL_VaredeLetter
/// کلید مشترک با LetterSource (SourceType = 3)
/// </summary>
public class IncomingLetter
{
    /// <summary>کلید مشترک با LetterSource (یک‌به‌یک) — همان VaredeLetterId</summary>
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    /// <summary>شماره اتوماسیون / اندیکاتور کامل سیستم (مانند «1404/V/12»)</summary>
    [MaxLength(100)] public string? LetterNumber { get; set; }

    /// <summary>شماره ترتیبی سیستم داخل سال</summary>
    public int Number { get; set; }

    /// <summary>شماره ثبت اندیکاتور (NumberSabt)</summary>
    public int NumberSabt { get; set; }

    /// <summary>عنوان / موضوع نامه</summary>
    [MaxLength(200)] public string Title { get; set; } = "";

    /// <summary>روش ارسال: پست، پیک/دستی، ایمیل، نمابر، ایتا، اتوماسیون و ...</summary>
    [MaxLength(100)] public string? TypeErsal { get; set; }

    /// <summary>سازنده / مکان یا واحد (Creator)</summary>
    public int Creator { get; set; }

    /// <summary>سازندهٔ نامه در ساختارهای قدیمی دبیرخانه (CreatorId).</summary>
    public int CreatorId { get; set; }

    /// <summary>کاربر ثبت‌کننده نامه وارده در سیستم</summary>
    public int CreateUserId { get; set; }

    /// <summary>فرستنده خارج از سازمان / شرکت یا شخص مبدأ</summary>
    [MaxLength(200)] public string Ferestande { get; set; } = "";

    /// <summary>شماره نامه مبدأ / وارده</summary>
    [MaxLength(100)] public string? NumberLetterVarede { get; set; }

    /// <summary>تاریخ نامه مبدأ</summary>
    public DateTime Date { get; set; } = DateTime.Now;

    /// <summary>تاریخ ورود / دریافت / ثبت به سازمان (Date_Ersal / DateSabt)</summary>
    public DateTime DateErsal { get; set; } = DateTime.Now;

    /// <summary>توضیحات / خلاصه نامه</summary>
    public string? Description { get; set; }

    /// <summary>تحویل‌گیرنده / تحویل‌دهنده (Deliveryname)</summary>
    [MaxLength(100)] public string? DeliveryName { get; set; }

    /// <summary>سطح محرمانگی: 0=عادی، 1=محرمانه، 2=خیلی محرمانه، 3=سرّی</summary>
    public int Mahramanegi { get; set; }

    /// <summary>سطح فوریت: 0=عادی، 1=فوری، 2=خیلی فوری، 3=آنی</summary>
    public int Foriat { get; set; }

    /// <summary>نشان‌شده / ستاره‌دار توسط فرستنده/ثبت‌کننده</summary>
    public bool IsNeshan { get; set; }

    /// <summary>بایگانی‌شده</summary>
    public bool IsBayegani { get; set; }

    /// <summary>حذف منطقی</summary>
    public bool IsDelete { get; set; }

    public LetterSource Source { get; set; } = null!;
}

/// <summary>
/// رزرو شماره نامه — بر اساس ساختار جدول TBL_RezervationNumberLetter
/// TypeForm: 1=داخلی، 2=صادره، 3=وارده
/// </summary>
public class LetterNumberReservation
{
    public int Id { get; set; }

    /// <summary>نوع فرم: 1=داخلی، 2=صادره، 3=وارده</summary>
    public int TypeForm { get; set; } = 3;

    /// <summary>شماره رزرو شده اندیکاتور (NumberSabt)</summary>
    public int NumberSabt { get; set; }

    /// <summary>تاریخ رزرو</summary>
    public DateTime DateRezerv { get; set; } = DateTime.Now;

    /// <summary>سمت کاربر (در صورت وجود)</summary>
    public int? SematId { get; set; }

    /// <summary>کاربر رزرو‌کننده</summary>
    public int UserId { get; set; }

    /// <summary>آیا این شماره استفاده شده است؟</summary>
    public bool IsUsed { get; set; }

    public bool IsDelete { get; set; }
}
