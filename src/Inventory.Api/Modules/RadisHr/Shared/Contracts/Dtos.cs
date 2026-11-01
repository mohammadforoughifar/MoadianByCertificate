using RadisHr.Shared.Calculations;
using RadisHr.Shared.Models;

namespace RadisHr.Shared.Contracts;

// ───────────────── احراز هویت ─────────────────
public record LoginRequest(string UserKey, string Password);

public record LoginResponse(
    string Token,
    string UserKey,
    string DisplayName,
    string RoleTitle,
    string[] Pages,
    string Home,
    bool MustChangePassword,
    int PasswordAgeDays,
    DateTime ExpiresAt);

public record ChangePasswordRequest(string UserKey, string CurrentPassword, string NewPassword);
public record ResetPasswordRequest(string UserKey);
public record UserAccountInfo(string UserKey, string DisplayName, string RoleTitle, bool MustChangePassword, DateTime? PasswordChangedAt, DateTime? LastLoginAt);

// ───────────────── پرسنل ─────────────────
public record EmployeeListItem(
    int Id, string Code, string First, string Last, string Nid, string Unit,
    string WorkStation, string ResponsibilityLevel, string PositionTitle,
    string Married, int Children, decimal Salary, string Hire);

public record ImportEmployeesRequest(List<Employee> Employees, bool Overwrite);
public record ImportEmployeesResult(int Inserted, int Updated, int Skipped, List<string> Errors);

// ───────────────── حضور و غیاب ─────────────────
public record AttendanceImportResult(int Rows, int Inserted, int Ignored, string From, string To);
public record RecalculateResult(Dictionary<string, List<PayrollRow>> RowsByMonth, int MonthCount, int RowCount);
public record DailyReportRow(
    string Code, string Name, string Presence, string Status,
    string Leave, string Shortfall, string Ot,
    string MonthPresence, string MonthLeave, string MonthShortfall, string MonthOt);

// ───────────────── حقوق و دستمزد ─────────────────
public record PayslipRequest(string EmployeeCode, int Year, int Month,
    decimal? Overtime, decimal? ShortfallPay, decimal? Mission,
    decimal? Shift, decimal? Night, decimal? Friday);

public record PayslipResponse(Employee Employee, PayrollResult Result, StatutoryRules Rules, string MonthLabel);

// ───────────────── تحلیل مدیریتی ─────────────────
public record ExecutiveSeriesPoint(string Month, string MonthLabel, decimal Gross, decimal Net,
    decimal Tax, decimal Insurance, decimal OtPay, decimal ShortfallPay, int Headcount);

public record ExecutiveAnalytics(List<ExecutiveSeriesPoint> Series, decimal TotalNet, decimal TotalGross,
    decimal AverageNet, int Months);

public record DashboardStats(
    int EmployeeCount, decimal AverageSalary, decimal MonthlyPayroll,
    List<UnitSalaryPoint> UnitSalaries, List<TenureBucket> Tenure);

public record UnitSalaryPoint(string Unit, decimal Average, decimal Total, int Count, double Share);
public record TenureBucket(string Label, int Count, double Percent);

// ───────────────── عمومی ─────────────────
public record ApiMessage(bool Ok, string Message);
public record IdResponse(int Id, string Message);
