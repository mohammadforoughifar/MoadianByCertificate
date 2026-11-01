using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Entities.Chat;

/// <summary>فایل خصوصی چت؛ نام و نوع فایل از سرور خوانده می‌شود، نه از درخواست ارسال پیام.</summary>
public class ChatAttachment
{
    public Guid Id { get; set; }
    public int UploadedByUserId { get; set; }
    public int? ConversationId { get; set; }
    [MaxLength(250)] public string FileName { get; set; } = "";
    [MaxLength(100)] public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
