using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

// ============================================================
//  ماژول اتوماسیون اداری — فاز دوم: نامه صادره (Outgoing)
//  ساختار مشابه نامه داخلی اما با گیرنده بیرونی + وضعیت صدور
//  SourceType = 2 در LetterSource
//  پوشه‌بندی تمیز: هر موجودیت در فایل اختصاصی خودش
// ============================================================

/// <summary>
/// نامه صادره — شناسه = همان شناسه LetterSource (کلید مشترک)
/// شماره‌گذاری اندیکاتور بر اساس سال شمسی (هر سال از ۱ شروع می‌شود)
/// مانند نامه داخلی اما با اطلاعات گیرنده بیرونی
/// </summary>
public class OutgoingLetter
{
    /// <summary>کلید مشترک با LetterSource — دستی مقداردهی می‌شود</summary>
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    /// <summary>شماره اندیکاتور کامل نامه — مثل «1404/ص-12» یا «1404/12»</summary>
    [MaxLength(60)] public string? LetterNumber { get; set; }

    /// <summary>شماره ترتیبی داخل سال شمسی (هر سال از ۱ شروع می‌شود)</summary>
    public int Number { get; set; }

    /// <summary>کاربر ثبت‌کننده (فرستنده/صادرکننده) در مدل فعلی.</summary>
    public int CreatorUserId { get; set; }

    /// <summary>
    /// ستون سازگار با دیتابیس‌های قدیمی اتوماسیون. بعضی نسخه‌های قبلی جدول
    /// OutgoingLetters این ستون را اجباری تعریف کرده‌اند؛ هنگام درج باید همراه
    /// CreatorUserId مقداردهی شود تا خطای Cannot insert NULL رخ ندهد.
    /// </summary>
    public int CreatorId { get; set; }

    /// <summary>سمت ثبت‌کننده — بعد از پیاده‌سازی چارت سازمانی فعال می‌شود</summary>
    public int? CreatorSematId { get; set; }

    [MaxLength(300)] public string Title { get; set; } = "";
    public string? Text { get; set; }

    public DateTime DateSabt { get; set; } = DateTime.Now;

    /// <summary>محرمانگی: عادی / محرمانه / سری</summary>
    [MaxLength(20)] public string Mahramanegi { get; set; } = "عادی";

    /// <summary>فوریت: عادی / فوری / آنی</summary>
    [MaxLength(20)] public string Foriat { get; set; } = "عادی";

    // ==================== گیرنده بیرونی (اطلاعات صادره) ====================

    /// <summary>سازمان/شرکت مقصد — الزامی برای نامه صادره</summary>
    [MaxLength(250)] public string ReceiverOrganization { get; set; } = "";

    /// <summary>نام شخص گیرنده در سازمان مقصد (اختیاری)</summary>
    [MaxLength(250)] public string? ReceiverName { get; set; }

    /// <summary>سمت گیرنده (اختیاری)</summary>
    [MaxLength(250)] public string? ReceiverTitle { get; set; }

    /// <summary>آدرس/توضیحات مقصد (اختیاری)</summary>
    [MaxLength(500)] public string? ReceiverAddress { get; set; }

    /// <summary>رونوشت‌ها — متن آزاد (مثلاً: «واحد مالی - جهت اطلاع»)</summary>
    [MaxLength(1000)] public string? CopyTo { get; set; }

    /// <summary>شماره نامه طرف مقابل در صورت وجود (برای مکاتبات دوطرفه)</summary>
    [MaxLength(100)] public string? ExternalRefNumber { get; set; }

    /// <summary>شماره صادره رسمی — بعد از امضا مقدار می‌گیرد (درخواست کارفرما: SadereNumber)</summary>
    [MaxLength(60)] public string? SadereNumber { get; set; }

    /// <summary>تاریخ صدور رسمی — زمان امضای نهایی</summary>
    public DateTime? DateSadere { get; set; }

    /// <summary>شرکت صادرکننده نامه — سربرگ چاپ از همین شرکت خوانده می‌شود (جدول SystemCompanies)</summary>
    public int? CompanyId { get; set; }

    // ==================== دبیرخانه نامه صادره ====================
    // نامه فقط بعد از امضا (SadereNumber مقدار گرفته) وارد دبیرخانه می‌شود.

    /// <summary>آیا در دبیرخانه ثبت (ارسال) شده است؟</summary>
    public bool DabirkhaneSabt { get; set; }

    /// <summary>کاربر دبیرخانه که ثبت را انجام داده</summary>
    public int? DabirkhaneUserId { get; set; }

    /// <summary>تاریخ ثبت در دبیرخانه</summary>
    public DateTime? DateDabirkhane { get; set; }

    /// <summary>شماره ثبت مقصد — شماره‌ای که دبیرخانه سازمان مقصد به نامه می‌دهد</summary>
    [MaxLength(100)] public string? DestRegNumber { get; set; }

    /// <summary>روش ارسال نامه از دبیرخانه: پست / پیک / ایمیل / فکس / تحویل حضوری / اتوماسیون</summary>
    [MaxLength(50)] public string? SendMethod { get; set; }

    /// <summary>توضیحات دبیرخانه (اختیاری)</summary>
    [MaxLength(1000)] public string? DabirkhaneNote { get; set; }

    /// <summary>وضعیت صدور: 0=پیش‌نویس داخلی، 1=در گردش تایید، 2=تایید شده، 3=صادر شده
    /// وضعیت 3 فقط با امضای همه امضا کنندگان ثبت می‌شود و به‌صورت دستی قابل انتخاب نیست.</summary>
    public int Status { get; set; } = 0;

    /// <summary>نشان‌کردن (ستاره) نامه صادره توسط فرستنده — مشابه IsNeshan نامه داخلی.
    /// نشان گیرندگان داخلی روی Erja.IsNeshan است؛ این ستون برای سمت «ارسالی/صادره» است.</summary>
    public bool IsNeshan { get; set; }

    /// <summary>ایمیل مقصد — وقتی دبیرخانه نامه را با «پست الکترونیک» ارسال می‌کند پر می‌شود</summary>
    [MaxLength(250)] public string? DestEmail { get; set; }

    // ==================== فیلدهای وابسته به «روش ارسال» ====================
    // هر روش ارسال اطلاعات متفاوتی نیاز دارد:
    //   • ایمیل            → آدرس ایمیل مقصد (+ حساب ارسال‌کننده)
    //   • پست / پست پیشتاز  → نام تحویل گیرنده + کد رهگیری مرسوله
    //   • پیک / تحویل حضوری → نام تحویل گیرنده
    //   • فکس              → شماره فکس مقصد
    // این فیلدها در زمان «ثبت و ارسال» در دبیرخانه و بر اساس روش انتخابی پر می‌شوند.

    /// <summary>نام شخص تحویل‌گیرنده — برای پست، پست پیشتاز، پیک و تحویل حضوری</summary>
    [MaxLength(250)] public string? DelivererName { get; set; }

    /// <summary>کد رهگیری / شماره مرسوله پستی — برای پست و پست پیشتاز</summary>
    [MaxLength(100)] public string? TrackingCode { get; set; }

    /// <summary>شماره فکس مقصد — برای روش ارسال «فکس»</summary>
    [MaxLength(50)] public string? DestFax { get; set; }

    // ==================== بایگانی دبیرخانه ====================

    /// <summary>آیا این نامه در بایگانی دبیرخانه (درخت پوشه‌ها) ثبت شده است؟</summary>
    public bool IsArchived { get; set; }

    /// <summary>تاریخ بایگانی شدن نامه توسط دبیرخانه</summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>کاربر دبیرخانه‌ای که نامه را بایگانی کرده است</summary>
    public int? ArchivedByUserId { get; set; }

    public bool IsDelete { get; set; }

    [ForeignKey(nameof(Id))] public LetterSource Source { get; set; } = null!;
    public User? Creator { get; set; }

    public ICollection<OutgoingLetterSigner> Signers { get; set; } = new List<OutgoingLetterSigner>();
}

/// <summary>
/// امضا کنندگان نامه صادره — هر نامه صادره می‌تواند چند امضا کننده داشته باشد
/// با توجه به دسترسی افراد (RBAC: OutgoingLetters.Sign) انتخاب می‌شوند
/// وقتی امضا شد SadereNumber مقدار می‌گیرد
/// </summary>
public class OutgoingLetterSigner
{
    public int Id { get; set; }

    /// <summary>کلید نامه (LetterSource)</summary>
    public int SourceId { get; set; }

    public int UserId { get; set; }

    /// <summary>سمت — فاز چارت سازمانی</summary>
    public int? SematId { get; set; }

    /// <summary>ترتیب امضا (1=اولین امضا کننده)</summary>
    public int Order { get; set; } = 1;

    public bool IsSigned { get; set; }
    public DateTime? DateSigned { get; set; }

    /// <summary>
    /// آیا امضاکننده نامه را «خوانده» است — مستقل از امضا کردن.
    /// پیش‌تر خوانده‌شدن مساوی امضا شدن گرفته می‌شد و نامه تا لحظهٔ امضا در فهرست
    /// «خوانده‌نشده» می‌ماند.
    /// </summary>
    public bool IsRead { get; set; }
    public DateTime? DateRead { get; set; }

    /// <summary>توضیح/پاراف امضا کننده (اختیاری)</summary>
    [MaxLength(1000)] public string? SignNote { get; set; }

    public bool IsDelete { get; set; }

    [ForeignKey(nameof(SourceId))] public LetterSource Source { get; set; } = null!;
    public User? User { get; set; }
}

/// <summary>پیش‌نویس نامه صادره — مشابه PishnevisLetter اما با فیلدهای صادره</summary>
public class OutgoingPishnevisLetter
{
    public int PishnevisId { get; set; }

    [MaxLength(300)] public string Title { get; set; } = "";
    public string Text { get; set; } = "";

    [MaxLength(250)] public string? ReceiverOrganization { get; set; }
    [MaxLength(250)] public string? ReceiverName { get; set; }
    [MaxLength(250)] public string? ReceiverTitle { get; set; }

    public int UserId { get; set; }
    public int? SematId { get; set; }

    public bool IsNeshan { get; set; }
    public bool IsDelete { get; set; }

    public User? User { get; set; }
}
