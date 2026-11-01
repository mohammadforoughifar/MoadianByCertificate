using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// =====================================================================
// انتیتی‌های «هوش مصنوعی فروغ آریا» — دستیار مرکزی + نامه‌ها + منشی کارمندی
// جدول‌ها با AiSchemaV1.EnsureAsync خودکار ساخته می‌شوند (SQL Server و SQLite).
// =====================================================================

/// <summary>یک رشته گفتگوی کاربر با دستیار (در وب، بله یا پیام‌رسان داخلی).</summary>
public class AiConversation
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>کانال گفتگو: web | bale | messenger</summary>
    [MaxLength(20)]
    public string Channel { get; set; } = "web";

    [MaxLength(200)]
    public string Title { get; set; } = "گفتگوی جدید";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsArchived { get; set; }
}

/// <summary>یک پیام داخل گفتگو (کاربر یا دستیار).</summary>
public class AiMessage
{
    public int Id { get; set; }

    public int ConversationId { get; set; }

    /// <summary>user | assistant</summary>
    [MaxLength(20)]
    public string Role { get; set; } = "user";

    public string Content { get; set; } = "";

    /// <summary>نام ابزارهایی که برای این پاسخ استفاده شد (با کاما) — برای شفافیت و دیباگ.</summary>
    [MaxLength(500)]
    public string? ToolsUsed { get; set; }

    /// <summary>آیا پاسخ در حالت آفلاین (بدون مدل زبانی) تولید شده؟</summary>
    public bool UsedFallback { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>مقاله راهنمای سیستم برای جستجوی معنایی دستیار (راهنمای هوشمند — قابلیت ۵).</summary>
public class AiKnowledgeDoc
{
    public int Id { get; set; }

    /// <summary>دسته: مثلا «فروش»، «مرخصی»، «نامه‌ها».</summary>
    [MaxLength(100)]
    public string Category { get; set; } = "";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public string Content { get; set; } = "";

    /// <summary>لینک صفحه مرتبط داخل نرم‌افزار (مثلا /fa-att/my-leaves) — می‌تواند خالی باشد.</summary>
    [MaxLength(300)]
    public string? Link { get; set; }

    /// <summary>کلید یکتا برای سید مجدد بدون تکرار (مثلا guide:leave-request).</summary>
    [MaxLength(100)]
    public string DocKey { get; set; } = "";

    /// <summary>بردار امبدینگ به‌صورت JSON (برای جستجوی معنایی) — تنبل و خودکار پر می‌شود.</summary>
    public string? EmbeddingJson { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// پیش‌فاکتور «اقدام با تأیید»: دستیار اقدام را پیشنهاد می‌دهد و فقط بعد از
/// تأیید صریح همان کاربر اجرا می‌شود (مثلاً ثبت مرخصی یا ساعت‌زنی).
/// </summary>
public class AiPendingAction
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>نوع اقدام: request_leave | clock | request_mission | decide_leave | answer_referral | create_ticket | report_work | create_letter_draft</summary>
    [MaxLength(40)]
    public string Action { get; set; } = "";

    /// <summary>آرگومان‌های اقدام به‌صورت JSON.</summary>
    public string ArgsJson { get; set; } = "{}";

    /// <summary>خلاصه فارسی برای نمایش به کاربر هنگام تأیید.</summary>
    public string Summary { get; set; } = "";

    /// <summary>0=در انتظار، 1=اجرا شده، 2=لغو/منقضی شده.</summary>
    public int Status { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(15);

    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>نتیجه اجرا یا دلیل لغو.</summary>
    public string? ResultText { get; set; }
}

/// <summary>یادآور شخصی کاربر (§۲۰) — ارسال سر وقت با پیام‌رسان (بله/ایتا).</summary>
public class AiReminder
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [MaxLength(500)]
    public string Text { get; set; } = "";

    /// <summary>زمان یادآوری به وقت محلی سرور.</summary>
    public DateTime RemindAt { get; set; }

    /// <summary>تکرار: 0=یکبار، 1=روزانه، 2=هفتگی.</summary>
    public int Recurrence { get; set; }

    public bool IsSent { get; set; }

    public int Attempts { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? SentAt { get; set; }
}

/// <summary>گزارش زمان‌بندی‌شده کاربر (§۲۱) — اجرای دوره‌ای یک خلاصه BI یا کاوش + ارسال در پیام‌رسان.</summary>
public class AiReportSchedule
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [MaxLength(300)]
    public string Title { get; set; } = "";

    /// <summary>نوع: bi (خلاصه آماده) | explore (کاوش آزاد).</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = "explore";

    /// <summary>مشخصات اجرا (JSON): برای bi {report,args} و برای explore همان AiExploreRequest.</summary>
    public string SpecJson { get; set; } = "{}";

    /// <summary>دوره: daily | weekly | monthly.</summary>
    [MaxLength(20)]
    public string ScheduleType { get; set; } = "weekly";

    /// <summary>هفتگی: DayOfWeek (شنبه=6)؛ ماهانه: روز ماه 1..31 یا 0=آخر ماه؛ روزانه نادیده.</summary>
    public int Day { get; set; }

    /// <summary>ساعت اجرا HH:mm.</summary>
    [MaxLength(5)]
    public string Time { get; set; } = "08:00";

    public bool WantExcel { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime NextRunAt { get; set; }

    public DateTime? LastRunAt { get; set; }

    public int FailCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>بردار معنایی سند برای جستجوی معنایی (§۲۲) — نامه‌ها و تیکت‌ها.</summary>
public class AiDocEmbedding
{
    public int Id { get; set; }

    /// <summary>نوع سند: inner_letter | incoming_letter | outgoing_letter | ticket</summary>
    [MaxLength(20)] public string DocType { get; set; } = "";

    public int DocId { get; set; }

    /// <summary>هش متن ایندکس‌شده؛ تغییر سند را لو می‌دهد.</summary>
    [MaxLength(16)] public string TextHash { get; set; } = "";

    /// <summary>بردار float32 به‌صورت JSON (الگوی AiKnowledgeDoc).</summary>
    public string? VectorJson { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>قانون هشدار شرطی (§۲۳): یک پرس‌وجوی کاوش + آستانه؛ در لبه صعودی خبر می‌دهد.</summary>
public class AiAlertRule
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [MaxLength(300)] public string Title { get; set; } = "";

    /// <summary>پرس‌وجوی کاوش (AiExploreRequest به‌صورت JSON).</summary>
    public string QueryJson { get; set; } = "";

    /// <summary>تجمیع: count | sum | avg</summary>
    [MaxLength(10)] public string Agg { get; set; } = "count";

    /// <summary>فیلد تجمیع برای sum/avg (نام انگلیسی فیلد کاتالوگ).</summary>
    [MaxLength(100)] public string? AggField { get; set; }

    /// <summary>عملگر: gt | lt | gte | lte | eq</summary>
    [MaxLength(10)] public string Op { get; set; } = "gt";

    public double Value { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>وضعیت آخرین ارزیابی (برای تحریک لبه‌ای: فقط گذار false→true خبر می‌دهد).</summary>
    public bool LastState { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    public DateTime? LastFiredAt { get; set; }

    public int FailCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>امتیاز کاربر به یک پاسخ دستیار (§۲۴).</summary>
public class AiFeedback
{
    public int Id { get; set; }

    public int MessageId { get; set; }

    public int ConversationId { get; set; }

    public int UserId { get; set; }

    /// <summary>امتیاز: +1 پسندیدم / -1 نپسندیدم.</summary>
    public int Rating { get; set; }

    /// <summary>دلیل نارضایتی (یکی از گزینه‌های ثابت) — فقط برای ۱-.</summary>
    [MaxLength(30)] public string? Reason { get; set; }

    [MaxLength(500)] public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }
}

/// <summary>ردپای حسابرسی هر نوبت پاسخ دستیار (§۲۵).</summary>
public class AiAuditLog
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int ConversationId { get; set; }

    /// <summary>کانال: web | bale | messenger</summary>
    [MaxLength(20)] public string Channel { get; set; } = "web";

    /// <summary>خلاصه پیام کاربر (حداکثر ۵۰۰ نویسه).</summary>
    [MaxLength(500)] public string UserMessage { get; set; } = "";

    /// <summary>ابزارهای استفاده‌شده با کاما (خالی = پاسخ مستقیم مدل؛ offline = حالت آفلاین).</summary>
    [MaxLength(500)] public string? ToolsUsed { get; set; }

    public bool Success { get; set; } = true;

    public bool UsedFallback { get; set; }

    [MaxLength(300)] public string? Error { get; set; }

    public int DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
