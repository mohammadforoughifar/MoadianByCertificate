using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// سازمان / واحد سازمانی — مبنای جزء «واحد» در شماره اندیکاتور نامه‌ها.
/// پورت از جدول Organization طرح کارفرما:
///   NameUnit  = نام نمایشی سازمان (مثلاً «مدیریت کیفیت»)
///   NameUniq  = کد/نام اختصاصی که در شماره نامه می‌نشیند (مثلاً MQ)
/// </summary>
public class Organization
{
    public int OrganizationId { get; set; }

    /// <summary>شناسه شرکت — واحدها به صورت جداگانه برای هر شرکت تعریف می‌شوند</summary>
    public int? CompanyId { get; set; }

    /// <summary>نام نمایشی سازمان/واحد — مثل «مدیریت کیفیت»</summary>
    [MaxLength(200)] public string NameUnit { get; set; } = "";

    /// <summary>نام اختصاصی (کد) — در شماره اندیکاتور استفاده می‌شود — مثل MQ</summary>
    [MaxLength(100)] public string NameUniq { get; set; } = "";

    /// <summary>سازمان پیش‌فرض — وقتی صادرکننده سمت (Semat) ندارد، جزء «واحد» از این سازمان خوانده می‌شود</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsDelete { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<Semat> Semats { get; set; } = new List<Semat>();

    /// <summary>شرکت ملحق به این واحد</summary>
    public SystemCompany? Company { get; set; }
}
