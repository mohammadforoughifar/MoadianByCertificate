using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services.Accounting;

/// <summary>
/// سرویس بودجه و کنترل بودجه:
///   • بودجه سالانه برای یک بُعد تحلیلی (مرکز هزینه/پروژه/شعبه)
///   • اقلام بودجه روی حساب‌های هزینه/درآمد
///   • ثبت تعهد (سفارش/قرارداد) و مصرف (سند حسابداری) و پایش سقف
/// </summary>
public interface IBudgetService
{
    Task<List<Budget>> GetBudgetsAsync(bool activeOnly = false, Paging.Request? pagination = null);
    Task<Budget> GetBudgetAsync(int id);
    Task<Budget> SaveBudgetAsync(Budget dto);
    Task DeleteBudgetAsync(int id);

    Task<List<BudgetTransaction>> GetTransactionsAsync(int budgetId, int? budgetItemId = null, Paging.Request? pagination = null);
    Task<BudgetTransaction> AddTransactionAsync(BudgetTransaction dto);
    Task DeleteTransactionAsync(int id);

    Task<BudgetDashboard> GetDashboardAsync();
}

public class BudgetService : IBudgetService
{
    private readonly Db.AppDbContext _db;
    public BudgetService(Db.AppDbContext db) => _db = db;

    // =====================================================================
    public async Task<List<Budget>> GetBudgetsAsync(bool activeOnly = false, Paging.Request? pagination = null)
    {
        var q = _db.Budgets.AsNoTracking()
            .Include(b => b.FiscalYear).Include(b => b.DimensionValue)
            .Include(b => b.Items).ThenInclude(i => i.AccAccount)
            .AsQueryable();
        if (activeOnly) q = q.Where(b => b.IsActive);

        var list = await q.OrderByDescending(b => b.Id).ToPageListAsync(pagination);
        return list.Select(b => ToDto(b, includeItems: true)).ToList();
    }

    public async Task<Budget> GetBudgetAsync(int id)
    {
        var b = await _db.Budgets.AsNoTracking()
            .Include(x => x.FiscalYear).Include(x => x.DimensionValue)
            .Include(x => x.Items).ThenInclude(i => i.AccAccount)
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("بودجه یافت نشد.");
        return ToDto(b, includeItems: true);
    }

    public async Task<Budget> SaveBudgetAsync(Budget dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام بودجه الزامی است.");
        if (dto.FiscalYearId == 0)
            throw new InvalidOperationException("سال مالی بودجه را انتخاب کنید.");

        Db.Budget entity;
        if (dto.Id == 0)
        {
            entity = new Db.Budget();
            _db.Budgets.Add(entity);
        }
        else
        {
            entity = await _db.Budgets.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == dto.Id)
                     ?? throw new InvalidOperationException("بودجه یافت نشد.");
        }

        entity.FiscalYearId = dto.FiscalYearId;
        entity.DimensionValueId = dto.DimensionValueId;
        entity.Name = dto.Name.Trim();
        entity.IsMaster = dto.IsMaster;
        entity.IsActive = dto.IsActive;
        entity.Description = dto.Description;

        // اقلام: همگام‌سازی (upsert)
        var incoming = dto.Items.Where(i => i.AccAccountId > 0).ToList();
        if (incoming.Count == 0)
            throw new InvalidOperationException("حداقل یک قلم بودجه با حساب مشخص اضافه کنید.");

        foreach (var item in incoming)
        {
            var existing = entity.Items.FirstOrDefault(i => i.AccAccountId == item.AccAccountId);
            if (existing is null)
            {
                existing = new Db.BudgetItem { BudgetId = entity.Id, AccAccountId = item.AccAccountId };
                entity.Items.Add(existing);
            }
            existing.PlannedAmount = item.PlannedAmount;
        }
        // حذف اقلامی که دیگر نیستند (فقط اگر مصرف/تعهد ندارند)
        var keep = incoming.Select(i => i.AccAccountId).ToHashSet();
        foreach (var gone in entity.Items.Where(i => !keep.Contains(i.AccAccountId)).ToList())
        {
            if (gone.CommittedAmount > 0 || gone.ActualAmount > 0)
                throw new InvalidOperationException(
                    $"قلم حساب {gone.AccAccountId} دارای تعهد/مصرف است و قابل حذف نیست.");
            entity.Items.Remove(gone);
        }

        entity.TotalAmount = entity.Items.Sum(i => i.PlannedAmount);
        await _db.SaveChangesAsync();
        return await GetBudgetAsync(entity.Id);
    }

    public async Task DeleteBudgetAsync(int id)
    {
        var entity = await _db.Budgets.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == id)
                     ?? throw new InvalidOperationException("بودجه یافت نشد.");
        var hasTx = await _db.BudgetTransactions.AnyAsync(t => t.BudgetId == id);
        if (hasTx)
            throw new InvalidOperationException("این بودجه دارای رویداد (تعهد/مصرف) است و قابل حذف نیست.");
        _db.BudgetItems.RemoveRange(entity.Items);
        _db.Budgets.Remove(entity);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    public async Task<List<BudgetTransaction>> GetTransactionsAsync(int budgetId, int? budgetItemId = null, Paging.Request? pagination = null)
    {
        var q = _db.BudgetTransactions.AsNoTracking()
            .Include(t => t.Budget).Include(t => t.BudgetItem)
            .Where(t => t.BudgetId == budgetId);
        if (budgetItemId is not null) q = q.Where(t => t.BudgetItemId == budgetItemId);
        return await q.OrderByDescending(t => t.Id)
            .Select(t => new BudgetTransaction
            {
                Id = t.Id, BudgetId = t.BudgetId, BudgetName = t.Budget != null ? t.Budget.Name : null,
                BudgetItemId = t.BudgetItemId, Type = t.Type, VoucherId = t.VoucherId,
                SourceId = t.SourceId, SourceTitle = t.SourceTitle, Amount = t.Amount,
                Date = t.Date, Description = t.Description
            })
            .ToPageListAsync(pagination);
    }

    public async Task<BudgetTransaction> AddTransactionAsync(BudgetTransaction dto)
    {
        if (dto.BudgetId == 0 || dto.BudgetItemId == 0 || dto.Amount <= 0)
            throw new InvalidOperationException("بودجه، قلم و مبلغ معتبر الزامی است.");

        var item = await _db.BudgetItems.Include(i => i.Budget)
            .FirstOrDefaultAsync(i => i.Id == dto.BudgetItemId && i.BudgetId == dto.BudgetId)
            ?? throw new InvalidOperationException("قلم بودجه یافت نشد.");

        // کنترل سقف فقط برای مصرف/تعهد
        if (dto.Type == BudgetTransactionType.Actual || dto.Type == BudgetTransactionType.Commitment)
        {
            var used = item.CommittedAmount + item.ActualAmount;
            if (dto.Type == BudgetTransactionType.Commitment && used + dto.Amount > item.PlannedAmount)
                throw new InvalidOperationException(
                    $"سقف بودجه رعایت نشد: مبلغ {item.PlannedAmount:N0} — تعهد/مصرف فعلی {used:N0} + درخواست {dto.Amount:N0}.");
        }

        var entity = new Db.BudgetTransaction
        {
            BudgetId = dto.BudgetId, BudgetItemId = dto.BudgetItemId, Type = dto.Type,
            VoucherId = dto.VoucherId, SourceId = dto.SourceId, SourceTitle = dto.SourceTitle,
            Amount = dto.Amount, Date = dto.Date == default ? DateTime.Now : dto.Date,
            Description = dto.Description
        };

        switch (dto.Type)
        {
            case BudgetTransactionType.Commitment:
                item.CommittedAmount += dto.Amount; break;
            case BudgetTransactionType.Actual:
                item.ActualAmount += dto.Amount; break;
            case BudgetTransactionType.Release:
                item.CommittedAmount = Math.Max(0, item.CommittedAmount - dto.Amount); break;
        }

        _db.BudgetTransactions.Add(entity);
        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        return dto;
    }

    public async Task DeleteTransactionAsync(int id)
    {
        var tx = await _db.BudgetTransactions.Include(t => t.BudgetItem)
                     .FirstOrDefaultAsync(t => t.Id == id)
                 ?? throw new InvalidOperationException("رویداد بودجه یافت نشد.");

        // برگرداندن اثر
        switch (tx.Type)
        {
            case BudgetTransactionType.Commitment:
                tx.BudgetItem.CommittedAmount = Math.Max(0, tx.BudgetItem.CommittedAmount - tx.Amount); break;
            case BudgetTransactionType.Actual:
                tx.BudgetItem.ActualAmount = Math.Max(0, tx.BudgetItem.ActualAmount - tx.Amount); break;
            case BudgetTransactionType.Release:
                tx.BudgetItem.CommittedAmount += tx.Amount; break;
        }

        _db.BudgetTransactions.Remove(tx);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    public async Task<BudgetDashboard> GetDashboardAsync()
    {
        var budgets = await GetBudgetsAsync(activeOnly: true);
        var over = budgets.Where(b => b.UsedPercent > 100).OrderByDescending(b => b.UsedPercent).ToList();
        var near = budgets.Where(b => b.UsedPercent is >= 80 and <= 100).OrderByDescending(b => b.UsedPercent).ToList();
        var master = budgets.Where(b => b.IsMaster).ToList();
        var scope = master.Count > 0 ? master : budgets;

        return new BudgetDashboard
        {
            BudgetCount = budgets.Count,
            TotalPlanned = scope.Sum(b => b.TotalAmount),
            TotalCommitted = scope.Sum(b => b.CommittedAmount),
            TotalActual = scope.Sum(b => b.ActualAmount),
            NearLimit = near, OverLimit = over
        };
    }

    // =====================================================================
    private static Budget ToDto(Db.Budget b, bool includeItems)
    {
        var dto = new Budget
        {
            Id = b.Id, FiscalYearId = b.FiscalYearId,
            FiscalYearTitle = b.FiscalYear != null ? b.FiscalYear.Title : null,
            DimensionValueId = b.DimensionValueId,
            DimensionValueName = b.DimensionValue != null
                ? $"{b.DimensionValue.Code} — {b.DimensionValue.Name}" : null,
            Name = b.Name, TotalAmount = b.TotalAmount,
            CommittedAmount = b.Items.Sum(i => i.CommittedAmount),
            ActualAmount = b.Items.Sum(i => i.ActualAmount),
            IsMaster = b.IsMaster, IsActive = b.IsActive, Description = b.Description
        };
        if (includeItems)
            dto.Items = b.Items.OrderBy(i => i.AccAccount != null ? i.AccAccount.Code : "9999")
                .Select(i => new BudgetItem
                {
                    Id = i.Id, BudgetId = i.BudgetId, AccAccountId = i.AccAccountId,
                    AccAccountCode = i.AccAccount?.Code, AccAccountName = i.AccAccount?.Name,
                    PlannedAmount = i.PlannedAmount, CommittedAmount = i.CommittedAmount,
                    ActualAmount = i.ActualAmount
                }).ToList();
        return dto;
    }
}
