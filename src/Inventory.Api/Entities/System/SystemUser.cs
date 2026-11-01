using System.ComponentModel.DataAnnotations;
using Inventory.Shared.Entities;

namespace Inventory.Api.Data;

public class SystemUser
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Username { get; set; } = "";

    [MaxLength(100)]
    public string FirstName { get; set; } = "";

    [MaxLength(100)]
    public string LastName { get; set; } = "";

    [MaxLength(50)]
    public string StaffNumber { get; set; } = "";

    /// <summary>شماره تماس</summary>
    [MaxLength(30)]
    public string? Phone { get; set; }

    public int? DepartmentId { get; set; }
    public int? CompanyId { get; set; }

    /// <summary>شناسه واحد/سازمان سازمانی کاربر — برای جزء «واحد» در شماره نامه</summary>
    public int? OrganizationId { get; set; }

    /// <summary>واحد/سازمان سازمانی کاربر</summary>
    public Organization? Organization { get; set; }

    [MaxLength(20)]
    public string? Role { get; set; } = "User";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>مسیر نسبی عکس داخل uploads/ (فقط اطلاعات — فایل روی دیسک است). null = عکس پیش‌فرض</summary>
    [MaxLength(200)]
    public string? PhotoPath { get; set; }
}