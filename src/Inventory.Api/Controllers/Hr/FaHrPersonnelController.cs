using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// پرسنل و چارت سازمانی «منابع انسانی فروغ آریا» — روی جدول اختصاصی FaHrPersonnel
/// (جایگزین نمای پیشینِ hr-core/employees و hr-core/org که مال منابع انسانی اصلی است).
/// همهٔ خروجی‌ها فقط پرسنلِ پیوندخورده به کاربرانِ فعالِ دارای دسترسی به ماژول را برمی‌گردانند.
/// </summary>
[ApiController]
[Route("api/fa-hr")]
[Authorize]
public class FaHrPersonnelController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FaHrPersonnelService _svc;

    public FaHrPersonnelController(AppDbContext db, FaHrPersonnelService svc) { _db = db; _svc = svc; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    private static readonly string[] ReadPerms = { "LeaveRequests.Approve", "HrPay.Read", "HrPay.Manage", "Attendance.ViewAll" };
    private static readonly string[] ManagePerms = { "LeaveRequests.Approve", "HrPay.Manage" };

    private bool? _rd, _mg;
    private async Task<bool> HasAnyAsync(string[] perms)
    {
        if (User.IsInRole("Admin")) return true;
        if (perms.Any(p => User.HasClaim("permission", p))) return true;
        var keys = perms.ToList();
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, pm => pm.Id, (rp, pm) => pm)
            .AnyAsync(pm => keys.Contains(pm.Module + "." + pm.Action));
    }
    private async Task<bool> CanReadAsync() => _rd ??= await HasAnyAsync(ReadPerms);
    private async Task<bool> CanManageAsync() => _mg ??= await HasAnyAsync(ManagePerms);

    [HttpGet("personnel")]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListAsync(search, status, pagination), skip, take));
    }

    [HttpGet("personnel/{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (!await CanReadAsync()) return Forbid();
        var p = await _svc.GetAsync(id);
        return p == null ? NotFound() : Ok(p);
    }

    [HttpPost("personnel")]
    public async Task<IActionResult> Create([FromBody] FaHrPersonnelSaveDto dto)
    {
        if (!await CanManageAsync()) return Forbid();
        if (dto == null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        try { return Ok(await _svc.CreateAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("personnel/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] FaHrPersonnelSaveDto dto)
    {
        if (!await CanManageAsync()) return Forbid();
        if (dto == null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        try { return Ok(await _svc.UpdateAsync(id, dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("personnel/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await CanManageAsync()) return Forbid();
        try { await _svc.DeleteAsync(id); return Ok(new { ok = true }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>پرسنلِ فعلاً نامرئی (بدون پیوند به کاربر مجاز) — فقط برای مدیر و فقط جهت برقراری پیوند</summary>
    [HttpGet("personnel/pending")]
    public async Task<IActionResult> Pending()
    {
        if (!await CanManageAsync()) return Forbid();
        return Ok(await _svc.PendingAsync());
    }

    [HttpPost("personnel/{id:int}/link")]
    public async Task<IActionResult> Link(int id, [FromBody] FaHrLinkDto dto)
    {
        if (!await CanManageAsync()) return Forbid();
        try { return Ok(await _svc.LinkUserAsync(id, dto?.UserId ?? 0)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>کمبوی کاربر سیستم: فقط کاربرانِ فعالِ دارای دسترسی به ماژول</summary>
    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        if (!await CanReadAsync()) return Forbid();
        return Ok(await _svc.UserOptionsAsync());
    }

    [HttpGet("org")]
    public async Task<IActionResult> Org([FromQuery] bool includeInactive = false)
    {
        if (!await CanReadAsync()) return Forbid();
        return Ok(await _svc.OrgTreeAsync(includeInactive));
    }
}
