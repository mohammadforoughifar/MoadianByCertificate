using System.Security.Claims;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.Dashboards;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

// =====================================================================
//  داشبورد شخصی کاربر
//
//  • GET  api/my-dashboards/widget-catalog      → ویجت‌های مجاز برای همین کاربر
//  • GET  api/my-dashboards/widget-data/{key}   → دادهٔ یک ویجت (با کنترل مجدد مجوز)
//  • GET  api/my-dashboards                     → داشبوردهای من
//  • POST api/my-dashboards                     → داشبورد جدید
//  • PUT  api/my-dashboards/{id}                → ذخیرهٔ کامل (نام + چیدمان ویجت‌ها)
//  • POST api/my-dashboards/{id}/default        → پیش‌فرض‌کردن
//  • DELETE api/my-dashboards/{id}              → حذف
//
//  امنیت: کاربر فقط داشبوردهای خودش را می‌بیند (UserId = MyUserId) و دادهٔ هر
//  ویجت تنها در صورت داشتن مجوز مشاهدهٔ ماژول مربوطه برگردانده می‌شود.
// =====================================================================
[ApiController]
[Route("api/my-dashboards")]
[Authorize]
public class DashboardsController : ControllerBase
{
    private const string Module = "MyDashboards";

    private readonly AppDbContext _db;
    private readonly IWidgetDataService _widgets;
    private readonly IEffectivePermissions _perms;

    public DashboardsController(AppDbContext db, IWidgetDataService widgets,
        IEffectivePermissions perms)
    {
        _db = db;
        _widgets = widgets;
        _perms = perms;
    }

    private int MyUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    // ================== دسترسی‌ها ==================

    /// <summary>مجموعهٔ دسترسی‌های مؤثر کاربر به شکل "Module.Action" (از JWT یا دیتابیس).</summary>
    private Task<HashSet<string>> MyPermissionsAsync() => _perms.GetAsync(User);

    /// <summary>
    /// خودتعمیری جدول‌های داشبورد شخصی؛ در صورت شکست، پیام خطای واقعی دیتابیس برمی‌گردد
    /// تا کاربر به‌جای صفحهٔ خالی، علت را ببیند.
    /// </summary>
    private async Task<IActionResult?> EnsureDashDbAsync()
    {
        await DashboardSchemaV1.EnsureOnceAsync(_db);
        // ویجت‌های اتوماسیون اداری به جدول IncomingLetters نیاز دارند و این جدول
        // در مایگریشن SquashedInitial ساخته نشده؛ اینجا خودتعمیر می‌شود.
        await IncomingLetterSchemaV1.EnsureOnceAsync(_db);
        try
        {
            _ = await _db.UserDashboards.AsNoTracking().Select(d => d.Id).FirstOrDefaultAsync();
            return null;
        }
        catch (Exception)
        {
            var why = DashboardSchemaV1.LastError is { Length: > 0 } e ? " خطای دیتابیس: " + e : "";
            return StatusCode(500, new
            {
                message = "جدول‌های داشبورد شخصی (UserDashboards / UserDashWidgets) در دیتابیس در دسترس نیستند." + why +
                          " — راه‌حل: اسکریپت inventory/sql/UserDashboards.sql را در SSMS اجرا کنید " +
                          "یا مجوز CREATE TABLE کاربرِ رشتهٔ اتصال را بررسی کنید؛ سپس API را ری‌استارت کنید."
            });
        }
    }

    private static bool CanSeeModule(HashSet<string> perms, string module) =>
        string.IsNullOrEmpty(module)
        || perms.Contains($"{module}.Read") || perms.Contains($"{module}.View") || perms.Contains($"{module}.Access");

    private async Task<IActionResult?> ForbiddenUnlessAsync(string action)
    {
        var perms = await MyPermissionsAsync();
        if (perms.Contains($"{Module}.{action}")) return null;
        // مدیر قدیمی بدون نقش RBAC
        if (await _perms.IsLegacyAdminAsync(User)) return null;
        return StatusCode(403, new { message = "شما به داشبورد شخصی دسترسی ندارید." });
    }

    // ================== کاتالوگ ویجت‌ها ==================

    /// <summary>ویجت‌هایی که کاربر مجوز مشاهدهٔ ماژولشان را دارد.</summary>
    [HttpGet("widget-catalog")]
    public async Task<IActionResult> Catalog()
    {
        if (await ForbiddenUnlessAsync("View") is ObjectResult fb) return fb;
        var perms = await MyPermissionsAsync();
        var isAdmin = !await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId)
                      && User.FindFirstValue(ClaimTypes.Role) == "Admin";

        var list = WidgetCatalog.All
            .Where(w => isAdmin || CanSeeModule(perms, w.Module))
            .OrderBy(w => w.Category).ThenBy(w => w.Title)
            .ToList();

        return Ok(new { canDesign = isAdmin || perms.Contains($"{Module}.Design"), widgets = list });
    }

    // ================== دادهٔ ویجت ==================

    [HttpGet("widget-data/{key}")]
    public async Task<IActionResult> WidgetData(string key,
        [FromQuery] DashChartType? chartType,
        [FromQuery] DashRange range = DashRange.Month,
        [FromQuery] int? limit = null,
        [FromQuery] string? cfg = null)
    {
        if (await ForbiddenUnlessAsync("View") is ObjectResult fb) return fb;

        // ویجت گزارش شخصی — گزارش‌ساز در حال بازطراحی است.
        // ویجت‌های قدیمیِ «rep:{id}» تا آماده شدن نسخهٔ جدید پیام راهنما نشان می‌دهند.
        if (key.StartsWith("rep:", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new WidgetDataDto
            {
                Key = key,
                Kind = DashWidgetKind.Table,
                Title = "گزارش شخصی",
                Error = ReportBuilderRetiredMessage,
                GeneratedAt = DateTime.Now
            });
        }

        var def = WidgetCatalog.Find(key);
        if (def is null) return NotFound(new { message = "ویجت پیدا نشد." });

        // کنترل واقعی دسترسی داده — مستقل از چیدمان ذخیره‌شدهٔ کاربر
        var perms = await MyPermissionsAsync();
        var isAdmin = !await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId)
                      && User.FindFirstValue(ClaimTypes.Role) == "Admin";
        if (!isAdmin && !CanSeeModule(perms, def.Module))
            return StatusCode(403, new { message = $"شما به دادهٔ این ویجت ({def.Title}) دسترسی ندارید." });

        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(cfg))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(cfg);
                if (parsed is not null) foreach (var kv in parsed) config[kv.Key] = kv.Value;
            }
            catch { /* پیکربندی نامعتبر = پیش‌فرض */ }
        }
        if (limit is > 0) config["limit"] = limit.Value.ToString();

        var data = await _widgets.GetAsync(def, MyUserId, chartType, range, config);
        return Ok(data);
    }

    /// <summary>پیام یکسان برای ویجت‌های گزارش‌سازِ بازنشسته.</summary>
    private const string ReportBuilderRetiredMessage =
        "گزارش‌ساز در حال بازطراحی است؛ این ویجت موقتاً غیرفعال است.";

    // ================== داشبوردهای من ==================

    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync("View") is ObjectResult fb) return fb;
        if (await EnsureDashDbAsync() is ObjectResult dbErr) return dbErr;

        var pagination = new Paging.Request(skip, take);
        var defaultId = await _db.UserDashboards.AsNoTracking().Where(d => d.UserId == MyUserId)
            .OrderByDescending(d => d.IsDefault).ThenBy(d => d.SortOrder).ThenBy(d => d.Id)
            .Select(d => (int?)d.Id).FirstOrDefaultAsync();
        var list = await _db.UserDashboards.AsNoTracking()
            .Where(d => d.UserId == MyUserId)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Id)
            .Select(d => new UserDashboardDto
            {
                Id = d.Id,
                Name = d.Name,
                IsDefault = d.IsDefault,
                SortOrder = d.SortOrder,
                UpdatedAt = d.UpdatedAt,
                Widgets = d.Widgets.OrderBy(w => w.SortOrder).ThenBy(w => w.Row).ThenBy(w => w.Col)
                    .Select(w => new UserDashWidgetDto
                    {
                        Id = w.Id,
                        WidgetKey = w.WidgetKey,
                        Title = w.Title,
                        Row = w.Row,
                        Col = w.Col,
                        W = w.W,
                        H = w.H,
                        SortOrder = w.SortOrder,
                        ChartType = w.ChartType == null ? null : (DashChartType)w.ChartType,
                        Range = (DashRange)w.Range
                    }).ToList()
            })
            .ToPageListAsync(pagination);

        // پیکربندی آزاد هر ویجت (JSON) جدا خوانده می‌شود تا در projection EF مشکل‌ساز نشود
        var ids = list.SelectMany(d => d.Widgets).Select(w => w.Id).ToList();
        if (ids.Count > 0)
        {
            var cfgs = await _db.UserDashWidgets.AsNoTracking()
                .Where(w => ids.Contains(w.Id))
                .Select(w => new { w.Id, w.ConfigJson })
                .ToDictionaryAsync(x => x.Id, x => x.ConfigJson);
            foreach (var d in list)
                foreach (var w in d.Widgets)
                    w.Config = ParseConfig(cfgs.GetValueOrDefault(w.Id));
        }

        // اولین داشبورد، پیش‌فرض تلقی می‌شود
        foreach (var dashboard in list)
            if (dashboard.Id == defaultId) dashboard.IsDefault = true;
        return Ok(pagination.Result(list));
    }

    public record SaveDashboardRequest(string? Name, bool IsDefault, List<UserDashWidgetDto>? Widgets);

    [HttpPost("")]
    public async Task<IActionResult> Create([FromBody] SaveDashboardRequest? req)
    {
        if (await ForbiddenUnlessAsync("Design") is ObjectResult fb) return fb;
        if (await EnsureDashDbAsync() is ObjectResult dbErr) return dbErr;

        var count = await _db.UserDashboards.CountAsync(d => d.UserId == MyUserId);
        if (count >= 10)
            return BadRequest(new { message = "حداکثر ۱۰ داشبورد می‌توانید بسازید." });

        var d = new UserDashboard
        {
            UserId = MyUserId,
            Name = string.IsNullOrWhiteSpace(req?.Name) ? $"داشبورد {Fa1(count + 1)}" : req!.Name!.Trim(),
            IsDefault = count == 0 || req?.IsDefault == true,
            SortOrder = count,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _db.UserDashboards.Add(d);
        await _db.SaveChangesAsync();

        if (req?.Widgets is { Count: > 0 })
            await ReplaceWidgetsAsync(d.Id, req.Widgets);

        return Ok(new { d.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveDashboardRequest? req)
    {
        if (await ForbiddenUnlessAsync("Design") is ObjectResult fb) return fb;
        if (await EnsureDashDbAsync() is ObjectResult dbErr) return dbErr;

        var d = await _db.UserDashboards.FirstOrDefaultAsync(x => x.Id == id && x.UserId == MyUserId);
        if (d is null) return NotFound(new { message = "داشبورد پیدا نشد." });

        if (!string.IsNullOrWhiteSpace(req?.Name)) d.Name = req.Name!.Trim();
        if (req is not null) d.IsDefault = req.IsDefault;
        d.UpdatedAt = DateTime.Now;

        if (req?.IsDefault == true)
        {
            var others = await _db.UserDashboards
                .Where(x => x.UserId == MyUserId && x.Id != id && x.IsDefault).ToListAsync();
            foreach (var o in others) o.IsDefault = false;
        }

        if (req?.Widgets is not null)
            await ReplaceWidgetsAsync(id, req.Widgets);

        await _db.SaveChangesAsync();
        return Ok(new { d.Id });
    }

    [HttpPost("{id:int}/default")]
    public async Task<IActionResult> SetDefault(int id)
    {
        if (await ForbiddenUnlessAsync("Design") is ObjectResult fb) return fb;

        var mine = await _db.UserDashboards.Where(x => x.UserId == MyUserId).ToListAsync();
        var target = mine.FirstOrDefault(x => x.Id == id);
        if (target is null) return NotFound(new { message = "داشبورد پیدا نشد." });
        foreach (var x in mine) x.IsDefault = x.Id == id;
        await _db.SaveChangesAsync();
        return Ok(new { id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("Design") is ObjectResult fb) return fb;

        var d = await _db.UserDashboards.FirstOrDefaultAsync(x => x.Id == id && x.UserId == MyUserId);
        if (d is null) return NotFound(new { message = "داشبورد پیدا نشد." });

        await _db.UserDashWidgets.Where(w => w.DashboardId == id).ExecuteDeleteAsync();
        _db.UserDashboards.Remove(d);
        await _db.SaveChangesAsync();

        // اگر داشبورد پیش‌فرض حذف شد، اولین داشبورد باقی‌مانده پیش‌فرض می‌شود
        if (d.IsDefault)
        {
            var next = await _db.UserDashboards.Where(x => x.UserId == MyUserId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).FirstOrDefaultAsync();
            if (next is not null) { next.IsDefault = true; await _db.SaveChangesAsync(); }
        }
        return Ok(new { id });
    }

    // ================== کمکی‌ها ==================

    /// <summary>جایگزینی کامل ویجت‌های یک داشبورد با چیدمان ارسالی.</summary>
    private async Task ReplaceWidgetsAsync(int dashboardId, List<UserDashWidgetDto> widgets)
    {
        await _db.UserDashWidgets.Where(w => w.DashboardId == dashboardId).ExecuteDeleteAsync();

        var perms = await MyPermissionsAsync();
        var isAdmin = await _perms.IsLegacyAdminAsync(User);
        var sort = 0;
        foreach (var w in widgets)
        {
            // کلید ممکن است ویجت کاتالوگ یا گزارش شخصی («rep:{id}») باشد؛ هر دو اعتبارسنجی می‌شوند
            var resolved = await ResolveWidgetAsync(w.WidgetKey, perms, isAdmin);
            if (resolved is null) continue;         // ویجت نامعتبر/حذف‌شده — بی‌صدا رد می‌شود
            var (key, title, kind) = resolved.Value;

            var defW = w.W > 0 ? w.W : kind switch { DashWidgetKind.Table => 9, DashWidgetKind.Chart => 6, _ => 3 };
            var defH = w.H > 0 ? w.H : kind switch { DashWidgetKind.Table => 3, DashWidgetKind.Chart => 3, _ => 2 };

            _db.UserDashWidgets.Add(new UserDashWidget
            {
                DashboardId = dashboardId,
                WidgetKey = key,
                Title = string.IsNullOrWhiteSpace(w.Title) ? title : w.Title.Trim(),
                Row = Math.Max(0, w.Row),
                Col = Math.Clamp(w.Col, 0, 11),
                W = Math.Clamp(defW, 1, 12),
                H = Math.Clamp(defH, 1, 8),
                SortOrder = sort++,
                ChartType = w.ChartType is null ? null : (int)w.ChartType,
                Range = (int)w.Range,
                ConfigJson = w.Config is { Count: > 0 } ? JsonSerializer.Serialize(w.Config) : null,
                CreatedAt = DateTime.Now
            });
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// اعتبارسنجی کلید ویجت هنگام ذخیرهٔ چیدمان.
    /// ویجت‌های قدیمی «rep:{id}» (گزارش‌سازِ بازنشسته) حفظ می‌شوند تا چیدمان کاربر
    /// خراب نشود، ولی هنگام نمایش پیام «در حال بازطراحی» می‌گیرند.
    /// </summary>
    private async Task<(string Key, string? Title, DashWidgetKind Kind)?> ResolveWidgetAsync(
        string key, HashSet<string> perms, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        if (key.StartsWith("rep:", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(key.AsSpan(4), out var rid)) return null;
            return ("rep:" + rid, "گزارش شخصی", DashWidgetKind.Table);
        }

        var def = WidgetCatalog.Find(key);
        if (def is null) return null;
        if (!isAdmin && !CanSeeModule(perms, def.Module)) return null;
        return (def.Key, def.Title, def.Kind);
    }

    private static Dictionary<string, string> ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }

    private static string Fa1(int n) => Inventory.Shared.Fa.Digits(n.ToString());
}
