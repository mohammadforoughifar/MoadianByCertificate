using System.Diagnostics;
using System.Globalization;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.ReportStudio;

public interface IRsExecutor
{
    Task<RsResultDto> RunAsync(RsQueryDto query, string title,
        IReadOnlyList<RsPromptValueDto> prompts, int page, int pageSize,
        RsRowScope scope, CancellationToken ct = default);
}

// =====================================================================
//  موتور اجرا
//
//  مراحل: واکشی سطرها ← اعمال فیلتر ← گروه‌بندی/تجمیع ← مرتب‌سازی ←
//  صفحه‌بندی ← قالب‌بندی خروجی (جدول/نمودار/KPI/محوری)
//
//  فلسفهٔ «بدون خطا»: هر مرحله داخل try سراسری است و هر استثنا به پیام
//  فارسیِ قابل‌فهم تبدیل می‌شود؛ کاربر هیچ‌وقت stack trace نمی‌بیند.
// =====================================================================
public sealed class RsExecutor : IRsExecutor
{
    /// <summary>سقف سطرهای خام که از دیتابیس خوانده می‌شود (قبل از تجمیع).</summary>
    private const int ScanLimit = 50_000;

    private readonly IRsRowSource _source;
    public RsExecutor(IRsRowSource source) => _source = source;

    public async Task<RsResultDto> RunAsync(RsQueryDto query, string title,
        IReadOnlyList<RsPromptValueDto> prompts, int page, int pageSize,
        RsRowScope scope, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var res = new RsResultDto
        {
            Title = title,
            Output = query.Output,
            Chart = query.Chart,
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 1000)
        };

        try
        {
            var v = RsValidator.Validate(query);
            if (!v.Ok)
            {
                res.Ok = false;
                res.Error = v.ErrorText;
                return res;
            }
            res.Warnings.AddRange(v.Warnings);

            // کاربر باید بداند که نتیجه فقط شامل دادهٔ خودش است
            if (!scope.Unrestricted && v.Modules.Any(m =>
                    m is "InnerLetters" or "OutgoingLetters" or "IncomingLetters"))
                res.Warnings.Add("این گزارش فقط نامه‌های شما را نشان می‌دهد. " +
                    "برای دیدن نامه‌های همه، مجوز «مشاهدهٔ نامه‌های دیگران» لازم است.");

            if (!scope.CanSeeAllWorkOrders && v.Modules.Contains("WorkOrders"))
                res.Warnings.Add("این گزارش فقط دستورکارهایی را نشان می‌دهد که شما صادر کرده‌اید یا به شما محول شده است.");

            // ۱) واکشی
            var rows = await _source.FetchAsync(query, ScanLimit, scope, ct);

            // ۲) فیلتر
            rows = ApplyFilters(rows, query.Filters, prompts);

            // ۳) گروه‌بندی و تجمیع
            var hasAgg = query.Columns.Any(c => c.Agg != RsAgg.None);
            List<List<object?>> data;
            var outCols = new List<RsResultColumnDto>();

            if (hasAgg)
                data = Aggregate(rows, query, outCols);
            else
                data = Flat(rows, query, outCols);

            // ۴) مرتب‌سازی
            data = ApplySort(data, query, outCols);

            if (query.Distinct)
                data = data
                    .GroupBy(r => string.Join('\u001f', r.Select(x => x?.ToString() ?? "")))
                    .Select(g => g.First())
                    .ToList();

            res.TotalRows = data.Count;

            // ۵) خروجی
            switch (query.Output)
            {
                case RsOutput.Kpi:
                    BuildKpi(res, data, outCols);
                    break;
                case RsOutput.Chart:
                    BuildChart(res, data, outCols, query);
                    break;
                case RsOutput.Pivot:
                    BuildPivot(res, data, outCols, query);
                    break;
                default:
                    BuildTable(res, data, outCols, query);
                    break;
            }
        }
        catch (RsUnsupportedShapeException ex)
        {
            res.Ok = false;
            res.Error = ex.Message;
        }
        catch (OperationCanceledException)
        {
            res.Ok = false;
            res.Error = "اجرای گزارش لغو شد.";
        }
        catch (Exception ex)
        {
            res.Ok = false;
            res.Error = "اجرای گزارش ناموفق بود: " + Friendly(ex);
        }

        res.ElapsedMs = sw.ElapsedMilliseconds;
        return res;
    }

    private static string Friendly(Exception ex)
    {
        var m = ex.Message ?? "";
        if (m.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            return "یکی از جدول‌های موردنیاز در دیتابیس ساخته نشده است. سرویس را یک‌بار ری‌استارت کنید.";
        if (m.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "زمان اجرای گزارش بیش از حد طول کشید؛ فیلتر بیشتری اضافه کنید.";
        return m;
    }

    // =====================================================================
    //  فیلتر
    // =====================================================================
    private static List<RsRow> ApplyFilters(List<RsRow> rows,
        List<RsFilterDto> filters, IReadOnlyList<RsPromptValueDto> prompts)
    {
        if (filters.Count == 0) return rows;

        var effective = new List<(RsFilterDto F, string? V1, string? V2)>();
        foreach (var f in filters)
        {
            var v1 = f.Value;
            var v2 = f.Value2;
            if (f.IsPrompt)
            {
                var p = prompts.FirstOrDefault(x =>
                    x.FieldKey.Equals(f.FieldKey, StringComparison.OrdinalIgnoreCase));
                // پارامتر بدون مقدار = نادیده گرفته می‌شود (رفتار مورد انتظار کاربر)
                if (p is null || (string.IsNullOrWhiteSpace(p.Value) && string.IsNullOrWhiteSpace(p.Value2)))
                    continue;
                v1 = p.Value;
                v2 = p.Value2;
            }
            effective.Add((f, v1, v2));
        }
        if (effective.Count == 0) return rows;

        return rows.Where(r =>
        {
            bool acc = true;
            bool first = true;
            foreach (var (f, v1, v2) in effective)
            {
                var hit = Match(r, f, v1, v2);
                if (first) { acc = hit; first = false; }
                else if (f.OrWithPrevious) acc = acc || hit;
                else acc = acc && hit;
            }
            return acc;
        }).ToList();
    }

    private static bool Match(RsRow r, RsFilterDto f, string? v1, string? v2)
    {
        r.V.TryGetValue(f.FieldKey, out var raw);
        var field = RsSchemaCatalog.FindField(f.FieldKey);

        switch (f.Op)
        {
            case RsOp.IsNull: return raw is null || (raw is string s0 && s0.Length == 0);
            case RsOp.NotNull: return raw is not null && !(raw is string s1 && s1.Length == 0);
        }
        if (raw is null) return false;

        // --- تاریخ ---
        if (field?.Type == RsFieldType.Date && raw is DateTime dt)
        {
            if (f.Op == RsOp.DateRange) return InRelativeRange(dt, v1);
            if (!DateTime.TryParse(v1, out var d1)) return false;
            return f.Op switch
            {
                RsOp.Eq => dt.Date == d1.Date,
                RsOp.Ne => dt.Date != d1.Date,
                RsOp.Gt => dt > d1,
                RsOp.Gte => dt >= d1,
                RsOp.Lt => dt < d1,
                RsOp.Lte => dt <= d1,
                RsOp.Between => DateTime.TryParse(v2, out var d2) && dt >= d1 && dt <= d2,
                _ => false
            };
        }

        // --- بولین ---
        if (raw is bool b)
        {
            var want = v1 is "1" or "true" or "True" or "بله";
            return f.Op == RsOp.Ne ? b != want : b == want;
        }

        // --- عدد ---
        if (IsNumeric(raw))
        {
            var n = ToDecimal(raw);
            if (f.Op == RsOp.In)
                return (f.Values ?? new()).Any(x => decimal.TryParse(x, out var xv) && xv == n);
            if (!decimal.TryParse(v1, NumberStyles.Any, CultureInfo.InvariantCulture, out var c1))
            {
                // ستون Enum ممکن است با متن فارسی فیلتر شود
                if (field?.EnumMap is not null)
                {
                    var code = field.EnumMap.FirstOrDefault(kv =>
                        kv.Value.Equals(v1?.Trim(), StringComparison.OrdinalIgnoreCase)).Key;
                    if (decimal.TryParse(code, out var ec)) c1 = ec;
                    else return false;
                }
                else return false;
            }
            return f.Op switch
            {
                RsOp.Eq => n == c1,
                RsOp.Ne => n != c1,
                RsOp.Gt => n > c1,
                RsOp.Gte => n >= c1,
                RsOp.Lt => n < c1,
                RsOp.Lte => n <= c1,
                RsOp.Between => decimal.TryParse(v2, NumberStyles.Any, CultureInfo.InvariantCulture, out var c2)
                                && n >= c1 && n <= c2,
                _ => false
            };
        }

        // --- متن ---
        var text = raw.ToString() ?? "";
        var cmp = StringComparison.OrdinalIgnoreCase;
        return f.Op switch
        {
            RsOp.Eq => text.Equals(v1, cmp),
            RsOp.Ne => !text.Equals(v1, cmp),
            RsOp.Contains => v1 is not null && text.Contains(v1, cmp),
            RsOp.StartsWith => v1 is not null && text.StartsWith(v1, cmp),
            RsOp.EndsWith => v1 is not null && text.EndsWith(v1, cmp),
            RsOp.In => (f.Values ?? new()).Any(x => text.Equals(x, cmp)),
            _ => false
        };
    }

    /// <summary>بازه‌های نسبی فارسی: today، week، month، quarter، year، last7، last30، last90</summary>
    private static bool InRelativeRange(DateTime dt, string? code)
    {
        var now = DateTime.Now;
        var today = now.Date;
        return (code ?? "").ToLowerInvariant() switch
        {
            "today" => dt.Date == today,
            "yesterday" => dt.Date == today.AddDays(-1),
            "last7" => dt >= today.AddDays(-7),
            "last30" => dt >= today.AddDays(-30),
            "last90" => dt >= today.AddDays(-90),
            "month" => IsSameJalaliMonth(dt, now),
            "year" => PersianDate.FromGregorian(dt).Year == PersianDate.FromGregorian(now).Year,
            _ => true
        };
    }

    private static bool IsSameJalaliMonth(DateTime a, DateTime b)
    {
        var (ay, am, _) = PersianDate.FromGregorian(a);
        var (by, bm, _) = PersianDate.FromGregorian(b);
        return ay == by && am == bm;
    }

    private static bool IsNumeric(object o) =>
        o is int or long or short or decimal or double or float;

    private static decimal ToDecimal(object? o) => o switch
    {
        null => 0m,
        decimal d => d,
        double db => (decimal)db,
        float f => (decimal)f,
        int i => i,
        long l => l,
        short s => s,
        bool b => b ? 1 : 0,
        _ => decimal.TryParse(o.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m
    };

    // =====================================================================
    //  ساخت ستون خروجی
    // =====================================================================
    /// <summary>نگاشت Enum هر ستون خروجی — برای تبدیل عدد به متن فارسی هنگام قالب‌بندی.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RsResultColumnDto,
        Dictionary<string, string>> EnumMaps = new();

    private static RsResultColumnDto OutCol(RsColumnDto c)
    {
        var f = RsSchemaCatalog.FindField(c.FieldKey);
        var baseTitle = c.Label;
        if (string.IsNullOrWhiteSpace(baseTitle))
        {
            baseTitle = f?.Title ?? c.FieldKey;
            if (c.Agg != RsAgg.None) baseTitle = $"{RsValidator.AggFa(c.Agg)} {baseTitle}";
            if (c.Bucket != RsDateBucket.None) baseTitle = $"{baseTitle} ({BucketFa(c.Bucket)})";
        }

        var type = c.Agg switch
        {
            RsAgg.None => f?.Type ?? RsFieldType.Text,
            RsAgg.Count or RsAgg.CountDistinct => RsFieldType.Number,
            _ => f?.Type == RsFieldType.Money ? RsFieldType.Money : RsFieldType.Number
        };
        if (c.Bucket != RsDateBucket.None) type = RsFieldType.Text;

        var col = new RsResultColumnDto
        {
            Key = c.FieldKey + (c.Agg == RsAgg.None ? "" : ":" + c.Agg),
            Title = baseTitle!,
            Type = type,
            Unit = c.Agg is RsAgg.Count or RsAgg.CountDistinct ? null : f?.Unit,
            Align = type is RsFieldType.Number or RsFieldType.Money ? "left"
                  : type == RsFieldType.Bool ? "center" : "right"
        };

        // ستون Enum بدون تجمیع: عدد را با برچسب فارسی نشان می‌دهیم
        if (c.Agg == RsAgg.None && c.Bucket == RsDateBucket.None
            && f?.Type == RsFieldType.Enum && f.EnumMap is { Count: > 0 })
        {
            col.Type = RsFieldType.Enum;
            col.Align = "right";
            EnumMaps.AddOrUpdate(col, f.EnumMap);
        }
        return col;
    }

    private static string BucketFa(RsDateBucket b) => b switch
    {
        RsDateBucket.Day => "روزانه",
        RsDateBucket.Month => "ماهانه",
        RsDateBucket.Quarter => "فصلی",
        RsDateBucket.Year => "سالانه",
        _ => ""
    };

    /// <summary>کلید گروه‌بندی یک سطر برای ستونی که ممکن است سطل زمانی داشته باشد.</summary>
    private static object? GroupValue(RsRow r, RsColumnDto c)
    {
        r.V.TryGetValue(c.FieldKey, out var raw);
        if (c.Bucket == RsDateBucket.None || raw is not DateTime dt) return raw;

        var (jy, jm, jd) = PersianDate.FromGregorian(dt);
        return c.Bucket switch
        {
            RsDateBucket.Day => $"{jy:0000}/{jm:00}/{jd:00}",
            RsDateBucket.Month => $"{jy:0000}/{jm:00}",
            RsDateBucket.Quarter => $"{jy:0000} - فصل {(jm - 1) / 3 + 1}",
            RsDateBucket.Year => jy.ToString(),
            _ => raw
        };
    }

    // =====================================================================
    //  حالت بدون تجمیع
    // =====================================================================
    private static List<List<object?>> Flat(List<RsRow> rows, RsQueryDto q,
        List<RsResultColumnDto> outCols)
    {
        var cols = q.Columns.Where(c => c.Visible).OrderBy(c => c.Order).ToList();
        foreach (var c in cols) outCols.Add(OutCol(c));

        return rows.Select(r => cols.Select(c => GroupValue(r, c)).ToList()).ToList();
    }

    // =====================================================================
    //  حالت تجمیعی
    // =====================================================================
    private static List<List<object?>> Aggregate(List<RsRow> rows, RsQueryDto q,
        List<RsResultColumnDto> outCols)
    {
        var cols = q.Columns.Where(c => c.Visible).OrderBy(c => c.Order).ToList();
        var dims = cols.Where(c => c.Agg == RsAgg.None).ToList();
        var measures = cols.Where(c => c.Agg != RsAgg.None).ToList();
        foreach (var c in cols) outCols.Add(OutCol(c));

        var groups = rows
            .GroupBy(r => string.Join('\u001f',
                dims.Select(d => GroupValue(r, d)?.ToString() ?? "")))
            .ToList();

        var result = new List<List<object?>>();
        foreach (var g in groups)
        {
            var first = g.First();
            var row = new List<object?>();
            foreach (var c in cols)
            {
                if (c.Agg == RsAgg.None) { row.Add(GroupValue(first, c)); continue; }

                var vals = g.Select(r => r.V.GetValueOrDefault(c.FieldKey)).ToList();
                row.Add(c.Agg switch
                {
                    RsAgg.Count => vals.Count(v => v is not null),
                    RsAgg.CountDistinct => vals.Where(v => v is not null)
                                               .Select(v => v!.ToString())
                                               .Distinct().Count(),
                    RsAgg.Sum => vals.Sum(ToDecimal),
                    RsAgg.Avg => vals.Count == 0 ? 0m : Math.Round(vals.Average(ToDecimal), 2),
                    RsAgg.Min => MinMax(vals, true),
                    RsAgg.Max => MinMax(vals, false),
                    _ => null
                });
            }
            result.Add(row);
        }
        return result;
    }

    private static object? MinMax(List<object?> vals, bool min)
    {
        var dates = vals.OfType<DateTime>().ToList();
        if (dates.Count > 0) return min ? dates.Min() : dates.Max();
        var nums = vals.Where(v => v is not null).Select(ToDecimal).ToList();
        if (nums.Count == 0) return null;
        return min ? nums.Min() : nums.Max();
    }

    // =====================================================================
    //  مرتب‌سازی
    // =====================================================================
    private static List<List<object?>> ApplySort(List<List<object?>> data,
        RsQueryDto q, List<RsResultColumnDto> outCols)
    {
        if (q.Sorts.Count == 0) return data;
        var cols = q.Columns.Where(c => c.Visible).OrderBy(c => c.Order).ToList();

        IOrderedEnumerable<List<object?>>? ordered = null;
        foreach (var s in q.Sorts)
        {
            var idx = cols.FindIndex(c =>
                c.FieldKey.Equals(s.FieldKey, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) continue;

            Func<List<object?>, object?> sel = r => idx < r.Count ? r[idx] : null;
            var cmp = Comparer<object?>.Create(CompareValues);

            ordered = ordered is null
                ? (s.Desc ? data.OrderByDescending(sel, cmp) : data.OrderBy(sel, cmp))
                : (s.Desc ? ordered.ThenByDescending(sel, cmp) : ordered.ThenBy(sel, cmp));
        }
        return ordered?.ToList() ?? data;
    }

    private static int CompareValues(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        if (a is DateTime da && b is DateTime db2) return da.CompareTo(db2);
        if (IsNumeric(a) && IsNumeric(b)) return ToDecimal(a).CompareTo(ToDecimal(b));
        if (a is bool ba && b is bool bb) return ba.CompareTo(bb);
        return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================
    //  قالب‌بندی خروجی
    // =====================================================================
    private static string Fmt(object? v, RsResultColumnDto col)
    {
        if (v is null) return "—";
        if (col.Type == RsFieldType.Enum && EnumMaps.TryGetValue(col, out var map))
        {
            var code = v is bool eb ? (eb ? "1" : "0") : v.ToString() ?? "";
            if (map.TryGetValue(code, out var fa)) return fa;
            return code.Length > 0 ? code : "—";
        }
        return col.Type switch
        {
            RsFieldType.Date => v is DateTime d ? Fa.Digits(PersianDate.ToShort(d)) : v.ToString() ?? "—",
            RsFieldType.Money => Fa.Digits(ToDecimal(v).ToString("N0")),
            RsFieldType.Number => Fa.Digits(FormatNumber(ToDecimal(v))),
            RsFieldType.Bool => v is bool b ? (b ? "بله" : "خیر") : v.ToString() ?? "—",
            _ => v.ToString() is { Length: > 0 } s ? s : "—"
        };
    }

    private static string FormatNumber(decimal d) =>
        d == Math.Floor(d) ? ((long)d).ToString("N0") : d.ToString("N2");

    private static void BuildTable(RsResultDto res, List<List<object?>> data,
        List<RsResultColumnDto> cols, RsQueryDto q)
    {
        res.Columns = cols;
        var skip = (res.Page - 1) * res.PageSize;
        var take = Math.Min(res.PageSize, Math.Max(0, q.Take - skip));
        var slice = data.Skip(skip).Take(take).ToList();
        res.HasMore = data.Count > skip + slice.Count;
        res.Rows = slice.Select(r => r.Select((v, i) => Fmt(v, cols[i])).ToList()).ToList();
    }

    private static void BuildKpi(RsResultDto res, List<List<object?>> data,
        List<RsResultColumnDto> cols)
    {
        res.Columns = cols;
        var col = cols.FirstOrDefault();
        var val = data.FirstOrDefault()?.FirstOrDefault();
        res.KpiValue = ToDecimal(val);
        res.KpiUnit = col?.Unit;
        if (col is not null)
            res.Rows = new List<List<string>> { new() { Fmt(val, col) } };
    }

    private static void BuildChart(RsResultDto res, List<List<object?>> data,
        List<RsResultColumnDto> cols, RsQueryDto q)
    {
        res.Columns = cols;
        var visible = q.Columns.Where(c => c.Visible).OrderBy(c => c.Order).ToList();
        var dimIdx = visible.Select((c, i) => (c, i)).Where(x => x.c.Agg == RsAgg.None)
                            .Select(x => x.i).ToList();
        var measIdx = visible.Select((c, i) => (c, i)).Where(x => x.c.Agg != RsAgg.None)
                             .Select(x => x.i).ToList();
        if (dimIdx.Count == 0 || measIdx.Count == 0) return;

        var limited = data.Take(Math.Min(q.Take, 200)).ToList();
        var labels = limited.Select(r => Fmt(r[dimIdx[0]], cols[dimIdx[0]])).ToList();

        // با دو بُعد: بُعد دوم به سری‌های جداگانه تبدیل می‌شود
        if (dimIdx.Count >= 2 && measIdx.Count == 1)
        {
            var d1 = dimIdx[0]; var d2 = dimIdx[1]; var m = measIdx[0];
            var xs = limited.Select(r => Fmt(r[d1], cols[d1])).Distinct().ToList();
            var seriesNames = limited.Select(r => Fmt(r[d2], cols[d2])).Distinct().ToList();
            foreach (var sn in seriesNames)
            {
                var ser = new RsSeriesDto { Name = sn, Labels = xs };
                foreach (var x in xs)
                {
                    var cell = limited.FirstOrDefault(r =>
                        Fmt(r[d1], cols[d1]) == x && Fmt(r[d2], cols[d2]) == sn);
                    ser.Values.Add(cell is null ? 0 : ToDecimal(cell[m]));
                }
                res.Series.Add(ser);
            }
            return;
        }

        foreach (var mi in measIdx)
            res.Series.Add(new RsSeriesDto
            {
                Name = cols[mi].Title,
                Labels = labels,
                Values = limited.Select(r => ToDecimal(r[mi])).ToList()
            });
    }

    private static void BuildPivot(RsResultDto res, List<List<object?>> data,
        List<RsResultColumnDto> cols, RsQueryDto q)
    {
        var visible = q.Columns.Where(c => c.Visible).OrderBy(c => c.Order).ToList();
        var pivotIdx = visible.FindIndex(c =>
            c.FieldKey.Equals(q.PivotColumnField, StringComparison.OrdinalIgnoreCase) && c.Agg == RsAgg.None);
        var rowDims = visible.Select((c, i) => (c, i))
                             .Where(x => x.c.Agg == RsAgg.None && x.i != pivotIdx)
                             .Select(x => x.i).ToList();
        var measIdx = visible.Select((c, i) => (c, i))
                             .Where(x => x.c.Agg != RsAgg.None).Select(x => x.i).ToList();

        if (pivotIdx < 0 || measIdx.Count == 0)
        {
            BuildTable(res, data, cols, q);
            return;
        }

        var headers = data.Select(r => Fmt(r[pivotIdx], cols[pivotIdx]))
                          .Distinct().OrderBy(x => x).ToList();

        res.Columns = rowDims.Select(i => cols[i]).ToList();
        foreach (var h in headers)
            res.Columns.Add(new RsResultColumnDto
            {
                Key = "pv:" + h, Title = h, Type = cols[measIdx[0]].Type, Align = "left"
            });

        var grouped = data.GroupBy(r =>
            string.Join('\u001f', rowDims.Select(i => Fmt(r[i], cols[i]))));

        foreach (var g in grouped)
        {
            var first = g.First();
            var line = rowDims.Select(i => Fmt(first[i], cols[i])).ToList();
            foreach (var h in headers)
            {
                var cell = g.FirstOrDefault(r => Fmt(r[pivotIdx], cols[pivotIdx]) == h);
                line.Add(cell is null ? "—" : Fmt(cell[measIdx[0]], cols[measIdx[0]]));
            }
            res.Rows.Add(line);
        }
        res.TotalRows = res.Rows.Count;
    }
}
