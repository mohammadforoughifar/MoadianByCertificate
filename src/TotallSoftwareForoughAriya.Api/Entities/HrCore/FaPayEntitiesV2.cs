using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// ================== حقوق و دستمزد فروغ آریا (FaPay) — افزونه‌های §۲ ==================
/// وام و مساعده اقساطی، معوقات، عیدی و سنوات پایان سال، تسویه پایان همکاری،
/// فایل بیمه (کد کارگاه + شماره بیمه پرسنل)، شب‌کاری و نوبت‌کاری.
/// جدول‌های جدید با پیشوند FaPay؛ سیستم قدیمی (HrPay) دست نمی‌خورد.
/// </summary>

/// <summary>نوع دوره حقوقی: ماهانه یا پایان‌سال (عیدی و سنوات)</summary>
public enum FaPayRunKind { Monthly = 0, YearEnd = 1 }

public enum FaPayLoanStatus { Active = 0, Paid = 1, Cancelled = 2 }

public enum FaPayArrearStatus { Draft = 0, Applied = 1, Cancelled = 2 }

public enum FaPaySettlementStatus { Draft = 0, Final = 1 }

/// <summary>علت پایان همکاری در تسویه‌حساب</summary>
public enum FaPaySettlementReason
{
    Resignation = 0,  // استعفا
    Termination = 1,  // اخراج/فسخ
    ContractEnd = 2,  // پایان قرارداد
    Retirement = 3,   // بازنشستگی
    Mutual = 4,       // توافق طرفین
    Other = 9         // سایر
}

/// <summary>تنظیمات تکمیلی حقوق (تک‌ردیف Id=1): بیمه، شب‌کاری</summary>
public class FaPayExtraSettings
{
    public int Id { get; set; }

    /// <summary>کد کارگاه بیمه تأمین اجتماعی (۱۰ تا ۱۴ رقم)</summary>
    [MaxLength(14)]
    public string? WorkshopCode { get; set; }

    [MaxLength(200)]
    public string? WorkshopName { get; set; }

    /// <summary>درصد فوق‌العاده شب‌کاری روی نرخ ساعتی (قانون کار: ۳۵٪)</summary>
    public double NightRatePercent { get; set; } = 35;

    /// <summary>سقف عیدی به چند برابر حداقل دستمزد ماهانه (قانون: ۳ برابر)</summary>
    public double EidiCapMultiplier { get; set; } = 3;

    /// <summary>مبنای عیدی به چند برابر آخرین حقوق ماهانه (قانون: ۲ برابر)</summary>
    public double EidiBaseMultiplier { get; set; } = 2;
}

/// <summary>وام و مساعده پرسنل — اقساط خودکار از فیش کسر می‌شود</summary>
public class FaPayLoan
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(150)]
    public string Title { get; set; } = "";

    /// <summary>مبلغ کل وام (ریال)</summary>
    public double TotalAmount { get; set; }

    /// <summary>تعداد اقساط ماهانه</summary>
    public int InstallmentCount { get; set; } = 1;

    /// <summary>مبلغ هر قسط (ریال) — محاسبه خودکار، قابل اصلاح قبل از شروع کسر</summary>
    public double InstallmentAmount { get; set; }

    /// <summary>سال شروع کسر (شمسی)</summary>
    public int StartYear { get; set; }

    /// <summary>ماه شروع کسر (شمسی)</summary>
    public int StartMonth { get; set; }

    public FaPayLoanStatus Status { get; set; } = FaPayLoanStatus.Active;

    [MaxLength(500)]
    public string? Note { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>یک قسط ماهانه از وام</summary>
public class FaPayLoanInstallment
{
    public int Id { get; set; }

    public int LoanId { get; set; }

    /// <summary>شماره قسط (از ۱)</summary>
    public int SeqNo { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public double Amount { get; set; }

    public bool IsPaid { get; set; }

    /// <summary>دوره‌ای که این قسط در آن کسر شد</summary>
    public int? PaidRunId { get; set; }

    public DateTime? PaidAt { get; set; }
}

/// <summary>معوقات و مابه‌التفاوت (افزایش معوقه حقوق) — در فیش ماه هدف اعمال می‌شود</summary>
public class FaPayArrear
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>بازه‌ای که معوقه مربوط به آن است (شمسی) — صرفاً اطلاعاتی</summary>
    public int FromYear { get; set; }
    public int FromMonth { get; set; }
    public int ToYear { get; set; }
    public int ToMonth { get; set; }

    /// <summary>مبلغ مابه‌التفاوت (ریال) — مزایا</summary>
    public double Amount { get; set; }

    /// <summary>ماه هدف اعمال در فیش (شمسی)</summary>
    public int TargetYear { get; set; }
    public int TargetMonth { get; set; }

    public FaPayArrearStatus Status { get; set; } = FaPayArrearStatus.Draft;

    public int? AppliedRunId { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>تسویه‌حساب پایان همکاری: سنوات + عیدی تناسبی + بازخرید مرخصی − وام و کسور</summary>
public class FaPaySettlement
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    /// <summary>تاریخ پایان همکاری (آخرین روز کار)</summary>
    public DateTime LeaveDate { get; set; } = DateTime.Today;

    public FaPaySettlementReason Reason { get; set; } = FaPaySettlementReason.Resignation;

    /// <summary>سابقه خدمت (روز)</summary>
    public int ServiceDays { get; set; }

    /// <summary>آخرین حقوق ماهانه مبنا (ریال)</summary>
    public double BaseSalary { get; set; }

    /// <summary>سنوات خدمت تناسبی (ریال)</summary>
    public double SenavatAmount { get; set; }

    /// <summary>عیدی تناسبی سال جاری (ریال)</summary>
    public double EidiAmount { get; set; }

    public double LeaveBuybackDays { get; set; }

    /// <summary>مبلغ بازخرید مرخصی استفاده‌نشده (ریال)</summary>
    public double LeaveBuybackAmount { get; set; }

    /// <summary>سایر مطالبات (ریال)</summary>
    public double OtherEarnings { get; set; }

    [MaxLength(300)]
    public string? OtherEarningsNote { get; set; }

    /// <summary>مانده وام‌ها (ریال) — خودکار</summary>
    public double LoanRemaining { get; set; }

    /// <summary>سایر کسور (ریال)</summary>
    public double OtherDeductions { get; set; }

    [MaxLength(300)]
    public string? OtherDeductionsNote { get; set; }

    /// <summary>مالیات تسویه (ریال)</summary>
    public double TaxAmount { get; set; }

    public double GrossTotal { get; set; }

    /// <summary>خالص قابل پرداخت (ریال)</summary>
    public double NetPayable { get; set; }

    public FaPaySettlementStatus Status { get; set; } = FaPaySettlementStatus.Draft;

    /// <summary>با نهایی‌شدن، پرسنل غیرفعال شود؟</summary>
    public bool DeactivateEmployee { get; set; } = true;

    [MaxLength(500)]
    public string? Note { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? FinalizedAt { get; set; }
}
