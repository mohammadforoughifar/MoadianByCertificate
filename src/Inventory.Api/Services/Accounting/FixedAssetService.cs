using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services.Accounting;

/// <summary>
/// سرویس دارایی ثابت: گروه‌بندی، ثبت اموال، اجرای استهلاک ماهانه (مستقیم/نزولی)
/// و صدور خودکار سند حسابداری استهلاک.
/// </summary>
public interface IFixedAssetService
{
    Task<List<FixedAssetCategory>> GetCategoriesAsync(Paging.Request? pagination = null);
    Task<FixedAssetCategory> SaveCategoryAsync(FixedAssetCategory dto);
    Task DeleteCategoryAsync(int id);

    Task<List<FixedAsset>> GetAssetsAsync(FixedAssetStatus? status = null, int? categoryId = null, string? search = null, Paging.Request? pagination = null);
    Task<FixedAsset> GetAssetAsync(int id);
    Task<FixedAsset> SaveAssetAsync(FixedAsset dto);
    Task DeleteAssetAsync(int id);

    Task<List<FixedAssetDepreciationRun>> GetRunsAsync(Paging.Request? pagination = null);
    Task<FixedAssetDepreciationRun> RunDepreciationAsync(FixedAssetDepreciationRequest req, string? user);
    Task DeleteRunAsync(int id);
    Task<FixedAssetDepreciationRun?> PostRunToAccountingAsync(int runId, string? user);
}

public class FixedAssetService : IFixedAssetService
{
    private readonly Db.AppDbContext _db;
    public FixedAssetService(Db.AppDbContext db) => _db = db;

    // =====================================================================
    // گروه‌ها
    // =====================================================================
    public async Task<List<FixedAssetCategory>> GetCategoriesAsync(Paging.Request? pagination = null)
    {
        var cats = await _db.FixedAssetCategories.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Code).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var counts = await _db.FixedAssets.AsNoTracking()
            .GroupBy(a => a.CategoryId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);
        return cats.Select(c => new FixedAssetCategory
        {
            Id = c.Id, Code = c.Code, Name = c.Name,
            DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths,
            DefaultMethod = c.DefaultMethod, DefaultResidualPercent = c.DefaultResidualPercent,
            DepreciationAccAccountId = c.DepreciationAccAccountId,
            ExpenseAccAccountId = c.ExpenseAccAccountId,
            AccumulatedAccAccountId = c.AccumulatedAccAccountId,
            IsActive = c.IsActive, SortOrder = c.SortOrder, Description = c.Description,
            AssetCount = counts.TryGetValue(c.Id, out var n) ? n : 0
        }).ToList();
    }

    public async Task<FixedAssetCategory> SaveCategoryAsync(FixedAssetCategory dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("کد و نام گروه الزامی است.");
        if (dto.DefaultUsefulLifeMonths <= 0) dto.DefaultUsefulLifeMonths = 60;

        var dup = await _db.FixedAssetCategories.AnyAsync(c => c.Code == dto.Code && c.Id != dto.Id);
        if (dup) throw new InvalidOperationException("گروهی با این کد قبلاً ثبت شده است.");

        Db.FixedAssetCategory entity;
        if (dto.Id == 0)
        {
            entity = new Db.FixedAssetCategory();
            _db.FixedAssetCategories.Add(entity);
        }
        else
        {
            entity = await _db.FixedAssetCategories.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("گروه یافت نشد.");
        }

        entity.Code = dto.Code.Trim();
        entity.Name = dto.Name.Trim();
        entity.DefaultUsefulLifeMonths = dto.DefaultUsefulLifeMonths;
        entity.DefaultMethod = dto.DefaultMethod;
        entity.DefaultResidualPercent = Math.Clamp(dto.DefaultResidualPercent, 0, 20);
        entity.DepreciationAccAccountId = dto.DepreciationAccAccountId;
        entity.ExpenseAccAccountId = dto.ExpenseAccAccountId;
        entity.AccumulatedAccAccountId = dto.AccumulatedAccAccountId;
        entity.IsActive = dto.IsActive;
        entity.SortOrder = dto.SortOrder;
        entity.Description = dto.Description;
        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        return dto;
    }

    public async Task DeleteCategoryAsync(int id)
    {
        var entity = await _db.FixedAssetCategories.FindAsync(id)
                     ?? throw new InvalidOperationException("گروه یافت نشد.");
        var inUse = await _db.FixedAssets.AnyAsync(a => a.CategoryId == id);
        if (inUse)
            throw new InvalidOperationException("این گروه دارای دارایی است و قابل حذف نیست.");
        _db.FixedAssetCategories.Remove(entity);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    // دارایی‌ها
    // =====================================================================
    public async Task<List<FixedAsset>> GetAssetsAsync(FixedAssetStatus? status = null, int? categoryId = null, string? search = null, Paging.Request? pagination = null)
    {
        var q = _db.FixedAssets.AsNoTracking().Include(a => a.Category).Include(a => a.DimensionValue)
            .AsQueryable();
        if (status is not null) q = q.Where(a => a.Status == status);
        if (categoryId is not null) q = q.Where(a => a.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(a => a.Code.Contains(search) || a.Name.Contains(search) || (a.SerialNo != null && a.SerialNo.Contains(search)));

        var list = await q.OrderByDescending(a => a.Id).ToPageListAsync(pagination);
        return list.Select(ToAssetDto).ToList();
    }

    public async Task<FixedAsset> GetAssetAsync(int id)
    {
        var entity = await _db.FixedAssets.AsNoTracking().Include(a => a.Category).Include(a => a.DimensionValue)
            .FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new InvalidOperationException("دارایی یافت نشد.");
        return ToAssetDto(entity);
    }

    public async Task<FixedAsset> SaveAssetAsync(FixedAsset dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("کد و نام دارایی الزامی است.");
        if (dto.CategoryId == 0)
            throw new InvalidOperationException("گروه دارایی را انتخاب کنید.");
        if (dto.UsefulLifeMonths <= 0)
            throw new InvalidOperationException("عمر مفید باید بزرگ‌تر از صفر باشد.");
        if (dto.SalvageValue < 0 || dto.SalvageValue >= dto.PurchasePrice)
            throw new InvalidOperationException("ارزش اسقاط باید کمتر از قیمت خرید و غیرمنفی باشد.");

        var dup = await _db.FixedAssets.AnyAsync(a => a.Code == dto.Code && a.Id != dto.Id);
        if (dup) throw new InvalidOperationException("دارایی با این کد قبلاً ثبت شده است.");

        Db.FixedAsset entity;
        if (dto.Id == 0)
        {
            entity = new Db.FixedAsset();
            _db.FixedAssets.Add(entity);
        }
        else
        {
            entity = await _db.FixedAssets.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("دارایی یافت نشد.");
        }

        entity.Code = dto.Code.Trim();
        entity.Name = dto.Name.Trim();
        entity.EnName = dto.EnName;
        entity.CategoryId = dto.CategoryId;
        entity.Location = dto.Location;
        entity.Vendor = dto.Vendor;
        entity.SerialNo = dto.SerialNo;
        entity.PurchaseDate = dto.PurchaseDate;
        entity.PurchasePrice = dto.PurchasePrice;
        entity.SalvageValue = dto.SalvageValue;
        entity.UsefulLifeMonths = dto.UsefulLifeMonths;
        entity.DepreciationMethod = dto.DepreciationMethod;
        entity.Status = dto.Status;
        entity.DimensionValueId = dto.DimensionValueId;
        entity.AssetAccAccountId = dto.AssetAccAccountId;
        entity.ExpenseAccAccountId = dto.ExpenseAccAccountId;
        entity.AccumulatedAccAccountId = dto.AccumulatedAccAccountId;
        entity.IsActive = dto.IsActive;
        entity.Notes = dto.Notes;

        if (entity.Id == 0)
        {
            entity.AccumulatedDepreciation = 0;
            // پیش‌فرض حساب‌ها از گروه
            var cat = await _db.FixedAssetCategories.FindAsync(dto.CategoryId);
            if (cat is not null)
            {
                entity.AssetAccAccountId ??= cat.DepreciationAccAccountId;
                entity.ExpenseAccAccountId ??= cat.ExpenseAccAccountId;
                entity.AccumulatedAccAccountId ??= cat.AccumulatedAccAccountId;
            }
        }

        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        dto.AccumulatedDepreciation = entity.AccumulatedDepreciation;
        return dto;
    }

    public async Task DeleteAssetAsync(int id)
    {
        var entity = await _db.FixedAssets.FindAsync(id)
                     ?? throw new InvalidOperationException("دارایی یافت نشد.");
        var hasLines = await _db.FixedAssetDepreciationLines.AnyAsync(l => l.AssetId == id);
        if (hasLines)
            throw new InvalidOperationException("این دارایی دارای ردیف استهلاک است و قابل حذف نیست (می‌توانید وضعیت را «امحاء» کنید).");
        _db.FixedAssets.Remove(entity);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    // استهلاک
    // =====================================================================
    public async Task<List<FixedAssetDepreciationRun>> GetRunsAsync(Paging.Request? pagination = null)
        => await _db.FixedAssetDepreciationRuns.AsNoTracking()
            .Include(r => r.Lines).ThenInclude(l => l.Asset)
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenBy(x => x.Id)
            .Select(r => new FixedAssetDepreciationRun
            {
                Id = r.Id, Year = r.Year, Month = r.Month, RunDate = r.RunDate,
                TotalAmount = r.TotalAmount, VoucherId = r.VoucherId, IsPosted = r.IsPosted,
                CreatedBy = r.CreatedBy, Notes = r.Notes,
                Lines = r.Lines.Select(l => new FixedAssetDepreciationLine
                {
                    Id = l.Id, AssetId = l.AssetId,
                    AssetCode = l.Asset != null ? l.Asset.Code : null,
                    AssetName = l.Asset != null ? l.Asset.Name : null,
                    Amount = l.Amount, AccumulatedAfter = l.AccumulatedAfter,
                    BookValueAfter = l.BookValueAfter
                }).ToList()
            })
            .ToPageListAsync(pagination);

    public async Task<FixedAssetDepreciationRun> RunDepreciationAsync(FixedAssetDepreciationRequest req, string? user)
    {
        if (req.Year < 1300 || req.Month is < 1 or > 12)
            throw new InvalidOperationException("سال/ماه معتبر نیست.");

        var dup = await _db.FixedAssetDepreciationRuns.AnyAsync(r => r.Year == req.Year && r.Month == req.Month);
        if (dup)
            throw new InvalidOperationException("استهلاک این دوره قبلاً اجرا شده است. ابتدا اجرای قبلی را حذف کنید.");

        var assets = await _db.FixedAssets
            .Include(a => a.Category)
            .Where(a => a.Status == FixedAssetStatus.Active && a.PurchaseDate <= NewDate(req.Year, req.Month, 31))
            .ToListAsync();

        var run = new Db.FixedAssetDepreciationRun
        {
            Year = req.Year, Month = req.Month, CreatedBy = user, Notes = req.Notes
        };

        decimal total = 0;
        foreach (var a in assets)
        {
            var monthsPast = MonthsBetween(a.PurchaseDate, NewDate(req.Year, req.Month, 1));
            if (monthsPast <= 0) continue;
            if (monthsPast > a.UsefulLifeMonths) monthsPast = a.UsefulLifeMonths;

            var current = a.AccumulatedDepreciation;
            var depreciationBase = a.PurchasePrice - a.SalvageValue;
            decimal thisMonth;

            if (a.DepreciationMethod == DepreciationMethod.StraightLine)
            {
                thisMonth = Math.Round(depreciationBase / a.UsefulLifeMonths, 2);
                // در ماه آخر، استهلاک به‌اندازه‌ی باقیمانده است تا از ارزش اسقاط پایین‌تر نرود
                var remaining = depreciationBase - current;
                if (thisMonth > remaining) thisMonth = remaining;
            }
            else // نزولی — نرخ = ۲ ÷ عمر مفید
            {
                var rate = 2m / a.UsefulLifeMonths;
                thisMonth = Math.Round((a.PurchasePrice - current) * rate, 2);
                var remaining = depreciationBase - current;
                if (thisMonth > remaining) thisMonth = remaining;
            }

            if (thisMonth <= 0) continue;

            var after = current + thisMonth;
            run.Lines.Add(new Db.FixedAssetDepreciationLine
            {
                AssetId = a.Id, Amount = thisMonth,
                AccumulatedAfter = after,
                BookValueAfter = a.PurchasePrice - after
            });
            a.AccumulatedDepreciation = after;
            total += thisMonth;
        }

        if (run.Lines.Count == 0)
            throw new InvalidOperationException("هیچ دارایی فعالی برای استهلاک در این دوره وجود ندارد.");

        run.TotalAmount = Math.Round(total, 2);
        _db.FixedAssetDepreciationRuns.Add(run);
        await _db.SaveChangesAsync();

        return (await GetRunsAsync()).First(r => r.Id == run.Id);
    }

    public async Task DeleteRunAsync(int id)
    {
        var run = await _db.FixedAssetDepreciationRuns.Include(r => r.Lines)
                     .FirstOrDefaultAsync(r => r.Id == id)
                 ?? throw new InvalidOperationException("اجرای استهلاک یافت نشد.");
        if (run.IsPosted)
            throw new InvalidOperationException("اجرای پست‌شده قابل حذف نیست؛ ابتدا سند حسابداری آن را ابطال کنید.");

        // برگرداندن استهلاک انباشته‌ی دارایی‌ها
        foreach (var line in run.Lines)
        {
            var asset = await _db.FixedAssets.FindAsync(line.AssetId);
            if (asset is not null)
                asset.AccumulatedDepreciation = Math.Max(0, asset.AccumulatedDepreciation - line.Amount);
        }

        _db.FixedAssetDepreciationLines.RemoveRange(run.Lines);
        _db.FixedAssetDepreciationRuns.Remove(run);
        await _db.SaveChangesAsync();
    }

    public async Task<FixedAssetDepreciationRun?> PostRunToAccountingAsync(int runId, string? user)
    {
        var run = await _db.FixedAssetDepreciationRuns
            .Include(r => r.Lines).ThenInclude(l => l.Asset)
            .FirstOrDefaultAsync(r => r.Id == runId)
            ?? throw new InvalidOperationException("اجرای استهلاک یافت نشد.");
        if (run.IsPosted)
            throw new InvalidOperationException("این اجرا قبلاً به حسابداری ارسال شده است.");
        if (run.Lines.Count == 0)
            throw new InvalidOperationException("اجرای استهلاک سطری ندارد.");

        var year = await _db.AccFiscalYears.FirstOrDefaultAsync(f => f.IsCurrent && !f.IsClosed)
                   ?? throw new InvalidOperationException("سال مالی جاریِ باز یافت نشد. ابتدا سال مالی را تنظیم کنید.");

        // جمع‌بندی هر حساب هزینه / ذخیره
        var byAccount = new Dictionary<(int? Expense, int? Acc), (int? Dim, decimal Amt)>();
        foreach (var l in run.Lines)
        {
            var asset = l.Asset!;
            var exp = asset.ExpenseAccAccountId;
            var acc = asset.AccumulatedAccAccountId;
            if (exp is null || acc is null)
                throw new InvalidOperationException(
                    $"حساب هزینه/ذخیره استهلاک دارایی «{asset.Code}» تنظیم نشده است. ابتدا در فرم دارایی، حساب‌ها را مشخص کنید.");

            var key = (exp, acc);
            if (byAccount.TryGetValue(key, out var cur))
                byAccount[key] = (cur.Dim ?? asset.DimensionValueId, cur.Amt + l.Amount);
            else
                byAccount[key] = (asset.DimensionValueId, l.Amount);
        }

        // شماره سند جدید در سال جاری
        var nextNo = await _db.AccVouchers
            .Where(v => v.FiscalYearId == year.Id)
            .MaxAsync(v => (int?)v.Number) ?? 0;

        var voucher = new Db.AccVoucher
        {
            FiscalYearId = year.Id, Number = nextNo + 1,
            Date = NewDate(run.Year, run.Month, 1),
            Description = $"استهلاک دارایی‌های ثابت — {run.Year}/{run.Month:00}",
            Status = VoucherStatus.Confirmed,
            Source = VoucherSource.Manual,
            SourceId = run.Id, SourceTitle = $"استهلاک {run.Year}/{run.Month:00}",
            CreatedBy = user, CreatedAt = DateTime.Now,
            ConfirmedBy = user, ConfirmedAt = DateTime.Now
        };

        int row = 1;
        decimal totalDebit = 0, totalCredit = 0;
        foreach (var kv in byAccount)
        {
            var (expenseAcc, accAcc) = kv.Key;
            var (dim, amt) = kv.Value;
            var expenseId = expenseAcc!.Value;
            var accumId = accAcc!.Value;

            voucher.Lines.Add(new Db.AccVoucherLine
            {
                RowNo = row++, AccountId = expenseId,
                DimensionValueId = dim, Debit = Math.Round(amt, 2), Credit = 0,
                Description = $"هزینه استهلاک {run.Year}/{run.Month:00}"
            });
            voucher.Lines.Add(new Db.AccVoucherLine
            {
                RowNo = row++, AccountId = accumId,
                DimensionValueId = dim, Debit = 0, Credit = Math.Round(amt, 2),
                Description = $"ذخیره استهلاک {run.Year}/{run.Month:00}"
            });
            totalDebit += amt; totalCredit += amt;
        }

        voucher.TotalDebit = Math.Round(totalDebit, 2);
        voucher.TotalCredit = Math.Round(totalCredit, 2);

        _db.AccVouchers.Add(voucher);
        await _db.SaveChangesAsync();
        run.VoucherId = voucher.Id;
        run.IsPosted = true;
        await _db.SaveChangesAsync();

        var result = await _db.FixedAssetDepreciationRuns.AsNoTracking()
            .Include(r => r.Lines).ThenInclude(l => l.Asset)
            .FirstOrDefaultAsync(r => r.Id == run.Id);
        return ToRunDto(result!);
    }

    // =====================================================================
    private static FixedAsset ToAssetDto(Db.FixedAsset a) => new()
    {
        Id = a.Id, Code = a.Code, Name = a.Name, EnName = a.EnName,
        CategoryId = a.CategoryId, CategoryName = a.Category?.Name,
        Location = a.Location, Vendor = a.Vendor, SerialNo = a.SerialNo,
        PurchaseDate = a.PurchaseDate, PurchasePrice = a.PurchasePrice,
        SalvageValue = a.SalvageValue, UsefulLifeMonths = a.UsefulLifeMonths,
        DepreciationMethod = a.DepreciationMethod, Status = a.Status,
        DimensionValueId = a.DimensionValueId,
        DimensionValueName = a.DimensionValue != null
            ? $"{a.DimensionValue.Code} — {a.DimensionValue.Name}" : null,
        AssetAccAccountId = a.AssetAccAccountId,
        ExpenseAccAccountId = a.ExpenseAccAccountId,
        AccumulatedAccAccountId = a.AccumulatedAccAccountId,
        AccumulatedDepreciation = a.AccumulatedDepreciation,
        IsActive = a.IsActive, Notes = a.Notes, CreatedAt = a.CreatedAt
    };

    private static FixedAssetDepreciationRun ToRunDto(Db.FixedAssetDepreciationRun r) => new()
    {
        Id = r.Id, Year = r.Year, Month = r.Month, RunDate = r.RunDate,
        TotalAmount = r.TotalAmount, VoucherId = r.VoucherId, IsPosted = r.IsPosted,
        CreatedBy = r.CreatedBy, Notes = r.Notes,
        Lines = r.Lines.Select(l => new FixedAssetDepreciationLine
        {
            Id = l.Id, AssetId = l.AssetId,
            AssetCode = l.Asset?.Code, AssetName = l.Asset?.Name,
            Amount = l.Amount, AccumulatedAfter = l.AccumulatedAfter,
            BookValueAfter = l.BookValueAfter
        }).ToList()
    };

    // =====================================================================
    private static DateTime NewDate(int year, int month, int day)
    {
        try { return PersianDate.ToGregorian(year, month, Math.Min(day, PersianDate.DaysInMonth(year, month))); }
        catch { return new DateTime(year, 1, 1); }
    }

    private static int MonthsBetween(DateTime from, DateTime to)
        => Math.Max(0, ((to.Year - from.Year) * 12) + (to.Month - from.Month));
}
