using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

// ============================================================
//  ماژول ایمیل سازمانی (پست الکترونیک) — پورت جداول دیتابیس Otomasion
//  • هر کاربر می‌تواند چند حساب ایمیل داشته باشد (Gmail / Yahoo / وب‌میل / Outlook / سفارشی)
//  • ایمیل دبیرخانه با فلگ IsDabirkhane از ایمیل‌های شخصی جدا می‌شود و
//    فقط برای ارسال نامه صادره از دبیرخانه استفاده می‌شود.
//  • نام جداول و ستون‌ها دقیقاً مطابق جداول اصلی است:
//    Oto_TBL_Email / Oto_TBL_Sent / Oto_TBL_Inbox / Oto_TBl_EmailFolder / Oto_TBL_EmailAttachments
// ============================================================

/// <summary>
/// حساب‌های ایمیل کاربران — معادل SELECT * FROM [Otomasion].[dbo].[Oto_TBL_Email]
/// SELECT [Email_Id],[Email_Address],[Password],[SMTP],[IMAP],[User_Id],
///        [Activation_Code],[IsActive],[EmailType],[Semat_Id],[IsDabirkhane]
/// </summary>
[Table("Oto_TBL_Email")]
public class OtoEmail
{
    [Key]
    [Column("Email_Id")]
    public int EmailId { get; set; }

    /// <summary>آدرس ایمیل — مثل name@gmail.com</summary>
    [Column("Email_Address")]
    [MaxLength(250)]
    public string EmailAddress { get; set; } = "";

    /// <summary>رمز عبور حساب — برای Gmail/Yahoo حتماً باید «App Password» باشد (نه رمز اصلی)</summary>
    [Column("Password")]
    [MaxLength(255)]
    public string Password { get; set; } = "";

    /// <summary>سرور SMTP ارسال — مثل smtp.gmail.com</summary>
    [Column("SMTP")]
    [MaxLength(150)]
    public string Smtp { get; set; } = "";

    /// <summary>سرور IMAP دریافت — مثل imap.gmail.com</summary>
    [Column("IMAP")]
    [MaxLength(150)]
    public string Imap { get; set; } = "";

    /// <summary>مالک حساب — هر کاربر می‌تواند چند ایمیل داشته باشد</summary>
    [Column("User_Id")]
    public int UserId { get; set; }

    [Column("Activation_Code")]
    [MaxLength(100)]
    public string? ActivationCode { get; set; }

    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>نوع حساب: Gmail / Yahoo / Outlook / Webmail / Custom</summary>
    [Column("EmailType")]
    [MaxLength(30)]
    public string EmailType { get; set; } = "Gmail";

    [Column("Semat_Id")]
    public int? SematId { get; set; }

    /// <summary>true = ایمیل رسمی دبیرخانه (جدا از ایمیل‌های شخصی کاربران)</summary>
    [Column("IsDabirkhane")]
    public bool IsDabirkhane { get; set; }

    // ---------------- ستون‌های تکمیلی (خارج از طرح اولیه برای اتصال درست) ----------------

    /// <summary>پورت SMTP — پیش‌فرض ۵۸۷</summary>
    [Column("SmtpPort")]
    public int SmtpPort { get; set; } = 587;

    /// <summary>اتصال امن SMTP (STARTTLS/SSL)</summary>
    [Column("SmtpSsl")]
    public bool SmtpSsl { get; set; } = true;

    /// <summary>پورت IMAP — پیش‌فرض ۹۹۳</summary>
    [Column("ImapPort")]
    public int ImapPort { get; set; } = 993;

    /// <summary>اتصال امن IMAP (SSL)</summary>
    [Column("ImapSsl")]
    public bool ImapSsl { get; set; } = true;

    /// <summary>نام نمایشی فرستنده (مثلاً «دبیرخانه شرکت فروغ آریا»)</summary>
    [Column("Display_Name")]
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>آخرین زمان همگام‌سازی صندوق (IMAP)</summary>
    [Column("Last_Sync")]
    public DateTime? LastSync { get; set; }

    /// <summary>آخرین شناسه‌ی IMAP همگام‌شده از صندوق دریافتی — ۰ یعنی هنوز همگام نشده</summary>
    [Column("Last_Inbox_Uid")]
    public long LastInboxUid { get; set; }

    /// <summary>آخرین شناسه‌ی IMAP همگام‌شده از صندوق ارسالی — ۰ یعنی هنوز همگام نشده</summary>
    [Column("Last_Sent_Uid")]
    public long LastSentUid { get; set; }

    /// <summary>آخرین شناسه‌ی IMAP همگام‌شده از پوشه‌ی هرزنامه (اسپم) — ۰ یعنی هنوز همگام نشده</summary>
    [Column("Last_Junk_Uid")]
    public long LastJunkUid { get; set; }

    [ForeignKey(nameof(UserId))] public User? User { get; set; }

    public ICollection<OtoInboxEmail> InboxEmails { get; set; } = new List<OtoInboxEmail>();
    public ICollection<OtoSentEmail> SentEmails { get; set; } = new List<OtoSentEmail>();
}

/// <summary>
/// ایمیل‌های ارسالی — معادل SELECT * FROM [Otomasion].[dbo].[Oto_TBL_Sent]
/// SELECT [Sent_Id],[Email_Id],[UId],[To_Display],[Subject],[Date],
///        [Is_Neshan],[Is_Attachment],[Body],[IsInFolder]
/// </summary>
[Table("Oto_TBL_Sent")]
public class OtoSentEmail
{
    [Key]
    [Column("Sent_Id")]
    public int SentId { get; set; }

    /// <summary>حساب ایمیل فرستنده</summary>
    [Column("Email_Id")]
    public int EmailId { get; set; }

    /// <summary>شناسه یکتای پیام (Message-Id سرور)</summary>
    [Column("UId")]
    [MaxLength(300)]
    public string UId { get; set; } = "";

    /// <summary>گیرنده(ها) — با کاما</summary>
    [Column("To_Display")]
    [MaxLength(1000)]
    public string ToDisplay { get; set; } = "";

    [Column("Subject")]
    [MaxLength(500)]
    public string Subject { get; set; } = "";

    [Column("Date")]
    public DateTime Date { get; set; } = DateTime.Now;

    [Column("Is_Neshan")]
    public bool IsNeshan { get; set; }

    [Column("Is_Attachment")]
    public bool IsAttachment { get; set; }

    [Column("Body")]
    public string Body { get; set; } = "";

    /// <summary>شناسه پوشه بایگانی (Oto_TBl_EmailFolder) — 0 یعنی بدون پوشه</summary>
    [Column("IsInFolder")]
    public int IsInFolder { get; set; }

    /// <summary>نامه صادره مرتبط (اختیاری) — وقتی نامه از دبیرخانه ایمیل شده</summary>
    [Column("LetterSourceId")]
    public int? LetterSourceId { get; set; }

    [ForeignKey(nameof(EmailId))] public OtoEmail? Email { get; set; }
    public ICollection<OtoEmailAttachment> Attachments { get; set; } = new List<OtoEmailAttachment>();
}

/// <summary>
/// ایمیل‌های دریافتی — معادل SELECT * FROM [Otomasion].[dbo].[Oto_TBL_Inbox]
/// SELECT [Inbox_Id],[Email_Id],[UId],[Subject],[Date],[Is_Read],
///        [Is_Neshan],[Body],[Is_Attachment],[From_Address],[IsInFolder]
/// </summary>
[Table("Oto_TBL_Inbox")]
public class OtoInboxEmail
{
    [Key]
    [Column("Inbox_Id")]
    public int InboxId { get; set; }

    /// <summary>حساب ایمیل گیرنده</summary>
    [Column("Email_Id")]
    public int EmailId { get; set; }

    /// <summary>شناسه یکتای پیام روی سرور (UID ایمیل یا Message-Id) — جلوگیری از دریافت تکراری</summary>
    [Column("UId")]
    [MaxLength(300)]
    public string UId { get; set; } = "";

    [Column("Subject")]
    [MaxLength(500)]
    public string Subject { get; set; } = "";

    [Column("Date")]
    public DateTime Date { get; set; } = DateTime.Now;

    [Column("Is_Read")]
    public bool IsRead { get; set; }

    [Column("Is_Neshan")]
    public bool IsNeshan { get; set; }

    [Column("Body")]
    public string Body { get; set; } = "";

    [Column("Is_Attachment")]
    public bool IsAttachment { get; set; }

    [Column("From_Address")]
    [MaxLength(300)]
    public string FromAddress { get; set; } = "";

    /// <summary>شناسه پوشه بایگانی (Oto_TBl_EmailFolder) — 0 یعنی بدون پوشه</summary>
    [Column("IsInFolder")]
    public int IsInFolder { get; set; }

    /// <summary>
    /// true = هرزنامه (اسپم). پیام‌های اسپم در صندوقِ دریافتی نمایش داده نمی‌شوند
    /// و فقط در بخش «اسپم» دیده می‌شوند. دو راه دارند: همگام‌سازیِ پوشه‌ی
    /// Junk/Spam روی سرورِ IMAP، یا نشان‌کردنِ دستی توسط کاربر.
    /// </summary>
    [Column("Is_Spam")]
    public bool IsSpam { get; set; }

    [ForeignKey(nameof(EmailId))] public OtoEmail? Email { get; set; }
    public ICollection<OtoEmailAttachment> Attachments { get; set; } = new List<OtoEmailAttachment>();
}

/// <summary>
/// پوشه‌های بایگانی ایمیل — معادل SELECT * FROM [Otomasion].[dbo].[Oto_TBl_EmailFolder]
/// SELECT [EmailFolderId],[Title],[EmailId],[ParentId],[UserId],[SematId],
///        [IsFolder],[TypeEmail],[Uid],[EmailSetId]
/// رکوردهایی که IsFolder=true دارند پوشه‌اند؛ رکوردهای IsFolder=false لینک ایمیل به پوشه‌اند.
/// </summary>
[Table("Oto_TBl_EmailFolder")]
public class OtoEmailFolder
{
    [Key]
    [Column("EmailFolderId")]
    public int EmailFolderId { get; set; }

    [Column("Title")]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>حساب ایمیل مرتبط (برای پوشه‌ها) / حساب پیام (برای لینک‌ها)</summary>
    [Column("EmailId")]
    public int EmailId { get; set; }

    [Column("ParentId")]
    public int ParentId { get; set; }

    [Column("UserId")]
    public int UserId { get; set; }

    [Column("SematId")]
    public int? SematId { get; set; }

    /// <summary>true = پوشه | false = ایمیل بایگانی‌شده در پوشه</summary>
    [Column("IsFolder")]
    public bool IsFolder { get; set; }

    /// <summary>نوع ایمیل: 0=دریافتی، 1=ارسالی</summary>
    [Column("TypeEmail")]
    public int TypeEmail { get; set; }

    /// <summary>شناسه پیام (Inbox_Id یا Sent_Id) برای رکوردهای لینک</summary>
    [Column("Uid")]
    public int Uid { get; set; }

    [Column("EmailSetId")]
    public int? EmailSetId { get; set; }

    public bool IsDelete { get; set; }
}

/// <summary>
/// پیوست‌های ایمیل — معادل SELECT * FROM [Otomasion].[dbo].[Oto_TBL_EmailAttachments]
/// SELECT [Attachment_Id],[Email_id],[UId],[Type],[Attachment_Real_Name],[Attachment_Saved_Name]
/// فایل روی دیسک در wwwroot/uploads/فایل های ایمیل/{Sent|Inbox}_Id/ ذخیره می‌شود.
/// </summary>
[Table("Oto_TBL_EmailAttachments")]
public class OtoEmailAttachment
{
    [Key]
    [Column("Attachment_Id")]
    public int AttachmentId { get; set; }

    /// <summary>شناسه پیام (Sent_Id یا Inbox_Id بر اساس Type)</summary>
    [Column("Email_id")]
    public int EmailId { get; set; }

    [Column("UId")]
    [MaxLength(300)]
    public string UId { get; set; } = "";

    /// <summary>نوع پیام مبدأ: Sent | Inbox</summary>
    [Column("Type")]
    [MaxLength(20)]
    public string Type { get; set; } = "Sent";

    /// <summary>نام اصلی فایل</summary>
    [Column("Attachment_Real_Name")]
    [MaxLength(255)]
    public string AttachmentRealName { get; set; } = "";

    /// <summary>نام ذخیره‌شده روی دیسک</summary>
    [Column("Attachment_Saved_Name")]
    [MaxLength(255)]
    public string AttachmentSavedName { get; set; } = "";

    /// <summary>مسیر نسبی داخل uploads/ (اختیاری — برای دانلود امن از مسیر API)</summary>
    [Column("Attachment_FilePath")]
    [MaxLength(300)]
    public string? FilePath { get; set; }
}
