using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Reports;

/// <summary>سرویس گزارش‌های بخش عملیات: خرید/فروش و داشبورد یکپارچه.</summary>
public interface IOperationsReportsService
{
    Task<OperationsOrderReportDto> GetOrderReportAsync(TransactionType type, DateTime? from, DateTime? to,
        int? partyId, int? warehouseId);

    Task<OperationsDashboardDto> GetDashboardAsync(DateTime? from, DateTime? to,
        bool includeOrders, bool includeRepairs, bool includeReferrers);
}
