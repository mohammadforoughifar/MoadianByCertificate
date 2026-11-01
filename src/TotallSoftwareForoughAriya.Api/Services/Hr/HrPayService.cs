using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services.Accounting;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// =====================================================================
// فرمول‌خوان حقوق: عدد، چهار عمل، پرانتز، متغیر، ROUND/MIN/MAX/IF، مقایسه
// =====================================================================
public static class HrPayFormula
{
    public static decimal Eval(string formula, Dictionary<string, decimal> vars)
    {
        var p = new Parser(formula, vars);
        var v = p.ParseExpr();
        p.ExpectEnd();
        return v;
    }

    private sealed class Parser
    {
        private readonly string _s; private int _i;
        private readonly Dictionary<string, decimal> _vars;
        public Parser(string s, Dictionary<string, decimal> vars)
        { _s = s.ToUpperInvariant(); _vars = new Dictionary<string, decimal>(vars, StringComparer.OrdinalIgnoreCase); }

        public decimal ParseExpr()
        {
            var v = ParseTerm();
            while (true)
            {
                Skip();
                if (Match('+')) v += ParseTerm();
                else if (Match('-')) v -= ParseTerm();
                else return v;
            }
        }
        private decimal ParseTerm()
        {
            var v = ParseFactor();
            while (true)
            {
                Skip();
                if (Match('*')) v *= ParseFactor();
                else if (Match('/'))
                {
                    var d = ParseFactor();
                    if (d == 0) throw new InvalidOperationException("تقسیم بر صفر در فرمول.");
                    v /= d;
                }
                else return v;
            }
        }
        private decimal ParseFactor()
        {
            Skip();
            if (Match('-')) return -ParseFactor();
            if (Match('+')) return ParseFactor();
            if (Match('(')) { var v = ParseExpr(); Skip(); if (!Match(')')) throw new InvalidOperationException("پرانتز بسته نشده است."); return v; }
            if (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) return ParseNumber();
            if (_i < _s.Length && (char.IsLetter(_s[_i]) || _s[_i] == '_'))
            {
                var name = ParseName();
                Skip();
                if (Match('(')) return ParseFunc(name);
                if (_vars.TryGetValue(name, out var v)) return v;
                throw new InvalidOperationException($"متغیر ناشناخته در فرمول: {name}");
            }
            throw new InvalidOperationException($"عبارت نامعتبر در فرمول (موقعیت {_i + 1}).");
        }
        private decimal ParseFunc(string name)
        {
            if (name == "IF")
            {
                var c = ParseCond(); Skip(); if (!Match(',')) throw new InvalidOperationException("IF سه آرگومان می‌خواهد.");
                var a = ParseExpr(); Skip(); if (!Match(',')) throw new InvalidOperationException("IF سه آرگومان می‌خواهد.");
                var b = ParseExpr(); Skip(); if (!Match(')')) throw new InvalidOperationException("پرانتز IF بسته نشده است.");
                return c ? a : b;
            }
            var args = new List<decimal> { ParseExpr() };
            Skip();
            while (Match(','))
            {
                if (name is "MIN" or "MAX") args.Add(ParseExpr());
                else args.Add(ParseNumber());
                Skip();
            }
            if (!Match(')')) throw new InvalidOperationException($"پرانتز {name} بسته نشده است.");
            return name switch
            {
                "ROUND" => Math.Round(args[0], args.Count > 1 ? (int)args[1] : 0),
                "MIN" => args.Min(),
                "MAX" => args.Max(),
                _ => throw new InvalidOperationException($"تابع ناشناخته: {name}")
            };
        }
        private bool ParseCond()
        {
            var a = ParseExpr(); Skip();
            string op;
            if (Match('>')) op = Match('=') ? ">=" : ">";
            else if (Match('<')) op = Match('=') ? "<=" : "<";
            else if (Match('=')) { if (Match('=')) { } op = "=="; }
            else if (Match('!')) { if (!Match('=')) throw new InvalidOperationException("عملگر مقایسه نامعتبر."); op = "!="; }
            else throw new InvalidOperationException("در IF باید مقایسه (>, <, =, ...) باشد.");
            var b = ParseExpr();
            return op switch { ">" => a > b, "<" => a < b, ">=" => a >= b, "<=" => a <= b, "==" => a == b, _ => a != b };
        }
        private decimal ParseNumber()
        {
            var start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
            if (!decimal.TryParse(_s.Substring(start, _i - start), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                throw new InvalidOperationException("عدد نامعتبر در فرمول.");
            return v;
        }
        private string ParseName()
        {
            var start = _i;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
            return _s.Substring(start, _i - start);
        }
        private void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private bool Match(char c) { if (_i < _s.Length && _s[_i] == c) { _i++; return true; } return false; }
        public void ExpectEnd() { Skip(); if (_i < _s.Length) throw new InvalidOperationException($"عبارت نامعتبر در فرمول (موقعیت {_i + 1})."); }
    }
}

/// <summary>سرویس حقوق و دستمزد: فرمول‌ساز، محاسبه از کارکرد، بیمه/مالیات، وام، تسویه، خروجی‌ها، سند</summary>
public class HrPayService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly HrTimeService _hrtime;
    private readonly IAccountingService _acc;

    public HrPayService(AppDbContext db, INotifyService notify, HrTimeService hrtime, IAccountingService acc)
    { _db = db; _notify = notify; _hrtime = hrtime; _acc = acc; }

    // ================= قوانین =================

    public static readonly (string Key, string Value, string Title)[] DefaultPayRules =
    [
        ("pay.min_wage", "103909270", "حداقل مزد ماهانه مصوب (ریال)"),
        ("pay.bon", "22000000", "بن کارگری ماهانه (ریال)"),
        ("pay.maskan", "9000000", "حق مسکن ماهانه (ریال)"),
        ("pay.eyab", "0", "ایاب و ذهاب پیش‌فرض (ریال)"),
        ("pay.ins.employee", "7", "بیمه سهم کارگر (٪)"),
        ("pay.ins.employer", "20", "بیمه سهم کارفرما (٪)"),
        ("pay.ins.unemployment", "3", "بیمه بیکاری (٪)"),
        ("pay.ins.ceiling", "0", "سقف مشمول بیمه ماهانه (ریال — 0=بدون سقف)"),
        ("pay.ins.hardjob.extra", "4", "اضافه بیمه کارفرما برای مشاغل سخت و زیان‌آور (٪)"),
        ("pay.tax.eydi.exemption", "0", "معافیت مالیاتی عیدی (ریال — ۱/۱۲ معافیت سالانه)"),
        ("pay.workshop.code", "", "کد کارگاه بیمه"),
        ("pay.workshop.name", "", "نام کارگاه"),
        ("pay.variance.pct", "10", "آستانه هشدار مغایرت ماهانه (٪)"),
        ("pay.acc.salaryExpense", "", "کد حساب هزینه حقوق"),
        ("pay.acc.insuranceExpense", "", "کد حساب هزینه بیمه کارفرما"),
        ("pay.acc.accrualExpense", "", "کد حساب هزینه ذخیره سنوات"),
        ("pay.acc.payable", "", "کد حساب حقوق پرداختنی"),
        ("pay.acc.taxPayable", "", "کد حساب مالیات پرداختنی"),
        ("pay.acc.insurancePayable", "", "کد حساب بیمه پرداختنی"),
        ("pay.acc.accrualPayable", "", "کد حساب ذخیره سنوات پرداختنی"),
        ("pay.acc.otherDed", "", "کد حساب سایر کسورات"),
    ];

    public async Task<Dictionary<string, string>> GetPayRulesAsync(Paging.Request? pagination = null)
    {
        var defaultKeys = DefaultPayRules.Select(r => r.Key).ToList();
        var existing = await _db.HrTimeRules.AsNoTracking().Where(r => defaultKeys.Contains(r.Key)).Select(r => r.Key).ToListAsync();
        var missing = DefaultPayRules.Where(d => !existing.Contains(d.Key)).ToList();
        if (missing.Count > 0)
        {
            foreach (var rule in missing)
                _db.HrTimeRules.Add(new HrTimeRule { Key = rule.Key, Value = rule.Value, Title = rule.Title });
            await _db.SaveChangesAsync();
        }
        return await _hrtime.GetRulesAsync(pagination, "pay.");
    }

    public Task SavePayRulesAsync(Dictionary<string, string> values)
        => _hrtime.SaveRulesAsync(values.Where(kv => kv.Key.StartsWith("pay.")).ToDictionary(kv => kv.Key, kv => kv.Value));

    private static decimal R(Dictionary<string, string> rules, string key, decimal fb)
        => rules.TryGetValue(key, out var v) && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : fb;

    // ================= سید آیتم‌ها و پلکان =================

    public async Task EnsureSeedAsync()
    {
        if (!await _db.HrPayItems.AnyAsync())
        {
            var items = new List<HrPayItem>
            {
                new() { Code = "BASE_HOKM", Title = "حقوق پایه حکم", Kind = "Earning", Category = "Hokmi", Formula = "BASE*DAYS_PAID/30", SortOrder = 10 },
                new() { Code = "OT_NORMAL", Title = "اضافه‌کاری عادی", Kind = "Earning", Category = "Mazaya", Formula = "OT_H*HOURLY_WAGE*OT_COEF_N", SortOrder = 20 },
                new() { Code = "OT_HOLIDAY", Title = "تعطیل‌کاری", Kind = "Earning", Category = "Mazaya", Formula = "OT_H_HOL*HOURLY_WAGE*OT_COEF_H", SortOrder = 21 },
                new() { Code = "OT_NIGHT", Title = "شب‌کاری", Kind = "Earning", Category = "Mazaya", Formula = "OT_H_NIGHT*HOURLY_WAGE*OT_COEF_NT", SortOrder = 22 },
                new() { Code = "BON", Title = "بن کارگری", Kind = "Earning", Category = "Mazaya", Formula = "BON*DAYS_PAID/30", SortOrder = 30 },
                new() { Code = "MASKAN", Title = "حق مسکن", Kind = "Earning", Category = "Mazaya", Formula = "MASKAN*DAYS_PAID/30", SortOrder = 31 },
                new() { Code = "OLAD", Title = "حق اولاد", Kind = "Earning", Category = "Mazaya", Formula = "CHILDREN*3*MIN_DAILY", IsTaxable = false, IsInsuranceable = false, SortOrder = 32 },
                new() { Code = "EYAB", Title = "ایاب و ذهاب", Kind = "Earning", Category = "Mazaya", Formula = "EYAB", IsInsuranceable = false, SortOrder = 33 },
                new() { Code = "SANAVAT", Title = "ذخیره سنوات ماهانه", Kind = "Earning", Category = "Other", Formula = "ROUND(BASE/12)", IsTaxable = false, IsInsuranceable = false, IsAccrual = true, SortOrder = 40 },
            };
            _db.HrPayItems.AddRange(items);
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>پلکان پیش‌فرض مالیات (قابل ویرایش — حتماً مطابق قانون بودجه همان سال تنظیم شود)</summary>
    public async Task EnsureTaxSeedAsync(int year)
    {
        if (await _db.HrPayTaxBrackets.AnyAsync(b => b.Year == year)) return;
        var rows = new (decimal From, decimal To, decimal Rate)[]
        {
            (0, 30000000, 0), (30000000, 40000000, 10), (40000000, 60000000, 15),
            (60000000, 100000000, 20), (100000000, 0, 25),
        };
        foreach (var (f, t, r) in rows)
            _db.HrPayTaxBrackets.Add(new HrPayTaxBracket { Year = year, FromAmount = f, ToAmount = t, Rate = r });
        await _db.SaveChangesAsync();
    }

    public static decimal CalcTax(decimal taxable, List<HrPayTaxBracket> brackets)
    {
        if (taxable <= 0) return 0;
        decimal tax = 0;
        foreach (var b in brackets.OrderBy(x => x.FromAmount))
        {
            var hi = b.ToAmount <= 0 ? decimal.MaxValue : b.ToAmount;
            var portion = Math.Min(taxable, hi) - b.FromAmount;
            if (portion > 0) tax += portion * b.Rate / 100;
        }
        return Math.Round(tax);
    }

    // ================= تجمیع ماهانه کارکرد =================

    public record MonthAgg(double DaysPaid, double DaysWorked, double OtH, double OtHHol, double OtHNight,
        double MissionDays, double UnpaidDays, decimal MissionAllowance);

    public static (DateTime Start, DateTime EndExclusive) JMonthRange(int jy, int jm)
    {
        var s = PersianDate.ToGregorian(jy, jm, 1);
        var e = jm == 12 ? PersianDate.ToGregorian(jy + 1, 1, 1) : PersianDate.ToGregorian(jy, jm + 1, 1);
        return (s, e);
    }

    public async Task<MonthAgg> MonthAggAsync(int? userId, int jy, int jm)
    {
        var (start, end) = JMonthRange(jy, jm);
        double otN = 0, otH = 0, otNt = 0, worked = 0, absent = 0;
        double unpaid = 0, missionDays = 0;
        decimal missionAllow = 0;
        if (userId != null)
        {
            var recs = await _db.AttendanceRecords.AsNoTracking()
                .Where(a => a.UserId == userId && a.WorkDate >= start && a.WorkDate < end).ToListAsync();
            var ext = await _hrtime.MonthExtrasSumAsync(userId.Value, jy, jm);
            otN = ext.OtN; otH = ext.OtH; otNt = ext.OtNight;
            worked = recs.Count(a => a.WorkMinutes > 0);
            absent = recs.Count(a => a.FinalStatus == "Absent");
            var leaves = await _db.LeaveRequests.AsNoTracking()
                .Where(l => l.RequesterUserId == userId && l.Status == "Approved"
                    && l.StartDate < end && l.EndDate >= start).ToListAsync();
            var lids = leaves.Select(l => l.Id).ToList();
            var lx = lids.Count == 0 ? new Dictionary<int, HrLeaveExtra>()
                : await _db.HrLeaveExtras.AsNoTracking().Where(x => lids.Contains(x.LeaveRequestId))
                    .ToDictionaryAsync(x => x.LeaveRequestId);
            string LCat(LeaveRequest l) => lx.TryGetValue(l.Id, out var e) ? e.Category : "Annual";
            foreach (var l in leaves)
            {
                if (l.Type == "Daily" && LCat(l) == "Unpaid")
                {
                    var os = l.StartDate.Date > start ? l.StartDate.Date : start;
                    var oe = l.EndDate.Date.AddDays(1) < end ? l.EndDate.Date.AddDays(1) : end;
                    unpaid += Math.Max(0, (oe - os).TotalDays);
                }
                else if (l.Type == "Hourly" && LCat(l) == "Unpaid" && l.StartDate >= start && l.StartDate < end)
                    unpaid += l.Hours / 8.0;
                else if (l.Type == "Mission")
                {
                    var os = l.StartDate.Date > start ? l.StartDate.Date : start;
                    var oe = l.EndDate.Date.AddDays(1) < end ? l.EndDate.Date.AddDays(1) : end;
                    missionDays += Math.Max(0, (oe - os).TotalDays);
                }
                else if (l.Type == "HourlyMission" && l.StartDate >= start && l.StartDate < end)
                    missionDays += l.Hours / 8.0;
                if (l.Type is "Mission" or "HourlyMission" && l.StartDate >= start && l.StartDate < end
                    && lx.TryGetValue(l.Id, out var le))
                    missionAllow += le.AllowanceAmount;
            }
        }
        var paid = Math.Max(0, 30 - unpaid - absent);
        return new(paid, worked, Math.Round(otN, 2), Math.Round(otH, 2), Math.Round(otNt, 2),
            Math.Round(missionDays, 2), Math.Round(unpaid + absent, 2), missionAllow);
    }

    // ================= محاسبه =================

    public record SlipLine(string Code, string Title, string Kind, decimal Amount, bool Taxable, bool Insuranceable, int? RefId = null);

    public async Task<HrPayRun> CreateRunAsync(int year, int month, string byName, string kind = "Monthly")
    {
        if (month is < 1 or > 12) throw new InvalidOperationException("ماه نامعتبر است.");
        kind = kind == "Eydi" ? "Eydi" : "Monthly";
        if (kind == "Eydi") month = 12;
        if (await _db.HrPayRuns.AnyAsync(r => r.Year == year && r.Month == month && r.Kind == kind))
            throw new InvalidOperationException("برای این دوره قبلاً دوره ساخته شده است.");
        await EnsureSeedAsync();
        await EnsureTaxSeedAsync(year);
        var run = new HrPayRun { Year = year, Month = month, Kind = kind, Status = "Draft", CreatedBy = byName };
        _db.HrPayRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    public async Task<HrPayRun> CalculateRunAsync(int runId)
    {
        var run = await _db.HrPayRuns.FirstOrDefaultAsync(r => r.Id == runId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        if (run.Status == "Locked") throw new InvalidOperationException("دوره قفل است؛ ابتدا باز کنید.");
        if (run.Kind == "Eydi") return await CalculateEydiAsync(run);
        var rules = await GetPayRulesAsync();
        var allRules = await _hrtime.GetRulesAsync();
        var minWage = await _hrtime.EffectiveMinWageAsync(run.Year);
        var brackets = await _db.HrPayTaxBrackets.AsNoTracking().Where(b => b.Year == run.Year).ToListAsync();
        var items = await _db.HrPayItems.AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.SortOrder).ToListAsync();
        // فقط پرسنلِ فعالِ جدول اختصاصی این ماژول که به کاربرِ فعالِ دارای دسترسی پیوند دارند
        var allowedEmp = await _hrtime.Access.PersonnelIdsAsync();
        var employees = (await _db.FaHrPersonnel.AsNoTracking().Where(e => e.Status == FaHrPersonnelStatus.Active).ToListAsync())
            .Where(e => allowedEmp.Contains(e.Id)).ToList();
        var profiles = await _db.HrPayProfiles.AsNoTracking().ToListAsync();
        var overrides = await _db.HrPayEmployeeItems.AsNoTracking().ToListAsync();

        _db.HrPaySlips.RemoveRange(_db.HrPaySlips.Where(s => s.RunId == runId));
        await _db.SaveChangesAsync();

        foreach (var emp in employees)
        {
            var prof = profiles.FirstOrDefault(p => p.EmployeeId == emp.Id);
            var agg = await MonthAggAsync(await _hrtime.UserIdOfEmployeeAsync(emp.Id), run.Year, run.Month);
            var vars = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["BASE"] = emp.BaseSalary,
                ["DAILY_WAGE"] = emp.BaseSalary / 30,
                ["HOURLY_WAGE"] = emp.BaseSalary / 220,
                ["MIN_WAGE"] = minWage,
                ["MIN_DAILY"] = minWage / 30,
                ["DAYS_PAID"] = (decimal)agg.DaysPaid,
                ["DAYS_WORKED"] = (decimal)agg.DaysWorked,
                ["OT_H"] = (decimal)agg.OtH,
                ["OT_H_HOL"] = (decimal)agg.OtHHol,
                ["OT_H_NIGHT"] = (decimal)agg.OtHNight,
                ["MISSION_DAYS"] = (decimal)agg.MissionDays,
                ["LEAVE_UNPAID"] = (decimal)agg.UnpaidDays,
                ["CHILDREN"] = prof?.ChildrenCount ?? 0,
                ["BON"] = R(rules, "pay.bon", 22000000),
                ["MASKAN"] = R(rules, "pay.maskan", 9000000),
                ["EYAB"] = R(rules, "pay.eyab", 0),
                ["OT_COEF_N"] = R(allRules, "ot.coef.normal", 1.4m),
                ["OT_COEF_H"] = R(allRules, "ot.coef.holiday", 1.4m),
                ["OT_COEF_NT"] = R(allRules, "ot.coef.night", 1.35m),
            };
            var lines = new List<SlipLine>();
            var pending = items.ToList();
            for (var pass = 0; pass <= items.Count && pending.Count > 0; pass++)
            {
                var progressed = false;
                foreach (var it in pending.ToList())
                {
                    var ov = overrides.FirstOrDefault(o => o.EmployeeId == emp.Id && o.ItemId == it.Id);
                    if (ov != null)
                    {
                        lines.Add(new(it.Code, it.Title, it.Kind, Math.Round(ov.Amount), it.IsTaxable, it.IsInsuranceable));
                        if (!it.IsAccrual) vars[it.Code] = ov.Amount;
                        pending.Remove(it); progressed = true;
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(it.Formula))
                    {
                        lines.Add(new(it.Code, it.Title, it.Kind, Math.Round(it.DefaultAmount), it.IsTaxable, it.IsInsuranceable));
                        if (!it.IsAccrual) vars[it.Code] = it.DefaultAmount;
                        pending.Remove(it); progressed = true;
                        continue;
                    }
                    try
                    {
                        var v = HrPayFormula.Eval(it.Formula, vars);
                        v = Math.Round(v);
                        lines.Add(new(it.Code, it.Title, it.Kind, v, it.IsTaxable, it.IsInsuranceable));
                        if (!it.IsAccrual) vars[it.Code] = v;
                        pending.Remove(it); progressed = true;
                    }
                    catch (InvalidOperationException ex) when (ex.Message.StartsWith("متغیر ناشناخته")) { /* پاس بعدی */ }
                }
                if (!progressed && pending.Count > 0)
                {
                    var it = pending.First();
                    try { HrPayFormula.Eval(it.Formula ?? "", vars); }
                    catch (Exception ex) { throw new InvalidOperationException($"خطای فرمول «{it.Title}» برای {emp.FirstName} {emp.LastName}: {ex.Message}"); }
                    throw new InvalidOperationException($"فرمول «{it.Title}» قابل حل نیست (ارجاع چرخه‌ای؟).");
                }
            }
            // خودکارها
            if (agg.MissionAllowance > 0)
                lines.Add(new("SYS_MISSION", "حق ماموریت", "Earning", Math.Round(agg.MissionAllowance), false, false));
            var arrears = await _db.HrPayArrears.Where(a => a.EmployeeId == emp.Id && a.Year == run.Year && a.Month == run.Month && a.Status == "Pending").ToListAsync();
            foreach (var a in arrears)
                lines.Add(new("SYS_ARREAR", a.Title ?? "معوقه", "Earning", a.Amount, true, true, a.Id));
            // فاز ۴: پاداش عملکرد قطعی‌شده (مشمول مالیات، غیرمشمول بیمه)
            var perfs = await _db.HrPerfResults.Where(x => x.EmployeeId == emp.Id && x.Status == "Final" && x.PayYear == run.Year && x.PayMonth == run.Month && x.BonusAmount > 0).ToListAsync();
            foreach (var pf in perfs)
                lines.Add(new("SYS_PERF", "پاداش عملکرد", "Earning", pf.BonusAmount, true, false, pf.Id));
            var loans = await _db.HrPayLoans.Where(l => l.EmployeeId == emp.Id && l.Status == "Active").ToListAsync();
            foreach (var ln in loans)
            {
                var idx = (run.Year * 12 + run.Month) - (ln.StartYear * 12 + ln.StartMonth);
                if (idx < 0 || idx >= ln.Installments) continue;
                var remaining = ln.Amount - ln.PaidCount * ln.MonthlyAmount;
                var due = Math.Min(ln.MonthlyAmount, Math.Max(0, remaining));
                if (due > 0) lines.Add(new("SYS_LOAN", ln.Kind == "Advance" ? "مساعده" : $"قسط وام ({ln.PaidCount + 1}/{ln.Installments})", "Deduction", due, false, false, ln.Id));
            }
            var onacc = await _db.HrPayOnAccounts.Where(o => o.EmployeeId == emp.Id && o.Year == run.Year && o.Month == run.Month && o.Status == "Pending").ToListAsync();
            foreach (var o in onacc)
                lines.Add(new("SYS_ONACC", "علی‌الحساب", "Deduction", o.Amount, false, false, o.Id));

            var accrualCodes = items.Where(i => i.IsAccrual).Select(i => i.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pay = lines.Where(l => !accrualCodes.Contains(l.Code)).ToList();
            var gross = pay.Where(l => l.Kind == "Earning").Sum(l => l.Amount);
            var taxable = pay.Where(l => l is { Kind: "Earning", Taxable: true }).Sum(l => l.Amount);
            var insBase = pay.Where(l => l is { Kind: "Earning", Insuranceable: true }).Sum(l => l.Amount);
            var ceiling = R(rules, "pay.ins.ceiling", 0);
            if (ceiling > 0) insBase = Math.Min(insBase, ceiling);
            var insPct = R(rules, "pay.ins.employee", 7);
            var insEmp = Math.Round(insBase * insPct / 100);
            var erPct = R(rules, "pay.ins.employer", 20) + R(rules, "pay.ins.unemployment", 3)
                + ((prof?.IsHardJob == true) ? R(rules, "pay.ins.hardjob.extra", 4) : 0);
            var insEr = Math.Round(insBase * erPct / 100);
            var taxBase = Math.Max(0, taxable - (prof?.ExtraTaxExempt ?? 0));
            var tax = CalcTax(taxBase, brackets);
            lines.Add(new("SYS_INS", "بیمه سهم کارگر", "Deduction", insEmp, false, false));
            lines.Add(new("SYS_TAX", "مالیات حقوق", "Deduction", tax, false, false));
            var ded = pay.Where(l => l.Kind == "Deduction").Sum(l => l.Amount) + insEmp + tax;
            var accrual = lines.Where(l => accrualCodes.Contains(l.Code)).Sum(l => l.Amount);

            _db.HrPaySlips.Add(new HrPaySlip
            {
                RunId = run.Id, EmployeeId = emp.Id, UserId = await _hrtime.UserIdOfEmployeeAsync(emp.Id),
                EmployeeName = $"{emp.FirstName} {emp.LastName}".Trim(),
                DaysPaid = agg.DaysPaid, DaysWorked = agg.DaysWorked, BaseAmount = emp.BaseSalary,
                GrossEarnings = gross, TaxableAmount = taxable, InsuranceableAmount = insBase,
                TaxAmount = tax, InsuranceAmount = insEmp, EmployerInsurance = insEr,
                OtherDeductions = ded - insEmp - tax, NetPay = gross - ded,
                DetailsJson = JsonSerializer.Serialize(lines),
            });
            _ = accrual;
        }
        await _db.SaveChangesAsync();
        var slips = await _db.HrPaySlips.AsNoTracking().Where(s => s.RunId == runId).ToListAsync();
        run.SlipCount = slips.Count;
        run.TotalGross = slips.Sum(s => s.GrossEarnings);
        run.TotalDeductions = slips.Sum(s => s.TaxAmount + s.InsuranceAmount + s.OtherDeductions);
        run.TotalNet = slips.Sum(s => s.NetPay);
        run.TotalTax = slips.Sum(s => s.TaxAmount);
        run.TotalInsuranceEmp = slips.Sum(s => s.InsuranceAmount);
        run.TotalInsuranceEr = slips.Sum(s => s.EmployerInsurance);
        if (run.Status == "Approved") run.Status = "Draft"; // محاسبه مجدد → پیش‌نویس
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("hr-pay");
        return run;
    }

    /// <summary>محاسبه عیدی و پاداش پایان سال: ۲×مبنای آخرین حقوق تا سقف ۳×حداقل مزد، به نسبت کارکرد سال (بدون بیمه)</summary>
    public async Task<HrPayRun> CalculateEydiAsync(HrPayRun run)
    {
        var rules = await GetPayRulesAsync();
        var minWage = await _hrtime.EffectiveMinWageAsync(run.Year);
        var brackets = await _db.HrPayTaxBrackets.AsNoTracking().Where(b => b.Year == run.Year).ToListAsync();
        var exempt = R(rules, "pay.tax.eydi.exemption", 0);
        var yStart = PersianDate.ToGregorian(run.Year, 1, 1);
        var yEnd = PersianDate.ToGregorian(run.Year + 1, 1, 1);
        // فقط پرسنلِ فعالِ جدول اختصاصی این ماژول که به کاربرِ فعالِ دارای دسترسی پیوند دارند
        var allowedEmp = await _hrtime.Access.PersonnelIdsAsync();
        var employees = (await _db.FaHrPersonnel.AsNoTracking().Where(e => e.Status == FaHrPersonnelStatus.Active).ToListAsync())
            .Where(e => allowedEmp.Contains(e.Id)).ToList();
        var monthlyRunIds = await _db.HrPayRuns.AsNoTracking()
            .Where(r => r.Year == run.Year && r.Kind == "Monthly").Select(r => r.Id).ToListAsync();
        _db.HrPaySlips.RemoveRange(_db.HrPaySlips.Where(x => x.RunId == run.Id));
        await _db.SaveChangesAsync();
        foreach (var emp in employees)
        {
            var from = emp.HireDate.Date > yStart ? emp.HireDate.Date : yStart;
            var daysIn = Math.Max(0, (yEnd - from).TotalDays);
            var factor = Math.Min(1, daysIn / 365.0);
            decimal? lastGross = null;
            if (monthlyRunIds.Count > 0)
                lastGross = await _db.HrPaySlips.AsNoTracking()
                    .Where(x => monthlyRunIds.Contains(x.RunId) && x.EmployeeId == emp.Id)
                    .OrderByDescending(x => x.RunId).Select(x => (decimal?)x.GrossEarnings).FirstOrDefaultAsync();
            var basis = lastGross ?? emp.BaseSalary;
            var full = Math.Min(2 * basis, 3 * minWage);
            var eydi = Math.Round(full * (decimal)factor);
            var tax = eydi <= 0 ? 0 : CalcTax(Math.Max(0, eydi - exempt), brackets);
            var uid = await _hrtime.UserIdOfEmployeeAsync(emp.Id);
            var lines = new List<SlipLine>
            {
                new("EYDI", "عیدی و پاداش پایان سال", "Earning", eydi, true, false),
            };
            _db.HrPaySlips.Add(new HrPaySlip
            {
                RunId = run.Id, EmployeeId = emp.Id, UserId = uid,
                EmployeeName = $"{emp.FirstName} {emp.LastName}".Trim(),
                DaysPaid = 30, DaysWorked = Math.Round(daysIn), BaseAmount = basis,
                GrossEarnings = eydi, TaxableAmount = eydi, InsuranceableAmount = 0,
                TaxAmount = tax, InsuranceAmount = 0, EmployerInsurance = 0,
                OtherDeductions = 0, NetPay = eydi - tax,
                DetailsJson = JsonSerializer.Serialize(lines),
            });
        }
        await _db.SaveChangesAsync();
        var slips = await _db.HrPaySlips.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync();
        run.SlipCount = slips.Count;
        run.TotalGross = slips.Sum(x => x.GrossEarnings);
        run.TotalDeductions = slips.Sum(x => x.TaxAmount);
        run.TotalNet = slips.Sum(x => x.NetPay);
        run.TotalTax = slips.Sum(x => x.TaxAmount);
        run.TotalInsuranceEmp = 0;
        run.TotalInsuranceEr = 0;
        if (run.Status == "Approved") run.Status = "Draft";
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("hr-pay");
        return run;
    }

    public async Task<HrPayRun> SetStatusAsync(int runId, string status)
    {
        var run = await _db.HrPayRuns.FirstOrDefaultAsync(r => r.Id == runId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        if (status == "Approved")
        {
            if (run.Status == "Locked") throw new InvalidOperationException("دوره قفل است.");
            run.Status = "Approved";
        }
        else if (status == "Locked")
        {
            if (run.Status == "Locked") return run;
            // اعمال اقساط/معوقه/علی‌الحساب فقط هنگام قفل نهایی
            var slips = await _db.HrPaySlips.AsNoTracking().Where(s => s.RunId == runId).ToListAsync();
            foreach (var s in slips)
            {
                List<SlipLine>? lines;
                try { lines = JsonSerializer.Deserialize<List<SlipLine>>(s.DetailsJson); } catch { continue; }
                if (lines == null) continue;
                foreach (var ln in lines.Where(l => l.Code == "SYS_LOAN" && l.RefId != null))
                {
                    var loan = await _db.HrPayLoans.FirstOrDefaultAsync(x => x.Id == ln.RefId);
                    if (loan == null || loan.Status != "Active") continue;
                    loan.PaidCount++;
                    if (loan.PaidCount >= loan.Installments) { loan.Status = "Paid"; loan.PaidCount = loan.Installments; }
                }
                foreach (var a in lines.Where(l => l.Code == "SYS_ARREAR" && l.RefId != null))
                {
                    var ar = await _db.HrPayArrears.FirstOrDefaultAsync(x => x.Id == a.RefId);
                    if (ar != null && ar.Status == "Pending") ar.Status = "Applied";
                }
                foreach (var o in lines.Where(l => l.Code == "SYS_ONACC" && l.RefId != null))
                {
                    var oa = await _db.HrPayOnAccounts.FirstOrDefaultAsync(x => x.Id == o.RefId);
                    if (oa != null && oa.Status == "Pending") oa.Status = "Applied";
                }
            }
            run.Status = "Locked";
            run.LockedAt = DateTime.Now;
        }
        else if (status == "Draft")
        {
            if (run.VoucherId != null) throw new InvalidOperationException("برای این دوره سند صادر شده؛ ابتدا سند را حذف کنید.");
            run.Status = "Draft";
            run.LockedAt = null;
        }
        else throw new InvalidOperationException("وضعیت نامعتبر.");
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("hr-pay");
        return run;
    }

    public record CompareRow(int EmployeeId, string Name, decimal PrevNet, decimal CurNet, decimal Diff, double Pct, bool Flag);

    public async Task<List<CompareRow>> CompareAsync(int runId, Paging.Request? pagination = null)
    {
        var run = await _db.HrPayRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        var prev = await _db.HrPayRuns.AsNoTracking()
            .Where(r => r.Id != runId && (r.Year < run.Year || (r.Year == run.Year && r.Month < run.Month)))
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).FirstOrDefaultAsync();
        var allowedCmp = (await _hrtime.Access.PersonnelIdsAsync()).ToList();
        var cur = await _db.HrPaySlips.AsNoTracking().Where(s => s.RunId == runId && allowedCmp.Contains(s.EmployeeId)).OrderBy(x => x.Id).ToPageListAsync(pagination);
        var rules = await GetPayRulesAsync();
        var thr = (double)R(rules, "pay.variance.pct", 10);
        if (prev == null)
            return cur.Select(s => new CompareRow(s.EmployeeId, s.EmployeeName, 0, s.NetPay, s.NetPay, 100, true)).ToList();
        var ps = await _db.HrPaySlips.AsNoTracking().Where(s => s.RunId == prev.Id && cur.Select(c => c.EmployeeId).Contains(s.EmployeeId)).ToDictionaryAsync(s => s.EmployeeId);
        return cur.Select(s =>
        {
            var pn = ps.TryGetValue(s.EmployeeId, out var p) ? p.NetPay : 0;
            var diff = s.NetPay - pn;
            var pct = pn == 0 ? (s.NetPay == 0 ? 0 : 100) : (double)(diff / pn * 100);
            return new CompareRow(s.EmployeeId, s.EmployeeName, pn, s.NetPay, diff, Math.Round(pct, 1), Math.Abs(pct) >= thr);
        }).ToList();
    }

    // ================= سند حسابداری =================

    public async Task<Inventory.Shared.Dtos.AccVoucher> IssueVoucherAsync(int runId, string byName)
    {
        var run = await _db.HrPayRuns.FirstOrDefaultAsync(r => r.Id == runId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        if (run.Status == "Draft") throw new InvalidOperationException("ابتدا دوره را تایید کنید.");
        if (run.VoucherId != null) throw new InvalidOperationException("برای این دوره قبلاً سند صادر شده است.");
        var rules = await GetPayRulesAsync();
        async Task<int> AccId(string key, string title)
        {
            var code = rules.TryGetValue(key, out var v) ? v.Trim() : "";
            if (string.IsNullOrEmpty(code)) throw new InvalidOperationException($"کد حساب «{title}» در قوانین تنظیم نشده ({key}).");
            var acc = await _db.AccAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code);
            if (acc == null) throw new InvalidOperationException($"حساب با کد {code} ({title}) پیدا نشد.");
            return acc.Id;
        }
        var slips = await _db.HrPaySlips.AsNoTracking().Where(s => s.RunId == runId).ToListAsync();
        if (slips.Count == 0) throw new InvalidOperationException("دوره فیشی ندارد؛ ابتدا محاسبه کنید.");
        var accrualCodes = (await _db.HrPayItems.AsNoTracking().Where(i => i.IsActive && i.IsAccrual).ToListAsync())
            .Select(i => i.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        decimal accrual = 0;
        foreach (var s in slips)
        {
            try
            {
                var lns = JsonSerializer.Deserialize<List<SlipLine>>(s.DetailsJson);
                accrual += lns?.Where(l => accrualCodes.Contains(l.Code)).Sum(l => l.Amount) ?? 0;
            }
            catch { }
        }
        var gross = slips.Sum(s => s.GrossEarnings);
        var erIns = slips.Sum(s => s.EmployerInsurance);
        var net = slips.Sum(s => s.NetPay);
        var tax = slips.Sum(s => s.TaxAmount);
        var empIns = slips.Sum(s => s.InsuranceAmount);
        var otherDed = slips.Sum(s => s.OtherDeductions);
        var expAcc = await AccId("pay.acc.salaryExpense", "هزینه حقوق");
        var insExpAcc = await AccId("pay.acc.insuranceExpense", "هزینه بیمه کارفرما");
        var payAcc = await AccId("pay.acc.payable", "حقوق پرداختنی");
        var taxAcc = await AccId("pay.acc.taxPayable", "مالیات پرداختنی");
        var insPayAcc = await AccId("pay.acc.insurancePayable", "بیمه پرداختنی");
        var lines = new List<Inventory.Shared.Dtos.AccVoucherLine>();
        var row = 0;
        void Add(int accId, decimal dr, decimal cr, string desc)
        {
            if (dr == 0 && cr == 0) return;
            lines.Add(new Inventory.Shared.Dtos.AccVoucherLine { RowNo = ++row, AccountId = accId, Debit = dr, Credit = cr, Description = desc });
        }
        Add(expAcc, gross, 0, $"هزینه حقوق {run.Year}/{run.Month}");
        Add(insExpAcc, erIns, 0, "هزینه بیمه سهم کارفرما و بیکاری");
        if (accrual > 0)
        {
            var accCode = rules.TryGetValue("pay.acc.accrualExpense", out var ac) ? ac.Trim() : "";
            Add(accCode.Length > 0 ? (await _db.AccAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == accCode))?.Id ?? expAcc : expAcc,
                accrual, 0, "هزینه ذخیره سنوات");
        }
        Add(payAcc, 0, net, "خالص پرداختنی به پرسنل");
        Add(taxAcc, 0, tax, "مالیات حقوق پرداختنی");
        Add(insPayAcc, 0, empIns + erIns, "حق بیمه پرداختنی");
        if (otherDed > 0) Add(await AccId("pay.acc.otherDed", "سایر کسورات"), 0, otherDed, "کسورات (وام/مساعده/علی‌الحساب)");
        if (accrual > 0)
        {
            var accCode = rules.TryGetValue("pay.acc.accrualPayable", out var ap) ? ap.Trim() : "";
            Add(accCode.Length > 0 ? (await _db.AccAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == accCode))?.Id ?? payAcc : payAcc,
                0, accrual, "ذخیره سنوات");
        }
        var (_, endEx) = JMonthRange(run.Year, run.Month);
        var v = await _acc.SaveVoucherAsync(new Inventory.Shared.Dtos.AccVoucher
        {
            FiscalYearId = 0,
            Date = endEx.AddDays(-1),
            Description = $"سند حقوق و دستمزد {run.Year}/{run.Month:D2} — {slips.Count} فیش",
            Source = VoucherSource.Payroll,
            SourceId = run.Id,
            SourceTitle = $"دوره {run.Year}/{run.Month}",
            Lines = lines,
        }, byName);
        run.VoucherId = v.Id;
        await _db.SaveChangesAsync();
        return v;
    }

    // ================= خروجی‌ها =================

    public record EmpRow(HrPaySlip Slip, string NationalCode, string InsuranceNo, string BankName, string Iban, string AccountNo);

    private async Task<List<EmpRow>> SlipRowsAsync(int runId)
    {
        var allowedEmp = await _hrtime.Access.PersonnelIdsAsync();
        var slips = (await _db.HrPaySlips.AsNoTracking().Where(s => s.RunId == runId).OrderBy(s => s.EmployeeName).ToListAsync())
            .Where(s => allowedEmp.Contains(s.EmployeeId)).ToList();
        var empIds = slips.Select(s => s.EmployeeId).ToList();
        var emps = await _db.FaHrPersonnel.AsNoTracking().Where(e => empIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id);
        var profs = await _db.HrPayProfiles.AsNoTracking().Where(p => empIds.Contains(p.EmployeeId)).ToDictionaryAsync(p => p.EmployeeId);
        return slips.Select(s =>
        {
            emps.TryGetValue(s.EmployeeId, out var e);
            profs.TryGetValue(s.EmployeeId, out var p);
            return new EmpRow(s, e?.NationalCode ?? "", p?.InsuranceNo ?? "", p?.BankName ?? "", p?.Iban ?? "", p?.AccountNo ?? "");
        }).ToList();
    }

    /// <summary>فایل بیمه (متنی | جداکننده) — شامل کد کارگاه، دوره، روزکرد، مشمول، سهم‌ها</summary>
    public async Task<(byte[] Bytes, string FileName, string ContentType)> InsuranceFileAsync(int runId, string format)
    {
        var run = await _db.HrPayRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        var rules = await GetPayRulesAsync();
        var rows = await SlipRowsAsync(runId);
        var erPct = R(rules, "pay.ins.employer", 20);
        var unPct = R(rules, "pay.ins.unemployment", 3);
        var wsCode = rules.TryGetValue("pay.workshop.code", out var w) ? w : "";
        var wsName = rules.TryGetValue("pay.workshop.name", out var wn) ? wn : "";
        var sb = new StringBuilder();
        sb.AppendLine($"# DISKET-BIMEH | Workshop={wsCode} {wsName} | Period={run.Year}/{run.Month:D2} | Count={rows.Count}");
        sb.AppendLine("RADIF|NATIONAL_CODE|INS_NO|FULL_NAME|WORK_DAYS|INS_BASE|EMP_SHARE|ER_SHARE|UNEMP_SHARE");
        var i = 0;
        foreach (var r in rows)
        {
            i++;
            var b = r.Slip.InsuranceableAmount;
            sb.AppendLine(string.Join("|", i, r.NationalCode, r.InsuranceNo, r.Slip.EmployeeName,
                r.Slip.DaysPaid.ToString("0.##", CultureInfo.InvariantCulture),
                b.ToString("0", CultureInfo.InvariantCulture),
                r.Slip.InsuranceAmount.ToString("0", CultureInfo.InvariantCulture),
                Math.Round(b * erPct / 100).ToString("0", CultureInfo.InvariantCulture),
                Math.Round(b * unPct / 100).ToString("0", CultureInfo.InvariantCulture)));
        }
        var name = $"Bimeh_{run.Year}_{run.Month:D2}.{(format == "csv" ? "csv" : "txt")}";
        var ct = format == "csv" ? "text/csv" : "text/plain";
        return (Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), name, ct);
    }

    /// <summary>فایل مالیات حقوق (متنی | جداکننده)</summary>
    public async Task<(byte[] Bytes, string FileName, string ContentType)> TaxFileAsync(int runId, string format)
    {
        var run = await _db.HrPayRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        var rows = await SlipRowsAsync(runId);
        var sb = new StringBuilder();
        sb.AppendLine($"# TAX-HOGHOOGH | Period={run.Year}/{run.Month:D2} | Count={rows.Count}");
        sb.AppendLine("RADIF|NATIONAL_CODE|FULL_NAME|GROSS|TAXABLE|TAX");
        var i = 0;
        foreach (var r in rows)
        {
            i++;
            sb.AppendLine(string.Join("|", i, r.NationalCode, r.Slip.EmployeeName,
                r.Slip.GrossEarnings.ToString("0", CultureInfo.InvariantCulture),
                r.Slip.TaxableAmount.ToString("0", CultureInfo.InvariantCulture),
                r.Slip.TaxAmount.ToString("0", CultureInfo.InvariantCulture)));
        }
        var name = $"Tax_{run.Year}_{run.Month:D2}.{(format == "csv" ? "csv" : "txt")}";
        var ct = format == "csv" ? "text/csv" : "text/plain";
        return (Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), name, ct);
    }

    /// <summary>لیست پرداخت بانکی (Excel | CSV)</summary>
    public async Task<(byte[] Bytes, string FileName, string ContentType)> BankFileAsync(int runId, string format)
    {
        var run = await _db.HrPayRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        var rows = await SlipRowsAsync(runId);
        var name = $"Bank_{run.Year}_{run.Month:D2}.{(format == "xlsx" ? "xlsx" : "csv")}";
        if (format == "xlsx")
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Payroll");
            ws.Cell(1, 1).Value = "ردیف"; ws.Cell(1, 2).Value = "نام";
            ws.Cell(1, 3).Value = "کد ملی"; ws.Cell(1, 4).Value = "بانک";
            ws.Cell(1, 5).Value = "شبا"; ws.Cell(1, 6).Value = "شماره حساب";
            ws.Cell(1, 7).Value = "مبلغ (ریال)"; ws.Cell(1, 8).Value = "شرح";
            var r = 2;
            foreach (var x in rows)
            {
                ws.Cell(r, 1).Value = r - 1;
                ws.Cell(r, 2).Value = x.Slip.EmployeeName;
                ws.Cell(r, 3).Value = x.NationalCode;
                ws.Cell(r, 4).Value = x.BankName;
                ws.Cell(r, 5).Value = x.Iban;
                ws.Cell(r, 6).Value = x.AccountNo;
                ws.Cell(r, 7).Value = x.Slip.NetPay;
                ws.Cell(r, 8).Value = $"حقوق {run.Year}/{run.Month:D2}";
                r++;
            }
            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return (ms.ToArray(), name, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
        var sb = new StringBuilder();
        sb.AppendLine("ردیف,نام,کد ملی,بانک,شبا,شماره حساب,مبلغ (ریال),شرح");
        var i = 0;
        foreach (var x in rows)
        {
            i++;
            sb.AppendLine($"{i},{x.Slip.EmployeeName},{x.NationalCode},{x.BankName},{x.Iban},{x.AccountNo},{x.Slip.NetPay:0},حقوق {run.Year}/{run.Month:D2}");
        }
        return (Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), name, "text/csv");
    }

    // ================= تسویه =================

    public async Task<HrPaySettlement> CalculateSettlementAsync(int employeeId, DateTime leaveDate, double? unusedOverride)
    {
        await _hrtime.EnsureAllowedPersonnelAsync(employeeId);
        var emp = await _db.FaHrPersonnel.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId)
            ?? throw new InvalidOperationException("پرسنل پیدا نشد.");
        var rules = await GetPayRulesAsync();
        var minWage = R(rules, "pay.min_wage", 103909270);
        var years = Math.Max(0, Math.Round((leaveDate.Date - emp.HireDate.Date).TotalDays / 365, 2));
        double unused;
        var linkUid = await _hrtime.UserIdOfEmployeeAsync(emp.Id);
        if (unusedOverride != null) unused = Math.Max(0, unusedOverride.Value);
        else if (linkUid != null)
        {
            var jy = new PersianCalendar().GetYear(leaveDate);
            var bals = await _hrtime.GetBalancesAsync(linkUid.Value, jy);
            unused = Math.Max(0, bals.FirstOrDefault(b => b.Category == "Annual")?.Remaining ?? 0);
        }
        else unused = 0;
        // عیدی نسبی: ماه‌های سپری‌شده سال خروج / ۱۲ × حداقل(۲×پایه، ۳×حداقل مزد)
        var jy2 = new PersianCalendar().GetYear(leaveDate);
        var far1 = PersianDate.ToGregorian(jy2, 1, 1);
        var months = Math.Min(12, Math.Max(0, (leaveDate.Date - far1).TotalDays / 365 * 12));
        var eydiFull = Math.Min(2 * emp.BaseSalary, 3 * minWage);
        var eydi = Math.Round(eydiFull * (decimal)(months / 12));
        var severance = Math.Round((decimal)years * emp.BaseSalary);
        var refund = Math.Round((decimal)unused * emp.BaseSalary / 30);
        var s = new HrPaySettlement
        {
            EmployeeId = employeeId, LeaveDate = leaveDate.Date, YearsOfService = years,
            LastBase = emp.BaseSalary, UnusedLeaveDays = Math.Round(unused, 2),
            SeveranceAmount = severance, LeaveRefund = refund, EydiProrata = eydi,
            TotalAmount = severance + refund + eydi, Status = "Draft",
        };
        _db.HrPaySettlements.Add(s);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("hr-pay");
        return s;
    }

    // ================= فاز ۳: بایگانی ارسال لیست‌های قانونی =================

    public record PreflightResult(List<string> Errors, List<string> Warnings);
    public record VerifyResult(bool Match, string CurrentHash, List<string> Diffs);

    public static string NormFilingKind(string kind)
        => kind is "Insurance" or "Tax" or "Bank" ? kind : throw new InvalidOperationException("نوع لیست نامعتبر است.");

    public async Task<PreflightResult> PreflightAsync(int runId, string kind)
    {
        kind = NormFilingKind(kind);
        var run = await _db.HrPayRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId)
            ?? throw new InvalidOperationException("دوره پیدا نشد.");
        var errors = new List<string>(); var warnings = new List<string>();
        var slips = await _db.HrPaySlips.AsNoTracking().Where(s => s.RunId == runId).ToListAsync();
        if (slips.Count == 0) { errors.Add("دوره محاسبه نشده است (فیشی ندارد)."); return new(errors, warnings); }
        if (run.Status == "Draft") errors.Add("دوره هنوز تأیید نشده است؛ ابتدا تأیید کنید.");
        var rows = await SlipRowsAsync(runId);
        var noNat = rows.Where(r => string.IsNullOrWhiteSpace(r.NationalCode)).Select(r => r.Slip.EmployeeName).ToList();
        if (noNat.Count > 0) errors.Add($"کد ملی ثبت نشده: {string.Join("، ", noNat.Take(5))}" + (noNat.Count > 5 ? " و…" : ""));
        if (kind == "Insurance")
        {
            var noIns = rows.Where(r => string.IsNullOrWhiteSpace(r.InsuranceNo)).Select(r => r.Slip.EmployeeName).ToList();
            if (noIns.Count > 0) errors.Add($"شماره بیمه ثبت نشده: {string.Join("، ", noIns.Take(5))}" + (noIns.Count > 5 ? " و…" : ""));
        }
        if (kind == "Bank")
        {
            var noIban = rows.Where(r => string.IsNullOrWhiteSpace(r.Iban)).Select(r => r.Slip.EmployeeName).ToList();
            if (noIban.Count > 0) errors.Add($"شبا ثبت نشده: {string.Join("، ", noIban.Take(5))}" + (noIban.Count > 5 ? " و…" : ""));
        }
        var badNet = slips.Where(s => s.NetPay <= 0).Select(s => s.EmployeeName).ToList();
        if (badNet.Count > 0) errors.Add($"خالص نامعتبر (صفر/منفی): {string.Join("، ", badNet.Take(5))}");
        if (run.Status != "Locked") warnings.Add("دوره قفل نهایی نشده است.");
        var (start, end) = JMonthRange(run.Year, run.Month);
        var pendOt = await _db.HrOtApprovals.AsNoTracking()
            .CountAsync(x => x.WorkDate >= start && x.WorkDate < end && x.Status == "Pending");
        if (pendOt > 0) warnings.Add($"{pendOt} ردیف اضافه‌کار معلق در این ماه وجود دارد.");
        var closed = await _db.HrAttendCloses.AsNoTracking()
            .AnyAsync(c => c.Year == run.Year && c.Month == run.Month && c.IsClosed);
        if (!closed) warnings.Add("ماه کارکرد بسته نشده است.");
        List<CompareRow> cmp;
        try { cmp = await CompareAsync(runId); } catch { cmp = new(); }
        var flagged = cmp.Where(c => c.Flag).ToList();
        if (flagged.Count > 0) warnings.Add($"{flagged.Count} نفر مغایرت بالای آستانه با دوره قبل دارند (مثلاً {flagged[0].Name}).");
        return new(errors, warnings);
    }

    public async Task<HrPayFiling> FileAsync(int runId, string kind, string byName, string? receiptNo, string? note)
    {
        kind = NormFilingKind(kind);
        var pre = await PreflightAsync(runId, kind);
        if (pre.Errors.Count > 0) throw new InvalidOperationException("ارسال ممکن نیست: " + string.Join(" | ", pre.Errors));
        // بانک با فرمت متنی ثبت می‌شود تا هش بازتولید پایدار بماند (خروجی xlsx قطعی نیست)
        var (bytes, name, _) = kind switch
        {
            "Tax" => await TaxFileAsync(runId, "txt"),
            "Bank" => await BankFileAsync(runId, "csv"),
            _ => await InsuranceFileAsync(runId, "txt"),
        };
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var slips = await _db.HrPaySlips.AsNoTracking().Where(s => s.RunId == runId).ToListAsync();
        var f = await _db.HrPayFilings.FirstOrDefaultAsync(x => x.RunId == runId && x.Kind == kind);
        if (f == null) { f = new HrPayFiling { RunId = runId, Kind = kind }; _db.HrPayFilings.Add(f); }
        else f.Version++;
        f.FileName = name; f.Sha256 = hash;
        f.SlipCount = slips.Count;
        f.TotalGross = slips.Sum(s => s.GrossEarnings);
        f.TotalTax = slips.Sum(s => s.TaxAmount);
        f.TotalInsurance = slips.Sum(s => s.InsuranceAmount + s.EmployerInsurance);
        f.TotalNet = slips.Sum(s => s.NetPay);
        f.FiledBy = byName; f.FiledAt = DateTime.Now;
        f.ReceiptNo = receiptNo; f.Note = note;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("hr-pay");
        return f;
    }

    public async Task<VerifyResult> VerifyFilingAsync(int runId, string kind)
    {
        kind = NormFilingKind(kind);
        var f = await _db.HrPayFilings.AsNoTracking().FirstOrDefaultAsync(x => x.RunId == runId && x.Kind == kind)
            ?? throw new InvalidOperationException("برای این دوره ارسالی ثبت نشده است.");
        var (bytes, _, _) = kind switch
        {
            "Tax" => await TaxFileAsync(runId, "txt"),
            "Bank" => await BankFileAsync(runId, "csv"),
            _ => await InsuranceFileAsync(runId, "txt"),
        };
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var diffs = new List<string>();
        if (!hash.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
            diffs.Add("فایل بازتولیدشده با فایل ارسال‌شده فرق دارد.");
        var slips = await _db.HrPaySlips.AsNoTracking().Where(s => s.RunId == runId).ToListAsync();
        if (slips.Count != f.SlipCount) diffs.Add($"تعداد فیش: {f.SlipCount} ← {slips.Count}");
        if (slips.Sum(s => s.GrossEarnings) != f.TotalGross) diffs.Add("جمع ناخالص تغییر کرده.");
        if (slips.Sum(s => s.TaxAmount) != f.TotalTax) diffs.Add("جمع مالیات تغییر کرده.");
        if (slips.Sum(s => s.InsuranceAmount + s.EmployerInsurance) != f.TotalInsurance) diffs.Add("جمع بیمه تغییر کرده.");
        if (slips.Sum(s => s.NetPay) != f.TotalNet) diffs.Add("جمع خالص تغییر کرده.");
        return new(diffs.Count == 0, hash, diffs);
    }

    public async Task<List<HrPayFiling>> FilingsAsync(int? runId, Paging.Request? pagination = null)
    {
        var q = _db.HrPayFilings.AsNoTracking().AsQueryable();
        if (runId != null) q = q.Where(x => x.RunId == runId);
        return await q.OrderByDescending(x => x.FiledAt).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 500);
    }
}
