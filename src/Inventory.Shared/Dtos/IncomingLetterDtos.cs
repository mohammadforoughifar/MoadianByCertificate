using System.ComponentModel.DataAnnotations;

namespace Inventory.Shared.Dtos;

/// <summary>خلاصه سطر کارتابل نامه وارده</summary>
public class IncomingLetterListItemDto
{
    public int LetterId { get; set; }
    public int? ErjaId { get; set; }
    public string LetterNumber { get; set; } = "";
    public int NumberSabt { get; set; }
    public string NumberLetterVarede { get; set; } = "";
    public string Title { get; set; } = "";
    public string Ferestande { get; set; } = "";
    public string Sender { get; set; } = "";

    /// <summary>تعداد پیوست‌های نامه (صفر یعنی بدون پیوست)</summary>
    public int AttachmentCount { get; set; }
    public int SenderUserId { get; set; }
    public DateTime Date { get; set; }
    public DateTime DateErsal { get; set; }
    public string TypeErsal { get; set; } = "";
    public string DeliveryName { get; set; } = "";
    public int Mahramanegi { get; set; }
    public int Foriat { get; set; }
    /// <summary>گیرنده / ارجاع / هامش — همان Erja.Type</summary>
    public string? ErjaType { get; set; }
    public string? MatnErja { get; set; }
    public DateTime? MohlatPasokh { get; set; }
    public bool IsNeshan { get; set; }
    public bool IsRead { get; set; }
    public bool IsBayegani { get; set; }
    public bool HasAnswer { get; set; }
    public int ReciverCount { get; set; }
    public bool HasAttachment { get; set; }
}

/// <summary>جزئیات کامل نامه وارده جهت نمایش در ریدر</summary>
public class IncomingLetterDetailDto
{
    public int LetterId { get; set; }
    public int SourceId { get; set; }
    public string LetterNumber { get; set; } = "";
    public int Number { get; set; }
    public int NumberSabt { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Ferestande { get; set; } = "";
    public string NumberLetterVarede { get; set; } = "";
    public DateTime Date { get; set; }
    public DateTime DateErsal { get; set; }
    public string TypeErsal { get; set; } = "";
    public string DeliveryName { get; set; } = "";
    public int CreatorUserId { get; set; }
    public string CreatorName { get; set; } = "";
    public int Mahramanegi { get; set; }
    public int Foriat { get; set; }
    public bool IsNeshan { get; set; }
    public bool IsMine { get; set; }

    /// <summary>ارجاع کاربر جاری (در صورت وجود)</summary>
    public ErjaDto? CurrentErja { get; set; }

    /// <summary>همه ارجاع‌های نامه (برای گردش نامه)</summary>
    public List<ErjaDto> Erjas { get; set; } = new();

    /// <summary>نامه‌های مرتبط (عطف/پیرو)</summary>
    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();

    /// <summary>پیوست‌های نامه</summary>
    public List<LetterAttachmentDto> Attachments { get; set; } = new();

    /// <summary>تاریخچه ارجاع‌ها — نام مستعار Erjas</summary>
    public List<ErjaDto> ErjaHistory
    {
        get => Erjas;
        set => Erjas = value ?? new List<ErjaDto>();
    }

    /// <summary>ارجاعی که کاربر جاری باید به آن پاسخ دهد</summary>
    public int? ActiveErjaId { get; set; }

    /// <summary>
    /// آیا کاربر جاری مجاز به ویرایش است: ثبت‌کننده، مدیر، یا کاربرِ دبیرخانه
    /// (مجوز IncomingLetters.Dabirkhane). ویرایش پس از رویتِ گیرندگان هم مجاز است و
    /// در آن حالت به همهٔ کسانی که نامه را دیده‌اند اعلان «ویرایش نامه» می‌رود.
    /// </summary>
    public bool CanEdit { get; set; }

    /// <summary>آیا نامه نزد کاربر جاری بایگانی شده</summary>
    public bool IsBayegani { get; set; }
}

/// <summary>فرم ثبت نامه وارده جدید</summary>
public class AddIncomingLetterDto
{
    [Required(ErrorMessage = "موضوع / عنوان نامه الزامی است.")]
    [MaxLength(200, ErrorMessage = "عنوان نباید بیش از ۲۰۰ کاراکتر باشد.")]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "فرستنده نامه الزامی است.")]
    [MaxLength(200, ErrorMessage = "نام فرستنده نباید بیش از ۲۰۰ کاراکتر باشد.")]
    public string Ferestande { get; set; } = "";

    [MaxLength(100)] public string? NumberLetterVarede { get; set; }

    /// <summary>شماره اندیکاتور ثبت شده یا انتخاب‌شده از لیست رزرو</summary>
    public int? NumberSabt { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;
    public DateTime DateErsal { get; set; } = DateTime.Now;

    [MaxLength(100)] public string? TypeErsal { get; set; }
    [MaxLength(100)] public string? DeliveryName { get; set; }

    public string? Description { get; set; }

    public int Mahramanegi { get; set; }
    public int Foriat { get; set; }

    /// <summary>گیرندگان اولیه برای ارجاع خودکار هنگام ثبت</summary>
    public List<int> InitialReciverUserIds { get; set; } = new();

    /// <summary>متن ارجاع اولیه</summary>
    public string? InitialMatnErja { get; set; }

    /// <summary>مهلت پاسخ ارجاع اولیه (مهلت رسیدگی گیرندهٔ اول)</summary>
    public DateTime? InitialMohlatPasokh { get; set; }

    /// <summary>
    /// نامه‌های مرتبط (عطف / پیرو). Related = 2 یعنی «عطف»، 1 یعنی «پیرو».
    /// تا پیش از این هیچ ورودی UI و هیچ ذخیره‌سازی‌ای برای آن نبود.
    /// </summary>
    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();

    /// <summary>
    /// سازگاری با نسخه‌های قبلی کلاینت: شناسهٔ LetterSource نامه‌های مرتبط.
    /// چون نوع رابطه در این فیلد مشخص نیست، همه به‌صورت «پیرو» ثبت می‌شوند.
    /// کلاینت جدید از RelatedLetters استفاده می‌کند.
    /// </summary>
    public List<int> RelatedSourceIds { get; set; } = new();
}

/// <summary>ویرایش نامه وارده (قبل از ارجاع یا خوانده‌شدن)</summary>
public class EditIncomingLetterDto
{
    [Required(ErrorMessage = "عنوان نامه الزامی است.")]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "فرستنده الزامی است.")]
    [MaxLength(200)]
    public string Ferestande { get; set; } = "";

    [MaxLength(100)] public string? NumberLetterVarede { get; set; }
    public DateTime Date { get; set; }
    public DateTime DateErsal { get; set; }
    [MaxLength(100)] public string? TypeErsal { get; set; }
    [MaxLength(100)] public string? DeliveryName { get; set; }
    public string? Description { get; set; }
    public int Mahramanegi { get; set; }
    public int Foriat { get; set; }

    /// <summary>
    /// نامه‌های مرتبط (عطف/پیرو) — فهرست کامل و جایگزین‌شونده.
    /// null یعنی «دست نزن»؛ فهرست خالی یعنی «همهٔ رابطه‌ها حذف شوند».
    /// </summary>
    public List<RelatedLetterDto>? RelatedLetters { get; set; }
}

/// <summary>آمار کارتابل نامه وارده</summary>
public class IncomingLetterCartableStatsDto
{
    public int TotalInbox { get; set; }
    public int UnreadCount { get; set; }
    public int TotalSent { get; set; }
    public int TotalArchive { get; set; }
    public int TotalStarred { get; set; }

    /// <summary>تعداد نامه‌های دارای پیوست در «دریافتی» (برای عدد چیپ «پیوست‌دار»)</summary>
    public int TotalAttach { get; set; }

    /// <summary>تعداد نامه‌های دستورکارشده در «دریافتی» (برای عدد چیپ «دستورکارشده»)</summary>
    public int TotalWorkOrder { get; set; }

    // نام‌های مستعار — رابط این آمار را با نام‌های رایج کارتابل می‌خواند
    public int InboxUnread => UnreadCount;
    public int InboxTotal => TotalInbox;
    public int SentTotal => TotalSent;
    public int ArchiveTotal => TotalArchive;
}

/// <summary>آیتم انتخاب نامه وارده جهت عطف/پیرو</summary>
public class IncomingLetterPickDto
{
    public int SourceId { get; set; }
    public string LetterNumber { get; set; } = "";
    public int NumberSabt { get; set; }
    public string Title { get; set; } = "";
    public string Ferestande { get; set; } = "";
    public DateTime Date { get; set; }
}

/// <summary>رزرو شماره نامه اندیکاتور (TBL_RezervationNumberLetter)</summary>
public class LetterNumberReservationDto
{
    public int Id { get; set; }
    public int TypeForm { get; set; }
    public string FormTitle => TypeForm switch { 1 => "داخلی", 2 => "صادره", 3 => "وارده", _ => "نامشخص" };
    public int NumberSabt { get; set; }
    public DateTime DateRezerv { get; set; }

    /// <summary>نام مستعارِ DateRezerv</summary>
    public DateTime DateReservation { get => DateRezerv; set => DateRezerv = value; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public bool IsUsed { get; set; }
}

/// <summary>درخواست رزرو شماره</summary>
public class ReserveNumberRequestDto
{
    public int TypeForm { get; set; } = 3; // پیش‌فرض 3 = وارده
    public int Count { get; set; } = 1;
}
