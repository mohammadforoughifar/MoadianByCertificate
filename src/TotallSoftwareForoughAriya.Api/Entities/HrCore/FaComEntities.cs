using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// ================== ارتباطات داخلی فروغ آریا (FaCom) — §۱۵، ماژول جدید و مستقل ==================
/// اطلاعیه‌های عمومی/واحدی + تیکت HR کارمندان + توزیع چندکاناله (سیستمی/ایمیل/پوش/پیامک) + رویدادهای خودکار.
/// جدول‌ها با پیشوند FaCom یکتا هستند. اسکیما با FaComSchemaV1 (Ensure) ساخته می‌شود (نه EF Migration).
/// کانال‌ها از زیرساخت موجود استفاده می‌کنند: INotifyService، IEmailService، IPushService.
/// </summary>

public enum FaComAudience { All = 0, Unit = 1 }
public enum FaComTicketStatus { New = 0, InProgress = 1, Answered = 2, Closed = 3 }
public enum FaComTicketCategory { Other = 0, Leave = 1, Payroll = 2, Insurance = 3, Contract = 4, Training = 5 }
public enum FaComSuggestionStatus { New = 0, Reviewing = 1, Accepted = 2, Rejected = 3, Done = 4 }

/// <summary>اطلاعیه HR — عمومی یا مخصوص یک واحد، با بازه انتشار اختیاری</summary>
public class FaComAnnouncement
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(4000)]
    public string Body { get; set; } = "";

    public FaComAudience Audience { get; set; } = FaComAudience.All;

    public int? OrgUnitId { get; set; }

    public DateTime? PublishFrom { get; set; }

    public DateTime? PublishTo { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>زمان ارسال اعلان انتشار زمان‌بندی‌شده (null = ارسال‌نشده)</summary>
    public DateTime? NotifiedAt { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>تیکت کارمند به منابع انسانی — با پیگیری وضعیت</summary>
public class FaComTicket
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(200)]
    public string Subject { get; set; } = "";

    [MaxLength(2000)]
    public string Body { get; set; } = "";

    public FaComTicketCategory Category { get; set; } = FaComTicketCategory.Other;

    /// <summary>اولویت: 0=کم، 1=متوسط، 2=زیاد</summary>
    public int Priority { get; set; } = 1;

    public FaComTicketStatus Status { get; set; } = FaComTicketStatus.New;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? ClosedAt { get; set; }

    [MaxLength(150)]
    public string? ClosedByName { get; set; }
}

/// <summary>پاسخ تیکت (کارمند یا HR)</summary>
public class FaComReply
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public int UserId { get; set; }

    [MaxLength(150)]
    public string UserName { get; set; } = "";

    [MaxLength(2000)]
    public string Body { get; set; } = "";

    public bool IsHrReply { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>نظرسنجی/رأی‌گیری (§۶)</summary>
public class FaComPoll
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(1000)]
    public string? Description { get; set; }

    public FaComAudience Audience { get; set; } = FaComAudience.All;

    public int? OrgUnitId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? CloseAt { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>گزینه نظرسنجی</summary>
public class FaComPollOption
{
    public int Id { get; set; }

    public int PollId { get; set; }

    [MaxLength(300)]
    public string Text { get; set; } = "";

    public int SortOrder { get; set; }
}

/// <summary>رأی کاربر — هر کاربر یک رأی در هر نظرسنجی (قابل تغییر)</summary>
public class FaComVote
{
    public int Id { get; set; }

    public int PollId { get; set; }

    public int OptionId { get; set; }

    public int UserId { get; set; }

    public DateTime VotedAt { get; set; } = DateTime.Now;
}

/// <summary>صندوق پیشنهادها — پیشنهاد/انتقاد پرسنل با پیگیری وضعیت (اختیار ناشناس)</summary>
public class FaComSuggestion
{
    public int Id { get; set; }

    public int? EmployeeId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(2000)]
    public string Body { get; set; } = "";

    /// <summary>دسته: 0=پیشنهاد، 1=انتقاد، 2=سایر</summary>
    public int Category { get; set; }

    public FaComSuggestionStatus Status { get; set; } = FaComSuggestionStatus.New;

    /// <summary>ناشناس: نام در کارتابل HR نمایش داده نمی‌شود</summary>
    public bool IsAnonymous { get; set; }

    [MaxLength(1000)]
    public string? Response { get; set; }

    [MaxLength(150)]
    public string? RespondedByName { get; set; }

    public DateTime? RespondedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
