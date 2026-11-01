using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// ============ جدول‌های مجزای هر قطعه‌ی کامپیوتر (مرتبط با شناسنامه سیستم) ============

public class SystemCpu
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public double ClockGhz { get; set; }
}

public class SystemRam
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    [MaxLength(100)] public string Slot { get; set; } = "";
    public int CapacityGb { get; set; }
    [MaxLength(20)] public string Type { get; set; } = "";
    public int SpeedMhz { get; set; }
    [MaxLength(100)] public string Manufacturer { get; set; } = "";
    [MaxLength(100)] public string PartNumber { get; set; } = "";
    [MaxLength(100)] public string SerialNumber { get; set; } = "";
}

public class SystemDisk
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    [MaxLength(200)] public string Model { get; set; } = "";
    public int SizeGb { get; set; }
    [MaxLength(50)] public string Interface { get; set; } = "";
    [MaxLength(100)] public string SerialNumber { get; set; } = "";
    /// <summary>وضعیت S.M.A.R.T: Healthy | Degraded | PredFail | Failed | Unknown</summary>
    [MaxLength(20)] public string? SmartStatus { get; set; }
    public DateTime? SmartUpdatedAt { get; set; }
}

public class SystemGpu
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(30)] public string Resolution { get; set; } = "";
}

public class SystemMonitor
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(30)] public string Resolution { get; set; } = "";
    [MaxLength(100)] public string SerialNumber { get; set; } = "";
    public bool IsPrimary { get; set; }
}

public class SystemNetAdapter
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string Description { get; set; } = "";
    [MaxLength(50)] public string Type { get; set; } = "";
    [MaxLength(50)] public string MacAddress { get; set; } = "";
    [MaxLength(200)] public string Ipv4 { get; set; } = "";
    [MaxLength(50)] public string Gateway { get; set; } = "";
}

public class SystemBoard
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    [MaxLength(250)] public string Board { get; set; } = "";
    [MaxLength(150)] public string BoardSerial { get; set; } = "";
    [MaxLength(250)] public string ComputerModel { get; set; } = "";
}

public class SystemVolume
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    [MaxLength(10)] public string Letter { get; set; } = "";
    [MaxLength(100)] public string Label { get; set; } = "";
    public int TotalGb { get; set; }
    public int UsedGb { get; set; }
}
