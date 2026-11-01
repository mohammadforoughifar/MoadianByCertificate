using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// ============================================================================
//  ماژول آرشیو اسناد و مدارک (DocArchive)
//  ─ پوشه‌بندی درختی مثل بایگانی
//  ─ دسترسی نفر به نفر روی پوشه و مدرک (مشاهده/خواندن/نوشتن/کامل + دانلود)
//  ─ مدرک با «کد یکتا»، تاریخ انقضا، گردش تایید چندنفره
//  ─ ورژن‌گذاری کامل + پیوست روی هر ورژن
//  ─ لینک مدارک به هم و ایجاد کار در کارتابل هنگام ورژن خوردن
// ============================================================================

/// <summary>سطح دسترسی به پوشه/مدرک — عدد بزرگ‌تر شامل سطوح پایین‌تر است.</summary>
public enum DocAccessLevel
{
    None = 0,
    /// <summary>مشاهده — فقط دیدن عنوان/کد در لیست (بدون باز کردن محتوا)</summary>
    View = 1,
    /// <summary>خواندن — باز کردن مدرک، دیدن ورژن‌ها و پیوست‌ها</summary>
    Read = 2,
    /// <summary>نوشتن — ثبت/ویرایش مدرک و ورژن جدید</summary>
    Write = 3,
    /// <summary>کامل — همه‌چیز + مدیریت دسترسی، حذف، دریافت کار در کارتابل</summary>
    Full = 4
}

/// <summary>وضعیت ورژن مدرک</summary>
public enum DocVersionStatus
{
    /// <summary>پیش‌نویس — هنوز به گردش نرفته</summary>
    Draft = 0,
    /// <summary>در گردش تایید — فریز شده و در کارتابل تاییدکنندگان</summary>
    InReview = 1,
    /// <summary>تایید شده</summary>
    Approved = 2,
    /// <summary>رد شده</summary>
    Rejected = 3,
    /// <summary>باطل/آرشیو شده</summary>
    Archived = 4
}

/// <summary>پوشه آرشیو اسناد — درختی و نامحدود.</summary>
public class DocFolder
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>نمایش برای همه — همه کاربران حداقل سطح خواندن دارند.</summary>
    public bool IsPublic { get; set; }

    /// <summary>در حالت نمایش عمومی، دانلود هم آزاد باشد؟</summary>
    public bool PublicCanDownload { get; set; }

    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsActive { get; set; } = true;
}

/// <summary>دسترسی روی یک پوشه (به زیرپوشه‌ها و مدارک داخل آن ارث می‌رسد).</summary>
public class DocFolderPermission
{
    public int Id { get; set; }
    public int FolderId { get; set; }

    /// <summary>شناسه کاربر — مقدار 0 یعنی این سطر «دسترسی گروهی (نقش)» است و RoleId تعیین‌کننده است.</summary>
    public int UserId { get; set; }

    /// <summary>شناسه نقش RBAC برای دسترسی گروهی — مقدار 0 یعنی دسترسی فردی (UserId).</summary>
    public int RoleId { get; set; }

    public DocAccessLevel Level { get; set; } = DocAccessLevel.Read;

    /// <summary>اجازه دانلود فایل پیوست</summary>
    public bool CanDownload { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>مدرک (سند) — کد یکتا، عنوان اجباری، تاریخ انقضا، سیاست ورژن.</summary>
public class ArchiveDocument
{
    public int Id { get; set; }

    public int FolderId { get; set; }

    [MaxLength(250)]
    public string Title { get; set; } = "";

    /// <summary>کد مدرک — اجباری و یکتا در کل سیستم</summary>
    [MaxLength(80)]
    public string Code { get; set; } = "";

    /// <summary>شماره مدرک نزد مشتری/کارفرما (اختیاری — یکتا نیست)</summary>
    [MaxLength(80)]
    public string? CustomerCode { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>تاریخ انقضای مدرک (اختیاری)</summary>
    public DateTime? ExpireDate { get; set; }

    /// <summary>
    /// اگر true: چند ورژن می‌توانند هم‌زمان فعال باشند.
    /// اگر false: فقط یک ورژن فعال است و با فعال شدن ورژن جدید، قبلی‌ها غیرفعال می‌شوند.
    /// </summary>
    public bool AllowMultipleActiveVersions { get; set; }

    /// <summary>نمایش برای همه — همه کاربران سطح خواندن دارند.</summary>
    public bool IsPublic { get; set; }
    public bool PublicCanDownload { get; set; }

    /// <summary>
    /// محرمانه — اگر فعال باشد، مشاهده/دانلود هر فایل پیوست این مدرک
    /// مستلزم تایید مجدد رمز عبور کاربر است (اعطای موقت کوتاه‌مدت).
    /// مدیر مدرک (دسترسی کامل) می‌تواند این گزینه را فعال/غیرفعال کند.
    /// </summary>
    public bool RequireDownloadConfirm { get; set; }

    /// <summary>
    /// واترمارک پیش‌نمایش — اگر فعال باشد، روی پیش‌نمایش داخل برنامه‌ی فایل‌های این مدرک
    /// (PDF/تصویر/متن) نام کاربر بازدیدکننده و زمان مشاهده به‌صورت واترمارک نمایش داده می‌شود.
    /// per-document توسط مدیر مدرک تعیین می‌شود.
    /// </summary>
    public bool WatermarkPreview { get; set; }

    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// وضعیت مدرک: فعال / غیرفعال.
    /// مدرک غیرفعال فقط-خواندنی است — ورژن جدید، ویرایش، لینک و گردش روی آن ممنوع است
    /// و در زیرمنوی «مدارک غیرفعال» نمایش داده می‌شود.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>غیرفعال‌سازی توسط چه کسی و چه زمانی</summary>
    public DateTime? DeactivatedAt { get; set; }

    [MaxLength(150)]
    public string? DeactivatedByName { get; set; }

    [MaxLength(500)]
    public string? DeactivateReason { get; set; }

    /// <summary>حذف نرم (soft delete) — از همه لیست‌ها حذف می‌شود</summary>
    public bool IsDeleted { get; set; }

    /// <summary>زمان انتقال به سطل بازیافت</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>کاربری که مدرک را حذف کرد</summary>
    [MaxLength(150)]
    public string? DeletedByName { get; set; }
}

/// <summary>دسترسی مستقیم یک کاربر روی مدرک؛ مقدم بر گروه و پوشه، حتی اگر محدودتر باشد.</summary>
public class DocumentPermission
{
    public int Id { get; set; }
    public int DocumentId { get; set; }

    /// <summary>شناسه کاربر — مقدار 0 یعنی این سطر «دسترسی گروهی (نقش)» است و RoleId تعیین‌کننده است.</summary>
    public int UserId { get; set; }

    /// <summary>شناسه نقش RBAC برای دسترسی گروهی — مقدار 0 یعنی دسترسی فردی (UserId).</summary>
    public int RoleId { get; set; }

    public DocAccessLevel Level { get; set; } = DocAccessLevel.Read;
    public bool CanDownload { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>نفرات گردش/تایید مدرک — الگو در سطح مدرک تعریف می‌شود و برای هر ورژن کپی می‌گردد.</summary>
public class DocumentApprover
{
    public int Id { get; set; }
    public int DocumentId { get; set; }

    /// <summary>null یعنی «الگوی مدرک»؛ مقدار دارد یعنی رکورد گردش همان ورژن.</summary>
    public int? VersionId { get; set; }

    public int UserId { get; set; }

    [MaxLength(150)]
    public string UserName { get; set; } = "";

    /// <summary>ترتیب نمایش (گردش موازی است؛ همه باید تایید کنند)</summary>
    public int Order { get; set; }

    /// <summary>0=در انتظار 1=تایید 2=رد</summary>
    public int Status { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    public DateTime? ActedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>امضای ترسیمی (base64 کوچک) — برای حسابرسی PWA آفلاین</summary>
    public string? SignatureDataUrl { get; set; }

    public bool BiometricVerified { get; set; }

    [MaxLength(300)]
    public string? DeviceInfo { get; set; }
}

/// <summary>ورژن مدرک — محتوا/پیوست‌ها روی ورژن ثبت می‌شوند.</summary>
public class DocumentVersion
{
    public int Id { get; set; }
    public int DocumentId { get; set; }

    /// <summary>شماره ورژن (۱، ۲، ۳ …)</summary>
    public int VersionNo { get; set; }

    [MaxLength(250)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? ChangeNote { get; set; }

    /// <summary>تاریخ انقضای همین ورژن (اختیاری)</summary>
    public DateTime? ExpireDate { get; set; }

    public DocVersionStatus Status { get; set; } = DocVersionStatus.Draft;

    /// <summary>ورژن فعال (قابل استناد)</summary>
    public bool IsActive { get; set; }

    /// <summary>فریز — تا پایان گردش تایید، قابل ویرایش نیست و به گردش عادی نمی‌آید.</summary>
    public bool IsFrozen { get; set; }

    /// <summary>کاربر خواسته این ورژن پس از تایید، فعال شود.</summary>
    public bool ActivateOnApprove { get; set; } = true;

    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ApprovedAt { get; set; }
}

/// <summary>لینک دو مدرک به هم (دوطرفه ذخیره می‌شود).</summary>
public class DocumentLink
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int LinkedDocumentId { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// کار کارتابل آرشیو — دو نوع:
/// Approval  : تایید ورژن (برای نفرات گردش)
/// RelatedUpdate : بررسی/به‌روزرسانی مدرک مرتبط پس از ورژن خوردن مدرک لینک‌شده
/// </summary>
public class DocCartableTask
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string Kind { get; set; } = "Approval";

    public int UserId { get; set; }
    public int DocumentId { get; set; }
    public int? VersionId { get; set; }

    /// <summary>مدرکی که باعث ایجاد این کار شده (در نوع RelatedUpdate)</summary>
    public int? SourceDocumentId { get; set; }

    [MaxLength(300)]
    public string Title { get; set; } = "";

    /// <summary>0=باز 1=انجام شده 2=رد شده</summary>
    public int Status { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DoneAt { get; set; }
}

/// <summary>لاگ رویدادهای مدرک</summary>
public class DocumentLog
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int? VersionId { get; set; }

    [MaxLength(50)]
    public string Action { get; set; } = "";

    [MaxLength(600)]
    public string? Detail { get; set; }

    public int UserId { get; set; }

    [MaxLength(150)]
    public string UserName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// ثبت اینکه برای یک مدرک، هشدار انقضای کدام آستانه قبلاً ارسال شده است.
/// جلوی ارسال تکراری در اجرای روزانه سرویس را می‌گیرد.
/// </summary>
public class DocExpiryAlert
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    /// <summary>آستانه هشدار بر حسب روز مانده (۶۰ / ۳۰ / ۷ / ۰=منقضی شد)</summary>
    public int ThresholdDays { get; set; }

    /// <summary>تاریخ انقضایی که این هشدار برایش صادر شد — اگر تاریخ عوض شود دوباره هشدار می‌رود.</summary>
    public DateTime ExpireDate { get; set; }

    public DateTime SentAt { get; set; } = DateTime.Now;

    /// <summary>تعداد کاربرانی که کار کارتابل گرفتند</summary>
    public int NotifiedCount { get; set; }
}

/// <summary>تگ (برچسب رنگی) برای دسته‌بندی و فیلتر سریع مدارک.</summary>
public class DocTag
{
    public int Id { get; set; }

    [MaxLength(60)]
    public string Name { get; set; } = "";

    /// <summary>کد رنگ هگز یا کلاس رنگی بوت‌استرپ</summary>
    [MaxLength(30)]
    public string Color { get; set; } = "#4f46e5";

    [MaxLength(250)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>انتساب تگ به مدرک (رابطه چند به چند).</summary>
public class DocumentTag
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int TagId { get; set; }
}

/// <summary>
/// متن استخراج‌شده و نتایج OCR پیوست‌های مدرک جهت جستجوی تمام‌متن (Full-Text).
/// </summary>
public class DocExtractedText
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int VersionId { get; set; }
    public int AttachmentId { get; set; }

    [MaxLength(255)]
    public string FileName { get; set; } = "";

    [MaxLength(100)]
    public string ContentType { get; set; } = "";

    /// <summary>نوع منبع استخراج: Pdf | PdfOcr | Word | Excel | Text | ImageOcr</summary>
    [MaxLength(30)]
    public string SourceType { get; set; } = "Text";

    /// <summary>متن کامل استخراج‌شده</summary>
    public string ExtractedText { get; set; } = "";

    /// <summary>متن نرمال‌شده (حذف اعراب، یکدست‌سازی حروف فارسی و اعداد) جهت جستجوی فوق‌سریع</summary>
    public string NormalizedText { get; set; } = "";

    /// <summary>وضعیت: Indexed | Failed | Pending</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Indexed";

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    public int CharacterCount { get; set; }

    public DateTime IndexedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// اتصال سند آرشیو به موجودیت‌های سایر ماژول‌های سامانه ERP (پروژه‌ها، پرسنل، اموال، فاکتورها، طرف‌حساب‌ها، تعمیرات و ...).
/// </summary>
public class DocEntityLink
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public ArchiveDocument? Document { get; set; }

    /// <summary>نام ماژول ERP: Projects | Hr | ItAssets | Invoicing | Catalog | Repairs | Office | Sales | Warehousing</summary>
    [MaxLength(60)]
    public string Module { get; set; } = "";

    /// <summary>شناسه رکورد در ماژول مبدأ (مثلاً شناسه پروژه، شناسه کارمند، شناسه دارایی و ...)</summary>
    public int EntityId { get; set; }

    /// <summary>کد یا شماره مرجع رکورد (اختیاری، مثلاً کد پروژه RE1-2001 یا شماره فاکتور)</summary>
    [MaxLength(100)]
    public string? EntityCode { get; set; }

    /// <summary>عنوان یا نام رکورد در ماژول مبدأ (مثلاً نام پروژه، نام کارمند، مدل دستگاه)</summary>
    [MaxLength(250)]
    public string EntityTitle { get; set; } = "";

    /// <summary>توضیحات یا یادداشت نحوه ارتباط</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}


/// <summary>وضعیت درخواست دسترسی به مدرک</summary>
public enum DocAccessRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Canceled = 3
}

/// <summary>
/// درخواست دسترسی — کاربری که به مدرکی دسترسی ندارد می‌تواند
/// از مدیران آن مدرک (دارندگان دسترسی کامل) سطح دسترسی بخواهد.
/// </summary>
public class DocAccessRequest
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    public int RequesterUserId { get; set; }

    [MaxLength(150)]
    public string RequesterName { get; set; } = "";

    /// <summary>سطح دسترسی درخواستی (خواندن / نوشتن / دسترسی کامل)</summary>
    public DocAccessLevel RequestedLevel { get; set; } = DocAccessLevel.Read;

    /// <summary>توضیح درخواست‌کننده — دلیل نیاز به دسترسی</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    public DocAccessRequestStatus Status { get; set; } = DocAccessRequestStatus.Pending;

    public int? HandledByUserId { get; set; }

    [MaxLength(150)]
    public string? HandledByName { get; set; }

    /// <summary>توضیح رسیدگی (مثلاً دلیل رد)</summary>
    [MaxLength(500)]
    public string? HandlerNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? HandledAt { get; set; }
}

/// <summary>
/// تنظیمات شماره‌گذار خودکار کد مدرک — تک‌ردیف (Id=1)، فقط مدیر آرشیو.
/// فعال‌سازی در «تنظیمات»؛ هنگام ساخت مدرک اگر کد خالی بماند به‌صورت خودکار تخصیص می‌یابد.
/// مدیر می‌تواند یک‌بار اجرای بازشمارش مدارک موجود را بزند.
/// </summary>
public class DocCodeSettings
{
    public int Id { get; set; }

    /// <summary>شماره‌گذار خودکار فعال باشد؟</summary>
    public bool Enabled { get; set; }

    /// <summary>پیشوند کد — مثل DOC-</summary>
    [MaxLength(10)]
    public string Prefix { get; set; } = "DOC-";

    /// <summary>تعداد ارقام عدد (Padding) — مثل 5 ⇒ DOC-00012</summary>
    public int Padding { get; set; } = 5;

    /// <summary>شماره بعدی که تخصیص خواهد یافت</summary>
    public int NextNumber { get; set; } = 1;

    /// <summary>اجرای یک‌باره بازشمارش مدارک موجود (فقط مدارک بدون کد منطبق با الگو)</summary>
    public DateTime? BackfillRanAt { get; set; }

    [MaxLength(150)]
    public string? BackfillRanByName { get; set; }

    public int BackfillAssignedCount { get; set; }
}

/// <summary>
/// لاگ چاپ فایل‌های مدرک — هر بار که کاربر نسخهٔ چاپی (واترمارک‌دار با نام
/// چاپ‌کننده + تاریخ) را دریافت می‌کند، یک ردیف اینجا + یک رویداد Print در
/// تاریخچه مدرک + یک ردیف Print در لاگ دسترسی پیوست ثبت می‌شود.
/// جدول با DocPrintSchemaV1 خودترمیم ساخته می‌شود (هر دو دیتابیس).
/// </summary>
public class DocumentPrintLog
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    public int VersionId { get; set; }

    public int AttachmentId { get; set; }

    /// <summary>نام فایل در لحظه چاپ (ممکن است بعداً حذف/جایگزین شود)</summary>
    [MaxLength(250)]
    public string FileName { get; set; } = "";

    [MaxLength(150)]
    public string? ContentType { get; set; }

    public int PrintedByUserId { get; set; }

    [MaxLength(150)]
    public string PrintedByName { get; set; } = "";

    public DateTime PrintedAt { get; set; } = DateTime.Now;

    /// <summary>متن واترمارکی که روی خروجی چاپی حک شد — مثل «نام — چاپ ۱۴۰۵/۰۶/۲۰ ۱۴:۳۲»</summary>
    [MaxLength(300)]
    public string WatermarkText { get; set; } = "";

    /// <summary>تعداد صفحات PDF؛ برای عکس ۱؛ اگر قابل‌تشخیص نبود null</summary>
    public int? PageCount { get; set; }

    /// <summary>حجم فایل واترمارک‌دار تحویلی (بایت)</summary>
    public long FileSizeBytes { get; set; }

    [MaxLength(60)]
    public string? IpAddress { get; set; }

    /// <summary>منبع چاپ: نسخه وب / موبایل / …</summary>
    [MaxLength(30)]
    public string Source { get; set; } = "Web";
}

/// <summary>
/// علاقه‌مندی (ستاره) — هر کاربر می‌تواند مدارک پرکاربرد را ستاره کند.
/// فقط روی مدارکی که کاربر حداقل View دارد ذخیره می‌شود، ولی نمایش فقط روی مدارک قابل‌دسترس فیلتر می‌شود.
/// </summary>
public class DocFavorite
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int DocumentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

// ========== اسکنر + نظرات + ردیاب نشت (2025-10-09) ==========

/// <summary>نظر/تاپیک روی مدرک یا نسخه — ترددار با منشن @</summary>
public class DocComment
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int? VersionId { get; set; }
    public int? ParentId { get; set; }
    public int AuthorUserId { get; set; }
    [MaxLength(150)] public string AuthorName { get; set; } = "";
    [MaxLength(2000)] public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? EditedAt { get; set; }
    public bool IsResolved { get; set; }
    public int? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>منشن‌های هر کامنت — برای نوتیف و هایلایت @</summary>
public class DocCommentMention
{
    public int Id { get; set; }
    public int CommentId { get; set; }
    public int MentionedUserId { get; set; }
    [MaxLength(150)] public string MentionedUserName { get; set; } = "";
}

/// <summary>لاگ ردیاب نشت — هر دانلود/چاپ با واترمارک نامرئی + هش</summary>
public class DocLeakTrace
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int? VersionId { get; set; }
    public int UserId { get; set; }
    [MaxLength(150)] public string UserName { get; set; } = "";
    [MaxLength(60)] public string? IpAddress { get; set; }
    [MaxLength(300)] public string VisibleWatermark { get; set; } = "";
    [MaxLength(300)] public string InvisiblePayload { get; set; } = "";
    [MaxLength(64)] public string FileHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Action { get; set; } = "Download"; // Download | Print | Share
}
