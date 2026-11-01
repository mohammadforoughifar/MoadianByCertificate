using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>قالب شخصی گزارش‌ساز (هر کاربر قالب‌های خودش را دارد)</summary>
public class HrReportTemplate
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = "";

    /// <summary>employee | contract | decree | leave | mission</summary>
    [MaxLength(20)]
    public string Entity { get; set; } = "";

    /// <summary>کلید ستون‌ها (JSON)</summary>
    public string ColumnsJson { get; set; } = "[]";

    /// <summary>فیلترها (JSON)</summary>
    public string FiltersJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
