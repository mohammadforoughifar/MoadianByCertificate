using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// لاگ عملیات نرم‌افزار — هر عملیاتِ ثبت/ویرایش/حذف/ورود به‌صورت خودکار ثبت می‌شود
/// (فیلتر سراسری AuditLogFilter). فقط مدیر از بخش «تنظیمات» به آن دسترسی دارد.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>زمان عملیات</summary>
    public DateTime At { get; set; } = DateTime.Now;

    /// <summary>شناسه کاربر (در ورود ناموفق = null)</summary>
    public int? UserId { get; set; }

    /// <summary>نام کاربری (در ورود ناموفق = نام تلاش‌شده)</summary>
    [MaxLength(100)]
    public string? Username { get; set; }

    /// <summary>ماژول/بخش (نام کنترلر — مثلاً Products، Attendance، Auth)</summary>
    [MaxLength(80)]
    public string Module { get; set; } = "";

    /// <summary>عملیات (نام اکشن — مثلاً Create، Update، Delete، CheckIn)</summary>
    [MaxLength(80)]
    public string Action { get; set; } = "";

    [MaxLength(10)]
    public string HttpMethod { get; set; } = "";

    [MaxLength(300)]
    public string? Path { get; set; }

    /// <summary>خلاصه‌ی عملیات (نام رکورد/شناسه) برای نمایش سریع در لیست</summary>
    [MaxLength(200)]
    public string? Summary { get; set; }

    /// <summary>بدنه‌ی درخواست (JSON — بدون مقادیر حساس مثل رمز عبور)</summary>
    [MaxLength(4000)]
    public string? Payload { get; set; }

    [MaxLength(64)]
    public string? Ip { get; set; }

    /// <summary>دستگاه/مرورگر (User-Agent کوتاه‌شده)</summary>
    [MaxLength(250)]
    public string? Device { get; set; }

    public int StatusCode { get; set; }

    /// <summary>مدت‌زمان اجرا (میلی‌ثانیه)</summary>
    public int DurationMs { get; set; }
}
