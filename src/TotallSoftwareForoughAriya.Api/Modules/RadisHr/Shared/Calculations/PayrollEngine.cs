using RadisHr.Shared.Models;

namespace RadisHr.Shared.Calculations;

/// <summary>ورودی محاسبهٔ حکم حقوق — معادل شیء input در calculatePayroll</summary>
public class PayrollInput
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int? Days { get; set; }

    public decimal? Base { get; set; }
    public decimal? Seniority { get; set; }
    public decimal? Housing { get; set; }
    public decimal? Food { get; set; }
    public decimal? Marriage { get; set; }
    public decimal? Children { get; set; }
    public decimal? Overtime { get; set; }
    public decimal? Shift { get; set; }
    public decimal? Night { get; set; }
    public decimal? Friday { get; set; }
    public decimal? Mission { get; set; }
    public decimal? Attraction { get; set; }
    public decimal? Supervisor { get; set; }
    public decimal? Performance { get; set; }
    public decimal? Agreed { get; set; }
    public decimal ShortfallPay { get; set; }
}

/// <summary>خروجی مزایای قانونی — معادل calculateStatutoryBenefits</summary>
public class StatutoryBenefits
{
    public decimal Seniority { get; set; }
    public decimal Housing { get; set; }
    public decimal Food { get; set; }
    public decimal Marriage { get; set; }
    public decimal Children { get; set; }
    public decimal ChildPerEligibleChild { get; set; }
    public int EligibleChildren { get; set; }
    public int Days { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
}

/// <summary>خروجی کامل فیش حقوقی — معادل خروجی calculatePayroll</summary>
public class PayrollResult
{
    public decimal Base { get; set; }
    public decimal Seniority { get; set; }
    public decimal Housing { get; set; }
    public decimal Food { get; set; }
    public decimal Marriage { get; set; }
    public decimal Child { get; set; }
    public decimal Children { get; set; }   // نام مستعار Child (مطابق خروجی JS)
    public decimal Overtime { get; set; }
    public decimal Shift { get; set; }
    public decimal Night { get; set; }
    public decimal Friday { get; set; }
    public decimal Mission { get; set; }
    public decimal Attraction { get; set; }
    public decimal Supervisor { get; set; }
    public decimal Performance { get; set; }
    public decimal Agreed { get; set; }

    public StatutoryBenefits Legal { get; set; } = new();
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

/// <summary>
/// موتور محاسبات حقوق و دستمزد — پورت مو به موی assets/statutory-rules.js
/// قاعدهٔ پروژه: هیچ منطقی «اصلاح» نشده است (تطابق کامل با نسخهٔ جاوااسکریپت).
/// </summary>
public static class PayrollEngine
{
    /// <summary>گرد کردن به سبک JavaScript Math.round  (نیم به سمت مثبت بی‌نهایت)</summary>
    public static decimal JsRound(decimal value) => Math.Floor(value + 0.5m);

    /// <summary>hasOneYearSeniority — مقایسهٔ لغوی سه‌جزئی تاریخ استخدام + ۱ سال</summary>
    public static bool HasOneYearSeniority(Employee? employee, int year, int month, int day)
    {
        var raw = employee?.Hire ?? "";
        var parts = raw.Split('/');
        if (parts.Length != 3) return false;
        var nums = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(PersianCalendarUtil.Digits(parts[i]), out nums[i])) return false;
        }
        var anniversary = new[] { nums[0] + 1, nums[1], nums[2] };
        var payrollDate = new[] { year, month, day };
        for (var i = 0; i < 3; i++)
        {
            if (payrollDate[i] > anniversary[i]) return true;
            if (payrollDate[i] < anniversary[i]) return false;
        }
        return true;
    }

    /// <summary>calculateStatutoryBenefits — بدون تسهیم (proration) مسکن/بن، دقیقاً مطابق نسخهٔ اصلی</summary>
    public static StatutoryBenefits CalculateStatutoryBenefits(Employee? employee, StatutoryRules rules, int year, int month, int? daysOverride = null)
    {
        var y = year != 0 ? year : rules.Year;
        var m = month != 0 ? month : 1;
        var days = daysOverride is > 0 ? daysOverride.Value : PersianCalendarUtil.PayrollMonthDays(y, m);
        var eligibleChildren = Math.Max(0, employee?.Children ?? 0);
        var married = (employee?.Married ?? "").Replace("آ", "ا").Contains("متاهل");

        return new StatutoryBenefits
        {
            Seniority = HasOneYearSeniority(employee, y, m, days) ? rules.SeniorityDaily * days : 0m,
            Housing = days > 0 ? rules.HousingMonthly : 0m,
            Food = days > 0 ? rules.FoodMonthly : 0m,
            Marriage = married && days > 0 ? rules.MarriageMonthly : 0m,
            Children = days > 0 ? rules.MinimumDailyWage * rules.ChildMultiplier * eligibleChildren : 0m,
            ChildPerEligibleChild = rules.MinimumDailyWage * rules.ChildMultiplier,
            EligibleChildren = eligibleChildren,
            Days = days,
            Year = y,
            Month = m
        };
    }

    /// <summary>calculateTax — مالیات پلکانی تجمعی روی مازاد معافیت</summary>
    public static decimal CalculateTax(decimal taxableIncome, StatutoryRules rules)
    {
        var income = Math.Max(0m, taxableIncome);
        if (income <= rules.TaxMonthlyExemption) return 0m;

        var tax = 0m;
        var lower = rules.TaxMonthlyExemption;
        foreach (var band in rules.TaxBrackets.OrderBy(b => b.Ordinal))
        {
            var upper = band.Ceiling == null ? income : Math.Min(income, band.Ceiling.Value);
            if (upper > lower) tax += (upper - lower) * band.Rate / 100m;
            if (band.Ceiling == null || income <= band.Ceiling.Value) break;
            lower = band.Ceiling.Value;
        }
        return JsRound(tax);
    }

    /// <summary>calculatePayroll — محاسبهٔ کامل فیش</summary>
    public static PayrollResult CalculatePayroll(Employee? employee, StatutoryRules rules, PayrollInput input)
    {
        var legal = CalculateStatutoryBenefits(employee, rules, input.Year, input.Month, input.Days);

        var componentBase        = input.Base        ?? employee?.Salary          ?? 0m;
        var componentSeniority   = input.Seniority   ?? legal.Seniority;
        var componentHousing     = input.Housing     ?? legal.Housing;
        var componentFood        = input.Food        ?? legal.Food;
        var componentMarriage    = input.Marriage    ?? legal.Marriage;
        var componentChild       = input.Children    ?? legal.Children;
        var componentOvertime    = input.Overtime    ?? 0m;
        var componentShift       = input.Shift       ?? 0m;
        var componentNight       = input.Night       ?? 0m;
        var componentFriday      = input.Friday      ?? 0m;
        var componentMission     = input.Mission     ?? 0m;
        var componentAttraction  = input.Attraction  ?? employee?.AttractionPay   ?? 0m;
        var componentSupervisor  = input.Supervisor  ?? employee?.SupervisorPay   ?? 0m;
        var componentPerformance = input.Performance ?? employee?.PerformancePay  ?? 0m;
        var componentAgreed      = input.Agreed      ?? employee?.AgreedBenefits  ?? 0m;

        var shortfallPay = Math.Max(0m, input.ShortfallPay);

        var gross = componentBase + componentSeniority + componentHousing + componentFood
                  + componentMarriage + componentChild + componentOvertime + componentShift
                  + componentNight + componentFriday + componentMission + componentAttraction
                  + componentSupervisor + componentPerformance + componentAgreed;

        var ins = rules.Insurable;
        var insuranceBase = 0m;
        if (ins.Base)        insuranceBase += componentBase;
        if (ins.Seniority)   insuranceBase += componentSeniority;
        if (ins.Housing)     insuranceBase += componentHousing;
        if (ins.Food)        insuranceBase += componentFood;
        if (ins.Marriage)    insuranceBase += componentMarriage;
        if (ins.Child)       insuranceBase += componentChild;
        if (ins.Overtime)    insuranceBase += componentOvertime;
        if (ins.Shift)       insuranceBase += componentShift;
        if (ins.Night)       insuranceBase += componentNight;
        if (ins.Friday)      insuranceBase += componentFriday;
        if (ins.Mission)     insuranceBase += componentMission;
        if (ins.Attraction)  insuranceBase += componentAttraction;
        if (ins.Supervisor)  insuranceBase += componentSupervisor;
        if (ins.Performance) insuranceBase += componentPerformance;
        if (ins.Agreed)      insuranceBase += componentAgreed;

        insuranceBase = Math.Max(0m, insuranceBase - shortfallPay);
        var maximumInsuranceBase = rules.MinimumDailyWage * rules.MaximumInsurableDailyMultiplier * legal.Days;
        insuranceBase = Math.Min(insuranceBase, maximumInsuranceBase);

        var insurance = JsRound(insuranceBase * rules.WorkerInsuranceRate / 100m);
        var employerInsurance = JsRound(insuranceBase * rules.EmployerInsuranceRate / 100m);
        var taxableIncome = Math.Max(0m, gross - shortfallPay - insurance - componentMission);
        var tax = CalculateTax(taxableIncome, rules);
        var net = Math.Max(0m, gross - shortfallPay - insurance - tax);

        return new PayrollResult
        {
            Base = componentBase,
            Seniority = componentSeniority,
            Housing = componentHousing,
            Food = componentFood,
            Marriage = componentMarriage,
            Child = componentChild,
            Children = componentChild,
            Overtime = componentOvertime,
            Shift = componentShift,
            Night = componentNight,
            Friday = componentFriday,
            Mission = componentMission,
            Attraction = componentAttraction,
            Supervisor = componentSupervisor,
            Performance = componentPerformance,
            Agreed = componentAgreed,
            Legal = legal,
            ShortfallPay = shortfallPay,
            Gross = gross,
            InsuranceBase = insuranceBase,
            MaximumInsuranceBase = maximumInsuranceBase,
            Insurance = insurance,
            EmployerInsurance = employerInsurance,
            TaxableIncome = taxableIncome,
            Tax = tax,
            Net = net
        };
    }

    /// <summary>
    /// نرخ ساعتی — عیناً base/220 مطابق attendance-engine.js.
    /// (کاربر صراحتاً خواسته است این مقسوم‌علیه تغییر نکند.)
    /// </summary>
    public const decimal MonthlyHoursDivisor = 220m;

    public static decimal HourlyRate(decimal baseSalary) => baseSalary / MonthlyHoursDivisor;

    /// <summary>مبلغ اضافه‌کاری از روی دقیقه — otPay در attendance-engine.js</summary>
    public static decimal OvertimePay(decimal baseSalary, int overtimeMinutes, StatutoryRules rules) =>
        JsRound(HourlyRate(baseSalary) * (1m + rules.OvertimePremiumRate / 100m) * overtimeMinutes / 60m);

    /// <summary>مبلغ کسرکار از روی دقیقه — shortfallPay در attendance-engine.js</summary>
    public static decimal ShortfallPay(decimal baseSalary, int shortfallMinutes) =>
        JsRound(HourlyRate(baseSalary) * shortfallMinutes / 60m);
}
