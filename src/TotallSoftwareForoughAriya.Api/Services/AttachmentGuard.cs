using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>سطح مجاز کاربر روی یک پیوست</summary>
public enum AttachmentAccess
{
    /// <summary>هیچ دسترسی‌ای ندارد</summary>
    None = 0,
    /// <summary>فقط می‌تواند در برنامه ببیند (پیش‌نمایش) ولی حق دانلود ندارد</summary>
    PreviewOnly = 1,
    /// <summary>هم پیش‌نمایش هم دانلود</summary>
    Download = 2
}

/// <summary>
/// نگهبان دسترسی پیوست‌ها.
/// پیوست‌ها با (Module + RefId) ذخیره می‌شوند و تا پیش از این هیچ کنترل دسترسی‌ای نداشتند.
/// این سرویس بر اساس ماژولِ صاحبِ پیوست تصمیم می‌گیرد کاربر چه سطحی دارد.
/// ماژول‌هایی که قانون اختصاصی ندارند، رفتار قبلی (باز برای کاربران واردشده) را حفظ می‌کنند
/// تا این تغییر امنیتی، فرم‌های موجود را از کار نیندازد.
/// </summary>
public interface IAttachmentGuard
{
    Task<AttachmentAccess> CheckAsync(string module, int refId, int userId, bool isAdmin);

    /// <summary>آیا این ماژول قانون دسترسی اختصاصی دارد؟</summary>
    bool IsProtected(string module);
}

public class AttachmentGuard : IAttachmentGuard
{
    private readonly AppDbContext _db;
    private readonly IDocAccessService _docAccess;

    public AttachmentGuard(AppDbContext db, IDocAccessService docAccess)
    {
        _db = db; _docAccess = docAccess;
    }

    /// <summary>ماژول‌هایی که کنترل دسترسی اختصاصی دارند.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "DocVersion",
        "HrEmployee",
        "WorkOrders"
    };

    public bool IsProtected(string module) => Protected.Contains(module);

    public async Task<AttachmentAccess> CheckAsync(string module, int refId, int userId, bool isAdmin)
    {
        if (userId <= 0) return AttachmentAccess.None;

        // Deleted work orders must not expose or accept attachments via the shared endpoint.
        if (string.Equals(module, "WorkOrders", StringComparison.OrdinalIgnoreCase))
        {
            var order = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == refId);
            if (order == null) return AttachmentAccess.None;
            if (order.OwnerUserId == userId || await _db.WorkOrderAssignees.AnyAsync(a => a.OrderId == refId && a.UserId == userId))
                return AttachmentAccess.Download;
            var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == userId);
            if (!hasRoles && isAdmin) return AttachmentAccess.Download;
            var canView = await _db.UserRoles.Where(ur => ur.UserId == userId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
                .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
                .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
                .AnyAsync(p => p.Module == "WorkOrders" && p.Action == "View");
            return canView ? AttachmentAccess.Download : AttachmentAccess.None;
        }

        // ---------- پیوست ورژن مدرک در آرشیو اسناد ----------
        if (string.Equals(module, "DocVersion", StringComparison.OrdinalIgnoreCase))
        {
            var ver = await _db.DocumentVersions.AsNoTracking()
                .Where(v => v.Id == refId)
                .Select(v => new { v.DocumentId })
                .FirstOrDefaultAsync();
            if (ver == null) return AttachmentAccess.None;

            var doc = await _db.Documents.AsNoTracking()
                .Where(d => d.Id == ver.DocumentId)
                .Select(d => new { d.IsDeleted })
                .FirstOrDefaultAsync();
            if (doc == null || doc.IsDeleted) return AttachmentAccess.None;

            var manager = await DocArchiveAuthorization.IsManagerAsync(_db, userId, isAdmin);
            var (level, canDownload) = await _docAccess.DocumentAccessAsync(userId, manager, ver.DocumentId);

            // View فقط سرفصل است؛ حتی لینک مستقیم نباید محتوا را باز کند.
            // Read فقط پیش‌نمایش می‌دهد؛ دانلود یک مجوز مستقل است.
            if (level < DocAccessLevel.Read) return AttachmentAccess.None;
            return canDownload ? AttachmentAccess.Download : AttachmentAccess.PreviewOnly;
        }

        // ---------- سایر ماژول‌ها ----------
        // رفتار قبلی حفظ می‌شود: هر کاربر واردشده دسترسی دارد.
        // برای محدودتر کردن، ماژول را به Protected اضافه و قانونش را اینجا بنویسید.
        if (string.Equals(module, "HrEmployee", StringComparison.OrdinalIgnoreCase))
        {
            if (isAdmin) return AttachmentAccess.Download;
            var exists = await _db.HrEmployees.AsNoTracking().AnyAsync(e => e.Id == refId);
            if (!exists) return AttachmentAccess.None;
            var allowed = await _db.UserRoles.Where(ur => ur.UserId == userId)
                .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
                .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
                .AnyAsync(p => p.Module == "HrCore" && (p.Action == "Read" || p.Action == "Manage"));
            return allowed ? AttachmentAccess.Download : AttachmentAccess.None;
        }

        return AttachmentAccess.Download;
    }
}
