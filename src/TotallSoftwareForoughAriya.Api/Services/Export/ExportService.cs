using Inventory.Api.Services.Accounting;
using Inventory.Api.Services.Invoicing;
using Inventory.Api.Services.Stocktaking;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Export;

/// <summary>پارامترهای درخواست یک گزارش — همه اختیاری‌اند و هر گزارش آنچه لازم دارد را برمی‌دارد.</summary>
public class ExportQuery
{
    public int? Id { get; set; }
    public int? ProductId { get; set; }
    public int? WarehouseId { get; set; }
    public int? CategoryId { get; set; }
    public int? AccountId { get; set; }
    public int? PartyId { get; set; }
    public int? DocTypeId { get; set; }
    public int? TrsAccountId { get; set; }
    public int? SessionId { get; set; }

    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Kind { get; set; }
    public string? Nature { get; set; }
    public string? Level { get; set; }
    public string? GroupBy { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public bool Below { get; set; }
    public bool ActiveOnly { get; set; }
    public bool HideZero { get; set; }
    public bool IncludeChildren { get; set; }
    public bool OnlyDiff { get; set; }

    /// <summary>سقف سطرهای گزارش — برای جلوگیری از خروجی چندصدهزار سطری.</summary>
    public int MaxRows { get; set; } = 5000;
}

/// <summary>معرفی یک گزارش قابل خروجی‌گیری.</summary>
public record ExportReport(string Key, string Title, string Module, string RbacModule);

/// <summary>
/// ساخت <see cref="ExportSpec"/> برای گزارش‌های همه‌ی ماژول‌ها.
///
/// این سرویس هیچ کوئری مستقیمی به دیتابیس نمی‌زند؛ داده را از سرویس‌های
/// موجود هر ماژول می‌گیرد تا منطق گزارش‌ها دوباره‌نویسی و واگرا نشود.
/// </summary>
public class ExportService : IExportService
{
    private readonly IWarehousingService _wh;
    private readonly IAccountingService _acc;
    private readonly IInvoicingService _fac;
    private readonly ITreasuryService _trs;
    private readonly IStocktakingService _stk;

    public ExportService(IWarehousingService wh, IAccountingService acc,
        IInvoicingService fac, ITreasuryService trs, IStocktakingService stk)
    {
        _wh = wh;
        _acc = acc;
        _fac = fac;
        _trs = trs;
        _stk = stk;
    }

    // =====================================================================
    // فهرست گزارش‌ها — «مرکز خروجی» از همین می‌خواند
    // =====================================================================

    private static readonly ExportReport[] Catalog =
    {
        new("inv-kardex",   "کاردکس کالا",              "انبارداری",    "InvDocs"),
        new("inv-stock",    "موجودی انبار",             "انبارداری",    "Products"),
        new("inv-products", "فهرست کالاها",             "انبارداری",    "Products"),
        new("inv-docs",     "اسناد رسید و حواله",       "انبارداری",    "InvDocs"),

        new("acc-ledger",   "دفتر حساب",                "حسابداری",     "AccVouchers"),
        new("acc-journal",  "دفتر روزنامه",             "حسابداری",     "AccVouchers"),
        new("acc-trial",    "تراز آزمایشی",             "حسابداری",     "AccVouchers"),
        new("acc-vouchers", "فهرست اسناد حسابداری",     "حسابداری",     "AccVouchers"),

        new("fac-invoices", "فهرست فاکتورها",           "فاکتور",       "FacInvoices"),
        new("fac-summary",  "خلاصه فروش و خرید",        "فاکتور",       "FacInvoices"),

        new("trs-vouchers", "اسناد دریافت و پرداخت",    "خزانه‌داری",   "TrsVouchers"),
        new("trs-cheques",  "دفتر چک",                  "خزانه‌داری",   "TrsCheques"),
        new("trs-flow",     "گردش خزانه",               "خزانه‌داری",   "TrsAccounts"),

        new("stk-diff",     "مغایرت انبارگردانی",       "انبارگردانی",  "StkSessions"),

        new("doc-list",        "فهرست مدارک",              "آرشیو اسناد",  "DocArchive"),
        new("doc-expiring",    "مدارک رو به انقضا",        "آرشیو اسناد",  "DocArchive"),
        new("doc-history",     "تاریخچه مدرک",             "آرشیو اسناد",  "DocArchive"),
        new("doc-permissions", "ماتریس دسترسی پوشه‌ها",    "آرشیو اسناد",  "DocArchive"),
        new("doc-pending",     "ورژن‌های در انتظار تایید", "آرشیو اسناد",  "DocArchive")
    };

    public IReadOnlyList<ExportReport> GetCatalog() => Catalog;

    public ExportReport? Find(string key)
        => Catalog.FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    // =====================================================================
    // نقطه‌ی ورود
    // =====================================================================

    public async Task<ExportSpec> BuildAsync(string key, ExportQuery q)
    {
        var report = Find(key)
            ?? throw new InvalidOperationException($"گزارشی با شناسه «{key}» تعریف نشده است.");

        var spec = key.ToLowerInvariant() switch
        {
            "inv-kardex" => await KardexAsync(q),
            "inv-stock" => await StockAsync(q),
            "inv-products" => await ProductsAsync(q),
            "inv-docs" => await DocsAsync(q),

            "acc-ledger" => await LedgerAsync(q),
            "acc-journal" => await JournalAsync(q),
            "acc-trial" => await TrialAsync(q),
            "acc-vouchers" => await AccVouchersAsync(q),

            "fac-invoices" => await InvoicesAsync(q),
            "fac-summary" => await SummaryAsync(q),

            "trs-vouchers" => await TrsVouchersAsync(q),
            "trs-cheques" => await ChequesAsync(q),
            "trs-flow" => await FlowAsync(q),

            "stk-diff" => await DiffAsync(q),

            // گزارش‌های آرشیو اسناد نیاز به هویت کاربر دارند و در کنترلر
            // از طریق IDocArchiveExportService ساخته می‌شوند.
            _ => throw new InvalidOperationException($"گزارش «{key}» پیاده‌سازی نشده است.")
        };

        spec.Module = report.Module;
        spec.FileBaseName ??= report.Key;
        return spec;
    }

    // =====================================================================
    // ۱) انبارداری
    // =====================================================================

    private async Task<ExportSpec> KardexAsync(ExportQuery q)
    {
        if (q.ProductId is not > 0)
            throw new InvalidOperationException("برای گزارش کاردکس، کالا را انتخاب کنید.");

        var k = await _wh.GetKardexAsync(q.ProductId.Value, q.WarehouseId, q.From, q.To);

        var spec = new ExportSpec
        {
            Title = "کاردکس کالا",
            Subtitle = $"{k.ProductName} ({k.ProductCode})" +
                       (string.IsNullOrWhiteSpace(k.WarehouseName) ? "" : $" — {k.WarehouseName}"),
            Landscape = true,
            Columns =
            {
                new ExportColumn("تاریخ", ExportValueKind.Date, 58),
                new ExportColumn("شماره سند", ExportValueKind.Text, 66),
                new ExportColumn("نوع سند"),
                new ExportColumn("طرف حساب") { Wrap = true },
                new ExportColumn("مقدار ورود", ExportValueKind.Number) { Sum = true },
                new ExportColumn("بهای ورود", ExportValueKind.Money),
                new ExportColumn("ارزش ورود", ExportValueKind.Money) { Sum = true },
                new ExportColumn("مقدار خروج", ExportValueKind.Number) { Sum = true },
                new ExportColumn("بهای خروج", ExportValueKind.Money),
                new ExportColumn("ارزش خروج", ExportValueKind.Money) { Sum = true },
                new ExportColumn("مانده", ExportValueKind.Number),
                new ExportColumn("ارزش مانده", ExportValueKind.Money),
                new ExportColumn("شرح") { ExcelOnly = true, Wrap = true }
            }
        };

        AddPeriod(spec, q);
        spec.Meta.Add(new ExportMeta("واحد", k.Unit));
        spec.Meta.Add(new ExportMeta("روش قیمت‌گذاری", k.MethodTitle));

        spec.Summary.Add(new ExportMeta("مانده اول دوره", $"{Fa.Number(k.OpeningQty)} {k.Unit}"));
        spec.Summary.Add(new ExportMeta("ارزش اول دوره", Fa.Money(k.OpeningValue)));
        spec.Summary.Add(new ExportMeta("مانده پایان دوره", $"{Fa.Number(k.ClosingQty)} {k.Unit}"));
        spec.Summary.Add(new ExportMeta("ارزش پایان دوره", Fa.Money(k.ClosingValue)));

        // سطر افتتاحیه
        spec.Rows.Add(new ExportRow(null, null, "مانده اول دوره", null,
            null, null, null, null, null, null,
            k.OpeningQty, k.OpeningValue, null) { Style = ExportRowStyle.Muted });

        foreach (var r in k.Rows.Take(q.MaxRows))
        {
            spec.Rows.Add(new ExportRow(
                r.Date, r.Number, r.DocTypeName, r.PartyName,
                Zero(r.InQty), Zero(r.InPrice), Zero(r.InValue),
                Zero(r.OutQty), Zero(r.OutPrice), Zero(r.OutValue),
                r.BalanceQty, r.BalanceValue, r.Description));
        }

        return spec;
    }

    private async Task<ExportSpec> StockAsync(ExportQuery q)
    {
        var page = await _wh.GetStockAsync(q.WarehouseId, q.CategoryId, q.Search, q.Below, 1, q.MaxRows);

        var spec = new ExportSpec
        {
            Title = "موجودی انبار",
            Landscape = false,
            Columns =
            {
                new ExportColumn("کد کالا", ExportValueKind.Text, 62),
                new ExportColumn("نام کالا") { Wrap = true },
                new ExportColumn("گروه"),
                new ExportColumn("انبار"),
                new ExportColumn("واحد", ExportValueKind.Text, 40),
                new ExportColumn("موجودی", ExportValueKind.Number) { Sum = true },
                new ExportColumn("بهای میانگین", ExportValueKind.Money),
                new ExportColumn("ارزش موجودی", ExportValueKind.Money) { Sum = true },
                new ExportColumn("نقطه سفارش", ExportValueKind.Number) { ExcelOnly = true }
            }
        };

        AddFilters(spec, q);
        if (q.Below) spec.Meta.Add(new ExportMeta("فیلتر", "فقط کالاهای زیر نقطه سفارش"));

        spec.Summary.Add(new ExportMeta("تعداد قلم", Fa.Digits(page.TotalCount.ToString())));
        spec.Summary.Add(new ExportMeta("ارزش کل موجودی", Fa.Money(page.Items.Sum(x => x.Value))));
        spec.Summary.Add(new ExportMeta("زیر نقطه سفارش",
            Fa.Digits(page.Items.Count(x => x.BelowReorder).ToString())));

        foreach (var s in page.Items)
        {
            spec.Rows.Add(new ExportRow(
                s.ProductCode, s.ProductName, s.CategoryName, s.WarehouseName, s.Unit,
                s.Quantity, s.AvgCost, s.Value, s.ReorderPoint)
            {
                Style = s.BelowReorder ? ExportRowStyle.Danger : ExportRowStyle.Normal
            });
        }

        if (page.TotalCount > page.Items.Count)
            spec.Notes.Add($"این گزارش {page.Items.Count} سطر از مجموع {page.TotalCount} سطر را نشان می‌دهد.");

        return spec;
    }

    private async Task<ExportSpec> ProductsAsync(ExportQuery q)
    {
        var page = await _wh.GetProductsAsync(q.Search, q.CategoryId, q.WarehouseId,
            q.Below, q.ActiveOnly, 1, q.MaxRows);

        var spec = new ExportSpec
        {
            Title = "فهرست کالاها",
            Landscape = true,
            Columns =
            {
                new ExportColumn("کد کالا", ExportValueKind.Text, 62),
                new ExportColumn("نام کالا") { Wrap = true },
                new ExportColumn("گروه"),
                new ExportColumn("واحد", ExportValueKind.Text, 42),
                new ExportColumn("بارکد", ExportValueKind.Text, 78),
                new ExportColumn("کد مالیاتی", ExportValueKind.Text, 66),
                new ExportColumn("قیمت خرید", ExportValueKind.Money),
                new ExportColumn("قیمت فروش", ExportValueKind.Money),
                new ExportColumn("موجودی کل", ExportValueKind.Number) { Sum = true },
                new ExportColumn("نقطه سفارش", ExportValueKind.Number) { ExcelOnly = true },
                new ExportColumn("فعال", ExportValueKind.Bool, 40)
            }
        };

        AddFilters(spec, q);
        spec.Summary.Add(new ExportMeta("تعداد کالا", Fa.Digits(page.TotalCount.ToString())));

        foreach (var p in page.Items)
        {
            spec.Rows.Add(new ExportRow(
                p.Code, p.Name, p.CategoryName, p.Unit, p.Barcode, p.TaxCode,
                p.PurchasePrice, p.SalePrice, p.TotalStock, p.ReorderPoint, p.IsActive));
        }

        if (page.TotalCount > page.Items.Count)
            spec.Notes.Add($"این گزارش {page.Items.Count} سطر از مجموع {page.TotalCount} سطر را نشان می‌دهد.");

        return spec;
    }

    private async Task<ExportSpec> DocsAsync(ExportQuery q)
    {
        var nature = ParseEnum<StockNature>(q.Nature);
        var status = ParseEnum<InvDocStatus>(q.Status);

        var page = await _wh.GetDocsAsync(nature, q.DocTypeId, q.WarehouseId, status,
            q.Search, q.From, q.To, 1, q.MaxRows);

        var spec = new ExportSpec
        {
            Title = "اسناد رسید و حواله",
            Landscape = true,
            Columns =
            {
                new ExportColumn("شماره", ExportValueKind.Text, 72),
                new ExportColumn("تاریخ", ExportValueKind.Date, 58),
                new ExportColumn("نوع سند"),
                new ExportColumn("ماهیت", ExportValueKind.Text, 50),
                new ExportColumn("انبار"),
                new ExportColumn("طرف حساب") { Wrap = true },
                new ExportColumn("تعداد اقلام", ExportValueKind.Number),
                new ExportColumn("مبلغ کل", ExportValueKind.Money) { Sum = true },
                new ExportColumn("وضعیت", ExportValueKind.Text, 56),
                new ExportColumn("شرح") { ExcelOnly = true, Wrap = true }
            }
        };

        AddPeriod(spec, q);
        AddFilters(spec, q);

        spec.Summary.Add(new ExportMeta("تعداد سند", Fa.Digits(page.TotalCount.ToString())));
        spec.Summary.Add(new ExportMeta("جمع مبلغ", Fa.Money(page.Items.Sum(x => x.TotalValue))));

        foreach (var d in page.Items)
        {
            spec.Rows.Add(new ExportRow(
                d.Number, d.Date, d.DocTypeName, NatureTitle(d.Nature), d.WarehouseName,
                d.PartyName, d.TotalQuantity, d.TotalValue, StatusTitle(d.Status), d.Description)
            {
                Style = d.Status == InvDocStatus.Cancelled ? ExportRowStyle.Muted : ExportRowStyle.Normal
            });
        }

        return spec;
    }

    // =====================================================================
    // ۲) حسابداری
    // =====================================================================

    private async Task<ExportSpec> LedgerAsync(ExportQuery q)
    {
        if (q.AccountId is not > 0)
            throw new InvalidOperationException("برای دفتر حساب، حساب را انتخاب کنید.");

        var l = await _acc.GetLedgerAsync(q.AccountId.Value, q.From, q.To, q.IncludeChildren);

        var spec = new ExportSpec
        {
            Title = "دفتر حساب",
            Subtitle = $"{l.AccountCode} — {l.AccountName}" +
                       (string.IsNullOrWhiteSpace(l.LevelTitle) ? "" : $" ({l.LevelTitle})"),
            Landscape = true,
            Columns =
            {
                new ExportColumn("تاریخ", ExportValueKind.Date, 58),
                new ExportColumn("سند", ExportValueKind.Int, 46),
                new ExportColumn("ردیف", ExportValueKind.Int, 40) { ExcelOnly = true },
                new ExportColumn("کد حساب", ExportValueKind.Text, 62),
                new ExportColumn("نام حساب") { Wrap = true },
                new ExportColumn("طرف حساب"),
                new ExportColumn("شرح") { Wrap = true },
                new ExportColumn("بدهکار", ExportValueKind.Money) { Sum = true },
                new ExportColumn("بستانکار", ExportValueKind.Money) { Sum = true },
                new ExportColumn("مانده", ExportValueKind.Money),
                new ExportColumn("تشخیص", ExportValueKind.Text, 40)
            }
        };

        AddPeriod(spec, q);
        if (q.IncludeChildren) spec.Meta.Add(new ExportMeta("دامنه", "شامل زیرحساب‌ها"));

        spec.Summary.Add(new ExportMeta("مانده ابتدای دوره", Fa.Money(l.OpeningBalance)));
        spec.Summary.Add(new ExportMeta("جمع بدهکار", Fa.Money(l.TotalDebit)));
        spec.Summary.Add(new ExportMeta("جمع بستانکار", Fa.Money(l.TotalCredit)));
        spec.Summary.Add(new ExportMeta("مانده پایان دوره", Fa.Money(l.ClosingBalance)));

        spec.Rows.Add(new ExportRow(null, null, null, null, "مانده ابتدای دوره", null, null,
            null, null, l.OpeningBalance, null) { Style = ExportRowStyle.Muted });

        foreach (var r in l.Rows.Take(q.MaxRows))
        {
            spec.Rows.Add(new ExportRow(
                r.Date, r.VoucherNumber, r.RowNo, r.AccountCode, r.AccountName,
                r.PartyName, r.Description, Zero(r.Debit), Zero(r.Credit), r.Balance, r.BalanceSide));
        }

        return spec;
    }

    private async Task<ExportSpec> JournalAsync(ExportQuery q)
    {
        var page = await _acc.GetJournalAsync(q.From, q.To, 1, q.MaxRows);

        var spec = new ExportSpec
        {
            Title = "دفتر روزنامه",
            Landscape = true,
            Columns =
            {
                new ExportColumn("تاریخ", ExportValueKind.Date, 58),
                new ExportColumn("سند", ExportValueKind.Int, 46),
                new ExportColumn("ردیف", ExportValueKind.Int, 40),
                new ExportColumn("کد حساب", ExportValueKind.Text, 62),
                new ExportColumn("نام حساب") { Wrap = true },
                new ExportColumn("طرف حساب"),
                new ExportColumn("شرح") { Wrap = true },
                new ExportColumn("بدهکار", ExportValueKind.Money) { Sum = true },
                new ExportColumn("بستانکار", ExportValueKind.Money) { Sum = true }
            }
        };

        AddPeriod(spec, q);
        spec.Summary.Add(new ExportMeta("تعداد آرتیکل", Fa.Digits(page.TotalCount.ToString())));
        spec.Summary.Add(new ExportMeta("جمع بدهکار", Fa.Money(page.Items.Sum(x => x.Debit))));
        spec.Summary.Add(new ExportMeta("جمع بستانکار", Fa.Money(page.Items.Sum(x => x.Credit))));

        foreach (var r in page.Items)
        {
            spec.Rows.Add(new ExportRow(
                r.Date, r.VoucherNumber, r.RowNo, r.AccountCode, r.AccountName,
                r.PartyName, r.Description, Zero(r.Debit), Zero(r.Credit)));
        }

        if (page.TotalCount > page.Items.Count)
            spec.Notes.Add($"این گزارش {page.Items.Count} آرتیکل از مجموع {page.TotalCount} آرتیکل را نشان می‌دهد.");

        return spec;
    }

    private async Task<ExportSpec> TrialAsync(ExportQuery q)
    {
        var level = ParseEnum<AccountLevel>(q.Level) ?? AccountLevel.Subsidiary;
        var t = await _acc.GetTrialBalanceAsync(level, q.From, q.To, q.HideZero);

        var spec = new ExportSpec
        {
            Title = "تراز آزمایشی",
            Subtitle = $"سطح {LevelTitle(level)} — {(t.IsBalanced ? "تراز است" : "تراز نیست")}",
            Landscape = true,
            Columns =
            {
                new ExportColumn("کد حساب", ExportValueKind.Text, 66),
                new ExportColumn("نام حساب") { Wrap = true },
                new ExportColumn("بدهکار ابتدا", ExportValueKind.Money) { Sum = true },
                new ExportColumn("بستانکار ابتدا", ExportValueKind.Money) { Sum = true },
                new ExportColumn("بدهکار دوره", ExportValueKind.Money) { Sum = true },
                new ExportColumn("بستانکار دوره", ExportValueKind.Money) { Sum = true },
                new ExportColumn("بدهکار پایان", ExportValueKind.Money) { Sum = true },
                new ExportColumn("بستانکار پایان", ExportValueKind.Money) { Sum = true }
            }
        };

        AddPeriod(spec, q);
        if (q.HideZero) spec.Meta.Add(new ExportMeta("فیلتر", "حساب‌های بدون گردش پنهان"));

        spec.Summary.Add(new ExportMeta("تعداد حساب", Fa.Digits(t.Rows.Count.ToString())));
        spec.Summary.Add(new ExportMeta("جمع بدهکار پایان", Fa.Money(t.SumClosingDebit)));
        spec.Summary.Add(new ExportMeta("جمع بستانکار پایان", Fa.Money(t.SumClosingCredit)));
        spec.Summary.Add(new ExportMeta("وضعیت تراز", t.IsBalanced ? "تراز" : "ناتراز"));

        foreach (var r in t.Rows.Take(q.MaxRows))
        {
            spec.Rows.Add(new ExportRow(
                r.AccountCode, r.AccountName,
                Zero(r.OpeningDebit), Zero(r.OpeningCredit),
                Zero(r.PeriodDebit), Zero(r.PeriodCredit),
                Zero(r.ClosingDebit), Zero(r.ClosingCredit))
            { Indent = r.Depth });
        }

        spec.TotalRow = new ExportRow("", "جمع کل",
            t.SumOpeningDebit, t.SumOpeningCredit,
            t.SumPeriodDebit, t.SumPeriodCredit,
            t.SumClosingDebit, t.SumClosingCredit)
        { Style = ExportRowStyle.Total };

        if (!t.IsBalanced)
            spec.Notes.Add("توجه: جمع بدهکار و بستانکار برابر نیست؛ اسناد ناتراز را بررسی کنید.");

        return spec;
    }

    private async Task<ExportSpec> AccVouchersAsync(ExportQuery q)
    {
        var status = ParseEnum<VoucherStatus>(q.Status);
        var page = await _acc.GetVouchersAsync(null, status, null, q.Search, q.From, q.To, 1, q.MaxRows);

        var spec = new ExportSpec
        {
            Title = "فهرست اسناد حسابداری",
            Columns =
            {
                new ExportColumn("شماره", ExportValueKind.Int, 50),
                new ExportColumn("تاریخ", ExportValueKind.Date, 58),
                new ExportColumn("شرح") { Wrap = true },
                new ExportColumn("منشأ"),
                new ExportColumn("تعداد آرتیکل", ExportValueKind.Int),
                new ExportColumn("بدهکار", ExportValueKind.Money) { Sum = true },
                new ExportColumn("بستانکار", ExportValueKind.Money) { Sum = true },
                new ExportColumn("وضعیت", ExportValueKind.Text, 56)
            }
        };

        AddPeriod(spec, q);
        spec.Summary.Add(new ExportMeta("تعداد سند", Fa.Digits(page.TotalCount.ToString())));
        spec.Summary.Add(new ExportMeta("جمع بدهکار", Fa.Money(page.Items.Sum(x => x.TotalDebit))));

        foreach (var v in page.Items)
        {
            spec.Rows.Add(new ExportRow(
                v.Number, v.Date, v.Description, SourceTitle(v.Source), v.LineCount,
                v.TotalDebit, v.TotalCredit, VoucherStatusTitle(v.Status))
            {
                Style = v.Status == VoucherStatus.Cancelled ? ExportRowStyle.Muted
                      : !v.IsBalanced ? ExportRowStyle.Danger : ExportRowStyle.Normal
            });
        }

        return spec;
    }

    // =====================================================================
    // ۳) فاکتور
    // =====================================================================

    private async Task<ExportSpec> InvoicesAsync(ExportQuery q)
    {
        var kind = ParseEnum<InvoiceKind>(q.Kind);
        var status = ParseEnum<InvoiceStatus>(q.Status);

        var page = await _fac.GetInvoicesAsync(kind, status, q.PartyId, q.WarehouseId,
            q.Search, q.From, q.To, 1, q.MaxRows);

        var spec = new ExportSpec
        {
            Title = "فهرست فاکتورها",
            Subtitle = kind is null ? null : page.Items.FirstOrDefault()?.KindTitle,
            Landscape = true,
            Columns =
            {
                new ExportColumn("شماره", ExportValueKind.Int, 50),
                new ExportColumn("تاریخ", ExportValueKind.Date, 58),
                new ExportColumn("نوع", ExportValueKind.Text, 60),
                new ExportColumn("طرف حساب") { Wrap = true },
                new ExportColumn("انبار"),
                new ExportColumn("تعداد", ExportValueKind.Number),
                new ExportColumn("مبلغ ناخالص", ExportValueKind.Money) { Sum = true },
                new ExportColumn("تخفیف", ExportValueKind.Money) { Sum = true },
                new ExportColumn("مالیات", ExportValueKind.Money) { Sum = true },
                new ExportColumn("مبلغ نهایی", ExportValueKind.Money) { Sum = true },
                new ExportColumn("وضعیت", ExportValueKind.Text, 56)
            }
        };

        AddPeriod(spec, q);
        spec.Summary.Add(new ExportMeta("تعداد فاکتور", Fa.Digits(page.TotalCount.ToString())));
        spec.Summary.Add(new ExportMeta("جمع مبلغ نهایی", Fa.Money(page.Items.Sum(x => x.TotalNet))));
        spec.Summary.Add(new ExportMeta("جمع مالیات", Fa.Money(page.Items.Sum(x => x.TotalVat))));

        foreach (var f in page.Items)
        {
            spec.Rows.Add(new ExportRow(
                f.Number, f.Date, f.KindTitle, f.PartyName, f.WarehouseName, f.TotalQuantity,
                f.TotalGross, f.TotalLineDiscount + f.InvoiceDiscount, f.TotalVat, f.TotalNet,
                f.StatusTitle)
            {
                Style = f.Status == InvoiceStatus.Cancelled ? ExportRowStyle.Muted : ExportRowStyle.Normal
            });
        }

        return spec;
    }

    private async Task<ExportSpec> SummaryAsync(ExportQuery q)
    {
        var kind = ParseEnum<InvoiceKind>(q.Kind) ?? InvoiceKind.Sale;
        var groupBy = string.IsNullOrWhiteSpace(q.GroupBy) ? "month" : q.GroupBy;

        var s = await _fac.GetSummaryAsync(kind, groupBy, q.From, q.To);

        var spec = new ExportSpec
        {
            Title = "خلاصه فروش و خرید",
            Subtitle = $"{(kind == InvoiceKind.Sale ? "فروش" : "خرید")} — گروه‌بندی بر اساس {GroupByTitle(groupBy)}",
            Columns =
            {
                new ExportColumn("عنوان") { Wrap = true },
                new ExportColumn("تعداد فاکتور", ExportValueKind.Int) { Sum = true },
                new ExportColumn("مقدار", ExportValueKind.Number) { Sum = true },
                new ExportColumn("مبلغ مشمول", ExportValueKind.Money) { Sum = true },
                new ExportColumn("مالیات", ExportValueKind.Money) { Sum = true },
                new ExportColumn("مبلغ نهایی", ExportValueKind.Money) { Sum = true }
            }
        };

        AddPeriod(spec, q);
        spec.Summary.Add(new ExportMeta("تعداد کل", Fa.Digits(s.TotalCount.ToString())));
        spec.Summary.Add(new ExportMeta("جمع مشمول", Fa.Money(s.TotalTaxable)));
        spec.Summary.Add(new ExportMeta("جمع مالیات", Fa.Money(s.TotalVat)));
        spec.Summary.Add(new ExportMeta("جمع نهایی", Fa.Money(s.TotalNet)));

        foreach (var r in s.Rows)
            spec.Rows.Add(new ExportRow(r.Title, r.Count, r.Quantity, r.Taxable, r.Vat, r.Net));

        spec.TotalRow = new ExportRow("جمع کل", s.TotalCount, s.TotalQuantity,
            s.TotalTaxable, s.TotalVat, s.TotalNet)
        { Style = ExportRowStyle.Total };

        return spec;
    }

    // =====================================================================
    // ۴) خزانه‌داری
    // =====================================================================

    private async Task<ExportSpec> TrsVouchersAsync(ExportQuery q)
    {
        var kind = ParseEnum<TreasuryKind>(q.Kind);
        var status = ParseEnum<TreasuryStatus>(q.Status);

        var page = await _trs.GetVouchersAsync(kind, status, q.PartyId, q.TrsAccountId,
            q.Search, q.From, q.To, 1, q.MaxRows);

        var spec = new ExportSpec
        {
            Title = "اسناد دریافت و پرداخت",
            Landscape = true,
            Columns =
            {
                new ExportColumn("شماره", ExportValueKind.Int, 50),
                new ExportColumn("تاریخ", ExportValueKind.Date, 58),
                new ExportColumn("نوع", ExportValueKind.Text, 60),
                new ExportColumn("طرف حساب") { Wrap = true },
                new ExportColumn("از حساب"),
                new ExportColumn("به حساب"),
                new ExportColumn("فاکتور مرتبط", ExportValueKind.Int) { ExcelOnly = true },
                new ExportColumn("مبلغ", ExportValueKind.Money) { Sum = true },
                new ExportColumn("کارمزد", ExportValueKind.Money) { Sum = true },
                new ExportColumn("وضعیت", ExportValueKind.Text, 56),
                new ExportColumn("شرح") { ExcelOnly = true, Wrap = true }
            }
        };

        AddPeriod(spec, q);
        spec.Summary.Add(new ExportMeta("تعداد سند", Fa.Digits(page.TotalCount.ToString())));
        spec.Summary.Add(new ExportMeta("جمع دریافت",
            Fa.Money(page.Items.Where(x => x.Kind == TreasuryKind.Receipt).Sum(x => x.TotalAmount))));
        spec.Summary.Add(new ExportMeta("جمع پرداخت",
            Fa.Money(page.Items.Where(x => x.Kind == TreasuryKind.Payment).Sum(x => x.TotalAmount))));

        foreach (var v in page.Items)
        {
            spec.Rows.Add(new ExportRow(
                v.Number, v.Date, v.KindTitle, v.PartyName, v.FromAccountName, v.ToAccountName,
                v.InvoiceNumber, v.TotalAmount, Zero(v.FeeAmount), v.StatusTitle, v.Description)
            {
                Style = v.Status == TreasuryStatus.Cancelled ? ExportRowStyle.Muted
                      : v.Kind == TreasuryKind.Receipt ? ExportRowStyle.Success
                      : ExportRowStyle.Normal
            });
        }

        return spec;
    }

    private async Task<ExportSpec> ChequesAsync(ExportQuery q)
    {
        var kind = ParseEnum<ChequeKind>(q.Kind);
        var status = ParseEnum<ChequeStatus>(q.Status);

        var page = await _trs.GetChequesAsync(kind, status, q.PartyId, q.TrsAccountId,
            q.Search, q.From, q.To, false, 1, q.MaxRows);

        var spec = new ExportSpec
        {
            Title = "دفتر چک",
            Landscape = true,
            Columns =
            {
                new ExportColumn("نوع", ExportValueKind.Text, 62),
                new ExportColumn("شماره چک", ExportValueKind.Text, 80),
                new ExportColumn("شناسه صیاد", ExportValueKind.Text, 92) { ExcelOnly = true },
                new ExportColumn("بانک"),
                new ExportColumn("صاحب حساب") { Wrap = true },
                new ExportColumn("طرف حساب") { Wrap = true },
                new ExportColumn("تاریخ صدور", ExportValueKind.Date, 58),
                new ExportColumn("سررسید", ExportValueKind.Date, 58),
                new ExportColumn("مبلغ", ExportValueKind.Money) { Sum = true },
                new ExportColumn("وضعیت", ExportValueKind.Text, 66)
            }
        };

        AddPeriod(spec, q);

        var open = page.Items.Where(c =>
            c.Status is ChequeStatus.InHand or ChequeStatus.InCollection).ToList();

        spec.Summary.Add(new ExportMeta("تعداد چک", Fa.Digits(page.TotalCount.ToString())));
        spec.Summary.Add(new ExportMeta("جمع مبلغ", Fa.Money(page.Items.Sum(x => x.Amount))));
        spec.Summary.Add(new ExportMeta("چک‌های باز", Fa.Digits(open.Count.ToString())));
        spec.Summary.Add(new ExportMeta("مبلغ چک‌های باز", Fa.Money(open.Sum(x => x.Amount))));

        foreach (var c in page.Items)
        {
            spec.Rows.Add(new ExportRow(
                c.KindTitle, c.Number, c.SayadId, c.BankName, c.OwnerName, c.PartyName,
                c.IssueDate, c.DueDate, c.Amount, c.StatusTitle)
            {
                Style = c.Status switch
                {
                    ChequeStatus.Bounced => ExportRowStyle.Danger,
                    ChequeStatus.Cleared => ExportRowStyle.Success,
                    ChequeStatus.Cancelled => ExportRowStyle.Muted,
                    _ => ExportRowStyle.Normal
                }
            });
        }

        return spec;
    }

    private async Task<ExportSpec> FlowAsync(ExportQuery q)
    {
        if (q.TrsAccountId is not > 0)
            throw new InvalidOperationException("برای گردش خزانه، حساب صندوق یا بانک را انتخاب کنید.");

        var f = await _trs.GetFlowAsync(q.TrsAccountId.Value, q.From, q.To);

        var spec = new ExportSpec
        {
            Title = "گردش خزانه",
            Subtitle = $"{f.AccountName} ({f.AccountKindTitle})",
            Columns =
            {
                new ExportColumn("تاریخ", ExportValueKind.Date, 58),
                new ExportColumn("سند", ExportValueKind.Int, 46),
                new ExportColumn("نوع", ExportValueKind.Text, 60),
                new ExportColumn("روش", ExportValueKind.Text, 60),
                new ExportColumn("طرف حساب") { Wrap = true },
                new ExportColumn("شرح") { Wrap = true },
                new ExportColumn("دریافت", ExportValueKind.Money) { Sum = true },
                new ExportColumn("پرداخت", ExportValueKind.Money) { Sum = true },
                new ExportColumn("مانده", ExportValueKind.Money)
            }
        };

        AddPeriod(spec, q);
        spec.Summary.Add(new ExportMeta("مانده ابتدای دوره", Fa.Money(f.OpeningBalance)));
        spec.Summary.Add(new ExportMeta("جمع دریافت", Fa.Money(f.TotalIn)));
        spec.Summary.Add(new ExportMeta("جمع پرداخت", Fa.Money(f.TotalOut)));
        spec.Summary.Add(new ExportMeta("مانده پایان دوره", Fa.Money(f.ClosingBalance)));

        spec.Rows.Add(new ExportRow(null, null, null, null, null, "مانده ابتدای دوره",
            null, null, f.OpeningBalance) { Style = ExportRowStyle.Muted });

        foreach (var r in f.Rows.Take(q.MaxRows))
        {
            spec.Rows.Add(new ExportRow(
                r.Date, r.Number, KindTitle(r.Kind), r.Method, r.PartyName, r.Description,
                Zero(r.In), Zero(r.Out), r.Balance)
            {
                Style = r.In > 0 ? ExportRowStyle.Success : ExportRowStyle.Normal
            });
        }

        return spec;
    }

    // =====================================================================
    // ۵) انبارگردانی
    // =====================================================================

    private async Task<ExportSpec> DiffAsync(ExportQuery q)
    {
        var id = q.SessionId ?? q.Id ?? 0;
        if (id <= 0)
            throw new InvalidOperationException("برای گزارش مغایرت، دوره انبارگردانی را انتخاب کنید.");

        var d = await _stk.GetDiffAsync(id, q.OnlyDiff);

        var spec = new ExportSpec
        {
            Title = "گزارش مغایرت انبارگردانی",
            Subtitle = $"{d.Title} — شماره {Fa.Digits(d.Number.ToString())} — {d.WarehouseName}",
            Landscape = true,
            Columns =
            {
                new ExportColumn("کد کالا", ExportValueKind.Text, 62),
                new ExportColumn("نام کالا") { Wrap = true },
                new ExportColumn("واحد", ExportValueKind.Text, 44),
                new ExportColumn("قفسه", ExportValueKind.Text, 52),
                new ExportColumn("موجودی سیستم", ExportValueKind.Number) { Sum = true },
                new ExportColumn("شمارش", ExportValueKind.Number) { Sum = true },
                new ExportColumn("مغایرت", ExportValueKind.Number) { Sum = true },
                new ExportColumn("بهای واحد", ExportValueKind.Money),
                new ExportColumn("ارزش مغایرت", ExportValueKind.Money) { Sum = true },
                new ExportColumn("شمارش شده", ExportValueKind.Bool, 50),
                new ExportColumn("یادداشت") { ExcelOnly = true, Wrap = true }
            }
        };

        spec.Meta.Add(new ExportMeta("تاریخ دوره", PersianDate.ToShort(d.Date)));
        spec.Meta.Add(new ExportMeta("وضعیت", StocktakeStatusTitle(d.Status)));
        if (q.OnlyDiff) spec.Meta.Add(new ExportMeta("فیلتر", "فقط اقلام دارای مغایرت"));

        spec.Summary.Add(new ExportMeta("اقلام شمارش‌شده",
            $"{Fa.Digits(d.CountedLines.ToString())} از {Fa.Digits(d.TotalLines.ToString())}"));
        spec.Summary.Add(new ExportMeta("ارزش اضافی", Fa.Money(d.SurplusValue)));
        spec.Summary.Add(new ExportMeta("ارزش کسری", Fa.Money(d.ShortageValue)));
        spec.Summary.Add(new ExportMeta("دقت انبار", Fa.Digits(d.Accuracy.ToString("0.#")) + "٪"));

        foreach (var r in d.Rows.Take(q.MaxRows))
        {
            spec.Rows.Add(new ExportRow(
                r.ProductCode, r.ProductName, r.Unit, r.ShelfCode,
                r.SystemQty, r.IsCounted ? r.CountedQty : null,
                r.IsCounted ? r.Diff : null, r.UnitCost,
                r.IsCounted ? r.DiffValue : null, r.IsCounted, r.Note)
            {
                Style = !r.IsCounted ? ExportRowStyle.Muted
                      : r.Diff > 0 ? ExportRowStyle.Success
                      : r.Diff < 0 ? ExportRowStyle.Danger
                      : ExportRowStyle.Normal
            });
        }

        spec.Notes.Add("مغایرت مثبت یعنی اضافی انبار و مغایرت منفی یعنی کسری انبار.");
        if (d.UncountedLines > 0)
            spec.Notes.Add($"{d.UncountedLines} قلم شمارش نشده است و در جمع مغایرت لحاظ نشده.");

        return spec;
    }

    // =====================================================================
    // کمکی
    // =====================================================================

    /// <summary>صفر را به null تبدیل می‌کند تا در گزارش «—» چاپ شود نه «۰».</summary>
    private static decimal? Zero(decimal v) => v == 0 ? null : v;

    private static void AddPeriod(ExportSpec spec, ExportQuery q)
    {
        if (q.From is not null) spec.Meta.Add(new ExportMeta("از تاریخ", PersianDate.ToShort(q.From.Value)));
        if (q.To is not null) spec.Meta.Add(new ExportMeta("تا تاریخ", PersianDate.ToShort(q.To.Value)));
        if (q.From is null && q.To is null) spec.Meta.Add(new ExportMeta("دوره", "از ابتدا تا کنون"));
    }

    private static void AddFilters(ExportSpec spec, ExportQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.Search)) spec.Meta.Add(new ExportMeta("جستجو", q.Search));
        if (q.ActiveOnly) spec.Meta.Add(new ExportMeta("وضعیت", "فقط فعال"));
    }

    private static T? ParseEnum<T>(string? text) where T : struct, Enum
        => string.IsNullOrWhiteSpace(text) ? null
           : Enum.TryParse<T>(text, true, out var v) ? v : null;

    private static string NatureTitle(StockNature n) => n switch
    {
        StockNature.Increase => "افزایشی",
        StockNature.Decrease => "کاهشی",
        _ => "خنثی"
    };

    private static string StatusTitle(InvDocStatus s) => s switch
    {
        InvDocStatus.Draft => "پیش‌نویس",
        InvDocStatus.Confirmed => "قطعی",
        _ => "ابطال"
    };

    private static string VoucherStatusTitle(VoucherStatus s) => s switch
    {
        VoucherStatus.Draft => "پیش‌نویس",
        VoucherStatus.Confirmed => "قطعی",
        _ => "ابطال"
    };

    private static string SourceTitle(VoucherSource s) => s switch
    {
        VoucherSource.InventoryDoc => "سند انبار",
        VoucherSource.Invoice => "فاکتور",
        VoucherSource.StoreSale => "فروش فروشگاه",
        VoucherSource.Opening => "افتتاحیه",
        VoucherSource.Closing => "اختتامیه",
        _ => "دستی"
    };

    private static string KindTitle(TreasuryKind k) => k switch
    {
        TreasuryKind.Receipt => "دریافت",
        TreasuryKind.Payment => "پرداخت",
        _ => "انتقال"
    };

    private static string LevelTitle(AccountLevel l) => l switch
    {
        AccountLevel.Group => "گروه",
        AccountLevel.General => "کل",
        AccountLevel.Subsidiary => "معین",
        _ => "تفصیلی"
    };

    private static string GroupByTitle(string g) => g.ToLowerInvariant() switch
    {
        "day" => "روز",
        "month" => "ماه",
        "party" => "طرف حساب",
        "product" => "کالا",
        "warehouse" => "انبار",
        _ => g
    };

    private static string StocktakeStatusTitle(StocktakeStatus s) => s switch
    {
        StocktakeStatus.Draft => "پیش‌نویس",
        StocktakeStatus.Counting => "در حال شمارش",
        StocktakeStatus.Review => "بررسی مغایرت",
        StocktakeStatus.Applied => "اعمال شده",
        _ => "لغو شده"
    };
}
