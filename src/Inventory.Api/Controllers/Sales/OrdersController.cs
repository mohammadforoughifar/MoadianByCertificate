using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>اسناد خرید و فروش.</summary>
[Route("api/orders")]
[Inventory.Api.Infrastructure.ModulePermission("Orders")]
public class OrdersController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public OrdersController(IInventoryService service) => _service = service;

    /// <summary>فهرست اسناد با فیلتر نوع/تاریخ/طرف حساب/انبار.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<Order>>> GetAll(
        [FromQuery] TransactionType type, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? partyId, [FromQuery] int? warehouseId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _service.GetOrdersAsync(type, from, to, partyId, warehouseId, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    /// <summary>پیشنهاد قیمت بر اساس آخرین معامله کالا.</summary>
    [HttpGet("suggest-price")]
    public async Task<ActionResult<decimal>> SuggestPrice([FromQuery] int productId, [FromQuery] TransactionType type)
        => Ok(await _service.SuggestPriceAsync(productId, type));

    /// <summary>آخرین فاکتور خریدی که شامل این کالاست.</summary>
    [HttpGet("last-purchase")]
    public async Task<ActionResult<Order>> LastPurchase([FromQuery] int productId)
    {
        var order = await _service.GetLastPurchaseAsync(productId);
        return order is null
            ? NotFound(new { message = "برای این کالا سند خریدی ثبت نشده است." })
            : Ok(order);
    }

    /// <summary>دریافت یک سند.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> GetById(int id)
    {
        var order = await _service.GetOrderAsync(id);
        return order is null ? NotFound(new { message = "سند یافت نشد." }) : Ok(order);
    }

    /// <summary>ثبت سند خرید/فروش جدید.</summary>
    [HttpPost]
    public async Task<ActionResult<Order>> Create([FromBody] OrderCommand cmd)
        => Ok(await _service.CreateOrderAsync(cmd));

    /// <summary>ویرایش سند خرید/فروش.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<Order>> Update(int id, [FromBody] OrderCommand cmd)
        => Ok(await _service.UpdateOrderAsync(id, cmd));

    /// <summary>حذف سند و برگرداندن موجودی.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteOrderAsync(id);
        return Ok(new { ok = true });
    }
}
