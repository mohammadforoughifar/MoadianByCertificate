using Inventory.Api.Services;
using System.Text;
using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

/// <summary>
/// لاگ عملیات نرم‌افزار — فقط مدیر؛ از بخش تنظیمات. ثبت خودکار توسط AuditLogFilter انجام می‌شود.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditLogsController(AppDbContext db) => _db = db;

    private bool IsAdmin => User.IsInRole("Admin");

    /// <summary>لیست لاگ‌ها با فیلتر حرفه‌ای: کاربر، ماژول، بازه تاریخی، جستجوی متنی + صفحه‌بندی</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] int? userId = null,
        [FromQuery] string? module = null,
        [FromQuery] string? action = null,
        [FromQuery] string? q = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        if (!IsAdmin) return Forbid();
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;
        if (page < 1) page = 1;

        var query2 = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (userId.HasValue) query2 = query2.Where(l => l.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(module)) query2 = query2.Where(l => l.Module == module);
        if (!string.IsNullOrWhiteSpace(action)) query2 = query2.Where(l => l.Action == action);
        if (from.HasValue) query2 = query2.Where(l => l.At >= from.Value.Date);
        if (to.HasValue) query2 = query2.Where(l => l.At < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query2 = query2.Where(l =>
                (l.Username != null && l.Username.Contains(term)) ||
                (l.Summary != null && l.Summary.Contains(term)) ||
                (l.Payload != null && l.Payload.Contains(term)) ||
                (l.Path != null && l.Path.Contains(term)) ||
                (l.Ip != null && l.Ip.Contains(term)));
        }

        var total = await query2.CountAsync();
        var items = await query2
            .OrderByDescending(l => l.At)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                l.Id,
                l.At,
                l.UserId,
                l.Username,
                l.Module,
                l.Action,
                l.HttpMethod,
                l.Summary,
                l.Ip,
                l.Device,
                l.StatusCode,
                l.DurationMs,
                HasPayload = l.Payload != null,
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>جزئیات کامل یک لاگ (شامل بدنه‌ی درخواست)</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        if (!IsAdmin) return Forbid();
        var log = await _db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (log == null) return NotFound(new { message = "لاگ پیدا نشد." });
        return Ok(log);
    }

    /// <summary>ماژول‌های ثبت‌شده (برای کمبوی فیلتر)</summary>
    [HttpGet("modules")]
    public async Task<IActionResult> Modules([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!IsAdmin) return Forbid();
        var list = await _db.AuditLogs.AsNoTracking()
            .Select(l => l.Module).Distinct().OrderBy(m => m).ToPageListAsync(pagination);
        return Ok(pagination.Result(list));
    }

    /// <summary>کاربران ثبت‌شده در لاگ (برای کمبوی سرچ‌دار)</summary>
    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!IsAdmin) return Forbid();
        var list = await _db.AuditLogs.AsNoTracking()
            .Where(l => l.UserId != null)
            .GroupBy(l => new { l.UserId, l.Username })
            .Select(g => new { Id = g.Key.UserId, Name = g.Key.Username })
            .OrderBy(u => u.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);
        return Ok(pagination.Result(list));
    }

    /// <summary>اکشن‌های یک ماژول (برای فیلتر دوم)</summary>
    [HttpGet("actions")]
    public async Task<IActionResult> Actions([FromQuery] string? module = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!IsAdmin) return Forbid();
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(module)) q = q.Where(l => l.Module == module);
        var list = await q.Select(l => l.Action).Distinct().OrderBy(a => a).ToPageListAsync(pagination);
        return Ok(pagination.Result(list));
    }

    /// <summary>خروجی CSV با همان فیلترهای فعال</summary>
    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] int? userId = null,
        [FromQuery] string? module = null,
        [FromQuery] string? action = null,
        [FromQuery] string? q = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (!IsAdmin) return Forbid();

        var query2 = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (userId.HasValue) query2 = query2.Where(l => l.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(module)) query2 = query2.Where(l => l.Module == module);
        if (!string.IsNullOrWhiteSpace(action)) query2 = query2.Where(l => l.Action == action);
        if (from.HasValue) query2 = query2.Where(l => l.At >= from.Value.Date);
        if (to.HasValue) query2 = query2.Where(l => l.At < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query2 = query2.Where(l =>
                (l.Username != null && l.Username.Contains(term)) ||
                (l.Summary != null && l.Summary.Contains(term)) ||
                (l.Payload != null && l.Payload.Contains(term)) ||
                (l.Path != null && l.Path.Contains(term)) ||
                (l.Ip != null && l.Ip.Contains(term)));
        }

        var rows = await query2.OrderByDescending(l => l.At).Take(5000).ToListAsync();
        var sb = new StringBuilder("زمان;کاربر;ماژول;عملیات;متد;مسیر;خلاصه;کد وضعیت;مدت(ms);IP;دستگاه\n");
        foreach (var l in rows)
        {
            sb.Append(PersianDate.ToShortWithTime(l.At)).Append(';')
              .Append(l.Username).Append(';')
              .Append(l.Module).Append(';')
              .Append(l.Action).Append(';')
              .Append(l.HttpMethod).Append(';')
              .Append(l.Path).Append(';')
              .Append((l.Summary ?? "").Replace(";", "،")).Append(';')
              .Append(l.StatusCode).Append(';')
              .Append(l.DurationMs).Append(';')
              .Append(l.Ip).Append(';')
              .Append((l.Device ?? "").Replace(";", " ").Replace("\n", " ")).Append('\n');
        }
        return File(Encoding.UTF8.GetBytes("\uFEFF" + sb.ToString()), "text/csv", $"audit-log-{PersianDate.TodayInput()}.csv");
    }
}
