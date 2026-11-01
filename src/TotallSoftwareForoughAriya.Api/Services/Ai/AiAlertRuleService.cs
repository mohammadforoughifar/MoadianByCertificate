using System.Globalization;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// هشدارهای شرطی (§۲۳): هر قانون = یک پرس‌وجوی کاوش + یک آستانه.
// ارزیابی دوره‌ای؛ فقط در گذار false→true (لبه صعودی) در بله/ایتا خبر می‌دهد.
// =====================================================================

public class AiAlertRuleService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AiAlertRuleService> _log;

    public const int MaxActivePerUser = 10;
    private const int EvalFetchLimit = 2000;

    private static readonly JsonSerializerOptions CiOpts = new() { PropertyNameCaseInsensitive = true };

    public AiAlertRuleService(AppDbContext db, ILogger<AiAlertRuleService> log)
    {
        _db = db;
        _log = log;
    }

    public static string? NormalizeOp(string? op)
    {
        var s = AiTextUtil.NormalizeFa(op ?? "").Trim().ToLowerInvariant();
        if (s is "gt" or ">" or "بیشتر" or "بیشتراز" or "بیشتر_از" or "بالا" or "بالای" or "بزرگتر" or "بزرگ‌تر" or "over") return "gt";
        if (s is "lt" or "<" or "کمتر" or "کمتراز" or "کمتر_از" or "زیر" or "پایین" or "under") return "lt";
        if (s is "gte" or ">=" or "=>" or "حداقل" or "حد_اقل" or "دست_کم" or "دستکم") return "gte";
        if (s is "lte" or "<=" or "=<" or "حداکثر" or "حد_اکثر" or "بیشترین") return "lte";
        if (s is "eq" or "=" or "==" or "مساوی" or "برابر") return "eq";
        if (s.Contains("بیشتر") || s.Contains("بالا")) return "gt";
        if (s.Contains("کمتر") || s.Contains("زیر") || s.Contains("پایین")) return "lt";
        if (s.Contains("حداقل") || s.Contains("حد اقل")) return "gte";
        if (s.Contains("حداکثر") || s.Contains("حد اکثر")) return "lte";
        if (s.Contains("مساوی") || s.Contains("برابر")) return "eq";
        return null;
    }

    public static string OpFa(string op) => op switch
    {
        "gt" => "بیشتر از",
        "lt" => "کمتر از",
        "gte" => "حداقل",
        "lte" => "حداکثر",
        _ => "برابر",
    };

    public static string AggFa(string agg) => agg switch
    {
        "sum" => "جمع",
        "avg" => "میانگین",
        _ => "تعداد",
    };

    public static string FaNum(double v)
        => AiTextUtil.ToFaDigits(v.ToString("0.##", CultureInfo.InvariantCulture));

    // ==================== ثبت ====================

    public async Task<(AiAlertRule? rule, string? error, double current, bool firesNow, bool approximate)> CreateAsync(
        int userId, string title, AiExploreRequest req, string agg, string? aggField,
        string op, double value, IServiceProvider services, CancellationToken ct)
    {
        agg = (agg ?? "").Trim().ToLowerInvariant();
        if (agg is not ("count" or "sum" or "avg")) agg = "count";
        if (agg != "count" && string.IsNullOrWhiteSpace(aggField))
            return (null, "bad_agg", 0, false, false);

        var active = await _db.AiAlertRules.CountAsync(r => r.UserId == userId && r.IsActive, ct);
        if (active >= MaxActivePerUser)
            return (null, "too_many", 0, false, false);

        var (ok, err, current, firesNow, approximate) =
            await EvaluateQueryAsync(userId, req, agg, aggField, op, value, services, ct);
        if (!ok)
            return (null, "dry_failed:" + err, 0, false, false);

        var rule = new AiAlertRule
        {
            UserId = userId,
            Title = title,
            QueryJson = JsonSerializer.Serialize(req),
            Agg = agg,
            AggField = agg == "count" ? null : aggField,
            Op = op,
            Value = value,
            LastState = firesNow, // وضعیت اولیه = فعلی؛ تا گذار بعدی خبری نیست
            LastCheckedAt = DateTime.Now,
            CreatedAt = DateTime.Now,
        };
        _db.AiAlertRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return (rule, null, current, firesNow, approximate);
    }

    public Task<List<AiAlertRule>> ListActiveAsync(int userId, CancellationToken ct)
        => _db.AiAlertRules.AsNoTracking()
            .Where(r => r.UserId == userId && r.IsActive)
            .OrderBy(r => r.Id).ToListAsync(ct);

    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct)
    {
        var n = await _db.AiAlertRules
            .Where(r => r.Id == id && r.UserId == userId && r.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false), ct);
        return n > 0;
    }

    // ==================== ارزیابی ====================

    public async Task<(bool ok, string? error, double current, bool fires, bool approximate)> EvaluateQueryAsync(
        int userId, AiExploreRequest req, string agg, string? aggField,
        string op, double value, IServiceProvider services, CancellationToken ct)
    {
        req.Limit = EvalFetchLimit;
        if (agg == "count")
        {
            if (req.Select == null || req.Select.Count == 0)
            {
                var ent = AiDataCatalog.Find(req.Entity ?? "");
                var first = ent?.Fields.FirstOrDefault(f => !f.Sensitive)?.Name;
                if (first == null) return (false, "موجودیت معتبر نیست.", 0, false, false);
                req.Select = new() { first };
            }
            req.GroupBy = null; // آستانه روی تعداد کل است، نه گروه‌ها
        }
        else
        {
            req.Select = new() { aggField! };
            req.GroupBy = null;
        }

        var explorer = services.GetRequiredService<AiDataExplorer>();
        var (ok, error, result) = await explorer.QueryAsync(userId, req, ct);
        if (!ok || result == null)
            return (false, error ?? "اجرای پرس‌وجو ناموفق بود.", 0, false, false);

        double current;
        if (agg == "count")
        {
            current = result.TotalCount;
        }
        else
        {
            var idx = result.Columns.FindIndex(c =>
                string.Equals(c.Key, aggField, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
                return (false, $"فیلد «{aggField}» در نتیجه نیست.", 0, false, false);
            var nums = new List<double>();
            foreach (var row in result.Rows)
            {
                if (idx < row.Count && TryNum(row[idx], out var n))
                    nums.Add(n);
            }
            current = agg == "sum" ? nums.Sum() : nums.Count == 0 ? 0 : nums.Average();
        }
        var fires = Compare(current, op, value);
        return (true, null, current, fires, result.Truncated && agg != "count");
    }

    private static bool TryNum(object? cell, out double n)
    {
        n = 0;
        if (cell == null) return false;
        if (cell is double d) { n = d; return true; }
        if (cell is float f) { n = f; return true; }
        if (cell is decimal m) { n = (double)m; return true; }
        if (cell is int i) { n = i; return true; }
        if (cell is long l) { n = l; return true; }
        if (cell is string s)
            return double.TryParse(AiTextUtil.ToEnDigits(s).Replace(",", "").Replace("٬", "").Trim(),
                NumberStyles.Any, CultureInfo.InvariantCulture, out n);
        try { n = Convert.ToDouble(cell, CultureInfo.InvariantCulture); return true; }
        catch { return false; }
    }

    private static bool Compare(double current, string op, double value) => op switch
    {
        "gt" => current > value,
        "lt" => current < value,
        "gte" => current >= value,
        "lte" => current <= value,
        _ => Math.Abs(current - value) < 1e-9,
    };

    // ==================== ورکر ====================

    public async Task CheckDueAsync(IServiceProvider services, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var rules = await db.AiAlertRules.Where(r => r.IsActive).OrderBy(r => r.Id).Take(50).ToListAsync(ct);
        foreach (var rule in rules)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var user = await db.Users.AsNoTracking()
                    .Where(u => u.Id == rule.UserId)
                    .Select(u => new { u.Role, u.BaleChatId, u.EitaaChatId })
                    .FirstOrDefaultAsync(ct);
                var linked = user != null && (user.BaleChatId != null || user.EitaaChatId != null);
                var allowed = user != null && await AiAccessHelper.UserHasAsync(
                    db, rule.UserId, "AiAssistant", "Use", user.Role, ct);
                if (!allowed)
                    continue;
                var req = JsonSerializer.Deserialize<AiExploreRequest>(rule.QueryJson, CiOpts);
                if (req == null) throw new InvalidOperationException("مشخصات قانون خراب است.");
                var self = sp.GetRequiredService<AiAlertRuleService>();
                var (ok, err, current, fires, _) =
                    await self.EvaluateQueryAsync(rule.UserId, req, rule.Agg, rule.AggField, rule.Op, rule.Value, sp, ct);
                if (!ok) throw new InvalidOperationException(err);
                var rising = fires && !rule.LastState;
                rule.LastState = fires;
                rule.LastCheckedAt = DateTime.Now;
                rule.FailCount = 0;
                await db.SaveChangesAsync(ct);
                if (!rising) continue;

                if (!linked) continue; // بدون لینک، وضعیت جلو رفته؛ گذار بعدی خبر می‌دهد
                var messenger = sp.GetRequiredService<IMessengerService>();
                await messenger.SendToUserAsync(rule.UserId, "⚠️ هشدار: " + rule.Title,
                    $"📊 مقدار فعلی: {FaNum(current)} ({AggFa(rule.Agg)} {OpFa(rule.Op)} {FaNum(rule.Value)})\n🗑 حذف قانون: delete_alert {rule.Id}");
                rule.LastFiredAt = DateTime.Now;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                rule.FailCount++;
                rule.LastCheckedAt = DateTime.Now;
                _log.LogWarning(ex, "ارزیابی قانون هشدار {RuleId} ناموفق.", rule.Id);
                if (rule.FailCount >= 5)
                {
                    rule.IsActive = false;
                    try
                    {
                        var messenger = sp.GetRequiredService<IMessengerService>();
                        await messenger.SendToUserAsync(rule.UserId, "⏸ توقف قانون هشدار",
                            $"قانون «{rule.Title}» بعد از ۵ خطای پیاپی متوقف شد. با my_alert_rules ببین و دوباره بساز.");
                    }
                    catch { }
                }
                try { await db.SaveChangesAsync(ct); }
                catch { }
            }
        }
    }
}

/// <summary>ورکر ارزیابی دوره‌ای قانون‌های هشدار (هر ۵ دقیقه).</summary>
public class AiAlertRuleWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AiAlertRuleWorker> _log;

    public AiAlertRuleWorker(IServiceProvider services, ILogger<AiAlertRuleWorker> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var opts = scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value;
                if (opts.Enabled && opts.AlertRuleEnabled)
                {
                    var svc = scope.ServiceProvider.GetRequiredService<AiAlertRuleService>();
                    await svc.CheckDueAsync(_services, stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "خطای ورکر قانون‌های هشدار."); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
