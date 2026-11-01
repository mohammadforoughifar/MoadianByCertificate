using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// مدیر آرشیو فقط دارنده واقعی DocArchive.Manage در نقش فعال است.
/// نام نقش/claim قدیمی Admin برای کاربر دارای RBAC، جایگزین این مجوز نمی‌شود.
/// حتی نقش RBAC غیرفعال هم نباید باعث بازگشت به دسترسی Admin قدیمی شود.
/// </summary>
public static class DocArchiveAuthorization
{
    public static async Task<bool> IsManagerAsync(AppDbContext db, int userId, bool isLegacyAdmin)
    {
        if (userId <= 0) return false;
        if (!await db.UserRoles.AnyAsync(ur => ur.UserId == userId)) return isLegacyAdmin;
        return await db.UserRoles
            .Where(ur => ur.UserId == userId && db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == "DocArchive" && p.Action == "Manage");
    }
}
