using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// شرکتِ مشتریِ خدمات IT — هر شرکتی که نسخه‌ی نرم‌افزار را روی سرور خودش اجرا می‌کند و
/// درخواست‌هایش را به سرور مرکزیِ ما می‌فرستد.
/// کاربران آن شرکت در این سرور «کاربر» نیستند؛ هویتشان با کلیدِ پایدارِ
/// <c>ItRequest.ExternalRequesterKey</c> و دسترسیِ شرکت با <c>ApiKeyHash</c> کنترل می‌شود.
/// </summary>
public class ItClientCompany
{
    public int Id { get; set; }

    /// <summary>کد یکتای شرکت — همان چیزی که نرم‌افزارِ شرکت در هدر می‌فرستد (مثلاً ACME)</summary>
    [MaxLength(50)]
    public string Code { get; set; } = "";

    [MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string? Phone { get; set; }

    /// <summary>هش کلید API — خودِ کلید هرگز ذخیره نمی‌شود (فقط یک‌بار هنگام ساخت/تعویض نمایش داده می‌شود)</summary>
    [MaxLength(100)]
    public string ApiKeyHash { get; set; } = "";

    /// <summary>۴ کاراکتر اول کلید — برای تشخیص کلیدِ اشتباه در لاگ</summary>
    [MaxLength(10)]
    public string? ApiKeyHint { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>سقف درخواست در ساعت (۰ = بدون سقف)</summary>
    public int HourlyLimit { get; set; } = 60;

    /// <summary>زمان آخرین ارسال موفق — برای پایشِ اتصال شرکت‌ها</summary>
    public DateTime? LastSeenAt { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? KeyRotatedAt { get; set; }
}

/// <summary>
/// وضعیت اتصالِ نصبِ «شعبه» به سرور مرکزی (فقط در نرم‌افزارِ شرکت‌های راه‌داده‌شده معنا دارد).
/// رکورد یکتا با Id=1.
/// </summary>
public class ItRemoteConnection
{
    public int Id { get; set; } = 1;

    /// <summary>آدرس سرور مرکزی — مثلاً https://it.forough.ir</summary>
    [MaxLength(300)]
    public string? ServerUrl { get; set; }

    /// <summary>کد شرکت — باید با کدی که سرور مرکزی تعریف کرده یکی باشد</summary>
    [MaxLength(50)]
    public string? CompanyCode { get; set; }

    [MaxLength(200)]
    public string? CompanyName { get; set; }

    /// <summary>کلید API دریافتی از سرور مرکزی (در این دیتابیسِ محلی نگه‌داری می‌شود)</summary>
    [MaxLength(100)]
    public string? ApiKey { get; set; }

    /// <summary>ارسال درخواست‌های این نصب به سرور مرکزی فعال باشد</summary>
    public bool Enabled { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// آینهٔ محلیِ درخواست‌هایی که این نصب به سرور مرکزی فرستاده است.
/// برخلاف localStorage مرورگر، با تغییر دستگاه از بین نمی‌رود و بین کاربران مشترک است.
/// </summary>
public class ItRemoteRequest
{
    public int Id { get; set; }

    /// <summary>شماره‌ای که سرور مرکزی برگردانده</summary>
    [MaxLength(30)]
    public string? RemoteNumber { get; set; }

    /// <summary>توکنِ محرمانه‌ی پیگیری — بدون آن شماره برای خواندن پاسخ کافی نیست</summary>
    [MaxLength(40)]
    public string? TrackToken { get; set; }

    /// <summary>شناسه‌ی یکتای ساخته‌شده در همین نصب — کلیدِ idempotency (تلاش مجدد = درخواست تکراری نمی‌سازد)</summary>
    [MaxLength(40)]
    public string ExternalId { get; set; } = "";

    /// <summary>کلید پایدار کاربرِ درخواست‌کننده در این نصب</summary>
    [MaxLength(40)]
    public string RequesterKey { get; set; } = "";

    /// <summary>کاربرِ محلی که درخواست را زده</summary>
    public int LocalUserId { get; set; }

    [MaxLength(150)]
    public string RequesterName { get; set; } = "";

    [MaxLength(250)]
    public string? SystemLabel { get; set; }

    [MaxLength(30)]
    public string RequestType { get; set; } = "Hardware";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(2000)]
    public string Description { get; set; } = "";

    /// <summary>وضعیت آخرین پاسخ سرور مرکزی: New | Assigned | ManagerApproved | Completed | Rejected</summary>
    [MaxLength(30)]
    public string Status { get; set; } = "New";

    [MaxLength(4000)]
    public string? FinalResponse { get; set; }

    public DateTime? ManagerApprovedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? SyncedAt { get; set; }

    /// <summary>خطای آخرین ارسال (اگر سرور مرکزی در دسترس نبوده — درخواست بعداً قابل تلاش مجدد است)</summary>
    [MaxLength(500)]
    public string? LastError { get; set; }
}

/// <summary>کلید پایدار هر کاربر در نصبِ شعبه — برای دیدن «درخواست‌های من» روی سرور مرکزی.</summary>
public class ItRemoteUserKey
{
    public int Id { get; set; }
    public int UserId { get; set; }

    [MaxLength(40)]
    public string Key { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
