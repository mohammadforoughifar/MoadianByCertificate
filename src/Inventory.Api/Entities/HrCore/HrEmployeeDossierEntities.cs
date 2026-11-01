using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// ================== پرونده کارمندان — جداول وابسته (HrEmployee*) ==================
/// تحت‌تکفل، دوره‌ها، مهارت‌ها، زبان‌ها و اسناد پرسنل (با تاریخ انقضا).
/// اسکیما با HrEmployeeDossierSchemaV1 به‌صورت Ensure ساخته می‌شود (نه EF Migration).
/// </summary>

/// <summary>نوع سند پرسنلی</summary>
public enum HrDocType
{
    Contract = 0,
    NationalCard = 1,
    Degree = 2,
    BirthCertificate = 3,
    Military = 4,
    Insurance = 5,
    Other = 9
}

/// <summary>سطح مهارت / زبان</summary>
public enum HrSkillLevel
{
    Beginner = 0,
    Intermediate = 1,
    Advanced = 2,
    Expert = 3
}

/// <summary>فرد تحت تکفل پرسنل (همسر، فرزند، پدر، مادر و…)</summary>
public class HrEmployeeDependent
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(150)]
    public string FullName { get; set; } = "";

    /// <summary>نسبت — مثلاً همسر، فرزند، پدر، مادر</summary>
    [MaxLength(50)]
    public string Relation { get; set; } = "";

    public DateTime? BirthDate { get; set; }

    [MaxLength(10)]
    public string? NationalCode { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>دوره آموزشی گذرانده‌شده توسط پرسنل</summary>
public class HrEmployeeCourse
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(200)]
    public string? Institute { get; set; }

    /// <summary>سال برگزاری (شمسی)</summary>
    public int? Year { get; set; }

    /// <summary>مدت دوره (ساعت)</summary>
    public int? DurationHours { get; set; }

    public bool HasCertificate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>مهارت پرسنل + سطح</summary>
public class HrEmployeeSkill
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(150)]
    public string Title { get; set; } = "";

    public HrSkillLevel Level { get; set; } = HrSkillLevel.Intermediate;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>زبان خارجی پرسنل + سطح</summary>
public class HrEmployeeLanguage
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(80)]
    public string Language { get; set; } = "";

    public HrSkillLevel Level { get; set; } = HrSkillLevel.Intermediate;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>سند پرسنلی — بایگانی دیجیتال با تاریخ انقضا و هشدار خودکار</summary>
public class HrEmployeeDocument
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public HrDocType DocType { get; set; } = HrDocType.Other;

    /// <summary>مسیر نسبی فایل از جذر wwwroot — مثلاً uploads/hr-employee/doc/5/xxx.pdf</summary>
    [MaxLength(300)]
    public string? FilePath { get; set; }

    [MaxLength(200)]
    public string? FileName { get; set; }

    [MaxLength(100)]
    public string? ContentType { get; set; }

    public long? FileSize { get; set; }

    public DateTime? IssueDate { get; set; }

    /// <summary>تاریخ انقضا — خالی یعنی بدون انقضا</summary>
    public DateTime? ExpiryDate { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
