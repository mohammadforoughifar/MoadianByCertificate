using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Dashboards;

public interface IWidgetDataService
{
    /// <summary>محاسبهٔ دادهٔ یک ویجت. کنترل مجوز قبل از صدا زدن این متد انجام می‌شود.</summary>
    Task<WidgetDataDto> GetAsync(WidgetDefDto def, int userId, DashChartType? chartType,
        DashRange range, IReadOnlyDictionary<string, string> config);
}

/// <summary>
/// تولید دادهٔ ویجت‌های داشبورد شخصی — همه با EF و مستقیم از دیتابیس.
/// هیچ ویجتی اینجا مجوز را دور نمی‌زند؛ کنترلر قبل از صدا زدن، مجوز ماژول را چک می‌کند.
/// </summary>
public class WidgetDataService : IWidgetDataService
{
    private readonly AppDbContext _db;
    public WidgetDataService(AppDbContext db) => _db = db;

    public async Task<WidgetDataDto> GetAsync(WidgetDefDto def, int userId, DashChartType? chartType,
        DashRange range, IReadOnlyDictionary<string, string> config)
    {
        var res = new WidgetDataDto
        {
            Key = def.Key,
            Kind = def.Kind,
            Title = def.Title,
            Unit = string.IsNullOrEmpty(def.Unit) ? null : def.Unit,
            Drilldown = string.IsNullOrEmpty(def.Drilldown) ? null : def.Drilldown,
            ChartType = chartType ?? def.ChartTypes.FirstOrDefault()
        };

        var (from, to) = Bounds(range, 0);
        var (pFrom, pTo) = Bounds(range, -1);

        // برای بازهٔ «همه»، مقایسه با دورهٔ قبل معنا ندارد
        var hasPrev = range != DashRange.All;
        decimal? Prev(decimal v) => hasPrev ? v : null;
        var limit = Int(config, "limit", 5);

        try
        {
            switch (def.Key)
            {
                // ==================== فروش و فاکتور ====================
                case "fac-sales-today":
                {
                    var t0 = DateTime.Now.Date;
                    FillKpi(res, await SaleSum(t0, t0.AddDays(1)), null,
                                 await SaleSum(t0.AddDays(-1), t0), "نسبت به دیروز");
                    break;
                }
                case "fac-sales-period":
                {
                    FillKpi(res, await SaleSum(from, to), await SaleCount(from, to),
                                 Prev(await SaleSum(pFrom, pTo)), "نسبت به بازهٔ قبل");
                    break;
                }
                case "fac-purchase-period":
                {
                    var sum = (decimal)(await _db.FacInvoices
                        .Where(i => i.Status == InvoiceStatus.Confirmed && i.Kind == InvoiceKind.Purchase
                                    && i.Date >= from && i.Date < to)
                        .SumAsync(i => (double?)i.TotalNet) ?? 0);
                    var prev = (decimal)(await _db.FacInvoices
                        .Where(i => i.Status == InvoiceStatus.Confirmed && i.Kind == InvoiceKind.Purchase
                                    && i.Date >= pFrom && i.Date < pTo)
                        .SumAsync(i => (double?)i.TotalNet) ?? 0);
                    var cnt = await _db.FacInvoices
                        .CountAsync(i => i.Status == InvoiceStatus.Confirmed && i.Kind == InvoiceKind.Purchase
                                         && i.Date >= from && i.Date < to);
                    FillKpi(res, sum, cnt, Prev(prev), "نسبت به بازهٔ قبل");
                    break;
                }
                case "fac-invoice-count":
                {
                    var cnt = await _db.FacInvoices.CountAsync(i => i.Status == InvoiceStatus.Confirmed
                                                                    && i.Date >= from && i.Date < to);
                    var prev = await _db.FacInvoices.CountAsync(i => i.Status == InvoiceStatus.Confirmed
                                                                     && i.Date >= pFrom && i.Date < pTo);
                    FillKpi(res, cnt, null, Prev(prev), "نسبت به بازهٔ قبل");
                    res.Unit = "عدد";
                    break;
                }
                case "fac-sales-trend":
                {
                    var months = LastMonths(12);
                    var mFrom = months[0].From;
                    var mTo = months[months.Count - 1].To;
                    var rows = await _db.FacInvoices
                        .Where(i => i.Status == InvoiceStatus.Confirmed && i.Kind == InvoiceKind.Sale
                                    && i.Date >= mFrom && i.Date < mTo)
                        .GroupBy(i => new { i.Date.Year, i.Date.Month, i.Date.Day })
                        .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Sum = g.Sum(x => (double)x.TotalNet) })
                        .ToListAsync();
                    res.Series.Add(MonthSeries("فروش", months,
                        rows.Select(r => JalaliRow(r.Year, r.Month, r.Day, (decimal)r.Sum)).ToList()));
                    break;
                }
                case "fac-sales-vs-purchase":
                {
                    var months = LastMonths(12);
                    var mFrom = months[0].From;
                    var mTo = months[months.Count - 1].To;
                    var all = await _db.FacInvoices
                        .Where(i => i.Status == InvoiceStatus.Confirmed
                                    && (i.Kind == InvoiceKind.Sale || i.Kind == InvoiceKind.Purchase)
                                    && i.Date >= mFrom && i.Date < mTo)
                        .GroupBy(i => new { i.Date.Year, i.Date.Month, i.Date.Day, i.Kind })
                        .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Kind, Sum = g.Sum(x => (double)x.TotalNet) })
                        .ToListAsync();
                    res.Series.Add(MonthSeries("فروش", months,
                        all.Where(a => a.Kind == InvoiceKind.Sale)
                           .Select(a => JalaliRow(a.Year, a.Month, a.Day, (decimal)a.Sum)).ToList()));
                    res.Series.Add(MonthSeries("خرید", months,
                        all.Where(a => a.Kind == InvoiceKind.Purchase)
                           .Select(a => JalaliRow(a.Year, a.Month, a.Day, (decimal)a.Sum)).ToList()));
                    break;
                }
                case "fac-top-parties":
                {
                    var rows = await _db.FacInvoices
                        .Where(i => i.Status == InvoiceStatus.Confirmed && i.Kind == InvoiceKind.Sale
                                    && i.Date >= from && i.Date < to && i.PartyId != null)
                        .GroupBy(i => i.PartyId!.Value)
                        .Select(g => new { PartyId = g.Key, Sum = g.Sum(x => (double)x.TotalNet), Cnt = g.Count() })
                        .OrderByDescending(x => x.Sum)
                        .Take(limit)
                        .ToListAsync();
                    var ids = rows.Select(r => r.PartyId).ToList();
                    var names = await _db.Parties.Where(p => ids.Contains(p.Id))
                        .ToDictionaryAsync(p => p.Id, p => p.Name);
                    res.Columns = new List<string> { "طرف حساب", "مبلغ", "تعداد" };
                    res.Rows = rows.Select(r => new DashRowDto
                    {
                        Cells = new List<string>
                        {
                            names.GetValueOrDefault(r.PartyId, "—"),
                            Fa.Digits(((decimal)r.Sum).ToString("N0")),
                            Fa.Digits(r.Cnt.ToString())
                        }
                    }).ToList();
                    break;
                }
                case "fac-top-products":
                {
                    var rows = await _db.FacInvoiceLines
                        .Where(l => l.Invoice!.Status == InvoiceStatus.Confirmed
                                    && l.Invoice.Kind == InvoiceKind.Sale
                                    && l.Invoice.Date >= from && l.Invoice.Date < to)
                        .GroupBy(l => l.ProductId)
                        .Select(g => new { ProductId = g.Key, Sum = g.Sum(x => (double)x.Total), Qty = g.Sum(x => (double)x.Quantity) })
                        .OrderByDescending(x => x.Sum)
                        .Take(limit)
                        .ToListAsync();
                    var pids = rows.Select(r => r.ProductId).ToList();
                    var names = await _db.Products.Where(p => pids.Contains(p.Id))
                        .ToDictionaryAsync(p => p.Id, p => p.Name);
                    res.Columns = new List<string> { "کالا", "مبلغ فروش", "تعداد" };
                    res.Rows = rows.Select(r => new DashRowDto
                    {
                        Cells = new List<string>
                        {
                            names.GetValueOrDefault(r.ProductId, "—"),
                            Fa.Digits(((decimal)r.Sum).ToString("N0")),
                            Fa.Digits(((decimal)r.Qty).ToString("N0"))
                        }
                    }).ToList();
                    break;
                }

                // ==================== انبارداری ====================
                case "inv-stock-value":
                {
                    var value = (decimal)(await _db.InvStocks.SumAsync(s => (double?)s.Value) ?? 0);
                    var items = await _db.InvStocks.CountAsync(s => s.Quantity > 0);
                    FillKpi(res, value, items, null, null);
                    res.SubLabel = "قلم دارای موجودی";
                    break;
                }
                case "inv-doc-count":
                {
                    var cnt = await _db.InvDocs.CountAsync(d => d.Status == InvDocStatus.Confirmed
                                                                && d.Date >= from && d.Date < to);
                    var prev = await _db.InvDocs.CountAsync(d => d.Status == InvDocStatus.Confirmed
                                                                 && d.Date >= pFrom && d.Date < pTo);
                    FillKpi(res, cnt, null, Prev(prev), "نسبت به بازهٔ قبل");
                    res.Unit = "عدد";
                    break;
                }
                case "inv-doc-trend":
                {
                    var months = LastMonths(12);
                    var mFrom = months[0].From;
                    var mTo = months[months.Count - 1].To;
                    var rows = await _db.InvDocs
                        .Where(d => d.Status == InvDocStatus.Confirmed
                                    && d.Date >= mFrom && d.Date < mTo)
                        .GroupBy(d => new { d.Date.Year, d.Date.Month, d.Date.Day })
                        .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Cnt = g.Count() })
                        .ToListAsync();
                    res.Series.Add(MonthSeries("سند انبار", months,
                        rows.Select(r => JalaliRow(r.Year, r.Month, r.Day, r.Cnt)).ToList()));
                    break;
                }

                // ==================== کالا و موجودی ====================
                case "prod-reorder-count":
                case "prod-reorder-list":
                {
                    var low = await ReorderItemsAsync();
                    if (def.Key == "prod-reorder-count")
                    {
                        FillKpi(res, low.Count, null, null, null);
                        res.Unit = "قلم";
                    }
                    else
                    {
                        res.Columns = new List<string> { "کالا", "موجودی", "نقطه سفارش", "کسری" };
                        res.Rows = low.Take(limit).Select(x => new DashRowDto
                        {
                            Tone = x.Qty <= 0 ? "danger" : "warning",
                            Cells = new List<string>
                            {
                                x.Name,
                                Fa.Digits(x.Qty.ToString("N0")) + " " + x.Unit,
                                Fa.Digits(x.ReorderPoint.ToString("N0")),
                                Fa.Digits((x.ReorderPoint - x.Qty).ToString("N0"))
                            }
                        }).ToList();
                    }
                    break;
                }
                case "prod-stock-by-warehouse":
                {
                    var stock = await _db.Stocks.Where(x => x.Quantity > 0)
                        .GroupBy(x => x.WarehouseId)
                        .Select(g => new { WarehouseId = g.Key, Cnt = g.Count() })
                        .ToListAsync();
                    var names = await _db.Warehouses.ToDictionaryAsync(w => w.Id, w => w.Name);
                    var ser = new DashSeriesDto { Name = "موجودی" };
                    ser.Labels = stock.Select(s => names.GetValueOrDefault(s.WarehouseId, "انبار " + s.WarehouseId)).ToList();
                    ser.Values = stock.Select(s => (decimal)s.Cnt).ToList();
                    res.Series.Add(ser);
                    break;
                }

                // ==================== حسابداری ====================
                case "acc-voucher-count":
                {
                    var cnt = await _db.AccVouchers.CountAsync(v => v.Status == VoucherStatus.Confirmed
                                                                    && v.Date >= from && v.Date < to);
                    var prev = await _db.AccVouchers.CountAsync(v => v.Status == VoucherStatus.Confirmed
                                                                     && v.Date >= pFrom && v.Date < pTo);
                    FillKpi(res, cnt, null, Prev(prev), "نسبت به بازهٔ قبل");
                    res.Unit = "عدد";
                    break;
                }
                case "acc-voucher-trend":
                {
                    var months = LastMonths(12);
                    var mFrom = months[0].From;
                    var mTo = months[months.Count - 1].To;
                    var rows = await _db.AccVouchers
                        .Where(v => v.Date >= mFrom && v.Date < mTo)
                        .GroupBy(v => new { v.Date.Year, v.Date.Month, v.Date.Day })
                        .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Cnt = g.Count() })
                        .ToListAsync();
                    res.Series.Add(MonthSeries("سند حسابداری", months,
                        rows.Select(r => JalaliRow(r.Year, r.Month, r.Day, r.Cnt)).ToList()));
                    break;
                }

                // ==================== خزانه‌داری ====================
                case "trs-receipt-period":
                case "trs-payment-period":
                {
                    var kind = def.Key == "trs-receipt-period" ? TreasuryKind.Receipt : TreasuryKind.Payment;
                    var sum = (decimal)(await _db.TrsVouchers
                        .Where(v => v.Status == TreasuryStatus.Confirmed && v.Kind == kind
                                    && v.Date >= from && v.Date < to)
                        .SumAsync(v => (double?)v.TotalAmount) ?? 0);
                    var prev = (decimal)(await _db.TrsVouchers
                        .Where(v => v.Status == TreasuryStatus.Confirmed && v.Kind == kind
                                    && v.Date >= pFrom && v.Date < pTo)
                        .SumAsync(v => (double?)v.TotalAmount) ?? 0);
                    var cnt = await _db.TrsVouchers
                        .CountAsync(v => v.Status == TreasuryStatus.Confirmed && v.Kind == kind
                                         && v.Date >= from && v.Date < to);
                    FillKpi(res, sum, cnt, Prev(prev), "نسبت به بازهٔ قبل");
                    break;
                }
                case "trs-flow-trend":
                {
                    var months = LastMonths(12);
                    var mFrom = months[0].From;
                    var mTo = months[months.Count - 1].To;
                    var rows = await _db.TrsVouchers
                        .Where(v => v.Status == TreasuryStatus.Confirmed
                                    && (v.Kind == TreasuryKind.Receipt || v.Kind == TreasuryKind.Payment)
                                    && v.Date >= mFrom && v.Date < mTo)
                        .GroupBy(v => new { v.Date.Year, v.Date.Month, v.Date.Day, v.Kind })
                        .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Kind, Sum = g.Sum(x => (double)x.TotalAmount) })
                        .ToListAsync();
                    res.Series.Add(MonthSeries("دریافت", months,
                        rows.Where(r => r.Kind == TreasuryKind.Receipt)
                            .Select(r => JalaliRow(r.Year, r.Month, r.Day, (decimal)r.Sum)).ToList()));
                    res.Series.Add(MonthSeries("پرداخت", months,
                        rows.Where(r => r.Kind == TreasuryKind.Payment)
                            .Select(r => JalaliRow(r.Year, r.Month, r.Day, (decimal)r.Sum)).ToList()));
                    break;
                }
                case "trs-cheques-amount":
                {
                    var sum = (decimal)(await _db.TrsCheques
                        .Where(c => c.Kind == ChequeKind.Received
                                    && (c.Status == ChequeStatus.InHand || c.Status == ChequeStatus.InCollection))
                        .SumAsync(c => (double?)c.Amount) ?? 0);
                    var cnt = await _db.TrsCheques
                        .CountAsync(c => c.Kind == ChequeKind.Received
                                         && (c.Status == ChequeStatus.InHand || c.Status == ChequeStatus.InCollection));
                    FillKpi(res, sum, cnt, null, null);
                    res.SubLabel = "چک وصول‌نشده";
                    break;
                }
                case "trs-cheques-due":
                {
                    var until = DateTime.Now.Date.AddDays(30);
                    var today = DateTime.Now.Date;
                    var rows = await _db.TrsCheques
                        .Where(c => (c.Status == ChequeStatus.InHand || c.Status == ChequeStatus.InCollection)
                                    && c.DueDate <= until)
                        .OrderBy(c => c.DueDate)
                        .Take(limit)
                        .Select(c => new { c.Id, c.Amount, c.DueDate, PartyName = c.Party != null ? c.Party.Name : null })
                        .ToListAsync();
                    res.Columns = new List<string> { "سررسید", "طرف حساب", "مبلغ" };
                    res.Rows = rows.Select(r => new DashRowDto
                    {
                        Tone = r.DueDate < today ? "danger" : (r.DueDate <= today.AddDays(7) ? "warning" : null),
                        Cells = new List<string>
                        {
                            Fa.Digits(PersianDate.ToShort(r.DueDate)),
                            r.PartyName ?? "—",
                            Fa.Digits(r.Amount.ToString("N0"))
                        }
                    }).ToList();
                    break;
                }

                // ==================== هزینه ====================
                case "exp-period":
                {
                    var sum = (decimal)(await _db.Expenses.Where(e => e.Date >= from && e.Date < to)
                                                .SumAsync(e => (double?)e.Amount) ?? 0);
                    var prev = (decimal)(await _db.Expenses.Where(e => e.Date >= pFrom && e.Date < pTo)
                                                 .SumAsync(e => (double?)e.Amount) ?? 0);
                    var cnt = await _db.Expenses.CountAsync(e => e.Date >= from && e.Date < to);
                    FillKpi(res, sum, cnt, Prev(prev), "نسبت به بازهٔ قبل");
                    break;
                }
                case "exp-by-category":
                {
                    var raw = await _db.Expenses.Where(e => e.Date >= from && e.Date < to)
                        .Select(e => new { e.CategoryId, e.Amount }).ToListAsync();
                    var cats = await _db.ExpenseCategories.ToDictionaryAsync(c => c.Id, c => c.Name);
                    var grouped = raw.GroupBy(e => cats.GetValueOrDefault(e.CategoryId, "بدون دسته"))
                                     .Select(g => new { Name = g.Key, Sum = g.Sum(x => x.Amount) })
                                     .OrderByDescending(x => x.Sum).ToList();
                    var ser = new DashSeriesDto { Name = "هزینه" };
                    ser.Labels = grouped.Select(g => g.Name).ToList();
                    ser.Values = grouped.Select(g => g.Sum).ToList();
                    res.Series.Add(ser);
                    break;
                }

                // ==================== دستور کار ====================
                case "wo-open":
                {
                    var open = await MyWorkOrders(userId).CountAsync(w => w.Status == "Open");
                    var all = await MyWorkOrders(userId).CountAsync();
                    FillKpi(res, open, all, null, null);
                    res.SubLabel = "کل دستورهای کار من";
                    break;
                }
                case "wo-overdue":
                {
                    var now = DateTime.Now;
                    var late = await MyWorkOrders(userId).CountAsync(w => w.Status == "Open" && w.DueAt < now);
                    var open = await MyWorkOrders(userId).CountAsync(w => w.Status == "Open");
                    FillKpi(res, late, open, null, null);
                    res.SubLabel = "دستور باز";
                    break;
                }
                case "wo-by-status":
                {
                    var rows = await _db.WorkOrders.Where(w => w.DeletedAt == null)
                        .GroupBy(w => w.Status)
                        .Select(g => new { Status = g.Key, Cnt = g.Count() })
                        .ToListAsync();
                    var ser = new DashSeriesDto { Name = "دستور کار" };
                    ser.Labels = rows.Select(r => WoStatusFa(r.Status)).ToList();
                    ser.Values = rows.Select(r => (decimal)r.Cnt).ToList();
                    res.Series.Add(ser);
                    break;
                }

                // ==================== اتوماسیون اداری ====================
                case "off-letters-count":
                {
                    var cnt = await LettersIn(from, to).CountAsync();
                    var prev = await LettersIn(pFrom, pTo).CountAsync();
                    FillKpi(res, cnt, null, Prev(prev), "نسبت به بازهٔ قبل");
                    res.Unit = "نامه";
                    break;
                }
                case "off-letters-by-type":
                {
                    var rows = await LettersIn(from, to)
                        .GroupBy(x => x.SourceType)
                        .Select(g => new { Type = g.Key, Cnt = g.Count() })
                        .ToListAsync();
                    var ser = new DashSeriesDto { Name = "تعداد نامه" };
                    foreach (var t in new[] { 1, 2, 3 })
                    {
                        ser.Labels.Add(LetterTypeFa(t));
                        ser.Values.Add(rows.FirstOrDefault(r => r.Type == t)?.Cnt ?? 0);
                    }
                    res.Series.Add(ser);
                    break;
                }
                case "off-letters-trend":
                {
                    var months = LastMonths(12);
                    var mFrom = months[0].From;
                    var mTo = months[months.Count - 1].To;
                    var rows = await LettersIn(mFrom, mTo)
                        .Select(x => new
                        {
                            x.SourceType,
                            D = x.InnerLetter != null ? x.InnerLetter.DateSabt
                              : x.OutgoingLetter != null ? x.OutgoingLetter.DateSabt
                              : x.IncomingLetter!.DateErsal
                        })
                        .GroupBy(x => new { x.SourceType, x.D.Year, x.D.Month, x.D.Day })
                        .Select(g => new { g.Key.SourceType, g.Key.Year, g.Key.Month, g.Key.Day, Cnt = g.Count() })
                        .ToListAsync();
                    foreach (var t in new[] { 1, 2, 3 })
                        res.Series.Add(MonthSeries(LetterTypeFa(t), months,
                            rows.Where(r => r.SourceType == t)
                                .Select(r => JalaliRow(r.Year, r.Month, r.Day, r.Cnt)).ToList()));
                    break;
                }
                case "off-letters-by-status":
                {
                    var rows = await LettersIn(from, to)
                        .Select(x => new
                        {
                            x.SourceType,
                            OutStatus = x.OutgoingLetter != null ? x.OutgoingLetter.Status : -1,
                            InBay = x.IncomingLetter != null && x.IncomingLetter.IsBayegani
                        })
                        .ToListAsync();
                    var buckets = rows
                        .GroupBy(r => r.SourceType == 1 ? "ثبت‌شده"
                                    : r.SourceType == 2 ? OutStatusFa(r.OutStatus)
                                    : (r.InBay ? "بایگانی‌شده" : "در جریان"))
                        .Select(g => new { Label = g.Key, Cnt = g.Count() })
                        .OrderByDescending(g => g.Cnt)
                        .ToList();
                    var ser = new DashSeriesDto { Name = "تعداد نامه" };
                    ser.Labels = buckets.Select(b => b.Label).ToList();
                    ser.Values = buckets.Select(b => (decimal)b.Cnt).ToList();
                    res.Series.Add(ser);
                    break;
                }
                case "off-letters-by-urgency":
                {
                    var rows = await LettersIn(from, to)
                        .Select(x => new
                        {
                            InnerU = x.InnerLetter != null ? x.InnerLetter.Foriat : null,
                            OutU = x.OutgoingLetter != null ? x.OutgoingLetter.Foriat : null,
                            IncU = x.IncomingLetter != null ? (int?)x.IncomingLetter.Foriat : null
                        })
                        .ToListAsync();
                    var buckets = rows
                        .GroupBy(r => UrgencyFa(r.InnerU ?? r.OutU, r.IncU))
                        .Select(g => new { Label = g.Key, Cnt = g.Count() })
                        .OrderByDescending(g => g.Cnt)
                        .ToList();
                    var ser = new DashSeriesDto { Name = "تعداد نامه" };
                    ser.Labels = buckets.Select(b => b.Label).ToList();
                    ser.Values = buckets.Select(b => (decimal)b.Cnt).ToList();
                    res.Series.Add(ser);
                    break;
                }
                case "off-erja-unread":
                {
                    var cnt = await _db.Erjas
                        .CountAsync(e => !e.IsDelete && !e.IsRead && e.ReciverUserId == userId);
                    FillKpi(res, cnt, null, null, null);
                    res.Unit = "مورد";
                    break;
                }

                // ---------- نامه صادره ----------
                case "off-out-count":
                {
                    var cnt = await OutLettersIn(from, to).CountAsync();
                    var prev = await OutLettersIn(pFrom, pTo).CountAsync();
                    FillKpi(res, cnt, null, Prev(prev), "نسبت به بازهٔ قبل");
                    res.Unit = "نامه";
                    break;
                }
                case "off-out-pending":
                {
                    var cnt = await OutLettersIn(DateTime.MinValue, DateTime.MaxValue)
                        .CountAsync(o => o.Status == 0 || o.Status == 1);
                    FillKpi(res, cnt, null, null, null);
                    res.Unit = "نامه";
                    break;
                }
                case "off-out-by-status":
                {
                    var rows = await OutLettersIn(from, to)
                        .GroupBy(o => o.Status)
                        .Select(g => new { Status = g.Key, Cnt = g.Count() })
                        .ToListAsync();
                    var ser = new DashSeriesDto { Name = "نامه صادره" };
                    foreach (var s in new[] { 0, 1, 2, 3 })
                    {
                        ser.Labels.Add(OutStatusFa(s));
                        ser.Values.Add(rows.FirstOrDefault(r => r.Status == s)?.Cnt ?? 0);
                    }
                    res.Series.Add(ser);
                    break;
                }
                case "off-out-by-method":
                {
                    var rows = await OutLettersIn(from, to)
                        .GroupBy(o => o.SendMethod)
                        .Select(g => new { Method = g.Key, Cnt = g.Count() })
                        .ToListAsync();
                    var buckets = rows
                        .GroupBy(r => string.IsNullOrWhiteSpace(r.Method) ? "نامشخص" : r.Method!.Trim())
                        .Select(g => new { Label = g.Key, Cnt = g.Sum(x => x.Cnt) })
                        .OrderByDescending(g => g.Cnt)
                        .ToList();
                    var ser = new DashSeriesDto { Name = "نامه صادره" };
                    ser.Labels = buckets.Select(b => b.Label).ToList();
                    ser.Values = buckets.Select(b => (decimal)b.Cnt).ToList();
                    res.Series.Add(ser);
                    break;
                }
                case "off-out-trend":
                {
                    var months = LastMonths(12);
                    var mFrom = months[0].From;
                    var mTo = months[months.Count - 1].To;

                    var reg = await OutLettersIn(mFrom, mTo)
                        .GroupBy(o => new { o.DateSabt.Year, o.DateSabt.Month, o.DateSabt.Day })
                        .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Cnt = g.Count() })
                        .ToListAsync();
                    res.Series.Add(MonthSeries("ثبت‌شده", months,
                        reg.Select(r => JalaliRow(r.Year, r.Month, r.Day, r.Cnt)).ToList()));

                    var issued = await OutLetters()
                        .Where(o => o.DateSadere != null && o.DateSadere >= mFrom && o.DateSadere < mTo)
                        .GroupBy(o => new { o.DateSadere!.Value.Year, o.DateSadere!.Value.Month, o.DateSadere!.Value.Day })
                        .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Cnt = g.Count() })
                        .ToListAsync();
                    res.Series.Add(MonthSeries("صادرشده", months,
                        issued.Select(r => JalaliRow(r.Year, r.Month, r.Day, r.Cnt)).ToList()));
                    break;
                }
                case "off-out-top-receivers":
                {
                    var rows = await OutLettersIn(from, to)
                        .GroupBy(o => o.ReceiverOrganization)
                        .Select(g => new { Org = g.Key, Cnt = g.Count() })
                        .OrderByDescending(x => x.Cnt)
                        .Take(limit)
                        .ToListAsync();
                    res.Columns = new List<string> { "سازمان گیرنده", "تعداد نامه" };
                    res.Rows = rows.Select(r => new DashRowDto
                    {
                        Cells = new List<string>
                        {
                            string.IsNullOrWhiteSpace(r.Org) ? "—" : r.Org,
                            Fa.Digits(r.Cnt.ToString())
                        }
                    }).ToList();
                    break;
                }

                // ---------- نامه داخلی ----------
                case "off-inner-count":
                {
                    var cnt = await InnerLettersIn(from, to).CountAsync();
                    var prev = await InnerLettersIn(pFrom, pTo).CountAsync();
                    FillKpi(res, cnt, null, Prev(prev), "نسبت به بازهٔ قبل");
                    res.Unit = "نامه";
                    break;
                }
                case "off-inner-overdue":
                {
                    var now = DateTime.Now;
                    var cnt = await _db.LetterSources
                        .Where(x => !x.IsDelete && x.InnerLetter != null
                            && x.Erjas.Any(e => !e.IsDelete && e.Answer == ""
                                && e.MohlatPasokh != null && e.MohlatPasokh < now))
                        .CountAsync();
                    FillKpi(res, cnt, null, null, null);
                    res.Unit = "نامه";
                    break;
                }
                case "off-inner-by-urgency":
                case "off-inner-by-conf":
                {
                    var isUrg = def.Key == "off-inner-by-urgency";
                    var rows = await InnerLettersIn(from, to)
                        .Select(i => isUrg ? i.Foriat : i.Mahramanegi)
                        .ToListAsync();
                    var buckets = rows
                        .GroupBy(v => string.IsNullOrWhiteSpace(v) ? "عادی" : v.Trim())
                        .Select(g => new { Label = g.Key, Cnt = g.Count() })
                        .OrderByDescending(g => g.Cnt)
                        .ToList();
                    var ser = new DashSeriesDto { Name = "نامه داخلی" };
                    ser.Labels = buckets.Select(b => b.Label).ToList();
                    ser.Values = buckets.Select(b => (decimal)b.Cnt).ToList();
                    res.Series.Add(ser);
                    break;
                }
                case "off-inner-trend":
                {
                    var months = LastMonths(12);
                    var mFrom = months[0].From;
                    var mTo = months[months.Count - 1].To;
                    var rows = await InnerLettersIn(mFrom, mTo)
                        .GroupBy(i => new { i.DateSabt.Year, i.DateSabt.Month, i.DateSabt.Day })
                        .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Cnt = g.Count() })
                        .ToListAsync();
                    res.Series.Add(MonthSeries("نامه داخلی", months,
                        rows.Select(r => JalaliRow(r.Year, r.Month, r.Day, r.Cnt)).ToList()));
                    break;
                }
                case "off-inner-top-senders":
                {
                    var rows = await InnerLettersIn(from, to)
                        .GroupBy(i => i.CreatorUserId)
                        .Select(g => new { UserId = g.Key, Cnt = g.Count() })
                        .OrderByDescending(x => x.Cnt)
                        .Take(limit)
                        .ToListAsync();
                    var ids = rows.Select(r => r.UserId).ToList();
                    var names = await _db.Users.Where(u => ids.Contains(u.Id))
                        .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim() })
                        .ToDictionaryAsync(u => u.Id, u => u.Name);
                    res.Columns = new List<string> { "ثبت‌کننده", "تعداد نامه" };
                    res.Rows = rows.Select(r => new DashRowDto
                    {
                        Cells = new List<string>
                        {
                            names.TryGetValue(r.UserId, out var n) && !string.IsNullOrWhiteSpace(n)
                                ? n : "کاربر " + Fa.Digits(r.UserId.ToString()),
                            Fa.Digits(r.Cnt.ToString())
                        }
                    }).ToList();
                    break;
                }

                // ==================== سایر ماژول‌ها ====================
                case "it-open":
                {
                    var open = await _db.ItRequests.CountAsync(r => r.Status != "Completed" && r.Status != "Rejected");
                    var all = await _db.ItRequests.CountAsync();
                    FillKpi(res, open, all, null, null);
                    res.SubLabel = "کل درخواست‌ها";
                    break;
                }
                case "hr-employees":
                {
                    var active = await _db.HrEmployees.CountAsync(e => e.IsActive);
                    var all = await _db.HrEmployees.CountAsync();
                    FillKpi(res, active, all, null, null);
                    res.SubLabel = "کل پرونده‌ها";
                    break;
                }
                case "fa-att-today":
                {
                    var t0 = DateTime.Now.Date;
                    var t1 = t0.AddDays(1);
                    var cnt = await _db.FaAttLogs.CountAsync(l => l.Timestamp >= t0 && l.Timestamp < t1);
                    var ppl = await _db.FaAttLogs.Where(l => l.Timestamp >= t0 && l.Timestamp < t1)
                                                 .Select(l => l.EmployeeId).Distinct().CountAsync();
                    FillKpi(res, cnt, ppl, null, null);
                    res.SubLabel = "نفر دارای تردد";
                    res.Unit = "رکورد";
                    break;
                }
                case "doc-expiring":
                {
                    var until = DateTime.Now.Date.AddDays(60);
                    var today = DateTime.Now.Date;
                    var rows = await _db.Documents
                        .Where(d => !d.IsDeleted && d.IsActive && d.ExpireDate != null && d.ExpireDate <= until)
                        .OrderBy(d => d.ExpireDate)
                        .Take(limit)
                        .Select(d => new { d.Id, d.Title, d.Code, d.ExpireDate })
                        .ToListAsync();
                    res.Columns = new List<string> { "مدرک", "کد", "انقضا" };
                    res.Rows = rows.Select(r => new DashRowDto
                    {
                        Href = $"doc-archive?doc={r.Id}",
                        Tone = r.ExpireDate != null && r.ExpireDate.Value < today ? "danger" : "warning",
                        Cells = new List<string>
                        {
                            r.Title,
                            string.IsNullOrEmpty(r.Code) ? "—" : r.Code,
                            r.ExpireDate is null ? "—" : Fa.Digits(PersianDate.ToShort(r.ExpireDate.Value))
                        }
                    }).ToList();
                    break;
                }

                default:
                    res.Error = "این ویجت هنوز پیاده‌سازی نشده است.";
                    break;
            }
        }
        catch (Exception ex)
        {
            var msg = ex.Message ?? "";
            // جدول نامه وارده در مایگریشن قدیمی وجود ندارد — پیام راهنما به‌جای خطای خام SQL
            res.Error = msg.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
                     || msg.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                ? "جدول‌های ماژول نامه در دیتابیس ساخته نشده‌اند (مثل IncomingLetters). " +
                  "سرویس را یک‌بار ری‌استارت کنید تا خودتعمیری اسکیما اجرا شود. جزئیات: " + msg
                : "خطا در محاسبهٔ ویجت: " + msg;
        }

        res.GeneratedAt = DateTime.Now;
        return res;
    }

    // =====================================================================
    //  کمکی‌ها
    // =====================================================================

    /// <summary>دستورهای کاری که کاربر یا دستوردهندهٔ آن است یا به او محول شده.</summary>
    private IQueryable<WorkOrder> MyWorkOrders(int userId) =>
        _db.WorkOrders.Where(w => w.DeletedAt == null
            && (w.OwnerUserId == userId
                || _db.WorkOrderAssignees.Any(a => a.OrderId == w.Id && a.UserId == userId)));

    /// <summary>همهٔ نامه‌ها (داخلی/صادره/وارده) که تاریخ ثبتشان در بازه است.</summary>
    private IQueryable<LetterSource> LettersIn(DateTime from, DateTime to) =>
        _db.LetterSources.Where(x => !x.IsDelete
            && ((x.InnerLetter != null && x.InnerLetter.DateSabt >= from && x.InnerLetter.DateSabt < to)
             || (x.OutgoingLetter != null && x.OutgoingLetter.DateSabt >= from && x.OutgoingLetter.DateSabt < to)
             || (x.IncomingLetter != null && x.IncomingLetter.DateErsal >= from && x.IncomingLetter.DateErsal < to)));

    /// <summary>نامه‌های صادرهٔ حذف‌نشده (از مسیر LetterSource تا حذف منطقی رعایت شود).</summary>
    private IQueryable<OutgoingLetter> OutLetters() =>
        _db.LetterSources.Where(x => !x.IsDelete && x.OutgoingLetter != null)
                         .Select(x => x.OutgoingLetter!);

    private IQueryable<OutgoingLetter> OutLettersIn(DateTime from, DateTime to) =>
        OutLetters().Where(o => o.DateSabt >= from && o.DateSabt < to);

    /// <summary>نامه‌های داخلی حذف‌نشده.</summary>
    private IQueryable<InnerLetter> InnerLetters() =>
        _db.LetterSources.Where(x => !x.IsDelete && x.InnerLetter != null)
                         .Select(x => x.InnerLetter!);

    private IQueryable<InnerLetter> InnerLettersIn(DateTime from, DateTime to) =>
        InnerLetters().Where(i => i.DateSabt >= from && i.DateSabt < to);

    private static string LetterTypeFa(int t) => t switch
    {
        1 => "داخلی",
        2 => "صادره",
        3 => "وارده",
        _ => "نامشخص"
    };

    private static string OutStatusFa(int s) => s switch
    {
        0 => "پیش‌نویس",
        1 => "در گردش تایید",
        2 => "تایید شده",
        3 => "صادر شده",
        _ => "نامشخص"
    };

    /// <summary>فوریت: داخلی/صادره رشته‌ای، وارده عددی — هر دو به متن یکسان.</summary>
    private static string UrgencyFa(string? text, int? code)
    {
        if (code is not null)
            return code switch { 0 => "عادی", 1 => "فوری", 2 => "خیلی فوری", 3 => "آنی", _ => "عادی" };
        return string.IsNullOrWhiteSpace(text) ? "عادی" : text.Trim();
    }

    private async Task<decimal> SaleSum(DateTime from, DateTime to) =>
        (decimal)(await _db.FacInvoices
            .Where(i => i.Status == InvoiceStatus.Confirmed && i.Kind == InvoiceKind.Sale
                        && i.Date >= from && i.Date < to)
            .SumAsync(i => (double?)i.TotalNet) ?? 0);

    private Task<int> SaleCount(DateTime from, DateTime to) =>
        _db.FacInvoices.CountAsync(i => i.Status == InvoiceStatus.Confirmed
                                        && i.Kind == InvoiceKind.Sale
                                        && i.Date >= from && i.Date < to);

    /// <summary>کالاهای فعالِ زیر نقطه سفارش (محاسبه در حافظه برای پرهیز از left-join پیچیده).</summary>
    private async Task<List<(int Id, string Name, string Unit, decimal ReorderPoint, decimal Qty)>> ReorderItemsAsync()
    {
        var products = await _db.Products
            .Where(p => p.IsActive && !p.IsService && p.ReorderPoint > 0)
            .Select(p => new { p.Id, p.Name, p.Unit, p.ReorderPoint })
            .ToListAsync();
        if (products.Count == 0) return new();

        var stock = await _db.Stocks
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => (double)x.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty);

        return products
            .Select(p => (p.Id, p.Name, p.Unit, p.ReorderPoint,
                          Qty: (decimal)stock.GetValueOrDefault(p.Id, 0d)))
            .Where(x => x.Qty < x.ReorderPoint)
            .OrderBy(x => x.Qty - x.ReorderPoint)
            .ToList();
    }

    private static void FillKpi(WidgetDataDto res, decimal value, decimal? sub, decimal? prev, string? prevLabel)
    {
        res.Value = value;
        if (sub is not null) { res.SubValue = sub; res.SubLabel ??= "تعداد سند"; }
        if (prev is null) return;

        if (prev.Value == 0)
        {
            res.DeltaText = value == 0 ? "بدون تغییر" : "جدید (دورهٔ قبل صفر)";
            res.DeltaUp = value > 0;
            return;
        }
        var pct = (value - prev.Value) / Math.Abs(prev.Value) * 100m;
        res.DeltaUp = pct >= 0;
        res.DeltaText = Fa.Digits($"{(pct >= 0 ? "+" : "")}{pct:F0}٪")
                        + (string.IsNullOrEmpty(prevLabel) ? "" : " " + prevLabel);
    }

    private static int Int(IReadOnlyDictionary<string, string> cfg, string key, int def) =>
        cfg.TryGetValue(key, out var v) && int.TryParse(v, out var n) && n > 0 ? Math.Min(n, 50) : def;

    private static string WoStatusFa(string s) => s switch
    {
        "Open" => "باز",
        "Closed" => "بسته",
        "Cancelled" => "لغوشده",
        _ => s
    };

    private record MonthBucket(int Year, int Month, DateTime From, DateTime To, string Label);

    /// <summary>۱۲ ماه شمسی اخیر (شامل ماه جاری).</summary>
    private static List<MonthBucket> LastMonths(int count)
    {
        var (jy, jm, _) = PersianDate.FromGregorian(DateTime.Now);
        var list = new List<MonthBucket>();
        for (var i = count - 1; i >= 0; i--)
        {
            var (y, m) = PersianDate.AddMonths(jy, jm, -i);
            var (ny, nm) = PersianDate.AddMonths(y, m, 1);
            list.Add(new MonthBucket(y, m,
                PersianDate.ToGregorian(y, m, 1),
                PersianDate.ToGregorian(ny, nm, 1),
                PersianDate.MonthName(m)));
        }
        return list;
    }

    private static DashSeriesDto MonthSeries(string name, List<MonthBucket> months,
        List<(int Year, int Month, decimal Value)> rows)
    {
        var ser = new DashSeriesDto { Name = name };
        foreach (var b in months)
        {
            ser.Labels.Add(b.Label);
            ser.Values.Add(rows.Where(r => r.Year == b.Year && r.Month == b.Month).Sum(r => r.Value));
        }
        return ser;
    }

    /// <summary>
    /// تبدیل یک ردیف روزانهٔ میلادی به سطل شمسی؛ چون سطل‌های ماهانه بر اساس تقویم شمسی هستند
    /// و گروه‌بندی پایگاه‌داده بر اساس سال/ماه/روز میلادی انجام می‌شود.
    /// </summary>
    private static (int Year, int Month, decimal Value) JalaliRow(int gy, int gm, int gd, decimal value)
    {
        var (jy, jm, _) = PersianDate.FromGregorian(new DateTime(gy, gm, gd));
        return (jy, jm, value);
    }

    /// <summary>مرز زمانی بازه — بر اساس تقویم شمسی. shift=-1 یعنی بازهٔ قبل (برای مقایسه).</summary>
    private static (DateTime From, DateTime To) Bounds(DashRange r, int shift)
    {
        var now = DateTime.Now;
        var (jy, jm, _) = PersianDate.FromGregorian(now);

        switch (r)
        {
            case DashRange.Today:
            {
                var d = now.Date.AddDays(shift);
                return (d, d.AddDays(1));
            }
            case DashRange.Week:
            {
                var start = now.Date.AddDays(-(int)now.DayOfWeek).AddDays(7 * shift);
                return (start, start.AddDays(7));
            }
            case DashRange.Month:
            {
                var (y, m) = PersianDate.AddMonths(jy, jm, shift);
                var (ny, nm) = PersianDate.AddMonths(y, m, 1);
                return (PersianDate.ToGregorian(y, m, 1), PersianDate.ToGregorian(ny, nm, 1));
            }
            case DashRange.Quarter:
            {
                var startMonth = ((jm - 1) / 3) * 3 + 1;   // ۱ / ۴ / ۷ / ۱۰
                var (y, m) = PersianDate.AddMonths(jy, startMonth, 3 * shift);
                var (ny, nm) = PersianDate.AddMonths(y, m, 3);
                return (PersianDate.ToGregorian(y, m, 1), PersianDate.ToGregorian(ny, nm, 1));
            }
            case DashRange.Year:
            {
                var y = jy + shift;
                return (PersianDate.ToGregorian(y, 1, 1), PersianDate.ToGregorian(y + 1, 1, 1));
            }
            default:
                return (new DateTime(1970, 1, 1), now.Date.AddDays(1));
        }
    }
}
