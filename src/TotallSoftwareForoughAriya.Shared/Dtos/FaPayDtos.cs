namespace Inventory.Shared.Dtos;

// ==================== FaPay (§۸ حقوق و دستمزد) ====================

public class FaPaySettingsDto
{
    public double DaysPerMonth { get; set; }
    public double HoursPerDay { get; set; }
    public double OvertimeFactor { get; set; }
    public double DelayFactor { get; set; }
    public double InsuranceEmployeeRate { get; set; }
    public double InsuranceEmployerRate { get; set; }
    public double TaxFreeMonthly { get; set; }
    public bool AbsentDeductEnabled { get; set; }
    public bool UnpaidLeaveDeductEnabled { get; set; }
    public string? Note { get; set; }
}

public class FaPaySettingsSaveDto
{
    public double DaysPerMonth { get; set; } = 30;
    public double HoursPerDay { get; set; } = 8;
    public double OvertimeFactor { get; set; } = 1.4;
    public double DelayFactor { get; set; } = 1.0;
    public double InsuranceEmployeeRate { get; set; } = 7;
    public double InsuranceEmployerRate { get; set; } = 23;
    public double TaxFreeMonthly { get; set; }
    public bool AbsentDeductEnabled { get; set; } = true;
    public bool UnpaidLeaveDeductEnabled { get; set; } = true;
    public string? Note { get; set; }
}

public class FaPayTaxBracketDto
{
    public int Id { get; set; }
    public double FromAmount { get; set; }
    public double? ToAmount { get; set; }
    public double Rate { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class FaPayTaxBracketSaveDto
{
    public double FromAmount { get; set; }
    public double? ToAmount { get; set; }
    public double Rate { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FaPayItemTypeDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Kind { get; set; }
    public bool IsFixed { get; set; }
    public double DefaultAmount { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class FaPayItemTypeSaveDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Kind { get; set; }
    public bool IsFixed { get; set; }
    public double DefaultAmount { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FaPayRunDto
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int Status { get; set; }
    public int Kind { get; set; }
    public int SlipsCount { get; set; }
    public double TotalNet { get; set; }
    public string? Note { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
}

public class FaPaySlipItemDto
{
    public int Id { get; set; }
    public int? ItemTypeId { get; set; }
    public string Title { get; set; } = "";
    public int Kind { get; set; }
    public double Amount { get; set; }
    public bool IsAuto { get; set; }
    public string? Note { get; set; }
}

public class FaPaySlipDto
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public int RunYear { get; set; }
    public int RunMonth { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeCode { get; set; }
    public string? OrgUnitName { get; set; }
    public double BaseSalary { get; set; }
    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public double UnpaidLeaveDays { get; set; }
    public int OvertimeMinutes { get; set; }
    public double OvertimeAmount { get; set; }
    public int DelayMinutes { get; set; }
    public double DelayAmount { get; set; }
    public int MissionDays { get; set; }
    public double AbsentAmount { get; set; }
    public double GrossEarnings { get; set; }
    public double TotalDeductions { get; set; }
    public double TaxAmount { get; set; }
    public double InsuranceAmount { get; set; }
    public double NetPay { get; set; }
    public bool IsPaid { get; set; }
    public List<FaPaySlipItemDto> Items { get; set; } = new();
}

public class FaPayBankRowDto
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string? NationalCode { get; set; }
    public string Sheba { get; set; } = "";
    public string? BankName { get; set; }
    public double Amount { get; set; }
}

public class FaPayBankMissingDto
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Reason { get; set; } = "";
}

public class FaPayBankCheckDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalCount { get; set; }
    public int ReadyCount { get; set; }
    public double TotalAmount { get; set; }
    public List<FaPayBankRowDto> Rows { get; set; } = new();
    public List<FaPayBankMissingDto> Missing { get; set; } = new();
}

public class FaPayAdjustmentDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int ItemTypeId { get; set; }
    public string? ItemTypeName { get; set; }
    public int Kind { get; set; }
    public double Amount { get; set; }
    public string? Note { get; set; }
}

public class FaPayAdjustmentSaveDto
{
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int ItemTypeId { get; set; }
    public double Amount { get; set; }
    public string? Note { get; set; }
}

public class FaPayRunSaveDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Kind { get; set; }
    public string? Note { get; set; }
}

public class FaPayMailResultDto
{
    public int Sent { get; set; }
    public int Skipped { get; set; }
}

public class FaPayInsuranceRowDto
{
    public int EmployeeId { get; set; }
    public string? EmployeeCode { get; set; }
    public string? EmployeeName { get; set; }
    public double BaseSalary { get; set; }
    public double InsurableAmount { get; set; }
    public double EmployeeShare { get; set; }
    public double EmployerShare { get; set; }
}

public class FaPayInsuranceReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<FaPayInsuranceRowDto> Rows { get; set; } = new();
    public double TotalInsurable { get; set; }
    public double TotalEmployee { get; set; }
    public double TotalEmployer { get; set; }
}

public class FaPayTaxRowDto
{
    public int EmployeeId { get; set; }
    public string? EmployeeCode { get; set; }
    public string? EmployeeName { get; set; }
    public double Gross { get; set; }
    public double Taxable { get; set; }
    public double Tax { get; set; }
}

public class FaPayTaxReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<FaPayTaxRowDto> Rows { get; set; } = new();
    public double TotalGross { get; set; }
    public double TotalTaxable { get; set; }
    public double TotalTax { get; set; }
}

public class FaPayUnitCostRowDto
{
    public int? OrgUnitId { get; set; }
    public string? OrgUnitName { get; set; }
    public int Headcount { get; set; }
    public double GrossTotal { get; set; }
    public double EmployerInsurance { get; set; }
    public double TotalCost { get; set; }
}

public class FaPayUnitCostReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<FaPayUnitCostRowDto> Rows { get; set; } = new();
    public double TotalGross { get; set; }
    public double TotalEmployerInsurance { get; set; }
    public double TotalCost { get; set; }
}
