using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// ================== حقوق و دستمزد فروغ آریا (FaPay) — §۸، ماژول جدید و مستقل ==================
/// ورودی محاسبه: خلاصه ماهانه FaAtt (اضافه‌کاری/تأخیر/غیبت/مأموریت) + حقوق پایه و ایمیل HrEmployee.
/// جدول‌ها با پیشوند FaPay یکتا هستند. اسکیما با FaPaySchemaV1 (Ensure) ساخته می‌شود (نه EF Migration).
/// سیستم قدیمی (ماژول HrPay، موتور RadisHr، صفحه payroll) دست نمی‌خورد.
/// </summary>

public enum FaPayRunStatus { Draft = 0, Final = 1 }
public enum FaPayItemKind { Earning = 0, Deduction = 1 }

/// <summary>تنظیمات محاسبه حقوق (تک‌ردیف Id=1) — فرمول‌های شروع طبق §۸، قابل اصلاح توسط HR</summary>
public class FaPaySettings
{
    public int Id { get; set; }

    /// <summary>مخرج حقوق روزانه (پایه ماهانه ÷ این عدد)</summary>
    public double DaysPerMonth { get; set; } = 30;

    /// <summary>مخرج نرخ ساعتی (حقوق روزانه ÷ این عدد)</summary>
    public double HoursPerDay { get; set; } = 8;

    /// <summary>ضریب اضافه‌کاری (قانون کار: ۱.۴)</summary>
    public double OvertimeFactor { get; set; } = 1.4;

    /// <summary>ضریب کسر تأخیر از نرخ ساعتی</summary>
    public double DelayFactor { get; set; } = 1.0;

    /// <summary>بیمه سهم کارمند (درصد حقوق پایه)</summary>
    public double InsuranceEmployeeRate { get; set; } = 7;

    /// <summary>بیمه سهم کارفرما (درصد — فقط برای گزارش هزینه نیروی انسانی)</summary>
    public double InsuranceEmployerRate { get; set; } = 23;

    /// <summary>معافیت مالیاتی ماهانه (ریال) — مقدار شروع؛ طبق بخشنامه جاری اصلاح شود</summary>
    public double TaxFreeMonthly { get; set; } = 120_000_000;

    /// <summary>کسر خودکار غیبت از حقوق؟</summary>
    public bool AbsentDeductEnabled { get; set; } = true;

    /// <summary>کسر خودکار مرخصی بدون حقوق؟</summary>
    public bool UnpaidLeaveDeductEnabled { get; set; } = true;

    [MaxLength(500)]
    public string? Note { get; set; }
}

/// <summary>پلکان مالیاتی ماهانه (روی مازاد مشمول بعد از معافیت) — مثال شروع؛ طبق بخشنامه جاری تنظیم شود</summary>
public class FaPayTaxBracket
{
    public int Id { get; set; }

    public double FromAmount { get; set; }

    /// <summary>سقف پلکان — خالی یعنی تا بی‌نهایت</summary>
    public double? ToAmount { get; set; }

    /// <summary>نرخ درصد</summary>
    public double Rate { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>قلم حقوقی (مزایا/کسور ثابت و متغیر)</summary>
public class FaPayItemType
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string Code { get; set; } = "";

    [MaxLength(100)]
    public string Name { get; set; } = "";

    public FaPayItemKind Kind { get; set; } = FaPayItemKind.Earning;

    /// <summary>ثابت (هر ماه خودکار) یا متغیر (با ثبت ماهانه)؟</summary>
    public bool IsFixed { get; set; }

    /// <summary>مبلغ پیش‌فرض ماهانه (ریال) برای اقلام ثابت</summary>
    public double DefaultAmount { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>دوره حقوقی (یک ردیف برای هر ماه شمسی)</summary>
public class FaPayRun
{
    public int Id { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public FaPayRunStatus Status { get; set; } = FaPayRunStatus.Draft;

    /// <summary>نوع دوره: ماهانه یا پایان‌سال (عیدی و سنوات) — §۲</summary>
    public FaPayRunKind Kind { get; set; } = FaPayRunKind.Monthly;

    [MaxLength(500)]
    public string? Note { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? FinalizedAt { get; set; }
}

/// <summary>فیش حقوقی یک پرسنل در یک دوره</summary>
public class FaPaySlip
{
    public int Id { get; set; }

    public int RunId { get; set; }

    public int EmployeeId { get; set; }

    /// <summary>حقوق پایه ماهانه در لحظه محاسبه (ریال)</summary>
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

    public DateTime CalculatedAt { get; set; } = DateTime.Now;
}

/// <summary>سطر تفکیکی فیش (مزایا/کسور خودکار و دستی)</summary>
public class FaPaySlipItem
{
    public int Id { get; set; }

    public int SlipId { get; set; }

    public int? ItemTypeId { get; set; }

    [MaxLength(150)]
    public string Title { get; set; } = "";

    public FaPayItemKind Kind { get; set; } = FaPayItemKind.Earning;

    public double Amount { get; set; }

    /// <summary>خودکار (موتور) یا دستی (ثبت ماهانه)؟</summary>
    public bool IsAuto { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }
}

/// <summary>ثبت ماهانه دستی (کارانه/پاداش/مساعده/...) برای یک پرسنل</summary>
public class FaPayAdjustment
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public int ItemTypeId { get; set; }

    public double Amount { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// حداقل دستمزد قانونی سالانه — جدول اختصاصی حقوق و دستمزد منابع انسانی اصلی (FaPayMinWages).
/// پیش‌تر از جدول HrMinWages (متعلق به «منابع انسانی فروغ آریا») خوانده می‌شد؛ برای تفکیک کامل
/// دو ماژول داده‌ها یک‌بار کپی شدند و از «تنظیمات حقوق» همین ماژول مدیریت می‌شوند.
/// </summary>
public class FaPayMinWage
{
    [Key] public int Year { get; set; }
    public decimal MonthlyWage { get; set; }
    public decimal DailyWage { get; set; }
    public DateTime EffectiveDate { get; set; }
    [MaxLength(300)] public string? Note { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
