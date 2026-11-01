using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services.FaAtt;
using Inventory.Api.Services.FaCom;
using Inventory.Api.Services.Office.Email;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Inventory.Api.Services.FaPay;

/// <summary>
/// ================== حقوق و دستمزد فروغ آریا (FaPay) — §۸، ماژول مستقل ==================
/// ورودی: خلاصه ماهانه FaAtt + حقوق پایه HrEmployee + پاداش عملکرد قطعی + ثبت‌های دستی ماه.
/// محاسبه: اضافه‌کاری/تأخیر/غیبت/بدون‌حقوق، بیمه سهم کارمند، مالیات پلکانی، بهای واحد.
/// خروجی: فیش PDF فارسی (QuestPDF + وزیرمتن)، ارسال ایمیل فیش، گزارش بیمه/مالیات/بهای تمام‌شده.
/// سیستم قدیمی (HrPay/RadisHr/payroll) دست نمی‌خورد.
/// </summary>
public interface IFaPayService
{
    Task<FaPaySettingsDto> GetSettingsAsync();
    Task<FaPaySettingsDto> SaveSettingsAsync(FaPaySettingsSaveDto dto);

    Task<List<FaPayTaxBracketDto>> ListBracketsAsync(Paging.Request? pagination = null);
    Task<FaPayTaxBracketDto> SaveBracketAsync(int? id, FaPayTaxBracketSaveDto dto);
    Task DeleteBracketAsync(int id);

    Task<List<FaPayItemTypeDto>> ListItemTypesAsync(Paging.Request? pagination = null);
    Task<FaPayItemTypeDto> SaveItemTypeAsync(int? id, FaPayItemTypeSaveDto dto);
    Task DeleteItemTypeAsync(int id);

    Task<List<FaPayAdjustmentDto>> ListAdjustmentsAsync(int? year, int? month, int? employeeId, Paging.Request? pagination = null);
    Task<FaPayAdjustmentDto> SaveAdjustmentAsync(int? id, FaPayAdjustmentSaveDto dto, int byUserId, string byName);
    Task DeleteAdjustmentAsync(int id);

    Task<List<FaPayRunDto>> ListRunsAsync(Paging.Request? pagination = null);
    Task<FaPayRunDto?> GetRunAsync(int id);
    Task<FaPayRunDto> CreateRunAsync(FaPayRunSaveDto dto, int byUserId, string byName);
    Task<int> CalculateRunAsync(int id);
    Task<FaPayRunDto> FinalizeRunAsync(int id);
    Task<FaPayRunDto> ReopenRunAsync(int id);
    Task DeleteRunAsync(int id);

    Task<List<FaPaySlipDto>> RunSlipsAsync(int runId, Paging.Request? pagination = null);
    Task<FaPayBankCheckDto> BankCheckAsync(int runId);
    Task<(byte[] Data, string FileName, string ContentType)> BankFileAsync(int runId, string format);
    Task<FaPaySlipDto?> GetSlipAsync(int id);
    Task<List<FaPaySlipDto>> MySlipsAsync(int userId, Paging.Request? pagination = null);
    Task<FaPaySlipDto?> GetMySlipAsync(int id, int userId);
    Task SetPaidAsync(int id, bool paid);
    Task<byte[]> SlipPdfAsync(int id);
    Task<byte[]> SlipsPdfAsync(int runId);
    Task<int> SendSlipEmailAsync(int id, int byUserId);
    Task<FaPayMailResultDto> SendRunEmailsAsync(int runId, int byUserId);

    Task<FaPayInsuranceReportDto> InsuranceReportAsync(int year, int month);
    Task<FaPayTaxReportDto> TaxReportAsync(int year, int month);
    Task<FaPayUnitCostReportDto> UnitCostReportAsync(int year, int month);
}

public class FaPayService : IFaPayService
{
    private readonly AppDbContext _db;
    private readonly IFaAttService _att;
    private readonly IEmailService _email;
    private readonly INotifyService _notify;
    private readonly ISmsSender _sms;
    private static readonly PersianCalendar Pc = new();

    private static bool _fontsOk;
    private static readonly object FontLock = new();

    static FaPayService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public FaPayService(AppDbContext db, IFaAttService att, IEmailService email, INotifyService notify, ISmsSender sms)
    {
        _db = db; _att = att; _email = email; _notify = notify; _sms = sms;
    }

    private static void JyCheck(int jy, int jm)
    {
        if (jy is < 1300 or > 1500 || jm is < 1 or > 12)
            throw new InvalidOperationException("سال/ماه شمسی نامعتبر است.");
    }

    private static (DateTime From, DateTime To) JMonthRange(int jy, int jm)
    {
        JyCheck(jy, jm);
        var from = Pc.ToDateTime(jy, jm, 1, 0, 0, 0, 0);
        return (from, from.AddDays(Pc.GetDaysInMonth(jy, jm) - 1));
    }

    private static string JmName(int m) => m switch
    {
        1 => "فروردین", 2 => "اردیبهشت", 3 => "خرداد", 4 => "تیر", 5 => "مرداد", 6 => "شهریور",
        7 => "مهر", 8 => "آبان", 9 => "آذر", 10 => "دی", 11 => "بهمن", 12 => "اسفند", _ => ""
    };

    private static double R(double v) => Math.Round(v, MidpointRounding.AwayFromZero);

    // ==================== تنظیمات ====================

    public async Task<FaPaySettingsDto> GetSettingsAsync()
        => ToDto(await _db.FaPaySettings.FirstOrDefaultAsync() ?? new FaPaySettings());

    public async Task<FaPaySettingsDto> SaveSettingsAsync(FaPaySettingsSaveDto dto)
    {
        if (dto.DaysPerMonth <= 0 || dto.HoursPerDay <= 0)
            throw new InvalidOperationException("مخرج روزانه/ساعتی باید بزرگ‌تر از صفر باشد.");
        if (dto.OvertimeFactor < 0 || dto.DelayFactor < 0)
            throw new InvalidOperationException("ضریب‌ها نباید منفی باشند.");
        if (dto.InsuranceEmployeeRate is < 0 or > 100 || dto.InsuranceEmployerRate is < 0 or > 100)
            throw new InvalidOperationException("نرخ بیمه باید بین ۰ تا ۱۰۰ باشد.");
        if (dto.TaxFreeMonthly < 0) throw new InvalidOperationException("معافیت مالیاتی نباید منفی باشد.");
        var s = await _db.FaPaySettings.FirstOrDefaultAsync();
        if (s == null) { s = new FaPaySettings(); _db.FaPaySettings.Add(s); }
        s.DaysPerMonth = dto.DaysPerMonth; s.HoursPerDay = dto.HoursPerDay;
        s.OvertimeFactor = dto.OvertimeFactor; s.DelayFactor = dto.DelayFactor;
        s.InsuranceEmployeeRate = dto.InsuranceEmployeeRate; s.InsuranceEmployerRate = dto.InsuranceEmployerRate;
        s.TaxFreeMonthly = dto.TaxFreeMonthly;
        s.AbsentDeductEnabled = dto.AbsentDeductEnabled; s.UnpaidLeaveDeductEnabled = dto.UnpaidLeaveDeductEnabled;
        s.Note = dto.Note;
        await _db.SaveChangesAsync();
        return ToDto(s);
    }

    private static FaPaySettingsDto ToDto(FaPaySettings s) => new()
    {
        DaysPerMonth = s.DaysPerMonth, HoursPerDay = s.HoursPerDay,
        OvertimeFactor = s.OvertimeFactor, DelayFactor = s.DelayFactor,
        InsuranceEmployeeRate = s.InsuranceEmployeeRate, InsuranceEmployerRate = s.InsuranceEmployerRate,
        TaxFreeMonthly = s.TaxFreeMonthly, AbsentDeductEnabled = s.AbsentDeductEnabled,
        UnpaidLeaveDeductEnabled = s.UnpaidLeaveDeductEnabled, Note = s.Note
    };

    // ==================== پلکان مالیاتی ====================

    public async Task<List<FaPayTaxBracketDto>> ListBracketsAsync(Paging.Request? pagination = null)
        => await _db.FaPayTaxBrackets.AsNoTracking().OrderBy(b => b.SortOrder).ThenBy(b => b.FromAmount).ThenBy(x => x.Id)
            .Select(b => new FaPayTaxBracketDto
            {
                Id = b.Id, FromAmount = b.FromAmount, ToAmount = b.ToAmount,
                Rate = b.Rate, SortOrder = b.SortOrder, IsActive = b.IsActive
            }).ToPageListAsync(pagination);

    public async Task<FaPayTaxBracketDto> SaveBracketAsync(int? id, FaPayTaxBracketSaveDto dto)
    {
        if (dto.FromAmount < 0) throw new InvalidOperationException("کف پلکان نباید منفی باشد.");
        if (dto.ToAmount != null && dto.ToAmount <= dto.FromAmount)
            throw new InvalidOperationException("سقف پلکان باید بزرگ‌تر از کف باشد.");
        if (dto.Rate is < 0 or > 100) throw new InvalidOperationException("نرخ باید بین ۰ تا ۱۰۰ باشد.");
        FaPayTaxBracket b;
        if (id == null) { b = new FaPayTaxBracket(); _db.FaPayTaxBrackets.Add(b); }
        else b = await _db.FaPayTaxBrackets.FindAsync(id.Value)
            ?? throw new InvalidOperationException("پلکان یافت نشد.");
        b.FromAmount = dto.FromAmount; b.ToAmount = dto.ToAmount; b.Rate = dto.Rate;
        b.SortOrder = dto.SortOrder; b.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return (await ListBracketsAsync()).First(x => x.Id == b.Id);
    }

    public async Task DeleteBracketAsync(int id)
    {
        var b = await _db.FaPayTaxBrackets.FindAsync(id)
            ?? throw new InvalidOperationException("پلکان یافت نشد.");
        _db.FaPayTaxBrackets.Remove(b);
        await _db.SaveChangesAsync();
    }

    // ==================== اقلام حقوقی ====================

    public async Task<List<FaPayItemTypeDto>> ListItemTypesAsync(Paging.Request? pagination = null)
        => await _db.FaPayItemTypes.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(x => x.Id)
            .Select(t => new FaPayItemTypeDto
            {
                Id = t.Id, Code = t.Code, Name = t.Name, Kind = (int)t.Kind,
                IsFixed = t.IsFixed, DefaultAmount = t.DefaultAmount,
                SortOrder = t.SortOrder, IsActive = t.IsActive
            }).ToPageListAsync(pagination);

    public async Task<FaPayItemTypeDto> SaveItemTypeAsync(int? id, FaPayItemTypeSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new InvalidOperationException("کد قلم الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("نام قلم الزامی است.");
        var code = dto.Code.Trim();
        if (await _db.FaPayItemTypes.AnyAsync(t => t.Code == code && t.Id != (id ?? 0)))
            throw new InvalidOperationException("کد قلم تکراری است.");
        FaPayItemType t;
        if (id == null) { t = new FaPayItemType(); _db.FaPayItemTypes.Add(t); }
        else t = await _db.FaPayItemTypes.FindAsync(id.Value)
            ?? throw new InvalidOperationException("قلم یافت نشد.");
        t.Code = code; t.Name = dto.Name.Trim(); t.Kind = (FaPayItemKind)dto.Kind;
        t.IsFixed = dto.IsFixed; t.DefaultAmount = dto.DefaultAmount;
        t.SortOrder = dto.SortOrder; t.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return (await ListItemTypesAsync()).First(x => x.Id == t.Id);
    }

    public async Task DeleteItemTypeAsync(int id)
    {
        var t = await _db.FaPayItemTypes.FindAsync(id)
            ?? throw new InvalidOperationException("قلم یافت نشد.");
        if (await _db.FaPayAdjustments.AnyAsync(a => a.ItemTypeId == id))
            throw new InvalidOperationException("این قلم در ثبت‌های ماهانه استفاده شده؛ ابتدا غیرفعالش کنید.");
        if (await _db.FaPaySlipItems.AnyAsync(i => i.ItemTypeId == id))
            throw new InvalidOperationException("این قلم در فیش‌های صادرشده استفاده شده؛ ابتدا غیرفعالش کنید.");
        _db.FaPayItemTypes.Remove(t);
        await _db.SaveChangesAsync();
    }

    // ==================== ثبت‌های ماهانه ====================

    public async Task<List<FaPayAdjustmentDto>> ListAdjustmentsAsync(int? year, int? month, int? employeeId, Paging.Request? pagination = null)
    {
        var q = _db.FaPayAdjustments.AsNoTracking().AsQueryable();
        if (year != null) q = q.Where(a => a.Year == year.Value);
        if (month != null) q = q.Where(a => a.Month == month.Value);
        if (employeeId != null) q = q.Where(a => a.EmployeeId == employeeId.Value);
        var rows = await q.OrderByDescending(a => a.Id).ToPageListAsync(pagination, defaultCap: 2000);
        var names = await _db.HrEmployees.AsNoTracking()
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName);
        var types = await _db.FaPayItemTypes.AsNoTracking().ToDictionaryAsync(t => t.Id);
        return rows.Select(a => new FaPayAdjustmentDto
        {
            Id = a.Id, EmployeeId = a.EmployeeId,
            EmployeeName = names.TryGetValue(a.EmployeeId, out var n) ? n : null,
            Year = a.Year, Month = a.Month, ItemTypeId = a.ItemTypeId,
            ItemTypeName = types.TryGetValue(a.ItemTypeId, out var t) ? t.Name : null,
            Kind = types.TryGetValue(a.ItemTypeId, out var t2) ? (int)t2.Kind : 0,
            Amount = a.Amount, Note = a.Note
        }).ToList();
    }

    private async Task GuardRunOpenAsync(int year, int month)
    {
        if (await _db.FaPayRuns.AnyAsync(r => r.Year == year && r.Month == month
            && r.Kind == FaPayRunKind.Monthly && r.Status == FaPayRunStatus.Final))
            throw new InvalidOperationException("دوره این ماه نهایی شده و قابل تغییر نیست (ابتدا بازگشایی کنید).");
    }

    public async Task<FaPayAdjustmentDto> SaveAdjustmentAsync(int? id, FaPayAdjustmentSaveDto dto, int byUserId, string byName)
    {
        JyCheck(dto.Year, dto.Month);
        await GuardRunOpenAsync(dto.Year, dto.Month);
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId && e.IsActive))
            throw new InvalidOperationException("پرسنل فعال یافت نشد.");
        if (!await _db.FaPayItemTypes.AnyAsync(t => t.Id == dto.ItemTypeId && t.IsActive))
            throw new InvalidOperationException("قلم حقوقی فعال یافت نشد.");
        FaPayAdjustment a;
        if (id == null)
        {
            a = new FaPayAdjustment { CreatedByUserId = byUserId, CreatedByName = byName, CreatedAt = DateTime.Now };
            _db.FaPayAdjustments.Add(a);
        }
        else a = await _db.FaPayAdjustments.FindAsync(id.Value)
            ?? throw new InvalidOperationException("ثبت یافت نشد.");
        a.EmployeeId = dto.EmployeeId; a.Year = dto.Year; a.Month = dto.Month;
        a.ItemTypeId = dto.ItemTypeId; a.Amount = dto.Amount; a.Note = dto.Note;
        await _db.SaveChangesAsync();
        return (await ListAdjustmentsAsync(null, null, null)).First(x => x.Id == a.Id);
    }

    public async Task DeleteAdjustmentAsync(int id)
    {
        var a = await _db.FaPayAdjustments.FindAsync(id)
            ?? throw new InvalidOperationException("ثبت یافت نشد.");
        await GuardRunOpenAsync(a.Year, a.Month);
        _db.FaPayAdjustments.Remove(a);
        await _db.SaveChangesAsync();
    }

    // ==================== دوره‌ها ====================

    public async Task<List<FaPayRunDto>> ListRunsAsync(Paging.Request? pagination = null)
    {
        var runs = await _db.FaPayRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var ids = runs.Select(r => r.Id).ToList();
        var slips = ids.Count == 0 ? new List<FaPaySlip>()
            : await _db.FaPaySlips.AsNoTracking().Where(s => ids.Contains(s.RunId)).ToListAsync();
        return runs.Select(r => new FaPayRunDto
        {
            Id = r.Id, Year = r.Year, Month = r.Month, Status = (int)r.Status, Kind = (int)r.Kind,
            SlipsCount = slips.Count(s => s.RunId == r.Id),
            TotalNet = slips.Where(s => s.RunId == r.Id).Sum(s => s.NetPay),
            Note = r.Note, CreatedByName = r.CreatedByName, CreatedAt = r.CreatedAt, FinalizedAt = r.FinalizedAt
        }).ToList();
    }

    public async Task<FaPayRunDto?> GetRunAsync(int id)
        => (await ListRunsAsync()).FirstOrDefault(r => r.Id == id);

    public async Task<FaPayRunDto> CreateRunAsync(FaPayRunSaveDto dto, int byUserId, string byName)
    {
        JyCheck(dto.Year, dto.Month);
        if (dto.Kind is < 0 or > 1) throw new InvalidOperationException("نوع دوره نامعتبر است.");
        if (dto.Kind == 1 && dto.Month != 12)
            throw new InvalidOperationException("دوره عیدی و سنوات فقط برای اسفند ساخته می‌شود.");
        if (await _db.FaPayRuns.AnyAsync(r => r.Year == dto.Year && r.Month == dto.Month && (int)r.Kind == dto.Kind))
            throw new InvalidOperationException("برای این ماه قبلاً دوره‌ای از همین نوع ساخته شده است.");
        var r = new FaPayRun
        {
            Year = dto.Year, Month = dto.Month, Status = FaPayRunStatus.Draft, Kind = (FaPayRunKind)dto.Kind,
            Note = dto.Note, CreatedByUserId = byUserId, CreatedByName = byName, CreatedAt = DateTime.Now
        };
        _db.FaPayRuns.Add(r);
        await _db.SaveChangesAsync();
        return (await GetRunAsync(r.Id))!;
    }

    /// <summary>موتور محاسبه: بازتولید کامل فیش‌های دوره پیش‌نویس.</summary>
    public async Task<int> CalculateRunAsync(int id)
    {
        var r = await _db.FaPayRuns.FindAsync(id)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (r.Status != FaPayRunStatus.Draft)
            throw new InvalidOperationException("فقط دوره پیش‌نویس قابل محاسبه مجدد است.");
        if (r.Kind != FaPayRunKind.Monthly)
            throw new InvalidOperationException("دوره پایان‌سال (عیدی و سنوات) با دکمه مخصوص خودش محاسبه می‌شود.");
        var s = await _db.FaPaySettings.FirstOrDefaultAsync() ?? new FaPaySettings();
        var xs = await _db.FaPayExtraSettings.FirstOrDefaultAsync() ?? new FaPayExtraSettings();
        var (mFrom, mTo) = JMonthRange(r.Year, r.Month);
        var monthDays = (mTo - mFrom).Days + 1;

        var emps = await _db.HrEmployees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Code).ToListAsync();
        if (emps.Count == 0) throw new InvalidOperationException("پرسنل فعالی یافت نشد.");
        var att = (await _att.MonthSummaryAsync(r.Year, r.Month, null)).ToDictionary(x => x.EmployeeId);

        // مرخصی بدون‌حقوق تأییدشده در بازه ماه (معادل روزانه، سقف روزهای ماه)
        var unpaidTypeId = await _db.FaAttLeaveTypes.AsNoTracking()
            .Where(t => t.Name == "بدون حقوق").Select(t => t.Id).FirstOrDefaultAsync();
        var unpaid = new Dictionary<int, double>();
        if (unpaidTypeId != 0)
        {
            var leaves = await _db.FaAttLeaves.AsNoTracking()
                .Where(l => l.LeaveTypeId == unpaidTypeId && l.Status == FaAttRequestStatus.Approved
                    && l.FromDate.Date <= mTo && l.ToDate.Date >= mFrom).ToListAsync();
            foreach (var l in leaves)
            {
                var ov = (new[] { mTo, l.ToDate.Date }.Min() - new[] { mFrom, l.FromDate.Date }.Max()).Days + 1;
                if (ov <= 0) continue;
                var equiv = l.HoursPerDay != null ? l.HoursPerDay.Value * ov / s.HoursPerDay : ov;
                unpaid[l.EmployeeId] = unpaid.TryGetValue(l.EmployeeId, out var d) ? d + equiv : equiv;
            }
        }

        // پاداش عملکرد: پیش‌تر از HrPerfResults («منابع انسانی فروغ آریا») خوانده می‌شد. برای تفکیک کامل
        // دو ماژول این وابستگی حذف شد؛ پاداش در این ماژول از «ثبت‌های ماهانه» (FaPayAdjustments) ثبت می‌شود.

        var fixedTypes = await _db.FaPayItemTypes.AsNoTracking()
            .Where(t => t.IsActive && t.IsFixed).OrderBy(t => t.SortOrder).ToListAsync();
        var adjs = await _db.FaPayAdjustments.AsNoTracking()
            .Where(a => a.Year == r.Year && a.Month == r.Month).ToListAsync();
        var adjTypes = await _db.FaPayItemTypes.AsNoTracking().ToDictionaryAsync(t => t.Id);
        var brackets = await _db.FaPayTaxBrackets.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.FromAmount).ToListAsync();

        // §۲ — اقساط وام سررسید این ماه (وام‌های فعال) + معوقات هدف این ماه
        var activeLoans = await _db.FaPayLoans.AsNoTracking()
            .Where(l => l.Status == FaPayLoanStatus.Active).ToListAsync();
        var activeLoanIds = activeLoans.Select(l => l.Id).ToList();
        var loanInfo = activeLoans.ToDictionary(l => l.Id, l => (l.Title, l.InstallmentCount, l.EmployeeId));
        var dueInsts = activeLoanIds.Count == 0 ? new List<FaPayLoanInstallment>()
            : await _db.FaPayLoanInstallments
                .Where(i => i.Year == r.Year && i.Month == r.Month && !i.IsPaid
                    && activeLoanIds.Contains(i.LoanId))
                .ToListAsync();
        var dueArrears = await _db.FaPayArrears
            .Where(x => x.TargetYear == r.Year && x.TargetMonth == r.Month
                && x.Status == FaPayArrearStatus.Draft)
            .ToListAsync();

        // §۲ — درصد نوبت‌کاری از شیفت جاری هر پرسنل در این ماه
        var monthAssigns = await _db.FaAttShiftAssigns.AsNoTracking()
            .Where(a => a.FromDate.Date <= mTo && (a.ToDate == null || a.ToDate.Value.Date >= mFrom))
            .ToListAsync();
        var empShiftPct = new Dictionary<int, (double Pct, string Name)>();
        {
            var shiftIds = monthAssigns.Select(a => a.ShiftId).Distinct().ToList();
            var shifts = shiftIds.Count == 0 ? new Dictionary<int, FaAttShift>()
                : await _db.FaAttShifts.AsNoTracking().Where(x => shiftIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id);
            foreach (var g in monthAssigns.GroupBy(a => a.EmployeeId))
            {
                var latest = g.OrderByDescending(a => a.FromDate).First();
                if (shifts.TryGetValue(latest.ShiftId, out var sh) && sh.AllowancePercent > 0)
                    empShiftPct[g.Key] = (sh.AllowancePercent, sh.Name);
            }
        }

        // پاک‌سازی محاسبه قبلی
        var oldSlipIds = await _db.FaPaySlips.Where(x => x.RunId == id).Select(x => x.Id).ToListAsync();
        if (oldSlipIds.Count > 0)
        {
            _db.FaPaySlipItems.RemoveRange(await _db.FaPaySlipItems.Where(i => oldSlipIds.Contains(i.SlipId)).ToListAsync());
            _db.FaPaySlips.RemoveRange(await _db.FaPaySlips.Where(x => x.RunId == id).ToListAsync());
            await _db.SaveChangesAsync();
        }
        // آزادسازی اقساط و معوقاتِ محاسبه قبلی همین دوره (برای محاسبه مجدد تمیز)
        var relInsts = await _db.FaPayLoanInstallments.Where(i => i.PaidRunId == id).ToListAsync();
        foreach (var ri in relInsts) { ri.IsPaid = false; ri.PaidRunId = null; ri.PaidAt = null; }
        var relArrs = await _db.FaPayArrears.Where(x => x.AppliedRunId == id).ToListAsync();
        foreach (var rx in relArrs) { rx.Status = FaPayArrearStatus.Draft; rx.AppliedRunId = null; }

        foreach (var e in emps)
        {
            att.TryGetValue(e.Id, out var a);
            double baseSalary = (double)e.BaseSalary;
            double daily = s.DaysPerMonth > 0 ? baseSalary / s.DaysPerMonth : 0;
            double hourly = s.HoursPerDay > 0 ? daily / s.HoursPerDay : 0;
            int present = a?.PresentDays ?? 0, absent = a?.AbsentDays ?? 0, mission = a?.MissionDays ?? 0;
            int otMin = a?.OvertimeMinutes ?? 0;
            int delayMin = (a?.LateMinutes ?? 0) + (a?.EarlyMinutes ?? 0);
            double unpaidDays = Math.Min(unpaid.TryGetValue(e.Id, out var u) ? u : 0, monthDays);

            double otAmount = R(otMin / 60.0 * hourly * s.OvertimeFactor);
            double delayAmount = R(delayMin / 60.0 * hourly * s.DelayFactor);
            int nightMin = a?.NightMinutes ?? 0;
            double nightAmount = R(nightMin / 60.0 * hourly * xs.NightRatePercent / 100.0);
            empShiftPct.TryGetValue(e.Id, out var shp);
            double shiftAmount = R(baseSalary * shp.Pct / 100.0);
            var myInsts = dueInsts.Where(i => loanInfo.TryGetValue(i.LoanId, out var li) && li.EmployeeId == e.Id).ToList();
            var myArrears = dueArrears.Where(x => x.EmployeeId == e.Id).ToList();
            double absentAmount = s.AbsentDeductEnabled ? R(absent * daily) : 0;
            double unpaidAmount = s.UnpaidLeaveDeductEnabled ? R(unpaidDays * daily) : 0;

            var items = new List<FaPaySlipItem>();
            double fixedEarn = 0;
            foreach (var t in fixedTypes.Where(t => t.Kind == FaPayItemKind.Earning))
            {
                if (t.DefaultAmount == 0) continue;
                fixedEarn += t.DefaultAmount;
                items.Add(new FaPaySlipItem { ItemTypeId = t.Id, Title = t.Name, Kind = FaPayItemKind.Earning, Amount = R(t.DefaultAmount), IsAuto = true });
            }
            if (otAmount != 0)
                items.Add(new FaPaySlipItem { Title = "اضافه‌کاری", Kind = FaPayItemKind.Earning, Amount = otAmount, IsAuto = true, Note = $"{otMin} دقیقه" });
            if (nightAmount != 0)
                items.Add(new FaPaySlipItem { Title = "فوق‌العاده شب‌کاری", Kind = FaPayItemKind.Earning, Amount = nightAmount, IsAuto = true, Note = $"{nightMin} دقیقه" });
            if (shiftAmount != 0)
                items.Add(new FaPaySlipItem { Title = $"فوق‌العاده نوبت‌کاری ({shp.Pct:0.#}٪)", Kind = FaPayItemKind.Earning, Amount = shiftAmount, IsAuto = true, Note = shp.Name });
            foreach (var ar in myArrears)
            {
                items.Add(new FaPaySlipItem { Title = $"معوقات: {ar.Title}", Kind = FaPayItemKind.Earning, Amount = R(ar.Amount), IsAuto = true });
                ar.Status = FaPayArrearStatus.Applied; ar.AppliedRunId = id;
            }
            foreach (var adj in adjs.Where(x => x.EmployeeId == e.Id))
            {
                if (!adjTypes.TryGetValue(adj.ItemTypeId, out var t)) continue;
                items.Add(new FaPaySlipItem { ItemTypeId = t.Id, Title = t.Name, Kind = t.Kind, Amount = R(adj.Amount), IsAuto = false, Note = adj.Note });
            }

            double gross = R(baseSalary + items.Where(i => i.Kind == FaPayItemKind.Earning).Sum(i => i.Amount));

            // کسور
            if (delayAmount != 0)
                items.Add(new FaPaySlipItem { Title = "کسر تأخیر و تعجیل", Kind = FaPayItemKind.Deduction, Amount = delayAmount, IsAuto = true, Note = $"{delayMin} دقیقه" });
            if (absentAmount != 0)
                items.Add(new FaPaySlipItem { Title = "کسر غیبت", Kind = FaPayItemKind.Deduction, Amount = absentAmount, IsAuto = true, Note = $"{absent} روز" });
            if (unpaidAmount != 0)
                items.Add(new FaPaySlipItem { Title = "کسر مرخصی بدون حقوق", Kind = FaPayItemKind.Deduction, Amount = unpaidAmount, IsAuto = true, Note = $"{unpaidDays:0.#} روز" });
            foreach (var inst in myInsts)
            {
                loanInfo.TryGetValue(inst.LoanId, out var linfo);
                items.Add(new FaPaySlipItem { Title = $"قسط وام: {linfo.Title} (قسط {inst.SeqNo} از {linfo.InstallmentCount})", Kind = FaPayItemKind.Deduction, Amount = R(inst.Amount), IsAuto = true });
                inst.IsPaid = true; inst.PaidRunId = id; inst.PaidAt = DateTime.Now;
            }
            foreach (var t in fixedTypes.Where(t => t.Kind == FaPayItemKind.Deduction))
            {
                if (t.DefaultAmount == 0) continue;
                items.Add(new FaPaySlipItem { ItemTypeId = t.Id, Title = t.Name, Kind = FaPayItemKind.Deduction, Amount = R(t.DefaultAmount), IsAuto = true });
            }

            // بیمه سهم کارمند روی (پایه + مزایای ثابت + اضافه‌کاری)
            double insurable = baseSalary + fixedEarn + otAmount;
            double insurance = R(insurable * s.InsuranceEmployeeRate / 100.0);
            if (insurance != 0)
                items.Add(new FaPaySlipItem { Title = $"بیمه سهم کارمند ({s.InsuranceEmployeeRate:0.#}٪)", Kind = FaPayItemKind.Deduction, Amount = insurance, IsAuto = true });

            // مالیات پلکانی روی مازاد مشمول
            double taxable = Math.Max(0, gross - s.TaxFreeMonthly);
            double tax = 0;
            foreach (var b in brackets)
            {
                double hi = Math.Min(taxable, b.ToAmount ?? double.MaxValue);
                if (hi > b.FromAmount) tax += (hi - b.FromAmount) * b.Rate / 100.0;
            }
            tax = R(tax);
            if (tax != 0)
                items.Add(new FaPaySlipItem { Title = "مالیات حقوق", Kind = FaPayItemKind.Deduction, Amount = tax, IsAuto = true });

            double ded = R(items.Where(i => i.Kind == FaPayItemKind.Deduction).Sum(i => i.Amount));
            var slip = new FaPaySlip
            {
                RunId = id, EmployeeId = e.Id, BaseSalary = R(baseSalary),
                PresentDays = present, AbsentDays = absent, UnpaidLeaveDays = Math.Round(unpaidDays, 1),
                OvertimeMinutes = otMin, OvertimeAmount = otAmount,
                DelayMinutes = delayMin, DelayAmount = delayAmount, MissionDays = mission,
                AbsentAmount = absentAmount, GrossEarnings = gross, TotalDeductions = ded,
                TaxAmount = tax, InsuranceAmount = insurance, NetPay = R(gross - ded),
                CalculatedAt = DateTime.Now
            };
            _db.FaPaySlips.Add(slip);
            await _db.SaveChangesAsync();
            foreach (var it in items) it.SlipId = slip.Id;
            _db.FaPaySlipItems.AddRange(items);
            await _db.SaveChangesAsync();
        }
        await _db.SaveChangesAsync(); // ثبت علامت پرداخت اقساط و اعمال معوقات
        return emps.Count;
    }

    public async Task<FaPayRunDto> FinalizeRunAsync(int id)
    {
        var r = await _db.FaPayRuns.FindAsync(id)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (r.Status == FaPayRunStatus.Final) throw new InvalidOperationException("این دوره قبلاً نهایی شده است.");
        if (!await _db.FaPaySlips.AnyAsync(s => s.RunId == id))
            throw new InvalidOperationException("ابتدا دوره را محاسبه کنید.");
        r.Status = FaPayRunStatus.Final; r.FinalizedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        try
        {
            var empIds = await _db.FaPaySlips.AsNoTracking().Where(s => s.RunId == id)
                .Select(s => s.EmployeeId).Distinct().ToListAsync();
            var emps = await _db.HrEmployees.AsNoTracking().Where(e => empIds.Contains(e.Id)).ToListAsync();
            var label = r.Kind == FaPayRunKind.YearEnd ? $"عیدی و سنوات {r.Year}" : $"{JmName(r.Month)} {r.Year}";
            var uids = emps.Where(e => e.SystemUserId is > 0).Select(e => e.SystemUserId!.Value).Distinct().ToList();
            if (uids.Count > 0)
            {
                await _notify.SendManyAsync(uids, $"فیش حقوقی {label} صادر شد",
                    "فیش حقوقی شما در سامانه قرار گرفت.", "حقوق و دستمزد", "FaPay", "fa-pay/my");
                await _notify.BroadcastChangedAsync("fa-pay");
            }
            if (_sms.IsConfigured)
                foreach (var e in emps.Where(e => !string.IsNullOrWhiteSpace(e.Mobile)))
                    try { await _sms.SendAsync(e.Mobile!.Trim(), $"فیش حقوقی {label} شما صادر شد."); } catch { }
        }
        catch { }
        return (await GetRunAsync(id))!;
    }

    public async Task<FaPayRunDto> ReopenRunAsync(int id)
    {
        var r = await _db.FaPayRuns.FindAsync(id)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (r.Status != FaPayRunStatus.Final) throw new InvalidOperationException("این دوره نهایی نیست.");
        r.Status = FaPayRunStatus.Draft; r.FinalizedAt = null;
        await _db.SaveChangesAsync();
        return (await GetRunAsync(id))!;
    }

    public async Task DeleteRunAsync(int id)
    {
        var r = await _db.FaPayRuns.FindAsync(id)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (r.Status == FaPayRunStatus.Final)
            throw new InvalidOperationException("دوره نهایی قابل حذف نیست (ابتدا بازگشایی کنید).");
        var relInsts = await _db.FaPayLoanInstallments.Where(i => i.PaidRunId == id).ToListAsync();
        foreach (var ri in relInsts) { ri.IsPaid = false; ri.PaidRunId = null; ri.PaidAt = null; }
        var relArrs = await _db.FaPayArrears.Where(x => x.AppliedRunId == id).ToListAsync();
        foreach (var rx in relArrs) { rx.Status = FaPayArrearStatus.Draft; rx.AppliedRunId = null; }
        var sids = await _db.FaPaySlips.Where(s => s.RunId == id).Select(s => s.Id).ToListAsync();
        _db.FaPaySlipItems.RemoveRange(await _db.FaPaySlipItems.Where(i => sids.Contains(i.SlipId)).ToListAsync());
        _db.FaPaySlips.RemoveRange(await _db.FaPaySlips.Where(s => s.RunId == id).ToListAsync());
        _db.FaPayRuns.Remove(r);
        await _db.SaveChangesAsync();
    }

    // ==================== فیش‌ها ====================

    public async Task<List<FaPaySlipDto>> RunSlipsAsync(int runId, Paging.Request? pagination = null)
    {
        var run = await _db.FaPayRuns.FindAsync(runId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        var slips = await _db.FaPaySlips.AsNoTracking().Where(s => s.RunId == runId).OrderBy(x => x.Id).ToPageListAsync(pagination);
        return await MapSlipsAsync(slips, run, withItems: false);
    }

    public async Task<FaPayBankCheckDto> BankCheckAsync(int runId)
    {
        var run = await _db.FaPayRuns.FindAsync(runId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        var slips = await _db.FaPaySlips.AsNoTracking().Where(s => s.RunId == runId).ToListAsync();
        var empIds = slips.Select(s => s.EmployeeId).Distinct().ToList();
        var emps = await _db.HrEmployees.AsNoTracking().Where(e => empIds.Contains(e.Id)).ToListAsync();
        var res = new FaPayBankCheckDto { Year = run.Year, Month = run.Month };
        foreach (var s in slips.OrderBy(x => x.EmployeeId))
        {
            var e = emps.FirstOrDefault(x => x.Id == s.EmployeeId);
            var name = e == null ? $"پرسنل {s.EmployeeId}" : $"{e.FirstName} {e.LastName}".Trim();
            var norm = HrSheba.Norm(e?.Sheba);
            if (!HrSheba.IsValid(norm))
            {
                res.Missing.Add(new FaPayBankMissingDto { Name = name, Code = e?.Code ?? "", Reason = string.IsNullOrWhiteSpace(e?.Sheba) ? "شبا ثبت نشده" : "شبا نامعتبر" });
                continue;
            }
            if (s.NetPay <= 0)
            {
                res.Missing.Add(new FaPayBankMissingDto { Name = name, Code = e?.Code ?? "", Reason = "خالص پرداختی صفر است" });
                continue;
            }
            res.Rows.Add(new FaPayBankRowDto { Name = name, Code = e?.Code ?? "", NationalCode = e?.NationalCode, Sheba = norm!, BankName = e?.BankName, Amount = s.NetPay });
        }
        res.TotalCount = slips.Count;
        res.ReadyCount = res.Rows.Count;
        res.TotalAmount = res.Rows.Sum(r => r.Amount);
        return res;
    }

    public async Task<(byte[] Data, string FileName, string ContentType)> BankFileAsync(int runId, string format)
    {
        var chk = await BankCheckAsync(runId);
        var f = (format ?? "xlsx").Trim().ToLowerInvariant();
        var desc = $"حقوق {JmName(chk.Month)} {Fa.Digits(chk.Year.ToString())}";
        if (f == "csv")
        {
            static string Csv(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            var sb = new StringBuilder();
            sb.AppendLine("ردیف,نام و نام خانوادگی,کد پرسنلی,کد ملی,شبا,بانک,مبلغ (ریال),شرح");
            var i = 1;
            foreach (var r in chk.Rows)
                sb.AppendLine($"{i++},{Csv(r.Name)},{Csv(r.Code)},{Csv(r.NationalCode ?? "")},{r.Sheba},{Csv(r.BankName ?? "")},{(long)Math.Round(r.Amount)},{Csv(desc)}");
            var bytes = new UTF8Encoding(true).GetBytes(sb.ToString());
            return (bytes, $"Payroll-Bank-{chk.Year}-{chk.Month}.csv", "text/csv");
        }
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("پرداخت حقوق");
        ws.RightToLeft = true;
        var headers = new[] { "ردیف", "نام و نام خانوادگی", "کد پرسنلی", "کد ملی", "شبا", "بانک", "مبلغ (ریال)", "شرح" };
        for (var i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
            c.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }
        ws.SheetView.FreezeRows(1);
        ws.Column(5).Style.NumberFormat.Format = "@";
        var row = 2;
        foreach (var r in chk.Rows)
        {
            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = r.Name;
            ws.Cell(row, 3).Value = r.Code;
            ws.Cell(row, 4).Value = r.NationalCode ?? "";
            ws.Cell(row, 5).Value = r.Sheba;
            ws.Cell(row, 6).Value = r.BankName ?? "";
            ws.Cell(row, 7).Value = r.Amount;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 8).Value = desc;
            row++;
        }
        ws.Cell(row, 6).Value = "جمع";
        ws.Cell(row, 6).Style.Font.Bold = true;
        ws.Cell(row, 7).Value = chk.TotalAmount;
        ws.Cell(row, 7).Style.Font.Bold = true;
        ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
        ws.Columns().AdjustToContents();
        if (chk.Missing.Count > 0)
        {
            var ws2 = wb.Worksheets.Add("فاقد شبا");
            ws2.RightToLeft = true;
            var h2 = new[] { "نام و نام خانوادگی", "کد پرسنلی", "علت" };
            for (var i = 0; i < h2.Length; i++)
            {
                var c = ws2.Cell(1, i + 1);
                c.Value = h2[i];
                c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF3C7");
                c.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
            var r2 = 2;
            foreach (var m in chk.Missing)
            {
                ws2.Cell(r2, 1).Value = m.Name;
                ws2.Cell(r2, 2).Value = m.Code;
                ws2.Cell(r2, 3).Value = m.Reason;
                r2++;
            }
            ws2.Columns().AdjustToContents();
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return (ms.ToArray(), $"Payroll-Bank-{chk.Year}-{chk.Month}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    public async Task<FaPaySlipDto?> GetSlipAsync(int id)
    {
        var s = await _db.FaPaySlips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return null;
        var run = await _db.FaPayRuns.FindAsync(s.RunId);
        return (await MapSlipsAsync(new List<FaPaySlip> { s }, run, withItems: true))[0];
    }

    public async Task<List<FaPaySlipDto>> MySlipsAsync(int userId, Paging.Request? pagination = null)
    {
        var empId = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.SystemUserId == userId).Select(e => e.Id).FirstOrDefaultAsync();
        if (empId == 0) return new();
        var slips = await _db.FaPaySlips.AsNoTracking().Where(s => s.EmployeeId == empId)
            .OrderByDescending(s => _db.FaPayRuns.Where(r => r.Id == s.RunId).Select(r => (int?)r.Year).FirstOrDefault() ?? 0)
            .ThenByDescending(s => _db.FaPayRuns.Where(r => r.Id == s.RunId).Select(r => (int?)r.Month).FirstOrDefault() ?? 0)
            .ThenBy(s => s.Id).ToPageListAsync(pagination);
        if (slips.Count == 0) return new();
        var runIds = slips.Select(s => s.RunId).ToList();
        var runs = await _db.FaPayRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        var out_ = new List<FaPaySlipDto>();
        foreach (var g in slips.GroupBy(s => s.RunId))
        {
            runs.TryGetValue(g.Key, out var run);
            out_.AddRange(await MapSlipsAsync(g.ToList(), run, withItems: false));
        }
        return out_.OrderByDescending(d => d.RunYear).ThenByDescending(d => d.RunMonth).ThenBy(d => d.Id).ToList();
    }

    public async Task<FaPaySlipDto?> GetMySlipAsync(int id, int userId)
    {
        var empId = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.SystemUserId == userId).Select(e => e.Id).FirstOrDefaultAsync();
        var s = await _db.FaPaySlips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == empId);
        if (s == null) return null;
        var run = await _db.FaPayRuns.FindAsync(s.RunId);
        return (await MapSlipsAsync(new List<FaPaySlip> { s }, run, withItems: true))[0];
    }

    private async Task<List<FaPaySlipDto>> MapSlipsAsync(List<FaPaySlip> slips, FaPayRun? run, bool withItems)
    {
        var empIds = slips.Select(s => s.EmployeeId).Distinct().ToList();
        var emps = await _db.HrEmployees.AsNoTracking().Where(e => empIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id);
        var unitIds = emps.Values.Where(e => e.OrgUnitId.HasValue).Select(e => e.OrgUnitId!.Value).ToList();
        var units = await _db.HrMainOrgNodes.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);
        var items = new Dictionary<int, List<FaPaySlipItem>>();
        if (withItems && slips.Count > 0)
        {
            var sids = slips.Select(s => s.Id).ToList();
            var all = await _db.FaPaySlipItems.AsNoTracking().Where(i => sids.Contains(i.SlipId)).ToListAsync();
            items = all.GroupBy(i => i.SlipId).ToDictionary(g => g.Key, g => g.ToList());
        }
        return slips.Select(s =>
        {
            emps.TryGetValue(s.EmployeeId, out var e);
            return new FaPaySlipDto
            {
                Id = s.Id, RunId = s.RunId, RunYear = run?.Year ?? 0, RunMonth = run?.Month ?? 0,
                EmployeeId = s.EmployeeId,
                EmployeeName = e == null ? null : e.FirstName + " " + e.LastName,
                EmployeeCode = e?.Code,
                OrgUnitName = e?.OrgUnitId != null && units.TryGetValue(e.OrgUnitId.Value, out var un) ? un : null,
                BaseSalary = s.BaseSalary, PresentDays = s.PresentDays, AbsentDays = s.AbsentDays,
                UnpaidLeaveDays = s.UnpaidLeaveDays, OvertimeMinutes = s.OvertimeMinutes,
                OvertimeAmount = s.OvertimeAmount, DelayMinutes = s.DelayMinutes, DelayAmount = s.DelayAmount,
                MissionDays = s.MissionDays, AbsentAmount = s.AbsentAmount,
                GrossEarnings = s.GrossEarnings, TotalDeductions = s.TotalDeductions,
                TaxAmount = s.TaxAmount, InsuranceAmount = s.InsuranceAmount, NetPay = s.NetPay,
                IsPaid = s.IsPaid,
                Items = items.TryGetValue(s.Id, out var list) ? list.Select(i => new FaPaySlipItemDto
                {
                    Id = i.Id, ItemTypeId = i.ItemTypeId, Title = i.Title,
                    Kind = (int)i.Kind, Amount = i.Amount, IsAuto = i.IsAuto, Note = i.Note
                }).ToList() : new()
            };
        }).ToList();
    }

    public async Task SetPaidAsync(int id, bool paid)
    {
        var s = await _db.FaPaySlips.FindAsync(id)
            ?? throw new InvalidOperationException("فیش یافت نشد.");
        var run = await _db.FaPayRuns.FindAsync(s.RunId);
        if (run?.Status != FaPayRunStatus.Final)
            throw new InvalidOperationException("فقط فیش دوره نهایی قابل پرداخت‌علامت‌گذاری است.");
        s.IsPaid = paid;
        await _db.SaveChangesAsync();
    }

    // ==================== PDF فیش ====================

    private static void EnsureFonts()
    {
        if (_fontsOk) return;
        lock (FontLock)
        {
            if (_fontsOk) return;
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "fonts");
                var reg = Path.Combine(dir, "Vazirmatn-Regular.ttf");
                var bold = Path.Combine(dir, "Vazirmatn-Bold.ttf");
                if (File.Exists(reg))
                    QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("FaPay", File.OpenRead(reg));
                if (File.Exists(bold))
                    QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("FaPay-Bold", File.OpenRead(bold));
            }
            catch { }
            _fontsOk = true;
        }
    }

    private static string PNum(double v) => Fa.Digits(v.ToString("#,0"));
    private static string PInt(int v) => Fa.Digits(v.ToString());

    public async Task<byte[]> SlipPdfAsync(int id)
    {
        var d = await GetSlipAsync(id) ?? throw new InvalidOperationException("فیش یافت نشد.");
        EnsureFonts();
        var doc = Document.Create(c =>
        {
            c.Page(p => SlipPage(p, d));
        });
        return doc.GeneratePdf();
    }

    /// <summary>چاپ گروهی فیش‌های یک دوره — یک فایل PDF چندصفحه‌ای (هر فیش یک صفحه).</summary>
    public async Task<byte[]> SlipsPdfAsync(int runId)
    {
        var run = await _db.FaPayRuns.FindAsync(runId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        var slips = await _db.FaPaySlips.AsNoTracking().Where(s => s.RunId == runId).ToListAsync();
        if (slips.Count == 0) throw new InvalidOperationException("این دوره فیشی ندارد.");
        EnsureFonts();
        var list = await MapSlipsAsync(slips, run, withItems: true);
        var doc = Document.Create(c =>
        {
            foreach (var dto in list)
                c.Page(p => SlipPage(p, dto));
        });
        return doc.GeneratePdf();
    }

    private static void SlipPage(PageDescriptor p, FaPaySlipDto d)
    {
        var earn = d.Items.Where(i => i.Kind == 0).ToList();
        var ded = d.Items.Where(i => i.Kind == 1).ToList();
        {
                p.Size(PageSizes.A5.Landscape());
                p.Margin(24);
                p.ContentFromRightToLeft();
                p.DefaultTextStyle(x => x.FontFamily("FaPay").FontSize(9));
                p.Header().Column(col =>
                {
                    col.Item().Text("فیش حقوقی ماهانه").FontFamily("FaPay-Bold").FontSize(15).AlignCenter();
                    col.Item().Text($"{JmName(d.RunMonth)} {Fa.Digits(d.RunYear.ToString())}").FontSize(11).AlignCenter();
                    col.Item().PaddingTop(4).LineHorizontal(1);
                });
                p.Content().Column(col =>
                {
                    col.Item().PaddingTop(6).Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(3); cd.RelativeColumn(2); cd.RelativeColumn(3); });
                        InfoCell(t, "نام و نام خانوادگی", d.EmployeeName ?? "—");
                        InfoCell(t, "کد پرسنلی", d.EmployeeCode ?? "—");
                        InfoCell(t, "واحد سازمانی", d.OrgUnitName ?? "—");
                        InfoCell(t, "حقوق پایه (ریال)", PNum(d.BaseSalary));
                        InfoCell(t, "روزهای حاضر / غیبت / مأموریت", $"{PInt(d.PresentDays)} / {PInt(d.AbsentDays)} / {PInt(d.MissionDays)}");
                        InfoCell(t, "اضافه‌کاری / تأخیر (دقیقه)", $"{PInt(d.OvertimeMinutes)} / {PInt(d.DelayMinutes)}");
                    });
                    col.Item().PaddingTop(8).Row(row =>
                    {
                        row.Spacing(12);
                        row.RelativeItem().Column(c2 =>
                        {
                            c2.Item().Text("مزایا (ریال)").FontFamily("FaPay-Bold").FontSize(10);
                            c2.Item().Table(t =>
                            {
                                t.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.RelativeColumn(2); });
                                HeadRow(t, "شرح", "مبلغ");
                                foreach (var i in earn) BodyRow(t, i.Title, PNum(i.Amount));
                            });
                        });
                        row.RelativeItem().Column(c2 =>
                        {
                            c2.Item().Text("کسور (ریال)").FontFamily("FaPay-Bold").FontSize(10);
                            c2.Item().Table(t =>
                            {
                                t.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.RelativeColumn(2); });
                                HeadRow(t, "شرح", "مبلغ");
                                foreach (var i in ded) BodyRow(t, i.Title, PNum(i.Amount));
                            });
                        });
                    });
                    col.Item().PaddingTop(8).Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(2); cd.RelativeColumn(2); cd.RelativeColumn(2); });
                        HeadRow4(t, "جمع مزایا", "جمع کسور", "مالیات + بیمه", "خالص پرداختی");
                        t.Cell().Element(TotalCell).Text(PNum(d.GrossEarnings)).AlignCenter();
                        t.Cell().Element(TotalCell).Text(PNum(d.TotalDeductions)).AlignCenter();
                        t.Cell().Element(TotalCell).Text(PNum(d.TaxAmount + d.InsuranceAmount)).AlignCenter();
                        t.Cell().Element(NetCell).Text(PNum(d.NetPay)).FontFamily("FaPay-Bold").AlignCenter();
                    });
                });
                p.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(1);
                    col.Item().Text($"تاریخ صدور: {Fa.Digits(DateTime.Today.ToString("yyyy/MM/dd"))} — این فیش به‌صورت سیستمی صادر شده است.")
                        .FontSize(8).FontColor(Colors.Grey.Darken1).AlignCenter();
                });
        }
    }

    private static void InfoCell(TableDescriptor t, string label, string value)
    {
        t.Cell().Element(LabelCell).Text(label).FontSize(8).FontColor(Colors.Grey.Darken2);
        t.Cell().Element(ValCell).Text(value).FontFamily("FaPay-Bold");
    }

    private static void HeadRow(TableDescriptor t, string a, string b)
    {
        t.Cell().Element(HeadCell).Text(a).FontFamily("FaPay-Bold").AlignCenter();
        t.Cell().Element(HeadCell).Text(b).FontFamily("FaPay-Bold").AlignCenter();
    }

    private static void HeadRow4(TableDescriptor t, string a, string b, string c, string d)
    {
        t.Cell().Element(HeadCell).Text(a).FontFamily("FaPay-Bold").FontSize(8).AlignCenter();
        t.Cell().Element(HeadCell).Text(b).FontFamily("FaPay-Bold").FontSize(8).AlignCenter();
        t.Cell().Element(HeadCell).Text(c).FontFamily("FaPay-Bold").FontSize(8).AlignCenter();
        t.Cell().Element(HeadCell).Text(d).FontFamily("FaPay-Bold").FontSize(8).AlignCenter();
    }

    private static void BodyRow(TableDescriptor t, string a, string b)
    {
        t.Cell().Element(ValCell).Text(a);
        t.Cell().Element(ValCell).Text(b).AlignLeft();
    }

    private static IContainer LabelCell(IContainer c)
        => c.Background(Colors.Grey.Lighten4).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(4);
    private static IContainer ValCell(IContainer c)
        => c.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(4);
    private static IContainer HeadCell(IContainer c)
        => c.Background("#E8E3FF").Border(1).BorderColor("#D8D4EE").Padding(4);
    private static IContainer TotalCell(IContainer c)
        => c.Background(Colors.Grey.Lighten4).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(5);
    private static IContainer NetCell(IContainer c)
        => c.Background("#E9F8F0").Border(1).BorderColor(Colors.Green.Lighten2).Padding(5);

    // ==================== ارسال ایمیل فیش ====================

    public async Task<int> SendSlipEmailAsync(int id, int byUserId)
    {
        var d = await GetSlipAsync(id) ?? throw new InvalidOperationException("فیش یافت نشد.");
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == d.EmployeeId);
        if (string.IsNullOrWhiteSpace(emp?.Email))
            throw new InvalidOperationException($"برای {d.EmployeeName} ایمیلی ثبت نشده است.");
        var acc = await _db.OtoEmails.AsNoTracking().FirstOrDefaultAsync(e => e.IsDabirkhane)
            ?? throw new InvalidOperationException("حساب ایمیل دبیرخانه تنظیم نشده است.");
        var pdf = await SlipPdfAsync(id);
        var dto = new EmailComposeDto
        {
            EmailAccountId = acc.EmailId,
            To = emp.Email!.Trim(),
            Subject = $"فیش حقوقی {JmName(d.RunMonth)} {d.RunYear}",
            Body = $"<div dir=\"rtl\"><p>{d.EmployeeName} عزیز،</p><p>فیش حقوقی {JmName(d.RunMonth)} ماه {d.RunYear} پیوست این ایمیل است.</p><p>واحد منابع انسانی</p></div>"
        };
        return await _email.SendAsync(dto, byUserId, true,
            new[] { ($"FaPaySlip-{d.RunYear}-{d.RunMonth:00}.pdf", "application/pdf", pdf) });
    }

    public async Task<FaPayMailResultDto> SendRunEmailsAsync(int runId, int byUserId)
    {
        var run = await _db.FaPayRuns.FindAsync(runId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (run.Status != FaPayRunStatus.Final)
            throw new InvalidOperationException("فقط فیش‌های دوره نهایی قابل ارسال گروهی است.");
        var sids = await _db.FaPaySlips.Where(s => s.RunId == runId).Select(s => s.Id).ToListAsync();
        int sent = 0, skipped = 0;
        foreach (var sid in sids)
        {
            try { await SendSlipEmailAsync(sid, byUserId); sent++; }
            catch { skipped++; }
        }
        return new FaPayMailResultDto { Sent = sent, Skipped = skipped };
    }

    // ==================== گزارش‌ها ====================

    private async Task<FaPayRun> RunForMonthAsync(int year, int month)
    {
        JyCheck(year, month);
        return await _db.FaPayRuns
                .OrderByDescending(r => r.Status).ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(r => r.Year == year && r.Month == month && r.Kind == FaPayRunKind.Monthly)
            ?? throw new InvalidOperationException("برای این ماه دوره حقوقی ساخته نشده است.");
    }

    public async Task<FaPayInsuranceReportDto> InsuranceReportAsync(int year, int month)
    {
        var run = await RunForMonthAsync(year, month);
        var s = await _db.FaPaySettings.FirstOrDefaultAsync() ?? new FaPaySettings();
        var slips = await _db.FaPaySlips.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync();
        var emps = await _db.HrEmployees.AsNoTracking().ToDictionaryAsync(e => e.Id);
        var sids = slips.Select(x => x.Id).ToList();
        var items = sids.Count == 0 ? new List<FaPaySlipItem>()
            : await _db.FaPaySlipItems.AsNoTracking().Where(i => sids.Contains(i.SlipId)).ToListAsync();
        var fixedEarnIds = await _db.FaPayItemTypes.AsNoTracking()
            .Where(t => t.IsFixed && t.Kind == FaPayItemKind.Earning).Select(t => t.Id).ToListAsync();
        var rows = slips.Select(x =>
        {
            emps.TryGetValue(x.EmployeeId, out var e);
            double fe = items.Where(i => i.SlipId == x.Id && i.ItemTypeId != null && fixedEarnIds.Contains(i.ItemTypeId.Value)).Sum(i => i.Amount);
            double insurable = x.BaseSalary + fe + x.OvertimeAmount;
            return new FaPayInsuranceRowDto
            {
                EmployeeId = x.EmployeeId,
                EmployeeCode = e?.Code,
                EmployeeName = e == null ? null : e.FirstName + " " + e.LastName,
                BaseSalary = x.BaseSalary, InsurableAmount = R(insurable),
                EmployeeShare = x.InsuranceAmount,
                EmployerShare = R(insurable * s.InsuranceEmployerRate / 100.0)
            };
        }).ToList();
        return new FaPayInsuranceReportDto
        {
            Year = year, Month = month, Rows = rows,
            TotalInsurable = rows.Sum(r => r.InsurableAmount),
            TotalEmployee = rows.Sum(r => r.EmployeeShare),
            TotalEmployer = rows.Sum(r => r.EmployerShare)
        };
    }

    public async Task<FaPayTaxReportDto> TaxReportAsync(int year, int month)
    {
        var run = await RunForMonthAsync(year, month);
        var s = await _db.FaPaySettings.FirstOrDefaultAsync() ?? new FaPaySettings();
        var slips = await _db.FaPaySlips.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync();
        var emps = await _db.HrEmployees.AsNoTracking().ToDictionaryAsync(e => e.Id);
        var rows = slips.Select(x => new FaPayTaxRowDto
        {
            EmployeeId = x.EmployeeId,
            EmployeeCode = emps.TryGetValue(x.EmployeeId, out var e) ? e.Code : null,
            EmployeeName = emps.TryGetValue(x.EmployeeId, out var e2) ? e2.FirstName + " " + e2.LastName : null,
            Gross = x.GrossEarnings,
            Taxable = R(Math.Max(0, x.GrossEarnings - s.TaxFreeMonthly)),
            Tax = x.TaxAmount
        }).ToList();
        return new FaPayTaxReportDto
        {
            Year = year, Month = month, Rows = rows,
            TotalGross = rows.Sum(r => r.Gross),
            TotalTaxable = rows.Sum(r => r.Taxable),
            TotalTax = rows.Sum(r => r.Tax)
        };
    }

    public async Task<FaPayUnitCostReportDto> UnitCostReportAsync(int year, int month)
    {
        var run = await RunForMonthAsync(year, month);
        var s = await _db.FaPaySettings.FirstOrDefaultAsync() ?? new FaPaySettings();
        var slips = await _db.FaPaySlips.AsNoTracking().Where(x => x.RunId == run.Id).ToListAsync();
        var emps = await _db.HrEmployees.AsNoTracking().ToDictionaryAsync(e => e.Id);
        var units = await _db.HrMainOrgNodes.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name);
        var sids = slips.Select(x => x.Id).ToList();
        var items = sids.Count == 0 ? new List<FaPaySlipItem>()
            : await _db.FaPaySlipItems.AsNoTracking().Where(i => sids.Contains(i.SlipId)).ToListAsync();
        var fixedEarnIds = await _db.FaPayItemTypes.AsNoTracking()
            .Where(t => t.IsFixed && t.Kind == FaPayItemKind.Earning).Select(t => t.Id).ToListAsync();
        double InsOf(FaPaySlip x)
        {
            double fe = items.Where(i => i.SlipId == x.Id && i.ItemTypeId != null && fixedEarnIds.Contains(i.ItemTypeId.Value)).Sum(i => i.Amount);
            return (x.BaseSalary + fe + x.OvertimeAmount) * s.InsuranceEmployerRate / 100.0;
        }
        var rows = slips.GroupBy(x => emps.TryGetValue(x.EmployeeId, out var e) ? e.OrgUnitId : null)
            .Select(g => new FaPayUnitCostRowDto
            {
                OrgUnitId = g.Key,
                OrgUnitName = g.Key != null && units.TryGetValue(g.Key.Value, out var n) ? n : "بدون واحد",
                Headcount = g.Count(),
                GrossTotal = R(g.Sum(x => x.GrossEarnings)),
                EmployerInsurance = R(g.Sum(InsOf)),
                TotalCost = 0
            }).ToList();
        foreach (var r in rows) r.TotalCost = R(r.GrossTotal + r.EmployerInsurance);
        return new FaPayUnitCostReportDto
        {
            Year = year, Month = month, Rows = rows.OrderByDescending(r => r.TotalCost).ToList(),
            TotalGross = rows.Sum(r => r.GrossTotal),
            TotalEmployerInsurance = rows.Sum(r => r.EmployerInsurance),
            TotalCost = rows.Sum(r => r.TotalCost)
        };
    }
}
