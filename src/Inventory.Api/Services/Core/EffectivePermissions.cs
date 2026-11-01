using System.Security.Claims;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Core;

// =====================================================================
//  دسترسی‌های مؤثر کاربر — منبع واحد برای کنترلرهای داشبورد/گزارش‌ساز
//
// مجوزها هنگام لاگین به‌صورت claim با نوع «permission» داخل JWT
// هم قرار می‌گیرند تا کلاینت منو را نمایش دهد؛ اما کنترل سمت سرور
// همیشه نقش‌های فعالِ فعلی را از پایگاه‌داده می‌خواند. در نتیجه
// تغییر یا حذف نقش، با وجود JWT قدیمی، دسترسی اضافه ایجاد نمی‌کند.
// =====================================================================

public interface IEffectivePermissions
{
    /// <summary>مجموعهٔ «Module.Action» برای کاربر جاری.</summary>
    Task<HashSet<string>> GetAsync(ClaimsPrincipal user);

    /// <summary>مدیر قدیمی بدون نقش RBAC (سازگاری عقب‌رو).</summary>
    Task<bool> IsLegacyAdminAsync(ClaimsPrincipal user);

    /// <summary>آیا کاربر مجوز مشاهدهٔ ماژول را دارد؟ (Read/View/Access)</summary>
    Task<bool> CanSeeModuleAsync(ClaimsPrincipal user, string module);

    /// <summary>شناسهٔ نقش‌های RBAC کاربر (برای اشتراک‌گذاری گزارش).</summary>
    Task<List<int>> MyRoleIdsAsync(int userId);
}

public sealed class EffectivePermissions : IEffectivePermissions
{
    private readonly AppDbContext _db;

    public EffectivePermissions(AppDbContext db) => _db = db;

    private static int UserId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    public async Task<HashSet<string>> GetAsync(ClaimsPrincipal user)
    {
        // هرگز fallback به claimهای permission انجام نمی‌شود: آن claimها تا ۱۲ ساعت
        // معتبرند و در صورت کاهش دسترسی، می‌توانند مجوزِ لغوشده را زنده نگه دارند.
        var uid = UserId(user);
        if (uid <= 0) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var roleIds = await _db.UserRoles
            .Where(ur => ur.UserId == uid && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToListAsync();

        List<string> perms;
        if (roleIds.Count > 0)
        {
            perms = await _db.RolePermissions.Where(rp => roleIds.Contains(rp.RoleId))
                .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Module + "." + p.Action)
                .Distinct().ToListAsync();
        }
        else
        {
            // فقط کاربران واقعاً legacy (بدون نقش RBAC فعال) از نقش قدیمی‌شان
            // بازسازی می‌شوند؛ نقش نیز از DB خوانده می‌شود، نه از JWT قدیمی.
            var legacyRole = await _db.Users.AsNoTracking()
                .Where(u => u.Id == uid && u.IsActive)
                .Select(u => u.Role)
                .FirstOrDefaultAsync();
            perms = legacyRole switch
            {
                "Admin" => await _db.Permissions.Select(p => p.Module + "." + p.Action).ToListAsync(),
                // کاربران legacy غیر Admin بدون نقش RBAC، permission سراسری دریافت نمی‌کنند.
                "Operator" or "Accountant" => new List<string>(),
                _ => await _db.Permissions
                    .Where(p => p.Module == "ReferrerPanel" || p.Module == "MyCartable"
                                || p.Module == "MyArchive" || p.Module == "MyDashboards")
                    .Select(p => p.Module + "." + p.Action).ToListAsync()
            };
        }
        return new HashSet<string>(perms, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> IsLegacyAdminAsync(ClaimsPrincipal user)
    {
        var uid = UserId(user);
        if (uid <= 0) return false;
        // نقش و فعال‌بودن حساب نیز باید از DB خوانده شوند؛ claim نقش در JWT ممکن است
        // پس از ویرایش کاربر قدیمی باشد.
        var isActiveLegacyAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == uid && u.IsActive && u.Role == "Admin");
        return isActiveLegacyAdmin && !await _db.UserRoles.AnyAsync(ur => ur.UserId == uid);
    }

    public async Task<bool> CanSeeModuleAsync(ClaimsPrincipal user, string module)
    {
        if (string.IsNullOrEmpty(module)) return true;
        if (await IsLegacyAdminAsync(user)) return true;
        var perms = await GetAsync(user);
        return perms.Contains($"{module}.Read") || perms.Contains($"{module}.View")
               || perms.Contains($"{module}.Access");
    }

    public Task<List<int>> MyRoleIdsAsync(int userId) =>
        _db.UserRoles.Where(ur => ur.UserId == userId)
            .Join(_db.Roles.Where(r => r.IsActive), ur => ur.RoleId, r => r.Id, (ur, r) => ur.RoleId)
            .Distinct().ToListAsync();
}
