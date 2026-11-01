using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>دوره ارزیابی عملکرد — Status: Draft=پیش‌نویس | Open=باز | Closed=بسته</summary>
public class HrPerfPeriod
{
    public int Id { get; set; }
    [MaxLength(150)] public string Title { get; set; } = "";
    public int Year { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Draft";
    /// <summary>مبنای پاداش به ماه حقوق پایه (مثلاً 1 = یک ماه)</summary>
    public decimal BonusMonthSalary { get; set; } = 1;
    /// <summary>حداقل نمره برای تعلق پاداش</summary>
    public double MinScoreForBonus { get; set; } = 60;
    [MaxLength(150)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>شاخص ارزیابی (KPI) یک دوره — جمع وزن‌های فعال باید ۱۰۰ شود</summary>
public class HrPerfKpi
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    [MaxLength(30)] public string Code { get; set; } = "";
    [MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(500)] public string? Description { get; set; }
    public double Weight { get; set; }
    public double MaxScore { get; set; } = 100;
    /// <summary>General=عمومی | Behavioral=رفتاری | Skill=مهارتی | Managerial=مدیریتی</summary>
    [MaxLength(20)] public string Category { get; set; } = "General";
    public bool IsActive { get; set; } = true;
}

/// <summary>نمره یک پرسنل در یک شاخص</summary>
public class HrPerfScore
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int EmployeeId { get; set; }
    public int KpiId { get; set; }
    public double Score { get; set; }
    public int ScoredByUserId { get; set; }
    [MaxLength(150)] public string? ScoredByName { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>کارنامه دوره: نمره کل وزنی، گرید و پاداش — Status: Draft=محاسبه‌شده | Final=قطعی (وارد حقوق می‌شود)</summary>
public class HrPerfResult
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int EmployeeId { get; set; }
    public double TotalScore { get; set; }
    [MaxLength(5)] public string Grade { get; set; } = "";
    public decimal BonusAmount { get; set; }
    public int? PayYear { get; set; }
    public int? PayMonth { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Draft";
    [MaxLength(150)] public string? FinalizedBy { get; set; }
    public DateTime? FinalizedAt { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
