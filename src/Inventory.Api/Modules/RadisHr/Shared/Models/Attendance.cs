namespace RadisHr.Shared.Models;

/// <summary>رکورد روزانهٔ حضور — معادل emptyDay در attendance-engine.js</summary>
public class AttendanceDay
{
    public int Id { get; set; }
    /// <summary>کلید یکتا: {code}|{date} — سیاست append-only بر همین کلید استوار است</summary>
    public string Key { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DeviceCode { get; set; } = "";
    public string Date { get; set; } = "";      // 1405/01/05
    public string Month { get; set; } = "";     // 1405/01

    public string First { get; set; } = "";     // اولین تردد
    public string Last { get; set; } = "";      // آخرین تردد

    public int Presence { get; set; }           // دقیقه
    public int Leave { get; set; }
    public int Mission { get; set; }
    public int Shortfall { get; set; }
    public int Ot { get; set; }
    public int UnauthorizedOt { get; set; }

    public string Source { get; set; } = "device";  // device | raw
    public string Status { get; set; } = "";
    public bool ManualCorrectionApplied { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>مرخصی و مأموریت دستی — radisHrLeaveMissionV005</summary>
public class LeaveMission
{
    public int Id { get; set; }
    public string Employee { get; set; } = "";  // کد پرسنلی
    public string Date { get; set; } = "";
    public string Type { get; set; } = "";      // «مرخصی ساعتی» / «مرخصی روزانه» / «مأموریت ...»
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Note { get; set; } = "";
    public string RegisteredBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>اصلاحیهٔ دستی تردد — radisHrManualPunchesV010</summary>
public class ManualPunch
{
    public int Id { get; set; }
    public string Employee { get; set; } = "";
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
    public string Type { get; set; } = "entry"; // entry | exit
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "pending"; // pending | approved | rejected
    public string RequestedBy { get; set; } = "";
    public string? ReviewedBy { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>گزارش ممیزی بارگذاری فایل دستگاه — radisHrImportAuditV009</summary>
public class ImportAudit
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
    public int Rows { get; set; }
    public int Inserted { get; set; }
    public int Ignored { get; set; }
    public string Policy { get; set; } = "append-only";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string ImportedBy { get; set; } = "";
}

/// <summary>ساعات کاری واحد — radisHrUnitSchedulesV006</summary>
public class UnitSchedule
{
    public int Id { get; set; }
    public string Unit { get; set; } = "";
    public string WorkStart { get; set; } = "08:00";
    public string WorkEnd { get; set; } = "16:00";
    public int EntryGrace { get; set; } = 5;
    public int EarlyEntryGrace { get; set; } = 5;
    public int ExitGrace { get; set; } = 5;
    public int LateExitGrace { get; set; } = 5;
    public int MaxLateWithoutLeave { get; set; } = 30;
    public int MaxEarlyWithoutLeave { get; set; } = 30;
    public string AbsenceStrategy { get; set; } = "leaveThenDeduction";
}

/// <summary>روز تعطیل سازمانی — radisHrHolidaysV006</summary>
public class Holiday
{
    public int Id { get; set; }
    public string Date { get; set; } = "";      // 1405/01/01
    public string Title { get; set; } = "تعطیل";
    public string Type { get; set; } = "friday"; // friday | other
}

/// <summary>چرخهٔ شیفت نگهبانی — radisHrGuardCyclesV010</summary>
public class GuardCycle
{
    public int Id { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string StartShift { get; set; } = "morning"; // morning | night
    public string RegisteredBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>نتیجهٔ محاسبه‌شدهٔ ماهانهٔ حقوق — radisHrCalculatedPayrollV009</summary>
public class PayrollRow
{
    public int Id { get; set; }
    public string Month { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DeviceCode { get; set; } = "—";
    public string Status { get; set; } = "";

    // ساعت:دقیقه
    public string Presence { get; set; } = "0:00";
    public string Leave { get; set; } = "0:00";
    public string Mission { get; set; } = "0:00";
    public string Shortfall { get; set; } = "0:00";
    public string Ot { get; set; } = "0:00";
    public string UnauthorizedOt { get; set; } = "0:00";

    // مبالغ
    public decimal Base { get; set; }
    public decimal Seniority { get; set; }
    public decimal Housing { get; set; }
    public decimal Food { get; set; }
    public decimal Marriage { get; set; }
    public decimal Children { get; set; }
    public decimal Attraction { get; set; }
    public decimal Supervisor { get; set; }
    public decimal Performance { get; set; }
    public decimal Agreed { get; set; }
    public decimal ShiftPay { get; set; }
    public decimal NightPay { get; set; }
    public decimal FridayPay { get; set; }
    public decimal MissionPay { get; set; }
    public decimal OtPay { get; set; }
    public decimal ShortfallPay { get; set; }
    public decimal Gross { get; set; }
    public decimal InsuranceBase { get; set; }
    public decimal Insurance { get; set; }
    public decimal EmployerInsurance { get; set; }
    public decimal TaxableIncome { get; set; }
    public decimal Tax { get; set; }
    public decimal Net { get; set; }

    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}
