using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>درخواست خدمت از واحد آی‌تی.</summary>
public class ItRequest
{
    public int Id { get; set; }

    /// <summary>شماره منحصربه‌فرد درخواست — مثل IT-0412-25</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    /// <summary>نام و نام خانوادگی درخواست‌کننده (پیش‌فرض از اطلاعات لاگین)</summary>
    [MaxLength(150)]
    public string RequesterName { get; set; } = "";

    /// <summary>شناسه کاربر لاگین درخواست‌کننده — برای درخواستِ بیرونی صفر است (کاربرِ ما نیست)</summary>
    public int RequesterUserId { get; set; }

    // ---------- درخواستِ بیرونی (از شرکتِ راه دور) ----------

    /// <summary>شرکتِ فرستنده — null یعنی درخواست داخلیِ همین نصب</summary>
    public int? SourceCompanyId { get; set; }
    public ItClientCompany? SourceCompany { get; set; }

    /// <summary>
    /// کلید پایدارِ کاربر در نرم‌افزارِ خودش (UUID). کاربر در سرور ما «کاربر» نیست؛
    /// با همین کلید «درخواست‌های من» را می‌بیند.
    /// </summary>
    [MaxLength(40)]
    public string? ExternalRequesterKey { get; set; }

    /// <summary>شناسه‌ی یکتای درخواست در سیستمِ فرستنده — کلیدِ idempotency (تلاش مجدد، درخواست تکراری نمی‌سازد)</summary>
    [MaxLength(40)]
    public string? ExternalId { get; set; }

    [MaxLength(30)]
    public string? RequesterPhone { get; set; }

    [MaxLength(120)]
    public string? RequesterEmail { get; set; }

    /// <summary>
    /// توکنِ محرمانه‌ی پیگیری — بدون آن، دانستن شمارهٔ درخواست برای خواندن پاسخ کافی نیست.
    /// (درخواست‌های داخلی این مقدار را ندارند و با شماره پیگیری می‌شوند.)
    /// </summary>
    [MaxLength(40)]
    public string? TrackToken { get; set; }

    /// <summary>سیستم انتخاب‌شده (اختیاری)</summary>
    public int? SystemInfoId { get; set; }

    /// <summary>برچسب سیستم برای نمایش (نام/شناسه سیستم)</summary>
    [MaxLength(250)]
    public string? SystemLabel { get; set; }

    /// <summary>نوع درخواست: Hardware | Software | Network | Telecom</summary>
    [MaxLength(30)]
    public string RequestType { get; set; } = "Hardware";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(2000)]
    public string Description { get; set; } = "";

    /// <summary>وضعیت: New | Assigned | ManagerApproved | Completed</summary>
    [MaxLength(30)]
    public string Status { get; set; } = "New";

    /// <summary>توضیح کلی مدیر هنگام ارجاع</summary>
    [MaxLength(2000)]
    public string? ManagerNote { get; set; }

    /// <summary>پاسخ نهایی مدیر (بر اساس شرح کارشناسان)</summary>
    [MaxLength(4000)]
    public string? FinalResponse { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? AssignedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>ارجاع درخواست به پرسنل آی‌تی — هر پرسنل شرح مدیر و گزارش خودش را دارد.</summary>
public class ItRequestAssignment
{
    public int Id { get; set; }
    public int RequestId { get; set; }

    /// <summary>شناسه کاربر لاگین کارشناس</summary>
    public int ExpertUserId { get; set; }

    [MaxLength(150)]
    public string ExpertName { get; set; } = "";

    /// <summary>شرح/دستور مدیر برای این پرسنل</summary>
    [MaxLength(2000)]
    public string? ManagerInstruction { get; set; }

    /// <summary>گزارش کارشناس</summary>
    [MaxLength(4000)]
    public string? ExpertReport { get; set; }

    public bool ReportSubmitted { get; set; }

    /// <summary>نتیجه کارشناس: true=انجام شد | false=انجام نشد | null=هنوز پاسخ نداده</summary>
    public bool? Done { get; set; }

    /// <summary>تصمیم مدیر روی گزارش این کارشناس: Approved | Rejected | null=در انتظار</summary>
    [MaxLength(20)]
    public string? ManagerDecision { get; set; }

    [MaxLength(1000)]
    public string? ManagerDecisionNote { get; set; }

    /// <summary>مدیر این گزارش را در پاسخ نهایی لحاظ کرده است</summary>
    public bool IncludeInFinal { get; set; } = true;

    public DateTime? RepliedAt { get; set; }
}

/// <summary>فایل پیوست درخواست — درخواست‌دهنده، کارشناس یا مدیر.</summary>
public class ItRequestAttachment
{
    public int Id { get; set; }
    public int RequestId { get; set; }

    [MaxLength(255)]
    public string FileName { get; set; } = "";

    [MaxLength(100)]
    public string ContentType { get; set; } = "";

    public byte[] Data { get; set; } = Array.Empty<byte>();
    /// <summary>مسیر نسبی فایل داخل uploads/ (فایل‌های جدید روی دیسک؛ Data فقط برای رکوردهای قدیمی).</summary>
    [MaxLength(255)]
    public string? FilePath { get; set; }


    /// <summary>نقش آپلودکننده: Requester | Expert | Manager</summary>
    [MaxLength(20)]
    public string UploaderRole { get; set; } = "Requester";

    [MaxLength(150)]
    public string UploaderName { get; set; } = "";

    public int UploaderUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.Now;
}


/// <summary>آرشیو کامل رفت‌وبرگشت‌های هر درخواست.</summary>
public class ItRequestLog
{
    public int Id { get; set; }
    public int RequestId { get; set; }

    [MaxLength(150)]
    public string ActorName { get; set; } = "";

    /// <summary>Requester | Expert | Manager | System</summary>
    [MaxLength(20)]
    public string ActorRole { get; set; } = "System";

    /// <summary>Created | Assigned | Report | Approved | Rejected | Finalized | Completed</summary>
    [MaxLength(30)]
    public string Action { get; set; } = "";

    [MaxLength(4000)]
    public string? Text { get; set; }

    /// <summary>فقط برای واحد IT قابل مشاهده (درخواست‌دهنده نمی‌بیند)</summary>
    public bool InternalOnly { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>اعلان (نوتیفیکیشن) کاربر — با SignalR بلادرنگ ارسال می‌شود.</summary>
public class AppNotification
{
    public int Id { get; set; }
    public int UserId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(500)]
    public string? Body { get; set; }

    /// <summary>فرستنده پیام</summary>
    [MaxLength(150)]
    public string FromName { get; set; } = "";

    /// <summary>نام فرم/بخش مربوطه</summary>
    [MaxLength(100)]
    public string FormName { get; set; } = "";

    /// <summary>لینک داخلی برای رفتن به فرم</summary>
    [MaxLength(200)]
    public string? Link { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}


/// <summary>رویت درخواست توسط هر کاربر — برای نشانگر «جدید» در کارتابل‌ها.</summary>
public class ItRequestSeen
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public int UserId { get; set; }
    public DateTime SeenAt { get; set; } = DateTime.Now;
}
