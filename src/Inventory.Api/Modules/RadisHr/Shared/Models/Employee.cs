namespace RadisHr.Shared.Models;

/// <summary>پرونده پرسنلی — پورت ساختار sampleEmployees + withDefaults از assets/app.js</summary>
public class Employee
{
    public int Id { get; set; }

    // هویت
    public string Code { get; set; } = "";          // کد پرسنلی
    public string First { get; set; } = "";         // نام
    public string Last { get; set; } = "";          // نام خانوادگی
    public string Nid { get; set; } = "";           // کد ملی
    public string Married { get; set; } = "مجرد";   // وضعیت تأهل
    public int Children { get; set; }               // تعداد اولاد

    // سازمانی
    public string Unit { get; set; } = "";
    public string WorkStation { get; set; } = "";
    public string ResponsibilityLevel { get; set; } = "اپراتور / کارمند";
    public string PositionTitle { get; set; } = "";

    // استخدام
    public string Hire { get; set; } = "";          // تاریخ جذب شمسی 1402/02/06
    public string ContractType { get; set; } = "مدت معین";
    public string ContractEnd { get; set; } = "";
    public List<EmployeeContract> Contracts { get; set; } = new();

    // بیمه
    public string PrimaryInsuredCode { get; set; } = "";
    public int SupplementaryFamilyCount { get; set; }
    public decimal SupplementaryEmployeeShare { get; set; }
    public decimal SupplementaryEmployerShare { get; set; }

    // پرداخت
    public decimal Salary { get; set; }             // حقوق پایه ماهانه (ریال)
    public decimal SeniorityPay { get; set; } = 166_667m;
    public decimal AttractionPay { get; set; }
    public decimal SupervisorPay { get; set; }
    public decimal PerformancePay { get; set; }
    public decimal AgreedBenefits { get; set; }

    // بانکی
    public string BankName { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string Iban { get; set; } = "";
    public string CardNumber { get; set; } = "";
    public string BankBranch { get; set; } = "";
    public string BranchCode { get; set; } = "";

    public string Photo { get; set; } = "";         // data URI
    public bool IsActive { get; set; } = true;

    public string FullName => $"{First} {Last}";
}

public class EmployeeContract
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string Month { get; set; } = "";
    public string? FileId { get; set; }
}
