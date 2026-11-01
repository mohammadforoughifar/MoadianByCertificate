using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Api.Services;
using RadisHr.Shared.Calculations;
using RadisHr.Shared.Contracts;
using RadisHr.Shared.Models;
using Paging = Inventory.Api.Services.Paging;

namespace RadisHr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/statutory-rules")]
public class StatutoryRulesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PayrollService _payroll;

    public StatutoryRulesController(AppDbContext db, PayrollService payroll)
    {
        _db = db;
        _payroll = payroll;
    }

    [HttpGet]
    public async Task<ActionResult<List<StatutoryRules>>> All([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.StatutoryRules.Include(r => r.TaxBrackets).AsNoTracking()
            .OrderByDescending(r => r.Year), skip, take));

    [HttpGet("{year:int}")]
    public async Task<ActionResult<StatutoryRules>> ForYear(int year) => await _payroll.RulesForAsync(year);

    [HttpGet("years")]
    public async Task<ActionResult<List<int>>> Years([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.StatutoryRules.Select(r => r.Year).OrderByDescending(y => y), skip, take));

    /// <summary>
    /// ویرایش الزامات — هر تغییر عددی در جدول ممیزی و اطلاع‌رسانی مدیرعامل ثبت می‌شود
    /// (پورت رفتار statutory-rules.js).
    /// </summary>
    [Authorize(Roles = "hr,ceo")]
    [HttpPut("{year:int}")]
    public async Task<ActionResult<ApiMessage>> Update(int year, StatutoryRules input)
    {
        var rules = await _db.StatutoryRules.Include(r => r.TaxBrackets)
            .FirstOrDefaultAsync(r => r.Year == year);

        var actor = User.Identity?.Name ?? "نامشخص";
        var isNew = rules == null;

        if (rules == null)
        {
            // سال جدید: اگر ورودی پله‌های مالیاتی نداشته باشد، از پله‌های پیش‌فرض رسمی استفاده می‌شود
            rules = StatutoryRules.CreateDefault(year);
            _db.StatutoryRules.Add(rules);
        }

        var labels = new Dictionary<string, string>
        {
            ["MinimumDailyWage"] = "حداقل مزد روزانه",
            ["SeniorityDaily"] = "پایه سنوات روزانه",
            ["HousingMonthly"] = "حق مسکن ماهانه",
            ["FoodMonthly"] = "بن کارگری ماهانه",
            ["MarriageMonthly"] = "حق تأهل ماهانه",
            ["ChildMultiplier"] = "ضریب حق اولاد",
            ["WorkerInsuranceRate"] = "سهم بیمه کارگر",
            ["EmployerInsuranceRate"] = "سهم بیمه کارفرما",
            ["MaximumInsurableDailyMultiplier"] = "سقف دستمزد مشمول بیمه",
            ["OvertimePremiumRate"] = "فوق‌العاده اضافه‌کاری",
            ["NightWorkPremiumRate"] = "فوق‌العاده شب‌کاری",
            ["FridayWorkPremiumRate"] = "فوق‌العاده جمعه‌کاری",
            ["TaxMonthlyExemption"] = "معافیت ماهانه مالیات"
        };

        var before = new Dictionary<string, decimal>
        {
            ["MinimumDailyWage"] = rules.MinimumDailyWage,
            ["SeniorityDaily"] = rules.SeniorityDaily,
            ["HousingMonthly"] = rules.HousingMonthly,
            ["FoodMonthly"] = rules.FoodMonthly,
            ["MarriageMonthly"] = rules.MarriageMonthly,
            ["ChildMultiplier"] = rules.ChildMultiplier,
            ["WorkerInsuranceRate"] = rules.WorkerInsuranceRate,
            ["EmployerInsuranceRate"] = rules.EmployerInsuranceRate,
            ["MaximumInsurableDailyMultiplier"] = rules.MaximumInsurableDailyMultiplier,
            ["OvertimePremiumRate"] = rules.OvertimePremiumRate,
            ["NightWorkPremiumRate"] = rules.NightWorkPremiumRate,
            ["FridayWorkPremiumRate"] = rules.FridayWorkPremiumRate,
            ["TaxMonthlyExemption"] = rules.TaxMonthlyExemption
        };

        rules.MinimumDailyWage = input.MinimumDailyWage;
        rules.SeniorityDaily = input.SeniorityDaily;
        rules.HousingMonthly = input.HousingMonthly;
        rules.FoodMonthly = input.FoodMonthly;
        rules.MarriageMonthly = input.MarriageMonthly;
        rules.ChildMultiplier = input.ChildMultiplier;
        rules.WorkerInsuranceRate = input.WorkerInsuranceRate;
        rules.EmployerInsuranceRate = input.EmployerInsuranceRate;
        rules.MaximumInsurableDailyMultiplier = input.MaximumInsurableDailyMultiplier;
        rules.OvertimePremiumRate = input.OvertimePremiumRate;
        rules.NightWorkPremiumRate = input.NightWorkPremiumRate;
        rules.FridayWorkPremiumRate = input.FridayWorkPremiumRate;
        rules.ShiftMorningEveningRate = input.ShiftMorningEveningRate;
        rules.ShiftThreeShiftRate = input.ShiftThreeShiftRate;
        rules.ShiftNightRotationRate = input.ShiftNightRotationRate;
        rules.MissionMinimumDailyMultiplier = input.MissionMinimumDailyMultiplier;
        rules.TaxMonthlyExemption = input.TaxMonthlyExemption;
        rules.Insurable = input.Insurable;
        rules.EffectiveFrom = input.EffectiveFrom;
        rules.SourceReference = input.SourceReference;
        rules.SourceNote = input.SourceNote;
        rules.UpdatedAt = DateTime.UtcNow;
        rules.UpdatedBy = actor;

        if (input.TaxBrackets.Count > 0)
        {
            rules.TaxBrackets.Clear();
            var ordinal = 0;
            foreach (var b in input.TaxBrackets)
                rules.TaxBrackets.Add(new TaxBracket { Ceiling = b.Ceiling, Rate = b.Rate, Ordinal = ordinal++ });
        }

        var after = new Dictionary<string, decimal>
        {
            ["MinimumDailyWage"] = rules.MinimumDailyWage,
            ["SeniorityDaily"] = rules.SeniorityDaily,
            ["HousingMonthly"] = rules.HousingMonthly,
            ["FoodMonthly"] = rules.FoodMonthly,
            ["MarriageMonthly"] = rules.MarriageMonthly,
            ["ChildMultiplier"] = rules.ChildMultiplier,
            ["WorkerInsuranceRate"] = rules.WorkerInsuranceRate,
            ["EmployerInsuranceRate"] = rules.EmployerInsuranceRate,
            ["MaximumInsurableDailyMultiplier"] = rules.MaximumInsurableDailyMultiplier,
            ["OvertimePremiumRate"] = rules.OvertimePremiumRate,
            ["NightWorkPremiumRate"] = rules.NightWorkPremiumRate,
            ["FridayWorkPremiumRate"] = rules.FridayWorkPremiumRate,
            ["TaxMonthlyExemption"] = rules.TaxMonthlyExemption
        };

        var changes = 0;
        foreach (var (field, oldValue) in before)
        {
            var newValue = after[field];
            if (oldValue == newValue) continue;
            changes++;

            _db.RuleAudits.Add(new StatutoryRuleAudit
            {
                Year = year,
                Field = field,
                FieldLabel = labels.TryGetValue(field, out var l) ? l : field,
                OldValue = oldValue.ToString("0.##"),
                NewValue = newValue.ToString("0.##"),
                ChangedBy = actor
            });

            _db.CeoNotifications.Add(new AuditNotice
            {
                Uid = Guid.NewGuid().ToString("N"),
                Channel = "statutory",
                Title = $"تغییر الزامات قانونی سال {year}",
                Message = $"{(labels.TryGetValue(field, out var lb) ? lb : field)} از {oldValue:N0} به {newValue:N0} تغییر یافت.",
                Actor = actor
            });
        }

        await _db.SaveChangesAsync();
        return new ApiMessage(true, isNew
            ? "الزامات سال جدید ایجاد شد."
            : $"الزامات به‌روزرسانی شد. {changes} مورد تغییر ثبت و به مدیرعامل اطلاع داده شد.");
    }

    [HttpGet("audits")]
    public async Task<ActionResult<List<StatutoryRuleAudit>>> Audits([FromQuery] int? year, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.RuleAudits.AsNoTracking().AsQueryable();
        if (year.HasValue) query = query.Where(a => a.Year == year.Value);
        return Ok((await Paging.QueryAsync(query.OrderByDescending(a => a.ChangedAt), skip, take, 500)).Result());
    }

    /// <summary>محاسبهٔ آزمایشی مزایای قانونی — همان ماشین‌حساب صفحهٔ الزامات</summary>
    [HttpGet("benefits")]
    public async Task<ActionResult<StatutoryBenefits>> Benefits(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] string? code, [FromQuery] int? days)
    {
        var rules = await _payroll.RulesForAsync(year);
        var employee = string.IsNullOrEmpty(code)
            ? null : await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Code == code);
        return PayrollEngine.CalculateStatutoryBenefits(employee, rules, year, month, days);
    }
}
