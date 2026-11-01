using System.ComponentModel.DataAnnotations;

namespace Inventory.Shared.Dtos;

// ============================================================
//  صورتجلسه — DTOهای مشترک API و کلاینت
// ============================================================

/// <summary>فهرست صورتجلسه‌ها (لیست کارتابل)</summary>
public class MinutesListItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    /// <summary>تاریخ جلسه — میلادی (کلاینت به شمسی تبدیل می‌کند)</summary>
    public DateTime MeetingDate { get; set; }

    public DateTime DateRegistered { get; set; }

    /// <summary>InReview | Closed</summary>
    public string Status { get; set; } = "";
    public DateTime? ClosedAt { get; set; }

    public int CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";

    public int AttendeeCount { get; set; }
    public int AbsentCount { get; set; }
    public int ItemCount { get; set; }
    public int ItemDoneCount { get; set; }

    /// <summary>تعداد پیوست‌ها</summary>
    public int AttachmentCount { get; set; }
}

/// <summary>جزئیات کامل صورتجلسه</summary>
public class MinutesDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime MeetingDate { get; set; }
    public DateTime DateRegistered { get; set; }
    public string Status { get; set; } = "";
    public DateTime? ClosedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";

    public List<MinutesParticipantDto> Participants { get; set; } = new();
    public List<MinutesItemDto> Items { get; set; } = new();
}

public class MinutesParticipantDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Attendee | Absent</summary>
    public string Kind { get; set; } = "";

    /// <summary>base64 تصویر امضا (null = امضا نشده)</summary>
    public string? SignatureData { get; set; }
    public DateTime? SignedAt { get; set; }
}

public class MinutesItemDto
{
    public int Id { get; set; }
    public int RowNo { get; set; }
    public string Description { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public int? ResponsibleUserId { get; set; }
    public string? ResponsibleName { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public int? FollowUpUserId { get; set; }
    public string? FollowUpName { get; set; }

    /// <summary>InProgress | Done | Rejected</summary>
    public string ItemStatus { get; set; } = "";

    public bool RespApproved { get; set; }
    public DateTime? RespApprovedAt { get; set; }
    public string? RespNote { get; set; }

    public string? FollowUpDecision { get; set; }
    public DateTime? FollowUpDecidedAt { get; set; }
    public string? FollowUpNote { get; set; }
}

/// <summary>دریافت/ویرایش بند — هم برای ساخت و هم ویرایش استفاده می‌شود.</summary>
public class SaveMinutesItemDto
{
    /// <summary>0 = بند جدید، غیرصفر = ویرایش بند موجود</summary>
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public int? ResponsibleUserId { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public int? FollowUpUserId { get; set; }
}

/// <summary>ساخت/ویرایش صورتجلسه</summary>
public class SaveMinutesDto
{
    /// <summary>0 = جدید، غیرصفر = ویرایش</summary>
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = "";

    /// <summary>تاریخ جلسه — میلادی (کلاینت از شمسی می‌فرستد)</summary>
    public DateTime MeetingDate { get; set; }

    /// <summary>حاضرین (شناسه کاربران)</summary>
    public List<int> AttendeeUserIds { get; set; } = new();

    /// <summary>غایبین (شناسه کاربران)</summary>
    public List<int> AbsentUserIds { get; set; } = new();

    /// <summary>بندها</summary>
    public List<SaveMinutesItemDto> Items { get; set; } = new();
}

/// <summary>
/// ثبت نهایی/ارسال صورتجلسه به گردش —
/// حاضرین همیشه اطلاع‌رسانی می‌شوند؛ غایبین فقط با اذین کاربر (IncludeAbsentees).
/// </summary>
public class SubmitMinutesDto
{
    /// <summary>آیا به غایبین هم اطلاع‌رسانی ارسال شود؟</summary>
    public bool IncludeAbsentees { get; set; } = true;
}

/// <summary>تصمیم مسئول اجرا (تایید انجام بند)</summary>
public class MinutesRespDecisionDto
{
    public bool Approved { get; set; } = true;
    [MaxLength(500)]
    public string? Note { get; set; }
}

/// <summary>تصمیم مسئول پیگیری — تایید یا رد + شرح</summary>
public class MinutesFollowUpDecisionDto
{
    /// <summary>Approved | Rejected</summary>
    public string Decision { get; set; } = "Approved";
    [MaxLength(500)]
    public string? Note { get; set; }
}

/// <summary>امضای الکترونیکی حاضر روی صورتجلسه</summary>
public class MinutesSignDto
{
    /// <summary>تصویر امضا — base64 PNG (پیشوند data: انتخابی)</summary>
    public string SignatureBase64 { get; set; } = "";
}

/// <summary>تبدیل صورتجلسه (یا بند) به نامه داخلی</summary>
public class MinutesToInnerLetterDto
{
    /// <summary>0 = کل صورتجلسه، غیرصفر = فقط یک بند</summary>
    public int ItemId { get; set; }
    public string Title { get; set; } = "";
    public string? Text { get; set; }

    /// <summary>گیرندگان اصلی</summary>
    public List<int> ReciversGirande { get; set; } = new();
    /// <summary>ارجاع (جهت اقدام)</summary>
    public List<int> ReciversErja { get; set; } = new();
    /// <summary>هامش (رونوشت)</summary>
    public List<int> ReciversHamesh { get; set; } = new();
}

/// <summary>تبدیل صورتجلسه به نامه صادره</summary>
public class MinutesToOutgoingLetterDto
{
    public string Title { get; set; } = "";
    public string? Text { get; set; }

    [Required]
    public string ReceiverOrganization { get; set; } = "";
    public string? ReceiverName { get; set; }
    public string? ReceiverTitle { get; set; }
    public string? CopyTo { get; set; }
}

/// <summary>ارسال مستقیم صورتجلسه با ایمیل سازمانی</summary>
public class MinutesSendEmailDto
{
    public int EmailAccountId { get; set; }

    /// <summary>مقصد — ایمیلهای جداشده با ویرگول</summary>
    public string To { get; set; } = "";
    public string? Cc { get; set; }
    public string Subject { get; set; } = "";
    public string? Body { get; set; }
}

/// <summary>آیتم سبک کاربر (شناسه/نام) برای کمبوهای حاضرین/مسئول/پیگیری صورتجلسه —
/// جایگزین امن برای api/users که نیازمند ماژول مدیریت کاربران است.</summary>
public class UserLookupItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
