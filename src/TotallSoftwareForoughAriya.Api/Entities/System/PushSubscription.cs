using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// اشتراک Web Push مرورگر/دستگاه کاربر — برای ارسال نوتیفیکیشن گوشی/تبلت
/// حتی وقتی کاربر داخل برنامه نیست (شبیه نوتیف گوشی که بالای صفحه می‌آید و می‌ماند).
/// هر کاربر می‌تواند چند اشتراک داشته باشد (چند مرورگر/دستگاه).
/// </summary>
public class PushSubscription
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>Endpoint اختصاصی Push Service (FCM/APNs/…)</summary>
    [MaxLength(500)]
    public string Endpoint { get; set; } = "";

    /// <summary>کلید عمومی P-256 (base64url) برای رمزنگاری payload</summary>
    [MaxLength(200)]
    public string P256DH { get; set; } = "";

    /// <summary>کلید Auth (base64url) برای رمزنگاری payload</summary>
    [MaxLength(100)]
    public string Auth { get; set; } = "";

    /// <summary>نام مرورگر/دستگاه (User-Agent)</summary>
    [MaxLength(250)]
    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastSeenAt { get; set; } = DateTime.Now;
}