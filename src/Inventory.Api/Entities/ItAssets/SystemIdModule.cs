using System.ComponentModel.DataAnnotations;
namespace Inventory.Api.Data;

/// <summary>تاریخچه‌ی کاربران یک سیستم — چه کسی قبلاً از این سیستم استفاده می‌کرده.</summary>
public class SystemInfoUserHistory
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    public int? UserId { get; set; }
    /// <summary>اسنپاشت نام در زمان اختصاص (حتی اگر کاربر بعداً حذف شود).</summary>
    [MaxLength(200)] public string UserName { get; set; } = "";
    [MaxLength(50)] public string? StaffNumber { get; set; }
    [MaxLength(200)] public string? CompanyName { get; set; }
    public DateTime FromAt { get; set; } = DateTime.Now;
    /// <summary>null = هنوز این کاربر استفاده می‌کند.</summary>
    public DateTime? ToAt { get; set; }
}

/// <summary>چک‌لیست تحویل دیجیتال سیستم با امضای دیجیتال (PNG data-url).</summary>
public class SystemHandover
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    [MaxLength(200)] public string FromUserName { get; set; } = "";
    public int? ToUserId { get; set; }
    [MaxLength(200)] public string ToUserName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>آیتم‌های چک‌لیست: [{key, fa, checked}]</summary>
    public string ChecklistJson { get; set; } = "[]";
    /// <summary>امضای دیجیتال — data-url PNG از بوم امضا.</summary>
    public string? SignatureDataUrl { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}

/// <summary>دستور از راه دور ارسالی به ایجنت سیستم (ری‌استارت، خاموش، قفل و...).</summary>
public class SystemRemoteCommand
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    /// <summary>Reboot | Shutdown | Lock</summary>
    [MaxLength(30)] public string Action { get; set; } = "";
    /// <summary>Pending | Completed | Failed</summary>
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(200)] public string? ByUserName { get; set; }
    public DateTime? CompletedAt { get; set; }
    [MaxLength(500)] public string? Result { get; set; }
}
