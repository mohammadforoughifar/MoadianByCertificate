using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

/// <summary>شرکت‌های مجاز هر کاربر برای انتخاب شرکت فعال.</summary>
public class UserCompanyAccess
{
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [ForeignKey(nameof(UserId))] public User User { get; set; } = null!;
    [ForeignKey(nameof(CompanyId))] public SystemCompany Company { get; set; } = null!;
}
