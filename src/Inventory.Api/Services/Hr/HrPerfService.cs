using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>ارزیابی عملکرد و KPI — دوره، شاخص، نمره‌دهی، کارنامه و اتصال پاداش به حقوق (فاز ۴)</summary>
public class HrPerfService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly HrTimeService _hrtime;

    public HrPerfService(AppDbContext db, INotifyService notify, HrTimeService hrtime)
    { _db = db; _notify = notify; _hrtime = hrtime; }

    // ================= دوره =================

    public async Task<List<HrPerfPeriod>> PeriodsAsync(Paging.Request? pagination = null)
        => await _db.HrPerfPeriods.AsNoTracking().OrderByDescending(p => p.Year).ThenByDescending(p => p.Id).ToPageListAsync(pagination);

    public async Task<HrPerfPeriod> CreatePeriodAsync(string title, int year, DateTime start, DateTime end,
        decimal bonusMonth, double minScore, string byName)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("عنوان دوره را وارد کنید.");
        if (end <= start) throw new InvalidOperationException("پایان دوره باید بعد از شروع باشد.");
        if (bonusMonth < 0 || bonusMonth > 12) throw new InvalidOperationException("مبنای پاداش باید بین ۰ تا ۱۲ ماه باشد.");
        var p = new HrPerfPeriod
        {
            Title = title.Trim(), Year = year, StartDate = start.Date, EndDate = end.Date,
            BonusMonthSalary = bonusMonth, MinScoreForBonus = minScore, CreatedBy = byName,
        };
        _db.HrPerfPeriods.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    public async Task<HrPerfPeriod> SetPeriodStatusAsync(int id, string status)
    {
        var p = await _db.HrPerfPeriods.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        if (status == "Open")
        {
            if (p.Status == "Closed") { p.Status = "Open"; await _db.SaveChangesAsync(); return p; } // بازگشایی
            if (p.Status == "Open") return p;
            var kpis = await _db.HrPerfKpis.Where(k => k.PeriodId == id && k.IsActive).ToListAsync();
            if (kpis.Count == 0) throw new InvalidOperationException("دوره شاخص فعالی ندارد.");
            var sum = kpis.Sum(k => k.Weight);
            if (Math.Abs(sum - 100) > 0.01) throw new InvalidOperationException($"جمع وزن شاخص‌ها باید ۱۰۰ باشد (فعلی: {sum:0.##}).");
            if (await _db.HrPerfPeriods.AnyAsync(x => x.Id != id && x.Status == "Open"))
                throw new InvalidOperationException("یک دوره باز دیگر وجود دارد؛ ابتدا آن را ببندید.");
            p.Status = "Open";
        }
        else if (status == "Closed")
        {
            if (p.Status != "Open") throw new InvalidOperationException("فقط دوره باز بسته می‌شود.");
            p.Status = "Closed";
        }
        else throw new InvalidOperationException("وضعیت نامعتبر است.");
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("hr-perf");
        return p;
    }

    public async Task DeletePeriodAsync(int id)
    {
        var p = await _db.HrPerfPeriods.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        if (p.Status != "Draft") throw new InvalidOperationException("فقط دوره پیش‌نویس حذف می‌شود.");
        _db.HrPerfScores.RemoveRange(_db.HrPerfScores.Where(s => s.PeriodId == id));
        _db.HrPerfResults.RemoveRange(_db.HrPerfResults.Where(r => r.PeriodId == id));
        _db.HrPerfKpis.RemoveRange(_db.HrPerfKpis.Where(k => k.PeriodId == id));
        _db.HrPerfPeriods.Remove(p);
        await _db.SaveChangesAsync();
    }

    // ================= شاخص =================

    public async Task<List<HrPerfKpi>> KpisAsync(int periodId, Paging.Request? pagination = null)
        => await _db.HrPerfKpis.AsNoTracking().Where(k => k.PeriodId == periodId).OrderBy(k => k.Id).ToPageListAsync(pagination);

    public async Task<HrPerfKpi> SaveKpiAsync(int periodId, int id, string code, string title, string? description,
        double weight, double maxScore, string category, bool isActive)
    {
        var p = await _db.HrPerfPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        if (p.Status == "Closed") throw new InvalidOperationException("دوره بسته است.");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("عنوان شاخص را وارد کنید.");
        if (weight <= 0 || weight > 100) throw new InvalidOperationException("وزن باید بین ۰ تا ۱۰۰ باشد.");
        if (maxScore <= 0) throw new InvalidOperationException("سقف نمره نامعتبر است.");
        HrPerfKpi k;
        if (id == 0)
        {
            k = new HrPerfKpi { PeriodId = periodId };
            _db.HrPerfKpis.Add(k);
        }
        else
        {
            k = await _db.HrPerfKpis.FirstOrDefaultAsync(x => x.Id == id && x.PeriodId == periodId)
                ?? throw new InvalidOperationException("شاخص پیدا نشد.");
        }
        k.Code = (code ?? "").Trim(); k.Title = title.Trim(); k.Description = description;
        k.Weight = weight; k.MaxScore = maxScore;
        k.Category = category is "Behavioral" or "Skill" or "Managerial" ? category : "General";
        k.IsActive = isActive;
        await _db.SaveChangesAsync();
        return k;
    }

    public async Task DeleteKpiAsync(int id)
    {
        var k = await _db.HrPerfKpis.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("شاخص پیدا نشد.");
        var st = await _db.HrPerfPeriods.AsNoTracking().Where(x => x.Id == k.PeriodId).Select(x => x.Status).FirstOrDefaultAsync();
        if (st != "Draft") throw new InvalidOperationException("حذف شاخص فقط در دوره پیش‌نویس ممکن است.");
        _db.HrPerfScores.RemoveRange(_db.HrPerfScores.Where(s => s.KpiId == id));
        _db.HrPerfKpis.Remove(k);
        await _db.SaveChangesAsync();
    }

    // ================= نمره‌دهی =================

    public record ScoreItem(int KpiId, double Score, string? Note);

    public async Task<int> SaveScoresAsync(int periodId, int employeeId, List<ScoreItem> items, int byId, string byName)
    {
        var p = await _db.HrPerfPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        if (p.Status != "Open") throw new InvalidOperationException("نمره‌دهی فقط در دوره باز ممکن است.");
        var emp = await _db.FaHrPersonnel.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId)
            ?? throw new InvalidOperationException("پرسنل پیدا نشد.");
        if (emp.Status != FaHrPersonnelStatus.Active) throw new InvalidOperationException("پرسنل فعال نیست.");
        await _hrtime.EnsureAllowedPersonnelAsync(employeeId);
        var kpis = await _db.HrPerfKpis.Where(k => k.PeriodId == periodId && k.IsActive).ToDictionaryAsync(k => k.Id);
        if (items.Count == 0) throw new InvalidOperationException("نمره‌ای ارسال نشده است.");
        var n = 0;
        foreach (var it in items)
        {
            if (!kpis.TryGetValue(it.KpiId, out var k)) throw new InvalidOperationException("یکی از شاخص‌ها معتبر نیست.");
            if (it.Score < 0 || it.Score > k.MaxScore)
                throw new InvalidOperationException($"نمره «{k.Title}» باید بین ۰ تا {k.MaxScore:0.##} باشد.");
            var ex = await _db.HrPerfScores.FirstOrDefaultAsync(s => s.PeriodId == periodId && s.EmployeeId == employeeId && s.KpiId == it.KpiId);
            if (ex == null)
            {
                _db.HrPerfScores.Add(new HrPerfScore
                {
                    PeriodId = periodId, EmployeeId = employeeId, KpiId = it.KpiId, Score = it.Score,
                    ScoredByUserId = byId, ScoredByName = byName, Note = it.Note,
                });
            }
            else { ex.Score = it.Score; ex.Note = it.Note; ex.ScoredByUserId = byId; ex.ScoredByName = byName; ex.UpdatedAt = DateTime.Now; }
            n++;
        }
        await _db.SaveChangesAsync();
        return n;
    }

    public async Task<List<HrPerfScore>> ScoresAsync(int periodId, int employeeId, Paging.Request? pagination = null)
        => await _db.HrPerfScores.AsNoTracking()
            .Where(s => s.PeriodId == periodId && s.EmployeeId == employeeId).OrderBy(s => s.KpiId).ThenBy(x => x.Id).ToPageListAsync(pagination);

    // ================= کارنامه =================

    private async Task<double> GradeCutAsync(string key, double fb)
    {
        var v = await _hrtime.RuleAsync(key, "");
        return double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : fb;
    }

    public static string GradeOf(double total, double a, double b, double c)
        => total >= a ? "A" : total >= b ? "B" : total >= c ? "C" : "D";

    public record ResultRow(int EmployeeId, string EmployeeName, decimal BaseSalary, int KpiCount, int ScoredCount,
        double TotalScore, string Grade, decimal BonusAmount, int? PayYear, int? PayMonth, string Status);

    public async Task<List<ResultRow>> ResultsAsync(int periodId, Paging.Request? pagination = null)
    {
        var p = await _db.HrPerfPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        var a = await GradeCutAsync("perf.grade.a", 90);
        var b = await GradeCutAsync("perf.grade.b", 75);
        var c = await GradeCutAsync("perf.grade.c", 60);
        var kpis = await _db.HrPerfKpis.AsNoTracking().Where(k => k.PeriodId == periodId && k.IsActive).ToListAsync();

        // ترتیب الزامی گزارش: Filter → Aggregate/Rank → Order → Count → Page → Materialize.
        // نمرهٔ وزنی هر پرسنل با زیرمجموعهٔ همبسته در SQL جمع می‌شود؛ رکوردهای ذخیره‌شده
        // (کارنامهٔ قطعی) بر نمرهٔ محاسبه‌شده مقدم‌اند. فقط ردیف‌های صفحه materialize می‌شوند.
        var scoreQ = _db.HrPerfScores.AsNoTracking().Where(s => s.PeriodId == periodId);
        var resultQ = _db.HrPerfResults.AsNoTracking().Where(r => r.PeriodId == periodId);
        var kpiQ = _db.HrPerfKpis.AsNoTracking().Where(k => k.PeriodId == periodId && k.IsActive && k.MaxScore > 0);

        // فقط پرسنلِ پیوندخورده به کاربرِ فعالِ دارای دسترسی به ماژول
        var allowedEmp = (await _hrtime.Access.PersonnelIdsAsync()).ToList();
        var rankQuery =
            from e in _db.FaHrPersonnel.AsNoTracking()
            where allowedEmp.Contains(e.Id)
            where scoreQ.Any(s => s.EmployeeId == e.Id) || resultQ.Any(r => r.EmployeeId == e.Id)
            let saved = resultQ.FirstOrDefault(r => r.EmployeeId == e.Id)
            let computed = scoreQ.Where(s => s.EmployeeId == e.Id)
                .Join(kpiQ, s => s.KpiId, k => k.Id, (s, k) => s.Score / k.MaxScore * k.Weight)
                .Sum(x => (double?)x) ?? 0
            let scoredCount = scoreQ.Count(s => s.EmployeeId == e.Id)
            orderby (saved == null ? computed : saved.TotalScore) descending, e.Id
            select new { e, saved, computed, scoredCount };

        var page = await rankQuery.ToPageListAsync(pagination);

        var rows = new List<ResultRow>();
        foreach (var x in page)
        {
            // نمایش و گرید/پاداش مانند قبل با نمرهٔ گردشدهٔ ۲ رقمی محاسبه می‌شود.
            var total = Math.Round(x.computed, 2);
            var r = x.saved;
            var bonus = r?.BonusAmount ?? (total >= p.MinScoreForBonus
                ? Math.Round(x.e.BaseSalary * p.BonusMonthSalary * (decimal)(total / 100)) : 0);
            rows.Add(new(x.e.Id, $"{x.e.FirstName} {x.e.LastName}".Trim(), x.e.BaseSalary, kpis.Count, x.scoredCount,
                r?.TotalScore ?? total, r?.Grade ?? GradeOf(total, a, b, c), bonus, r?.PayYear, r?.PayMonth, r?.Status ?? "—"));
        }
        return rows;
    }

    public async Task<HrPerfResult> ComputeResultAsync(int periodId, int employeeId)
    {
        await _hrtime.EnsureAllowedPersonnelAsync(employeeId);
        var p = await _db.HrPerfPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        if (p.Status == "Closed") throw new InvalidOperationException("دوره بسته است.");
        var kpis = await _db.HrPerfKpis.AsNoTracking().Where(k => k.PeriodId == periodId && k.IsActive).ToListAsync();
        if (kpis.Count == 0) throw new InvalidOperationException("دوره شاخص فعالی ندارد.");
        var scores = await _db.HrPerfScores.AsNoTracking()
            .Where(s => s.PeriodId == periodId && s.EmployeeId == employeeId).ToListAsync();
        var missing = kpis.Where(k => scores.All(s => s.KpiId != k.Id)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"نمره {missing.Count} شاخص ثبت نشده (مثلاً {missing[0].Title}).");
        var sum = kpis.Sum(k => k.Weight);
        if (Math.Abs(sum - 100) > 0.01) throw new InvalidOperationException($"جمع وزن شاخص‌ها باید ۱۰۰ باشد (فعلی: {sum:0.##}).");
        double total = 0;
        foreach (var k in kpis)
            total += scores.First(s => s.KpiId == k.Id).Score / k.MaxScore * k.Weight;
        total = Math.Round(total, 2);
        var a = await GradeCutAsync("perf.grade.a", 90);
        var b = await GradeCutAsync("perf.grade.b", 75);
        var c = await GradeCutAsync("perf.grade.c", 60);
        var emp = await _db.FaHrPersonnel.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId)
            ?? throw new InvalidOperationException("پرسنل پیدا نشد.");
        var bonus = total >= p.MinScoreForBonus
            ? Math.Round(emp.BaseSalary * p.BonusMonthSalary * (decimal)(total / 100)) : 0;
        var r = await _db.HrPerfResults.FirstOrDefaultAsync(x => x.PeriodId == periodId && x.EmployeeId == employeeId);
        if (r == null) { r = new HrPerfResult { PeriodId = periodId, EmployeeId = employeeId }; _db.HrPerfResults.Add(r); }
        else if (r.Status == "Final") throw new InvalidOperationException("کارنامه قطعی شده؛ ابتدا برگردانید.");
        r.TotalScore = total; r.Grade = GradeOf(total, a, b, c); r.BonusAmount = bonus; r.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return r;
    }

    public async Task<HrPerfResult> FinalizeAsync(int periodId, int employeeId, int payYear, int payMonth, string byName, string? note)
    {
        if (payMonth is < 1 or > 12) throw new InvalidOperationException("ماه پرداخت نامعتبر است.");
        var r = await ComputeResultAsync(periodId, employeeId);
        r.Status = "Final"; r.PayYear = payYear; r.PayMonth = payMonth;
        r.FinalizedBy = byName; r.FinalizedAt = DateTime.Now; r.Note = note; r.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("hr-perf");
        return r;
    }

    public async Task<HrPerfResult> ReopenResultAsync(int periodId, int employeeId)
    {
        var st = await _db.HrPerfPeriods.AsNoTracking().Where(x => x.Id == periodId).Select(x => x.Status).FirstOrDefaultAsync();
        if (st == "Closed") throw new InvalidOperationException("دوره بسته است.");
        var r = await _db.HrPerfResults.FirstOrDefaultAsync(x => x.PeriodId == periodId && x.EmployeeId == employeeId)
            ?? throw new InvalidOperationException("کارنامه‌ای وجود ندارد.");
        r.Status = "Draft"; r.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return r;
    }

    public record PerfSummary(int Employees, double AvgScore, int CountA, int CountB, int CountC, int CountD, decimal TotalBonus, int FinalCount);

    public async Task<PerfSummary> SummaryAsync(int periodId)
    {
        var rows = await ResultsAsync(periodId);
        return new(rows.Count,
            rows.Count == 0 ? 0 : Math.Round(rows.Average(r => r.TotalScore), 2),
            rows.Count(r => r.Grade == "A"), rows.Count(r => r.Grade == "B"),
            rows.Count(r => r.Grade == "C"), rows.Count(r => r.Grade == "D"),
            rows.Where(r => r.Status == "Final").Sum(r => r.BonusAmount),
            rows.Count(r => r.Status == "Final"));
    }
}
