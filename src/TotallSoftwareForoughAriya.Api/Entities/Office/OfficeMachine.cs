using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

/// <summary>
/// ماشین‌های اداری (پرینتر، MFP، فتوکپی، اسکنر، فکس، پلاتر و ...) — شناسنامه، اتصال،
/// قرارداد/گارانتی و پایش زندهٔ SNMP.
/// اسرار SNMP (community و رمزهای v3) فقط به‌صورت رمزنگاری‌شده (AES-GCM با کلید گاوصندوق IT)
/// ذخیره می‌شوند و هرگز به کلاینت برگردانده نمی‌شوند.
/// </summary>
public class OfficeMachine
{
    public int Id { get; set; }

    // ------------------------------------------------------------ شناسنامه
    /// <summary>Printer | MFP | Copier | Scanner | Fax | Plotter | LabelPrinter | Other</summary>
    [MaxLength(30)] public string DeviceType { get; set; } = "Printer";
    [MaxLength(80)] public string? Brand { get; set; }
    [MaxLength(150)] public string Model { get; set; } = "";
    [MaxLength(150)] public string? SerialNumber { get; set; }
    /// <summary>برچسب/شمارهٔ اموال</summary>
    [MaxLength(80)] public string? AssetTag { get; set; }
    public bool IsColor { get; set; }
    [MaxLength(250)] public string? Location { get; set; }
    /// <summary>واحد سازمانی استفاده‌کننده</summary>
    [MaxLength(150)] public string? Department { get; set; }
    /// <summary>مسئول دستگاه — مقصد اعلان هشدارها</summary>
    public int? ResponsibleUserId { get; set; }

    /// <summary>شرکتی که دستگاه به آن اختصاص دارد (SystemCompanies)</summary>
    public int? CompanyId { get; set; }

    /// <summary>کاربری که دستگاه به او اختصاص دارد (SystemUsers — همان کاربران مدیریت سیستم)</summary>
    public int? AssignedUserId { get; set; }
    public DateTime? InstallDate { get; set; }

    /// <summary>InService (در حال کار) | InRepair (در تعمیر) | Standby (رزرو/انبار) | Retired (خارج از رده)</summary>
    [MaxLength(20)] public string Status { get; set; } = "InService";
    /// <summary>سازگاری با نسخهٔ قبل — همگام با Status نگه‌داری می‌شود (Retired/Standby ⇒ false).</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>تاریخ رفت (قدیمی — جای خود را به سابقهٔ تعمیر داده است)</summary>
    public DateTime? GoneDate { get; set; }
    /// <summary>تاریخ برگشت (قدیمی)</summary>
    public DateTime? ReturnDate { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }

    // ------------------------------------------------------------ اتصال
    /// <summary>نوع اتصال: Network (شبکه) | Cable (کابلی)</summary>
    [MaxLength(20)] public string ConnectionType { get; set; } = "Network";
    /// <summary>آی‌پی — برای ماشین‌های شبکه</summary>
    [MaxLength(50)] public string? IpAddress { get; set; }
    /// <summary>سیستم متصل — برای ماشین‌های کابلی</summary>
    public int? LinkedSystemInfoId { get; set; }
    /// <summary>برچسب سیستم متصل (نام مالک) برای نمایش</summary>
    [MaxLength(250)] public string? LinkedSystemLabel { get; set; }
    [MaxLength(30)] public string? MacAddress { get; set; }
    [MaxLength(100)] public string? Hostname { get; set; }
    [MaxLength(100)] public string? FirmwareVersion { get; set; }
    /// <summary>sysDescr خوانده‌شده از دستگاه</summary>
    [MaxLength(300)] public string? SysDescr { get; set; }

    // ------------------------------------------------------------ خرید / گارانتی / قرارداد
    public DateTime? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public DateTime? WarrantyUntil { get; set; }
    [MaxLength(150)] public string? ServiceVendor { get; set; }
    public DateTime? ServiceContractUntil { get; set; }

    // ------------------------------------------------------------ SNMP
    public bool SnmpEnabled { get; set; }
    /// <summary>v1 | v2c | v3</summary>
    [MaxLength(5)] public string SnmpVersion { get; set; } = "v2c";
    public int SnmpPort { get; set; } = 161;
    /// <summary>community (v1/v2c) — رمزنگاری‌شده</summary>
    [MaxLength(600)] public string? SnmpCommunityCipher { get; set; }
    [MaxLength(100)] public string? SnmpV3User { get; set; }
    /// <summary>None | MD5 | SHA1 | SHA256</summary>
    [MaxLength(10)] public string SnmpV3AuthProtocol { get; set; } = "SHA1";
    [MaxLength(600)] public string? SnmpV3AuthCipher { get; set; }
    /// <summary>None | DES | AES</summary>
    [MaxLength(10)] public string SnmpV3PrivProtocol { get; set; } = "AES";
    [MaxLength(600)] public string? SnmpV3PrivCipher { get; set; }
    /// <summary>فاصلهٔ پایش خودکار (دقیقه) — ۲ تا ۱۴۴۰</summary>
    public int PollIntervalMinutes { get; set; } = 10;
    /// <summary>ارسال اعلان برای هشدارهای این دستگاه</summary>
    public bool AlertsEnabled { get; set; } = true;
    /// <summary>آستانهٔ هشدار «تونر/مادهٔ مصرفی کم» (درصد)</summary>
    public int SupplyLowThreshold { get; set; } = 15;

    // ------------------------------------------------------------ آخرین وضعیت (کش برای لیست سریع)
    public DateTime? LastPollAt { get; set; }
    public DateTime? LastSeenOnlineAt { get; set; }
    /// <summary>Online | Warning | Error | Offline | Unknown | NotMonitored</summary>
    [MaxLength(20)] public string OnlineState { get; set; } = "Unknown";
    /// <summary>idle | printing | warmup | other | unknown</summary>
    [MaxLength(20)] public string? PrinterState { get; set; }
    public long? LastPageCount { get; set; }
    public int? LowestSupplyPercent { get; set; }
    /// <summary>متن نمایشگر دستگاه (LCD)</summary>
    [MaxLength(300)] public string? LastStatusText { get; set; }
    /// <summary>کدهای خطای فعال جداشده با کاما (Jammed,LowToner,...)</summary>
    [MaxLength(300)] public string? LastErrorCodes { get; set; }
    [MaxLength(500)] public string? LastPollError { get; set; }
    public long? UptimeSeconds { get; set; }

    // ---- پایش از طریق ایجنت (چاپگرهای USB/محلی)
    /// <summary>چاپگر محلیِ گزارش‌شده توسط ایجنت سیستم متصل که وضعیت این دستگاه از آن خوانده می‌شود</summary>
    public int? AgentPrinterId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>سابقه‌ی تعمیر هر ماشین — تاریخ + شرح مشکل.</summary>
public class OfficeMachineRepair
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public DateTime RepairDate { get; set; } = DateTime.Now;
    /// <summary>تاریخ رفت ماشین به تعمیر</summary>
    public DateTime? GoneDate { get; set; }
    /// <summary>تاریخ برگشت</summary>
    public DateTime? ReturnDate { get; set; }
    /// <summary>شرح ایراد</summary>
    [MaxLength(1000)] public string Problem { get; set; } = "";
    /// <summary>شرح کارهای انجام‌شده</summary>
    [MaxLength(2000)] public string? PerformedWork { get; set; }
    /// <summary>هزینه‌ی همین تعمیر</summary>
    public decimal Cost { get; set; }
    public bool Fixed { get; set; } = true;
}

/// <summary>هزینه‌های انجام‌شده برای هر ماشین.</summary>
public class OfficeMachineCost
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public DateTime CostDate { get; set; } = DateTime.Now;
    [MaxLength(300)] public string Title { get; set; } = "";
    public decimal Amount { get; set; }
    /// <summary>Toner | Paper | Service | Part | Contract | Other</summary>
    [MaxLength(30)] public string Category { get; set; } = "Other";
}

/// <summary>وضعیت فعلی هر مادهٔ مصرفی دستگاه (تونر، درام، مخزن ضایعات، فیوزر...) — از prtMarkerSuppliesTable.</summary>
[Table("OfficeMachineSupplies")]
public class OfficeMachineSupply
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public int SupplyIndex { get; set; }
    /// <summary>Toner | Ink | Drum | Developer | Fuser | Transfer | Waste | Staples | Other</summary>
    [MaxLength(30)] public string Kind { get; set; } = "Other";
    /// <summary>black | cyan | magenta | yellow | ...</summary>
    [MaxLength(30)] public string? Color { get; set; }
    [MaxLength(200)] public string? Description { get; set; }
    public int Level { get; set; }
    public int MaxCapacity { get; set; }
    /// <summary>درصد باقی‌مانده؛ null یعنی دستگاه مقدار دقیق گزارش نمی‌کند.</summary>
    public int? Percent { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>سینی‌های کاغذ — از prtInputTable.</summary>
[Table("OfficeMachineTrays")]
public class OfficeMachineTray
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public int TrayIndex { get; set; }
    [MaxLength(100)] public string? Name { get; set; }
    [MaxLength(100)] public string? MediaName { get; set; }
    public int CurrentLevel { get; set; }
    public int MaxCapacity { get; set; }
    public int? Percent { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>شمارندهٔ صفحات — یک ردیف برای هر روز (برای گزارش تیراژ).</summary>
[Table("OfficeMachineCounters")]
public class OfficeMachineCounter
{
    public long Id { get; set; }
    public int MachineId { get; set; }
    /// <summary>تاریخ روز (بدون ساعت)</summary>
    public DateTime Day { get; set; }
    public long TotalPages { get; set; }
    public DateTime TakenAt { get; set; } = DateTime.Now;
}

/// <summary>رویدادها و هشدارهای دستگاه (باز/بسته) — هر شرط فعال یک رویداد باز دارد.</summary>
[Table("OfficeMachineEvents")]
public class OfficeMachineEvent
{
    public long Id { get; set; }
    public int MachineId { get; set; }
    /// <summary>Offline | Jammed | NoPaper | LowPaper | NoToner | LowSupply:3 | DoorOpen | ...</summary>
    [MaxLength(40)] public string Code { get; set; } = "";
    /// <summary>Info | Warning | Critical</summary>
    [MaxLength(20)] public string Severity { get; set; } = "Warning";
    [MaxLength(500)] public string Message { get; set; } = "";
    /// <summary>Poll | User</summary>
    [MaxLength(20)] public string Source { get; set; } = "Poll";
    public DateTime OpenedAt { get; set; } = DateTime.Now;
    public DateTime LastSeenAt { get; set; } = DateTime.Now;
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    [MaxLength(200)] public string? ResolvedByName { get; set; }
    public bool Notified { get; set; }
}


/// <summary>
/// چاپگری که ایجنت یک سیستم (Agent.Console) گزارش کرده است: صف چاپ ویندوز/CUPS با وضعیت، صف کارها
/// و شمارندهٔ صفحات. می‌تواند به یک ماشین اداری «کابلی» لینک شود تا آن دستگاه هم پایش زنده داشته باشد.
/// </summary>
public class OfficeAgentPrinter
{
    public int Id { get; set; }
    [MaxLength(100)] public string AgentId { get; set; } = "";
    public int? SystemInfoId { get; set; }
    [MaxLength(100)] public string? HostName { get; set; }
    [MaxLength(250)] public string PrinterName { get; set; } = "";
    [MaxLength(250)] public string? DriverName { get; set; }
    [MaxLength(150)] public string? PortName { get; set; }
    /// <summary>Usb | Local | Network | Virtual | Connection | Other</summary>
    [MaxLength(20)] public string Kind { get; set; } = "Other";
    [MaxLength(50)] public string? PortIp { get; set; }
    public bool IsDefault { get; set; }
    public bool IsShared { get; set; }
    [MaxLength(250)] public string? Location { get; set; }
    /// <summary>Idle | Printing | WarmingUp | Offline | Paused | Error | Unknown</summary>
    [MaxLength(20)] public string Status { get; set; } = "Unknown";
    [MaxLength(300)] public string? ErrorCodes { get; set; }
    public int JobsCount { get; set; }
    public int JobsInError { get; set; }
    public int? OldestJobMinutes { get; set; }
    /// <summary>آخرین شمارندهٔ تجمعی گزارش‌شده توسط ایجنت (برای محاسبهٔ اختلاف)</summary>
    public long? AgentCumulativePages { get; set; }
    /// <summary>جمع صفحاتی که سرور از این چاپگر ثبت کرده است</summary>
    public long PagesLogged { get; set; }
    /// <summary>در آخرین گزارش ایجنت وجود داشت؟ (اگر درایور حذف شود false می‌شود)</summary>
    public bool IsPresent { get; set; } = true;
    public bool IsIgnored { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.Now;
    public DateTime LastSeenAt { get; set; } = DateTime.Now;
    /// <summary>آخرین بار که ایجنت این سیستم (با هر محتوایی) گزارش داد</summary>
    public DateTime LastReportAt { get; set; } = DateTime.Now;
}
