namespace InventoryAgent.Models;

// ============================================================================
// گزارش وضعیت چاپگرهای محلی سیستم — جدا از گزارش سخت‌افزار.
//   POST api/OfficeMachines/agent/printers
// عمداً داخل DetailsJson گزارش سخت‌افزار نیست: وضعیت چاپگر مدام عوض می‌شود و اگر آن‌جا بود
// هر بار «تغییر سخت‌افزار — در انتظار تایید» می‌ساخت.
// ============================================================================

public sealed class AgentPrinterReport
{
    public string AgentId { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public string OsName { get; set; } = "";
    public string CollectedAt { get; set; } = "";
    /// <summary>آیا لاگ چاپ ویندوز (PrintService/Operational) فعال است و شمارش صفحات انجام می‌شود؟</summary>
    public bool PageLogEnabled { get; set; }
    public List<LocalPrinter> Printers { get; set; } = new();
}

public sealed class LocalPrinter
{
    /// <summary>نام صف چاپ در سیستم‌عامل (کلید شناسایی)</summary>
    public string Name { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string PortName { get; set; } = "";
    /// <summary>Usb | Network | Virtual | Other</summary>
    public string Kind { get; set; } = "Other";
    /// <summary>اگر پورت شبکه‌ای مستقیم باشد (IP_192.168.1.50) آدرس IP استخراج‌شده</summary>
    public string PortIp { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsShared { get; set; }
    public string ShareName { get; set; } = "";
    public string Location { get; set; } = "";
    public string Comment { get; set; } = "";

    /// <summary>Idle | Printing | WarmingUp | Offline | Paused | Error | Unknown</summary>
    public string Status { get; set; } = "Unknown";
    /// <summary>پرچم‌ها با همان نام‌های سمت سرور: NoPaper, LowPaper, NoToner, LowToner, Jammed, DoorOpen, Offline,
    /// ServiceRequested, OutputFull, Paused, Error, NotAvailable, UserIntervention</summary>
    public List<string> ErrorFlags { get; set; } = new();

    public int JobsCount { get; set; }
    public int JobsInError { get; set; }
    /// <summary>عمر قدیمی‌ترین کار در صف (دقیقه) — برای تشخیص صف گیرکرده</summary>
    public int? OldestJobMinutes { get; set; }

    /// <summary>شمارندهٔ تجمعی صفحات چاپ‌شده از این سیستم (از لاگ چاپ ویندوز) — یکنوا؛ سرور اختلاف را حساب می‌کند.</summary>
    public long? CumulativePages { get; set; }
}
