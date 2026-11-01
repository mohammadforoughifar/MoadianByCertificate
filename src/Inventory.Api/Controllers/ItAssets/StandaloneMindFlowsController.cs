using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>Standalone MindFlows stored in the Inventory database, optionally shared as viewer/editor.</summary>
[ApiController]
[Route("api/mindflows")]
[Authorize]
public sealed class StandaloneMindFlowsController : ControllerBase
{
    private const string Viewer = "Viewer";
    private const string Editor = "Editor";
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;

    public StandaloneMindFlowsController(AppDbContext db, INotifyService notify)
    {
        _db = db;
        _notify = notify;
    }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";
    private bool IsLegacyAdmin => User.IsInRole("Admin");

    private async Task<int?> ActiveCompanyIdAsync()
    {
        if (HttpContext.Items["ActiveCompanyId"] is int active) return active > 0 ? active : null;
        if (!int.TryParse(Request.Headers["X-Company-Id"].FirstOrDefault(), out var id) || id <= 0) return null;
        return await _db.UserCompanyAccesses.AnyAsync(a => a.UserId == MyUserId && a.CompanyId == id) || IsLegacyAdmin ? id : null;
    }

    private Task<StandaloneMindFlow?> FindMapAsync(int id, int companyId)
        => _db.StandaloneMindFlows.FirstOrDefaultAsync(m => m.Id == id && m.CompanyId == companyId);

    private async Task<string?> AccessRoleAsync(StandaloneMindFlow map)
    {
        if (map.OwnerUserId == MyUserId) return "Owner";
        return await _db.StandaloneMindFlowMembers.AsNoTracking()
            .Where(m => m.MindFlowId == map.Id && m.UserId == MyUserId)
            .Select(m => m.Role).FirstOrDefaultAsync();
    }

    private static bool CanEditRole(string? role) => role is "Owner" or Editor;

    // ================== مجوزهای ماژول «نگار ذهنی» ==================
    // نگار ذهنی تا امروز فقط بر پایهٔ «دعوت/مالکیت همان نگاشت» کنترل می‌شد و هیچ
    // کلیدی در «تنظیمات ← نقش‌ها» نداشت. حالا دو لایه داریم:
    //   ۱) مجوز ماژول MindFlow (View/Create/Update/Delete/Members) — سیاست کلی شرکت
    //   ۲) نقش روی همان نگاشت (Owner/Editor/Viewer) — سیاست هر نگاشت
    // برای دیدن/ویرایش هر دو لازم است؛ «حذف» و «مدیریت اعضا» همچنان فقط سازنده.
    /// <summary>بررسی مجوز RBAC؛ هم‌قاعده با WorkProjects (مدیر قدیمی همه‌چیز، اپراتور قدیمی مشاهده/ساخت/ویرایش).</summary>
    private async Task<bool> HasAsync(string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy == "Operator") return action is "View" or "Create" or "Update";
            return false;
        }
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == "MindFlow" && p.Action == action);
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!await HasAsync("View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var maps = await _db.StandaloneMindFlows.AsNoTracking()
            .Where(m => m.CompanyId == companyId.Value
                && (m.OwnerUserId == MyUserId || _db.StandaloneMindFlowMembers.Any(x => x.MindFlowId == m.Id && x.UserId == MyUserId)))
            .OrderByDescending(m => m.UpdatedAt)
            .Select(m => new { Map = m, SharedRole = _db.StandaloneMindFlowMembers.Where(x => x.MindFlowId == m.Id && x.UserId == MyUserId).Select(x => x.Role).FirstOrDefault() })
            .ToListAsync();
        return Ok(maps.Select(x => new StandaloneMindFlowSummaryDto
        {
            Id = x.Map.Id,
            Title = x.Map.Title,
            OwnerUserId = x.Map.OwnerUserId,
            OwnerName = x.Map.OwnerName,
            Role = x.Map.OwnerUserId == MyUserId ? "Owner" : x.SharedRole ?? Viewer,
            IsOwner = x.Map.OwnerUserId == MyUserId,
            CanEdit = x.Map.OwnerUserId == MyUserId || x.SharedRole == Editor,
            CanManageAccess = x.Map.OwnerUserId == MyUserId,
            UpdatedAt = x.Map.UpdatedAt
        }).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStandaloneMindFlowDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        if (dto is null || string.IsNullOrWhiteSpace(dto.Title) || dto.Title.Trim().Length > 200)
            return BadRequest(new { message = "نام نگاشت باید بین ۱ تا ۲۰۰ نویسه باشد." });
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var owner = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        var title = dto.Title.Trim();
        var map = new WorkProjectMindFlowDto
        {
            ProjectId = 0,
            Title = title,
            Nodes = new() { new() { Text = title, X = 800, Y = 450, Color = "#596cf3" } }
        };
        var now = DateTime.Now;
        var ownerName = UserDisplay.Name(owner);
        var entity = new StandaloneMindFlow
        {
            CompanyId = companyId.Value,
            OwnerUserId = MyUserId,
            OwnerName = string.IsNullOrWhiteSpace(ownerName) ? MyUsername : ownerName,
            Title = title,
            MapJson = System.Text.Json.JsonSerializer.Serialize(map, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.StandaloneMindFlows.Add(entity);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("mindflows");
        return Ok(new StandaloneMindFlowSummaryDto
        {
            Id = entity.Id, Title = entity.Title, OwnerUserId = entity.OwnerUserId, OwnerName = entity.OwnerName,
            Role = "Owner", IsOwner = true, CanEdit = true, CanManageAccess = true, UpdatedAt = entity.UpdatedAt
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (!await HasAsync("View")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var map = await FindMapAsync(id, companyId.Value);
        if (map is null) return NotFound(new { message = "نگاشت مستقل پیدا نشد." });
        if (await AccessRoleAsync(map) is null) return Forbid();
        var dto = Deserialize(map.MapJson, map.Title);
        return dto is null ? Conflict(new { message = "دادهٔ نگاشت قابل خواندن نیست." }) : Ok(dto);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Save(int id, [FromBody] WorkProjectMindFlowDto dto)
    {
        if (!await HasAsync("Update")) return Forbid();
        if (dto is null) return BadRequest(new { message = "دادهٔ نگاشت ارسال نشده است." });
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var map = await FindMapAsync(id, companyId.Value);
        if (map is null) return NotFound(new { message = "نگاشت مستقل پیدا نشد." });
        if (!CanEditRole(await AccessRoleAsync(map))) return Forbid();
        var error = WorkProjectsController.ValidateMindFlow(dto);
        if (error is not null) return BadRequest(new { message = error });
        var savedMap = Deserialize(map.MapJson, map.Title);
        if (savedMap is null) return Conflict(new { message = "دادهٔ نگاشت قبلی قابل خواندن نیست." });
        WorkProjectsController.PreserveSavedSubMindFlows(savedMap, dto);
        WorkProjectsController.NormalizeMindFlowMap(dto, 0);
        error = WorkProjectsController.ValidateMindFlow(dto);
        if (error is not null) return BadRequest(new { message = error });
        error = await ValidateLinksAsync(dto, companyId.Value);
        if (error is not null) return BadRequest(new { message = error });
        var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (json.Length > 1_000_000) return BadRequest(new { message = "حجم نقشه از حد مجاز یک مگابایت بیشتر است." });
        map.Title = dto.Title.Trim();
        map.MapJson = json;
        map.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("mindflows");
        return Ok(new { id = map.Id, savedAt = map.UpdatedAt });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await HasAsync("Delete")) return Forbid();
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return BadRequest(new { message = "شرکت فعال انتخاب نشده است." });
        var map = await FindMapAsync(id, companyId.Value);
        if (map is null) return NotFound(new { message = "نگاشت مستقل پیدا نشد." });
        if (map.OwnerUserId != MyUserId) return Forbid();
        _db.StandaloneMindFlows.Remove(map);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("mindflows");
        return Ok();
    }

    [HttpGet("{id:int}/sub")]
    public async Task<IActionResult> GetSub(int id, [FromQuery] string? path)
    {
        if (!await HasAsync("View")) return Forbid();
        var result = await GetAccessibleMapAsync(id);
        if (result.Error is not null) return result.Error;
        if (!WorkProjectsController.TryParseSubMindFlowPath(path, out var pathIds))
            return BadRequest(new { message = "مسیر زیرنگاشت معتبر نیست." });
        var root = Deserialize(result.Map!.MapJson, result.Map.Title);
        if (root is null) return Conflict(new { message = "دادهٔ نگاشت قابل خواندن نیست." });
        var current = root;
        foreach (var nodeId in pathIds)
        {
            var owner = current.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (owner?.ChildMindFlow is null) return NotFound(new { message = "زیرنگاشت پیدا نشد." });
            current = owner.ChildMindFlow;
        }
        return Ok(current);
    }

    [HttpPut("{id:int}/sub")]
    public async Task<IActionResult> SaveSub(int id, [FromQuery] string? path, [FromBody] WorkProjectMindFlowDto dto)
    {
        if (!await HasAsync("Update")) return Forbid();
        if (dto is null) return BadRequest(new { message = "دادهٔ زیرنگاشت ارسال نشده است." });
        var result = await GetAccessibleMapAsync(id, requireEdit: true);
        if (result.Error is not null) return result.Error;
        if (!WorkProjectsController.TryParseSubMindFlowPath(path, out var pathIds))
            return BadRequest(new { message = "مسیر زیرنگاشت معتبر نیست." });
        var root = Deserialize(result.Map!.MapJson, result.Map.Title);
        if (root is null) return Conflict(new { message = "دادهٔ نگاشت اصلی قابل خواندن نیست." });
        var current = root;
        WorkProjectMindFlowNodeDto? owner = null;
        for (var i = 0; i < pathIds.Count; i++)
        {
            owner = current.Nodes.FirstOrDefault(n => n.Id == pathIds[i]);
            if (owner is null) return NotFound(new { message = "گرهٔ والد زیرنگاشت پیدا نشد." });
            if (i < pathIds.Count - 1)
            {
                if (owner.ChildMindFlow is null) return NotFound(new { message = "زیرنگاشت در مسیر پیدا نشد." });
                current = owner.ChildMindFlow;
            }
        }
        var error = WorkProjectsController.ValidateMindFlow(dto);
        if (error is not null) return BadRequest(new { message = error });
        if (owner?.ChildMindFlow is not null) WorkProjectsController.PreserveSavedSubMindFlows(owner.ChildMindFlow, dto);
        WorkProjectsController.NormalizeMindFlowMap(dto, 0);
        if (owner is null) return BadRequest(new { message = "مسیر زیرنگاشت معتبر نیست." });
        owner.ChildMindFlow = dto;
        error = WorkProjectsController.ValidateMindFlow(root);
        if (error is not null) return BadRequest(new { message = error });
        error = await ValidateLinksAsync(root, result.Map!.CompanyId);
        if (error is not null) return BadRequest(new { message = error });
        var json = System.Text.Json.JsonSerializer.Serialize(root, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if (json.Length > 1_000_000) return BadRequest(new { message = "حجم نقشه از حد مجاز یک مگابایت بیشتر است." });
        result.Map!.Title = root.Title;
        result.Map.MapJson = json;
        result.Map.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("mindflows");
        return Ok(new { id, savedAt = result.Map.UpdatedAt });
    }

    [HttpGet("{id:int}/members")]
    public async Task<IActionResult> Members(int id)
    {
        if (!await HasAsync("Members")) return Forbid();
        var result = await GetAccessibleMapAsync(id, requireOwner: true);
        if (result.Error is not null) return result.Error;
        var rows = await _db.StandaloneMindFlowMembers.AsNoTracking().Where(m => m.MindFlowId == id)
            .Join(_db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { Member = m, User = u })
            .OrderBy(x => x.User.FirstName).ThenBy(x => x.User.LastName).ToListAsync();
        return Ok(rows.Select(x => new WorkProjectMindFlowMemberDto
        { UserId = x.User.Id, Name = UserDisplay.Name(x.User), Role = x.Member.Role, AddedAt = x.Member.AddedAt }).ToList());
    }

    [HttpGet("{id:int}/candidates")]
    public async Task<IActionResult> Candidates(int id, [FromQuery] string? q, [FromQuery] int? take)
    {
        if (!await HasAsync("Members")) return Forbid();
        var result = await GetAccessibleMapAsync(id, requireOwner: true);
        if (result.Error is not null) return result.Error;
        var map = result.Map!;
        var query = _db.Users.AsNoTracking().Where(u => u.IsActive
            && _db.UserCompanyAccesses.Any(a => a.UserId == u.Id && a.CompanyId == map.CompanyId)
            && u.Id != map.OwnerUserId
            && !_db.StandaloneMindFlowMembers.Any(m => m.MindFlowId == id && m.UserId == u.Id));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(u => EF.Functions.Like(u.Username, $"%{term}%")
                || EF.Functions.Like(u.FirstName, $"%{term}%") || EF.Functions.Like(u.LastName, $"%{term}%"));
        }
        var count = Math.Clamp(take is > 0 ? take.Value : 50, 1, 100);
        var users = await query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName).Take(count).ToListAsync();
        return Ok(users.Select(u => new WorkProjectMindFlowUserDto { UserId = u.Id, Name = UserDisplay.Name(u) }).ToList());
    }

    [HttpPost("{id:int}/members")]
    public async Task<IActionResult> AddMembers(int id, [FromBody] AddWorkProjectMindFlowMembersDto dto)
    {
        if (!await HasAsync("Members")) return Forbid();
        if (dto is null) return BadRequest(new { message = "اطلاعات دعوت ارسال نشده است." });
        var result = await GetAccessibleMapAsync(id, requireOwner: true);
        if (result.Error is not null) return result.Error;
        var map = result.Map!;
        var userIds = (dto?.UserIds ?? new()).Where(x => x > 0 && x != map.OwnerUserId).Distinct().ToList();
        if (userIds.Count == 0) return BadRequest(new { message = "حداقل یک کاربر را انتخاب کنید." });
        var role = dto.Role is Editor or Viewer ? dto.Role : Viewer;
        var validIds = await _db.Users.Where(u => userIds.Contains(u.Id) && u.IsActive
                && _db.UserCompanyAccesses.Any(a => a.UserId == u.Id && a.CompanyId == map.CompanyId))
            .Select(u => u.Id).ToListAsync();
        if (validIds.Count != userIds.Count) return BadRequest(new { message = "همهٔ افراد انتخاب‌شده کاربر فعال همین شرکت نیستند." });
        var existing = await _db.StandaloneMindFlowMembers.Where(m => m.MindFlowId == id && userIds.Contains(m.UserId)).ToListAsync();
        var existingIds = existing.Select(m => m.UserId).ToHashSet();
        foreach (var member in existing) member.Role = role;
        var now = DateTime.Now;
        _db.StandaloneMindFlowMembers.AddRange(userIds.Where(uid => !existingIds.Contains(uid)).Select(uid => new StandaloneMindFlowMember
        { MindFlowId = id, UserId = uid, Role = role, AddedByUserId = MyUserId, AddedAt = now }));
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("mindflows");
        return Ok(new { count = userIds.Count, role });
    }

    [HttpDelete("{id:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        if (!await HasAsync("Members")) return Forbid();
        var result = await GetAccessibleMapAsync(id, requireOwner: true);
        if (result.Error is not null) return result.Error;
        var member = await _db.StandaloneMindFlowMembers.FirstOrDefaultAsync(m => m.MindFlowId == id && m.UserId == userId);
        if (member is null) return NotFound(new { message = "این کاربر به نگاشت مستقل دعوت نشده است." });
        _db.StandaloneMindFlowMembers.Remove(member);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("mindflows");
        return Ok();
    }

    private async Task<(StandaloneMindFlow? Map, IActionResult? Error)> GetAccessibleMapAsync(int id, bool requireEdit = false, bool requireOwner = false)
    {
        var companyId = await ActiveCompanyIdAsync();
        if (companyId is null) return (null, BadRequest(new { message = "شرکت فعال انتخاب نشده است." }));
        var map = await FindMapAsync(id, companyId.Value);
        if (map is null) return (null, NotFound(new { message = "نگاشت مستقل پیدا نشد." }));
        var role = await AccessRoleAsync(map);
        if (role is null) return (null, Forbid());
        if (requireOwner && map.OwnerUserId != MyUserId) return (null, Forbid());
        if (requireEdit && !CanEditRole(role)) return (null, Forbid());
        return (map, null);
    }

    private async Task<string?> ValidateLinksAsync(WorkProjectMindFlowDto dto, int companyId)
    {
        var nodes = WorkProjectsController.EnumerateMindFlowNodes(dto).ToList();
        var orderIds = nodes.SelectMany(WorkProjectsController.NodeWorkOrderIds).Distinct().ToList();
        if (orderIds.Count > 0)
        {
            var valid = await _db.WorkOrders.CountAsync(w => orderIds.Contains(w.Id) && w.CompanyId == companyId);
            if (valid != orderIds.Count) return "فقط دستورهای کاری همین شرکت قابل اتصال‌اند.";
        }
        var linkedProjectIds = nodes.Where(n => n.LinkedMindFlowProjectId.HasValue).Select(n => n.LinkedMindFlowProjectId!.Value).Distinct().ToList();
        if (linkedProjectIds.Count > 0)
        {
            var valid = await _db.WorkProjects.CountAsync(p => linkedProjectIds.Contains(p.Id) && p.CompanyId == companyId);
            if (valid != linkedProjectIds.Count) return "فقط نگاشت‌های پروژه‌های همین شرکت قابل پیوند هستند.";
        }
        return null;
    }

    private static WorkProjectMindFlowDto? Deserialize(string? json, string title)
    {
        try
        {
            var map = string.IsNullOrWhiteSpace(json) ? null
                : System.Text.Json.JsonSerializer.Deserialize<WorkProjectMindFlowDto>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (map is null) return null;
            map.Title = string.IsNullOrWhiteSpace(map.Title) ? title : map.Title;
            WorkProjectsController.NormalizeMindFlowMap(map, 0);
            return map;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
