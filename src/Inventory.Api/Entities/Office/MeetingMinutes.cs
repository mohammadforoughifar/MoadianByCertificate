using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// وضعیت‌های صورتجلسه:
/// InReview = «در حال بررسی» (وضعیت عادی پس از ثبت)
/// Closed   = «اتمام نهایی / بسته» (وقتی همهٔ بندها «انجام شد» شوند)
/// </summary>
public static class MeetingMinutesStatus
{
    public const string InReview = "InReview";
    public const string Closed = "Closed";

    public static string ToFa(string? s) => (s ?? InReview) switch
    {
        Closed => "اتمام نهایی",
        _ => "در حال بررسی"
    };
}

/// <summary>وضعیت‌های تک‌تک بندها (آیتم‌ها) صورتجلسه.</summary>
public static class MinutesItemStatus
{
    public const string InProgress = "InProgress"; // در جریان
    public const string Done = "Done";             // انجام شد (مسئول + مسئول پیگیری هر دو تایید کرده‌اند)
    public const string Rejected = "Rejected";     // رد شده (مسئول پیگیری رد کرده)

    public static string ToFa(string? s) => (s ?? InProgress) switch
    {
        Done => "انجام شد",
        Rejected => "رد شده",
        _ => "در جریان"
    };
}

/// <summary>نوع حضور در صورتجلسه.</summary>
public static class MinutesParticipantKind
{
    public const string Attendee = "Attendee"; // حاضر
    public const string Absent = "Absent";     // غایب

    public static string ToFa(string? s) => (s ?? Attendee) == Absent ? "غایب" : "حاضر";
}

/// <summary>صورتجلسه — زیر ماژول «فرم‌های متفرقه».</summary>
public class MeetingMinutes
{
    public int Id { get; set; }

    /// <summary>شرکت فعال مالک صورتجلسه</summary>
    public int? CompanyId { get; set; }

    /// <summary>عنوان صورتجلسه</summary>
    [MaxLength(300)]
    public string Title { get; set; } = "";

    /// <summary>تاریخ جلسه (نمایش شمسی، ذخیره میلادی)</summary>
    public DateTime MeetingDate { get; set; }

    /// <summary>تاریخ ثبت — به‌صورت خودکار در لحظهٔ ایجاد ست می‌شود</summary>
    public DateTime DateRegistered { get; set; } = DateTime.Now;

    /// <summary>وضعیت کلی: InReview (در حال بررسی) | Closed (اتمام نهایی)</summary>
    [MaxLength(20)]
    public string Status { get; set; } = MeetingMinutesStatus.InReview;

    /// <summary>زمان بسته‌شدن (وقتی همهٔ بندها انجام شد شدند)</summary>
    public DateTime? ClosedAt { get; set; }

    public int CreatedByUserId { get; set; }
    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }

    public List<MeetingMinutesParticipant> Participants { get; set; } = new();
    public List<MeetingMinutesItem> Items { get; set; } = new();
}

/// <summary>
/// شرکت‌کنندهٔ صورتجلسه (حاضر یا غایب).
/// حاضران می‌توانند امضای الکترونیکی روی صورتجلسه بزنند.
/// </summary>
public class MeetingMinutesParticipant
{
    public int Id { get; set; }

    public int MinutesId { get; set; }

    public int UserId { get; set; }
    [MaxLength(150)]
    public string Name { get; set; } = "";

    /// <summary>Attendee = حاضر | Absent = غایب</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = MinutesParticipantKind.Attendee;

    /// <summary>امضای الکترونیکی — تصویر PNG به‌صورت base64 (بدون پیشوند data:).</summary>
    public string? SignatureData { get; set; }

    public DateTime? SignedAt { get; set; }

    /// <summary>آیا این نفر در گردش (ارسال) به او اطلاع‌رسانی شده است؟</summary>
    public bool Notified { get; set; }

    public MeetingMinutes? Minutes { get; set; }
}

/// <summary>
/// بند/آیتم صورتجلسه — هر بند یک تصمیم/کار مورد بحث با:
/// شرح، تاریخ انجام، مسئول، تاریخ پیگیری، مسئول پیگیری.
/// مسئول پیگیری می‌تواند تایید یا رد کند و شرح بدهد.
/// وضعیت بند «انجام شد» فقط وقتی می‌شود که هم مسئول و هم مسئول پیگیری تایید کرده باشند.
/// </summary>
public class MeetingMinutesItem
{
    public int Id { get; set; }

    public int MinutesId { get; set; }

    /// <summary>شماره ترتیبی بند داخل صورتجلسه</summary>
    public int RowNo { get; set; }

    /// <summary>شرح بند / تصمیم گرفته‌شده</summary>
    public string Description { get; set; } = "";

    /// <summary>تاریخ انجام (مهلت اجرای بند)</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>مسئول اجرای بند</summary>
    public int? ResponsibleUserId { get; set; }
    [MaxLength(150)]
    public string? ResponsibleName { get; set; }

    /// <summary>تاریخ پیگیری</summary>
    public DateTime? FollowUpDate { get; set; }

    /// <summary>مسئول پیگیری بند — حق تایید/رد با شرح را دارد</summary>
    public int? FollowUpUserId { get; set; }
    [MaxLength(150)]
    public string? FollowUpName { get; set; }

    /// <summary>وضعیت بند: InProgress | Done | Rejected</summary>
    [MaxLength(20)]
    public string ItemStatus { get; set; } = MinutesItemStatus.InProgress;

    // ============ تصمیم مسئول اجرا ============
    public bool RespApproved { get; set; }
    public DateTime? RespApprovedAt { get; set; }
    [MaxLength(500)]
    public string? RespNote { get; set; }

    // ============ تصمیم مسئول پیگیری (تایید/رد + شرح) ============
    /// <summary>null = تصمیم نگرفته | Approved = تایید | Rejected = رد</summary>
    [MaxLength(20)]
    public string? FollowUpDecision { get; set; }
    public DateTime? FollowUpDecidedAt { get; set; }
    [MaxLength(500)]
    public string? FollowUpNote { get; set; }

    public MeetingMinutes? Minutes { get; set; }
}
