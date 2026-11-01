using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// =====================================================================
// ماژول حقوق و دستمزد (HrPay)
// فرمول‌ساز، محاسبه از کارکرد، بیمه/مالیات، وام، فیش، خروجی‌ها، تسویه
// =====================================================================

/// <summary>آیتم حقوقی (فرمول‌ساز) — Kind: Earning|Deduction — Category: Hokmi|Mazaya|Kosoor|Other</summary>
public class HrPayItem
{
    public int Id { get; set; }
    [MaxLength(30)] public string Code { get; set; } = "";
    [MaxLength(150)] public string Title { get; set; } = "";
    [MaxLength(20)] public string Kind { get; set; } = "Earning";
    [MaxLength(20)] public string Category { get; set; } = "Mazaya";
    /// <summary>فرمول (null = مبلغ ثابت). متغیرها: BASE, DAILY_WAGE, HOURLY_WAGE, MIN_WAGE, DAYS_PAID, OT_H, ... + کد آیتم‌های دیگر</summary>
    [MaxLength(1000)] public string? Formula { get; set; }
    public decimal DefaultAmount { get; set; }
    public bool IsTaxable { get; set; } = true;
    public bool IsInsuranceable { get; set; } = true;
    public bool IsActive { get; set; } = true;
    /// <summary>ذخیره (هزینه کارفرما — در ناخالص/خالص پرسنل اثر ندارد)</summary>
    public bool IsAccrual { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>مبلغ اختصاصی یک آیتم برای یک پرسنل (جایگزین فرمول/مبلغ پیش‌فرض)</summary>
public class HrPayEmployeeItem
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int ItemId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>پروفایل حقوقی پرسنل: اولاد، بیمه، بانکی</summary>
public class HrPayProfile
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int ChildrenCount { get; set; }
    [MaxLength(30)] public string? InsuranceNo { get; set; }
    [MaxLength(60)] public string? BankName { get; set; }
    [MaxLength(30)] public string? Iban { get; set; }
    [MaxLength(30)] public string? AccountNo { get; set; }
    /// <summary>معافیت مالیاتی اضافه (ریال/ماه — جانبازی و...)</summary>
    public decimal ExtraTaxExempt { get; set; }
    /// <summary>شغل سخت و زیان‌آور (۴٪ اضافه بیمه سهم کارفرما)</summary>
    public bool IsHardJob { get; set; }
}

/// <summary>پلکان مالیات حقوق یک سال (مبالغ ماهانه به ریال، نرخ به درصد)</summary>
public class HrPayTaxBracket
{
    public int Id { get; set; }
    public int Year { get; set; }
    public decimal FromAmount { get; set; }
    /// <summary>0 = تا بی‌نهایت</summary>
    public decimal ToAmount { get; set; }
    public decimal Rate { get; set; }
}

/// <summary>وام و مساعده — Kind: Loan|Advance — Status: Active|Paid|Cancelled</summary>
public class HrPayLoan
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    [MaxLength(20)] public string Kind { get; set; } = "Loan";
    public decimal Amount { get; set; }
    public int Installments { get; set; } = 1;
    public decimal MonthlyAmount { get; set; }
    public int PaidCount { get; set; }
    public int StartYear { get; set; }
    public int StartMonth { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Active";
    [MaxLength(300)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>حقوق معوقه (طلبکار پرسنل — در فیش ماه هدف اضافه می‌شود)</summary>
public class HrPayArrear
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(200)] public string? Title { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Pending";
}

/// <summary>علی‌الحساب (پرداخت وسط ماه — در فیش ماه هدف کسر می‌شود)</summary>
public class HrPayOnAccount
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public DateTime? PaidDate { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Pending";
}

/// <summary>دوره محاسبه حقوق — Status: Draft|Approved|Locked</summary>
public class HrPayRun
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    /// <summary>Monthly=حقوق ماه | Eydi=عیدی و پاداش پایان سال</summary>
    [MaxLength(10)] public string Kind { get; set; } = "Monthly";
    [MaxLength(20)] public string Status { get; set; } = "Draft";
    public int SlipCount { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalInsuranceEmp { get; set; }
    public decimal TotalInsuranceEr { get; set; }
    public int? VoucherId { get; set; }
    [MaxLength(150)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LockedAt { get; set; }
    [MaxLength(300)] public string? Note { get; set; }
}

/// <summary>فیش حقوقی یک پرسنل در یک دوره</summary>
public class HrPaySlip
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public int EmployeeId { get; set; }
    public int? UserId { get; set; }
    [MaxLength(150)] public string EmployeeName { get; set; } = "";
    public double DaysPaid { get; set; }
    public double DaysWorked { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal InsuranceableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public decimal EmployerInsurance { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetPay { get; set; }
    /// <summary>جزئیات آیتم‌ها (JSON)</summary>
    public string DetailsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>تسویه خروج پرسنل — Status: Draft|Approved|Paid</summary>
public class HrPaySettlement
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime LeaveDate { get; set; }
    public double YearsOfService { get; set; }
    public decimal LastBase { get; set; }
    public double UnusedLeaveDays { get; set; }
    public decimal SeveranceAmount { get; set; }
    public decimal LeaveRefund { get; set; }
    public decimal EydiProrata { get; set; }
    public decimal TotalAmount { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Draft";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>بایگانی ارسال لیست‌های قانونی — Kind: Insurance=بیمه | Tax=مالیات | Bank=بانک. هش فایل + جمع‌ها برای تشخیص تغییر پس از ارسال.</summary>
public class HrPayFiling
{
    public int Id { get; set; }
    public int RunId { get; set; }
    [MaxLength(10)] public string Kind { get; set; } = "Insurance";
    [MaxLength(150)] public string FileName { get; set; } = "";
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    public int SlipCount { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalInsurance { get; set; }
    public decimal TotalNet { get; set; }
    public int Version { get; set; } = 1;
    [MaxLength(150)] public string FiledBy { get; set; } = "";
    public DateTime FiledAt { get; set; } = DateTime.Now;
    [MaxLength(100)] public string? ReceiptNo { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}
