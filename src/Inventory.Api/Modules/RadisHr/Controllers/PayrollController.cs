using Inventory.Api.Services;
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
[Route("api/payroll")]
public class PayrollController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PayrollService _payroll;

    public PayrollController(AppDbContext db, PayrollService payroll)
    {
        _db = db;
        _payroll = payroll;
    }

    /// <summary>ردیف‌های محاسبه‌شدهٔ یک ماه (۱۴۰۵/۰۳)</summary>
    [HttpGet("month/{month}")]
    public async Task<ActionResult<List<PayrollRow>>> Month(string month, [FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(async pagination => await _payroll.MonthRowsAsync(month, pagination: pagination), skip, take));

    [HttpGet("months")]
    public async Task<ActionResult<List<string>>> Months([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.PayrollRows.Select(r => r.Month).Distinct().OrderBy(m => m), skip, take));

    /// <summary>محاسبهٔ فیش یک نفر — معادل RADIS_PRINT.payslip</summary>
    [HttpPost("payslip")]
    public async Task<ActionResult<PayslipResponse>> Payslip(PayslipRequest request)
    {
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Code == request.EmployeeCode);
        if (employee == null) return NotFound(new ApiMessage(false, "پرسنل یافت نشد."));

        var rules = await _payroll.RulesForAsync(request.Year);
        var result = await _payroll.ComputeAsync(employee, request.Year, request.Month,
            request.Overtime, request.ShortfallPay, request.Mission,
            request.Shift, request.Night, request.Friday);

        var label = $"{PersianCalendarUtil.MonthNames[Math.Clamp(request.Month - 1, 0, 11)]} {request.Year}";
        return new PayslipResponse(employee, result, rules, label);
    }

    /// <summary>محاسبهٔ همهٔ پرسنل برای یک ماه بدون داده‌های حضور (حکم پایه)</summary>
    [HttpGet("preview")]
    public async Task<ActionResult<List<PayrollRow>>> Preview([FromQuery] int year, [FromQuery] int month, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Code).ThenBy(e => e.Id).ToPageListAsync(pagination);
        var rules = await _payroll.RulesForAsync(year);
        var monthKey = $"{year}/{month:00}";
        var rows = new List<PayrollRow>();

        foreach (var employee in employees)
        {
            var r = PayrollEngine.CalculatePayroll(employee, rules, new PayrollInput
            {
                Year = year, Month = month,
                Base = employee.Salary,
                Attraction = employee.AttractionPay,
                Supervisor = employee.SupervisorPay,
                Performance = employee.PerformancePay,
                Agreed = employee.AgreedBenefits
            });

            rows.Add(new PayrollRow
            {
                Month = monthKey, Code = employee.Code, Name = $"{employee.First} {employee.Last}",
                Base = r.Base, Seniority = r.Seniority, Housing = r.Housing, Food = r.Food,
                Marriage = r.Marriage, Children = r.Child, Attraction = r.Attraction,
                Supervisor = r.Supervisor, Performance = r.Performance, Agreed = r.Agreed,
                Gross = r.Gross, InsuranceBase = r.InsuranceBase, Insurance = r.Insurance,
                EmployerInsurance = r.EmployerInsurance, TaxableIncome = r.TaxableIncome,
                Tax = r.Tax, Net = r.Net
            });
        }
        return Ok(pagination.Result(rows.OrderBy(r => r.Code).ToList()));
    }

    /// <summary>جمع‌های ماه برای داشبورد و حسابداری</summary>
    [HttpGet("summary/{month}")]
    public async Task<ActionResult<object>> Summary(string month)
    {
        var rows = await _payroll.MonthRowsAsync(month);
        return new
        {
            month,
            count = rows.Count,
            gross = rows.Sum(r => r.Gross),
            net = rows.Sum(r => r.Net),
            tax = rows.Sum(r => r.Tax),
            insurance = rows.Sum(r => r.Insurance),
            employerInsurance = rows.Sum(r => r.EmployerInsurance),
            otPay = rows.Sum(r => r.OtPay),
            shortfallPay = rows.Sum(r => r.ShortfallPay)
        };
    }
}
