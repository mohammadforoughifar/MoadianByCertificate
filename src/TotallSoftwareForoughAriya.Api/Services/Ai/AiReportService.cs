using Inventory.Api.Data;
using Inventory.Api.Services.Export;
using Inventory.Api.Services.Invoicing;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// گزارش‌ساز دستیار: ساخت گزارش تحلیلی از روی داده سامانه + کش ۱۵ دقیقه‌ای
// برای دانلود اکسل. پیش‌نمایش (۱۵ سطر اول) در چت، فایل کامل در اکسل.
// =====================================================================

public class AiReportPreview
{
    public string ReportId { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Columns { get; set; } = new();
    /// <summary>سطرهای نمایشی چت (تاریخ‌ها شمسی و آماده نمایش).</summary>
    public List<List<object?>> Rows { get; set; } = new();
    public List<object?>? TotalRow { get; set; }
    public int TotalRows { get; set; }
}

public class AiDebtorRow
{
    public string Name { get; set; } = "";
    public decimal Total { get; set; }
    public int Count { get; set; }
}

public class AiReportService
{
    public const int PreviewRows = 15;
    public const int MaxExcelRows = 1000;
    private const int CacheMinutes = 15;

    private readonly AppDbContext _db;
    private readonly IInvoicingService _inv;
    private readonly IWarehousingService _wh;
    private readonly ITreasuryService _trs;
    private readonly IMemoryCache _cache;

    private record CachedReport(int UserId, ExportSpec Spec);

    public AiReportService(AppDbContext db, IInvoicingService inv, IWarehousingService wh,
        ITreasuryService trs, IMemoryCache cache)
    {
        _db = db;
        _inv = inv;
        _wh = wh;
        _trs = trs;
        _cache = cache;
    }

    /// <summary>بازیابی مشخصات گزارش برای دانلود — فقط سازنده، تا ۱۵ دقیقه.</summary>
    public ExportSpec? TryGetSpec(int userId, string reportId)
    {
        if (string.IsNullOrWhiteSpace(reportId)) return null;
        if (!_cache.TryGetValue(CachedReportKey(reportId), out CachedReport? cached)) return null;
        if (cached == null || cached.UserId != userId) return null;
        return cached.Spec;
    }

    private static string CachedReportKey(string reportId) => $"ai-report:{reportId}";

    private string StoreSpec(int userId, ExportSpec spec)
    {
        var id = Guid.NewGuid().ToString("N");
        spec.FileBaseName = $"gozaresh-{DateTime.Now:yyyyMMdd-HHmmss}";
        spec.Module = "فروغ آریا";
        spec.Notes.Add("ساخته‌شده با دستیار هوشمند فروغ آریا 🤖");
        _cache.Set(CachedReportKey(id), new CachedReport(userId, spec),
            TimeSpan.FromMinutes(CacheMinutes));
        return id;
    }

    // ---------------- فروش ----------------

    public async Task<AiReportPreview> BuildSalesAsync(int userId, InvoiceKind kind, string groupBy,
        DateTime from, DateTime to, string periodLabel, CancellationToken ct)
    {
        _ = ct;
        var gb = AiTextUtil.NormalizeFa(groupBy) switch
        {
            var g when g.Contains("مشتری") || g.Contains("طرف") || g == "party" => "party",
            var g when g.Contains("کالا") || g.Contains("محصول") || g == "product" => "product",
            var g when g.Contains("روز") || g == "day" => "day",
            _ => "month",
        };
        var firstCol = gb switch { "party" => "طرف حساب", "product" => "کالا", "day" => "روز", _ => "ماه" };
        var kindName = kind == InvoiceKind.Sale ? "فروش" : "خرید";
        var title = $"گزارش {kindName} به تفکیک {firstCol} — {periodLabel}";

        var r = await _inv.GetSummaryAsync(kind, gb, from, to);
        var spec = new ExportSpec
        {
            Title = title,
            Subtitle = $"دوره: {periodLabel}",
            Columns =
            {
                new ExportColumn(firstCol, ExportValueKind.Text, 30),
                new ExportColumn("تعداد", ExportValueKind.Int, 12) { Sum = true },
                new ExportColumn("مقدار", ExportValueKind.Number, 14) { Sum = true },
                new ExportColumn("مبلغ", ExportValueKind.Money, 20) { Sum = true },
            },
        };
        spec.Meta.Add(new ExportMeta("دوره", periodLabel));
        spec.Meta.Add(new ExportMeta("تعداد سطرها", r.Rows.Count.ToString()));
        foreach (var row in r.Rows.Take(MaxExcelRows))
            spec.Rows.Add(new ExportRow(row.Title, row.Count, row.Quantity, row.Net));

        var totalRow = new List<object?> { "جمع کل", r.TotalCount, r.TotalQuantity, r.TotalNet };
        return FinishPreview(userId, spec, spec.Rows.Select(x => x.Values).ToList(), totalRow);
    }

    // ---------------- موجودی ----------------

    public async Task<AiReportPreview> BuildStockAsync(int userId, string? search, CancellationToken ct)
    {
        var s = (search ?? "").Trim();
        var belowOnly = s == "";
        var page = await _wh.GetStockAsync(null, null, belowOnly ? null : s, belowOnly, 1, MaxExcelRows);
        var title = belowOnly ? "گزارش کالاهای زیر نقطه سفارش" : $"گزارش موجودی — جستجو: {s}";

        var spec = new ExportSpec
        {
            Title = title,
            Columns =
            {
                new ExportColumn("کد", ExportValueKind.Text, 14),
                new ExportColumn("کالا", ExportValueKind.Text, 34),
                new ExportColumn("انبار", ExportValueKind.Text, 20),
                new ExportColumn("موجودی", ExportValueKind.Number, 14),
                new ExportColumn("واحد", ExportValueKind.Text, 10),
                new ExportColumn("نقطه سفارش", ExportValueKind.Number, 14),
                new ExportColumn("وضعیت", ExportValueKind.Text, 12),
            },
        };
        spec.Meta.Add(new ExportMeta("تعداد سطرها", page.Items.Count.ToString()));
        var preview = new List<List<object?>>();
        foreach (var r in page.Items)
        {
            var status = r.BelowReorder ? "کمبود" : "موجود";
            spec.Rows.Add(new ExportRow(r.ProductCode, r.ProductName, r.WarehouseName, r.Quantity, r.Unit, r.ReorderPoint, status));
        }
        preview = spec.Rows.Select(x => x.Values).ToList();
        return FinishPreview(userId, spec, preview, null);
    }

    // ---------------- چک‌ها ----------------

    public async Task<AiReportPreview> BuildChequesAsync(int userId, ChequeKind? kind, int days, CancellationToken ct)
    {
        _ = ct;
        var today = DateTime.Today;
        var kindName = kind == ChequeKind.Received ? "دریافتی" : kind == ChequeKind.Issued ? "صادره" : "";
        var title = $"گزارش چک‌های {kindName} نزدیک سررسید".Replace("  ", " ") + $" (تا {days} روز آینده + معوق‌ها)";
        var page = await _trs.GetChequesAsync(kind, null, null, null, null,
            today.AddDays(-60), today.AddDays(days), true, 1, MaxExcelRows);

        var spec = new ExportSpec
        {
            Title = title,
            Columns =
            {
                new ExportColumn("شماره", ExportValueKind.Text, 16),
                new ExportColumn("نوع", ExportValueKind.Text, 12),
                new ExportColumn("طرف حساب", ExportValueKind.Text, 28),
                new ExportColumn("مبلغ", ExportValueKind.Money, 20) { Sum = true },
                new ExportColumn("بانک", ExportValueKind.Text, 18),
                new ExportColumn("سررسید", ExportValueKind.Date, 14),
                new ExportColumn("وضعیت", ExportValueKind.Text, 16),
            },
        };
        spec.Meta.Add(new ExportMeta("تعداد سطرها", page.Items.Count.ToString()));
        var preview = new List<List<object?>>();
        foreach (var c in page.Items)
        {
            var type = c.Kind == ChequeKind.Received ? "دریافتی" : "صادره";
            var status = c.DueDate.Date < today ? $"معوق ({(today - c.DueDate.Date).Days} روز)"
                : c.DueDate.Date == today ? "امروز" : $"{(c.DueDate.Date - today).Days} روز مانده";
            var party = c.PartyName ?? c.OwnerName ?? "";
            spec.Rows.Add(new ExportRow(c.Number, type, party, c.Amount, c.BankName, c.DueDate.Date, status));
            preview.Add(new List<object?> { c.Number, type, party, c.Amount, c.BankName, AiDateUtil.ToFaShort(c.DueDate), status });
        }
        return FinishPreview(userId, spec, preview, null);
    }

    // ---------------- بدهکاران ----------------

    /// <summary>مطالبات به تعریف خود سامانه: جمع فاکتور نسیه منهای برگشتی، نزولی.</summary>
    public async Task<List<AiDebtorRow>> GetDebtorRowsAsync(int limit, CancellationToken ct)
    {
        return await _db.FacInvoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Confirmed && i.Settlement == SettlementType.Credit
                && (i.Kind == InvoiceKind.Sale || i.Kind == InvoiceKind.SaleReturn))
            .GroupBy(i => i.Party == null ? "بدون طرف حساب" : i.Party.Name)
            .Select(g => new AiDebtorRow
            {
                Name = g.Key,
                Total = g.Sum(i => i.Kind == InvoiceKind.Sale ? i.TotalNet : -i.TotalNet),
                Count = g.Count(),
            })
            .Where(x => x.Total > 0)
            .OrderByDescending(x => x.Total)
            .Take(Math.Clamp(limit, 1, MaxExcelRows))
            .ToListAsync(ct);
    }

    public async Task<AiReportPreview> BuildDebtorsAsync(int userId, CancellationToken ct)
    {
        var rows = await GetDebtorRowsAsync(MaxExcelRows, ct);
        var title = "گزارش بدهکاران بزرگ";
        var spec = new ExportSpec
        {
            Title = title,
            Subtitle = "جمع فاکتور نسیه منهای برگشتی (تعریف سامانه)",
            Columns =
            {
                new ExportColumn("طرف حساب", ExportValueKind.Text, 36),
                new ExportColumn("جمع نسیه", ExportValueKind.Money, 22) { Sum = true },
                new ExportColumn("تعداد فاکتور", ExportValueKind.Int, 14) { Sum = true },
            },
        };
        spec.Meta.Add(new ExportMeta("تعداد سطرها", rows.Count.ToString()));
        foreach (var r in rows)
            spec.Rows.Add(new ExportRow(r.Name, r.Total, r.Count));
        var totalRow = new List<object?> { "جمع کل", rows.Sum(r => r.Total), rows.Sum(r => r.Count) };
        return FinishPreview(userId, spec, spec.Rows.Select(x => x.Values).ToList(), totalRow);
    }

    // ---------------- سفارشی (خروجی اکسپلورر داده §۱۵) ----------------

    /// <summary>ساخت اکسل از نتیجه کاوش داده؛ ستون‌های مبلغ/تعداد جمع خودکار می‌گیرند.</summary>
    public Task<AiReportPreview> BuildCustomAsync(int userId, string title,
        List<AiExploreColumn> columns, List<List<object?>> rows)
    {
        var spec = new ExportSpec { Title = title };
        foreach (var c in columns)
        {
            var kind = c.Kind switch
            {
                "money" => ExportValueKind.Money,
                "number" => ExportValueKind.Number,
                _ => ExportValueKind.Text,
            };
            // جمع خودکار فقط برای مبلغ‌ها و ستون تجمیع (نه کد/شناسه)
            var sum = c.Kind == "money" || c.Key == "__agg";
            spec.Columns.Add(new ExportColumn(c.Title, kind, c.Kind == "money" ? 20 : 16) { Sum = sum });
        }
        foreach (var r in rows.Take(MaxExcelRows))
            spec.Rows.Add(new ExportRow(r.ToArray()));
        return Task.FromResult(FinishPreview(userId, spec,
            spec.Rows.Select(x => x.Values).ToList(), null));
    }

    // ---------------- مشترک ----------------

    private AiReportPreview FinishPreview(int userId, ExportSpec spec, List<List<object?>> displayRows, List<object?>? totalRow)
    {
        var id = StoreSpec(userId, spec);
        return new AiReportPreview
        {
            ReportId = id,
            Title = spec.Title,
            Columns = spec.Columns.Select(c => c.Title).ToList(),
            Rows = displayRows.Take(PreviewRows).ToList(),
            TotalRow = totalRow,
            TotalRows = displayRows.Count,
        };
    }
}
