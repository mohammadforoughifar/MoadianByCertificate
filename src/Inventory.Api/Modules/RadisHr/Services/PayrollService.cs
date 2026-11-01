using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Shared.Calculations;
using RadisHr.Shared.Models;

namespace RadisHr.Api.Services;

/// <summary>
/// سرویس حقوق و دستمزد — همان ترتیب محاسبهٔ app.js/statutory-rules.js
/// خروجی عیناً برابر نسخهٔ اصلی است (تطابق عددی تأییدشده).
/// </summary>
public class PayrollService
{
    private readonly AppDbContext _db;
    public PayrollService(AppDbContext db) => _db = db;

    public async Task<StatutoryRules> RulesForAsync(int year)
    {
        var rules = await _db.StatutoryRules
            .Include(r => r.TaxBrackets)
            .FirstOrDefaultAsync(r => r.Year == year);

        if (rules == null)
        {
            rules = await _db.StatutoryRules.Include(r => r.TaxBrackets)
                .OrderByDescending(r => r.Year).FirstOrDefaultAsync();
        }
        if (rules != null) rules.TaxBrackets = rules.TaxBrackets.OrderBy(t => t.Ordinal).ToList();
        return rules ?? new StatutoryRules { Year = year };
    }

    /// <summary>محاسبهٔ فیش یک کارمند برای یک ماه، با اعمال ردیف حضور و غیاب در صورت وجود</summary>
    public async Task<PayrollResult> ComputeAsync(Employee employee, int year, int month,
        decimal? overtime = null, decimal? shortfallPay = null, decimal? mission = null,
        decimal? shift = null, decimal? night = null, decimal? friday = null, int? days = null)
    {
        var rules = await RulesForAsync(year);
        var input = new PayrollInput
        {
            Year = year,
            Month = month,
            Days = days,
            Base = employee.Salary,
            Attraction = employee.AttractionPay,
            Supervisor = employee.SupervisorPay,
            Performance = employee.PerformancePay,
            Agreed = employee.AgreedBenefits,
            Overtime = overtime,
            Mission = mission,
            Shift = shift,
            Night = night,
            Friday = friday,
            ShortfallPay = shortfallPay ?? 0m
        };
        return PayrollEngine.CalculatePayroll(employee, rules, input);
    }

    /// <summary>محاسبهٔ کل ماه از روی ردیف‌های حضور و غیاب ذخیره‌شده</summary>
    public async Task<List<PayrollRow>> MonthRowsAsync(string month, Paging.Request? pagination = null)
    {
        return await _db.PayrollRows.Where(r => r.Month == month)
            .OrderBy(r => r.Code).ThenBy(x => x.Id).ToPageListAsync(pagination);
    }
}
