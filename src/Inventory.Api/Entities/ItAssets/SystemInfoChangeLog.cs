namespace Inventory.Api.Data;

/// <summary>تاریخچه‌ی تغییرات سخت‌افزار یک سیستم — هر بار که تایید مقایسه انجام شود یک رکورد ثبت می‌شود.</summary>
public class SystemInfoChangeLog
{
    public int Id { get; set; }
    public int SystemInfoId { get; set; }
    public string? AgentId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.Now;
    public int ChangeCount { get; set; }
    /// <summary>لیست اختلاف‌ها: [{field, old, new, changed}]</summary>
    public string ChangesJson { get; set; } = "";
}
