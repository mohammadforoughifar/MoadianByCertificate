using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Shared.Calculations;
using RadisHr.Shared.Models;
using Paging = Inventory.Api.Services.Paging;

namespace RadisHr.Api.Controllers;

/// <summary>تحلیل مدیریتی — معادل executive-analytics.js و کارت‌های داشبورد</summary>
[ApiController]
[Authorize]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AnalyticsController(AppDbContext db) => _db = db;

    private static decimal Hours(string hm)
    {
        var parts = (hm ?? "0:00").Split(':');
        if (parts.Length != 2) return 0m;
        _ = int.TryParse(parts[0], out var h);
        _ = int.TryParse(parts[1], out var m);
        return h + m / 60m;
    }

    private static string LabelFor(string monthKey)
    {
        var parts = monthKey.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var m)) return monthKey;
        var name = m >= 1 && m <= 12 ? PersianCalendarUtil.MonthNames[m - 1] : m.ToString();
        return $"{name} {parts[0]}";
    }

    /// <summary>فهرست ماه‌های دارای داده برای فیلترهای نمودار</summary>
    [HttpGet("months")]
    public async Task<ActionResult<object>> Months([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var keys = await _db.PayrollRows.AsNoTracking()
            .Select(r => r.Month).Distinct().OrderBy(m => m).ToPageListAsync(pagination);
        return pagination.Result(keys.OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => new { key = k, label = LabelFor(k) }).ToList());
    }

    /// <summary>
    /// نمودارهای مدیریتی: مقایسهٔ اضافه‌کار، جمع پرداختی (میلیارد ریال) و مرخصی.
    /// mode=months → مرخصی به تفکیک ماه، mode=units → مرخصی به تفکیک واحد.
    /// </summary>
    [Authorize(Roles = "ceo,hr,finance,accounting")]
    [HttpGet("executive")]
    public async Task<ActionResult<object>> Executive([FromQuery] string? months, [FromQuery] string mode = "months")
    {
        var selected = (months ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).ToHashSet();

        var rows = await _db.PayrollRows.AsNoTracking().ToListAsync();
        if (selected.Count > 0) rows = rows.Where(r => selected.Contains(r.Month)).ToList();

        var grouped = rows.GroupBy(r => r.Month)
            .OrderBy(g => g.Key, StringComparer.Ordinal).ToList();

        var overtime = grouped.Select(g => new
        {
            label = LabelFor(g.Key),
            shortLabel = LabelFor(g.Key).Split(' ')[0],
            value = Math.Round(g.Sum(r => Hours(r.Ot)), 2)
        }).ToList();

        var payment = grouped.Select(g => new
        {
            label = LabelFor(g.Key),
            shortLabel = LabelFor(g.Key).Split(' ')[0],
            value = Math.Round(g.Sum(r => r.Net) / 1_000_000_000m, 3)   // میلیارد ریال
        }).ToList();

        object leave;
        string leaveSubtitle;
        if (mode == "units")
        {
            var unitOf = (await _db.Employees.AsNoTracking()
                    .Select(e => new { e.Code, e.Unit }).ToListAsync())
                .GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.First().Unit ?? "بدون واحد");

            leaveSubtitle = "مقایسه ساعات مرخصی واحدها در ماه‌های انتخابی";
            leave = rows.GroupBy(r => unitOf.TryGetValue(r.Code, out var u) && !string.IsNullOrEmpty(u) ? u : "بدون واحد")
                .Select(g => new { label = g.Key, shortLabel = g.Key, value = Math.Round(g.Sum(r => Hours(r.Leave)), 2) })
                .OrderByDescending(x => x.value).ToList();
        }
        else
        {
            leaveSubtitle = "مجموع ساعات مرخصی تمام واحدها در ماه‌های انتخابی";
            leave = grouped.Select(g => new
            {
                label = LabelFor(g.Key),
                shortLabel = LabelFor(g.Key).Split(' ')[0],
                value = Math.Round(g.Sum(r => Hours(r.Leave)), 2)
            }).ToList();
        }

        return new
        {
            message = grouped.Count > 0
                ? $"{grouped.Count} ماه ثبت‌شده در تحلیل قرار دارد."
                : "برای انتخاب فعلی ماه ثبت‌شده‌ای وجود ندارد.",
            overtime,
            payment,
            leave,
            leaveSubtitle,
            leaveMode = mode
        };
    }

    /// <summary>کارت‌های داشبورد اصلی</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<object>> Dashboard()
    {
        var employees = await _db.Employees.AsNoTracking().ToListAsync();
        var lastMonth = await _db.PayrollRows.AsNoTracking()
            .Select(r => r.Month).Distinct().OrderByDescending(m => m).FirstOrDefaultAsync();
        var lastRows = lastMonth == null
            ? new List<PayrollRow>()
            : await _db.PayrollRows.AsNoTracking().Where(r => r.Month == lastMonth).ToListAsync();

        var year = PersianCalendarUtil.Today().Jy;
        var incidents = await _db.Incidents.AsNoTracking()
            .Where(i => i.Date.StartsWith(year.ToString())).ToListAsync();

        return new
        {
            employeeCount = employees.Count,
            activeEmployees = employees.Count(e => e.IsActive),
            byUnit = employees.GroupBy(e => string.IsNullOrEmpty(e.Unit) ? "بدون واحد" : e.Unit)
                .Select(g => new { unit = g.Key, count = g.Count() }).OrderByDescending(x => x.count).ToList(),
            lastMonth,
            lastMonthLabel = lastMonth == null ? "" : LabelFor(lastMonth),
            lastMonthNet = lastRows.Sum(r => r.Net),
            lastMonthOvertimeHours = Math.Round(lastRows.Sum(r => Hours(r.Ot)), 2),
            lastMonthShortfallHours = Math.Round(lastRows.Sum(r => Hours(r.Shortfall)), 2),
            lastMonthInsurance = lastRows.Sum(r => r.Insurance + r.EmployerInsurance),
            lastMonthTax = lastRows.Sum(r => r.Tax),
            incidentsThisYear = incidents.Count,
            lostDaysThisYear = incidents.Sum(i => i.LostDays),
            openAdvances = await _db.Advances.CountAsync(a => a.SettlementMethod == "pending"),
            unseenNotices = await _db.CeoNotifications.CountAsync(n => !n.Seen)
        };
    }
}
