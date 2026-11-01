using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>ثبت‌نام دوربین‌های مداربسته — مدل، سریال، آی‌پی، مک و محل استقرار.</summary>
public class CctvCamera
{
    public int Id { get; set; }
    [MaxLength(150)] public string Model { get; set; } = "";
    [MaxLength(150)] public string SerialNumber { get; set; } = "";
    [MaxLength(50)] public string? Ip { get; set; }
    [MaxLength(50)] public string? Mac { get; set; }
    [MaxLength(250)] public string? Location { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    /// <summary>دستگاه NVR متصل (در صورت وجود)</summary>
    public int? NvrId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
