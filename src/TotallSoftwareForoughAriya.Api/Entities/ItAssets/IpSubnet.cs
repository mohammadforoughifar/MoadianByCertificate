using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// مدیریت آی‌پی (IPAM) — یک رنج/سابنت شبکه به تفکیک شرکت.
/// وضعیت هر آی‌پی از سه منبع ساخته می‌شود:
///   ۱) شناسنامه‌ها (سیستم‌ها، ماشین‌های اداری، دوربین، NVR، گاوصندوق) + رزرو دستی → «ثبت‌شده» (سبز)
///   ۲) آخرین اسکن شبکه (پینگ/TCP) → «اشغال» — اگر در شناسنامه نباشد: زرد
///   ۳) بقیه → «خالی» (سفید)
/// </summary>
public class IpSubnet
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    [MaxLength(150)] public string Name { get; set; } = "";
    /// <summary>به‌صورت نرمال‌شده، مثل 192.168.30.0/24</summary>
    [MaxLength(40)] public string Cidr { get; set; } = "";
    /// <summary>آدرس شبکه به‌صورت عدد (برای مرتب‌سازی و جست‌وجوی سریع)</summary>
    public long NetworkNum { get; set; }
    public int Prefix { get; set; }
    [MaxLength(40)] public string? Gateway { get; set; }
    public int? Vlan { get; set; }
    [MaxLength(200)] public string? DnsServers { get; set; }
    /// <summary>بازهٔ DHCP (اختیاری) مثل 192.168.30.100-192.168.30.200 — فقط برای نمایش</summary>
    [MaxLength(80)] public string? DhcpRange { get; set; }
    [MaxLength(250)] public string? Location { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    /// <summary>اسکن خودکار هر چند دقیقه — ۰ یعنی خاموش</summary>
    public int AutoScanMinutes { get; set; }
    public DateTime? LastScanAt { get; set; }
    public int LastScanAliveCount { get; set; }
    public int LastScanDurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(200)] public string? UpdatedByName { get; set; }
}

/// <summary>
/// وضعیت هر آی‌پی داخل یک رنج: نتیجهٔ آخرین اسکن + رزرو دستی.
/// ردیف‌ها به‌صورت تنبل ساخته می‌شوند (فقط برای آی‌پی‌هایی که دیده یا رزرو شده‌اند).
/// </summary>
public class IpAddressEntry
{
    public int Id { get; set; }
    public int SubnetId { get; set; }
    [MaxLength(40)] public string Ip { get; set; } = "";
    public long IpNum { get; set; }

    // ---- نتیجهٔ اسکن ----
    public bool IsAlive { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? FirstSeenAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    [MaxLength(30)] public string? Mac { get; set; }
    [MaxLength(200)] public string? Hostname { get; set; }
    /// <summary>ping | tcp</summary>
    [MaxLength(10)] public string? Method { get; set; }

    // ---- رزرو دستی (ثبت در مدیریت آی‌پی = شناسنامه) ----
    public bool IsReserved { get; set; }
    [MaxLength(200)] public string? ReservedTitle { get; set; }
    /// <summary>Server | Switch | Router | Firewall | AccessPoint | Printer | Camera | Pc | Phone | Other</summary>
    [MaxLength(30)] public string? ReservedType { get; set; }
    [MaxLength(200)] public string? ReservedOwner { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(200)] public string? UpdatedByName { get; set; }
}
