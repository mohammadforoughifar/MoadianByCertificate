using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.ReportStudio;

/// <summary>نتیجهٔ اعتبارسنجی — خطاها مانع اجرا می‌شوند، هشدارها نه.</summary>
public class RsValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool Ok => Errors.Count == 0;

    /// <summary>ماژول‌های RBAC که این گزارش لمس می‌کند.</summary>
    public HashSet<string> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string ErrorText => string.Join(" • ", Errors);
}

// =====================================================================
//  اعتبارسنج کوئری
//
//  هدف: هیچ کوئری نامعتبری به لایهٔ اجرا نرسد. هر ایراد با پیام فارسیِ
//  قابل‌فهم برمی‌گردد تا کاربر به‌جای «خطای ۵۰۰» بداند دقیقاً چه چیزی
//  را باید اصلاح کند.
// =====================================================================
public static class RsValidator
{
    public const int MaxTables = 5;
    public const int MaxColumns = 40;
    public const int MaxFilters = 25;
    public const int MaxTake = 5000;

    public static RsValidationResult Validate(RsQueryDto? q)
    {
        var r = new RsValidationResult();
        if (q is null) { r.Errors.Add("تعریف گزارش خالی است."); return r; }

        // ---------- جدول‌ها ----------
        if (q.Tables.Count == 0)
        {
            r.Errors.Add("حداقل یک جدول باید انتخاب شود.");
            return r;
        }
        if (q.Tables.Count > MaxTables)
            r.Errors.Add($"حداکثر {MaxTables} جدول در یک گزارش مجاز است.");

        var present = new List<string>();
        for (var i = 0; i < q.Tables.Count; i++)
        {
            var qt = q.Tables[i];
            var def = RsSchemaCatalog.FindTable(qt.TableKey);
            if (def is null)
            {
                r.Errors.Add($"جدول «{qt.TableKey}» ناشناخته است.");
                continue;
            }
            if (present.Contains(def.Key, StringComparer.OrdinalIgnoreCase))
            {
                r.Errors.Add($"جدول «{def.Title}» دوبار اضافه شده است.");
                continue;
            }

            if (i == 0)
            {
                // جدول اصلی نباید جوین داشته باشد
                if (!string.IsNullOrWhiteSpace(qt.JoinKey))
                    r.Warnings.Add("جدول اصلی نیازی به مسیر جوین ندارد؛ نادیده گرفته شد.");
            }
            else
            {
                var join = RsSchemaCatalog.FindJoin(qt.JoinKey);
                if (join is null)
                {
                    r.Errors.Add($"برای «{def.Title}» مسیر جوین معتبری انتخاب نشده است.");
                    continue;
                }
                if (!join.ToTable.Equals(def.Key, StringComparison.OrdinalIgnoreCase))
                {
                    r.Errors.Add($"مسیر جوین «{join.Title}» به جدول «{def.Title}» نمی‌رسد.");
                    continue;
                }
                if (!present.Contains(join.FromTable, StringComparer.OrdinalIgnoreCase))
                {
                    var fromTitle = RsSchemaCatalog.FindTable(join.FromTable)?.Title ?? join.FromTable;
                    r.Errors.Add($"برای افزودن «{def.Title}» باید اول جدول «{fromTitle}» در گزارش باشد.");
                    continue;
                }
                if (join.Multiplies)
                    r.Warnings.Add($"رابطهٔ «{join.Title}» یک‌به‌چند است؛ سطرها تکرار می‌شوند. " +
                                   "برای شمارش درست از «تعداد یکتا» استفاده کنید.");
            }

            present.Add(def.Key);
            if (!string.IsNullOrWhiteSpace(def.Module)) r.Modules.Add(def.Module);
        }

        if (present.Count == 0) return r;

        // ---------- ستون‌ها ----------
        if (q.Columns.Count == 0)
            r.Errors.Add("حداقل یک ستون باید انتخاب شود.");
        if (q.Columns.Count > MaxColumns)
            r.Errors.Add($"حداکثر {MaxColumns} ستون مجاز است.");

        var hasAgg = false;
        var hasPlain = false;
        foreach (var c in q.Columns)
        {
            var f = RsSchemaCatalog.FindField(c.FieldKey);
            if (f is null)
            {
                r.Errors.Add($"ستون «{c.FieldKey}» ناشناخته است.");
                continue;
            }
            var tbl = RsSchemaCatalog.TableOfField(c.FieldKey)!;
            if (!present.Contains(tbl, StringComparer.OrdinalIgnoreCase))
            {
                var tblTitle = RsSchemaCatalog.FindTable(tbl)?.Title ?? tbl;
                r.Errors.Add($"ستون «{f.Title}» از جدول «{tblTitle}» است که به گزارش اضافه نشده.");
                continue;
            }

            if (c.Agg != RsAgg.None)
            {
                hasAgg = true;
                // Count روی هر نوعی مجاز است؛ بقیه فقط روی عدد/تاریخ
                var numeric = f.Type is RsFieldType.Number or RsFieldType.Money;
                var dateOk = f.Type == RsFieldType.Date && c.Agg is RsAgg.Min or RsAgg.Max;
                var countOk = c.Agg is RsAgg.Count or RsAgg.CountDistinct;
                if (!numeric && !dateOk && !countOk)
                    r.Errors.Add($"تجمیع «{AggFa(c.Agg)}» روی ستون «{f.Title}» معنا ندارد.");
                if (numeric && !f.Aggregatable && !countOk)
                    r.Warnings.Add($"ستون «{f.Title}» برای تجمیع طراحی نشده است.");
            }
            else
            {
                hasPlain = true;
                if (!f.Groupable && hasAgg)
                    r.Errors.Add($"ستون «{f.Title}» قابل گروه‌بندی نیست؛ یا حذفش کنید یا رویش تجمیع بگذارید.");
            }

            if (c.Bucket != RsDateBucket.None && f.Type != RsFieldType.Date)
                r.Errors.Add($"گروه‌بندی زمانی فقط روی ستون تاریخ ممکن است، نه «{f.Title}».");
        }

        // حالت تجمیعی: ستون‌های بدون تجمیع خودکار گروه‌بندی می‌شوند — این درست است،
        // ولی اگر ستون غیرقابل‌گروه‌بندی باشد بالاتر خطا داده‌ایم.
        if (hasAgg && hasPlain)
            r.Warnings.Add("ستون‌های بدون تجمیع به‌صورت خودکار گروه‌بندی می‌شوند.");

        // ---------- فیلترها ----------
        if (q.Filters.Count > MaxFilters)
            r.Errors.Add($"حداکثر {MaxFilters} فیلتر مجاز است.");

        foreach (var fl in q.Filters)
        {
            var f = RsSchemaCatalog.FindField(fl.FieldKey);
            if (f is null)
            {
                r.Errors.Add($"فیلتر روی ستون ناشناختهٔ «{fl.FieldKey}».");
                continue;
            }
            var tbl = RsSchemaCatalog.TableOfField(fl.FieldKey)!;
            if (!present.Contains(tbl, StringComparer.OrdinalIgnoreCase))
            {
                r.Errors.Add($"فیلتر روی ستون «{f.Title}» که جدولش در گزارش نیست.");
                continue;
            }

            var needsValue = fl.Op is not (RsOp.IsNull or RsOp.NotNull);
            if (needsValue && !fl.IsPrompt)
            {
                var empty = fl.Op == RsOp.In
                    ? (fl.Values is null || fl.Values.Count == 0)
                    : string.IsNullOrWhiteSpace(fl.Value);
                if (empty)
                    r.Errors.Add($"برای فیلتر «{f.Title}» مقداری وارد نشده است.");
            }
            if (fl.Op == RsOp.Between && !fl.IsPrompt && string.IsNullOrWhiteSpace(fl.Value2))
                r.Errors.Add($"فیلتر بازه‌ای «{f.Title}» به مقدار دوم نیاز دارد.");

            // سازگاری عملگر با نوع ستون
            var textOps = fl.Op is RsOp.Contains or RsOp.StartsWith or RsOp.EndsWith;
            if (textOps && f.Type is not (RsFieldType.Text or RsFieldType.Enum))
                r.Errors.Add($"عملگر متنی روی ستون «{f.Title}» قابل استفاده نیست.");
            if (fl.Op == RsOp.DateRange && f.Type != RsFieldType.Date)
                r.Errors.Add($"بازهٔ زمانی فقط روی ستون تاریخ کار می‌کند، نه «{f.Title}».");

            // اعتبار مقدار عددی/تاریخ
            if (!fl.IsPrompt && needsValue && fl.Op != RsOp.In && fl.Op != RsOp.DateRange)
            {
                if (f.Type is RsFieldType.Number or RsFieldType.Money)
                {
                    if (!decimal.TryParse(fl.Value, out _))
                        r.Errors.Add($"مقدار فیلتر «{f.Title}» باید عدد باشد.");
                    if (fl.Op == RsOp.Between && !decimal.TryParse(fl.Value2, out _))
                        r.Errors.Add($"مقدار دوم فیلتر «{f.Title}» باید عدد باشد.");
                }
                else if (f.Type == RsFieldType.Date)
                {
                    if (!DateTime.TryParse(fl.Value, out _))
                        r.Errors.Add($"مقدار فیلتر «{f.Title}» تاریخ معتبر نیست.");
                }
                else if (f.Type == RsFieldType.Bool)
                {
                    if (!bool.TryParse(fl.Value, out _) && fl.Value is not ("0" or "1"))
                        r.Errors.Add($"مقدار فیلتر «{f.Title}» باید بله/خیر باشد.");
                }
            }
        }

        // ---------- مرتب‌سازی ----------
        foreach (var s in q.Sorts)
        {
            var f = RsSchemaCatalog.FindField(s.FieldKey);
            if (f is null)
            {
                r.Errors.Add($"مرتب‌سازی روی ستون ناشناختهٔ «{s.FieldKey}».");
                continue;
            }
            if (!q.Columns.Any(c => c.FieldKey.Equals(s.FieldKey, StringComparison.OrdinalIgnoreCase)))
                r.Errors.Add($"برای مرتب‌سازی، ستون «{f.Title}» باید در خروجی باشد.");
        }

        // ---------- محدودیت سطر ----------
        if (q.Take is < 1 or > MaxTake)
            r.Errors.Add($"تعداد سطر باید بین ۱ تا {MaxTake} باشد.");

        // ---------- سازگاری خروجی ----------
        switch (q.Output)
        {
            case RsOutput.Chart:
            {
                var dims = q.Columns.Count(c => c.Agg == RsAgg.None);
                var measures = q.Columns.Count(c => c.Agg != RsAgg.None);
                if (dims == 0) r.Errors.Add("نمودار به حداقل یک ستون گروه‌بندی (محور) نیاز دارد.");
                if (measures == 0) r.Errors.Add("نمودار به حداقل یک سنجه (مثل مجموع یا تعداد) نیاز دارد.");
                if (dims > 2) r.Warnings.Add("برای نمودار، یک یا دو ستون گروه‌بندی مناسب‌تر است.");
                if (q.Chart is RsChart.Pie or RsChart.Doughnut && measures > 1)
                    r.Warnings.Add("نمودار دایره‌ای فقط سنجهٔ اول را نشان می‌دهد.");
                break;
            }
            case RsOutput.Kpi:
            {
                var measures = q.Columns.Count(c => c.Agg != RsAgg.None);
                if (measures != 1)
                    r.Errors.Add("نمای KPI دقیقاً به یک سنجه نیاز دارد.");
                if (q.Columns.Any(c => c.Agg == RsAgg.None))
                    r.Errors.Add("در نمای KPI نباید ستون گروه‌بندی داشته باشید.");
                break;
            }
            case RsOutput.Pivot:
            {
                if (string.IsNullOrWhiteSpace(q.PivotColumnField))
                    r.Errors.Add("برای جدول محوری باید ستون سرستون را انتخاب کنید.");
                else
                {
                    var pf = RsSchemaCatalog.FindField(q.PivotColumnField);
                    if (pf is null)
                        r.Errors.Add("ستون سرستونِ جدول محوری ناشناخته است.");
                    else if (!q.Columns.Any(c => c.Agg != RsAgg.None))
                        r.Errors.Add("جدول محوری به حداقل یک سنجه نیاز دارد.");
                }
                break;
            }
        }

        return r;
    }

    public static string AggFa(RsAgg a) => a switch
    {
        RsAgg.Count => "تعداد",
        RsAgg.CountDistinct => "تعداد یکتا",
        RsAgg.Sum => "مجموع",
        RsAgg.Avg => "میانگین",
        RsAgg.Min => "کمینه",
        RsAgg.Max => "بیشینه",
        _ => ""
    };
}
