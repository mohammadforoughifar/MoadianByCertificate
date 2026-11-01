using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Shared.Calculations;
using RadisHr.Shared.Models;

namespace RadisHr.Api.Services;

/// <summary>
/// سرویس حضور و غیاب — پورت recalculate() از assets/attendance-engine.js
/// سیاست append-only بر کلید {code}|{date}؛ محاسبهٔ مجدد idempotent است.
/// </summary>
public class AttendanceService
{
    private readonly AppDbContext _db;
    private readonly PayrollService _payroll;

    public AttendanceService(AppDbContext db, PayrollService payroll)
    {
        _db = db;
        _payroll = payroll;
    }

    /// <summary>
    /// محاسبهٔ مجدد کامل: اعمال ترددهای دستی تأییدشده، قواعد خام،
    /// کسر مرخصی/مأموریت از کسری کار، سپس تولید ردیف‌های حقوق برای هر ماه.
    /// </summary>
    public async Task<(int Months, int Rows)> RecalculateAsync(string? onlyMonth = null)
    {
        var employees = await _db.Employees.AsNoTracking().ToListAsync();
        var byCode = employees.ToDictionary(e => e.Code, e => e);
        var schedules = (await _db.UnitSchedules.AsNoTracking().ToListAsync())
            .ToDictionary(s => s.Unit, s => s);
        var cycles = (await _db.GuardCycles.AsNoTracking().ToListAsync())
            .ToDictionary(c => c.EmployeeCode, c => c);

        var daysQuery = _db.AttendanceDays.AsQueryable();
        if (!string.IsNullOrEmpty(onlyMonth)) daysQuery = daysQuery.Where(d => d.Month == onlyMonth);
        var days = await daysQuery.ToListAsync();

        var punches = (await _db.ManualPunches.AsNoTracking()
                .Where(p => p.Status == "approved").ToListAsync())
            .GroupBy(p => $"{p.Employee}|{p.Date}")
            .ToDictionary(g => g.Key, g => g.ToList());

        var leaves = (await _db.LeaveMissions.AsNoTracking().ToListAsync())
            .GroupBy(l => $"{l.Employee}|{l.Date}")
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var day in days)
        {
            var employee = byCode.TryGetValue(day.Code, out var emp) ? emp : null;
            var unitSchedule = employee != null && schedules.TryGetValue(employee.Unit, out var us) ? us : null;
            var guardShift = cycles.TryGetValue(day.Code, out var cycle)
                ? AttendanceEngine.CycleFor(cycle, day.Date) : null;
            var schedule = AttendanceEngine.ScheduleForEmployee(employee, unitSchedule, guardShift);

            // ── ترددهای دستی تأییدشده، ورود/خروج را بازنویسی می‌کنند
            if (punches.TryGetValue(day.Key, out var dayPunches) && dayPunches.Count > 0)
            {
                var entry = dayPunches.Where(p => p.Type == "entry")
                    .OrderBy(p => AttendanceEngine.Clock(p.Time) ?? int.MaxValue).FirstOrDefault();
                var exit = dayPunches.Where(p => p.Type == "exit")
                    .OrderByDescending(p => AttendanceEngine.Clock(p.Time) ?? int.MinValue).FirstOrDefault();

                if (entry != null) day.First = entry.Time;
                if (exit != null) day.Last = exit.Time;
                day.ManualCorrectionApplied = true;
                day.Source = "raw";

                var first = AttendanceEngine.Clock(day.First);
                var last = AttendanceEngine.Clock(day.Last);
                if (first != null && last != null)
                {
                    var span = last.Value - first.Value;
                    if (span < 0) span += 1440;
                    day.Presence = Math.Max(0, span);
                }
            }

            AttendanceEngine.ApplyRawRules(day, schedule);

            // ── مرخصی و مأموریت، کسری کار را کاهش می‌دهند
            var leaveMinutes = 0;
            var missionMinutes = 0;
            if (leaves.TryGetValue(day.Key, out var records))
            {
                foreach (var record in records)
                {
                    var minutes = AttendanceEngine.ManualMinutes(record, schedule);
                    if (record.Type.Contains("مأموریت")) missionMinutes += minutes;
                    else leaveMinutes += minutes;
                }
            }
            day.Leave = leaveMinutes;
            day.Mission = missionMinutes;
            day.Shortfall = Math.Max(0, day.Shortfall - leaveMinutes - missionMinutes);
        }

        await _db.SaveChangesAsync();

        // ── تجمیع ماهانه و تولید ردیف‌های حقوق
        var months = days.Select(d => d.Month).Distinct().ToList();
        var rowCount = 0;

        foreach (var month in months)
        {
            var parts = month.Split('/');
            if (parts.Length < 2) continue;
            if (!int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var mo)) continue;

            var rules = await _payroll.RulesForAsync(year);
            var existing = await _db.PayrollRows.Where(r => r.Month == month).ToListAsync();
            var existingByCode = existing.ToDictionary(r => r.Code, r => r);

            var grouped = days.Where(d => d.Month == month).GroupBy(d => d.Code);
            foreach (var group in grouped)
            {
                var code = group.Key;
                var employee = byCode.TryGetValue(code, out var e) ? e : null;

                var presence = group.Sum(d => d.Presence);
                var leave = group.Sum(d => d.Leave);
                var mission = group.Sum(d => d.Mission);
                var shortfall = group.Sum(d => d.Shortfall);
                var ot = group.Sum(d => d.Ot);
                var unauthorizedOt = group.Sum(d => d.UnauthorizedOt);

                // ── همان فرمول attendance-engine.js: hourly = base/220
                var baseSalary = employee?.Salary ?? 0m;
                var hourly = baseSalary / 220m;
                var otPay = PayrollEngine.JsRound(hourly * (1m + rules.OvertimePremiumRate / 100m) * ot / 60m);
                var shortfallPay = PayrollEngine.JsRound(hourly * shortfall / 60m);

                var result = employee == null ? null : PayrollEngine.CalculatePayroll(employee, rules,
                    new PayrollInput
                    {
                        Year = year,
                        Month = mo,
                        Base = employee.Salary,
                        Overtime = otPay,
                        ShortfallPay = shortfallPay,
                        Attraction = employee.AttractionPay,
                        Supervisor = employee.SupervisorPay,
                        Performance = employee.PerformancePay,
                        Agreed = employee.AgreedBenefits
                    });

                if (!existingByCode.TryGetValue(code, out var row))
                {
                    row = new PayrollRow { Month = month, Code = code };
                    _db.PayrollRows.Add(row);
                }

                row.Name = group.First().Name;
                row.DeviceCode = group.Select(d => d.DeviceCode).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "—";
                row.Status = group.Any(d => d.Status.Contains("استراحت")) ? "حضور در استراحت؛ منتظر تصمیم اداری" : "";
                row.Presence = AttendanceEngine.Hm(presence);
                row.Leave = AttendanceEngine.Hm(leave);
                row.Mission = AttendanceEngine.Hm(mission);
                row.Shortfall = AttendanceEngine.Hm(shortfall);
                row.Ot = AttendanceEngine.Hm(ot);
                row.UnauthorizedOt = AttendanceEngine.Hm(unauthorizedOt);
                row.OtPay = otPay;
                row.ShortfallPay = shortfallPay;
                row.CalculatedAt = DateTime.UtcNow;

                if (result != null)
                {
                    row.Base = result.Base;
                    row.Seniority = result.Seniority;
                    row.Housing = result.Housing;
                    row.Food = result.Food;
                    row.Marriage = result.Marriage;
                    row.Children = result.Child;
                    row.Attraction = result.Attraction;
                    row.Supervisor = result.Supervisor;
                    row.Performance = result.Performance;
                    row.Agreed = result.Agreed;
                    row.ShiftPay = result.Shift;
                    row.NightPay = result.Night;
                    row.FridayPay = result.Friday;
                    row.MissionPay = result.Mission;
                    row.Gross = result.Gross;
                    row.InsuranceBase = result.InsuranceBase;
                    row.Insurance = result.Insurance;
                    row.EmployerInsurance = result.EmployerInsurance;
                    row.TaxableIncome = result.TaxableIncome;
                    row.Tax = result.Tax;
                    row.Net = result.Net;
                }
                rowCount++;
            }
        }

        await _db.SaveChangesAsync();
        return (months.Count, rowCount);
    }
}
