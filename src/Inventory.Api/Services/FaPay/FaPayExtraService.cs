using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services.FaCom;
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
/// ================== حقوق و دستمزد فروغ آریا — افزونه‌های §۲ ==================
/// وام و مساعده اقساطی (کسر خودکار از فیش)، معوقات، دوره پایان‌سال (عیدی و سنوات)،
/// تسویه پایان همکاری، فایل لیست بیمه، چاپ گروهی فیش‌ها، مقایسه دوره‌ها.
/// سیستم قدیمی (HrPay) دست نمی‌خورد.
/// </summary>
public interface IFaPayExtraService
{
    Task<FaPayExtraSettingsDto> GetExtraSettingsAsync();
    Task<FaPayExtraSettingsDto> SaveExtraSettingsAsync(FaPayExtraSettingsSaveDto dto);

    Task<List<FaPayMinWageDto>> ListMinWagesAsync();
    Task<FaPayMinWageDto> SaveMinWageAsync(FaPayMinWageSaveDto dto);
    Task DeleteMinWageAsync(int year);

    Task<List<FaPayLoanDto>> ListLoansAsync(int? employeeId, int? status, Paging.Request? pagination = null);
    Task<FaPayLoanDto?> GetLoanAsync(int id);
    Task<FaPayLoanDto> CreateLoanAsync(FaPayLoanSaveDto dto, int byUserId, string byName);
    Task CancelLoanAsync(int id);
    Task DeleteLoanAsync(int id);

    Task<List<FaPayArrearDto>> ListArrearsAsync(int? employeeId, int? status, Paging.Request? pagination = null);
    Task<FaPayArrearDto> SaveArrearAsync(int? id, FaPayArrearSaveDto dto, int byUserId, string byName);
    Task DeleteArrearAsync(int id);

    Task<int> CalculateYearEndAsync(int runId);

    Task<FaPaySettlementDto> PreviewSettlementAsync(int employeeId, DateTime leaveDate, int reason, double otherEarnings, double otherDeductions);
    Task<List<FaPaySettlementDto>> ListSettlementsAsync(int? employeeId, int? status, Paging.Request? pagination = null);
    Task<FaPaySettlementDto?> GetSettlementAsync(int id);
    Task<FaPaySettlementDto> SaveSettlementAsync(int? id, FaPaySettlementSaveDto dto, int byUserId, string byName);
    Task<FaPaySettlementDto> FinalizeSettlementAsync(int id);
    Task DeleteSettlementAsync(int id);
    Task<byte[]> SettlementPdfAsync(int id);

    Task<FaPayInsuranceFileCheckDto> InsuranceCheckAsync(int runId);
    Task<(byte[] Data, string FileName, string ContentType)> InsuranceFileAsync(int runId, string format);
    Task<FaPayCompareDto> CompareRunsAsync(int runAId, int runBId);
}

public class FaPayExtraService : IFaPayExtraService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private static readonly PersianCalendar Pc = new();

    private static bool _fontsOk;
    private static readonly object FontLock = new();

    static FaPayExtraService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public FaPayExtraService(AppDbContext db, INotifyService notify)
    {
        _db = db; _notify = notify;
    }

    private static void JyCheck(int jy, int jm)
    {
        if (jy is < 1300 or > 1500 || jm is < 1 or > 12)
            throw new InvalidOperationException("سال/ماه شمسی نامعتبر است.");
    }

    private static double R(double v) => Math.Round(v, MidpointRounding.AwayFromZero);

    private static string JmName(int m) => m switch
    {
        1 => "فروردین", 2 => "اردیبهشت", 3 => "خرداد", 4 => "تیر", 5 => "مرداد", 6 => "شهریور",
        7 => "مهر", 8 => "آبان", 9 => "آذر", 10 => "دی", 11 => "بهمن", 12 => "اسفند", _ => ""
    };

    private static (int Y, int M) NextJm(int y, int m) => m == 12 ? (y + 1, 1) : (y, m + 1);

    private static double TaxOn(double taxableBase, double exempt, List<FaPayTaxBracket> brackets)
    {
        double taxable = Math.Max(0, taxableBase - exempt);
        double tax = 0;
        foreach (var b in brackets)
        {
            double hi = Math.Min(taxable, b.ToAmount ?? double.MaxValue);
            if (hi > b.FromAmount) tax += (hi - b.FromAmount) * b.Rate / 100.0;
        }
        return R(tax);
    }

    // ==================== تنظیمات تکمیلی ====================

    public async Task<FaPayExtraSettingsDto> GetExtraSettingsAsync()
    {
        var s = await _db.FaPayExtraSettings.FirstOrDefaultAsync() ?? new FaPayExtraSettings();
        return new FaPayExtraSettingsDto
        {
            WorkshopCode = s.WorkshopCode, WorkshopName = s.WorkshopName,
            NightRatePercent = s.NightRatePercent,
            EidiCapMultiplier = s.EidiCapMultiplier, EidiBaseMultiplier = s.EidiBaseMultiplier
        };
    }

    public async Task<FaPayExtraSettingsDto> SaveExtraSettingsAsync(FaPayExtraSettingsSaveDto dto)
    {
        if (dto.NightRatePercent < 0 || dto.NightRatePercent > 200)
            throw new InvalidOperationException("درصد شب‌کاری باید بین ۰ تا ۲۰۰ باشد.");
        if (dto.EidiCapMultiplier <= 0 || dto.EidiBaseMultiplier <= 0)
            throw new InvalidOperationException("ضرایب عیدی باید بزرگ‌تر از صفر باشند.");
        var code = string.IsNullOrWhiteSpace(dto.WorkshopCode) ? null
            : new string(dto.WorkshopCode.Trim().Where(char.IsDigit).ToArray());
        if (code != null && code.Length is < 10 or > 14)
            throw new InvalidOperationException("کد کارگاه بیمه باید ۱۰ تا ۱۴ رقم باشد.");
        var s = await _db.FaPayExtraSettings.FirstOrDefaultAsync();
        if (s == null) { s = new FaPayExtraSettings(); _db.FaPayExtraSettings.Add(s); }
        s.WorkshopCode = code;
        s.WorkshopName = string.IsNullOrWhiteSpace(dto.WorkshopName) ? null : dto.WorkshopName.Trim();
        s.NightRatePercent = dto.NightRatePercent;
        s.EidiCapMultiplier = dto.EidiCapMultiplier;
        s.EidiBaseMultiplier = dto.EidiBaseMultiplier;
        await _db.SaveChangesAsync();
        return await GetExtraSettingsAsync();
    }

    // ==================== حداقل دستمزد قانونی (جدول اختصاصی FaPayMinWages) ====================

    public async Task<List<FaPayMinWageDto>> ListMinWagesAsync()
        => await _db.FaPayMinWages.AsNoTracking().OrderByDescending(w => w.Year)
            .Select(w => new FaPayMinWageDto
            {
                Year = w.Year, MonthlyWage = w.MonthlyWage, DailyWage = w.DailyWage,
                EffectiveDate = w.EffectiveDate, Note = w.Note
            }).ToListAsync();

    public async Task<FaPayMinWageDto> SaveMinWageAsync(FaPayMinWageSaveDto dto)
    {
        if (dto.Year < 1300 || dto.Year > 1500) throw new InvalidOperationException("سال معتبر نیست.");
        if (dto.MonthlyWage <= 0) throw new InvalidOperationException("مبلغ حداقل دستمزد معتبر نیست.");
        var row = await _db.FaPayMinWages.FirstOrDefaultAsync(w => w.Year == dto.Year);
        if (row == null) { row = new FaPayMinWage { Year = dto.Year }; _db.FaPayMinWages.Add(row); }
        row.MonthlyWage = dto.MonthlyWage;
        row.DailyWage = Math.Round(dto.MonthlyWage / 30, 0);
        row.EffectiveDate = (dto.EffectiveDate ?? Pc.ToDateTime(dto.Year, 1, 1, 0, 0, 0, 0)).Date;
        row.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        row.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new FaPayMinWageDto
        {
            Year = row.Year, MonthlyWage = row.MonthlyWage, DailyWage = row.DailyWage,
            EffectiveDate = row.EffectiveDate, Note = row.Note
        };
    }

    public async Task DeleteMinWageAsync(int year)
    {
        var row = await _db.FaPayMinWages.FirstOrDefaultAsync(w => w.Year == year)
            ?? throw new InvalidOperationException("ردیف حداقل دستمزد یافت نشد.");
        _db.FaPayMinWages.Remove(row);
        await _db.SaveChangesAsync();
    }

    // ==================== وام و مساعده ====================

    public async Task<List<FaPayLoanDto>> ListLoansAsync(int? employeeId, int? status, Paging.Request? pagination = null)
    {
        var q = _db.FaPayLoans.AsNoTracking().AsQueryable();
        if (employeeId is > 0) q = q.Where(l => l.EmployeeId == employeeId.Value);
        if (status is >= 0) q = q.Where(l => (int)l.Status == status.Value);
        var loans = await q.OrderByDescending(l => l.Id).ToPageListAsync(pagination, defaultCap: 1000);
        if (loans.Count == 0) return new();
        var names = await _db.HrEmployees.AsNoTracking()
            .ToDictionaryAsync(e => e.Id, e => new { N = e.FirstName + " " + e.LastName, e.Code });
        var lids = loans.Select(l => l.Id).ToList();
        var insts = await _db.FaPayLoanInstallments.AsNoTracking()
            .Where(i => lids.Contains(i.LoanId)).OrderBy(i => i.SeqNo).ToListAsync();
        return loans.Select(l =>
        {
            var list = insts.Where(i => i.LoanId == l.Id).ToList();
            double paid = list.Where(i => i.IsPaid).Sum(i => i.Amount);
            names.TryGetValue(l.EmployeeId, out var nm);
            return new FaPayLoanDto
            {
                Id = l.Id, EmployeeId = l.EmployeeId, EmployeeName = nm?.N, EmployeeCode = nm?.Code,
                Title = l.Title, TotalAmount = l.TotalAmount,
                InstallmentCount = l.InstallmentCount, InstallmentAmount = l.InstallmentAmount,
                StartYear = l.StartYear, StartMonth = l.StartMonth, Status = (int)l.Status,
                PaidCount = list.Count(i => i.IsPaid), PaidAmount = paid,
                RemainingAmount = R(Math.Max(0, l.TotalAmount - paid)),
                Note = l.Note, CreatedAt = l.CreatedAt,
                Installments = list.Select(i => new FaPayLoanInstallmentDto
                {
                    Id = i.Id, SeqNo = i.SeqNo, Year = i.Year, Month = i.Month,
                    Amount = i.Amount, IsPaid = i.IsPaid
                }).ToList()
            };
        }).ToList();
    }

    public async Task<FaPayLoanDto?> GetLoanAsync(int id)
        => (await ListLoansAsync(null, null)).FirstOrDefault(l => l.Id == id);

    public async Task<FaPayLoanDto> CreateLoanAsync(FaPayLoanSaveDto dto, int byUserId, string byName)
    {
        JyCheck(dto.StartYear, dto.StartMonth);
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان وام الزامی است.");
        if (dto.TotalAmount <= 0) throw new InvalidOperationException("مبلغ وام باید بزرگ‌تر از صفر باشد.");
        if (dto.InstallmentCount is < 1 or > 120) throw new InvalidOperationException("تعداد اقساط باید بین ۱ تا ۱۲۰ باشد.");
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId && e.IsActive))
            throw new InvalidOperationException("پرسنل فعال یافت نشد.");
        var per = R(dto.TotalAmount / dto.InstallmentCount);
        var loan = new FaPayLoan
        {
            EmployeeId = dto.EmployeeId, Title = dto.Title.Trim(),
            TotalAmount = R(dto.TotalAmount), InstallmentCount = dto.InstallmentCount,
            InstallmentAmount = per, StartYear = dto.StartYear, StartMonth = dto.StartMonth,
            Status = FaPayLoanStatus.Active, Note = dto.Note,
            CreatedByUserId = byUserId, CreatedByName = byName, CreatedAt = DateTime.Now
        };
        _db.FaPayLoans.Add(loan);
        await _db.SaveChangesAsync();
        // جدول اقساط: ماه‌های متوالی از ماه شروع؛ الباقیِ گردکردن در قسط آخر
        var (y, m) = (dto.StartYear, dto.StartMonth);
        double acc = 0;
        for (var k = 1; k <= dto.InstallmentCount; k++)
        {
            double amt = k == dto.InstallmentCount ? R(dto.TotalAmount - acc) : per;
            acc += amt;
            _db.FaPayLoanInstallments.Add(new FaPayLoanInstallment
            {
                LoanId = loan.Id, SeqNo = k, Year = y, Month = m, Amount = amt
            });
            (y, m) = NextJm(y, m);
        }
        await _db.SaveChangesAsync();
        return (await GetLoanAsync(loan.Id))!;
    }

    public async Task CancelLoanAsync(int id)
    {
        var l = await _db.FaPayLoans.FindAsync(id)
            ?? throw new InvalidOperationException("وام یافت نشد.");
        if (l.Status != FaPayLoanStatus.Active) throw new InvalidOperationException("این وام فعال نیست.");
        l.Status = FaPayLoanStatus.Cancelled;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteLoanAsync(int id)
    {
        var l = await _db.FaPayLoans.FindAsync(id)
            ?? throw new InvalidOperationException("وام یافت نشد.");
        if (await _db.FaPayLoanInstallments.AnyAsync(i => i.LoanId == id && i.IsPaid))
            throw new InvalidOperationException("قسط پرداخت‌شده دارد؛ حذف ممکن نیست (می‌توانید لغوش کنید).");
        _db.FaPayLoanInstallments.RemoveRange(
            await _db.FaPayLoanInstallments.Where(i => i.LoanId == id).ToListAsync());
        _db.FaPayLoans.Remove(l);
        await _db.SaveChangesAsync();
    }

    // ==================== معوقات ====================

    public async Task<List<FaPayArrearDto>> ListArrearsAsync(int? employeeId, int? status, Paging.Request? pagination = null)
    {
        var q = _db.FaPayArrears.AsNoTracking().AsQueryable();
        if (employeeId is > 0) q = q.Where(a => a.EmployeeId == employeeId.Value);
        if (status is >= 0) q = q.Where(a => (int)a.Status == status.Value);
        var rows = await q.OrderByDescending(a => a.Id).ToPageListAsync(pagination, defaultCap: 1000);
        var names = await _db.HrEmployees.AsNoTracking()
            .ToDictionaryAsync(e => e.Id, e => new { N = e.FirstName + " " + e.LastName, e.Code });
        return rows.Select(a => new FaPayArrearDto
        {
            Id = a.Id, EmployeeId = a.EmployeeId,
            EmployeeName = names.TryGetValue(a.EmployeeId, out var n) ? n.N : null,
            EmployeeCode = names.TryGetValue(a.EmployeeId, out var n2) ? n2.Code : null,
            Title = a.Title, FromYear = a.FromYear, FromMonth = a.FromMonth,
            ToYear = a.ToYear, ToMonth = a.ToMonth, Amount = a.Amount,
            TargetYear = a.TargetYear, TargetMonth = a.TargetMonth,
            Status = (int)a.Status, Note = a.Note, CreatedAt = a.CreatedAt
        }).ToList();
    }

    public async Task<FaPayArrearDto> SaveArrearAsync(int? id, FaPayArrearSaveDto dto, int byUserId, string byName)
    {
        JyCheck(dto.FromYear, dto.FromMonth);
        JyCheck(dto.ToYear, dto.ToMonth);
        JyCheck(dto.TargetYear, dto.TargetMonth);
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new InvalidOperationException("عنوان معوقه الزامی است.");
        if (dto.Amount == 0) throw new InvalidOperationException("مبلغ معوقه نباید صفر باشد.");
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId && e.IsActive))
            throw new InvalidOperationException("پرسنل فعال یافت نشد.");
        if (await _db.FaPayRuns.AnyAsync(r => r.Year == dto.TargetYear && r.Month == dto.TargetMonth
            && r.Kind == FaPayRunKind.Monthly && r.Status == FaPayRunStatus.Final))
            throw new InvalidOperationException("دوره ماه هدف نهایی شده است.");
        FaPayArrear a;
        if (id == null)
        {
            a = new FaPayArrear { CreatedByUserId = byUserId, CreatedByName = byName, CreatedAt = DateTime.Now };
            _db.FaPayArrears.Add(a);
        }
        else
        {
            a = await _db.FaPayArrears.FindAsync(id.Value)
                ?? throw new InvalidOperationException("معوقه یافت نشد.");
            if (a.Status == FaPayArrearStatus.Applied)
                throw new InvalidOperationException("این معوقه در فیش اعمال شده و قابل ویرایش نیست.");
        }
        a.EmployeeId = dto.EmployeeId; a.Title = dto.Title.Trim();
        a.FromYear = dto.FromYear; a.FromMonth = dto.FromMonth;
        a.ToYear = dto.ToYear; a.ToMonth = dto.ToMonth;
        a.Amount = R(dto.Amount);
        a.TargetYear = dto.TargetYear; a.TargetMonth = dto.TargetMonth;
        a.Note = dto.Note;
        await _db.SaveChangesAsync();
        return (await ListArrearsAsync(null, null)).First(x => x.Id == a.Id);
    }

    public async Task DeleteArrearAsync(int id)
    {
        var a = await _db.FaPayArrears.FindAsync(id)
            ?? throw new InvalidOperationException("معوقه یافت نشد.");
        if (a.Status == FaPayArrearStatus.Applied)
            throw new InvalidOperationException("این معوقه در فیش اعمال شده و قابل حذف نیست.");
        _db.FaPayArrears.Remove(a);
        await _db.SaveChangesAsync();
    }

    // ==================== دوره پایان‌سال (عیدی و سنوات) ====================

    /// <summary>
    /// محاسبه دوره عیدی و سنوات (فقط دوره Kind=YearEnd پیش‌نویس، ماه ۱۲):
    /// عیدی = ۲× آخرین حقوق تا سقف ۳× حداقل (تناسبی با کارکرد سال) + سنوات = حقوق × روزهای خدمت ÷ ۳۶۵.
    /// سنوات معاف از مالیات؛ عیدی با معافیت ماهانه مشمول پلکان. بیمه ندارد.
    /// </summary>
    public async Task<int> CalculateYearEndAsync(int runId)
    {
        var r = await _db.FaPayRuns.FindAsync(runId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (r.Kind != FaPayRunKind.YearEnd)
            throw new InvalidOperationException("این دوره پایان‌سال نیست.");
        if (r.Status != FaPayRunStatus.Draft)
            throw new InvalidOperationException("فقط دوره پیش‌نویس قابل محاسبه مجدد است.");
        var s = await _db.FaPaySettings.FirstOrDefaultAsync() ?? new FaPaySettings();
        var xs = await _db.FaPayExtraSettings.FirstOrDefaultAsync() ?? new FaPayExtraSettings();
        var yearStart = Pc.ToDateTime(r.Year, 1, 1, 0, 0, 0, 0);
        var yearEnd = Pc.ToDateTime(r.Year, 12, Pc.GetDaysInMonth(r.Year, 12), 0, 0, 0, 0);
        var minWage = await _db.FaPayMinWages.AsNoTracking()
            .Where(w => w.Year == r.Year).Select(w => (double?)w.MonthlyWage).FirstOrDefaultAsync();
        if (minWage is null or <= 0)
            throw new InvalidOperationException($"حداقل دستمزد سال {r.Year} ثبت نشده است؛ ابتدا از «تنظیمات حقوق ← حداقل دستمزد قانونی» ثبت کنید.");
        var brackets = await _db.FaPayTaxBrackets.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.FromAmount).ToListAsync();
        var emps = await _db.HrEmployees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Code).ToListAsync();
        if (emps.Count == 0) throw new InvalidOperationException("پرسنل فعالی یافت نشد.");

        var oldSlipIds = await _db.FaPaySlips.Where(x => x.RunId == runId).Select(x => x.Id).ToListAsync();
        if (oldSlipIds.Count > 0)
        {
            _db.FaPaySlipItems.RemoveRange(await _db.FaPaySlipItems.Where(i => oldSlipIds.Contains(i.SlipId)).ToListAsync());
            _db.FaPaySlips.RemoveRange(await _db.FaPaySlips.Where(x => x.RunId == runId).ToListAsync());
            await _db.SaveChangesAsync();
        }

        foreach (var e in emps)
        {
            double baseSalary = (double)e.BaseSalary;
            var svcFrom = e.HireDate.Date > yearStart ? e.HireDate.Date : yearStart;
            var svcDays = svcFrom > yearEnd ? 0 : (yearEnd - svcFrom).Days + 1;
            var ratio = Math.Min(1.0, svcDays / 365.0);
            var eidiFull = Math.Min(xs.EidiBaseMultiplier * baseSalary, xs.EidiCapMultiplier * minWage.Value);
            var eidi = R(eidiFull * ratio);
            var senavat = R(baseSalary * ratio);
            var tax = TaxOn(eidi, s.TaxFreeMonthly, brackets);
            var items = new List<FaPaySlipItem>();
            if (eidi != 0)
                items.Add(new FaPaySlipItem
                {
                    Title = $"عیدی پایان سال {r.Year}", Kind = FaPayItemKind.Earning, Amount = eidi,
                    IsAuto = true, Note = $"تناسبی {svcDays} روز خدمت"
                });
            if (senavat != 0)
                items.Add(new FaPaySlipItem
                {
                    Title = "سنوات خدمت (پاداش پایان سال)", Kind = FaPayItemKind.Earning, Amount = senavat,
                    IsAuto = true, Note = "معاف از مالیات"
                });
            if (tax != 0)
                items.Add(new FaPaySlipItem { Title = "مالیات عیدی", Kind = FaPayItemKind.Deduction, Amount = tax, IsAuto = true });
            var gross = R(eidi + senavat);
            var slip = new FaPaySlip
            {
                RunId = runId, EmployeeId = e.Id, BaseSalary = R(baseSalary),
                GrossEarnings = gross, TotalDeductions = tax,
                TaxAmount = tax, InsuranceAmount = 0, NetPay = R(gross - tax),
                CalculatedAt = DateTime.Now
            };
            _db.FaPaySlips.Add(slip);
            await _db.SaveChangesAsync();
            foreach (var it in items) it.SlipId = slip.Id;
            _db.FaPaySlipItems.AddRange(items);
            await _db.SaveChangesAsync();
        }
        return emps.Count;
    }

    // ==================== تسویه پایان همکاری ====================

    private async Task<(int ServiceDays, double BaseSalary, double Senavat, double Eidi,
        double LeaveDays, double LeaveAmount, double LoanRem, double Tax, double Gross, double Net)> ComputeSettlementAsync(
        HrEmployee e, DateTime leaveDate, double otherEarnings, double otherDeductions)
    {
        leaveDate = leaveDate.Date;
        if (leaveDate < e.HireDate.Date)
            throw new InvalidOperationException("تاریخ پایان همکاری نمی‌تواند قبل از استخدام باشد.");
        var s = await _db.FaPaySettings.FirstOrDefaultAsync() ?? new FaPaySettings();
        var xs = await _db.FaPayExtraSettings.FirstOrDefaultAsync() ?? new FaPayExtraSettings();
        var jy = Pc.GetYear(leaveDate);
        var yearStart = Pc.ToDateTime(jy, 1, 1, 0, 0, 0, 0);
        double baseSalary = (double)e.BaseSalary;
        double daily = s.DaysPerMonth > 0 ? baseSalary / s.DaysPerMonth : 0;

        var svcDays = (leaveDate - e.HireDate.Date).Days + 1;
        // سنوات کل سابقه: حقوق × سال‌های خدمت (کسری تناسبی)
        var senavat = R(baseSalary * (svcDays / 365.0));

        var yearSvcFrom = e.HireDate.Date > yearStart ? e.HireDate.Date : yearStart;
        var yearDays = yearSvcFrom > leaveDate ? 0 : (leaveDate - yearSvcFrom).Days + 1;
        var minWage = await _db.FaPayMinWages.AsNoTracking()
            .Where(w => w.Year == jy).Select(w => (double?)w.MonthlyWage).FirstOrDefaultAsync() ?? 0;
        var eidiFull = minWage > 0
            ? Math.Min(xs.EidiBaseMultiplier * baseSalary, xs.EidiCapMultiplier * minWage)
            : xs.EidiBaseMultiplier * baseSalary;
        var eidi = R(eidiFull * Math.Min(1.0, yearDays / 365.0));

        // بازخرید مانده استحقاقی سال جاری
        var annualTypeId = await _db.FaAttLeaveTypes.AsNoTracking()
            .Where(t => t.Name == "استحقاقی").Select(t => t.Id).FirstOrDefaultAsync();
        double leaveDays = 0;
        if (annualTypeId != 0)
        {
            var bal = await _db.FaAttLeaveBalances.AsNoTracking()
                .FirstOrDefaultAsync(b => b.EmployeeId == e.Id && b.Year == jy && b.LeaveTypeId == annualTypeId);
            if (bal != null)
                leaveDays = Math.Max(0, bal.EntitledDays + bal.CarriedDays - bal.UsedDays - bal.CashedDays);
        }
        var leaveAmt = R(leaveDays * daily);

        var loanRem = R(await _db.FaPayLoanInstallments.AsNoTracking()
            .Where(i => !i.IsPaid && _db.FaPayLoans.Any(l => l.Id == i.LoanId
                && l.EmployeeId == e.Id && l.Status == FaPayLoanStatus.Active))
            .SumAsync(i => (double?)i.Amount) ?? 0);

        var brackets = await _db.FaPayTaxBrackets.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.FromAmount).ToListAsync();
        // سنوات معاف از مالیات؛ عیدی + بازخرید + سایر با معافیت ماهانه
        var tax = TaxOn(eidi + leaveAmt + otherEarnings, s.TaxFreeMonthly, brackets);
        var gross = R(senavat + eidi + leaveAmt + otherEarnings);
        var net = R(gross - loanRem - otherDeductions - tax);
        return (svcDays, R(baseSalary), senavat, eidi,
            Math.Round(leaveDays, 1), leaveAmt, loanRem, tax, gross, net);
    }

    public async Task<FaPaySettlementDto> PreviewSettlementAsync(
        int employeeId, DateTime leaveDate, int reason, double otherEarnings, double otherDeductions)
    {
        var e = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId)
            ?? throw new InvalidOperationException("پرسنل یافت نشد.");
        var c = await ComputeSettlementAsync(e, leaveDate, otherEarnings, otherDeductions);
        return new FaPaySettlementDto
        {
            EmployeeId = e.Id, EmployeeName = e.FirstName + " " + e.LastName,
            EmployeeCode = e.Code, NationalCode = e.NationalCode,
            LeaveDate = leaveDate.Date, Reason = reason,
            ServiceDays = c.ServiceDays, BaseSalary = c.BaseSalary,
            SenavatAmount = c.Senavat, EidiAmount = c.Eidi,
            LeaveBuybackDays = c.LeaveDays, LeaveBuybackAmount = c.LeaveAmount,
            OtherEarnings = R(otherEarnings), LoanRemaining = c.LoanRem,
            OtherDeductions = R(otherDeductions), TaxAmount = c.Tax,
            GrossTotal = c.Gross, NetPayable = c.Net, DeactivateEmployee = true
        };
    }

    public async Task<List<FaPaySettlementDto>> ListSettlementsAsync(int? employeeId, int? status, Paging.Request? pagination = null)
    {
        var q = _db.FaPaySettlements.AsNoTracking().AsQueryable();
        if (employeeId is > 0) q = q.Where(x => x.EmployeeId == employeeId.Value);
        if (status is >= 0) q = q.Where(x => (int)x.Status == status.Value);
        var rows = await q.OrderByDescending(x => x.Id).ToPageListAsync(pagination, defaultCap: 500);
        var emps = await _db.HrEmployees.AsNoTracking().Where(e => rows.Select(r => r.EmployeeId).Contains(e.Id)).ToDictionaryAsync(e => e.Id);
        return rows.Select(x => MapSettlement(x, emps.TryGetValue(x.EmployeeId, out var e) ? e : null)).ToList();
    }

    public async Task<FaPaySettlementDto?> GetSettlementAsync(int id)
    {
        var x = await _db.FaPaySettlements.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (x == null) return null;
        var e = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(v => v.Id == x.EmployeeId);
        return MapSettlement(x, e);
    }

    private static FaPaySettlementDto MapSettlement(FaPaySettlement x, HrEmployee? e) => new()
    {
        Id = x.Id, EmployeeId = x.EmployeeId,
        EmployeeName = e == null ? null : e.FirstName + " " + e.LastName,
        EmployeeCode = e?.Code, NationalCode = e?.NationalCode,
        LeaveDate = x.LeaveDate, Reason = (int)x.Reason,
        ServiceDays = x.ServiceDays, BaseSalary = x.BaseSalary,
        SenavatAmount = x.SenavatAmount, EidiAmount = x.EidiAmount,
        LeaveBuybackDays = x.LeaveBuybackDays, LeaveBuybackAmount = x.LeaveBuybackAmount,
        OtherEarnings = x.OtherEarnings, OtherEarningsNote = x.OtherEarningsNote,
        LoanRemaining = x.LoanRemaining,
        OtherDeductions = x.OtherDeductions, OtherDeductionsNote = x.OtherDeductionsNote,
        TaxAmount = x.TaxAmount, GrossTotal = x.GrossTotal, NetPayable = x.NetPayable,
        Status = (int)x.Status, DeactivateEmployee = x.DeactivateEmployee,
        Note = x.Note, CreatedAt = x.CreatedAt, FinalizedAt = x.FinalizedAt
    };

    public async Task<FaPaySettlementDto> SaveSettlementAsync(
        int? id, FaPaySettlementSaveDto dto, int byUserId, string byName)
    {
        if (dto.OtherEarnings < 0 || dto.OtherDeductions < 0)
            throw new InvalidOperationException("مبالغ سایر نباید منفی باشند.");
        var e = await _db.HrEmployees.FirstOrDefaultAsync(x => x.Id == dto.EmployeeId)
            ?? throw new InvalidOperationException("پرسنل یافت نشد.");
        FaPaySettlement x;
        if (id == null)
        {
            if (await _db.FaPaySettlements.AnyAsync(s => s.EmployeeId == dto.EmployeeId
                && s.Status == FaPaySettlementStatus.Draft))
                throw new InvalidOperationException("برای این پرسنل یک تسویه پیش‌نویس باز وجود دارد.");
            x = new FaPaySettlement { CreatedByUserId = byUserId, CreatedByName = byName, CreatedAt = DateTime.Now };
            _db.FaPaySettlements.Add(x);
        }
        else
        {
            x = await _db.FaPaySettlements.FindAsync(id.Value)
                ?? throw new InvalidOperationException("تسویه یافت نشد.");
            if (x.Status == FaPaySettlementStatus.Final)
                throw new InvalidOperationException("تسویه نهایی قابل ویرایش نیست.");
        }
        var c = await ComputeSettlementAsync(e, dto.LeaveDate, dto.OtherEarnings, dto.OtherDeductions);
        x.EmployeeId = dto.EmployeeId; x.LeaveDate = dto.LeaveDate.Date;
        x.Reason = (FaPaySettlementReason)dto.Reason;
        x.ServiceDays = c.ServiceDays; x.BaseSalary = c.BaseSalary;
        x.SenavatAmount = c.Senavat; x.EidiAmount = c.Eidi;
        x.LeaveBuybackDays = c.LeaveDays; x.LeaveBuybackAmount = c.LeaveAmount;
        x.OtherEarnings = R(dto.OtherEarnings); x.OtherEarningsNote = dto.OtherEarningsNote;
        x.LoanRemaining = c.LoanRem;
        x.OtherDeductions = R(dto.OtherDeductions); x.OtherDeductionsNote = dto.OtherDeductionsNote;
        x.TaxAmount = c.Tax; x.GrossTotal = c.Gross; x.NetPayable = c.Net;
        x.DeactivateEmployee = dto.DeactivateEmployee; x.Note = dto.Note;
        await _db.SaveChangesAsync();
        return (await GetSettlementAsync(x.Id))!;
    }

    public async Task<FaPaySettlementDto> FinalizeSettlementAsync(int id)
    {
        var x = await _db.FaPaySettlements.FindAsync(id)
            ?? throw new InvalidOperationException("تسویه یافت نشد.");
        if (x.Status == FaPaySettlementStatus.Final)
            throw new InvalidOperationException("این تسویه قبلاً نهایی شده است.");
        x.Status = FaPaySettlementStatus.Final;
        x.FinalizedAt = DateTime.Now;
        // مانده وام‌ها یکجا از تسویه کسر شد → اقساط باز، پرداخت‌شده با تسویه محسوب می‌شوند
        var openInsts = await _db.FaPayLoanInstallments
            .Where(i => !i.IsPaid && _db.FaPayLoans.Any(l => l.Id == i.LoanId
                && l.EmployeeId == x.EmployeeId && l.Status == FaPayLoanStatus.Active))
            .ToListAsync();
        foreach (var i in openInsts) { i.IsPaid = true; i.PaidAt = DateTime.Now; }
        var empLoanIds = await _db.FaPayLoans
            .Where(l => l.EmployeeId == x.EmployeeId && l.Status == FaPayLoanStatus.Active)
            .Select(l => l.Id).ToListAsync();
        foreach (var lid in empLoanIds)
        {
            if (!await _db.FaPayLoanInstallments.AnyAsync(i => i.LoanId == lid && !i.IsPaid))
            {
                var loan = await _db.FaPayLoans.FindAsync(lid);
                if (loan != null) loan.Status = FaPayLoanStatus.Paid;
            }
        }
        HrEmployee? e = null;
        if (x.DeactivateEmployee)
        {
            e = await _db.HrEmployees.FindAsync(x.EmployeeId);
            if (e != null)
            {
                e.IsActive = false;
                e.Status = x.Reason == FaPaySettlementReason.Retirement
                    ? HrEmployeeStatus.Retired : HrEmployeeStatus.Terminated;
                e.UpdatedAt = DateTime.Now;
            }
        }
        await _db.SaveChangesAsync();
        try
        {
            e ??= await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(v => v.Id == x.EmployeeId);
            if (e?.SystemUserId is > 0)
                await _notify.SendAsync(e.SystemUserId.Value, "تسویه‌حساب پایان همکاری صادر شد",
                    $"تسویه‌حساب شما به مبلغ خالص {Fa.Money((decimal)x.NetPayable)} ریال نهایی شد.",
                    "حقوق و دستمزد", "FaPay", "fa-pay/my");
        }
        catch { }
        return (await GetSettlementAsync(x.Id))!;
    }

    public async Task DeleteSettlementAsync(int id)
    {
        var x = await _db.FaPaySettlements.FindAsync(id)
            ?? throw new InvalidOperationException("تسویه یافت نشد.");
        if (x.Status == FaPaySettlementStatus.Final)
            throw new InvalidOperationException("تسویه نهایی قابل حذف نیست.");
        _db.FaPaySettlements.Remove(x);
        await _db.SaveChangesAsync();
    }

    // ==================== فایل لیست بیمه ====================

    private async Task<FaPayRun> MonthlyRunForAsync(int year, int month)
    {
        JyCheck(year, month);
        return await _db.FaPayRuns
                .OrderByDescending(r => r.Status).ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(r => r.Year == year && r.Month == month && r.Kind == FaPayRunKind.Monthly)
            ?? throw new InvalidOperationException("برای این ماه دوره حقوقی ماهانه ساخته نشده است.");
    }

    public async Task<FaPayInsuranceFileCheckDto> InsuranceCheckAsync(int runId)
    {
        var run = await _db.FaPayRuns.FindAsync(runId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (run.Kind != FaPayRunKind.Monthly)
            throw new InvalidOperationException("فایل بیمه فقط از دوره ماهانه ساخته می‌شود.");
        var xs = await _db.FaPayExtraSettings.FirstOrDefaultAsync() ?? new FaPayExtraSettings();
        if (string.IsNullOrWhiteSpace(xs.WorkshopCode))
            throw new InvalidOperationException("کد کارگاه بیمه ثبت نشده است؛ از تنظیمات تکمیلی حقوق ثبت کنید.");
        var monthDays = Pc.GetDaysInMonth(run.Year, run.Month);
        var slips = await _db.FaPaySlips.AsNoTracking().Where(x => x.RunId == runId).ToListAsync();
        var emps = await _db.HrEmployees.AsNoTracking().ToDictionaryAsync(e => e.Id);
        var sids = slips.Select(x => x.Id).ToList();
        var items = sids.Count == 0 ? new List<FaPaySlipItem>()
            : await _db.FaPaySlipItems.AsNoTracking().Where(i => sids.Contains(i.SlipId)).ToListAsync();
        var fixedEarnIds = await _db.FaPayItemTypes.AsNoTracking()
            .Where(t => t.IsFixed && t.Kind == FaPayItemKind.Earning).Select(t => t.Id).ToListAsync();
        var res = new FaPayInsuranceFileCheckDto
        {
            Year = run.Year, Month = run.Month, WorkshopCode = xs.WorkshopCode, TotalCount = slips.Count
        };
        foreach (var x in slips.OrderBy(s => emps.TryGetValue(s.EmployeeId, out var e) ? e.Code : ""))
        {
            emps.TryGetValue(x.EmployeeId, out var e);
            double fe = items.Where(i => i.SlipId == x.Id && i.ItemTypeId != null
                && fixedEarnIds.Contains(i.ItemTypeId.Value)).Sum(i => i.Amount);
            double insurable = R(x.BaseSalary + fe + x.OvertimeAmount);
            int workDays = Math.Max(0, Math.Min(monthDays,
                monthDays - x.AbsentDays - (int)Math.Round(x.UnpaidLeaveDays)));
            string? problem = null;
            if (e == null) problem = "پرسنل یافت نشد";
            else if (string.IsNullOrWhiteSpace(e.InsuranceNo)) problem = "شماره بیمه ثبت نشده";
            else if (string.IsNullOrWhiteSpace(e.NationalCode) || e.NationalCode.Trim().Length != 10)
                problem = "کد ملی نامعتبر";
            else if (insurable <= 0) problem = "دستمزد مشمول صفر است";
            res.Rows.Add(new FaPayInsuranceFileRowDto
            {
                EmployeeName = e == null ? "—" : e.FirstName + " " + e.LastName,
                EmployeeCode = e?.Code ?? "",
                NationalCode = e?.NationalCode, InsuranceNo = e?.InsuranceNo,
                WorkDays = workDays, InsurableAmount = insurable, Problem = problem
            });
            if (problem == null) { res.ReadyCount++; res.TotalInsurable += insurable; }
        }
        res.TotalInsurable = R(res.TotalInsurable);
        return res;
    }

    /// <summary>
    /// فایل لیست بیمه تأمین اجتماعی (قالب متنی ثابت‌عرض + نسخه اکسل برای کنترل):
    /// سطر H: کد کارگاه/سال/ماه/تعداد/جمع مشمول — سطر D: هر بیمه‌شده.
    /// قبل از بارگذاری در سامانه، با خروجی اکسل کنترل شود.
    /// </summary>
    public async Task<(byte[] Data, string FileName, string ContentType)> InsuranceFileAsync(int runId, string format)
    {
        var chk = await InsuranceCheckAsync(runId);
        var ready = chk.Rows.Where(r => r.Problem == null).ToList();
        if (ready.Count == 0) throw new InvalidOperationException("ردیف آماده‌ای برای فایل بیمه وجود ندارد.");
        var f = (format ?? "txt").Trim().ToLowerInvariant();
        if (f != "txt")
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("لیست بیمه");
            ws.RightToLeft = true;
            var headers = new[] { "ردیف", "نام و نام خانوادگی", "کد پرسنلی", "کد ملی", "شماره بیمه", "روزهای کارکرد", "دستمزد مشمول (ریال)" };
            for (var i = 0; i < headers.Length; i++)
            {
                var c = ws.Cell(1, i + 1);
                c.Value = headers[i];
                c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
                c.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
            ws.SheetView.FreezeRows(1);
            ws.Column(4).Style.NumberFormat.Format = "@";
            ws.Column(5).Style.NumberFormat.Format = "@";
            var row = 2;
            foreach (var r in ready)
            {
                ws.Cell(row, 1).Value = row - 1;
                ws.Cell(row, 2).Value = r.EmployeeName;
                ws.Cell(row, 3).Value = r.EmployeeCode;
                ws.Cell(row, 4).Value = r.NationalCode ?? "";
                ws.Cell(row, 5).Value = r.InsuranceNo ?? "";
                ws.Cell(row, 6).Value = r.WorkDays;
                ws.Cell(row, 7).Value = r.InsurableAmount;
                ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
                row++;
            }
            ws.Cell(row, 6).Value = "جمع";
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 7).Value = chk.TotalInsurable;
            ws.Cell(row, 7).Style.Font.Bold = true;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
            ws.Columns().AdjustToContents();
            var ws0 = wb.Worksheets.Add("مشخصات");
            ws0.RightToLeft = true;
            ws0.Cell(1, 1).Value = "کد کارگاه"; ws0.Cell(1, 2).Value = chk.WorkshopCode ?? "";
            ws0.Cell(2, 1).Value = "دوره"; ws0.Cell(2, 2).Value = $"{JmName(chk.Month)} {chk.Year}";
            ws0.Cell(3, 1).Value = "تعداد"; ws0.Cell(3, 2).Value = ready.Count;
            ws0.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return (ms.ToArray(), $"Insurance-{chk.Year}-{chk.Month:00}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
        static string L(string? v, int len)
        {
            v ??= "";
            if (v.Length > len) v = v[..len];
            return v.PadRight(len);
        }
        static string Z(long v, int len)
        {
            var s = Math.Max(0, v).ToString();
            if (s.Length > len) s = s[^len..];
            return s.PadLeft(len, '0');
        }
        var sb = new StringBuilder();
        sb.Append('H').Append(L(chk.WorkshopCode, 14)).Append(chk.Year.ToString("0000"))
            .Append(chk.Month.ToString("00")).Append(Z(ready.Count, 6))
            .Append(Z((long)Math.Round(chk.TotalInsurable), 15)).AppendLine();
        var parts = new char[] { ' ' };
        foreach (var r in ready)
        {
            var nm = (r.EmployeeName ?? "").Split(parts, StringSplitOptions.RemoveEmptyEntries);
            var first = nm.Length > 0 ? nm[0] : "";
            var last = nm.Length > 1 ? string.Join(" ", nm.Skip(1)) : "";
            var ins = new string((r.InsuranceNo ?? "").Where(char.IsDigit).ToArray());
            var nat = new string((r.NationalCode ?? "").Where(char.IsDigit).ToArray());
            sb.Append('D')
                .Append(Z(long.TryParse(ins, out var iv) ? iv : 0, 10))
                .Append(nat.PadLeft(10, '0')[^10..])
                .Append(L(first, 25)).Append(L(last, 35))
                .Append(Z(r.WorkDays, 2))
                .Append(Z((long)Math.Round(r.InsurableAmount), 13))
                .AppendLine();
        }
        return (new UTF8Encoding(false).GetBytes(sb.ToString()),
            $"Insurance-{chk.Year}-{chk.Month:00}.txt", "text/plain");
    }

    // ==================== مقایسه دوره‌ها ====================

    public async Task<FaPayCompareDto> CompareRunsAsync(int runAId, int runBId)
    {
        if (runAId == runBId) throw new InvalidOperationException("دو دوره متفاوت انتخاب کنید.");
        var ra = await _db.FaPayRuns.FindAsync(runAId)
            ?? throw new InvalidOperationException("دوره اول یافت نشد.");
        var rb = await _db.FaPayRuns.FindAsync(runBId)
            ?? throw new InvalidOperationException("دوره دوم یافت نشد.");
        string Lbl(FaPayRun r) => r.Kind == FaPayRunKind.YearEnd
            ? $"عیدی و سنوات {Fa.Digits(r.Year.ToString())}"
            : $"{JmName(r.Month)} {Fa.Digits(r.Year.ToString())}";
        var sa = await _db.FaPaySlips.AsNoTracking().Where(s => s.RunId == runAId)
            .ToDictionaryAsync(s => s.EmployeeId);
        var sb = await _db.FaPaySlips.AsNoTracking().Where(s => s.RunId == runBId)
            .ToDictionaryAsync(s => s.EmployeeId);
        var empIds = sa.Keys.Union(sb.Keys).ToList();
        var emps = await _db.HrEmployees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { N = e.FirstName + " " + e.LastName, e.Code });
        var res = new FaPayCompareDto
        {
            RunAId = runAId, RunBId = runBId, LabelA = Lbl(ra), LabelB = Lbl(rb),
            TotalA = R(sa.Values.Sum(s => s.NetPay)), TotalB = R(sb.Values.Sum(s => s.NetPay))
        };
        foreach (var id in empIds.OrderBy(i => emps.TryGetValue(i, out var e) ? e.Code : ""))
        {
            sa.TryGetValue(id, out var a);
            sb.TryGetValue(id, out var b);
            emps.TryGetValue(id, out var nm);
            var row = new FaPayCompareRowDto
            {
                EmployeeId = id, EmployeeName = nm?.N, EmployeeCode = nm?.Code,
                NetA = a?.NetPay, NetB = b?.NetPay
            };
            if (a == null) row.Flag = "New";
            else if (b == null) row.Flag = "Left";
            else
            {
                row.Diff = R(b.NetPay - a.NetPay);
                row.DiffPercent = a.NetPay != 0 ? Math.Round(row.Diff.Value / a.NetPay * 100, 1) : null;
                row.Flag = row.Diff == 0 ? "Same" : "Changed";
            }
            res.Rows.Add(row);
        }
        return res;
    }

    // ==================== PDF تسویه ====================

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
                    QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("FaPayX", File.OpenRead(reg));
                if (File.Exists(bold))
                    QuestPDF.Drawing.FontManager.RegisterFontWithCustomName("FaPayX-Bold", File.OpenRead(bold));
            }
            catch { }
            _fontsOk = true;
        }
    }

    private static string PNum(double v) => Fa.Digits(v.ToString("#,0"));

    private static string ReasonName(int r) => r switch
    {
        0 => "استعفا", 1 => "اخراج/فسخ", 2 => "پایان قرارداد",
        3 => "بازنشستگی", 4 => "توافق طرفین", _ => "سایر"
    };

    public async Task<byte[]> SettlementPdfAsync(int id)
    {
        var d = await GetSettlementAsync(id) ?? throw new InvalidOperationException("تسویه یافت نشد.");
        EnsureFonts();
        var jy = Pc.GetYear(d.LeaveDate); var jm = Pc.GetMonth(d.LeaveDate); var jd = Pc.GetDayOfMonth(d.LeaveDate);
        var doc = Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(30);
                p.ContentFromRightToLeft();
                p.DefaultTextStyle(x => x.FontFamily("FaPayX").FontSize(10));
                p.Header().Column(col =>
                {
                    col.Item().Text("تسویه‌حساب پایان همکاری").FontFamily("FaPayX-Bold").FontSize(16).AlignCenter();
                    col.Item().Text($"علت: {ReasonName(d.Reason)} — تاریخ پایان همکاری: {Fa.Digits($"{jy}/{jm:00}/{jd:00}")}")
                        .FontSize(10).AlignCenter();
                    col.Item().PaddingTop(4).LineHorizontal(1);
                    if (d.Status == 0)
                        col.Item().PaddingTop(2).Text("پیش‌نویس — غیرقابل استناد")
                            .FontColor(Colors.Red.Medium).FontSize(9).AlignCenter();
                });
                p.Content().Column(col =>
                {
                    col.Item().PaddingTop(8).Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(3); cd.RelativeColumn(2); cd.RelativeColumn(3); });
                        InfoCell(t, "نام و نام خانوادگی", d.EmployeeName ?? "—");
                        InfoCell(t, "کد پرسنلی", d.EmployeeCode ?? "—");
                        InfoCell(t, "کد ملی", d.NationalCode ?? "—");
                        InfoCell(t, "سابقه خدمت (روز)", Fa.Digits(d.ServiceDays.ToString()));
                        InfoCell(t, "آخرین حقوق مبنا (ریال)", PNum(d.BaseSalary));
                        InfoCell(t, "وضعیت", d.Status == 1 ? "نهایی" : "پیش‌نویس");
                    });
                    col.Item().PaddingTop(10).Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(4); cd.RelativeColumn(3); });
                        HeadRow(t, "شرح مطالبات", "مبلغ (ریال)");
                        BodyRow(t, "سنوات خدمت (معاف از مالیات)", PNum(d.SenavatAmount));
                        BodyRow(t, "عیدی تناسبی", PNum(d.EidiAmount));
                        BodyRow(t, $"بازخرید مرخصی ({Fa.Digits(d.LeaveBuybackDays.ToString("0.#"))} روز)", PNum(d.LeaveBuybackAmount));
                        if (d.OtherEarnings != 0)
                            BodyRow(t, "سایر مطالبات" + (string.IsNullOrWhiteSpace(d.OtherEarningsNote) ? "" : $" ({d.OtherEarningsNote})"), PNum(d.OtherEarnings));
                        BodyRow(t, "جمع مطالبات", PNum(d.GrossTotal), true);
                        HeadRow(t, "شرح کسور", "مبلغ (ریال)");
                        BodyRow(t, "مانده وام‌ها", PNum(d.LoanRemaining));
                        if (d.OtherDeductions != 0)
                            BodyRow(t, "سایر کسور" + (string.IsNullOrWhiteSpace(d.OtherDeductionsNote) ? "" : $" ({d.OtherDeductionsNote})"), PNum(d.OtherDeductions));
                        BodyRow(t, "مالیات تسویه", PNum(d.TaxAmount));
                    });
                    col.Item().PaddingTop(8).Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(1); cd.RelativeColumn(1); });
                        t.Cell().Element(NetCell).Text("خالص قابل پرداخت (ریال)").FontFamily("FaPayX-Bold").AlignCenter();
                        t.Cell().Element(NetCell).Text(PNum(d.NetPayable)).FontFamily("FaPayX-Bold").FontSize(13).AlignCenter();
                    });
                    col.Item().PaddingTop(24).Row(row =>
                    {
                        row.Spacing(12);
                        row.RelativeItem().Text("امضای کارمند:\n\n........................").AlignCenter();
                        row.RelativeItem().Text("منابع انسانی:\n\n........................").AlignCenter();
                        row.RelativeItem().Text("امور مالی:\n\n........................").AlignCenter();
                    });
                });
                p.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(1);
                    col.Item().Text("این سند در دو نسخه تنظیم و پس از امضا معتبر است.")
                        .FontSize(8).FontColor(Colors.Grey.Darken1).AlignCenter();
                });
            });
        });
        return doc.GeneratePdf();
    }

    private static void InfoCell(TableDescriptor t, string label, string value)
    {
        t.Cell().Element(LabCell).Text(label).FontSize(8).FontColor(Colors.Grey.Darken2);
        t.Cell().Element(ValCell).Text(value).FontFamily("FaPayX-Bold");
    }

    private static void HeadRow(TableDescriptor t, string a, string b)
    {
        t.Cell().Element(HdCell).Text(a).FontFamily("FaPayX-Bold").AlignCenter();
        t.Cell().Element(HdCell).Text(b).FontFamily("FaPayX-Bold").AlignCenter();
    }

    private static void BodyRow(TableDescriptor t, string a, string b, bool bold = false)
    {
        var t1 = t.Cell().Element(ValCell).Text(a);
        var t2 = t.Cell().Element(ValCell).Text(b).AlignLeft();
        if (bold) { t1.FontFamily("FaPayX-Bold"); t2.FontFamily("FaPayX-Bold"); }
    }

    private static IContainer LabCell(IContainer c)
        => c.Background(Colors.Grey.Lighten4).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(4);
    private static IContainer ValCell(IContainer c)
        => c.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(4);
    private static IContainer HdCell(IContainer c)
        => c.Background("#E8E3FF").Border(1).BorderColor("#D8D4EE").Padding(4);
    private static IContainer NetCell(IContainer c)
        => c.Background("#E9F8F0").Border(1).BorderColor(Colors.Green.Lighten2).Padding(6);
}
