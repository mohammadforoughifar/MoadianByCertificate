namespace Inventory.Shared.BonHr;

public record EmployeeListItem(
    int Id, string Code, string First, string Last, string Nid, string Unit,
    string WorkStation, string ResponsibilityLevel, string PositionTitle,
    string Married, int Children, decimal Salary, string Hire);

public record ImportEmployeesRequest(List<Employee> Employees, bool Overwrite);
public record ImportEmployeesResult(int Inserted, int Updated, int Skipped, List<string> Errors);

public record AttendanceImportResult(int Rows, int Inserted, int Ignored, string From, string To);
public record RecalculateResult(Dictionary<string, List<PayrollRow>> RowsByMonth, int MonthCount, int RowCount);
public record DailyReportRow(
    string Code, string Name, string Presence, string Status,
    string Leave, string Shortfall, string Ot,
    string MonthPresence, string MonthLeave, string MonthShortfall, string MonthOt);

public record PayslipRequest(string EmployeeCode, int Year, int Month,
    decimal? Overtime, decimal? ShortfallPay, decimal? Mission,
    decimal? Shift, decimal? Night, decimal? Friday);

public class PayrollResult
{
    public decimal Base { get; set; }
    public decimal Seniority { get; set; }
    public decimal Housing { get; set; }
    public decimal Food { get; set; }
    public decimal Marriage { get; set; }
    public decimal Child { get; set; }
    public decimal Children { get; set; }
    public decimal Overtime { get; set; }
    public decimal Shift { get; set; }
    public decimal Night { get; set; }
    public decimal Friday { get; set; }
    public decimal Mission { get; set; }
    public decimal Attraction { get; set; }
    public decimal Supervisor { get; set; }
    public decimal Performance { get; set; }
    public decimal Agreed { get; set; }
    public decimal ShortfallPay { get; set; }
    public decimal Gross { get; set; }
    public decimal InsuranceBase { get; set; }
    public decimal MaximumInsuranceBase { get; set; }
    public decimal Insurance { get; set; }
    public decimal EmployerInsurance { get; set; }
    public decimal TaxableIncome { get; set; }
    public decimal Tax { get; set; }
    public decimal Net { get; set; }
}

public record PayslipResponse(Employee Employee, PayrollResult Result, StatutoryRules Rules, string MonthLabel);

public record ExecutiveSeriesPoint(string Month, string MonthLabel, decimal Gross, decimal Net,
    decimal Tax, decimal Insurance, decimal OtPay, decimal ShortfallPay, int Headcount);

public record ExecutiveAnalytics(List<ExecutiveSeriesPoint> Series, decimal TotalNet, decimal TotalGross,
    decimal AverageNet, int Months);

public record DashboardStats(
    int EmployeeCount, decimal AverageSalary, decimal MonthlyPayroll,
    List<UnitSalaryPoint> UnitSalaries, List<TenureBucket> Tenure);

public record UnitSalaryPoint(string Unit, decimal Average, decimal Total, int Count, double Share);
public record TenureBucket(string Label, int Count, double Percent);

public record BonHrApiMessage(bool Ok, string Message);
public record BonHrIdResponse(int Id, string Message);
