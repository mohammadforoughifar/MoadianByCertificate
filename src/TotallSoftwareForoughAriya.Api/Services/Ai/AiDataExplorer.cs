// موتور کاوش داده حکمرانی‌شده (§۱۵) — تنها مسیر دسترسی AI به داده خام.
// - فقط موجودیت‌ها و فیلدهای AiDataCatalog (هیچ SQL خامی، فقط Expression Tree)
// - فیلتر و مرتب‌سازی سمت SQL؛ پرژکشن و گروه‌بندی در حافظه (سقف ۲۰۰۰ سطر)
// - RBAC ماژول + فیلد حساس + محدوده سطر (نامه/ارجاع/مرخصی/حضور/تیکت/مأموریت/وام/مانده: مال خودم مگر مدیر)
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Ai;

public sealed class AiExploreFilter
{
    public string Field { get; set; } = "";
    public string Op { get; set; } = "eq";
    public string? Value { get; set; }
    public string? Value2 { get; set; }
}

public sealed class AiExploreRequest
{
    public string Entity { get; set; } = "";
    public List<AiExploreFilter> Filters { get; set; } = new();
    public List<string>? Select { get; set; }
    public string? GroupBy { get; set; }
    public string? Agg { get; set; } = "count";
    public string? AggField { get; set; }
    public string? OrderBy { get; set; }
    public bool Desc { get; set; } = true;
    public int Limit { get; set; } = 20;
}

public sealed class AiExploreColumn
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "text";
}

public sealed class AiExploreResult
{
    public List<AiExploreColumn> Columns { get; set; } = new();
    /// <summary>همه سطرهای واکشی‌شده (تا سقف) — ابزار فقط پیش‌نمایش را به مدل می‌دهد.</summary>
    public List<List<object?>> Rows { get; set; } = new();
    public int TotalCount { get; set; }
    public bool Truncated { get; set; }
    public string? Notice { get; set; }
}

public class AiDataExplorer
{
    public const int MaxFetch = 2000;
    public const int MaxLimit = 50;

    private readonly AppDbContext _db;
    public AiDataExplorer(AppDbContext db) { _db = db; }

    private sealed class ExecPlan
    {
        public AiEntityDef Entity = null!;
        public List<AiFieldDef> Select = new();
        public AiFieldDef? Group;
        public AiFieldDef? Agg;
        public string AggKind = "count";
        public AiFieldDef Order = null!;
        public bool Desc = true;
        public int Limit = 20;
        public List<AiExploreFilter> Filters = new();
        public string ScopeKind = "all";
        public int UserId;
        public int EmpId;
        public List<int> LetterIds = new();
        public string? Notice;
    }

    public async Task<(bool Ok, string? Error, AiExploreResult? Result)> QueryAsync(
        int userId, AiExploreRequest req, CancellationToken ct)
    {
        var entityName = (req.Entity ?? "").Trim();
        var ent = AiDataCatalog.Find(entityName);
        if (ent == null)
            return (false, $"موجودیت «{entityName}» را نمی‌شناسم. با ابزار data_catalog لیست موجودیت‌ها را ببین: " +
                string.Join("، ", AiDataCatalog.Entities.Select(e => e.Name)), null);

        var role = await _db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.Role).FirstOrDefaultAsync(ct);

        // ۱) دسترسی سطح موجودیت
        if (ent.Module != "" && !await AiAccessHelper.UserHasAsync(_db, userId, ent.Module, ent.ModuleAction, role, ct))
            return (false, $"به داده «{ent.Fa}» دسترسی نداری. (مجوز لازم: {ent.Module}/{ent.ModuleAction})", null);

        // ۲) اعتبارسنجی فیلدها
        string? FieldError(string? name, string what)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (ent.FindField(name) == null)
                return $"{what} «{name}» در «{ent.Fa}» وجود ندارد؛ فیلدهای معتبر: " +
                    string.Join("، ", ent.Fields.Select(f => f.Name));
            return null;
        }
        foreach (var f in req.Filters ?? new())
        {
            var e = FieldError(f.Field, "فیلد فیلتر");
            if (e != null) return (false, e, null);
            if (!IsOp(f.Op)) return (false, $"عملگر «{f.Op}» معتبر نیست؛ عملگرها: eq, neq, gt, gte, lt, lte, contains, starts, between, in", null);
        }
        if (req.Select != null) foreach (var s in req.Select)
        {
            var e = FieldError(s, "فیلد نمایشی");
            if (e != null) return (false, e, null);
        }
        var ge = FieldError(req.GroupBy, "فیلد گروه‌بندی");
        if (ge != null) return (false, ge, null);
        var ae = FieldError(req.AggField, "فیلد تجمیع");
        if (ae != null) return (false, ae, null);
        var oe = FieldError(req.OrderBy, "فیلد مرتب‌سازی");
        if (oe != null) return (false, oe, null);

        var aggKind = (req.Agg ?? "count").Trim().ToLowerInvariant();
        if (req.GroupBy != null && aggKind is not ("count" or "sum" or "avg"))
            return (false, $"نوع تجمیع «{req.Agg}» معتبر نیست؛ یکی از: count, sum, avg", null);
        if (req.GroupBy != null && aggKind != "count" && string.IsNullOrWhiteSpace(req.AggField))
            return (false, "برای تجمیع sum/avg باید agg_field هم بدهی.", null);
        if (req.GroupBy != null && aggKind != "count" && req.AggField != null)
        {
            var ak = ent.FindField(req.AggField)!.Kind;
            if (ak is not ("number" or "money" or "duration"))
                return (false, $"تجمیع {aggKind} روی فیلد «{req.AggField}» ممکن نیست؛ فقط فیلدهای عددی/مبلغی/مدت.", null);
        }

        // ۳) فیلدهای حساس
        var sensitiveDenied = new List<string>();
        async Task<bool> CanSeeAsync(AiFieldDef f)
        {
            if (!f.Sensitive) return true;
            if (await AiAccessHelper.UserHasAsync(_db, userId, f.SensitiveModule, "Read", role, ct)) return true;
            if (!sensitiveDenied.Contains(f.Fa)) sensitiveDenied.Add(f.Fa);
            return false;
        }
        foreach (var f in req.Filters ?? new())
        {
            var fd = ent.FindField(f.Field)!;
            if (!await CanSeeAsync(fd))
                return (false, $"به فیلد «{fd.Fa}» دسترسی نداری و نمی‌توانی رویش فیلتر بزنی. (مجوز: {fd.SensitiveModule})", null);
        }
        foreach (var n in new[] { req.GroupBy, req.AggField, req.OrderBy })
        {
            if (n == null) continue;
            var fd = ent.FindField(n)!;
            if (!await CanSeeAsync(fd))
                return (false, $"به فیلد «{fd.Fa}» دسترسی نداری. (مجوز: {fd.SensitiveModule})", null);
        }

        var plan = new ExecPlan
        {
            Entity = ent,
            AggKind = req.GroupBy != null ? aggKind : "count",
            Desc = req.Desc,
            Limit = Math.Clamp(req.Limit <= 0 ? 20 : req.Limit, 1, MaxLimit),
            Filters = req.Filters ?? new(),
            ScopeKind = ent.Scope,
            UserId = userId,
        };
        if (req.GroupBy != null) plan.Group = ent.FindField(req.GroupBy);
        if (req.AggField != null) plan.Agg = ent.FindField(req.AggField);
        plan.Order = req.OrderBy != null ? ent.FindField(req.OrderBy)! : ent.FindField(ent.DefaultOrder)!;

        // ستون‌های خروجی (بدون حساس‌های ممنوع)
        var wanted = req.Select is { Count: > 0 } ? req.Select : ent.Fields.Select(f => f.Name).ToList();
        foreach (var n in wanted)
        {
            var fd = ent.FindField(n)!;
            if (await CanSeeAsync(fd)) plan.Select.Add(fd);
        }
        if (plan.Group != null) plan.Select.Clear(); // خروجی گروه‌بندی ستون خودش را دارد
        if (plan.Select.Count == 0 && plan.Group == null)
            return (false, "هیچ فیلد قابل‌نمایشی باقی نماند.", null);
        if (sensitiveDenied.Count > 0)
            plan.Notice = $"فیلد حساس ({string.Join("، ", sensitiveDenied)}) به‌خاطر نداشتن مجوز نمایش داده نشد.";

        // ۴) محدوده سطر
        if (ent.Scope != "all" && ent.ScopeManageModule != "" &&
            await AiAccessHelper.UserHasAsync(_db, userId, ent.ScopeManageModule, ent.ScopeManageAction, role, ct))
            plan.ScopeKind = "all";
        if (plan.ScopeKind == "employee")
        {
            plan.EmpId = await _db.HrEmployees.AsNoTracking()
                .Where(e => e.SystemUserId == userId).Select(e => e.Id).FirstOrDefaultAsync(ct);
            if (plan.EmpId == 0)
                return (true, null, new AiExploreResult { Notice = "رکورد پرسنلی برایت ثبت نشده؛ چیزی برای نمایش نیست." });
        }
        else if (plan.ScopeKind == "letters")
        {
            plan.LetterIds = await _db.Erjas.AsNoTracking()
                .Where(e => e.ReciverUserId == userId).Select(e => e.SourceId).ToListAsync(ct);
        }

        // ۵) اجرای ژنریک
        var mi = typeof(AiDataExplorer).GetMethod(nameof(RunAsync),
            BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(ent.ClrType);
        var task = (Task<(bool Ok, string? Error, AiExploreResult? Result)>)mi.Invoke(this, new object[] { plan, ct })!;
        return await task;
    }

    private static bool IsOp(string? op) => (op ?? "").Trim().ToLowerInvariant() switch
    {
        "eq" or "neq" or "gt" or "gte" or "lt" or "lte" or "contains" or "starts" or "between" or "in" or "period" => true,
        _ => false,
    };

    // ==================== اجرای ژنریک ====================

    private async Task<(bool Ok, string? Error, AiExploreResult? Result)> RunAsync<T>(
        ExecPlan plan, CancellationToken ct) where T : class
    {
        var today = DateTime.Today;
        var p = Expression.Parameter(typeof(T), "e");
        Expression? pred = BuildScope<T>(plan, p);

        // فیلترهای پایه + کاربر
        var all = plan.Entity.BaseFilters
            .Select(b => new AiExploreFilter { Field = b.Field, Op = b.Op, Value = b.Value })
            .Concat(plan.Filters).ToList();
        foreach (var f in all)
        {
            // فیلترهای پایه توسعه‌دهنده (مثل IsDelete) در کاتالوگ عمومی نیستند
            var fd = plan.Entity.FindField(f.Field)
                ?? new AiFieldDef { Name = f.Field, Fa = f.Field, Kind = "bool" };
            var member = MemberAccess(p, fd.Name);
            var cond = BuildCondition(member, fd, f, today, out var err);
            if (cond == null) return (false, err, null);
            pred = pred == null ? cond : Expression.AndAlso(pred, cond);
        }

        var q = _db.Set<T>().AsNoTracking().AsQueryable();
        if (pred != null) q = q.Where(Expression.Lambda<Func<T, bool>>(pred, p));
        var total = await q.CountAsync(ct);

        // مرتب‌سازی (در گروه‌بندی: بعداً روی مقدار تجمیع)
        if (plan.Group == null)
        {
            var om = MemberAccess(p, plan.Order.Name);
            var mname = plan.Desc ? "OrderByDescending" : "OrderBy";
            q = q.Provider.CreateQuery<T>(Expression.Call(typeof(Queryable), mname,
                new[] { typeof(T), om.Type }, q.Expression,
                Expression.Quote(Expression.Lambda(om, p))));
        }

        var list = await q.Take(MaxFetch).ToListAsync(ct);
        var result = new AiExploreResult { TotalCount = total, Truncated = total > list.Count, Notice = plan.Notice };

        if (plan.Group != null)
            return GroupProject<T>(plan, result, list);

        foreach (var f in plan.Select)
            result.Columns.Add(new AiExploreColumn { Key = f.Name, Title = f.Fa, Kind = f.Kind });
        foreach (var row in list)
        {
            var cells = new List<object?>();
            foreach (var f in plan.Select)
                cells.Add(FormatValue(f, ReadPath(row, f.Name)));
            result.Rows.Add(cells);
        }
        return (true, null, result);
    }

    private static Expression? BuildScope<T>(ExecPlan plan, ParameterExpression p)
    {
        switch (plan.ScopeKind)
        {
            case "letters":
            {
                var creator = Expression.Property(p, "CreatorUserId");
                var me = Expression.Constant(plan.UserId);
                Expression cond = Expression.Equal(creator, me);
                if (plan.LetterIds.Count > 0)
                {
                    var id = Expression.Property(p, "Id");
                    var contains = typeof(List<int>).GetMethod(nameof(List<int>.Contains), new[] { typeof(int) })!;
                    cond = Expression.OrElse(cond,
                        Expression.Call(Expression.Constant(plan.LetterIds), contains, id));
                }
                return cond;
            }
            case "employee":
                return Expression.Equal(Expression.Property(p, "EmployeeId"), Expression.Constant(plan.EmpId));
            case "user":
                return Expression.Equal(Expression.Property(p, "UserId"), Expression.Constant(plan.UserId));
            case "referral":
                return Expression.OrElse(
                    Expression.Equal(Expression.Property(p, "ReciverUserId"), Expression.Constant(plan.UserId)),
                    Expression.Equal(Expression.Property(p, "SenderUserId"), Expression.Constant(plan.UserId)));
            default:
                return null;
        }
    }

    private static (bool Ok, string? Error, AiExploreResult? Result) GroupProject<T>(
        ExecPlan plan, AiExploreResult result, List<T> list)
    {
        var gf = plan.Group!;
        var groups = list.GroupBy(r => GroupKey(ReadPath(r, gf.Name))).ToList();
        var rows = new List<(object? Key, decimal Value, int Count, TimeSpan Dur)>();
        foreach (var g in groups)
        {
            if (plan.AggKind == "count")
            {
                rows.Add((g.Key, g.Count(), g.Count(), TimeSpan.Zero));
                continue;
            }
            var vals = g.Select(r => ReadPath(r, plan.Agg!.Name)).Where(v => v != null).ToList();
            if (plan.Agg!.Kind == "duration" || vals.All(v => v is TimeSpan))
            {
                var sum = TimeSpan.Zero;
                foreach (var v in vals) if (v is TimeSpan t) sum += t;
                var avg = vals.Count > 0 ? TimeSpan.FromTicks(sum.Ticks / vals.Count) : TimeSpan.Zero;
                rows.Add((g.Key, 0, vals.Count, plan.AggKind == "avg" ? avg : sum));
            }
            else
            {
                var nums = vals.Select(ToDecimal).Where(n => n != null).Select(n => n!.Value).ToList();
                var v = nums.Count == 0 ? 0 : plan.AggKind == "avg" ? nums.Average() : nums.Sum();
                rows.Add((g.Key, v, nums.Count, TimeSpan.Zero));
            }
        }
        rows = rows.OrderByDescending(r => r.Dur.Ticks).ThenByDescending(r => r.Value).ToList();

        var aggTitle = plan.AggKind == "count" ? "تعداد"
            : (plan.AggKind == "avg" ? "میانگین " : "جمع ") + plan.Agg!.Fa;
        var isDur = plan.AggKind != "count" && plan.Agg != null && plan.Agg.Kind == "duration";
        result.Columns.Add(new AiExploreColumn { Key = gf.Name, Title = gf.Fa, Kind = gf.Kind });
        result.Columns.Add(new AiExploreColumn
        {
            Key = "__agg", Title = aggTitle,
            Kind = plan.AggKind == "count" ? "number"
                : isDur ? "text" : plan.Agg!.Kind == "money" ? "money" : "number",
        });
        foreach (var r in rows)
        {
            object? v = plan.AggKind == "count" ? r.Count
                : isDur ? (object?)FormatDuration(r.Dur) : r.Value;
            // قالب‌بندی کلید گروه با همان فرمت فیلد
            result.Rows.Add(new List<object?> { FormatValue(gf, r.Key is DateTime dk && gf.Kind is "date" or "datetime" ? dk : r.Key), v });
        }
        // سطرهای گروه‌بندی‌شده کامل‌اند (نه برش‌خورده از MaxFetch در منطق چت) —
        // Truncated همان نسبت واکشی است.
        return (true, null, result);
    }

    private static object? GroupKey(object? raw)
        => raw is DateTime d ? d.Date : raw ?? "—";

    private static decimal? ToDecimal(object? v) => v switch
    {
        null => null,
        decimal d => d,
        int i => i, long l => l, short s => s, byte b => b,
        double db => (decimal)db, float f => (decimal)f,
        _ => null,
    };

    // ==================== ساخت شرط فیلتر ====================

    private static MemberExpression MemberAccess(ParameterExpression p, string path)
    {
        Expression e = p;
        foreach (var part in path.Split('.'))
            e = Expression.PropertyOrField(e, part);
        return (MemberExpression)e;
    }

    private static Expression? BuildCondition(MemberExpression m, AiFieldDef fd,
        AiExploreFilter f, DateTime today, out string? error)
    {
        error = null;
        var op = (f.Op ?? "eq").Trim().ToLowerInvariant();
        var memberType = m.Type;
        var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;
        var isNullable = Nullable.GetUnderlyingType(memberType) != null;

        // گارد نال برای نالبل‌ها و رشته‌ها
        Expression Has() => isNullable
            ? Expression.Property(m, "HasValue")
            : memberType == typeof(string)
                ? (Expression)Expression.NotEqual(m, Expression.Constant(null, typeof(string)))
                : Expression.Constant(true);
        Expression Val() => isNullable ? Expression.Property(m, "Value") : m;

        if (underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan))
        {
            error = $"فیلتر روی فیلد «{fd.Fa}» پشتیبانی نمی‌شود (فقط نمایشی است).";
            return null;
        }

        // --- رشته ---
        if (underlying == typeof(string))
        {
            if (op is "contains" or "starts" or "eq" or "neq" or "in")
            {
                var raw = AiTextUtil.ToEnDigits(f.Value ?? "").Trim();
                if (op == "in")
                {
                    var parts = raw.Split(new[] { ',', '،' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length == 0) { error = $"مقدار فیلتر «{fd.Fa}» خالی است."; return null; }
                    Expression? or = null;
                    foreach (var part in parts)
                    {
                        var eq = (Expression)Expression.Equal(m, Expression.Constant(part));
                        or = or == null ? eq : Expression.OrElse(or, eq);
                    }
                    return Expression.AndAlso(Has(), or!);
                }
                if (raw == "") { error = $"مقدار فیلتر «{fd.Fa}» خالی است."; return null; }
                var c = Expression.Constant(raw);
                Expression core = op switch
                {
                    "contains" => Expression.Call(m, typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!, c),
                    "starts" => Expression.Call(m, typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!, c),
                    "neq" => Expression.NotEqual(m, c),
                    _ => Expression.Equal(m, c),
                };
                return op == "neq" ? core : Expression.AndAlso(Has(), core);
            }
            error = $"عملگر «{op}» برای متن معتبر نیست (eq, neq, contains, starts, in).";
            return null;
        }

        // --- تاریخ ---
        if (underlying == typeof(DateTime))
        {
            Expression Ge(Expression v, DateTime d) => Expression.GreaterThanOrEqual(v, Expression.Constant(d));
            Expression Lt(Expression v, DateTime d) => Expression.LessThan(v, Expression.Constant(d));
            var v = Val();
            if (op == "period")
            {
                var pc = BuildPeriodDate(v, Ge, Lt, f, today, out error);
                if (pc == null) return null;
                return isNullable ? Expression.AndAlso(Has(), pc) : pc;
            }
            var d1 = ParseDay(f.Value, today);
            if (d1 == null)
            {
                var rp = ResolvePeriod(f.Value, today);
                error = rp != null
                    ? $"«{f.Value}» یک بازه است نه یک روز؛ با op=period بده."
                    : $"تاریخ «{f.Value}» را نفهمیدم؛ روز دقیق (۱۴۰۴/۰۷/۰۵، امروز، دیروز) یا بازه با op=period (این هفته، این ماه، امسال) بنویس.";
                return null;
            }
            var day1 = d1.Value.Date;
            Expression? core = op switch
            {
                "eq" => Expression.AndAlso(Ge(v, day1), Lt(v, day1.AddDays(1))),
                "neq" => Expression.Not(Expression.AndAlso(Ge(v, day1), Lt(v, day1.AddDays(1)))),
                "gt" => Ge(v, day1.AddDays(1)),
                "gte" => Ge(v, day1),
                "lt" => Lt(v, day1),
                "lte" => Lt(v, day1.AddDays(1)),
                "between" => BuildBetweenDate(v, Ge, Lt, f, today, fd, out error),
                _ => null,
            };
            if (core == null && error == null) error = $"عملگر «{op}» برای تاریخ معتبر نیست (eq, neq, gt, gte, lt, lte, between, period).";
            if (core == null) return null;
            return isNullable && op != "neq" ? Expression.AndAlso(Has(), core) : core;
        }

        // --- بولین ---
        if (underlying == typeof(bool))
        {
            var b = ParseBool(f.Value);
            if (b == null && op != "in") { error = $"مقدار «{f.Value}» برای «{fd.Fa}» معتبر نیست (بله/خیر)."; return null; }
            if (op is "eq" or "neq")
            {
                var core = op == "eq"
                    ? Expression.Equal(Val(), Expression.Constant(b!.Value))
                    : Expression.NotEqual(Val(), Expression.Constant(b!.Value));
                return isNullable && op == "eq" ? Expression.AndAlso(Has(), core) : core;
            }
            error = $"عملگر «{op}» برای بله/خیر معتبر نیست (eq, neq).";
            return null;
        }

        // --- enum / عدد ---
        var isEnum = underlying.IsEnum;
        if (!isEnum && !IsNumeric(underlying))
        {
            error = $"فیلتر روی فیلد «{fd.Fa}» پشتیبانی نمی‌شود.";
            return null;
        }
        if (op == "between")
        {
            var a = ParseScalar(underlying, fd, f.Value, out var e1);
            var b2 = ParseScalar(underlying, fd, f.Value2, out var e2);
            if (a == null) { error = e1; return null; }
            if (b2 == null) { error = e2 ?? $"مقدار دوم بازه «{fd.Fa}» نامعتبر است."; return null; }
            var v = Val();
            var core = Expression.AndAlso(
                Expression.GreaterThanOrEqual(v, Expression.Constant(a, underlying)),
                Expression.LessThanOrEqual(v, Expression.Constant(b2, underlying)));
            return isNullable ? Expression.AndAlso(Has(), core) : core;
        }
        if (op == "in")
        {
            var parts = (f.Value ?? "").Split(new[] { ',', '،' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) { error = $"مقدار فیلتر «{fd.Fa}» خالی است."; return null; }
            Expression? or = null;
            foreach (var part in parts)
            {
                var pv = ParseScalar(underlying, fd, part, out var pe);
                if (pv == null) { error = pe; return null; }
                var eq = (Expression)Expression.Equal(Val(), Expression.Constant(pv, underlying));
                or = or == null ? eq : Expression.OrElse(or, eq);
            }
            return isNullable ? Expression.AndAlso(Has(), or!) : or!;
        }
        if (op is "contains" or "starts")
        {
            error = $"عملگر «{op}» فقط برای متن است؛ برای «{fd.Fa}» از eq, neq, gt, gte, lt, lte, between, in استفاده کن.";
            return null;
        }
        var scalar = ParseScalar(underlying, fd, f.Value, out var perr);
        if (scalar == null) { error = perr; return null; }
        var vv = Val();
        var cc = Expression.Constant(scalar, underlying);
        Expression cmp = op switch
        {
            "neq" => Expression.NotEqual(vv, cc),
            "gt" => Expression.GreaterThan(vv, cc),
            "gte" => Expression.GreaterThanOrEqual(vv, cc),
            "lt" => Expression.LessThan(vv, cc),
            "lte" => Expression.LessThanOrEqual(vv, cc),
            _ => Expression.Equal(vv, cc),
        };
        return isNullable && op != "neq" ? Expression.AndAlso(Has(), cmp) : cmp;
    }

    private static Expression? BuildBetweenDate(Expression v,
        Func<Expression, DateTime, Expression> ge, Func<Expression, DateTime, Expression> lt,
        AiExploreFilter f, DateTime today, AiFieldDef fd, out string? error)
    {
        error = null;
        var d1 = ParseDay(f.Value, today)?.Date;
        var d2 = ParseDay(f.Value2, today)?.Date;
        if (d1 == null || d2 == null)
        {
            error = $"بازه تاریخ «{fd.Fa}» نامعتبر است؛ هر دو مقدار را مثل ۱۴۰۴/۰۷/۰۱ بده.";
            return null;
        }
        var (from, to) = d1 <= d2 ? (d1.Value, d2.Value) : (d2.Value, d1.Value);
        return Expression.AndAlso(ge(v, from), lt(v, to.AddDays(1)));
    }

    private static Expression? BuildPeriodDate(Expression v,
        Func<Expression, DateTime, Expression> ge, Func<Expression, DateTime, Expression> lt,
        AiExploreFilter f, DateTime today, out string? error)
    {
        error = null;
        var r = ResolvePeriod(f.Value, today);
        if (r == null)
        {
            error = $"بازه «{f.Value}» را نفهمیدم؛ مثلاً: امروز، دیروز، این هفته، هفته گذشته، این ماه، ماه گذشته، امسال، پارسال، ۷ روز گذشته.";
            return null;
        }
        return Expression.AndAlso(ge(v, r.Value.From), lt(v, r.Value.To.AddDays(1)));
    }

    /// <summary>روز تنها: اول مفسر مشترک، بعد کلمات نسبی تک‌روزه (دیروز و...).</summary>
    private static DateTime? ParseDay(string? raw, DateTime today)
    {
        var d = AiLeaveHelper.ParseFaDate(raw, today);
        if (d != null) return d;
        var r = ResolvePeriod(raw, today);
        return r != null && r.Value.From == r.Value.To ? r.Value.From : null;
    }

    /// <summary>بازه نسبی فارسی → [از، تا] (هر دو شامل). ناشناس = null (بدون حدس ساکت).</summary>
    private static (DateTime From, DateTime To, string Label)? ResolvePeriod(string? raw, DateTime today)
    {
        var t = AiTextUtil.NormalizeFa(raw);
        if (t == "") return null;
        var pc = new PersianCalendar();
        var fy = pc.GetYear(today);
        var fm = pc.GetMonth(today);

        // روزهای تنها
        if (t is "امروز") return (today, today, "امروز");
        if (t is "دیروز") return (today.AddDays(-1), today.AddDays(-1), "دیروز");
        if (t is "فردا") return (today.AddDays(1), today.AddDays(1), "فردا");
        if (t is "پس فردا") return (today.AddDays(2), today.AddDays(2), "پس‌فردا");

        // فصل/ربع سال پشتیبانی نمی‌شود — نباید ساکت به «ماه» بچسبد
        if (t.Contains("فصل") || t.Contains("سه ماهه") || t.Contains("شش ماهه")) return null;

        // هفته (شنبه تا جمعه، کامل)
        if (t.Contains("هفته"))
        {
            var wn = ParseLeadingInt(t);
            if (wn != null)
            {
                if (t.Contains("گذشته") || t.Contains("اخیر") || t.Contains("پیش") || t.Contains("قبل"))
                {
                    var wdays = Math.Clamp(wn.Value * 7, 1, 365);
                    return (today.AddDays(-wdays + 1), today, $"{wn.Value} هفته گذشته");
                }
                return null;
            }
            var sinceSat = ((int)today.DayOfWeek + 1) % 7;
            var sat = today.AddDays(-sinceSat);
            if (t.Contains("گذشته") || t.Contains("قبل") || t.Contains("پیش"))
                return (sat.AddDays(-7), sat.AddDays(-1), "هفته گذشته");
            if (t.Contains("آینده") || t.Contains("بعد"))
                return (sat.AddDays(7), sat.AddDays(13), "هفته آینده");
            return (sat, sat.AddDays(6), "این هفته");
        }

        // ماه شمسی (کامل)
        if (t.Contains("ماه"))
        {
            if (ParseLeadingInt(t) != null) return null; // «۳ ماه گذشته» پشتیبانی نمی‌شود
            var (yy, mm) = (fy, fm);
            var label = "این ماه";
            if (t.Contains("گذشته") || t.Contains("قبل") || t.Contains("پیش"))
            {
                mm--; if (mm < 1) { mm = 12; yy--; }
                label = "ماه گذشته";
            }
            else if (t.Contains("آینده") || t.Contains("بعد"))
            {
                mm++; if (mm > 12) { mm = 1; yy++; }
                label = "ماه آینده";
            }
            var from = pc.ToDateTime(yy, mm, 1, 0, 0, 0, 0);
            return (from, from.AddDays(pc.GetDaysInMonth(yy, mm) - 1), label);
        }

        // سال شمسی (کامل)
        if (t.Contains("امسال"))
        {
            var from = pc.ToDateTime(fy, 1, 1, 0, 0, 0, 0);
            return (from, pc.ToDateTime(fy, 12, pc.GetDaysInMonth(fy, 12), 0, 0, 0, 0), "امسال");
        }
        if (t is "پارسال" || t.Contains("پارسال")
            || (t.Contains("سال") && (t.Contains("گذشته") || t.Contains("قبل") || t.Contains("پیش"))))
        {
            var from = pc.ToDateTime(fy - 1, 1, 1, 0, 0, 0, 0);
            return (from, pc.ToDateTime(fy - 1, 12, pc.GetDaysInMonth(fy - 1, 12), 0, 0, 0, 0), "پارسال");
        }

        // N روز گذشته/آینده
        var dn = ParseLeadingInt(t);
        if (dn != null && t.Contains("روز"))
        {
            var days = Math.Clamp(dn.Value, 1, 365);
            if (t.Contains("گذشته") || t.Contains("اخیر") || t.Contains("پیش") || t.Contains("قبل"))
                return (today.AddDays(-days + 1), today, $"{dn.Value} روز گذشته");
            if (t.Contains("آینده") || t.Contains("بعد"))
                return (today, today.AddDays(days - 1), $"{dn.Value} روز آینده");
        }
        return null;
    }

    private static int? ParseLeadingInt(string normFa)
    {
        var m = System.Text.RegularExpressions.Regex.Match(AiTextUtil.ToEnDigits(normFa), @"\d+");
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    private static bool IsNumeric(Type t) => t is not null && (t == typeof(int) || t == typeof(long)
        || t == typeof(short) || t == typeof(byte) || t == typeof(decimal)
        || t == typeof(double) || t == typeof(float));

    private static bool? ParseBool(string? raw)
    {
        var s = AiTextUtil.NormalizeFa(raw);
        return s switch
        {
            "true" or "1" or "بله" or "هست" or "آری" or "فعال" or "دارد" => true,
            "false" or "0" or "خیر" or "نه" or "نیست" or "غیرفعال" or "ندارد" => false,
            _ => null,
        };
    }

    private static object? ParseScalar(Type underlying, AiFieldDef fd, string? raw, out string? error)
    {
        error = null;
        var s = AiTextUtil.ToEnDigits(raw ?? "").Trim();

        // نگاشت معکوس فارسی (enum یا کد int)
        if (fd.MapFa != null)
        {
            var norm = AiTextUtil.NormalizeFa(raw);
            var hit = fd.MapFa.FirstOrDefault(kv =>
                AiTextUtil.NormalizeFa(kv.Value) == norm || kv.Key.Equals(s, StringComparison.OrdinalIgnoreCase));
            if (!hit.Equals(default(KeyValuePair<string, string>)))
            {
                var key = hit.Key;
                if (underlying.IsEnum)
                {
                    try { return Enum.Parse(underlying, key, true); }
                    catch { error = $"مقدار «{raw}» برای «{fd.Fa}» معتبر نیست."; return null; }
                }
                if (underlying == typeof(int) && int.TryParse(key, out var iv)) return iv;
            }
            else if (s != "" && !underlying.IsEnum && underlying == typeof(int) && int.TryParse(s, out var direct))
                return direct;
            else if (s != "" && underlying.IsEnum)
            {
                // تلاش آخر: نام انگلیسی یا عدد
                try { return Enum.Parse(underlying, s, true); }
                catch
                {
                    if (int.TryParse(s, out var iv)) return Enum.ToObject(underlying, iv);
                }
            }
            if (s == "" || fd.MapFa.All(kv => AiTextUtil.NormalizeFa(kv.Value) != norm
                && !kv.Key.Equals(s, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"مقدار «{raw}» برای «{fd.Fa}» معتبر نیست؛ یکی از: {string.Join("، ", fd.MapFa.Values)}";
                return null;
            }
        }

        if (underlying.IsEnum)
        {
            try { return Enum.Parse(underlying, s, true); }
            catch
            {
                if (int.TryParse(s, out var iv)) return Enum.ToObject(underlying, iv);
                error = $"مقدار «{raw}» برای «{fd.Fa}» معتبر نیست.";
                return null;
            }
        }
        var num = s.Replace(",", "").Replace("٬", "").Replace(" ", "").Replace("٫", ".").Replace("/", ".");
        if (!decimal.TryParse(num, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec))
        {
            error = $"مقدار «{raw}» برای «{fd.Fa}» عدد معتبر نیست.";
            return null;
        }
        try { return Convert.ChangeType(dec, underlying, CultureInfo.InvariantCulture); }
        catch { error = $"مقدار «{raw}» برای «{fd.Fa}» معتبر نیست."; return null; }
    }

    // ==================== خواندن و قالب‌بندی ====================

    private static string FormatDuration(TimeSpan t) => t.TotalMinutes < 1 ? "—"
        : $"{(int)t.TotalHours} ساعت" + (t.Minutes > 0 ? $" و {t.Minutes} دقیقه" : "");

    private static object? ReadPath(object row, string path)
    {
        object? cur = row;
        foreach (var part in path.Split('.'))
        {
            if (cur == null) return null;
            var prop = cur.GetType().GetProperty(part);
            if (prop == null) return null;
            cur = prop.GetValue(cur);
        }
        return cur;
    }

    private static object? FormatValue(AiFieldDef fd, object? raw)
    {
        if (raw == null) return null;
        if (fd.MapFa != null)
        {
            var key = raw is Enum e ? e.ToString() : raw.ToString() ?? "";
            if (fd.MapFa.TryGetValue(key, out var fa)) return fa;
            return key;
        }
        return fd.Kind switch
        {
            "date" when raw is DateTime d => AiDateUtil.ToFaShort(d),
            "datetime" when raw is DateTime d => AiDateUtil.ToFaShort(d) + " " + d.ToString("HH:mm"),
            "bool" when raw is bool b => b ? "بله" : "خیر",
            "duration" when raw is TimeSpan t => FormatDuration(t),
            "time" when raw is TimeOnly to => to.ToString("HH:mm"),
            "time" when raw is TimeSpan ts => ts.ToString(@"h\:mm"),
            "text" when raw is string s && fd.Truncate > 0 && s.Length > fd.Truncate
                => s[..fd.Truncate] + "…",
            _ => raw,
        };
    }
}
