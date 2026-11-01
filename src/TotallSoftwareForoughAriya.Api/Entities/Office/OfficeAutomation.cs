using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

// ============================================================
//  ماژول اتوماسیون اداری — فاز اول: کارتابل نامه داخلی
//  (منطق بر اساس سرویس‌های ارسالی کارفرما — تطبیق با زیرساخت این پروژه:
//   کاربر = User با int Id ، پیوست = AppAttachment ، دسترسی = RBAC)
//  ستون‌های SematId برای فاز «چارت سازمانی» به‌صورت nullable آماده‌اند.
// ============================================================

/// <summary>
/// کلید مرجع مشترک همه‌ی انواع نامه (SourceKeyID در طرح اصلی) —
/// ارجاع‌ها و نامه‌های مرتبط به این کلید وصل می‌شوند تا در آینده
/// نامه صادره/وارده هم بدون تغییر ساختار اضافه شوند.
/// SourceType: 1=نامه داخلی، 2=نامه صادره، 3=نامه وارده
/// </summary>
public class LetterSource
{
    public int Id { get; set; }

    /// <summary>1=نامه داخلی، 2=صادره، 3=وارده</summary>
    public int SourceType { get; set; } = 1;

    public bool IsDelete { get; set; }

    public InnerLetter? InnerLetter { get; set; }
    public OutgoingLetter? OutgoingLetter { get; set; }
    public IncomingLetter? IncomingLetter { get; set; }
    public ICollection<Erja> Erjas { get; set; } = new List<Erja>();
    public ICollection<OutgoingLetterSigner> OutgoingSigners { get; set; } = new List<OutgoingLetterSigner>();

    /// <summary>روابطی که این نامه به‌عنوان نامه اصلی دارد (عطف/پیرو)</summary>
    public ICollection<RelatedLetter> RelatedLetters { get; set; } = new List<RelatedLetter>();

    /// <summary>روابطی که این نامه به‌عنوان نامه مرتبط در آن‌هاست</summary>
    public ICollection<RelatedLetter> RelatedToLetters { get; set; } = new List<RelatedLetter>();
}

/// <summary>نامه داخلی — شناسه = همان شناسه LetterSource (کلید مشترک)</summary>
public class InnerLetter
{
    /// <summary>کلید مشترک با LetterSource — دستی مقداردهی می‌شود</summary>
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    /// <summary>شماره اندیکاتور کامل نامه — مثل «1404/12»</summary>
    [MaxLength(60)] public string? LetterNumber { get; set; }

    /// <summary>شماره ترتیبی داخل سال شمسی (هر سال از ۱ شروع می‌شود)</summary>
    public int Number { get; set; }

    /// <summary>کاربر ثبت‌کننده (فرستنده)</summary>
    public int CreatorUserId { get; set; }

    /// <summary>سمت ثبت‌کننده — بعد از پیاده‌سازی چارت سازمانی فعال می‌شود</summary>
    public int? CreatorSematId { get; set; }

    [MaxLength(300)] public string Title { get; set; } = "";
    public string? Text { get; set; }

    public DateTime DateSabt { get; set; } = DateTime.Now;

    /// <summary>محرمانگی: عادی / محرمانه / سری</summary>
    [MaxLength(20)] public string Mahramanegi { get; set; } = "عادی";

    /// <summary>فوریت: عادی / فوری / آنی</summary>
    [MaxLength(20)] public string Foriat { get; set; } = "عادی";

    /// <summary>نشان‌کردن نامه توسط فرستنده (ستاره سمت ارسالی) —
    /// نشان گیرندگان روی Erja.IsNeshan است</summary>
    public bool IsNeshan { get; set; }

    public bool IsDelete { get; set; }

    [ForeignKey(nameof(Id))] public LetterSource Source { get; set; } = null!;
    public User? Creator { get; set; }
}

/// <summary>
/// ارجاع/گردش نامه (Erja) — هر گیرنده‌ی نامه یک رکورد ارجاع دارد.
/// Type: «گیرنده» (اصلی) / «ارجاع» (جهت اقدام) / «هامش» (رونوشت/جهت اطلاع)
/// </summary>
public class Erja
{
    public int ErjaId { get; set; }

    /// <summary>کلید نامه (LetterSource)</summary>
    public int SourceId { get; set; }

    public int SenderUserId { get; set; }
    public int ReciverUserId { get; set; }

    /// <summary>سمت فرستنده/گیرنده — فاز چارت سازمانی</summary>
    public int? SenderSematId { get; set; }
    public int? ReciverSematId { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;

    /// <summary>گیرنده / ارجاع / هامش</summary>
    [MaxLength(20)] public string Type { get; set; } = "گیرنده";

    /// <summary>وضعیت تایید: 0=بدون اقدام، 1=تایید، 2=رد</summary>
    public int TypeTaeed { get; set; }

    /// <summary>پاسخ گیرنده به ارجاع</summary>
    public string Answer { get; set; } = "";

    public bool IsRead { get; set; }
    public bool? IsBayegani { get; set; } = false;

    /// <summary>مهلت پاسخ (اختیاری)</summary>
    public DateTime? MohlatPasokh { get; set; }

    /// <summary>متن/دستور ارجاع (پاراف)</summary>
    public string MatnErja { get; set; } = "";

    /// <summary>عملگر ارجاع (جهت اطلاع/اقدام/...)</summary>
    public int AmalgarId { get; set; } = 1;

    /// <summary>نشان‌شده توسط گیرنده (ستاره)</summary>
    public bool IsNeshan { get; set; }

    /// <summary>نمایش پاسخ برای همه گیرندگان نامه</summary>
    public bool ShowForAll { get; set; }

    public bool ShowMassage { get; set; } = true;
    public DateTime? DateRead { get; set; }
    public DateTime? DateEmza { get; set; }
    public DateTime? DateAnswer { get; set; }
    public bool IsReadAnswer { get; set; }
    public bool ShowMassageAnswer { get; set; }
    public bool IsDelete { get; set; }

    /// <summary>ارجاع والد (برای درخت گردش) — null یعنی ارجاع اولیه از خود نامه</summary>
    public int? ParentErjaId { get; set; }

    [ForeignKey(nameof(SourceId))] public LetterSource Source { get; set; } = null!;
    [ForeignKey(nameof(AmalgarId))] public Amalgar? Amalgar { get; set; }
    public User? UserSender { get; set; }
    public User? UserReciver { get; set; }
}

/// <summary>عملگر ارجاع — «جهت اطلاع»، «جهت اقدام» و…</summary>
public class Amalgar
{
    public int AmalgarId { get; set; }
    [MaxLength(100)] public string Title { get; set; } = "";

    /// <summary>اگر مقدار داشته باشد، این عملگر نیاز به تایید/امضا دارد</summary>
    [MaxLength(30)] public string TaeedEmza { get; set; } = "";

    public bool IsDelete { get; set; }

    public ICollection<Erja> Erjas { get; set; } = new List<Erja>();
}

/// <summary>پیش‌نویس نامه داخلی</summary>
public class PishnevisLetter
{
    public int PishnevisId { get; set; }
    [MaxLength(300)] public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public int UserId { get; set; }

    /// <summary>سمت — فاز چارت سازمانی</summary>
    public int? SematId { get; set; }

    public bool IsNeshan { get; set; }
    public bool IsDelete { get; set; }

    public User? User { get; set; }
}

/// <summary>نامه‌های مرتبط — عطف (2) و پیرو (1)</summary>
public class RelatedLetter
{
    public int Id { get; set; }

    /// <summary>1=پیرو، 2=عطف</summary>
    public int Related { get; set; }

    /// <summary>نامه اصلی</summary>
    public int LetterId { get; set; }

    /// <summary>نامه مرتبط</summary>
    public int RelateLetterId { get; set; }

    public int UserId { get; set; }
    public int? SematId { get; set; }
    public bool IsDelete { get; set; }

    [ForeignKey(nameof(LetterId))] public LetterSource Letter { get; set; } = null!;
    [ForeignKey(nameof(RelateLetterId))] public LetterSource RelateLetter { get; set; } = null!;
}

/// <summary>
/// بایگانی درختی نامه‌ها — ساختار جدول از الان آماده است؛
/// سرویس و UI آن در فاز بعد اضافه می‌شود.
/// TypeBayegani: 0=نامه داخلی
/// </summary>
public class LetterBayegani
{
    public int BayeganiId { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    public int? ErjaId { get; set; }

    /// <summary>شناسه نامه — برای بایگانی نامه ارسالی توسط فرستنده (که ارجاعی ندارد)</summary>
    public int? LetterId { get; set; }
    public int ParentId { get; set; }
    public int UserId { get; set; }
    public int? SematId { get; set; }
    public int TypeBayegani { get; set; }
    public bool IsFolder { get; set; }
    public bool IsDelete { get; set; }
}

/// <summary>
/// گروه گیرندگان نامه — پورت از جدول Groups طرح کارفرما (نسخه کاربرمحور؛
/// SematGroups در فاز چارت سازمانی به عضویت سمت‌محور ارتقا می‌یابد)
/// </summary>
public class LetterGroup
{
    public int GroupId { get; set; }
    [MaxLength(150)] public string NameGroup { get; set; } = "";

    /// <summary>فعال بودن گروه (معادل Condition طرح اصلی)</summary>
    public bool Condition { get; set; } = true;
    public bool IsDelete { get; set; }
    public int CreatorUserId { get; set; }

    /// <summary>سمت سازنده — فاز چارت سازمانی</summary>
    public int? CreatorSematId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public User? Creator { get; set; }
    public ICollection<LetterGroupMember> Members { get; set; } = new List<LetterGroupMember>();
}

/// <summary>عضو گروه گیرندگان (معادل SematGroups — فعلاً کاربرمحور)</summary>
public class LetterGroupMember
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int UserId { get; set; }

    /// <summary>سمت — فاز چارت سازمانی</summary>
    public int? SematId { get; set; }

    public LetterGroup? Group { get; set; }
    public User? User { get; set; }
}

/// <summary>
/// ساختار شماره اندیکاتور نامه — پورت از LetterStrature طرح کارفرما.
/// هر ردیف یک جزء ساختار است و ترتیب ردیف‌ها، ترتیب اجزا را تعیین می‌کند.
/// TypeForm: 1=نامه داخلی، 2=صادره، 3=وارده
/// TypeStrature: «واحد» | «شماره» | «سال»
/// مثال خروجی با ترتیب واحد/شماره/سال: MQ/1/1405
/// </summary>
public class LetterStrature
{
    public int StratureId { get; set; }
    public int TypeForm { get; set; }
    [MaxLength(50)] public string TypeStrature { get; set; } = "";
}

