using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>دستگاه‌های NVR/DVR مداربسته.</summary>
public class CctvNvr
{
    public int Id { get; set; }
    [MaxLength(150)] public string Model { get; set; } = "";
    [MaxLength(150)] public string SerialNumber { get; set; } = "";
    [MaxLength(50)] public string? Ip { get; set; }
    [MaxLength(50)] public string? Mac { get; set; }
    [MaxLength(250)] public string? Location { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
