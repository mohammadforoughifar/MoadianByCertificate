namespace Inventory.Shared.Dtos;

// ============================================================
//  منابع انسانی اصلی — مدیریت پایه سازمانی (HrMain) — DTOها
// ============================================================

public class HrMainCompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? LogoPath { get; set; }
    public string? Address { get; set; }
    public string? EconomicCode { get; set; }
    public string? RegistrationNo { get; set; }
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? ManagerName { get; set; }
}

public class HrMainCompanySaveDto
{
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? EconomicCode { get; set; }
    public string? RegistrationNo { get; set; }
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? ManagerName { get; set; }
}

public class HrMainBranchDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? ManagerName { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int NodeCount { get; set; }
}

public class HrMainBranchSaveDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? ManagerName { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class HrMainOrgNodeDto
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public int Level { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentName { get; set; }
    public int? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? ManagerTitle { get; set; }
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public int PositionCount { get; set; }
    /// <summary>تعداد پرسنل فعال شاغل مستقیم در این گره (برای چارت سازمانی تصویری)</summary>
    public int EmployeeCount { get; set; }
    public List<HrMainOrgNodeDto> Children { get; set; } = new();
}

public class HrMainOrgNodeSaveDto
{
    public int? ParentId { get; set; }
    public int Level { get; set; } = 1;
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int? BranchId { get; set; }
    public string? ManagerTitle { get; set; }
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HrMainPositionDto
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string Title { get; set; } = "";
    public int? OrgNodeId { get; set; }
    public string? OrgNodeName { get; set; }
    public string? Grade { get; set; }
    public string? JobDescription { get; set; }
    public string? Requirements { get; set; }
    public int HeadCount { get; set; }
    public bool IsActive { get; set; }

    /// <summary>تعداد پرسنل فعالی که هم‌اکنون روی این پست منصوب هستند</summary>
    public int AssignedCount { get; set; }

    /// <summary>ظرفیت خالی = تعداد مصوب − تعداد منصوب (هرگز منفی نیست)</summary>
    public int VacantCount => Math.Max(0, HeadCount - AssignedCount);
}

public class HrMainPositionSaveDto
{
    public string? Code { get; set; }
    public string Title { get; set; } = "";
    public int? OrgNodeId { get; set; }
    public string? Grade { get; set; }
    public string? JobDescription { get; set; }
    public string? Requirements { get; set; }
    public int HeadCount { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public class HrMainLocaleDto
{
    public string Language { get; set; } = "fa";
    public string Calendar { get; set; } = "jalali";
}

public class HrMainRulesDto
{
    public int AnnualLeaveDays { get; set; } = 26;
    public int MaxConsecutiveLeaveDays { get; set; } = 9;
    public int MaxCarryOverDays { get; set; } = 9;
    public bool UnpaidLeaveAllowed { get; set; } = true;
    public int LateGraceMinutes { get; set; } = 10;
    public int MonthlyAllowedLateMinutes { get; set; } = 60;
    public int MaxLateWithoutLeaveMinutes { get; set; } = 120;
    public decimal LateDeductionFactor { get; set; } = 1.0m;
    public decimal AbsenceDailyDeductionFactor { get; set; } = 1.0m;
    public int UnexcusedAbsenceWarningAfter { get; set; } = 3;
    public decimal OvertimeFactor { get; set; } = 1.4m;
    public int MaxMonthlyOvertimeHours { get; set; } = 60;
    public bool OvertimeNeedsApproval { get; set; } = true;
    public int ContractAlertDays { get; set; } = 30;

    // ---------- ساعت کاری پیش‌فرض ----------
    public TimeSpan DefaultWorkStartTime { get; set; } = new(8, 0, 0);
    public TimeSpan DefaultWorkEndTime { get; set; } = new(17, 0, 0);
    /// <summary>روزهای تعطیل هفتگی با کاما — اعداد DayOfWeek (پیش‌فرض "5" = جمعه)</summary>
    public string? DefaultWeeklyOffDays { get; set; } = "5";
}

/// <summary>تعطیلات رسمی/شرکتی — نگاشت روی جدول موجود CompanyHoliday</summary>
public class HrMainHolidayDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Name { get; set; } = "";
    public bool IsOfficial { get; set; }
    public string? CreatedByName { get; set; }
}

public class HrMainHolidaySaveDto
{
    public DateTime Date { get; set; } = DateTime.Today;
    public string Name { get; set; } = "";
    public bool IsOfficial { get; set; } = true;
}

/// <summary>نمای کلی منابع انسانی اصلی برای هاب/داشبورد</summary>
public class HrMainOverviewDto
{
    public string? CompanyName { get; set; }
    public string? LogoPath { get; set; }
    public int ActiveBranches { get; set; }
    public int ActiveNodes { get; set; }
    public int ActivePositions { get; set; }
    public int ActiveEmployees { get; set; }

    /// <summary>مجموع ظرفیت خالی پست‌های فعال (تعداد مصوب منهای پرسنل فعال منصوب)</summary>
    public int VacantPositions { get; set; }
    public int HolidaysThisYear { get; set; }
    public int CurrentJalaliYear { get; set; }
}

/// <summary>یک ردیف تاریخچه تغییرات ساختار سازمانی (گره/پست)</summary>
public class HrMainChangeLogDto
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    /// <summary>OrgNode یا Position</summary>
    public string Entity { get; set; } = "";
    /// <summary>Create، Update یا Delete</summary>
    public string Action { get; set; } = "";
    public int EntityId { get; set; }
    public string EntityName { get; set; } = "";
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string ByUsername { get; set; } = "";
}

/// <summary>نتیجه صفحه‌بندی‌شده تاریخچه تغییرات</summary>
public class HrMainChangeLogListResult
{
    public int Total { get; set; }
    public List<HrMainChangeLogDto> Items { get; set; } = new();
}

/// <summary>یک آیتم تفاوت بین دو تاریخ برای مقایسه ساختار سازمانی</summary>
public class HrMainCompareItemDto
{
    public string Entity { get; set; } = "";
    public string Action { get; set; } = "";
    public int EntityId { get; set; }
    public string EntityName { get; set; } = "";
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime At { get; set; }
    public string ByUsername { get; set; } = "";
}

/// <summary>نتیجه مقایسه ساختار سازمانی بین دو تاریخ</summary>
public class HrMainCompareResultDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int NodesCreated { get; set; }
    public int NodesUpdated { get; set; }
    public int NodesDeleted { get; set; }
    public int PositionsCreated { get; set; }
    public int PositionsUpdated { get; set; }
    public int PositionsDeleted { get; set; }
    public List<HrMainCompareItemDto> Items { get; set; } = new();
}
