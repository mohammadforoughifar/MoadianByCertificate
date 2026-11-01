namespace Inventory.Shared.Dtos;

// ============================================================
//  ماژول آرشیو اسناد و مدارک — DTOها
// ============================================================

/// <summary>سطح دسترسی (هم‌ارز DocAccessLevel در بک‌اند)</summary>
public enum DocAccessLevelDto
{
    None = 0,
    View = 1,
    Read = 2,
    Write = 3,
    Full = 4
}

public enum DocVersionStatusDto
{
    Draft = 0,
    InReview = 1,
    Approved = 2,
    Rejected = 3,
    Archived = 4
}

/// <summary>پوشه آرشیو</summary>
public class DocFolderDto
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public bool PublicCanDownload { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>تعداد مدارک مستقیم داخل پوشه</summary>
    public int DocumentCount { get; set; }

    /// <summary>سطح دسترسی کاربر جاری روی این پوشه</summary>
    public DocAccessLevelDto MyLevel { get; set; }
    public bool MyCanDownload { get; set; }

    /// <summary>دسترسی‌های تعریف‌شده (فقط در حالت ویرایش برای مدیر)</summary>
    public List<DocPermissionDto> Permissions { get; set; } = new();
}

/// <summary>یک ردیف دسترسی کاربر</summary>
public class DocPermissionDto
{
    public int Id { get; set; }

    /// <summary>شناسه کاربر — مقدار 0 یعنی این ردیف «دسترسی گروهی (نقش)» است.</summary>
    public int UserId { get; set; }

    /// <summary>شناسه نقش RBAC برای دسترسی گروهی — مقدار 0 یعنی دسترسی فردی.</summary>
    public int RoleId { get; set; }

    public string UserName { get; set; } = "";

    /// <summary>نام نقش — فقط در ردیف‌های گروهی پر می‌شود.</summary>
    public string? RoleName { get; set; }

    /// <summary>آیا ردیف گروهی است؟</summary>
    public bool IsRole => RoleId > 0;

    public string? AvatarUrl { get; set; }
    public DocAccessLevelDto Level { get; set; } = DocAccessLevelDto.Read;
    public bool CanDownload { get; set; }
}

/// <summary>نفر گردش/تایید</summary>
public class DocApproverDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public int Order { get; set; }
    /// <summary>0=در انتظار 1=تایید 2=رد</summary>
    public int Status { get; set; }
    public string? Comment { get; set; }
    public DateTime? ActedAt { get; set; }
}

/// <summary>ورژن مدرک</summary>
public class DocVersionDto
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int VersionNo { get; set; }
    public string? Title { get; set; }
    public string? ChangeNote { get; set; }
    public DateTime? ExpireDate { get; set; }
    public DocVersionStatusDto Status { get; set; }
    public bool IsActive { get; set; }
    public bool IsFrozen { get; set; }
    public bool ActivateOnApprove { get; set; } = true;
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int AttachmentCount { get; set; }

    /// <summary>نفرات گردش این ورژن</summary>
    public List<DocApproverDto> Approvers { get; set; } = new();

    /// <summary>آیا کاربر جاری تاییدکننده در انتظارِ این ورژن است؟</summary>
    public bool IsMyTurn { get; set; }
}

/// <summary>مدرک — خلاصه برای لیست</summary>
public class DocumentListDto
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public string FolderName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";

    /// <summary>شماره مدرک مشتری</summary>
    public string? CustomerCode { get; set; }

    public DateTime? ExpireDate { get; set; }
    public bool IsExpired { get; set; }

    /// <summary>روز مانده تا انقضا؛ منفی یعنی منقضی شده، null یعنی تاریخ انقضا ندارد.</summary>
    public int? DaysToExpire { get; set; }

    /// <summary>در آستانه انقضاست (هنوز منقضی نشده ولی کمتر از حد هشدار مانده).</summary>
    public bool IsExpiringSoon { get; set; }

    public bool AllowMultipleActiveVersions { get; set; }
    public bool IsPublic { get; set; }

    /// <summary>محرمانه — دانلود/مشاهده فایل‌ها با تایید مجدد رمز</summary>
    public bool RequireDownloadConfirm { get; set; }

    /// <summary>واترمارک پیش‌نمایش فایل‌ها</summary>
    public bool WatermarkPreview { get; set; }

    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>دلیل و اطلاعات غیرفعال‌سازی</summary>
    public DateTime? DeactivatedAt { get; set; }
    public string? DeactivatedByName { get; set; }
    public string? DeactivateReason { get; set; }

    /// <summary>در سطل بازیافت است</summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByName { get; set; }

    public int VersionCount { get; set; }
    public int ActiveVersionNo { get; set; }
    public DocVersionStatusDto LastVersionStatus { get; set; }
    public int LinkCount { get; set; }

    public DocAccessLevelDto MyLevel { get; set; }
    public bool MyCanDownload { get; set; }

    /// <summary>تگ‌های اختصاص‌داده‌شده به مدرک</summary>
    public List<DocTagDto> Tags { get; set; } = new();

    /// <summary>آیا مدرک دارای محتوای ایندکس‌شده / OCR است؟</summary>
    public bool HasIndexedContent { get; set; }

    /// <summary>بخشی از متن استخراج‌شده در صورت جستجوی تمام‌متن (Snippet)</summary>
    public string? ContentSnippet { get; set; }

    /// <summary>نام فایلی که کلمه در آن پیدا شد</summary>
    public string? MatchedAttachmentFileName { get; set; }

    /// <summary>نوع فایل منطبق‌شده: Pdf | PdfOcr | Word | Excel | ImageOcr | Text</summary>
    public string? MatchedSourceType { get; set; }

    /// <summary>سازگاری با نسخه قدیمی؛ اتصال ERP غیرفعال است و مقدار همیشه صفر است.</summary>
    public int EntityLinkCount { get; set; }

    /// <summary>سازگاری با نسخه قدیمی؛ همیشه خالی است.</summary>
    public List<string> LinkedModules { get; set; } = new();
}

/// <summary>مدرک — کامل (فرم ثبت/ویرایش و صفحه جزئیات)</summary>
public class DocumentDto
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public string FolderName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";

    /// <summary>شماره مدرک مشتری</summary>
    public string? CustomerCode { get; set; }

    public string? Description { get; set; }
    public DateTime? ExpireDate { get; set; }
    public bool AllowMultipleActiveVersions { get; set; }
    public bool IsPublic { get; set; }
    public bool PublicCanDownload { get; set; }

    /// <summary>محرمانه — دانلود/مشاهده هر فایل این مدرک با تایید مجدد رمز کاربر</summary>
    public bool RequireDownloadConfirm { get; set; }

    /// <summary>واترمارک پیش‌نمایش — روی نمایش داخل برنامه فایل‌ها نام کاربر+زمان حک می‌شود</summary>
    public bool WatermarkPreview { get; set; }

    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? DeactivatedAt { get; set; }
    public string? DeactivatedByName { get; set; }
    public string? DeactivateReason { get; set; }

    /// <summary>تگ‌های مدرک</summary>
    public List<DocTagDto> Tags { get; set; } = new();

    /// <summary>شناسه‌های تگ‌های انتخاب‌شده در فرم</summary>
    public List<int> TagIds { get; set; } = new();

    /// <summary>تعداد پیوست‌های دارای متن استخراج‌شده / OCR</summary>
    public int ExtractedTextCount { get; set; }

    /// <summary>شناسه مدارکی که باید به این مدرک لینک شوند (چندانتخابی در فرم)</summary>
    public List<int> LinkedDocumentIds { get; set; } = new();

    /// <summary>نفرات گردش (الگوی مدرک)</summary>
    public List<DocApproverDto> Approvers { get; set; } = new();

    /// <summary>دسترسی‌های مستقیم مدرک</summary>
    public List<DocPermissionDto> Permissions { get; set; } = new();

    public List<DocVersionDto> Versions { get; set; } = new();
    public List<DocLinkDto> Links { get; set; } = new();
    public List<DocEntityLinkDto> EntityLinks { get; set; } = new();
    public List<DocLogDto> Logs { get; set; } = new();

    public DocAccessLevelDto MyLevel { get; set; }
    public bool MyCanDownload { get; set; }

    /// <summary>در ثبت اولیه: آیا ورژن ۱ بلافاصله فعال باشد</summary>
    public bool FirstVersionActive { get; set; } = true;
}

/// <summary>مدرک لینک‌شده</summary>
public class DocLinkDto
{
    public int Id { get; set; }
    public int LinkedDocumentId { get; set; }
    public string Code { get; set; } = "";
    public string? CustomerCode { get; set; }
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
    public int ActiveVersionNo { get; set; }
}

/// <summary>لاگ رویداد</summary>
public class DocLogDto
{
    public int Id { get; set; }
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
    public string UserName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? VersionId { get; set; }
}

/// <summary>ورودی ساخت ورژن جدید</summary>
public class DocVersionCreateDto
{
    public int DocumentId { get; set; }
    public string? Title { get; set; }
    public string? ChangeNote { get; set; }
    public DateTime? ExpireDate { get; set; }

    /// <summary>پس از تایید، این ورژن فعال شود</summary>
    public bool ActivateOnApprove { get; set; } = true;

    /// <summary>نفرات گردش این ورژن — خالی یعنی از الگوی مدرک استفاده شود</summary>
    public List<int> ApproverUserIds { get; set; } = new();

    /// <summary>بلافاصله به گردش برود (فریز شود و در کارتابل بنشیند)</summary>
    public bool SendToFlow { get; set; } = true;
}

/// <summary>اقدام تایید/رد — با پشتیبانی امضای موبایل و آفلاین</summary>
public class DocApprovalActionDto
{
    public int VersionId { get; set; }
    public bool Approve { get; set; }
    public string? Comment { get; set; }
    /// <summary>امضای ترسیمی base64 PNG (data:image/png;base64,...)</summary>
    public string? SignatureDataUrl { get; set; }
    /// <summary>آیا بیومتریک (WebAuthn) تایید شد؟</summary>
    public bool BiometricVerified { get; set; }
    /// <summary>اطلاعات دستگاه برای حسابرسی آفلاین</summary>
    public string? DeviceInfo { get; set; }
    public DateTime? ClientTimestamp { get; set; }
}

/// <summary>آیتم کارتابل آرشیو</summary>
public class DocCartableItemDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = "Approval";
    public int DocumentId { get; set; }
    public int? VersionId { get; set; }
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public int VersionNo { get; set; }
    public string? SourceDocumentCode { get; set; }
    public string Title { get; set; } = "";
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>ورودی لینک چندتایی مدارک</summary>
public class DocLinkSaveDto
{
    public int DocumentId { get; set; }
    /// <summary>لیست مدارکی که باید لینک شوند (چندانتخابی)</summary>
    public List<int> LinkedDocumentIds { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>ورودی تغییر وضعیت فعال/غیرفعال مدرک</summary>
public class DocSetActiveDto
{
    public bool IsActive { get; set; }
    public string? Reason { get; set; }
}

/// <summary>ورودی ذخیره دسترسی‌ها</summary>
public class DocPermissionsSaveDto
{
    public bool IsPublic { get; set; }
    public bool PublicCanDownload { get; set; }

    /// <summary>محرمانه — دانلود/مشاهده هر فایل با تایید مجدد رمز کاربر</summary>
    public bool RequireDownloadConfirm { get; set; }

    /// <summary>واترمارک پیش‌نمایش فایل‌های مدرک</summary>
    public bool WatermarkPreview { get; set; }

    public List<DocPermissionDto> Items { get; set; } = new();
}

/// <summary>داده‌های کمکی صفحه آرشیو</summary>
public class DocArchiveLookups
{
    public List<LookupItem> Users { get; set; } = new();
    public List<LookupItem> Documents { get; set; } = new();

    /// <summary>نقش‌های فعال RBAC — برای تعریف «دسترسی گروهی» روی پوشه/مدرک</summary>
    public List<LookupItem> Roles { get; set; } = new();

    /// <summary>شماره‌گذار خودکار کد مدرک فعال است — در فرم ساخت، کد می‌تواند خالی بماند</summary>
    public bool CodeNumberingEnabled { get; set; }
}

/// <summary>ردیف درخواست دسترسی به مدرک</summary>
public class DocAccessRequestDto
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int RequesterUserId { get; set; }
    public string RequesterName { get; set; } = "";
    public DocAccessLevelDto RequestedLevel { get; set; } = DocAccessLevelDto.Read;
    public string? Note { get; set; }

    /// <summary>0=درانتظار، 1=پذیرفته، 2=ردشده، 3=لغوشده</summary>
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? HandledByName { get; set; }
    public string? HandlerNote { get; set; }
    public DateTime? HandledAt { get; set; }
}

/// <summary>ثبت درخواست دسترسی</summary>
public class DocAccessRequestSaveDto
{
    public DocAccessLevelDto Level { get; set; } = DocAccessLevelDto.Read;
    public string? Note { get; set; }
}

/// <summary>پذیرش درخواست دسترسی (توسط مدیر مدرک)</summary>
public class DocAccessRequestApproveDto
{
    public DocAccessLevelDto Level { get; set; } = DocAccessLevelDto.Read;
    /// <summary>دانلود مستقل از خواندن است و باید صریحاً اعطا شود.</summary>
    public bool CanDownload { get; set; }
}

/// <summary>تنظیمات شماره‌گذار خودکار کد مدرک</summary>
public class DocCodeSettingsDto
{
    public bool Enabled { get; set; }
    public string Prefix { get; set; } = "DOC-";
    public int Padding { get; set; } = 5;
    public int NextNumber { get; set; } = 1;
    public DateTime? BackfillRanAt { get; set; }
    public string? BackfillRanByName { get; set; }
    public int BackfillAssignedCount { get; set; }
}

/// <summary>نتیجه اجرای یک‌باره بازشمارش مدارک موجود</summary>
public class DocCodeBackfillResultDto
{
    public int Assigned { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>یک مورد از گزارش مشاهده/دانلود فایل‌های مدرک</summary>
public class DocAttachmentAccessLogDto
{
    public int AttachmentId { get; set; }
    public string FileName { get; set; } = "";
    /// <summary>Preview یا Download</summary>
    public string Action { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? Ip { get; set; }
    public DateTime At { get; set; }
}

/// <summary>یک مورد از لاگ چاپ فایل‌های مدرک — چه کسی، چه فایلی، کی، با چه واترمارکی</summary>
public class DocPrintLogDto
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public int VersionId { get; set; }
    public int VersionNo { get; set; }
    public int AttachmentId { get; set; }
    public string FileName { get; set; } = "";
    public string UserName { get; set; } = "";
    /// <summary>متن واترمارک حک‌شده روی خروجی چاپی</summary>
    public string WatermarkText { get; set; } = "";
    /// <summary>تعداد صفحات (PDF) — برای عکس ۱</summary>
    public int? PageCount { get; set; }
    public long FileSizeBytes { get; set; }
    public string? Ip { get; set; }
    public DateTime At { get; set; }
}

/// <summary>خلاصه وضعیت انقضای مدارک برای بج‌های درخت پوشه‌ها</summary>
public class DocExpirySummaryDto
{
    /// <summary>تعداد مدارکی که تاریخ انقضایشان گذشته است</summary>
    public int Expired { get; set; }

    /// <summary>تعداد مدارکی که تا آستانه تعیین‌شده منقضی می‌شوند</summary>
    public int ExpiringSoon { get; set; }

    /// <summary>آستانه «رو به انقضا» بر حسب روز</summary>
    public int ExpiringDays { get; set; }
}

/// <summary>نتیجه اجرای دستی بررسی انقضا</summary>
public class DocExpiryRunResultDto
{
    public int Created { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>پاسخ ساده حاوی پیام</summary>
public class DocMessageDto
{
    public string Message { get; set; } = "";
}

// ==================== مقایسه دو ورژن ====================

/// <summary>یک تفاوت بین دو ورژن</summary>
public class DocVersionDiffRow
{
    /// <summary>نام فیلد برای نمایش — مثل «عنوان» یا «تاریخ انقضا»</summary>
    public string Field { get; set; } = "";

    /// <summary>مقدار در ورژن مبدأ</summary>
    public string? Left { get; set; }

    /// <summary>مقدار در ورژن مقصد</summary>
    public string? Right { get; set; }

    /// <summary>added | removed | changed | same</summary>
    public string Kind { get; set; } = "same";
}

/// <summary>نتیجه مقایسه دو ورژن یک مدرک</summary>
public class DocVersionCompareDto
{
    public int DocumentId { get; set; }
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";

    public DocVersionDto? Left { get; set; }
    public DocVersionDto? Right { get; set; }

    /// <summary>تفاوت فیلدهای اصلی</summary>
    public List<DocVersionDiffRow> Fields { get; set; } = new();

    /// <summary>تفاوت نفرات گردش</summary>
    public List<DocVersionDiffRow> Approvers { get; set; } = new();

    /// <summary>تفاوت پیوست‌ها</summary>
    public List<DocVersionDiffRow> Attachments { get; set; } = new();

    /// <summary>رویدادهای تاریخچه بین این دو ورژن</summary>
    public List<DocLogDto> Between { get; set; } = new();

    /// <summary>تعداد کل تفاوت‌ها (بدون موارد یکسان)</summary>
    public int ChangeCount { get; set; }
}

// ==================== تگ‌ها، OCR و جستجوی تمام‌متن ====================

/// <summary>تگ مدرک</summary>
public class DocTagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4f46e5";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public int DocumentCount { get; set; }
}

public class DocTagSaveDto
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4f46e5";
    public string? Description { get; set; }
}

public class DocSetTagsDto
{
    public int DocumentId { get; set; }
    public List<int> TagIds { get; set; } = new();
}

/// <summary>اطلاعات متن استخراج‌شده یا OCR یک پیوست</summary>
public class DocExtractedTextDto
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int VersionId { get; set; }
    public int AttachmentId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string SourceType { get; set; } = "Text";
    public string ExtractedText { get; set; } = "";
    public string Status { get; set; } = "Indexed";
    public string? ErrorMessage { get; set; }
    public int CharacterCount { get; set; }
    public DateTime IndexedAt { get; set; }
}

/// <summary>فیلترهای ترکیبی و پیشرفته جستجوی مدارک</summary>
public class DocSearchFilterDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    /// <summary>جستجوی عمومی در عنوان، کد مدرک، کد مشتری و توضیحات</summary>
    public string? Search { get; set; }

    /// <summary>جستجوی تمام‌متن درون محتوای فایل‌ها و نتایج OCR</summary>
    public string? ContentSearch { get; set; }

    public int? FolderId { get; set; }

    /// <summary>جستجو شامل تمام زیرپوشه‌های این پوشه هم باشد</summary>
    public bool IncludeSubfolders { get; set; } = true;

    /// <summary>active | inactive | deleted | all</summary>
    public string Status { get; set; } = "active";

    /// <summary>all | expiring | expired | valid</summary>
    public string ExpiryStatus { get; set; } = "all";

    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }

    public DateTime? ExpiryFrom { get; set; }
    public DateTime? ExpiryTo { get; set; }

    /// <summary>انواع فایل: pdf, word, excel, image, text</summary>
    public List<string> FileTypes { get; set; } = new();

    public int? ApproverUserId { get; set; }
    public int? CreatedByUserId { get; set; }

    public List<int> TagIds { get; set; } = new();

    public bool? HasAttachment { get; set; }

    /// <summary>فیلتر بازنشسته ERP؛ مقدار غیرخالی یا LinkedEntityId پاسخ 410 می‌گیرد.</summary>
    public string? LinkedModule { get; set; }
    public int? LinkedEntityId { get; set; }
}

/// <summary>نتیجه اجرای دستی OCR</summary>
public class DocOcrRunResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int CharacterCount { get; set; }
    public string SourceType { get; set; } = "";
    public string? ExtractedSnippet { get; set; }
}

/// <summary>نتیجه بازایندکس دسته‌ای</summary>
public class DocReindexResultDto
{
    public int ProcessedCount { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public string Message { get; set; } = "";
}

// ==================== یکپارچه‌سازی با ماژول‌های ERP و دانلود درختی ZIP ====================

/// <summary>ارتباط سند آرشیو با موجودیت سایر ماژول‌های سامانه ERP</summary>
public class DocEntityLinkDto
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public int FolderId { get; set; }
    public string FolderName { get; set; } = "";
    public int ActiveVersionNo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ExpireDate { get; set; }
    public bool IsExpired { get; set; }

    /// <summary>نام ماژول: Projects | Hr | ItAssets | Invoicing | Catalog | Repairs | Office | Sales | Warehousing</summary>
    public string Module { get; set; } = "";
    public string ModuleTitle { get; set; } = "";

    /// <summary>شناسه رکورد ماژول</summary>
    public int EntityId { get; set; }

    /// <summary>کد موجودیت در ماژول مبدأ</summary>
    public string? EntityCode { get; set; }

    /// <summary>عنوان موجودیت</summary>
    public string EntityTitle { get; set; } = "";

    /// <summary>لینک مستقیم در سامانه به این موجودیت</summary>
    public string? EntityUrl { get; set; }

    public string? Note { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public DocAccessLevelDto MyLevel { get; set; }
    public bool MyCanDownload { get; set; }

    public List<DocTagDto> Tags { get; set; } = new();
    public List<DocAttachmentSummaryDto> Attachments { get; set; } = new();
}

public class DocAttachmentSummaryDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public bool CanPreview { get; set; }
    public bool CanDownload { get; set; }
    public int VersionNo { get; set; }
}

public class DocEntityLinkSaveDto
{
    public int DocumentId { get; set; }
    public string Module { get; set; } = "";
    public int EntityId { get; set; }
    public string? EntityCode { get; set; }
    public string EntityTitle { get; set; } = "";
    public string? Note { get; set; }
}

public class DocEntityLookupItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string Module { get; set; } = "";
}

public class DocQuickCreateLinkedDto
{
    public int FolderId { get; set; }
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";
    public string? CustomerCode { get; set; }
    public string? Description { get; set; }
    public DateTime? ExpireDate { get; set; }
    public List<int> TagIds { get; set; } = new();
    public string Module { get; set; } = "";
    public int EntityId { get; set; }
    public string? EntityCode { get; set; }
    public string EntityTitle { get; set; } = "";
    public string? LinkNote { get; set; }
}

public class DocFolderZipExportOptionsDto
{
    public int? FolderId { get; set; }
    public bool IncludeSubfolders { get; set; } = true;
    public bool OnlyActiveVersions { get; set; } = true;
    public bool IncludeManifest { get; set; } = true;
}

// ==================== علاقه‌مندی و اخیراً دیده‌شده ====================
public class DocFavoriteDto
{
    public int DocumentId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int FolderId { get; set; }
    public string FolderName { get; set; } = "";
    public DateTime FavoritedAt { get; set; }
    public bool IsFavorite { get; set; } = true;
}

public class DocRecentlyViewedDto
{
    public int DocumentId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int FolderId { get; set; }
    public string FolderName { get; set; } = "";
    public DateTime ViewedAt { get; set; }
    public string Action { get; set; } = "View";
}

// ==================== عملیات گروهی (Bulk) ====================
public class DocBulkMoveDto
{
    public List<int> DocumentIds { get; set; } = new();
    public int TargetFolderId { get; set; }
}

public class DocBulkTagsDto
{
    public List<int> DocumentIds { get; set; } = new();
    public List<int> TagIds { get; set; } = new();
    /// <summary>append = افزودن به موجود، replace = جایگزینی کامل</summary>
    public string Mode { get; set; } = "append";
}

public class DocBulkExpireDto
{
    public List<int> DocumentIds { get; set; } = new();
    public DateTime? ExpireDate { get; set; }
}

public class DocBulkResultDto
{
    public int Requested { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
    public string Message { get; set; } = "";
}

// ==================== جستجوی معنایی (B1) ====================
public class DocSemanticSearchRequest
{
    public string Query { get; set; } = "";
    public int? FolderId { get; set; }
    public bool IncludeSubfolders { get; set; } = true;
    public List<int> TagIds { get; set; } = new();
    /// <summary>active | inactive | all — پیش‌فرض active</summary>
    public string Status { get; set; } = "active";
    public int Take { get; set; } = 10;
}

public class DocSemanticHitDto
{
    public int DocumentId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int FolderId { get; set; }
    public string FolderName { get; set; } = "";
    public string Snippet { get; set; } = "";
    public double Score { get; set; }
    public DocAccessLevelDto MyLevel { get; set; }
    public bool MyCanDownload { get; set; }
    public List<DocTagDto> Tags { get; set; } = new();
    public DateTime? ExpireDate { get; set; }
    public string Link { get; set; } = "";
}

public class DocSemanticSearchResultDto
{
    public List<DocSemanticHitDto> Hits { get; set; } = new();
    /// <summary>true = بردار معنایی، false = کلیدواژه‌ای fallback</summary>
    public bool Semantic { get; set; }
    public string? Note { get; set; }
    public int TotalCandidates { get; set; }
}

// ==================== نظرات و منشن + ردیاب نشت + تشخیص تکراری (2025-10-09) ====================
public class DocCommentDto
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int? VersionId { get; set; }
    public int? ParentId { get; set; }
    public int AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsResolved { get; set; }
    public List<DocCommentDto> Replies { get; set; } = new();
    public List<int> MentionedUserIds { get; set; } = new();
    public List<string> MentionedUserNames { get; set; } = new();
}

public class DocCommentCreateDto
{
    public int DocumentId { get; set; }
    public int? VersionId { get; set; }
    public int? ParentId { get; set; }
    public string Body { get; set; } = "";
    public List<int> MentionedUserIds { get; set; } = new();
}

public class DocLeakTraceDto
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public string UserName { get; set; } = "";
    public string VisibleWatermark { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? FileHash { get; set; }
}

public class DocDuplicateCheckRequest
{
    public string? FileHash { get; set; }
    public string? FileName { get; set; }
    public string? Title { get; set; }
    public string? Snippet { get; set; }
    public long? FileSize { get; set; }
}

public class DocDuplicateHitDto
{
    public int DocumentId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string FolderName { get; set; } = "";
    public double Score { get; set; }
    public string Reason { get; set; } = ""; // hash-exact | semantic | name
    public DateTime CreatedAt { get; set; }
}

public class DocDuplicateResultDto
{
    public List<DocDuplicateHitDto> Hits { get; set; } = new();
    public bool IsDuplicate { get; set; }
    public string? Note { get; set; }
}

// ==================== پیشنهاد خودکار برچسب و پوشه (B2) ====================
public class DocAutoSuggestRequest
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? CurrentFolderId { get; set; }
    public string? ContentSnippet { get; set; }
}

public class DocTagSuggestionDto
{
    public int TagId { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4f46e5";
    public double Score { get; set; }
    public string Reason { get; set; } = "";
}

public class DocFolderSuggestionDto
{
    public int FolderId { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public double Score { get; set; }
    public bool CanWrite { get; set; }
}

public class DocAutoSuggestResultDto
{
    public List<DocTagSuggestionDto> TagSuggestions { get; set; } = new();
    public DocFolderSuggestionDto? FolderSuggestion { get; set; }
    public bool Semantic { get; set; }
    public string? Note { get; set; }
}


// =====================================================================
// گفتگو با سند — پرسش از متن استخراج‌شدهٔ مدرک (با هوش مصنوعی یا بازیابی متن)
// =====================================================================

/// <summary>یک نوبت از تاریخچهٔ گفتگو.</summary>
public class DocChatTurn
{
    public string Role { get; set; } = "user"; // user | assistant
    public string Text { get; set; } = "";
}

/// <summary>درخواست گفتگو با سند.</summary>
public class DocChatRequest
{
    public string Question { get; set; } = "";
    public List<DocChatTurn> History { get; set; } = new();
}

/// <summary>پاسخ گفتگو با سند.</summary>
public class DocChatResponse
{
    public string Answer { get; set; } = "";

    /// <summary>ai = پاسخ مدل زبانی | retrieval = بازیابی از متن سند | notext = متن استخراج نشده</summary>
    public string Mode { get; set; } = "retrieval";

    public bool HasText { get; set; }

    /// <summary>نام فایل‌هایی که پاسخ از آن‌ها ساخته شده.</summary>
    public List<string> Sources { get; set; } = new();
}
