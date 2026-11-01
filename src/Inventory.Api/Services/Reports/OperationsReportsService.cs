using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Reports;

/// <summary>تجمیع و آماده‌سازی گزارش‌های عملیاتی بر مبنای اسناد خرید/فروش و تعمیرات.</summary>
public class OperationsReportsService : IOperationsReportsService
{
    private readonly AppDbContext _db;

    public OperationsReportsService(AppDbContext db) => _db = db;

    public async Task<OperationsOrderReportDto> GetOrderReportAsync(TransactionType type, DateTime? from, DateTime? to,
        int? partyId, int? warehouseId)
    {
        EnsureOrderType(type);
        (from, to) = NormalizeRange(from, to);

        var documents = await OrderQuery(type, from, to, partyId, warehouseId)
            .Include(t => t.Lines)
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync();

        var partyIds = documents.Where(t => t.PartyId.HasValue).Select(t => t.PartyId!.Value).Distinct().ToList();
        var partyNames = partyIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);

        var result = new OperationsOrderReportDto
        {
            Type = type,
            From = from,
            To = to,
            DocumentCount = documents.Count,
            LineCount = documents.Sum(t => t.Lines.Count),
            TotalQuantity = documents.Sum(t => t.Lines.Sum(l => l.Quantity)),
            TotalAmount = documents.Sum(t => t.Amount)
        };
        result.AverageDocumentAmount = result.DocumentCount == 0
            ? 0m
            : result.TotalAmount / result.DocumentCount;

        var groupByDay = GroupByDay(from, to, documents.Select(t => t.Date));
        result.Trend = documents
            .GroupBy(t => BucketStart(t.Date, groupByDay))
            .OrderBy(g => g.Key)
            .Select(g => new OperationsOrderTrendPoint
            {
                PeriodStart = g.Key,
                Label = PeriodLabel(g.Key, groupByDay),
                DocumentCount = g.Count(),
                Amount = g.Sum(t => t.Amount)
            })
            .ToList();

        result.TopParties = documents
            .Where(t => t.PartyId.HasValue)
            .GroupBy(t => t.PartyId!.Value)
            .Select(g => new OperationsPartyReportRow
            {
                PartyId = g.Key,
                PartyName = partyNames.TryGetValue(g.Key, out var name) ? name : "—",
                DocumentCount = g.Count(),
                TotalQuantity = g.Sum(t => t.Lines.Sum(l => l.Quantity)),
                TotalAmount = g.Sum(t => t.Amount)
            })
            .OrderByDescending(p => p.TotalAmount)
            .ThenBy(p => p.PartyName)
            .Take(10)
            .ToList();

        return result;
    }

    public async Task<OperationsDashboardDto> GetDashboardAsync(DateTime? from, DateTime? to,
        bool includeOrders, bool includeRepairs, bool includeReferrers)
    {
        (from, to) = NormalizeRange(from, to);
        var result = new OperationsDashboardDto { From = from, To = to };

        var transactions = new List<TransactionReportRow>();
        if (includeOrders)
        {
            var query = _db.Transactions.AsNoTracking()
                .Where(t => t.Type == TransactionType.Purchase || t.Type == TransactionType.Sale);
            query = ApplyDateRange(query, from, to);
            var rows = await query.Select(t => new { t.Type, t.Date, t.Amount, t.ReferrerId }).ToListAsync();
            transactions = rows.Select(t => new TransactionReportRow
            {
                Type = t.Type,
                Date = t.Date,
                Amount = t.Amount,
                ReferrerId = t.ReferrerId
            }).ToList();

            var purchases = transactions.Where(t => t.Type == TransactionType.Purchase).ToList();
            var sales = transactions.Where(t => t.Type == TransactionType.Sale).ToList();
            result.PurchaseCount = purchases.Count;
            result.PurchaseTotal = purchases.Sum(t => t.Amount);
            result.SaleCount = sales.Count;
            result.SaleTotal = sales.Sum(t => t.Amount);

            if (includeReferrers)
                result.TopReferrers = await BuildReferrerRankingAsync(sales);
            result.BestReferrer = result.TopReferrers.FirstOrDefault();
        }

        var repairAdmissions = new List<RepairOrder>();
        var deliveredRepairs = new List<RepairOrder>();
        if (includeRepairs)
        {
            var admissionQuery = _db.RepairOrders.AsNoTracking().Include(r => r.Items).AsQueryable();
            if (from.HasValue)
            {
                var start = from.Value.Date;
                admissionQuery = admissionQuery.Where(r => r.ReceivedAt >= start);
            }
            if (to.HasValue)
            {
                var endExclusive = EndExclusive(to.Value);
                admissionQuery = admissionQuery.Where(r => r.ReceivedAt < endExclusive);
            }
            repairAdmissions = await admissionQuery.ToListAsync();

            var deliveryQuery = _db.RepairOrders.AsNoTracking().Include(r => r.Items)
                .Where(r => r.Status == RepairStatus.Delivered && r.DeliveredAt.HasValue);
            if (from.HasValue)
            {
                var start = from.Value.Date;
                deliveryQuery = deliveryQuery.Where(r => r.DeliveredAt.HasValue && r.DeliveredAt.Value >= start);
            }
            if (to.HasValue)
            {
                var endExclusive = EndExclusive(to.Value);
                deliveryQuery = deliveryQuery.Where(r => r.DeliveredAt.HasValue && r.DeliveredAt.Value < endExclusive);
            }
            deliveredRepairs = await deliveryQuery.ToListAsync();

            result.RepairCount = repairAdmissions.Count;
            result.ActiveRepairCount = repairAdmissions.Count(r => r.Status != RepairStatus.Delivered && r.Status != RepairStatus.Cancelled);
            result.CancelledRepairCount = repairAdmissions.Count(r => r.Status == RepairStatus.Cancelled);
            result.DeliveredRepairCount = deliveredRepairs.Count;

            var deliveredItems = deliveredRepairs.SelectMany(r => r.Items).ToList();
            result.RepairRevenue = deliveredItems.Sum(i => i.Price * i.Quantity);
            result.RepairPartsCost = deliveredItems.Where(i => i.ProductId is > 0).Sum(i => i.Cost * i.Quantity);
            result.RepairLaborCost = deliveredItems.Where(i => i.ProductId is null or 0).Sum(i => i.Cost * i.Quantity);
            result.RepairCost = result.RepairPartsCost + result.RepairLaborCost;
            result.RepairProfit = result.RepairRevenue - result.RepairCost;

            var turnaroundDays = deliveredRepairs
                .Where(r => r.DeliveredAt.HasValue && r.DeliveredAt.Value >= r.ReceivedAt)
                .Select(r => (decimal)(r.DeliveredAt!.Value - r.ReceivedAt).TotalHours / 24m)
                .ToList();
            result.AverageRepairTurnaroundDays = turnaroundDays.Count == 0
                ? null
                : decimal.Round(turnaroundDays.Average(), 1, MidpointRounding.AwayFromZero);

            // کارهای باز فعلی مستقل از فیلتر تاریخ گزارش می‌شوند تا پذیرش‌های قدیمیِ معوق پنهان نشوند.
            var openQuery = _db.RepairOrders.AsNoTracking()
                .Where(r => r.Status != RepairStatus.Delivered && r.Status != RepairStatus.Cancelled);
            var openStatusCounts = await openQuery.GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            result.OpenRepairCount = openStatusCounts.Sum(x => x.Count);
            result.OpenReceivedCount = openStatusCounts.Where(x => x.Status == RepairStatus.Received).Select(x => x.Count).FirstOrDefault();
            result.OpenInProgressCount = openStatusCounts.Where(x => x.Status == RepairStatus.InProgress).Select(x => x.Count).FirstOrDefault();
            result.OpenReadyCount = openStatusCounts.Where(x => x.Status == RepairStatus.Ready).Select(x => x.Count).FirstOrDefault();

            var today = DateTime.Today;
            var threeDaysAgo = today.AddDays(-3);
            var sevenDaysAgo = today.AddDays(-7);
            var tomorrow = today.AddDays(1);
            result.OpenAge0To3DaysCount = await openQuery.CountAsync(r => r.ReceivedAt >= threeDaysAgo && r.ReceivedAt < tomorrow);
            result.OpenAge4To7DaysCount = await openQuery.CountAsync(r => r.ReceivedAt >= sevenDaysAgo && r.ReceivedAt < threeDaysAgo);
            result.OpenAgeOver7DaysCount = await openQuery.CountAsync(r => r.ReceivedAt < sevenDaysAgo);
        }

        var dates = transactions.Select(t => t.Date)
            .Concat(deliveredRepairs.Where(r => r.DeliveredAt.HasValue).Select(r => r.DeliveredAt!.Value))
            .ToList();
        var groupByDay = GroupByDay(from, to, dates);
        var trend = new SortedDictionary<DateTime, OperationsDashboardTrendPoint>();

        OperationsDashboardTrendPoint PointFor(DateTime date)
        {
            var periodStart = BucketStart(date, groupByDay);
            if (!trend.TryGetValue(periodStart, out var point))
            {
                point = new OperationsDashboardTrendPoint
                {
                    PeriodStart = periodStart,
                    Label = PeriodLabel(periodStart, groupByDay)
                };
                trend.Add(periodStart, point);
            }
            return point;
        }

        foreach (var transaction in transactions)
        {
            var point = PointFor(transaction.Date);
            if (transaction.Type == TransactionType.Purchase) point.Purchase += transaction.Amount;
            else if (transaction.Type == TransactionType.Sale) point.Sale += transaction.Amount;
        }

        // درآمد و هزینهٔ تعمیر در روند بر مبنای تاریخ تحویل ثبت می‌شود؛ برآورد تعمیرات باز درآمد قطعی نیست.
        foreach (var repair in deliveredRepairs.Where(r => r.DeliveredAt.HasValue))
        {
            var revenue = repair.Items.Sum(i => i.Price * i.Quantity);
            var partsCost = repair.Items.Where(i => i.ProductId is > 0).Sum(i => i.Cost * i.Quantity);
            var laborCost = repair.Items.Where(i => i.ProductId is null or 0).Sum(i => i.Cost * i.Quantity);
            var cost = partsCost + laborCost;
            var point = PointFor(repair.DeliveredAt!.Value);
            point.RepairRevenue += revenue;
            point.RepairPartsCost += partsCost;
            point.RepairLaborCost += laborCost;
            point.RepairCost += cost;
            point.RepairProfit += revenue - cost;
        }

        result.Trend = trend.Values.ToList();
        return result;
    }

    private async Task<List<OperationsReferrerRankDto>> BuildReferrerRankingAsync(List<TransactionReportRow> sales)
    {
        var ranking = sales
            .Where(t => t.ReferrerId is > 0)
            .GroupBy(t => t.ReferrerId!.Value)
            .Select(g => new { ReferrerId = g.Key, SaleCount = g.Count(), SalesAmount = g.Sum(t => t.Amount) })
            .OrderByDescending(g => g.SalesAmount)
            .ThenByDescending(g => g.SaleCount)
            .ThenBy(g => g.ReferrerId)
            .Take(5)
            .ToList();

        var ids = ranking.Select(r => r.ReferrerId).ToList();
        if (ids.Count == 0) return new List<OperationsReferrerRankDto>();

        var names = await _db.Referrers.AsNoTracking().Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name);

        return ranking.Select(r => new OperationsReferrerRankDto
        {
            ReferrerId = r.ReferrerId,
            ReferrerName = names.TryGetValue(r.ReferrerId, out var name) ? name : "معرف حذف‌شده",
            SaleCount = r.SaleCount,
            SalesAmount = r.SalesAmount
        }).ToList();
    }

    private IQueryable<Transaction> OrderQuery(TransactionType type, DateTime? from, DateTime? to,
        int? partyId, int? warehouseId)
    {
        var query = _db.Transactions.AsNoTracking().Where(t => t.Type == type);
        query = ApplyDateRange(query, from, to);
        if (partyId is > 0) query = query.Where(t => t.PartyId == partyId.Value);
        if (warehouseId is > 0) query = query.Where(t => t.WarehouseId == warehouseId.Value);
        return query;
    }

    private static IQueryable<Transaction> ApplyDateRange(IQueryable<Transaction> query, DateTime? from, DateTime? to)
    {
        if (from.HasValue)
        {
            var start = from.Value.Date;
            query = query.Where(t => t.Date >= start);
        }
        if (to.HasValue)
        {
            var endExclusive = EndExclusive(to.Value);
            query = query.Where(t => t.Date < endExclusive);
        }
        return query;
    }

    private static DateTime EndExclusive(DateTime value)
        => value.Date == DateTime.MaxValue.Date ? DateTime.MaxValue : value.Date.AddDays(1);

    private static void EnsureOrderType(TransactionType type)
    {
        if (type != TransactionType.Purchase && type != TransactionType.Sale)
            throw new InvalidOperationException("نوع گزارش باید خرید یا فروش باشد.");
    }

    private static (DateTime? From, DateTime? To) NormalizeRange(DateTime? from, DateTime? to)
    {
        if (from.HasValue) from = from.Value.Date;
        if (to.HasValue) to = to.Value.Date;
        if (from.HasValue && to.HasValue && from.Value > to.Value) (from, to) = (to, from);
        return (from, to);
    }

    private static bool GroupByDay(DateTime? from, DateTime? to, IEnumerable<DateTime> dates)
    {
        var values = dates.Select(d => d.Date).ToList();
        var start = from?.Date ?? (values.Count > 0 ? values.Min() : DateTime.Today.AddDays(-30));
        var end = to?.Date ?? (values.Count > 0 ? values.Max() : DateTime.Today);
        return Math.Abs((end - start).TotalDays) <= 45;
    }

    private static DateTime BucketStart(DateTime date, bool byDay)
    {
        if (byDay) return date.Date;
        var (year, month, _) = PersianDate.FromGregorian(date);
        return PersianDate.ToGregorian(year, month, 1);
    }

    private static string PeriodLabel(DateTime date, bool byDay)
        => byDay ? PersianDate.ToShortFa(date) : PersianDate.MonthLabel(date);

    private sealed class TransactionReportRow
    {
        public TransactionType Type { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public int? ReferrerId { get; set; }
    }
}
