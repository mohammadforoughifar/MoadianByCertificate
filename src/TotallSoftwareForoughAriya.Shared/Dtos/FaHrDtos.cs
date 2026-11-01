namespace Inventory.Shared.Dtos;

// ==================== منابع انسانی فروغ آریا — پرسنل و چارت (جدول اختصاصی FaHrPersonnel) ====================
// این DTOها فقط مال همین ماژول‌اند و به پرونده‌های پرسنلی منابع انسانی اصلی (HrEmployee) ربطی ندارند.

public class FaHrPersonnelDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string NationalCode { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public int Gender { get; set; }
    public int MaritalStatus { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime HireDate { get; set; }
    public string? UnitTitle { get; set; }
    public string? PostTitle { get; set; }
    public int? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public int EmploymentType { get; set; }
    /// <summary>0 فعال، 1 مرخصی، 2 تعلیق، 3 پایان همکاری، 4 بازنشسته</summary>
    public int Status { get; set; }
    public decimal BaseSalary { get; set; }
    public bool IsActive { get; set; }
    public string? Sheba { get; set; }
    public string? BankName { get; set; }
    public string? InsuranceNo { get; set; }
    /// <summary>کاربر سیستمِ پیوندخورده (همیشه کاربرِ فعالِ دارای دسترسی به ماژول)</summary>
    public int UserId { get; set; }
    public string? Username { get; set; }
}

public class FaHrPersonnelSaveDto
{
    public string? Code { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string NationalCode { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public int Gender { get; set; }
    public int MaritalStatus { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime HireDate { get; set; } = DateTime.Today;
    public string? UnitTitle { get; set; }
    public string? PostTitle { get; set; }
    public int? ManagerId { get; set; }
    public int EmploymentType { get; set; } = 1;
    public int Status { get; set; }
    public decimal BaseSalary { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Sheba { get; set; }
    public string? BankName { get; set; }
    public string? InsuranceNo { get; set; }
    /// <summary>الزامی: فقط کاربرانِ فعالِ دارای دسترسی به «منابع انسانی فروغ آریا»</summary>
    public int UserId { get; set; }
}

/// <summary>
/// پرسنلی که فعلاً در ماژول دیده نمی‌شوند (پیوند ندارند یا کاربرشان غیرفعال/بدون دسترسی است) —
/// فقط برای مدیر، جهت برقراری پیوند با یک کاربر مجاز. در هیچ کمبو/فهرست/محاسبه‌ای استفاده نمی‌شوند.
/// </summary>
public class FaHrPendingPersonnelDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string FullName { get; set; } = "";
    public string NationalCode { get; set; } = "";
    public int Status { get; set; }
    public string? Username { get; set; }
    /// <summary>علت: بدون پیوند / کاربر غیرفعال / کاربر بدون دسترسی</summary>
    public string Reason { get; set; } = "";
}

public class FaHrLinkDto { public int UserId { get; set; } }

/// <summary>گزینهٔ کمبوی «کاربر سیستم» — فقط کاربرانِ فعالِ دارای دسترسی به ماژول</summary>
public class FaHrUserOptionDto
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    /// <summary>اگر این کاربر قبلاً به پرسنلی پیوند خورده باشد</summary>
    public int? PersonnelId { get; set; }
}

public class FaHrOrgNodeDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Code { get; set; } = "";
    public string? PostTitle { get; set; }
    public string? UnitTitle { get; set; }
    public int? ManagerId { get; set; }
    public int Status { get; set; }
    public List<FaHrOrgNodeDto> Children { get; set; } = new();
}
