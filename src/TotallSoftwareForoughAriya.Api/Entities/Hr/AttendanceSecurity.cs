using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// دستگاه‌های مورد استفاده برای ثبت حضور و غیاب هر کاربر.
/// در مرورگر MAC واقعی در دسترس نیست؛ به‌جای آن یک «شناسه دستگاه» یکتا (Device ID)
/// در localStorage مرورگر ذخیره می‌شود و همراه با IP و User-Agent ثبت می‌گردد.
/// اولین دستگاهی که کاربر با آن ورود می‌زند، به‌عنوان «دستگاه اصلی» (IsPrimary) ثبت می‌شود.
/// اگر بعداً با دستگاه جدیدی ورود بزند، هشداری برای مدیر ثبت می‌شود و تا زمانی که
/// مدیر آن را تأیید نکند، دستگاه جدید IsApproved = false می‌ماند.
/// </summary>
public class UserDevice
{
    public int Id { get; set; }

    public int UserId { get; set; }
    [MaxLength(150)]
    public string UserName { get; set; } = "";

    /// <summary>شناسه یکتای دستگاه (Device ID ذخیره‌شده در localStorage مرورگر)</summary>
    [MaxLength(100)]
    public string DeviceId { get; set; } = "";

    /// <summary>آخرین IP که دستگاه از آن متصل شده</summary>
    [MaxLength(64)]
    public string? Ip { get; set; }

    /// <summary>User-Agent مرورگر دستگاه</summary>
    [MaxLength(250)]
    public string? UserAgent { get; set; }

    /// <summary>این دستگاه «دستگاه اصلی» کاربر است (مورد تأیید مدیر)</summary>
    public bool IsPrimary { get; set; }

    /// <summary>تأیید شده توسط مدیر (برای دستگاه‌های غیراول)</summary>
    public bool IsApproved { get; set; }

    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>اولین باری که دستگاه دیده شد</summary>
    public DateTime FirstSeenAt { get; set; } = DateTime.Now;

    /// <summary>آخرین باری که دستگاه استفاده شد</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.Now;

    /// <summary>تعداد دفعات استفاده</summary>
    public int UsedCount { get; set; }
}

/// <summary>
/// هشدارهای امنیتی حضور و غیاب برای مدیر:
/// NewDevice   — ورود کاربر با دستگاهی که قبلاً دیده نشده (نیاز به تأیید/رد مدیر)
/// SharedDevice— یک دستگاه توسط دو نفر مختلف استفاده شده است
/// OutOfRange  — ورود/خروج از خارج از محدوده‌ی مجاز مکانی (بیش از شعاع مجاز)
/// </summary>
public class AttendanceAlert
{
    public int Id { get; set; }

    public int UserId { get; set; }
    [MaxLength(150)]
    public string UserName { get; set; } = "";

    /// <summary>NewDevice | SharedDevice | OutOfRange</summary>
    [MaxLength(20)]
    public string AlertType { get; set; } = "";

    [MaxLength(400)]
    public string Message { get; set; } = "";

    [MaxLength(100)]
    public string? DeviceId { get; set; }
    [MaxLength(64)]
    public string? Ip { get; set; }

    /// <summary>مختصات موقعیت (در صورت ارسال)</summary>
    public double? Lat { get; set; }
    public double? Lng { get; set; }

    /// <summary>فاصله از مرکز مجاز (متر) — برای هشدار OutOfRange</summary>
    public double? DistanceMeters { get; set; }

    /// <summary>Pending | Approved | Rejected</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public int? HandledBy { get; set; }
    public DateTime? HandledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// تنظیمات محدوده‌ی مکانی مجاز برای ثبت حضور و غیاب.
/// مدیر سیستم مرکز (طول/عرض جغرافیایی) و شعاع مجاز را تعیین می‌کند (پیش‌فرض ۱ کیلومتر).
/// </summary>
public class AttendanceAreaSetting
{
    public int Id { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>شعاع مجاز به متر (پیش‌فرض: ۱۰۰۰ = یک کیلومتر)</summary>
    public double RadiusMeters { get; set; } = 1000;

    [MaxLength(250)]
    public string? LocationName { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public int? UpdatedBy { get; set; }
}
