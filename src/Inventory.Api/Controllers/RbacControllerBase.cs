using System.Security.Claims;
using Inventory.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// کنترلر پایه برای ماژول‌هایی که چک دسترسی دقیق (ماژول.اکشن) لازم دارند —
/// مثل WorkOrdersController: اگر کاربر نقش RBAC دارد از جداول RBAC چک می‌شود،
/// وگرنه نقش قدیمی (Admin=همه‌چیز، Operator=خواندنی/ایجاد) فال‌بک است.
/// </summary>
public abstract class RbacControllerBase : ApiControllerBase
{
    protected readonly AppDbContext Db;

    protected RbacControllerBase(AppDbContext db) => Db = db;

    protected int MyUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    protected string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";

    /// <summary>آیا کاربر جاری این مجوز را دارد؟ (مثال: HasAsync("Projects", "Create"))</summary>
    protected async Task<bool> HasAsync(string module, string action)
    {
        if (module == "DocArchive" && action == "Manage")
            return await Services.DocArchive.DocArchiveAuthorization.IsManagerAsync(Db, MyUserId, User.IsInRole("Admin"));
        var hasRoles = await Db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId && Db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            // کاربران legacy غیر Admin بدون نقش RBAC، دسترسی سراسری دریافت نمی‌کنند؛
            // اعطای permission باید از طریق Role و به‌صورت module-scoped انجام شود.
            return false;
        }
        return await Db.UserRoles.Where(ur => ur.UserId == MyUserId && Db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(Db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(Db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == module && p.Action == action);
    }

    /// <summary>اگر مجوز را نداشت 403 می‌دهد.</summary>
    protected async Task<IActionResult?> ForbiddenUnlessAsync(string module, string action)
        => await HasAsync(module, action) ? null
           : StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });

    /// <summary>آیا کاربر جاری حداقل یکی از این اکشن‌های ماژول را دارد؟ (دسترسی تفکیکی هر لینک منو)</summary>
    protected async Task<bool> HasAnyAsync(string module, params string[] actions)
    {
        var hasRoles = await Db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId && Db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            return false;
        }
        return await Db.UserRoles.Where(ur => ur.UserId == MyUserId && Db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(Db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(Db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == module && actions.Contains(p.Action));
    }

    /// <summary>اگر هیچ‌کدام از مجوزها را نداشت 403 می‌دهد.</summary>
    protected async Task<IActionResult?> ForbiddenUnlessAnyAsync(string module, params string[] actions)
        => await HasAnyAsync(module, actions) ? null
           : StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });

    /// <summary>
    /// gate مخصوص «آرشیو اسناد و مدارک»: دسترسی این ماژول به‌ازای هر پوشه و هر مدرک
    /// جداگانه تعریف می‌شود، پس مجوز ماژول در «تنظیمات ← نقش‌ها و دسترسی‌ها» تنها یک
    /// دسترسی سراسری است. کاربری که آن مجوز را ندارد اما روی حداقل یک پوشه/مدرک به او
    /// دسترسی داده شده، باید بتواند وارد آرشیو شود و فقط همان موارد را ببیند.
    /// سطح واقعی دسترسی هر آیتم داخل خود endpoint (با DocAccessService) چک می‌شود؛
    /// برای کارهای مدیریتی سراسری همان <see cref="ForbiddenUnlessAsync"/> با action=Manage
    /// استفاده شود.
    /// </summary>
    protected async Task<IActionResult?> ForbiddenUnlessDocArchiveAsync(string module, string action)
    {
        if (await HasAsync(module, action)) return null;
        if (await Services.DocArchive.DocArchiveAccessProbe.AnyAsync(Db, MyUserId)) return null;
        return StatusCode(403, new { message = "شما به آرشیو اسناد و مدارک دسترسی ندارید." });
    }

    /// <summary>
    /// شناسهٔ کاربران دارای یک مجوز خاص (مثل ProjectCartable.Manager) — برای ارسال اعلان به گروه مجاز.
    /// کاربران ادمین قدیمی (بدون نقش RBAC) هم لحاظ می‌شوند. خود کاربرِ رخداددهنده حذف می‌شود تا به خودش اعلان نزند.
    /// </summary>
    /// <param name="excludeSelf">اگر true باشد کاربر جاری از نتیجه حذف می‌شود</param>
    protected async Task<List<int>> UsersWithPermissionAsync(string module, string action, bool excludeSelf = false)
    {
        var rbac = await Db.UserRoles
            .Where(ur => Db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(Db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(Db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == module && x.Action == action)
            .Select(x => x.UserId).Distinct().ToListAsync();
        // ادمین‌های قدیمی بدون نقش RBAC — به همهٔ مجوزها دسترسی دارند
        var legacyAdmins = await Db.Users
            .Where(u => u.Role == "Admin" && u.IsActive && !Db.UserRoles.Any(ur => ur.UserId == u.Id))
            .Select(u => u.Id).ToListAsync();
        var ids = rbac.Concat(legacyAdmins).Distinct();
        if (excludeSelf) ids = ids.Where(id => id != MyUserId);
        return ids.ToList();
    }
}
