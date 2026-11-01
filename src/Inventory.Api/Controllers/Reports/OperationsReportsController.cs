using Inventory.Api.Data;
using Inventory.Api.Services.Reports;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>گزارش‌های بخش «عملیات» — خرید، فروش و داشبورد مدیریتی یکپارچه.</summary>
[Route("api/operations/reports")]
public class OperationsReportsController : RbacControllerBase
{
    private readonly IOperationsReportsService _reports;

    public OperationsReportsController(AppDbContext db, IOperationsReportsService reports) : base(db)
        => _reports = reports;

    /// <summary>خلاصهٔ تحلیلی خرید یا فروش با فیلتر تاریخ، طرف حساب و انبار.</summary>
    [HttpGet("orders")]
    public async Task<ActionResult<OperationsOrderReportDto>> GetOrderReport(
        [FromQuery] TransactionType type, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] int? partyId = null, [FromQuery] int? warehouseId = null)
    {
        if (await ForbiddenUnlessAnyAsync("Orders", "View", "Read", "Access") is ObjectResult forbidden) return forbidden;
        return Ok(await _reports.GetOrderReportAsync(type, from, to, partyId, warehouseId));
    }

    /// <summary>
    /// داشبورد عملیات. هر بخش فقط در صورت داشتن مجوز همان ماژول برگردانده می‌شود؛
    /// رتبه‌بندی معرف‌ها علاوه بر مشاهدهٔ خرید/فروش به مجوز مشاهدهٔ معرف‌ها هم نیاز دارد.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<OperationsDashboardDto>> GetDashboard(
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var canSeeOrders = await HasAnyAsync("Orders", "View", "Read", "Access");
        var canSeeRepairs = await HasAnyAsync("Repairs", "View", "Read", "Access");
        var canSeeReferrers = await HasAnyAsync("Referrers", "View", "Read", "Access");
        if (!canSeeOrders && !canSeeRepairs)
            return StatusCode(403, new { message = "برای مشاهدهٔ داشبورد عملیات، دسترسی خرید/فروش یا تعمیرات لازم است." });

        return Ok(await _reports.GetDashboardAsync(from, to, canSeeOrders, canSeeRepairs,
            canSeeOrders && canSeeReferrers));
    }
}
