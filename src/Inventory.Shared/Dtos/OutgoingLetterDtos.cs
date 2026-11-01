using System.ComponentModel.DataAnnotations;

namespace Inventory.Shared.Dtos;

// ============================================================
//  ماژول اتوماسیون اداری — فاز دوم: نامه صادره (Outgoing)
//  DTO ها مشابه نامه داخلی اما با گیرنده بیرونی + وضعیت صدور
//  پوشه‌بندی تمیز و خوانا — هر DTO با کامنت فارسی
// ============================================================

/// <summary>ثبت/ویرایش نامه صادره — معادل AddInnerLetterDto اما با گیرنده بیرونی</summary>
public class AddOutgoingLetterDto
{
    public int LetterId { get; set; }

    [Required(ErrorMessage = "عنوان نامه الزامی است")]
    public string Title { get; set; } = "";
    public string? Text { get; set; }

    /// <summary>عادی / محرمانه / سری</summary>
    public string Mahramanegi { get; set; } = "عادی";

    /// <summary>عادی / فوری / آنی</summary>
    public string Foriat { get; set; } = "عادی";

    // ==================== گیرنده بیرونی — الزامی برای صادره ====================

    [Required(ErrorMessage = "نام سازمان مقصد الزامی است")]
    public string ReceiverOrganization { get; set; } = "";

    public string? ReceiverName { get; set; }
    public string? ReceiverTitle { get; set; }
    public string? ReceiverAddress { get; set; }

    /// <summary>رونوشت‌ها — متن آزاد</summary>
    /// <summary>رونوشت‌ها — متن آزاد (ستون قدیمی؛ برای نامه‌های پیش از این)</summary>
    public string? CopyTo { get; set; }

    /// <summary>رونوشت‌گیرندگان از جدول مستقل — منبع اصلی برای فرم و چاپ</summary>
    public List<SaveOutgoingLetterCopyToDto> CopyTos { get; set; } = new();

    public string? ExternalRefNumber { get; set; }

    /// <summary>شرکت صادرکننده (سربرگ چاپ) — از جدول کمپانی‌ها</summary>
    public int? CompanyId { get; set; }

    // ==================== گردش داخلی جهت تایید قبل از صدور ====================

    /// <summary>گیرندگان اصلی داخلی (جهت تایید/اطلاع قبل از صدور)</summary>
    public List<int> ReciversGirande { get; set; } = new();

    /// <summary>گیرندگان ارجاع (جهت اقدام)</summary>
    public List<int> ReciversErja { get; set; } = new();

    /// <summary>گیرندگان هامش (رونوشت داخلی)</summary>
    public List<int> ReciversHamesh { get; set; } = new();

    public List<int> GroupsGirande { get; set; } = new();
    public List<int> GroupsErja { get; set; } = new();
    public List<int> GroupsHamesh { get; set; } = new();

    /// <summary>نامه‌های مرتبط: عطف (2) / پیرو (1)</summary>
    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();

    /// <summary>شناسه پیش‌نویس مبدأ — بعد از ارسال حذف می‌شود (اختیاری)</summary>
    public int? FromPishnevisId { get; set; }

    // ==================== امضا کنندگان — انتخاب بر اساس دسترسی ====================

    /// <summary>کاربران امضا کننده — باید دسترسی OutgoingLetters.Sign داشته باشند</summary>
    public List<int> SignerUserIds { get; set; } = new();

    /// <summary>کاربر به‌همراه سمت انتخاب‌شده برای امضای نامه.</summary>
    public List<OutgoingSignerSelectionDto> SignerSelections { get; set; } = new();

    /// <summary>گروه‌های امضا کننده — اعضای گروه باید دسترسی Sign داشته باشند</summary>
    public List<int> SignerGroupIds { get; set; } = new();
}

/// <summary>ویرایش نامه صادره — فقط قبل از خوانده‌شدن توسط گیرندگان داخلی یا قبل از صدور نهایی</summary>
public class EditOutgoingLetterDto
{
    [Required(ErrorMessage = "عنوان نامه الزامی است")]
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    public string Mahramanegi { get; set; } = "عادی";
    public string Foriat { get; set; } = "عادی";

    [Required(ErrorMessage = "نام سازمان مقصد الزامی است")]
    public string ReceiverOrganization { get; set; } = "";
    public string? ReceiverName { get; set; }
    public string? ReceiverTitle { get; set; }
    public string? ReceiverAddress { get; set; }
    public string? CopyTo { get; set; }

    /// <summary>رونوشت‌گیرندگان از جدول مستقل — منبع اصلی برای فرم و چاپ</summary>
    public List<SaveOutgoingLetterCopyToDto> CopyTos { get; set; } = new();
    public string? ExternalRefNumber { get; set; }

    /// <summary>شرکت صادرکننده (سربرگ چاپ)</summary>
    public int? CompanyId { get; set; }

    // گیرندگان داخلی — قابل ویرایش تا قبل از خوانده‌شدن
    public List<int> ReciversGirande { get; set; } = new();
    public List<int> GroupsGirande { get; set; } = new();
    public List<int> ReciversErja { get; set; } = new();
    public List<int> GroupsErja { get; set; } = new();
    public List<int> ReciversHamesh { get; set; } = new();
    public List<int> GroupsHamesh { get; set; } = new();

    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();

    public List<int> SignerUserIds { get; set; } = new();
    public List<OutgoingSignerSelectionDto> SignerSelections { get; set; } = new();
    public List<int> SignerGroupIds { get; set; } = new();
}

/// <summary>انتخاب امضاکننده با سمت مشخص</summary>
public sealed class OutgoingSignerSelectionDto
{
    public int UserId { get; set; }
    public int? SematId { get; set; }
}

/// <summary>امضا کننده نامه صادره</summary>
public class OutgoingSignerDto
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int? SematId { get; set; }
    public string? SematTitle { get; set; }
    public int Order { get; set; }
    public bool IsSigned { get; set; }
    public DateTime? DateSigned { get; set; }
    public string? SignNote { get; set; }
}

/// <summary>درخواست امضا</summary>
public class SignOutgoingLetterDto
{
    public string? SignNote { get; set; }
}

/// <summary>سطر لیست کارتابل نامه صادره</summary>
public class OutgoingLetterListItemDto
{
    public int LetterId { get; set; }
    public int? ErjaId { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Sender { get; set; } = "";
    public int SenderUserId { get; set; }

    // گیرنده بیرونی
    public string ReceiverOrganization { get; set; } = "";
    public string? ReceiverName { get; set; }

    public DateTime Date { get; set; }
    public string Mahramanegi { get; set; } = "عادی";
    public string Foriat { get; set; } = "عادی";
    public string? ErjaType { get; set; }
    public string? MatnErja { get; set; }
    public DateTime? MohlatPasokh { get; set; }
    public bool IsNeshan { get; set; }
    public bool HasAttachment { get; set; }
    public bool IsRead { get; set; }
    public int ReciverCount { get; set; }
    public int TypeTaeed { get; set; }
    public bool HasAnswer { get; set; }
    public int Status { get; set; }
    public string StatusTitle => Status switch
    {
        0 => "پیش‌نویس",
        1 => "در گردش",
        2 => "تایید شده",
        3 => "صادر شده",
        _ => "نامشخص"
    };

    // امضا
    public string? SadereNumber { get; set; }
    public DateTime? DateSadere { get; set; }
    public bool IsSigner { get; set; }
    public bool IsSigned { get; set; }
    public bool CanSign { get; set; }
    public int SignersTotal { get; set; }
    public int SignersSigned { get; set; }

    // دبیرخانه
    public bool DabirkhaneSabt { get; set; }
    public DateTime? DateDabirkhane { get; set; }
    public string? DestRegNumber { get; set; }
    public string? SendMethod { get; set; }
    public string? DabirkhaneNote { get; set; }
    public string? DabirkhaneUserName { get; set; }

    // شرکت صادرکننده (سربرگ)
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
}

/// <summary>جزئیات کامل نامه صادره</summary>
public class OutgoingLetterDetailDto
{
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = "";
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    public string Mahramanegi { get; set; } = "عادی";
    public string Foriat { get; set; } = "عادی";
    public DateTime DateSabt { get; set; }
    public int SenderUserId { get; set; }
    public string SenderName { get; set; } = "";

    // گیرنده بیرونی
    public string ReceiverOrganization { get; set; } = "";
    public string? ReceiverName { get; set; }
    public string? ReceiverTitle { get; set; }
    public string? ReceiverAddress { get; set; }
    public string? CopyTo { get; set; }

    /// <summary>رونوشت‌گیرندگان از جدول مستقل — منبع اصلی برای فرم و چاپ</summary>
    public List<OutgoingLetterCopyToDto> CopyTos { get; set; } = new();
    public string? ExternalRefNumber { get; set; }
    public int Status { get; set; }

    // شماره صادره رسمی — بعد از امضا
    public string? SadereNumber { get; set; }
    public DateTime? DateSadere { get; set; }

    // شرکت صادرکننده (سربرگ چاپ)
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }

    // دبیرخانه
    public bool DabirkhaneSabt { get; set; }
    public DateTime? DateDabirkhane { get; set; }
    public string? DestRegNumber { get; set; }
    public string? SendMethod { get; set; }
    public string? DabirkhaneNote { get; set; }
    public string? DabirkhaneUserName { get; set; }

    /// <summary>ایمیل مقصد که دبیرخانه نامه را به آن ارسال کرده (روش ارسال ایمیل)</summary>
    public string? DestEmail { get; set; }

    /// <summary>نشان‌کردن (ستاره) نامه صادره توسط فرستنده — سمت ارسالی</summary>
    public bool IsNeshan { get; set; }

    public List<LetterReciverDto> ReciversGirande { get; set; } = new();
    public List<LetterReciverDto> ReciversErja { get; set; } = new();
    public List<LetterReciverDto> ReciversHamesh { get; set; } = new();
    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();
    public List<OutgoingSignerDto> Signers { get; set; } = new();

    public ErjaDto? MyErja { get; set; }
    public OutgoingSignerDto? MySigner { get; set; }
    public bool IsMine { get; set; }
    public bool CanEdit { get; set; }
    public bool CanSign { get; set; }
    public bool IsSigner { get; set; }
}

/// <summary>پیش‌نویس نامه صادره</summary>
public class OutgoingPishnevisDto
{
    public int PishnevisId { get; set; }

    [Required(ErrorMessage = "عنوان الزامی است")]
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? ReceiverOrganization { get; set; }
    public string? ReceiverName { get; set; }
    public string? ReceiverTitle { get; set; }
    public bool IsNeshan { get; set; }
}

/// <summary>آمار کارتابل نامه صادره</summary>
public class OutgoingLetterCartableStatsDto
{
    public int InboxUnread { get; set; }
    public int InboxTotal { get; set; }
    public int SentTotal { get; set; }
    public int PishnevisTotal { get; set; }
    public int DeadlineSoon { get; set; }
    public int DraftTotal { get; set; }
}

/// <summary>آیتم انتخاب نامه برای عطف/پیرو در صادره — شامل هر دو نوع داخلی و صادره</summary>
public class OutgoingLetterPickDto
{
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Date { get; set; }
    public bool IsSent { get; set; }
    public int SourceType { get; set; }
    public string SourceTypeTitle => SourceType == 2 ? "صادره" : "داخلی";
}

// ============================================================
//  دبیرخانه نامه صادره — نامه‌های امضا شده (SadereNumber دار)
// ============================================================

/// <summary>روش‌های ارسال نامه از دبیرخانه</summary>
public static class LetterSendMethods
{
    public const string Post = "پست";
    public const string PostPishTaz = "پست پیشتاز";
    public const string Peyk = "پیک";
    public const string Email = "ایمیل";
    public const string Fax = "فکس";
    public const string Hozoori = "تحویل حضوری";
    public const string Ece = "اتوماسیون (ECE)";

    public static readonly string[] All =
    {
        Post, PostPishTaz, Peyk, Email, Fax, Hozoori, Ece
    };

    /// <summary>
    /// مشخصات فیلدهای موردنیاز هر روش ارسال.
    /// دبیرخانه با انتخاب هر روش، فقط فیلدهای مرتبط با همان روش را از کاربر می‌پرسد:
    /// • ایمیل            → «کدوم ایمیل» (آدرس ایمیل مقصد + حساب ارسال‌کننده)
    /// • پست / پست پیشتاز  → «نام تحویل گیرنده» (+ کد رهگیری مرسوله)
    /// • پیک / تحویل حضوری → «نام تحویل گیرنده»
    /// • فکس              → «شماره فکس مقصد»
    /// • اتوماسیون (ECE)  → «شماره ثبت مقصد»
    /// </summary>
    public static SendMethodSpec Spec(string? method)
    {
        if (string.IsNullOrWhiteSpace(method)) return new SendMethodSpec { Method = "" };

        var m = method.Trim();

        if (m == Email)
            return new SendMethodSpec
            {
                Method = m,
                NeedsDestEmail = true,
                NeedsEmailAccount = true,
                Hint = "نامه به‌صورت PDF روی سربرگ شرکت، به ایمیل مقصد ارسال می‌شود."
            };

        if (m == Post || m == PostPishTaz)
            return new SendMethodSpec
            {
                Method = m,
                NeedsDelivererName = true,
                NeedsTrackingCode = true,
                TrackingCodeRequired = false,
                Hint = m == PostPishTaz
                    ? "برای پست پیشتاز، نام کامل تحویل گیرنده و کد رهگیری مرسوله را ثبت کنید."
                    : "نام تحویل گیرنده را بنویسید؛ کد رهگیری در صورت وجود ثبت شود."
            };

        if (m == Peyk || m == Hozoori)
            return new SendMethodSpec
            {
                Method = m,
                NeedsDelivererName = true,
                Hint = m == Peyk
                    ? "نام شخصی که مرسوله را از پیک تحویل می‌گیرد بنویسید."
                    : "نام شخصی که نامه را حضوری تحویل می‌گیرد بنویسید."
            };

        if (m == Fax)
            return new SendMethodSpec
            {
                Method = m,
                NeedsFax = true,
                Hint = "شماره فکس مقصد را با پیش‌شماره وارد کنید."
            };

        if (m == Ece)
            return new SendMethodSpec
            {
                Method = m,
                NeedsDestRegNumber = true,
                Hint = "شماره ثبت اتوماسیون سازمان مقصد (ECE) را وارد کنید."
            };

        return new SendMethodSpec { Method = m };
    }
}

/// <summary>فیلدهایی که برای یک روش ارسال باید از دبیرخانه پرسیده شود</summary>
public class SendMethodSpec
{
    public string Method { get; set; } = "";

    /// <summary>آدرس ایمیل مقصد لازم است؟ (روش ارسال = ایمیل)</summary>
    public bool NeedsDestEmail { get; set; }

    /// <summary>انتخاب حساب ایمیل دبیرخانه (ارسال‌کننده) لازم است؟</summary>
    public bool NeedsEmailAccount { get; set; }

    /// <summary>نام تحویل گیرنده لازم است؟ (پست/پست پیشتاز/پیک/تحویل حضوری)</summary>
    public bool NeedsDelivererName { get; set; }

    /// <summary>کد رهگیری مرسوله نمایش داده شود؟</summary>
    public bool NeedsTrackingCode { get; set; }

    /// <summary>کد رهگیری اجباری است؟</summary>
    public bool TrackingCodeRequired { get; set; }

    /// <summary>شماره فکس مقصد لازم است؟</summary>
    public bool NeedsFax { get; set; }

    /// <summary>شماره ثبت مقصد (ECE) اجباری است؟</summary>
    public bool NeedsDestRegNumber { get; set; }

    /// <summary>راهنمای نمایش‌داده‌شده زیر فیلدها</summary>
    public string Hint { get; set; } = "";
}

/// <summary>سطر لیست دبیرخانه نامه صادره</summary>
public class DabirkhaneListItemDto
{
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = "";
    public string? SadereNumber { get; set; }
    public string Title { get; set; } = "";
    public string CreatorName { get; set; } = "";
    public string ReceiverOrganization { get; set; } = "";
    public string? ReceiverName { get; set; }
    public DateTime DateSabt { get; set; }
    public DateTime? DateSadere { get; set; }
    public string Mahramanegi { get; set; } = "عادی";
    public string Foriat { get; set; } = "عادی";
    public bool HasAttachment { get; set; }
    public int SignersTotal { get; set; }
    public int SignersSigned { get; set; }

    // شرکت صادرکننده (سربرگ)
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }

    // وضعیت دبیرخانه
    public bool DabirkhaneSabt { get; set; }
    public DateTime? DateDabirkhane { get; set; }
    public string? DestRegNumber { get; set; }
    public string? SendMethod { get; set; }
    public string? DabirkhaneNote { get; set; }
    public string? DabirkhaneUserName { get; set; }

    /// <summary>ایمیل مقصد — وقتی با پست الکترونیک ارسال شده پر می‌شود</summary>
    public string? DestEmail { get; set; }

    // ==================== فیلدهای وابسته به روش ارسال ====================

    /// <summary>نام تحویل گیرنده — پست / پست پیشتاز / پیک / تحویل حضوری</summary>
    public string? DelivererName { get; set; }

    /// <summary>کد رهگیری مرسوله پستی</summary>
    public string? TrackingCode { get; set; }

    /// <summary>شماره فکس مقصد</summary>
    public string? DestFax { get; set; }

    // ==================== بایگانی دبیرخانه ====================

    /// <summary>آیا نامه در بایگانی دبیرخانه ثبت شده است؟</summary>
    public bool IsArchived { get; set; }

    /// <summary>زمان بایگانی شدن</summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>نام کاربر دبیرخانه‌ای که نامه را بایگانی کرده است</summary>
    public string? ArchivedByUserName { get; set; }

    /// <summary>عنوان پوشه‌ای که نامه در آن بایگانی شده است</summary>
    public string? ArchiveFolderTitle { get; set; }

    /// <summary>شناسه گره بایگانی (برای خروج از بایگانی / جابجایی)</summary>
    public int? BayeganiId { get; set; }

    // ==================== چاپ ====================

    /// <summary>آیا نامه رونوشت دارد؟ (برای فعال‌بودن نسخهٔ چاپ «با رونوشت»)</summary>
    public bool HasCopyTo { get; set; }

    /// <summary>متن رونوشت — برای پیش‌نمایش در دبیرخانه</summary>
    public string? CopyTo { get; set; }

    /// <summary>رونوشت‌گیرندگان از جدول مستقل — منبع اصلی برای فرم و چاپ</summary>
    public List<OutgoingLetterCopyToDto> CopyTos { get; set; } = new();
}

/// <summary>ثبت دبیرخانه: شماره ثبت مقصد + روش ارسال + توضیح (+ ارسال با پست الکترونیک)</summary>
public class DabirkhaneRegisterDto
{
    /// <summary>شماره ثبت مقصد — شماره‌ای که دبیرخانه سازمان مقصد به نامه داده است</summary>
    public string? DestRegNumber { get; set; }

    /// <summary>روش ارسال — پست / پیک / ایمیل / فکس / تحویل حضوری / اتوماسیون</summary>
    [Required(ErrorMessage = "روش ارسال الزامی است")]
    public string SendMethod { get; set; } = "";

    public string? Note { get; set; }

    // ==================== ارسال با پست الکترونیک ====================

    /// <summary>ارسال نامه با ایمیل انجام شود؟ (روش ارسال = ایمیل یا انتخاب صریح کاربر)</summary>
    public bool SendByEmail { get; set; }

    /// <summary>آدرس ایمیل مقصد — برای روش ارسال «ایمیل» الزامی است</summary>
    public string? DestEmail { get; set; }

    /// <summary>شناسه حساب ایمیل دبیرخانه (Oto_TBL_Email با IsDabirkhane=true) — خالی = اولین حساب فعال دبیرخانه</summary>
    public int? EmailAccountId { get; set; }

    // ==================== فیلدهای وابسته به روش ارسال ====================

    /// <summary>نام تحویل گیرنده — الزامی برای پست، پست پیشتاز، پیک و تحویل حضوری</summary>
    public string? DelivererName { get; set; }

    /// <summary>کد رهگیری مرسوله پستی — پست و پست پیشتاز</summary>
    public string? TrackingCode { get; set; }

    /// <summary>شماره فکس مقصد — الزامی برای روش ارسال «فکس»</summary>
    public string? DestFax { get; set; }
}

// ============================================================
//  جستجوی پیشرفتهٔ دبیرخانه — فیلتر روی نامه‌های امضا شده
// ============================================================

/// <summary>فیلترهای جستجوی پیشرفتهٔ دبیرخانه نامه صادره</summary>
public class DabirkhaneSearchDto
{
    /// <summary>عبارت جستجو — عنوان، شماره صادره، اندیکاتور، سازمان مقصد، نام تحویل گیرنده، کد رهگیری، شماره ثبت مقصد</summary>
    public string? Text { get; set; }

    /// <summary>فقط نامه‌های ثبت‌شده در دبیرخانه / فقط در انتظار ثبت</summary>
    public bool? RegisteredOnly { get; set; }

    /// <summary>فقط نامه‌های بایگانی‌شده / بایگانی‌نشده</summary>
    public bool? ArchivedOnly { get; set; }

    /// <summary>فیلتر بر اساس روش ارسال</summary>
    public string? SendMethod { get; set; }

    /// <summary>فیلتر بر اساس ثبت‌کننده (فرستنده)</summary>
    public int? CreatorUserId { get; set; }

    /// <summary>فیلتر بر اساس شرکت صادرکننده (سربرگ)</summary>
    public int? CompanyId { get; set; }

    /// <summary>فیلتر بر اساس نام سازمان مقصد</summary>
    public string? ReceiverOrganization { get; set; }

    /// <summary>فیلتر بر اساس محرمانگی</summary>
    public string? Mahramanegi { get; set; }

    /// <summary>فیلتر بر اساس فوریت</summary>
    public string? Foriat { get; set; }

    /// <summary>فقط نامه‌های پیوست‌دار</summary>
    public bool? HasAttachment { get; set; }

    /// <summary>از تاریخ صدور (شمسی/میلادی — میلادی ارسال می‌شود)</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>تا تاریخ صدور</summary>
    public DateTime? ToDate { get; set; }

    /// <summary>آیا هیچ فیلتری اعمال شده است؟</summary>
    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(Text) ||
        RegisteredOnly != null || ArchivedOnly != null ||
        !string.IsNullOrWhiteSpace(SendMethod) ||
        CreatorUserId != null || CompanyId != null ||
        !string.IsNullOrWhiteSpace(ReceiverOrganization) ||
        !string.IsNullOrWhiteSpace(Mahramanegi) ||
        !string.IsNullOrWhiteSpace(Foriat) ||
        HasAttachment != null ||
        FromDate != null || ToDate != null;
}

// ============================================================
//  بایگانی دبیرخانه — نامه‌های صادره در درخت پوشه‌ها
// ============================================================

/// <summary>بایگانی کردن یک یا چند نامه صادره در پوشهٔ انتخابی</summary>
public class ArchiveOutgoingLettersDto
{
    /// <summary>شناسه پوشه مقصد (0 = ریشه بایگانی دبیرخانه)</summary>
    public int FolderId { get; set; }

    /// <summary>شناسه نامه‌های صادره (همان LetterSource.Id)</summary>
    public List<int> LetterIds { get; set; } = new();

    /// <summary>عنوان اختیاری — خالی باشد عنوان خود نامه استفاده می‌شود</summary>
    public string? Title { get; set; }
}

/// <summary>جابجایی نامه بایگانی‌شده به پوشه‌ای دیگر</summary>
public class MoveArchivedLetterDto
{
    /// <summary>شناسه گره بایگانی (LetterBayegani.BayeganiId)</summary>
    public int BayeganiId { get; set; }

    /// <summary>شناسه پوشه مقصد</summary>
    public int NewParentId { get; set; }
}

/// <summary>آمار دبیرخانه صادره</summary>
public class DabirkhaneStatsDto
{
    /// <summary>امضا شده و منتظر ثبت دبیرخانه</summary>
    public int Pending { get; set; }

    /// <summary>ثبت و ارسال شده</summary>
    public int Registered { get; set; }

    /// <summary>بایگانی شده در بایگانی دبیرخانه</summary>
    public int Archived { get; set; }

    public int Total => Pending + Registered;
}

/// <summary>شرکت (برای انتخاب سربرگ نامه صادره)</summary>
public class LetterCompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? LetterheadFileName { get; set; }
    public bool HasLetterhead { get; set; }
}

// ============================================================
//  رونوشت‌گیرندگان نامه صادره — جدول مستقل
//  هر ردیف یک رونوشت‌گیرنده است تا کاربر در فرم ایجاد نامه
//  آن‌ها را یکی‌یکی بیفزاید و چاپ «با رونوشت» از همین جدول بخواند.
// ============================================================

/// <summary>یک ردیف رونوشت‌گیرنده</summary>
public class OutgoingLetterCopyToDto
{
    public int Id { get; set; }
    public int OutgoingLetterId { get; set; }

    /// <summary>ترتیب نمایش (از ۱)</summary>
    public int RowNo { get; set; }

    /// <summary>نام رونوشت‌گیرنده — سازمان یا شخص</summary>
    public string Name { get; set; } = "";

    /// <summary>توضیح/سمت (اختیاری)</summary>
    public string? Desc { get; set; }

    /// <summary>شماره/کد داخلی مقصد (اختیاری)</summary>
    public string? RefNo { get; set; }
}

/// <summary>ذخیره یک ردیف رونوشت — Id=0 یعنی ایجاد جدید</summary>
public class SaveOutgoingLetterCopyToDto
{
    /// <summary>0 = جدید، غیرصفر = ویرایش همان ردیف</summary>
    public int Id { get; set; }

    [Required(ErrorMessage = "نام رونوشت‌گیرنده الزامی است")]
    [MaxLength(300, ErrorMessage = "نام رونوشت‌گیرنده حداکثر ۳۰۰ کاراکتر است")]
    public string Name { get; set; } = "";

    public string? Desc { get; set; }

    public string? RefNo { get; set; }
}

/// <summary>جایگزینی کامل فهرست رونوشت‌های یک نامه (ذخیرهٔ دسته‌ای از فرم)</summary>
public class ReplaceOutgoingLetterCopyTosDto
{
    public List<SaveOutgoingLetterCopyToDto> Items { get; set; } = new();
}

/// <summary>
/// نتیجهٔ «تشخیص چاپ» نامهٔ صادره — فقط خواندن، بدون تولید PDF.
/// به این پرسش پاسخ می‌دهد: «کدام شرکت انتخاب شد، کدام فایل/قالب خوانده شد،
/// و اگر طرح پوشهٔ Reports/OutgoingLetters/Companies خوانده نمی‌شود چرا؟»
/// </summary>
public class OutgoingLetterPrintDiagnosticsDto
{
    public int LetterId { get; set; }
    public string? LetterNumber { get; set; }
    public string? Title { get; set; }

    /// <summary>ستون CompanyId خودِ نامه؛ خالی بودنش ریشهٔ چاپ روی طرح شرکت دیگر است.</summary>
    public int? LetterCompanyId { get; set; }

    /// <summary>شرکت فعال همین درخواست (هدر X-Company-Id).</summary>
    public int? RequestActiveCompanyId { get; set; }

    public int? EffectiveCompanyId { get; set; }

    /// <summary>Letter | OnlyActiveCompany | RequestActiveCompany | None</summary>
    public string CompanySource { get; set; } = "None";

    public string? CompanyName { get; set; }
    public bool CompanyRecordMissing { get; set; }
    public string? LetterheadFileName { get; set; }

    /// <summary>شناسهٔ پوشهٔ طرح (پس از اعمال CompanyTemplateMap).</summary>
    public int? TemplateDesignId { get; set; }
    public bool CompanyTemplateMapApplied { get; set; }

    public string? TemplatePath { get; set; }
    public int? TemplateImageBytes { get; set; }
    public string? TemplateError { get; set; }
    public string? RegisteredLetterheadPath { get; set; }

    public bool PreferTemplateLetterhead { get; set; }

    /// <summary>Template | Registered | None — منبعی که چاپ واقعاً استفاده می‌کند.</summary>
    public string LetterheadSource { get; set; } = "None";

    public string TemplateRoot { get; set; } = "";
    public List<string> SearchRoots { get; set; } = new();

    /// <summary>پوشه‌های طرحی که روی سرور موجودند، با نام فایل‌هایشان.</summary>
    public string AvailableDesigns { get; set; } = "";

    /// <summary>توضیح فارسی علت — قابل نمایش مستقیم به کاربر یا پشتیبان.</summary>
    public string Hint { get; set; } = "";
}

