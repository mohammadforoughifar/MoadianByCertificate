namespace Inventory.Shared.Dtos;

// ============================================================
//  حضور و غیاب فروغ آریا (FaAtt) — DTOها
// ============================================================

public class FaAttShiftDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int LateToleranceMin { get; set; }
    public int EarlyToleranceMin { get; set; }
    public int OvertimeGraceMin { get; set; }
    public int RequiredMinutes { get; set; }
    public string? OffDays { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; }
    public double AllowancePercent { get; set; }
    public int SortOrder { get; set; }
    /// <summary>شب‌کاری گذرنده از نیمه‌شب (پایان کوچک‌تر/مساوی شروع)</summary>
    public bool IsOvernight => EndTime <= StartTime;
}

public class FaAttShiftSaveDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public TimeSpan StartTime { get; set; } = new(8, 0, 0);
    public TimeSpan EndTime { get; set; } = new(17, 0, 0);
    public int LateToleranceMin { get; set; }
    public int EarlyToleranceMin { get; set; }
    public int OvertimeGraceMin { get; set; } = 10;
    public int RequiredMinutes { get; set; } = 480;
    public string? OffDays { get; set; } = "5";
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public double AllowancePercent { get; set; }
    public int SortOrder { get; set; }
}

public class FaAttShiftAssignDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public int ShiftId { get; set; }
    public string? ShiftName { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class FaAttShiftAssignSaveDto
{
    public int EmployeeId { get; set; }
    public int ShiftId { get; set; }
    public DateTime FromDate { get; set; } = DateTime.Today;
    public DateTime? ToDate { get; set; }
}

public class FaAttDeviceDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public string? Location { get; set; }
    public string? SerialNo { get; set; }
    public string? IpAddress { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public int LogsCount { get; set; }
}

public class FaAttDeviceSaveDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public string? Location { get; set; }
    public string? SerialNo { get; set; }
    public string? IpAddress { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FaAttLogDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime Timestamp { get; set; }
    public int Type { get; set; }
    public int Source { get; set; }
    public int? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Note { get; set; }
    public string? CreatedByName { get; set; }
}

/// <summary>ثبت دستی ادمین / ایمپورت دستگاه</summary>
public class FaAttLogSaveDto
{
    public int EmployeeId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int Type { get; set; } = 2;
    public int Source { get; set; } = 3;
    public int? DeviceId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Note { get; set; }
}

public class FaAttImportResultDto
{
    public int Added { get; set; }
    public int Skipped { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>درخواست وب‌کلاک کاربر جاری (پرسنل از روی کاربر لاگین تشخیص داده می‌شود)</summary>
public class FaAttClockSaveDto
{
    /// <summary>0=ورود 1=خروج</summary>
    public int Type { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class FaAttDailyDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeCode { get; set; }
    public DateTime Date { get; set; }
    public int? ShiftId { get; set; }
    public string? ShiftName { get; set; }
    public DateTime? FirstIn { get; set; }
    public DateTime? LastOut { get; set; }
    public int WorkMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public int NightMinutes { get; set; }
    public int Status { get; set; }
    public bool IsIncomplete { get; set; }
    public string? Note { get; set; }
}

/// <summary>وضعیت امروز کاربر جاری برای صفحه وب‌کلاک</summary>
public class FaAttMyTodayDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTime Date { get; set; }
    public string? ShiftName { get; set; }
    public TimeSpan? ShiftStart { get; set; }
    public TimeSpan? ShiftEnd { get; set; }
    public FaAttDailyDto? Daily { get; set; }
    public List<FaAttLogDto> Logs { get; set; } = new();
}

public class FaAttMissionDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string? Destination { get; set; }
    public string? Reason { get; set; }
    public int Status { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
}

public class FaAttMissionSaveDto
{
    public int EmployeeId { get; set; }
    public DateTime FromDate { get; set; } = DateTime.Today;
    public DateTime ToDate { get; set; } = DateTime.Today;
    public string? Destination { get; set; }
    public string? Reason { get; set; }
}

public class FaAttLeaveTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? AnnualLimitDays { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public class FaAttLeaveTypeSaveDto
{
    public string Name { get; set; } = "";
    public int? AnnualLimitDays { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class FaAttLeaveDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public int LeaveTypeId { get; set; }
    public string? LeaveTypeName { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public double? HoursPerDay { get; set; }
    public string? Reason { get; set; }
    public int Status { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public int WorkflowStep { get; set; }
    public string? ManagerDecidedByName { get; set; }
    public DateTime? ManagerDecidedAt { get; set; }
}

public class FaAttDecideBatchDto
{
    public List<int> Ids { get; set; } = new();
    public bool Approve { get; set; }
}

public class FaAttBatchResultDto
{
    public int Ok { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class FaAttCalendarEventDto
{
    /// <summary>Leave | Mission | Shift | Training | Holiday</summary>
    public string Kind { get; set; } = "";
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string Title { get; set; } = "";
    public string? Color { get; set; }
    /// <summary>0=در انتظار، 1=تأییدشده/فعال</summary>
    public int Status { get; set; }
}

public class FaAttLeaveSaveDto
{
    public int EmployeeId { get; set; }
    public int LeaveTypeId { get; set; }
    public DateTime FromDate { get; set; } = DateTime.Today;
    public DateTime ToDate { get; set; } = DateTime.Today;
    public double? HoursPerDay { get; set; }
    public string? Reason { get; set; }
}

/// <summary>خلاصه ماهانه یک پرسنل — مبنای ورودی حقوق و دستمزد</summary>
public class FaAttMonthSummaryDto
{
    public int EmployeeId { get; set; }
    public string? EmployeeCode { get; set; }
    public string? EmployeeName { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public int LateCount { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public int WorkMinutes { get; set; }
    public int MissionDays { get; set; }
    public int LeaveDays { get; set; }
    public int HolidayWorkDays { get; set; }
    public int IncompleteDays { get; set; }
    public int NightMinutes { get; set; }
}

public class FaAttLeaveBalanceDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeCode { get; set; }
    public int Year { get; set; }
    public int LeaveTypeId { get; set; }
    public string? LeaveTypeName { get; set; }
    public double EntitledDays { get; set; }
    public double UsedDays { get; set; }
    public double CarriedDays { get; set; }
    public double CashedDays { get; set; }
    public double Remaining { get; set; }
    public double? CashAmount { get; set; }
    public string? Note { get; set; }
}

public class FaAttLeaveBalanceSaveDto
{
    public double EntitledDays { get; set; }
    public string? Note { get; set; }
}

public class FaAttLeaveCarryDto
{
    public int FromYear { get; set; }
    public double MaxDays { get; set; } = 9;
}

public class FaAttLeaveCashDto
{
    public double Days { get; set; }
    public double Amount { get; set; }
    public string? Note { get; set; }
}

public class FaAttInitYearDto
{
    public int Year { get; set; }
    public int? LeaveTypeId { get; set; }
}

// ==================== برنامه‌ریزی گرافیکی شیفت ====================

/// <summary>یک روز از برنامه‌ی شیفت — ShiftId صفر/تهی یعنی فقط پاک‌کردن تخصیص آن روز</summary>
public class FaAttShiftPlanItemDto
{
    public int EmployeeId { get; set; }
    public int? ShiftId { get; set; }
    public DateTime Date { get; set; }
}

public class FaAttShiftPlanSaveDto
{
    public List<FaAttShiftPlanItemDto> Items { get; set; } = new();
}

public class FaAttShiftPlanResultDto
{
    public int Applied { get; set; }
}
