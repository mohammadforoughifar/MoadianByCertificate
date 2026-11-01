using Inventory.Shared.Dtos;

namespace Inventory.Api.Services;

/// <summary>قرارداد سرویس هزینه‌ها (دسته‌ها + اسناد هزینه).</summary>
public interface IExpenseService
{
    // ---------------- دسته‌های هزینه ----------------
    Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool activeOnly = false, Paging.Request? pagination = null);
    Task<ExpenseCategoryDto> SaveCategoryAsync(ExpenseCategoryDto dto);
    Task DeleteCategoryAsync(int id);

    // ---------------- اسناد هزینه ----------------
    Task<PagedResult<ExpenseDto>> GetExpensesAsync(string? search, int? categoryId, DateTime? from, DateTime? to, int page, int pageSize);
    Task<ExpenseDto> SaveExpenseAsync(ExpenseDto dto);
    Task DeleteExpenseAsync(int id);

    /// <summary>جمع هزینه‌های یک بازه (برای داشبورد).</summary>
    Task<decimal> GetTotalAsync(DateTime from, DateTime to);
}
