using Db = Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>سرویس هزینه‌ها — دسته‌های قابل مدیریت + اسناد هزینه.</summary>
public class ExpenseService : IExpenseService
{
    private readonly Db.AppDbContext _db;

    public ExpenseService(Db.AppDbContext db) => _db = db;

    // =============================== دسته‌ها ===============================

    public async Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool activeOnly = false, Paging.Request? pagination = null)
    {
        var q = _db.ExpenseCategories.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(c => c.IsActive);
        var cats = await q.OrderBy(c => c.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);

        // آمار در حافظه (SQLite جمع decimal در SQL ندارد)
        var ids = cats.Select(c => c.Id).ToList();
        var expenses = await _db.Expenses.AsNoTracking().Where(e => ids.Contains(e.CategoryId)).ToListAsync();
        var stats = expenses.GroupBy(e => e.CategoryId)
            .ToDictionary(g => g.Key, g => new { Count = g.Count(), Total = g.Sum(x => x.Amount) });

        return cats.Select(c => new ExpenseCategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            IsActive = c.IsActive,
            CreatedAt = c.CreatedAt,
            ExpenseCount = stats.TryGetValue(c.Id, out var s) ? s.Count : 0,
            TotalAmount = stats.TryGetValue(c.Id, out var s2) ? s2.Total : 0
        }).ToList();
    }

    public async Task<ExpenseCategoryDto> SaveCategoryAsync(ExpenseCategoryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام دسته هزینه را وارد کنید.");

        var name = dto.Name.Trim();
        var dup = await _db.ExpenseCategories.AnyAsync(c => c.Name == name && c.Id != dto.Id);
        if (dup) throw new InvalidOperationException("دسته‌ای با این نام از قبل وجود دارد.");

        Db.ExpenseCategory entity;
        if (dto.Id == 0)
        {
            entity = new Db.ExpenseCategory { CreatedAt = DateTime.Now };
            _db.ExpenseCategories.Add(entity);
        }
        else
        {
            entity = await _db.ExpenseCategories.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("دسته هزینه یافت نشد.");
        }

        entity.Name = name;
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();

        dto.Id = entity.Id;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task DeleteCategoryAsync(int id)
    {
        var used = await _db.Expenses.AnyAsync(e => e.CategoryId == id);
        if (used) throw new InvalidOperationException("این دسته دارای سند هزینه است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var c = await _db.ExpenseCategories.FindAsync(id);
        if (c is null) return;
        _db.ExpenseCategories.Remove(c);
        await _db.SaveChangesAsync();
    }

    // =============================== اسناد هزینه ===============================

    public async Task<PagedResult<ExpenseDto>> GetExpensesAsync(string? search, int? categoryId, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var q = _db.Expenses.AsNoTracking().AsQueryable();
        if (categoryId is > 0) q = q.Where(e => e.CategoryId == categoryId);
        if (from.HasValue) q = q.Where(e => e.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(e => e.Date < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Number.Contains(s) ||
                             (e.Payee != null && e.Payee.Contains(s)) ||
                             (e.Description != null && e.Description.Contains(s)));
        }

        var total = await q.CountAsync();
        if (pageSize <= 0) pageSize = 15;
        if (page <= 0) page = 1;

        var items = await q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var catNames = await _db.ExpenseCategories.ToDictionaryAsync(c => c.Id, c => c.Name);

        return new PagedResult<ExpenseDto>
        {
            Items = items.Select(e => new ExpenseDto
            {
                Id = e.Id,
                Number = e.Number,
                CategoryId = e.CategoryId,
                CategoryName = catNames.TryGetValue(e.CategoryId, out var n) ? n : "—",
                Amount = e.Amount,
                Date = e.Date,
                PayType = e.PayType,
                TrsAccountId = e.TrsAccountId,
                TrsAccountName = e.TrsAccountName,
                Payee = e.Payee,
                Description = e.Description,
                CreatedAt = e.CreatedAt
            }).ToList(),
            TotalCount = total
        };
    }

    public async Task<ExpenseDto> SaveExpenseAsync(ExpenseDto dto)
    {
        if (dto.CategoryId <= 0)
            throw new InvalidOperationException("نوع هزینه را انتخاب کنید.");
        if (dto.Amount <= 0)
            throw new InvalidOperationException("مبلغ هزینه باید بزرگ‌تر از صفر باشد.");
        _ = await _db.ExpenseCategories.FindAsync(dto.CategoryId)
            ?? throw new InvalidOperationException("دسته هزینه یافت نشد.");

        Db.Expense entity;
        if (dto.Id == 0)
        {
            var count = await _db.Expenses.CountAsync();
            entity = new Db.Expense { Number = $"EX-{count + 1:0000}", CreatedAt = DateTime.Now };
            _db.Expenses.Add(entity);
        }
        else
        {
            entity = await _db.Expenses.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("سند هزینه یافت نشد.");
        }

        entity.CategoryId = dto.CategoryId;
        entity.Amount = dto.Amount;
        entity.Date = dto.Date == default ? DateTime.Now : dto.Date;
        entity.PayType = dto.PayType;
        entity.TrsAccountId = dto.TrsAccountId;
        entity.TrsAccountName = dto.TrsAccountId is int accId
            ? (await _db.TrsAccounts.FindAsync(accId))?.Name
            : null;
        entity.Payee = string.IsNullOrWhiteSpace(dto.Payee) ? null : dto.Payee.Trim();
        entity.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        await _db.SaveChangesAsync();
        dto.TrsAccountId = entity.TrsAccountId;
        dto.TrsAccountName = entity.TrsAccountName;

        var cat = await _db.ExpenseCategories.FindAsync(entity.CategoryId);
        dto.Id = entity.Id;
        dto.Number = entity.Number;
        dto.CategoryName = cat?.Name;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task DeleteExpenseAsync(int id)
    {
        var e = await _db.Expenses.FindAsync(id);
        if (e is null) return;
        _db.Expenses.Remove(e);
        await _db.SaveChangesAsync();
    }

    public async Task<decimal> GetTotalAsync(DateTime from, DateTime to)
    {
        var list = await _db.Expenses.AsNoTracking()
            .Where(e => e.Date >= from && e.Date < to).ToListAsync();
        return list.Sum(e => e.Amount);
    }
}
