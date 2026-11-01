using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Entities.Chat;

/// <summary>گفتگوی چت (خصوصی، گروهی، کانال)</summary>
public class ChatConversation
{
    [Key]
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public ChatTypeDto Type { get; set; } = ChatTypeDto.Direct;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? LastMessageSnippet { get; set; }

    public int? LastMessageSenderId { get; set; }

    [MaxLength(100)]
    public string? LastMessageSenderName { get; set; }

    public int? PinnedMessageId { get; set; }

    public bool IsArchived { get; set; }

    // ناوبری
    public ICollection<ChatMember> Members { get; set; } = new List<ChatMember>();
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

/// <summary>عضو در گفتگو</summary>
public class ChatMember
{
    [Key]
    public int Id { get; set; }

    public int ConversationId { get; set; }
    [ForeignKey(nameof(ConversationId))]
    public ChatConversation? Conversation { get; set; }

    public int UserId { get; set; }

    [MaxLength(100)]
    public string UserName { get; set; } = "";

    [MaxLength(150)]
    public string UserDisplayName { get; set; } = "";

    [MaxLength(500)]
    public string? UserAvatarUrl { get; set; }

    public ChatMemberRoleDto Role { get; set; } = ChatMemberRoleDto.Member;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public int LastReadMessageId { get; set; } = 0;

    public int UnreadCount { get; set; } = 0;

    public bool IsMuted { get; set; } = false;

    public bool IsPinned { get; set; } = false;

    public bool IsArchived { get; set; } = false;
}

/// <summary>پیام ارسال‌شده در گفتگو</summary>
public class ChatMessage
{
    [Key]
    public int Id { get; set; }

    public int ConversationId { get; set; }
    [ForeignKey(nameof(ConversationId))]
    public ChatConversation? Conversation { get; set; }

    public int SenderUserId { get; set; }

    [MaxLength(150)]
    public string SenderName { get; set; } = "";

    [MaxLength(500)]
    public string? SenderAvatarUrl { get; set; }

    public string? Text { get; set; }

    public ChatMessageTypeDto MessageType { get; set; } = ChatMessageTypeDto.Text;

    // پیوست فایل
    [MaxLength(500)]
    public string? FileUrl { get; set; }

    [MaxLength(250)]
    public string? FileName { get; set; }

    public long? FileSizeBytes { get; set; }

    [MaxLength(100)]
    public string? FileContentType { get; set; }

    // ---- تبدیل ویس به متن (رونویسی پیام صوتی) ----
    /// <summary>متنِ رونویسی‌شدهٔ پیام صوتی؛ خالی = هنوز تبدیل نشده.</summary>
    public string? Transcript { get; set; }

    /// <summary>زمان آخرین رونویسی (برای نمایش «آخرین تبدیل» و تبدیل دوباره).</summary>
    public DateTime? TranscribedAt { get; set; }

    // ریپلای و فوروارد
    public int? ReplyToMessageId { get; set; }

    [MaxLength(150)]
    public string? ReplyToSenderName { get; set; }

    [MaxLength(300)]
    public string? ReplyToSnippet { get; set; }

    public int? ForwardFromMessageId { get; set; }

    [MaxLength(150)]
    public string? ForwardFromSenderName { get; set; }

    // اتصال به ماژول‌های سازمانی ERP
    [MaxLength(50)]
    public string? ErpModule { get; set; }

    [MaxLength(50)]
    public string? ErpEntityId { get; set; }

    [MaxLength(200)]
    public string? ErpEntityTitle { get; set; }

    [MaxLength(500)]
    public string? ErpEntitySummary { get; set; }

    public bool IsEdited { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public bool IsPinned { get; set; }

    /// <summary>ذخیره واکنش‌ها به صورت JSON (e.g. {"👍": [{"UserId": 1, "UserName": "admin"}]})</summary>
    public string? ReactionsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
