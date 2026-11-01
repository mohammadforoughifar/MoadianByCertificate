using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// ================== «کاربران مجاز» منابع انسانی فروغ آریا (برنامهٔ پیشین) ==================
/// <para>
/// منبع واحدِ فیلتر همهٔ کمبوها، فهرست‌ها، گزارش‌ها و محاسبات این ماژول.
/// کاربر فقط وقتی دیده می‌شود که <b>فعال</b> باشد <b>و</b> به این ماژول <b>دسترسی</b> داشته باشد
/// (قاعده در <see cref="ForoughAryaHrAccess"/>). این دو شرط با هم لازم‌اند؛ یکی به‌تنهایی کافی نیست.
/// </para>
/// <para>
/// دسترسی همیشه از پایگاه‌داده خوانده می‌شود (نه از claimهای JWT)، پس با غیرفعال‌کردن کاربر،
/// غیرفعال‌کردن نقش یا برداشتن تیک مجوز، کاربر بلافاصله از همهٔ فهرست‌ها حذف می‌شود.
/// </para>
/// Scoped است و نتیجه را در طول یک درخواست کش می‌کند.
/// </summary>
public sealed class ForoughAryaHrUsers
{
    private readonly AppDbContext _db;
    private HashSet<int>? _userIds;
    private Dictionary<int, int>? _personnelToUser;

    public ForoughAryaHrUsers(AppDbContext db) => _db = db;

    /// <summary>شناسهٔ کاربرانِ فعالِ دارای دسترسی به ماژول.</summary>
    public async Task<HashSet<int>> UserIdsAsync()
    {
        if (_userIds != null) return _userIds;

        var keys = new HashSet<string>(ForoughAryaHrAccess.Permissions, StringComparer.OrdinalIgnoreCase);
        var perms = await _db.Permissions.AsNoTracking()
            .Select(p => new { p.Id, p.Module, p.Action }).ToListAsync();
        var permIds = perms.Where(p => keys.Contains(p.Module + "." + p.Action)).Select(p => p.Id).ToHashSet();

        var activeRoles = (await _db.Roles.AsNoTracking().Where(r => r.IsActive).Select(r => r.Id).ToListAsync()).ToHashSet();
        var grantingRoles = (await _db.RolePermissions.AsNoTracking()
                .Select(rp => new { rp.RoleId, rp.PermissionId }).ToListAsync())
            .Where(rp => permIds.Contains(rp.PermissionId) && activeRoles.Contains(rp.RoleId))
            .Select(rp => rp.RoleId).ToHashSet();
        var rolesByUser = (await _db.UserRoles.AsNoTracking().Select(ur => new { ur.UserId, ur.RoleId }).ToListAsync())
            .Where(ur => activeRoles.Contains(ur.RoleId))
            .GroupBy(ur => ur.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleId).ToList());

        var legacy = new HashSet<string>(ForoughAryaHrAccess.LegacyRoles, StringComparer.OrdinalIgnoreCase);
        var users = await _db.Users.AsNoTracking().Where(u => u.IsActive)
            .Select(u => new { u.Id, u.Role }).ToListAsync();

        var result = new HashSet<int>();
        foreach (var u in users)
        {
            if (rolesByUser.TryGetValue(u.Id, out var roles))
            {
                // کاربر دارای نقش RBAC فعال: فقط مجوزهای همان نقش‌ها ملاک است
                if (roles.Any(grantingRoles.Contains)) result.Add(u.Id);
            }
            else if (!string.IsNullOrEmpty(u.Role) && legacy.Contains(u.Role))
            {
                // کاربر قدیمیِ بدون نقش RBAC فعال (سازگاری عقب‌رو)
                result.Add(u.Id);
            }
        }
        return _userIds = result;
    }

    public async Task<bool> IsAllowedUserAsync(int userId)
        => userId > 0 && (await UserIdsAsync()).Contains(userId);

    /// <summary>نگاشت «پرسنل ← کاربر» فقط برای پرسنلی که به یک کاربر مجاز پیوند دارند.</summary>
    public async Task<Dictionary<int, int>> PersonnelUserMapAsync()
    {
        if (_personnelToUser != null) return _personnelToUser;
        var allowed = await UserIdsAsync();
        var links = await _db.HrUserLinks.AsNoTracking().Select(l => new { l.EmployeeId, l.UserId }).ToListAsync();
        var map = new Dictionary<int, int>();
        foreach (var l in links.Where(l => allowed.Contains(l.UserId)))
            map.TryAdd(l.EmployeeId, l.UserId);
        return _personnelToUser = map;
    }

    /// <summary>شناسهٔ پرسنل (FaHrPersonnel) که به یک کاربرِ فعالِ دارای دسترسی پیوند دارند.</summary>
    public async Task<HashSet<int>> PersonnelIdsAsync() => (await PersonnelUserMapAsync()).Keys.ToHashSet();

    public async Task<bool> IsAllowedPersonnelAsync(int personnelId)
        => personnelId > 0 && (await PersonnelUserMapAsync()).ContainsKey(personnelId);

    /// <summary>پس از تغییر پیوند پرسنل/کاربر در همین درخواست، کش نگاشت باطل شود.</summary>
    public void InvalidatePersonnel() => _personnelToUser = null;
}
