using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>اسناد هزینه.</summary>
[Route("api/expenses")]
[Inventory.Api.Infrastructure.ModulePermission("Expenses")]
public class ExpensesController : ApiControllerBase
{
    private readonly IExpenseService _svc;

    public ExpensesController(IExpenseService svc) => _svc = svc;

    /// <summary>فهرست اسناد هزینه با جستجو/فیلتر/صفحه‌بندی.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ExpenseDto>>> GetAll(
        [FromQuery] string? search, [FromQuery] int? categoryId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _svc.GetExpensesAsync(search, categoryId, from, to, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    /// <summary>ایجاد / ویرایش سند هزینه.</summary>
    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Save([FromBody] ExpenseDto expense)
        => Ok(await _svc.SaveExpenseAsync(expense));

    /// <summary>حذف سند هزینه.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _svc.DeleteExpenseAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>دسته‌های هزینه (قابل مدیریت).</summary>
[Route("api/expense-categories")]
[Inventory.Api.Infrastructure.ModulePermission("Expenses")]
public class ExpenseCategoriesController : ApiControllerBase
{
    private readonly IExpenseService _svc;

    public ExpenseCategoriesController(IExpenseService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<ExpenseCategoryDto>>> GetAll([FromQuery] bool activeOnly = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetCategoriesAsync(activeOnly, pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<ExpenseCategoryDto>> Save([FromBody] ExpenseCategoryDto category)
        => Ok(await _svc.SaveCategoryAsync(category));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _svc.DeleteCategoryAsync(id);
        return Ok(new { ok = true });
    }
}
