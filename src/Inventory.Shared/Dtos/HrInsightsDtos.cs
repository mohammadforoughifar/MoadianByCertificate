namespace Inventory.Shared.Dtos;

// ==================== داشبورد یکپارچه مدیر (§۶) ====================

public class HrMonthPointDto
{
    /// <summary>ماه شمسی ۱..۱۲</summary>
    public int Month { get; set; }
    public double Value { get; set; }
}

public class HrNameValueDto
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
}

public class HrManagerDashboardDto
{
    public int Year { get; set; }
    public int ActiveEmployees { get; set; }
    public int HiresThisYear { get; set; }
    public int ExitsThisYear { get; set; }
    /// <summary>نرخ ترک‌خدمت ٪ (خروجی / میانگین نفرات)</summary>
    public double TurnoverRate { get; set; }
    /// <summary>نرخ غیبت ٪ (روز مرخصی تأییدشده / نفر-روز کاری)</summary>
    public double AbsenceRate { get; set; }
    public double AvgLeaveDaysPerEmp { get; set; }
    /// <summary>میانگین هزینه سرانه ناخالص آخرین دوره (ریال)</summary>
    public double? AvgCostPerHead { get; set; }
    /// <summary>جمع ناخالص آخرین دوره (ریال)</summary>
    public double? TotalPayrollLastRun { get; set; }
    public string? LastRunLabel { get; set; }
    /// <summary>روند اضافه‌کاری ماهانه (ساعت)</summary>
    public List<HrMonthPointDto> OvertimeTrend { get; set; } = new();
    /// <summary>روند مرخصی ماهانه (روز)</summary>
    public List<HrMonthPointDto> LeaveTrend { get; set; } = new();
    /// <summary>روند حقوق ناخالص ماهانه (ریال)</summary>
    public List<HrMonthPointDto> PayrollTrend { get; set; } = new();
    public List<HrNameValueDto> LeaveByType { get; set; } = new();
    public List<HrNameValueDto> HeadcountByUnit { get; set; } = new();
}

// ==================== گزارش کیفیت داده پرسنل ====================

/// <summary>یک پرسنل با یک یا چند فیلد مهم ناقص</summary>
public class HrDataQualityRowDto
{
    public int EmployeeId { get; set; }
    public string Code { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? OrgUnitName { get; set; }
    /// <summary>نام‌های فارسی فیلدهای ناقص — مثلاً «کد ملی»، «شماره شبا»</summary>
    public List<string> MissingFields { get; set; } = new();
}

/// <summary>خلاصه گزارش کیفیت داده پرسنل — چند نفر کدام فیلد را ناقص دارند + فهرست کامل</summary>
public class HrDataQualityReportDto
{
    public int TotalActiveEmployees { get; set; }
    public int EmployeesWithIssues { get; set; }
    /// <summary>تعداد نفراتی که هر فیلد مشخص را ناقص دارند — برای نمودار/خلاصه</summary>
    public List<HrNameValueDto> MissingByField { get; set; } = new();
    public List<HrDataQualityRowDto> Rows { get; set; } = new();
}
