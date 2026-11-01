using System.Text.Json;
using System.Text.Json.Serialization;

namespace InventoryAgent.Models;

// ============================================================================
// مدل‌های گزارش سخت‌افزار — دقیقاً مطابق قراردادی که سمت سرور انتظار دارد:
//   POST api/SystemInfo  با بدنه‌ی SystemInfo { AgentId, Motherboard, Cpu, Ram,
//     HardDisk, Graphics, Monitor, OsName, TotalRamGb, DetailsJson }
// داخل DetailsJson (به‌صورت camelCase) خوانده می‌شود:
//   board, boardSerial, computerModel,
//   cpus[], ramSticks[], disks[], gpus[], monitors[], netAdapters[], volumes[]
// نکته: نام‌ها در DetailsJson به حروف کوچک/بزرگ حساس‌اند — حتماً camelCase.
// ============================================================================

public sealed class AgentReport
{
    public string AgentId { get; set; } = "";
    public string? Motherboard { get; set; }
    public string? Cpu { get; set; }
    public string? Ram { get; set; }
    public string? HardDisk { get; set; }
    public string? Graphics { get; set; }
    public string? Monitor { get; set; }
    public string? OsName { get; set; }
    public int TotalRamGb { get; set; }
    public string? DetailsJson { get; set; }
}

/// <summary>جزئیات ساختاریافته‌ی سخت‌افزار — همه‌ی قطعات به‌صورت لیست (چند CPU، چند رم، چند هارد، ...).</summary>
public sealed class DetailsDocument
{
    public string Board { get; set; } = "";
    public string BoardSerial { get; set; } = "";
    public string ComputerModel { get; set; } = "";

    public List<CpuDetail> Cpus { get; set; } = new();
    public List<RamDetail> RamSticks { get; set; } = new();
    public List<DiskDetail> Disks { get; set; } = new();
    public List<GpuDetail> Gpus { get; set; } = new();
    public List<MonitorDetail> Monitors { get; set; } = new();
    public List<NetAdapterDetail> NetAdapters { get; set; } = new();
    public List<VolumeDetail> Volumes { get; set; } = new();

    // --- متادیتای ایجنت (سرور نادیده می‌گیرد؛ فقط برای دیباگ مفید است) ---
    public string AgentVersion { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string CollectedAt { get; set; } = "";
}

public sealed class CpuDetail
{
    public string Name { get; set; } = "";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public double ClockGhz { get; set; }
}

public sealed class RamDetail
{
    public string Slot { get; set; } = "";
    public int CapacityGb { get; set; }
    public string Type { get; set; } = "";
    public int SpeedMhz { get; set; }
    public string Manufacturer { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string SerialNumber { get; set; } = "";
}

public sealed class DiskDetail
{
    public string Model { get; set; } = "";
    public int SizeGb { get; set; }
    public string Interface { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    /// <summary>Healthy | Degraded | PredFail | Failed | Unknown</summary>
    public string Smart { get; set; } = "Unknown";
}

public sealed class GpuDetail
{
    public string Name { get; set; } = "";
    public string Resolution { get; set; } = "";
}

public sealed class MonitorDetail
{
    public string Name { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public bool IsPrimary { get; set; }
}

public sealed class NetAdapterDetail
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Ipv4 { get; set; } = "";
    public string Gateway { get; set; } = "";
}

public sealed class VolumeDetail
{
    public string Letter { get; set; } = "";
    public string Label { get; set; } = "";
    public int TotalGb { get; set; }
    public int UsedGb { get; set; }
}

/// <summary>دستور از راه دور دریافتی از سرور (Reboot | Shutdown | Lock).</summary>
public sealed class RemoteCommand
{
    public int Id { get; set; }
    public string Action { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public static class AgentJson
{
    /// <summary>سریالایز camelCase — الزامی برای DetailsJson چون سرور نام‌ها را case-sensitive می‌خواند.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    public static readonly JsonSerializerOptions PrettyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

/// <summary>ساخت خلاصه‌ی تخت (flat) از روی جزئیات — برای فیلدهای نمایشی رکورد سرور.</summary>
public static class ReportBuilder
{
    public static AgentReport Build(string agentId, string osName, DetailsDocument d)
    {
        var totalRam = d.RamSticks.Sum(r => r.CapacityGb);
        return new AgentReport
        {
            AgentId = agentId,
            Motherboard = string.IsNullOrWhiteSpace(d.Board) ? null : d.Board,
            Cpu = Join(d.Cpus.Select(c => c.Name)),
            Ram = d.RamSticks.Count == 0 ? null
                : $"{totalRam} GB — {d.RamSticks.Count} ماژول",
            HardDisk = Join(d.Disks.Select(x => $"{x.Model} ({x.SizeGb}GB)")),
            Graphics = Join(d.Gpus.Select(g => g.Name)),
            Monitor = Join(d.Monitors.Select(m => m.Name)),
            OsName = osName,
            TotalRamGb = totalRam,
            DetailsJson = JsonSerializer.Serialize(d, AgentJson.Options)
        };
    }

    private static string? Join(IEnumerable<string?> parts)
    {
        var list = parts.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
        return list.Count == 0 ? null : string.Join(" + ", list);
    }
}
