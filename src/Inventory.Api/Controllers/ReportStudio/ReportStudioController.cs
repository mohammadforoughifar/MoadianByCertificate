using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.ReportStudio;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

// =====================================================================
//  گزارش‌ساز حرفه‌ای — API
//
//  GET    api/report-studio/catalog          → جدول‌ها و جوین‌های مجاز
//  POST   api/report-studio/preview          → اجرای زنده بدون ذخیره
//  GET    api/report-studio/reports          → فهرست گزارش‌های من و اشتراکی
//  GET    api/report-studio/reports/{id}     → یک گزارش
//  POST   api/report-studio/reports          → ساخت
//  PUT    api/report-studio/reports/{id}     → ویرایش
//  DELETE api/report-studio/reports/{id}     → حذف
//  POST   api/report-studio/reports/{id}/run → اجرای گزارش ذخیره‌شده
//  GET    api/report-studio/reports/{id}/export → خروجی CSV
//  PUT    api/report-studio/reports/{id}/shares → تنظیم اشتراک‌ها
//  GET    api/report-studio/share-targets    → کاربران و نقش‌ها برای انتخاب
// =====================================================================
[ApiController]
[Route("api/report-studio")]
[Authorize]
public class ReportStudioController : ControllerBase
{
    private const string Module = "ReportStudio";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _db;
    private readonly IEffectivePermissions _perms;
    private readonly IRsAccessService _access;
    private readonly IRsExecutor _exec;
    private readonly IRsRowSecurity _rowSec;

    public ReportStudioController(AppDbContext db, IEffectivePermissions perms,
        IRsAccessService access, IRsExecutor exec, IRsRowSecurity rowSec)
    {
        _db = db;
        _perms = perms;
        _access = access;
        _exec = exec;
        _rowSec = rowSec;
    }

    private int MyUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    private bool IsAdmin =>
        User.IsInRole("Admin") || User.FindFirstValue(ClaimTypes.Role) == "Admin";

    /// <summary>مجوز پایهٔ ماژول گزارش‌ساز.</summary>
    private async Task<ObjectResult?> GuardAsync(string action)
    {
        if (IsAdmin) return null;
        var perms = await _perms.GetAsync(User);
        if (perms.Contains($"{Module}.{action}")) return null;
        return StatusCode(403, new { message = $"برای این عملیات به مجوز «{Module}.{action}» نیاز دارید." });
    }

    private async Task EnsureDbAsync() => await ReportStudioSchemaV1.EnsureOnceAsync(_db);

    // =====================================================================
    //  کاتالوگ
    // =====================================================================
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog()
    {
        if (await GuardAsync("View") is ObjectResult fb) return fb;

        var perms = await _perms.GetAsync(User);
        var admin = IsAdmin;

        bool CanSee(string module) =>
            admin || string.IsNullOrWhiteSpace(module)
                  || perms.Contains($"{module}.Read") || perms.Contains($"{module}.View")
                  || perms.Contains($"{module}.Access");

        var tables = RsSchemaCatalog.Tables.Where(t => CanSee(t.Module)).ToList();
        var keys = tables.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Ok(new RsCatalogDto
        {
            Tables = tables,
            // فقط جوین‌هایی که هر دو سرشان برای کاربر قابل مشاهده است
            Joins = RsSchemaCatalog.Joins
                .Where(j => keys.Contains(j.FromTable) && keys.Contains(j.ToTable))
                .ToList(),
            CanDesign = admin || perms.Contains($"{Module}.Design"),
            CanShare = admin || perms.Contains($"{Module}.Share")
        });
    }

    // =====================================================================
    //  پیش‌نمایش زنده (بدون ذخیره)
    // =====================================================================
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] RsRunRequestDto? req)
    {
        if (await GuardAsync("View") is ObjectResult fb) return fb;
        if (req?.Query is null)
            return Ok(new RsResultDto { Ok = false, Error = "تعریف گزارش ارسال نشده است." });

        var v = RsValidator.Validate(req.Query);
        if (!v.Ok) return Ok(new RsResultDto { Ok = false, Error = v.ErrorText });

        var (ok, missing) = await _access.HasDataAccessAsync(User, v.Modules);
        if (!ok)
            return Ok(new RsResultDto
            {
                Ok = false,
                Error = $"برای دیدن دادهٔ جدول‌های انتخابی به مجوز ماژول «{missing}» نیاز دارید."
            });

        var scope = await _rowSec.GetScopeAsync(User);
        var res = await _exec.RunAsync(req.Query, "پیش‌نمایش", req.Prompts,
            req.Page, req.PageSize, scope, HttpContext.RequestAborted);
        return Ok(res);
    }

    // =====================================================================
    //  فهرست و خواندن
    // =====================================================================
    [HttpGet("reports")]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await GuardAsync("View") is ObjectResult fb) return fb;
        await EnsureDbAsync();

        var pagination = new Paging.Request(skip, take);
        var reports = await _access.VisibleReportsAsync(User, pagination);
        var owners = await OwnerNamesAsync(reports.Select(r => r.OwnerUserId));

        var list = new List<RsReportDto>();
        foreach (var r in reports)
        {
            var acc = await _access.EvaluateAsync(User, r);
            if (!acc.CanView) continue;
            list.Add(ToDto(r, acc, owners, includeQuery: false));
        }
        return Ok(pagination.Result(list));
    }

    [HttpGet("reports/{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await GuardAsync("View") is ObjectResult fb) return fb;
        await EnsureDbAsync();

        var r = await LoadAsync(id);
        if (r is null) return NotFound(new { message = "گزارش پیدا نشد." });

        var acc = await _access.EvaluateAsync(User, r);
        if (!acc.CanView) return StatusCode(403, new { message = acc.Reason });

        var owners = await OwnerNamesAsync(new[] { r.OwnerUserId });
        var dto = ToDto(r, acc, owners, includeQuery: true);
        await FillShareNamesAsync(dto);
        return Ok(dto);
    }

    // =====================================================================
    //  ساخت / ویرایش / حذف
    // =====================================================================
    [HttpPost("reports")]
    public async Task<IActionResult> Create([FromBody] RsReportDto? dto)
    {
        if (await GuardAsync("Design") is ObjectResult fb) return fb;
        if (dto is null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "نام گزارش را وارد کنید." });

        await EnsureDbAsync();

        var v = RsValidator.Validate(dto.Query);
        if (!v.Ok) return BadRequest(new { message = v.ErrorText });

        var (ok, missing) = await _access.HasDataAccessAsync(User, v.Modules);
        if (!ok) return StatusCode(403, new { message = $"به مجوز ماژول «{missing}» نیاز دارید." });

        var e = new RsReport
        {
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Icon = string.IsNullOrWhiteSpace(dto.Icon) ? "bi-file-earmark-bar-graph" : dto.Icon,
            Folder = dto.Folder?.Trim(),
            OwnerUserId = MyUserId,
            Visibility = (int)dto.Visibility,
            QueryJson = JsonSerializer.Serialize(dto.Query, Json),
            ModulesCsv = string.Join(',', v.Modules),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _db.RsReports.Add(e);
        await _db.SaveChangesAsync();

        await ApplySharesAsync(e, dto);
        await _db.SaveChangesAsync();

        return Ok(new { id = e.Id, message = "گزارش ذخیره شد." });
    }

    [HttpPut("reports/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RsReportDto? dto)
    {
        if (await GuardAsync("View") is ObjectResult fb) return fb;
        if (dto is null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        await EnsureDbAsync();

        var e = await LoadAsync(id);
        if (e is null) return NotFound(new { message = "گزارش پیدا نشد." });

        var acc = await _access.EvaluateAsync(User, e);
        if (!acc.CanEdit)
            return StatusCode(403, new { message = "اجازهٔ ویرایش این گزارش را ندارید." });

        var v = RsValidator.Validate(dto.Query);
        if (!v.Ok) return BadRequest(new { message = v.ErrorText });

        var (ok, missing) = await _access.HasDataAccessAsync(User, v.Modules);
        if (!ok) return StatusCode(403, new { message = $"به مجوز ماژول «{missing}» نیاز دارید." });

        if (!string.IsNullOrWhiteSpace(dto.Name)) e.Name = dto.Name.Trim();
        e.Description = dto.Description?.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Icon)) e.Icon = dto.Icon;
        e.Folder = dto.Folder?.Trim();
        e.QueryJson = JsonSerializer.Serialize(dto.Query, Json);
        e.ModulesCsv = string.Join(',', v.Modules);
        e.UpdatedAt = DateTime.Now;

        // تغییر سطح دید و اشتراک‌ها فقط برای مالک/ادمین
        if (acc.IsOwner || IsAdmin)
        {
            e.Visibility = (int)dto.Visibility;
            await ApplySharesAsync(e, dto);
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "گزارش به‌روزرسانی شد." });
    }

    [HttpDelete("reports/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await GuardAsync("View") is ObjectResult fb) return fb;
        await EnsureDbAsync();

        var e = await LoadAsync(id);
        if (e is null) return NotFound(new { message = "گزارش پیدا نشد." });

        var acc = await _access.EvaluateAsync(User, e);
        if (!acc.IsOwner && !IsAdmin)
            return StatusCode(403, new { message = "فقط سازندهٔ گزارش می‌تواند آن را حذف کند." });

        e.IsDelete = true;
        e.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { message = "گزارش حذف شد." });
    }

    // =====================================================================
    //  اجرا
    // =====================================================================
    [HttpPost("reports/{id:int}/run")]
    public async Task<IActionResult> Run(int id, [FromBody] RsRunRequestDto? req)
    {
        if (await GuardAsync("View") is ObjectResult fb) return fb;
        await EnsureDbAsync();

        var e = await LoadAsync(id);
        if (e is null) return NotFound(new { message = "گزارش پیدا نشد." });

        var acc = await _access.EvaluateAsync(User, e);
        if (!acc.CanView)
            return Ok(new RsResultDto { Ok = false, Error = acc.Reason });

        // نکتهٔ امنیتی: محدودهٔ سطر همیشه از کاربرِ *اجراکننده* گرفته می‌شود،
        // نه سازندهٔ گزارش. پس اشتراک گزارش هرگز دادهٔ دیگران را فاش نمی‌کند.
        var scope = await _rowSec.GetScopeAsync(User);
        var q = ParseQuery(e.QueryJson);
        var res = await _exec.RunAsync(q, e.Name, req?.Prompts ?? new(),
            req?.Page ?? 1, req?.PageSize ?? 100, scope, HttpContext.RequestAborted);
        return Ok(res);
    }

    [HttpGet("reports/{id:int}/export")]
    public async Task<IActionResult> Export(int id)
    {
        if (await GuardAsync("View") is ObjectResult fb) return fb;
        await EnsureDbAsync();

        var e = await LoadAsync(id);
        if (e is null) return NotFound(new { message = "گزارش پیدا نشد." });

        var acc = await _access.EvaluateAsync(User, e);
        if (!acc.CanExport)
            return StatusCode(403, new { message = "اجازهٔ گرفتن خروجی از این گزارش را ندارید." });

        var q = ParseQuery(e.QueryJson);
        q.Take = Math.Min(q.Take <= 0 ? 5000 : q.Take, RsValidator.MaxTake);
        var scope = await _rowSec.GetScopeAsync(User);
        var res = await _exec.RunAsync(q, e.Name, new List<RsPromptValueDto>(),
            1, RsValidator.MaxTake, scope, HttpContext.RequestAborted);

        if (!res.Ok) return BadRequest(new { message = res.Error });

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', res.Columns.Select(c => Csv(c.Title))));
        foreach (var row in res.Rows)
            sb.AppendLine(string.Join(',', row.Select(Csv)));

        // BOM لازم است تا اکسل فارسی را درست نشان دهد
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var name = string.Concat(e.Name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        return File(bytes, "text/csv; charset=utf-8", $"{name}.csv");
    }

    private static string Csv(string? v)
    {
        v ??= "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
    }

    // =====================================================================
    //  اشتراک‌گذاری
    // =====================================================================
    [HttpGet("share-targets")]
    public async Task<IActionResult> ShareTargets([FromQuery] string? q)
    {
        if (await GuardAsync("View") is ObjectResult fb) return fb;

        var users = _db.Users.AsNoTracking().Where(u => u.Id != MyUserId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            users = users.Where(u => u.Username.Contains(term)
                                  || (u.FirstName ?? "").Contains(term)
                                  || (u.LastName ?? "").Contains(term));
        }

        var userList = await users
            .OrderBy(u => u.Username)
            .Take(50)
            .Select(u => new RsUserShareDto
            {
                UserId = u.Id,
                UserName = u.Username,
                FullName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()
            })
            .ToListAsync();

        var roles = await _db.Roles.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new RsRoleShareDto { RoleId = r.Id, RoleName = r.Name })
            .ToListAsync();

        return Ok(new RsShareTargetsDto { Users = userList, Roles = roles });
    }

    [HttpPut("reports/{id:int}/shares")]
    public async Task<IActionResult> SetShares(int id, [FromBody] RsReportDto? dto)
    {
        if (await GuardAsync("View") is ObjectResult fb) return fb;
        if (dto is null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        await EnsureDbAsync();

        var e = await LoadAsync(id);
        if (e is null) return NotFound(new { message = "گزارش پیدا نشد." });

        var acc = await _access.EvaluateAsync(User, e);
        if (!acc.IsOwner && !IsAdmin)
            return StatusCode(403, new { message = "فقط سازندهٔ گزارش می‌تواند دسترسی‌ها را تغییر دهد." });

        e.Visibility = (int)dto.Visibility;
        await ApplySharesAsync(e, dto);
        e.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return Ok(new { message = "دسترسی‌ها ذخیره شد." });
    }

    /// <summary>جایگزینی کامل اشتراک‌ها با فهرست جدید.</summary>
    private async Task ApplySharesAsync(RsReport e, RsReportDto dto)
    {
        var oldUsers = await _db.RsReportUserShares.Where(s => s.ReportId == e.Id).ToListAsync();
        var oldRoles = await _db.RsReportRoleShares.Where(s => s.ReportId == e.Id).ToListAsync();
        _db.RsReportUserShares.RemoveRange(oldUsers);
        _db.RsReportRoleShares.RemoveRange(oldRoles);

        if (dto.Visibility != RsVisibility.Shared) return;

        foreach (var u in dto.UserShares.GroupBy(x => x.UserId).Select(g => g.First()))
        {
            if (u.UserId <= 0 || u.UserId == e.OwnerUserId) continue;
            if (!await _db.Users.AnyAsync(x => x.Id == u.UserId)) continue;
            _db.RsReportUserShares.Add(new RsReportUserShare
            {
                ReportId = e.Id, UserId = u.UserId, Access = (int)u.Access
            });
        }

        foreach (var r in dto.RoleShares.GroupBy(x => x.RoleId).Select(g => g.First()))
        {
            if (r.RoleId <= 0) continue;
            if (!await _db.Roles.AnyAsync(x => x.Id == r.RoleId)) continue;
            _db.RsReportRoleShares.Add(new RsReportRoleShare
            {
                ReportId = e.Id, RoleId = r.RoleId, Access = (int)r.Access
            });
        }
    }

    // =====================================================================
    //  کمکی‌ها
    // =====================================================================
    private Task<RsReport?> LoadAsync(int id) =>
        _db.RsReports
            .Include(r => r.UserShares)
            .Include(r => r.RoleShares)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDelete);

    private static RsQueryDto ParseQuery(string json)
    {
        try { return JsonSerializer.Deserialize<RsQueryDto>(json, Json) ?? new RsQueryDto(); }
        catch { return new RsQueryDto(); }
    }

    private async Task<Dictionary<int, string>> OwnerNamesAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
            .Select(u => new { u.Id, N = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(), u.Username })
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.N) ? u.Username : u.N);
    }

    private static RsReportDto ToDto(RsReport r, RsAccessInfo acc,
        Dictionary<int, string> owners, bool includeQuery) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Description = r.Description,
        Icon = r.Icon,
        Folder = r.Folder,
        Query = includeQuery ? ParseQuery(r.QueryJson) : new RsQueryDto(),
        Visibility = (RsVisibility)r.Visibility,
        UserShares = r.UserShares.Select(s => new RsUserShareDto
        {
            UserId = s.UserId, Access = (RsAccess)s.Access
        }).ToList(),
        RoleShares = r.RoleShares.Select(s => new RsRoleShareDto
        {
            RoleId = s.RoleId, Access = (RsAccess)s.Access
        }).ToList(),
        OwnerUserId = r.OwnerUserId,
        OwnerName = owners.GetValueOrDefault(r.OwnerUserId),
        IsMine = acc.IsOwner,
        MyAccess = acc.Level,
        CanEdit = acc.CanEdit,
        CanShare = acc.CanShare || acc.IsOwner,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    /// <summary>پر کردن نام کاربران و نقش‌ها در پنل اشتراک.</summary>
    private async Task FillShareNamesAsync(RsReportDto dto)
    {
        if (dto.UserShares.Count > 0)
        {
            var ids = dto.UserShares.Select(s => s.UserId).ToList();
            var users = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, N = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() })
                .ToListAsync();
            foreach (var s in dto.UserShares)
            {
                var u = users.FirstOrDefault(x => x.Id == s.UserId);
                s.UserName = u?.Username;
                s.FullName = string.IsNullOrWhiteSpace(u?.N) ? u?.Username : u!.N;
            }
        }
        if (dto.RoleShares.Count > 0)
        {
            var ids = dto.RoleShares.Select(s => s.RoleId).ToList();
            var roles = await _db.Roles.AsNoTracking().Where(r => ids.Contains(r.Id))
                .Select(r => new { r.Id, r.Name }).ToListAsync();
            foreach (var s in dto.RoleShares)
                s.RoleName = roles.FirstOrDefault(x => x.Id == s.RoleId)?.Name;
        }
    }
}
