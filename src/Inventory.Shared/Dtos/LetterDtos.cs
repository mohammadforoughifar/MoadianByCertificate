using System.ComponentModel.DataAnnotations;

namespace Inventory.Shared.Dtos;

// ============================================================
//  ماژول اتوماسیون اداری — فاز اول: کارتابل نامه داخلی
//  DTO ها بر اساس ویومدل‌های کارفرما (VMInnerLetrer / VMErja / VMPishNavisName)
//  با تطبیق کاربر به int UserId (ساختار فعلی پروژه)
// ============================================================

public class BatchBayeganiRequest
{
    public List<int> ErjaIds { get; set; } = new();
    public string? Description { get; set; }
}

/// <summary>گیرنده‌ی نامه/ارجاع — معادل SematUserFullName (فعلاً بر پایه کاربر)</summary>
public class LetterReciverDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";

    /// <summary>سمت — بعد از چارت سازمانی فعال می‌شود</summary>
    public int? SematId { get; set; }
    public string? SematTitle { get; set; }

    /// <summary>
    /// همهٔ سمت‌های فعالِ کاربر (هر کاربر می‌تواند چند سمت داشته باشد).
    /// در کمبو باکسِ انتخابِ امضاکننده نمایش داده می‌شود و جستجو با آن هم کار می‌کند.
    /// </summary>
    public List<string>? Positions { get; set; }

    /// <summary>شناسه و عنوانِ هر سمت فعال برای انتخاب سمتِ مشخصِ امضاکننده.</summary>
    public List<LetterPositionDto>? PositionOptions { get; set; }
}

public sealed class LetterPositionDto
{
    public int SematId { get; set; }
    public string Title { get; set; } = "";
}

/// <summary>ثبت/ویرایش نامه داخلی — معادل VMInnerLetrer.AddInner</summary>
public class AddInnerLetterDto
{
    public int LetterId { get; set; }

    [Required(ErrorMessage = "عنوان نامه الزامی است")]
    public string Title { get; set; } = "";
    public string? Text { get; set; }

    /// <summary>عادی / محرمانه / سری</summary>
    public string Mahramanegi { get; set; } = "عادی";

    /// <summary>عادی / فوری / آنی</summary>
    public string Foriat { get; set; } = "عادی";

    /// <summary>گیرندگان اصلی (حداقل یک نفر)</summary>
    public List<int> ReciversGirande { get; set; } = new();

    /// <summary>گیرندگان ارجاع (جهت اقدام)</summary>
    public List<int> ReciversErja { get; set; } = new();

    /// <summary>گیرندگان هامش (رونوشت / جهت اطلاع)</summary>
    public List<int> ReciversHamesh { get; set; } = new();

    /// <summary>نامه‌های مرتبط: عطف (2) / پیرو (1)</summary>
    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();

    /// <summary>شناسه پیش‌نویس مبدأ — بعد از ارسال حذف می‌شود (اختیاری)</summary>
    public int? FromPishnevisId { get; set; }

    // ---------- انتخاب گروهی (معادل Reciver_Groups طرح کارفرما) ----------
    /// <summary>گروه‌های گیرنده اصلی — سرور به اعضا باز می‌کند</summary>
    public List<int> GroupsGirande { get; set; } = new();

    /// <summary>گروه‌های ارجاع</summary>
    public List<int> GroupsErja { get; set; } = new();

    /// <summary>گروه‌های هامش</summary>
    public List<int> GroupsHamesh { get; set; } = new();
}

/// <summary>نامه مرتبط (عطف/پیرو)</summary>
public class RelatedLetterDto
{
    public int Id { get; set; }

    /// <summary>1=پیرو، 2=عطف</summary>
    public int Related { get; set; }
    public int RelateLetterId { get; set; }
    public string? RelateLetterNumber { get; set; }
    public string? RelateLetterTitle { get; set; }

    /// <summary>
    /// دفتر نامهٔ مرتبط: 1=داخلی، 2=صادره، 3=وارده (0=نامشخص).
    /// ریدر با همین مقدار برچسب «وارده/داخلی/صادره» را کنار رابطه نشان می‌دهد.
    /// </summary>
    public int RelateSourceType { get; set; }

    public string RelatedTitle => Related == 2 ? "عطف" : "پیرو";
    public string RelateSourceTitle => RelateSourceType switch
    {
        1 => "داخلی",
        2 => "صادره",
        3 => "وارده",
        _ => ""
    };
}

/// <summary>سطر لیست کارتابل — معادل viewInnerLetterList</summary>
public class InnerLetterListItemDto
{
    public int LetterId { get; set; }

    /// <summary>شناسه ارجاع مربوط به کاربر جاری (در کارتابل وارده)</summary>
    public int? ErjaId { get; set; }

    public string LetterNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Sender { get; set; } = "";
    public int SenderUserId { get; set; }
    public string Reciver { get; set; } = "";
    public DateTime Date { get; set; }
    public string Mahramanegi { get; set; } = "عادی";
    public string Foriat { get; set; } = "عادی";

    /// <summary>نوع دریافت: گیرنده / ارجاع / هامش</summary>
    public string? ErjaType { get; set; }

    /// <summary>متن دستور ارجاع (پاراف)</summary>
    public string? MatnErja { get; set; }

    public DateTime? MohlatPasokh { get; set; }
    public bool IsNeshan { get; set; }
    public bool HasAttachment { get; set; }
    public bool IsRead { get; set; }

    /// <summary>تعداد کل گیرندگان نامه</summary>
    public int ReciverCount { get; set; }

    /// <summary>وضعیت تایید: 0=بدون اقدام، 1=تایید، 2=رد</summary>
    public int TypeTaeed { get; set; }
    public bool HasAnswer { get; set; }
}

/// <summary>جزئیات کامل نامه — معادل VMInnerLetrer.InnerLetrer</summary>
public class InnerLetterDetailDto
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

    public List<LetterReciverDto> ReciversGirande { get; set; } = new();
    public List<LetterReciverDto> ReciversErja { get; set; } = new();
    public List<LetterReciverDto> ReciversHamesh { get; set; } = new();
    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();

    /// <summary>ارجاع مربوط به کاربر جاری (اگر گیرنده باشد)</summary>
    public ErjaDto? MyErja { get; set; }

    /// <summary>آیا کاربر جاری فرستنده‌ی نامه است؟</summary>
    public bool IsMine { get; set; }

    /// <summary>آیا نامه قابل ویرایش است؟ (فرستنده است و هنوز هیچ گیرنده‌ای نخوانده)</summary>
    public bool CanEdit { get; set; }
}

/// <summary>ارجاع — معادل VMErja</summary>
public class ErjaDto
{
    public int ErjaId { get; set; }
    public int SourceId { get; set; }
    public int SenderUserId { get; set; }
    public int ReciverUserId { get; set; }
    public string SenderName { get; set; } = "";
    public string ReciverName { get; set; } = "";
    public DateTime Date { get; set; }

    /// <summary>گیرنده / ارجاع / هامش</summary>
    public string Type { get; set; } = "گیرنده";

    /// <summary>0=بدون اقدام، 1=تایید، 2=رد</summary>
    public int TypeTaeed { get; set; }
    public string Answer { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime? MohlatPasokh { get; set; }

    // نام‌های مستعار — رابط گردش نامه از این نام‌ها استفاده می‌کند
    public string ReceiverName { get => ReciverName; set => ReciverName = value; }
    public DateTime DateErja { get => Date; set => Date = value; }
    public string MatnPasokh { get => Answer; set => Answer = value; }
    public DateTime? DatePasokh { get => DateAnswer; set => DateAnswer = value; }
    public string MatnErja { get; set; } = "";
    public int AmalgarId { get; set; }
    public string? AmalgarTitle { get; set; }
    public bool IsNeshan { get; set; }
    public bool ShowForAll { get; set; }
    public DateTime? DateRead { get; set; }
    public DateTime? DateEmza { get; set; }
    public DateTime? DateAnswer { get; set; }
    public int? ParentErjaId { get; set; }
}

/// <summary>ثبت ارجاع جدید — معادل AddErja</summary>
public class AddErjaDto
{
    /// <summary>شناسه نامه (LetterSource)</summary>
    public int LetterId { get; set; }

    /// <summary>ارجاع والد — اگر از داخل کارتابل ارجاع می‌شود</summary>
    public int? ParentErjaId { get; set; }

    /// <summary>متن/دستور ارجاع (پاراف)</summary>
    public string TextErja { get; set; } = "";

    public int AmalgarId { get; set; } = 1;
    public DateTime? DeadlineAnswer { get; set; }

    /// <summary>ارسال مجدد برای گیرنده‌ای که قبلاً در گردش نامه است، فقط با تأیید صریح کاربر.</summary>
    public bool AllowDuplicateRecipients { get; set; }

    /// <summary>گیرندگان (انفرادی) — اعتبارسنجی «حداقل یک گیرنده» بعد از باز شدن گروه‌ها در سرور انجام می‌شود</summary>
    public List<int> ReciversGirandegan { get; set; } = new();

    /// <summary>گیرندگان هامش (جهت اطلاع)</summary>
    public List<int> ReciversHamesh { get; set; } = new();

    /// <summary>گروه‌های گیرنده ارجاع (معادل Reciver_GroupsErja) — سرور به اعضا باز می‌کند</summary>
    public List<int> GroupsGirandegan { get; set; } = new();

    /// <summary>گروه‌های هامش (معادل Reciver_GroupsHamesh)</summary>
    public List<int> GroupsHamesh { get; set; } = new();

    // نام‌های مستعار — رابط ارجاع نامه وارده از این نام‌ها استفاده می‌کند
    public List<int> ReceiverUserIds { get => ReciversGirandegan; set => ReciversGirandegan = value ?? new List<int>(); }
    public string MatnErja { get => TextErja; set => TextErja = value ?? ""; }
    public DateTime? MohlatPasokh { get => DeadlineAnswer; set => DeadlineAnswer = value; }
}

/// <summary>پاسخ به ارجاع — معادل AnswerRequest</summary>
public class AnswerErjaDto
{
    public string Answer { get; set; } = "";

    /// <summary>نام مستعارِ Answer — متن پاسخ</summary>
    public string MatnPasokh { get => Answer; set => Answer = value ?? ""; }
    public bool ShowForAll { get; set; }

    /// <summary>0=بدون اقدام، 1=تایید، 2=رد</summary>
    public int TypeTaeed { get; set; }
}

/// <summary>گره‌ی درخت گردش ارجاع — معادل TreeNodeLetter</summary>
public class ErjaTreeNodeDto
{
    public int ErjaId { get; set; }
    public int SenderUserId { get; set; }
    public int ReciverUserId { get; set; }
    public string Sender { get; set; } = "";
    public string Reciver { get; set; } = "";
    public string Type { get; set; } = "";
    public string MatnErja { get; set; } = "";
    public string? AmalgarTitle { get; set; }
    public int TypeTaeed { get; set; }
    public string Answer { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime Date { get; set; }
    public DateTime? DateRead { get; set; }
    public DateTime? DateAnswer { get; set; }
    public DateTime? MohlatPasokh { get; set; }
    public bool ShowForAll { get; set; }
    public List<ErjaTreeNodeDto> Children { get; set; } = new();

    // نام‌های مستعار — رابط گردش از این نام‌ها استفاده می‌کند
    public string SenderName { get => Sender; set => Sender = value; }
    public string ReceiverName { get => Reciver; set => Reciver = value; }
    public DateTime DateErja { get => Date; set => Date = value; }

    /// <summary>متن پاسخ — همان Answer</summary>
    public string MatnPasokh { get => Answer; set => Answer = value; }

    /// <summary>زمان ثبت پاسخ — همان DateAnswer</summary>
    public DateTime? DatePasokh { get => DateAnswer; set => DateAnswer = value; }
}

/// <summary>پیش‌نویس — معادل VMPishNavisName</summary>
public class PishnevisDto
{
    public int PishnevisId { get; set; }

    [Required(ErrorMessage = "عنوان الزامی است")]
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsNeshan { get; set; }
}

/// <summary>عملگر ارجاع — معادل VMAmalgar</summary>
public class AmalgarDto
{
    public int AmalgarId { get; set; }
    public string Title { get; set; } = "";
    public string TaeedEmza { get; set; } = "";
}

/// <summary>افزودن عملگر ارجاع تازه از فرم ارجاع</summary>
public class CreateAmalgarDto
{
    public string Title { get; set; } = "";

    /// <summary>خالی = بدون نیاز به تایید/امضا</summary>
    public string? TaeedEmza { get; set; }
}

/// <summary>آمار کارتابل (شمارنده‌های منو)</summary>
public class LetterCartableStatsDto
{
    public int InboxUnread { get; set; }
    public int InboxTotal { get; set; }
    public int SentTotal { get; set; }
    public int PishnevisTotal { get; set; }

    /// <summary>ارجاع‌های دارای مهلت پاسخ نزدیک/گذشته</summary>
    public int DeadlineSoon { get; set; }
}

/// <summary>آیتم انتخاب نامه برای عطف/پیرو</summary>
public class LetterPickDto
{
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Date { get; set; }

    /// <summary>true = نامه ارسالیِ کاربر جاری، false = دریافتی (برای نشان دریافتی/ارسالی در انتخاب عطف/پیرو)</summary>
    public bool IsSent { get; set; }
}

// ==================== گروه‌های گیرندگان (پورت جدول Groups کارفرما) ====================

/// <summary>گروه گیرندگان — برای انتخاب گروهی در فرم ارسال و ارجاع</summary>
public class LetterGroupDto
{
    public int GroupId { get; set; }
    public string NameGroup { get; set; } = "";
    public int MemberCount { get; set; }
    public bool Condition { get; set; } = true;
    public List<LetterReciverDto> Members { get; set; } = new();
}

/// <summary>ایجاد/ویرایش گروه گیرندگان</summary>
public class SaveLetterGroupDto
{
    public int GroupId { get; set; }

    [Required(ErrorMessage = "نام گروه الزامی است")]
    public string NameGroup { get; set; } = "";

    [MinLength(1, ErrorMessage = "حداقل یک عضو برای گروه انتخاب کنید")]
    public List<int> MemberUserIds { get; set; } = new();
}

// ==================== ویرایش نامه ====================

/// <summary>ویرایش نامه — فقط قبل از خوانده‌شدن توسط هر گیرنده مجاز است</summary>
public class EditInnerLetterDto
{
    [Required(ErrorMessage = "عنوان نامه الزامی است")]
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    public string Mahramanegi { get; set; } = "عادی";
    public string Foriat { get; set; } = "عادی";

    // ---------- گیرندگان (همانند فرم ایجاد — فقط تا قبل از خوانده‌شدن قابل تغییر) ----------
    public List<int> ReciversGirande { get; set; } = new();
    public List<int> GroupsGirande { get; set; } = new();
    public List<int> ReciversErja { get; set; } = new();
    public List<int> GroupsErja { get; set; } = new();
    public List<int> ReciversHamesh { get; set; } = new();
    public List<int> GroupsHamesh { get; set; } = new();

    /// <summary>نامه‌های مرتبط (عطف/پیرو) — جایگزین فهرست قبلی می‌شود</summary>
    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();
}

// ==================== پیوست ====================

/// <summary>پیوست نامه (متادیتا — دانلود جداگانه)</summary>
public class LetterAttachmentDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string UploaderName { get; set; } = "";
    public int UploaderUserId { get; set; }
    public DateTime UploadedAt { get; set; }

    // نام‌های مستعار — رابط نمایش نامه از این نام‌ها استفاده می‌کند
    public int AttachmentId { get => Id; set => Id = value; }
    public long FileSize { get => Size; set => Size = value; }
    public DateTime UploadDate { get => UploadedAt; set => UploadedAt = value; }
}

// ==================== بایگانی درختی ====================

/// <summary>گره درخت بایگانی — پوشه یا نامه بایگانی‌شده (معادل TreeNodeBayegani طرح کارفرما)</summary>
public class BayeganiNodeDto
{
    public int BayeganiId { get; set; }
    public string Title { get; set; } = "";
    public int ParentId { get; set; }

    /// <summary>نوع بایگانی — ۱ = بایگانی شخصی</summary>
    public int TypeBayegani { get; set; } = 1;
    public bool IsFolder { get; set; }

    /// <summary>شناسه ارجاع (فقط برای برگ‌های نامه)</summary>
    public int? ErjaId { get; set; }

    // اطلاعات نامه (فقط برای برگ‌ها)
    public int? LetterId { get; set; }
    public string? LetterNumber { get; set; }
    public string? Sender { get; set; }
    public DateTime? Date { get; set; }
    public string? Foriat { get; set; }
    public string? Mahramanegi { get; set; }
    public bool HasAttachment { get; set; }

    // ==================== ویژهٔ بایگانی دبیرخانه (نامه صادره) ====================

    /// <summary>1 = نامه داخلی، 2 = نامه صادره (بایگانی دبیرخانه)</summary>
    public int SourceType { get; set; } = 1;

    /// <summary>سازمان/شرکت مقصد نامه صادره</summary>
    public string? ReceiverOrganization { get; set; }

    /// <summary>شماره صادره رسمی (بعد از امضا)</summary>
    public string? SadereNumber { get; set; }

    /// <summary>روش ارسال ثبت‌شده توسط دبیرخانه</summary>
    public string? SendMethod { get; set; }

    public List<BayeganiNodeDto> Children { get; set; } = new();
}

/// <summary>ایجاد/ویرایش پوشه بایگانی</summary>
public class SaveBayeganiFolderDto
{
    public string Title { get; set; } = "";

    /// <summary>0 = ریشه (دسته اصلی)</summary>
    public int ParentId { get; set; }

    /// <summary>۱ = بایگانی شخصی (پیش‌فرض)</summary>
    public int TypeBayegani { get; set; } = 1;
}

/// <summary>بایگانی یک یا چند نامه در پوشه انتخابی (معادل BayeganiRequest.ErjaIds)</summary>
public class ArchiveLettersDto
{
    public int FolderId { get; set; }
    public List<int> ErjaIds { get; set; } = new();

    /// <summary>شناسه نامه‌های ارسالی (فرستنده — بدون ارجاع)</summary>
    public List<int> LetterIds { get; set; } = new();
    /// <summary>شناسه نامه‌های صادره برای بایگانی شخصی (TypeBayegani=1)</summary>
    public List<int> OutgoingLetterIds { get; set; } = new();

    /// <summary>عنوان اختیاری — خالی باشد عنوان نامه استفاده می‌شود</summary>
    public string? Title { get; set; }
}
