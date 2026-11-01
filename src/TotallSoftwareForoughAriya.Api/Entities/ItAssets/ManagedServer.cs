using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

/// <summary>
/// شناسنامهٔ سرور فیزیکی یا مجازیِ قابل مدیریت. اطلاعات ورود iLO/Redfish داخل این جدول
/// ذخیره نمی‌شود؛ فقط شناسهٔ رکورد رمزنگاری‌شدهٔ آن در گاوصندوق رمزها نگه‌داری می‌شود.
/// </summary>
[Table("ManagedServers")]
public sealed class ManagedServer
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    [MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(50)] public string Manufacturer { get; set; } = "HPE";
    [MaxLength(150)] public string? Model { get; set; }
    [MaxLength(100)] public string? SerialNumber { get; set; }
    [MaxLength(100)] public string? AssetTag { get; set; }

    /// <summary>نام یا IP کنترلر مدیریتی iLO. فقط HTTPS پشتیبانی می‌شود.</summary>
    [MaxLength(250)] public string IloAddress { get; set; } = "";
    public int IloPort { get; set; } = 443;
    /// <summary>اثر انگشت SHA-256 گواهی TLS که کاربر برای این iLO تأیید کرده است (هگز بزرگ، ۶۴ کاراکتر).</summary>
    [MaxLength(64)] public string? IloCertThumbprint { get; set; }
    [MaxLength(400)] public string? IloCertSubject { get; set; }
    public DateTime? IloCertNotAfter { get; set; }
    public DateTime? IloCertTrustedAt { get; set; }
    [MaxLength(200)] public string? IloCertTrustedByName { get; set; }
    /// <summary>ارجاع اختیاری به ItCredentials؛ رمز هرگز به کلاینت برگردانده نمی‌شود.</summary>
    public int? CredentialId { get; set; }
    [MaxLength(80)] public string? IloVersion { get; set; }

    [MaxLength(100)] public string? Role { get; set; }
    [MaxLength(100)] public string? Environment { get; set; }
    /// <summary>Low | Normal | High | Critical</summary>
    [MaxLength(20)] public string Criticality { get; set; } = "Normal";
    /// <summary>Active | Maintenance | Standby | Retired</summary>
    [MaxLength(20)] public string Status { get; set; } = "Active";
    [MaxLength(100)] public string? Rack { get; set; }
    public int? RackUnit { get; set; }
    [MaxLength(200)] public string? OperatingSystem { get; set; }
    public DateTime? WarrantyEndDate { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }

    public DateTime? LastHealthCheckAt { get; set; }
    /// <summary>آخرین زمان دریافت جزئیات قطعات/رخدادهای Redfish؛ مستقل از Health سبک دوره‌ای.</summary>
    public DateTime? LastHardwareDetailsAt { get; set; }
    [MaxLength(20)] public string? LastHealthStatus { get; set; }
    [MaxLength(30)] public string? LastPowerState { get; set; }
    [MaxLength(500)] public string? LastHealthMessage { get; set; }
    [MaxLength(1000)] public string? LastConnectionError { get; set; }
    public DateTime? LastInventoryAt { get; set; }

    /// <summary>اگر فعال باشد، Worker پس‌زمینه با فاصلهٔ تعیین‌شده Health را از Redfish می‌خواند.</summary>
    public bool MonitoringEnabled { get; set; }
    /// <summary>فاصلهٔ پایش برحسب دقیقه؛ مقدار معتبر ۵ تا ۱۴۴۰ است.</summary>
    public int MonitoringIntervalMinutes { get; set; } = 15;
    public DateTime? LastAutomaticPollAt { get; set; }

    /// <summary>مسئول عملیاتی سرور؛ مقصد اعلان هشدارها و مبنای واگذاری امن گردش‌کارهای بعدی.</summary>
    public int? ResponsibleUserId { get; set; }
    /// <summary>فقط در صورت انتخاب مسئول، اعلان درون‌برنامه‌ای/Push برای هشدارها ارسال می‌شود.</summary>
    public bool AlertNotificationsEnabled { get; set; }
    /// <summary>تا پایان این بازه، پایش ادامه دارد اما اعلان هشدار به مسئول ارسال نمی‌شود.</summary>
    public DateTime? MaintenanceUntil { get; set; }
    [MaxLength(500)] public string? MaintenanceNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    [MaxLength(200)] public string? UpdatedByName { get; set; }
}

/// <summary>خلاصهٔ غیرمحرمانهٔ هر بررسی Redfish برای روند سلامت سرور.</summary>
[Table("ServerHealthSnapshots")]
public sealed class ServerHealthSnapshot
{
    public long Id { get; set; }
    public int ManagedServerId { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    [MaxLength(20)] public string OverallHealth { get; set; } = "Unknown";
    [MaxLength(30)] public string? PowerState { get; set; }
    [MaxLength(100)] public string? FirmwareVersion { get; set; }
    [MaxLength(100)] public string? BiosVersion { get; set; }
    [MaxLength(150)] public string? Model { get; set; }
    [MaxLength(100)] public string? SerialNumber { get; set; }
    [MaxLength(500)] public string? Message { get; set; }
}

/// <summary>نمونهٔ ساخت‌یافته از وضعیت قطعات سخت‌افزاری Redfish؛ پاسخ خام iLO یا دادهٔ محرمانه ذخیره نمی‌شود.</summary>
[Table("ServerHardwareComponentSnapshots")]
public sealed class ServerHardwareComponentSnapshot
{
    public long Id { get; set; }
    public int ManagedServerId { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    /// <summary>Chassis | PowerSupply | Fan | Temperature | Memory | Processor | Storage | Disk</summary>
    [MaxLength(40)] public string ComponentType { get; set; } = "";
    /// <summary>شناسهٔ پایدار Redfish در محدودهٔ نوع قطعه.</summary>
    [MaxLength(200)] public string ComponentKey { get; set; } = "";
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(20)] public string Health { get; set; } = "Unknown";
    [MaxLength(40)] public string? State { get; set; }
    [MaxLength(100)] public string? Reading { get; set; }
    [MaxLength(150)] public string? Model { get; set; }
    [MaxLength(100)] public string? SerialNumber { get; set; }
    [MaxLength(500)] public string? Message { get; set; }
}

/// <summary>رخدادهای غیرمحرمانهٔ Event Log در Redfish/iLO؛ برای نمایش و روند رخدادها نگه‌داری می‌شوند.</summary>
[Table("ServerHardwareEvents")]
public sealed class ServerHardwareEvent
{
    public long Id { get; set; }
    public int ManagedServerId { get; set; }
    [MaxLength(200)] public string EventKey { get; set; } = "";
    [MaxLength(30)] public string Severity { get; set; } = "Unknown";
    [MaxLength(2000)] public string Message { get; set; } = "";
    [MaxLength(500)] public string? Origin { get; set; }
    public DateTime? OccurredAt { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.Now;
    public DateTime LastSeenAt { get; set; } = DateTime.Now;
}

/// <summary>لاگ عملیات ماژول سرورها؛ رمز، هدر احراز هویت و پاسخ خام Redfish در آن نوشته نمی‌شود.</summary>
[Table("ServerOperationLogs")]
public sealed class ServerOperationLog
{
    public long Id { get; set; }
    public int ManagedServerId { get; set; }
    public int? UserId { get; set; }
    [MaxLength(200)] public string? UserName { get; set; }
    /// <summary>Create | Update | Delete | TestConnection | CollectInventory | AutoCollect | AlertAcknowledge | AlertResolve | AlertWorkOrder</summary>
    [MaxLength(40)] public string Action { get; set; } = "";
    /// <summary>Success | Failed</summary>
    [MaxLength(20)] public string Result { get; set; } = "Success";
    [MaxLength(1000)] public string? Details { get; set; }
    [MaxLength(64)] public string? ClientIp { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
}

/// <summary>
/// هشدار پایش سرور. برای هر سرور و هر دسته تنها یک هشدار باز/تأییدشده نگه‌داری می‌شود
/// و پس از سالم‌شدن پاسخ Redfish خودکار Resolve خواهد شد.
/// </summary>
[Table("ServerAlerts")]
public sealed class ServerAlert
{
    public long Id { get; set; }
    public int ManagedServerId { get; set; }
    /// <summary>Connection | Health | HardwareEvent</summary>
    [MaxLength(40)] public string Category { get; set; } = "";
    /// <summary>Warning | Critical</summary>
    [MaxLength(20)] public string Severity { get; set; } = "Warning";
    [MaxLength(1000)] public string Message { get; set; } = "";
    /// <summary>Open | Acknowledged | Resolved</summary>
    [MaxLength(20)] public string Status { get; set; } = "Open";
    public DateTime OpenedAt { get; set; } = DateTime.Now;
    public DateTime LastSeenAt { get; set; } = DateTime.Now;
    public DateTime? AcknowledgedAt { get; set; }
    public int? AcknowledgedByUserId { get; set; }
    [MaxLength(200)] public string? AcknowledgedByName { get; set; }
    /// <summary>زمان آخرین اعلان به مسئول؛ هشدار Critical پس از فاصلهٔ کنترل‌شده قابل یادآوری است.</summary>
    public DateTime? LastNotifiedAt { get; set; }
    public int NotificationCount { get; set; }
    /// <summary>دستور کار دستیِ ایجادشده از این هشدار؛ ایجاد خودکار عمداً انجام نمی‌شود.</summary>
    public int? WorkOrderId { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
