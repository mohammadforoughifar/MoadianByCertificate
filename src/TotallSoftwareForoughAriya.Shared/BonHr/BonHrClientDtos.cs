namespace Inventory.Shared.BonHr;

public class MonthOption
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

public class UnitCount
{
    public string Unit { get; set; } = "";
    public int Count { get; set; }
}

public class DashboardDto
{
    public int EmployeeCount { get; set; }
    public int ActiveEmployees { get; set; }
    public List<UnitCount> ByUnit { get; set; } = new();
    public string? LastMonth { get; set; }
    public string LastMonthLabel { get; set; } = "";
    public decimal LastMonthNet { get; set; }
    public decimal LastMonthOvertimeHours { get; set; }
    public decimal LastMonthShortfallHours { get; set; }
    public decimal LastMonthInsurance { get; set; }
    public decimal LastMonthTax { get; set; }
    public int IncidentsThisYear { get; set; }
    public int LostDaysThisYear { get; set; }
    public int OpenAdvances { get; set; }
    public int UnseenNotices { get; set; }
}

public class ChartPoint
{
    public string Label { get; set; } = "";
    public string ShortLabel { get; set; } = "";
    public decimal Value { get; set; }
}

public class ExecutiveDto
{
    public string Message { get; set; } = "";
    public List<ChartPoint> Overtime { get; set; } = new();
    public List<ChartPoint> Payment { get; set; } = new();
    public List<ChartPoint> Leave { get; set; } = new();
    public string LeaveSubtitle { get; set; } = "";
    public string LeaveMode { get; set; } = "months";
}

public class AccountingRowDto
{
    public PayrollRow Row { get; set; } = new();
    public AccountingAdjustment? Adjustment { get; set; }
    public decimal FinalNet { get; set; }
    public bool Paid { get; set; }
}

public class AccountingMonthDto
{
    public string Month { get; set; } = "";
    public string Status { get; set; } = "در حال بررسی";
    public bool Locked { get; set; }
    public List<AccountingRowDto> Rows { get; set; } = new();
    public decimal TotalNet { get; set; }
    public int EmployeeCount { get; set; }
    public AccountingArchive? Archive { get; set; }
}

public class CalendarCellDto
{
    public string Date { get; set; } = "";
    public int Day { get; set; }
    public int Weekday { get; set; }
    public bool IsFriday { get; set; }
    public bool IsHoliday { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
}

public class CalendarMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = "";
    public int Days { get; set; }
    public int LeadingBlanks { get; set; }
    public List<CalendarCellDto> Cells { get; set; } = new();
}

public class MatrixPersonDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ResponsibilityLevel { get; set; } = "";
    public string PositionTitle { get; set; } = "";
}

public class MatrixStationDto
{
    public string Station { get; set; } = "";
    public List<MatrixPersonDto> Employees { get; set; } = new();
}

public class MatrixUnitDto
{
    public string Unit { get; set; } = "";
    public List<MatrixStationDto> Stations { get; set; } = new();
    public List<MatrixPersonDto> Unassigned { get; set; } = new();
}

public class HseDefinitionsDto
{
    public List<string> IncidentTypes { get; set; } = new();
    public List<string> PpeTypes { get; set; } = new();
    public List<string> ExtinguisherTypes { get; set; } = new();
    public List<string> Severities { get; set; } = new();
    public List<string> Shifts { get; set; } = new();
    public List<string> Conditions { get; set; } = new();
    public List<string> EarlyReplacementReasons { get; set; } = new();
}

public class CountPair
{
    public string Severity { get; set; } = "";
    public string Type { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Month { get; set; } = "";
    public int Count { get; set; }
}

public class HseReportsDto
{
    public int TotalIncidents { get; set; }
    public int LostDays { get; set; }
    public List<CountPair> BySeverity { get; set; } = new();
    public List<CountPair> ByType { get; set; } = new();
    public List<CountPair> ByUnit { get; set; } = new();
    public List<CountPair> ByMonth { get; set; } = new();
    public int OpenActions { get; set; }
    public int PpeDeliveries { get; set; }
    public int PpePendingApproval { get; set; }
    public int EarlyReplacements { get; set; }
    public int ExtinguishersTotal { get; set; }
    public int ExtinguishersExpired { get; set; }
}
