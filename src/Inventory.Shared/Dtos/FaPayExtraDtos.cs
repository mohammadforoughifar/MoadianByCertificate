namespace Inventory.Shared.Dtos;

// ==================== FaPay افزونه‌های §۲ (وام، معوقات، عیدی/سنوات، تسویه، بیمه) ====================

public class FaPayExtraSettingsDto
{
    public string? WorkshopCode { get; set; }
    public string? WorkshopName { get; set; }
    public double NightRatePercent { get; set; }
    public double EidiCapMultiplier { get; set; }
    public double EidiBaseMultiplier { get; set; }
}

public class FaPayExtraSettingsSaveDto
{
    public string? WorkshopCode { get; set; }
    public string? WorkshopName { get; set; }
    public double NightRatePercent { get; set; } = 35;
    public double EidiCapMultiplier { get; set; } = 3;
    public double EidiBaseMultiplier { get; set; } = 2;
}

/// <summary>حداقل دستمزد قانونی سالانه (جدول اختصاصی FaPayMinWages — جدا از فروغ آریا)</summary>
public class FaPayMinWageDto
{
    public int Year { get; set; }
    public decimal MonthlyWage { get; set; }
    public decimal DailyWage { get; set; }
    public DateTime EffectiveDate { get; set; }
    public string? Note { get; set; }
}

public class FaPayMinWageSaveDto
{
    public int Year { get; set; }
    public decimal MonthlyWage { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public string? Note { get; set; }
}

public class FaPayLoanDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeCode { get; set; }
    public string Title { get; set; } = "";
    public double TotalAmount { get; set; }
    public int InstallmentCount { get; set; }
    public double InstallmentAmount { get; set; }
    public int StartYear { get; set; }
    public int StartMonth { get; set; }
    public int Status { get; set; }
    public int PaidCount { get; set; }
    public double PaidAmount { get; set; }
    public double RemainingAmount { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<FaPayLoanInstallmentDto> Installments { get; set; } = new();
}

public class FaPayLoanInstallmentDto
{
    public int Id { get; set; }
    public int SeqNo { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public double Amount { get; set; }
    public bool IsPaid { get; set; }
}

public class FaPayLoanSaveDto
{
    public int EmployeeId { get; set; }
    public string Title { get; set; } = "";
    public double TotalAmount { get; set; }
    public int InstallmentCount { get; set; } = 1;
    public int StartYear { get; set; }
    public int StartMonth { get; set; }
    public string? Note { get; set; }
}

public class FaPayArrearDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeCode { get; set; }
    public string Title { get; set; } = "";
    public int FromYear { get; set; }
    public int FromMonth { get; set; }
    public int ToYear { get; set; }
    public int ToMonth { get; set; }
    public double Amount { get; set; }
    public int TargetYear { get; set; }
    public int TargetMonth { get; set; }
    public int Status { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FaPayArrearSaveDto
{
    public int EmployeeId { get; set; }
    public string Title { get; set; } = "";
    public int FromYear { get; set; }
    public int FromMonth { get; set; }
    public int ToYear { get; set; }
    public int ToMonth { get; set; }
    public double Amount { get; set; }
    public int TargetYear { get; set; }
    public int TargetMonth { get; set; }
    public string? Note { get; set; }
}

public class FaPaySettlementDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeCode { get; set; }
    public string? NationalCode { get; set; }
    public DateTime LeaveDate { get; set; }
    public int Reason { get; set; }
    public int ServiceDays { get; set; }
    public double BaseSalary { get; set; }
    public double SenavatAmount { get; set; }
    public double EidiAmount { get; set; }
    public double LeaveBuybackDays { get; set; }
    public double LeaveBuybackAmount { get; set; }
    public double OtherEarnings { get; set; }
    public string? OtherEarningsNote { get; set; }
    public double LoanRemaining { get; set; }
    public double OtherDeductions { get; set; }
    public string? OtherDeductionsNote { get; set; }
    public double TaxAmount { get; set; }
    public double GrossTotal { get; set; }
    public double NetPayable { get; set; }
    public int Status { get; set; }
    public bool DeactivateEmployee { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
}

public class FaPaySettlementSaveDto
{
    public int EmployeeId { get; set; }
    public DateTime LeaveDate { get; set; } = DateTime.Today;
    public int Reason { get; set; }
    public double OtherEarnings { get; set; }
    public string? OtherEarningsNote { get; set; }
    public double OtherDeductions { get; set; }
    public string? OtherDeductionsNote { get; set; }
    public bool DeactivateEmployee { get; set; } = true;
    public string? Note { get; set; }
}

public class FaPayInsuranceFileRowDto
{
    public string EmployeeName { get; set; } = "";
    public string EmployeeCode { get; set; } = "";
    public string? NationalCode { get; set; }
    public string? InsuranceNo { get; set; }
    public int WorkDays { get; set; }
    public double InsurableAmount { get; set; }
    public string? Problem { get; set; }
}

public class FaPayInsuranceFileCheckDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string? WorkshopCode { get; set; }
    public int TotalCount { get; set; }
    public int ReadyCount { get; set; }
    public double TotalInsurable { get; set; }
    public List<FaPayInsuranceFileRowDto> Rows { get; set; } = new();
}

public class FaPayCompareRowDto
{
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeCode { get; set; }
    public double? NetA { get; set; }
    public double? NetB { get; set; }
    public double? Diff { get; set; }
    public double? DiffPercent { get; set; }
    public string Flag { get; set; } = ""; // Same | Changed | New | Left
}

public class FaPayCompareDto
{
    public int RunAId { get; set; }
    public int RunBId { get; set; }
    public string LabelA { get; set; } = "";
    public string LabelB { get; set; } = "";
    public double TotalA { get; set; }
    public double TotalB { get; set; }
    public List<FaPayCompareRowDto> Rows { get; set; } = new();
}
