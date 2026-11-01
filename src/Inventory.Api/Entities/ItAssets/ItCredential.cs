using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

/// <summary>
/// گاوصندوق رمزهای تجهیزات (مدیریت سخت‌افزار): سرور، سوییچ منیجبل، میکروتیک، فایروال، سامانه‌ها و ...
/// به تفکیک شرکت. رمز عبور و «اطلاعات محرمانهٔ تکمیلی» هرگز به‌صورت متن ساده ذخیره نمی‌شوند؛
/// با AES-256-GCM و کلید اصلی گاوصندوق (خارج از دیتابیس) رمزنگاری می‌شوند — <see cref="Services.ItAssets.CredentialVaultCrypto"/>.
/// </summary>
[Table("ItCredentials")]
public class ItCredential
{
    public int Id { get; set; }

    /// <summary>شرکت مالک (SystemCompanies.Id) — هر کاربر فقط شرکت‌های مجاز خودش را می‌بیند.</summary>
    public int CompanyId { get; set; }

    /// <summary>Server | Switch | Mikrotik | Firewall | System | Other</summary>
    [MaxLength(30)] public string Category { get; set; } = "Server";

    [MaxLength(200)] public string Title { get; set; } = "";

    /// <summary>آی‌پی یا نام میزبان</summary>
    [MaxLength(200)] public string? Host { get; set; }
    public int? Port { get; set; }

    /// <summary>روش اتصال: SSH / Telnet / Web / HTTPS / Winbox / RDP / ...</summary>
    [MaxLength(30)] public string? AccessMethod { get; set; }

    /// <summary>آدرس ورود سامانه/پنل وب</summary>
    [MaxLength(500)] public string? Url { get; set; }

    [MaxLength(200)] public string? Username { get; set; }

    /// <summary>رمز عبور رمزنگاری‌شده (v1.base64) — هرگز در لیست‌ها برگردانده نمی‌شود.</summary>
    public string? PasswordCipher { get; set; }

    /// <summary>اطلاعات محرمانهٔ تکمیلی رمزنگاری‌شده (enable secret، SNMP community، کلید Wi-Fi و ...).</summary>
    public string? ExtraSecretCipher { get; set; }

    /// <summary>شناسهٔ کلید اصلی‌ای که با آن رمزنگاری شده (تشخیص تعویض/گم‌شدن کلید).</summary>
    [MaxLength(32)] public string? KeyId { get; set; }

    [MaxLength(250)] public string? Location { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime? PasswordChangedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    [MaxLength(200)] public string? UpdatedByName { get; set; }
}

/// <summary>لاگ دسترسی به گاوصندوق: مشاهدهٔ رمز، کپی، ایجاد، ویرایش، حذف، تلاش ناموفق باز کردن.</summary>
[Table("ItCredentialLogs")]
public class ItCredentialLog
{
    public int Id { get; set; }
    public int CredentialId { get; set; }
    public int CompanyId { get; set; }
    public int UserId { get; set; }
    [MaxLength(200)] public string? UserName { get; set; }

    /// <summary>Reveal | Copy | Create | Update | Delete | UnlockFailed | Unlock</summary>
    [MaxLength(30)] public string Action { get; set; } = "";
    [MaxLength(64)] public string? ClientIp { get; set; }
    [MaxLength(500)] public string? Details { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
}
