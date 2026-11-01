using System;
using System.Collections.Generic;

namespace Inventory.Shared.Dtos;

/// <summary>نوع گفتگو</summary>
public enum ChatTypeDto
{
    Direct = 1,   // گفتگوی خصوصی دونفره
    Group = 2,    // گروه کاری سازمانی
    Channel = 3   // کانال اطلاع‌رسانی
}

/// <summary>نوع پیام</summary>
public enum ChatMessageTypeDto
{
    Text = 1,
    Image = 2,
    File = 3,
    Audio = 4,
    Video = 5,
    System = 6,
    ErpShare = 7
}

/// <summary>نقش عضو در گروه</summary>
public enum ChatMemberRoleDto
{
    Owner = 1,
    Admin = 2,
    Member = 3
}

/// <summary>خلاصه گفتگوی چت (برای لیست دیالوگ‌ها)</summary>
public class ChatConversationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public ChatTypeDto Type { get; set; }
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
    public string? LastMessageSnippet { get; set; }
    public int? LastMessageSenderId { get; set; }
    public string? LastMessageSenderName { get; set; }
    public int? PinnedMessageId { get; set; }
    public string? PinnedMessageSnippet { get; set; }

    // وضعیت اختصاصی کاربر جاری در این گفتگو
    public int UnreadCount { get; set; }
    public bool IsPinned { get; set; }
    public bool IsMuted { get; set; }
    public bool IsArchived { get; set; }
    public int LastReadMessageId { get; set; }
    public ChatMemberRoleDto MyRole { get; set; }

    // اطلاعات مخاطب مقابل در چت خصوصی
    public int? DirectPeerUserId { get; set; }
    public string? DirectPeerName { get; set; }
    public string? DirectPeerAvatarUrl { get; set; }
    public bool DirectPeerIsOnline { get; set; }
    public DateTime? DirectPeerLastSeen { get; set; }

    // تعداد اعضا در گروه‌ها
    public int MembersCount { get; set; }
}

/// <summary>یک پیام در گفتگو</summary>
public class ChatMessageDto
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public int SenderUserId { get; set; }
    public string SenderName { get; set; } = "";
    public string? SenderAvatarUrl { get; set; }
    public string? Text { get; set; }
    public ChatMessageTypeDto MessageType { get; set; } = ChatMessageTypeDto.Text;

    // پیوست فایل/رسانه
    public string? FileUrl { get; set; }
    public string? FileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? FileContentType { get; set; }

    // تبدیل ویس به متن (فقط برای پیام‌های صوتی پر می‌شود)
    public string? Transcript { get; set; }
    public DateTime? TranscribedAt { get; set; }

    // ریپلای و فوروارد
    public int? ReplyToMessageId { get; set; }
    public string? ReplyToSenderName { get; set; }
    public string? ReplyToSnippet { get; set; }
    public int? ForwardFromMessageId { get; set; }
    public string? ForwardFromSenderName { get; set; }

    // کارت اشتراک سند/رکورد ERP
    public string? ErpModule { get; set; }
    public string? ErpEntityId { get; set; }
    public string? ErpEntityTitle { get; set; }
    public string? ErpEntitySummary { get; set; }

    public bool IsEdited { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsPinned { get; set; }

    // واکنش‌ها (ایموجی -> لیست کاربران)
    public Dictionary<string, List<ChatReactionUserDto>> Reactions { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public bool IsOutgoing { get; set; } // آیا فرستنده کاربر جاری است؟
    public bool IsReadByPeer { get; set; } // آیا توسط مخاطب خوانده شده است؟

    // یک رویداد مشترک نباید پرچم فرستنده را به همهٔ گیرندگان تحمیل کند.
    public ChatMessageDto ForUser(int userId)
    {
        var copy = (ChatMessageDto)MemberwiseClone();
        copy.IsOutgoing = SenderUserId == userId;
        copy.IsReadByPeer = copy.IsOutgoing && IsReadByPeer;
        return copy;
    }
}

/// <summary>اطلاعات کاربری که به پیام واکنش داده</summary>
public class ChatReactionUserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
}

/// <summary>عضو گروه چت</summary>
public class ChatMemberDto
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public string? UserAvatarUrl { get; set; }
    public ChatMemberRoleDto Role { get; set; }
    public DateTime JoinedAt { get; set; }
    public bool IsOnline { get; set; }
    public DateTime? LastSeen { get; set; }
}

/// <summary>کاربران نرم‌افزار برای شروع گفتگو</summary>
public class ChatUserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? Department { get; set; }
    public string? Role { get; set; }
    public bool IsOnline { get; set; }
    public DateTime? LastSeen { get; set; }
    public int? ExistingConversationId { get; set; }
}

/// <summary>درخواست ارسال پیام</summary>
public class SendChatMessageRequest
{
    public int ConversationId { get; set; }
    public string? Text { get; set; }
    public ChatMessageTypeDto MessageType { get; set; } = ChatMessageTypeDto.Text;
    public string? FileUrl { get; set; }
    public string? FileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? FileContentType { get; set; }
    public int? ReplyToMessageId { get; set; }
    public int? ForwardFromMessageId { get; set; }

    // ارجاع ERP
    public string? ErpModule { get; set; }
    public string? ErpEntityId { get; set; }
    public string? ErpEntityTitle { get; set; }
    public string? ErpEntitySummary { get; set; }
}

/// <summary>درخواست فوروارد پیام به گفتگویی که کاربر عضو آن است</summary>
public class ForwardChatMessageRequest
{
    public int ConversationId { get; set; }
}

/// <summary>درخواست شروع گفتگوی خصوصی</summary>
public class CreateDirectChatRequest
{
    public int TargetUserId { get; set; }
}

/// <summary>درخواست ساخت گروه جدید</summary>
public class CreateGroupChatRequest
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public ChatTypeDto Type { get; set; } = ChatTypeDto.Group;
    public List<int> MemberUserIds { get; set; } = new();
}

/// <summary>درخواست ویرایش پیام</summary>
public class EditChatMessageRequest
{
    public string NewText { get; set; } = "";
}

/// <summary>درخواست ثبت واکنش ایموجی</summary>
public class ReactChatMessageRequest
{
    public string Emoji { get; set; } = "";
}

/// <summary>خلاصه اعلان‌های چت برای هدر نرم‌افزار</summary>
public class ChatSummaryDto
{
    public int TotalUnreadMessages { get; set; }
    public int UnreadConversationsCount { get; set; }
}

/// <summary>آخرین پیامی که واقعاً در کلاینت نمایش داده شده؛ پیام جدیدتر نخوانده می‌ماند.</summary>
public class MarkChatReadRequest
{
    public int? LastReadMessageId { get; set; }
}

public class ChatUploadResultDto
{
    public Guid Id { get; set; }
    public string FileUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string FileContentType { get; set; } = "";
    public ChatMessageTypeDto MessageType { get; set; } = ChatMessageTypeDto.File;
}

/// <summary>نتیجهٔ «تبدیل ویس به متن» برای یک پیام صوتی.</summary>
public class ChatTranscriptResultDto
{
    public int MessageId { get; set; }
    public string Transcript { get; set; } = "";
    public DateTime TranscribedAt { get; set; }
    /// <summary>اگر متن قبلاً وجود داشت و از دیتابیس برگشت (بدون تماس دوباره با مدل).</summary>
    public bool FromCache { get; set; }
}

public static class ChatFileLimits
{
    public const long MaxFileBytes = 50L * 1024 * 1024;
    // فضای اضافه برای سربرگ multipart؛ محدودیت خود فایل همچنان ۵۰ MiB است.
    public const long MaxRequestBytes = MaxFileBytes + 1024 * 1024;
}
