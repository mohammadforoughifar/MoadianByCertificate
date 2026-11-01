using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;
public class User
{
    public int Id { get; set; }
    [MaxLength(100)] public string Username { get; set; } = "";
    [MaxLength(200)] public string PasswordHash { get; set; } = "";
    [MaxLength(20)] public string Role { get; set; } = "Admin";
    public int? ReferrerId { get; set; }

    /// <summary>نام</summary>
    [MaxLength(100)] public string? FirstName { get; set; }

    /// <summary>نام خانوادگی</summary>
    [MaxLength(100)] public string? LastName { get; set; }

    /// <summary>شماره موبایل — برای ارسال پیام بله/ایتا</summary>
    [MaxLength(20)] public string? Mobile { get; set; }

    /// <summary>آیا شماره موبایل کاربر با کد یک‌بارمصرف تأیید شده است؟</summary>
    public bool MobileVerified { get; set; }

    /// <summary>ایمیل اختیاری کاربر؛ فقط پس از تأیید OTP ثبت می‌شود.</summary>
    [MaxLength(254)] public string? Email { get; set; }

    /// <summary>آیا ایمیل کاربر با کد یک‌بارمصرف تأیید شده است؟</summary>
    public bool EmailVerified { get; set; }

    /// <summary>شناسه چت کاربر در پیام‌رسان بله (بعد از استارت ربات)</summary>
    [MaxLength(50)] public string? BaleChatId { get; set; }

    /// <summary>شناسه چت کاربر در پیام‌رسان ایتا</summary>
    [MaxLength(50)] public string? EitaaChatId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>مسیر نسبی عکس داخل uploads/ (فقط اطلاعات — فایل روی دیسک است). null = عکس پیش‌فرض</summary>
    [MaxLength(200)]
    public string? PhotoPath { get; set; }

    /// <summary>مسیر نسبی تصویر امضای کاربر در uploads/UserSignatures/{id}/</summary>
    [MaxLength(300)]
    public string? SignaturePath { get; set; }

    /// <summary>
    /// واحد سازمانیِ کاربر (جدول Organizations) — برای جزء «واحد» در شمارهٔ نامه.
    /// ستون به‌صورت خودتعمیر (DbInitializer) به دیتابیس‌های موجود اضافه می‌شود؛
    /// رابطهٔ EF عمداً تعریف نشده تا حذف یک واحد، کاربر را تحت تأثیر قرار ندهد.
    /// </summary>
    public int? OrganizationId { get; set; }

    /// <summary>شیفت پیش‌فرض پرسنل (می‌تواند null باشد)</summary>
    public int? ShiftGroupId { get; set; }
    public ShiftGroup? ShiftGroup { get; set; }
}