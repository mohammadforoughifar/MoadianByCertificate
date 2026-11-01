using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

/// <summary>
/// سمت سازمانی (پورت جدول Semats طرح کارفرما — نسخه کاربرمحور با User از نوع int).
/// هر سمت به یک Organization وصل است؛ جزء «واحد» در شماره نامه از
/// Organization.NameUniq سمتِ صادرکننده (CreatorSematId) خوانده می‌شود.
/// فعلاً چارت سازمانی کامل پورت نشده؛ همین رابطه‌ی حداقلی سمت←سازمان
/// برای تولید شماره کافی است و با سمت‌های آتیِ نامه‌ها (SematId در جداول نامه)
/// به‌صورت خودکار فعال می‌شود.
/// </summary>
public class Semat
{
    public int SematId { get; set; }

    /// <summary>سمت والد — برای درخت سازمانی آینده</summary>
    public int? Parent { get; set; }

    /// <summary>کاربرِ دارای این سمت (اختیاری — یک کاربر می‌تواند چند سمت داشته باشد)</summary>
    public int? UserId { get; set; }

    [MaxLength(200)] public string Title { get; set; } = "";

    /// <summary>عنوان مکاتباتی (روی سربرگ نامه)</summary>
    [MaxLength(300)] public string? OnvanMokatebati { get; set; }

    public int OrganizationId { get; set; }

    public bool DefaultSemat { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDelete { get; set; }

    [ForeignKey(nameof(OrganizationId))] public Organization? Organization { get; set; }
    public User? User { get; set; }
}
