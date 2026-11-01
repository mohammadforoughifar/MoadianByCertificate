using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;
public class SystemInfo
{
    public int Id { get; set; }
    public string AgentId { get; set; } = "";
    public string? Motherboard { get; set; }
    public string? Cpu { get; set; }
    public string? Ram { get; set; }
    public string? HardDisk { get; set; }
    public string? Graphics { get; set; }
    public string? Monitor { get; set; }
    public bool IsApproved { get; set; } = false;
    public string? OsName { get; set; }
    public int TotalRamGb { get; set; }
    public int? CompanyId { get; set; }
    public int? DepartmentId { get; set; }
    public int? UserId { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.Now;
    /// <summary>جزئیات کامل سخت‌افزار به صورت JSON (همه هاردها، همه ماژول‌های رم، ...)</summary>
    public string? DetailsJson { get; set; }
    /// <summary>اطلاعات جدید ارسالی ایجنت که در انتظار تایید مقایسه است (JSON کامل)</summary>
    public string? PendingPayloadJson { get; set; }
    public DateTime? PendingReceivedAt { get; set; }
}