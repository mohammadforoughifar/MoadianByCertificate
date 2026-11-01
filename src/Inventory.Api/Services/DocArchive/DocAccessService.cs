using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// دسترسی اختصاصی، محدودکننده است و با مجوز قوی‌تر گروه/والد باز نمی‌شود.
/// ترتیب: مدیر آرشیو؛ فرد روی مدرک؛ دسترسی موقت فرد؛ نزدیک‌ترین مجوز فردی پوشه؛
/// گروه روی مدرک؛ نزدیک‌ترین مجوز گروهی پوشه؛ سازنده؛ نمایش عمومی.
/// دانلود فقط از همان منبع برنده گرفته می‌شود و از منابع کنارگذاشته‌شده ارث نمی‌رسد.
/// </summary>
public interface IDocAccessService
{
    Task<Dictionary<int, (DocAccessLevel Level, bool Download)>> FolderAccessMapAsync(int userId, bool isManager);
    Task<(DocAccessLevel Level, bool Download)> FolderAccessAsync(int userId, bool isManager, int folderId);
    Task<(DocAccessLevel Level, bool Download)> DocumentAccessAsync(int userId, bool isManager, int documentId);

    /// <summary>محاسبه دسته‌ای برای صفحه فعلی؛ همان قواعد جزئیات و APIهای تغییر/دانلود.</summary>
    Task<Dictionary<int, (DocAccessLevel Level, bool Download)>> DocumentAccessMapAsync(
        int userId, bool isManager, IEnumerable<int> documentIds);

    /// <summary>اعمال همان اولویت‌ها در SQL، پیش از شمارش و صفحه‌بندی.</summary>
    Task<IQueryable<ArchiveDocument>> AccessibleDocumentsAsync(
        int userId, bool isManager, DocAccessLevel minimum = DocAccessLevel.View);

    Task<List<int>> UsersWithFullAccessAsync(int documentId);
}

public class DocAccessService : IDocAccessService
{
    private readonly AppDbContext _db;
    public DocAccessService(AppDbContext db) => _db = db;

    private readonly record struct Grant(DocAccessLevel Level, bool Download);
    private sealed record FolderState(Grant? Personal, Grant? Group, Grant? Public)
    {
        public Grant? Assigned => Personal ?? Group;
        public Grant Effective => Assigned ?? Public ?? new Grant(DocAccessLevel.None, false);
    }
    private static readonly FolderState Empty = new(null, null, null);

    private Task<List<int>> ActiveRoleIdsAsync(int userId) => _db.UserRoles.AsNoTracking()
        .Where(ur => ur.UserId == userId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
        .Select(ur => ur.RoleId).Distinct().ToListAsync();

    // اجتماع فقط بین ردیف‌های هم‌اولویت (مثلاً چند نقش روی همان پوشه) انجام می‌شود.
    // مقدار نامعتبر و سطح View/None هرگز اجازه دریافت فایل ایجاد نمی‌کنند.
    private static Grant Combine(IEnumerable<(DocAccessLevel Level, bool Download)> rows)
    {
        var level = DocAccessLevel.None;
        var download = false;
        foreach (var row in rows)
        {
            var valid = row.Level >= DocAccessLevel.None && row.Level <= DocAccessLevel.Full;
            var current = valid ? row.Level : DocAccessLevel.None;
            if (current > level) level = current;
            download |= current >= DocAccessLevel.Read && row.Download;
        }
        return new Grant(level, level >= DocAccessLevel.Read && download);
    }

    private async Task<Dictionary<int, FolderState>> FolderStatesAsync(int userId, List<int> roleIds)
    {
        var folders = await _db.DocFolders.AsNoTracking()
            .Select(f => new { f.Id, f.ParentId, f.IsPublic, f.PublicCanDownload }).ToListAsync();
        var permissions = await _db.DocFolderPermissions.AsNoTracking()
            .Where(p => (p.RoleId == 0 && p.UserId == userId)
                     || (p.RoleId > 0 && roleIds.Contains(p.RoleId))).ToListAsync();
        var personal = permissions.Where(p => p.RoleId == 0).GroupBy(p => p.FolderId)
            .ToDictionary(g => g.Key, g => Combine(g.Select(p => (p.Level, p.CanDownload))));
        var groups = permissions.Where(p => p.RoleId > 0).GroupBy(p => p.FolderId)
            .ToDictionary(g => g.Key, g => Combine(g.Select(p => (p.Level, p.CanDownload))));
        var byId = folders.ToDictionary(f => f.Id);
        var states = new Dictionary<int, FolderState>();

        FolderState Resolve(int id, HashSet<int> visiting)
        {
            if (states.TryGetValue(id, out var cached)) return cached;
            if (!byId.TryGetValue(id, out var folder)) return Empty;
            if (!visiting.Add(id))
                return new FolderState(new Grant(DocAccessLevel.None, false), null, null); // چرخه: fail closed

            var parent = folder.ParentId is int pid ? Resolve(pid, visiting) : Empty;
            Grant? individual = personal.TryGetValue(id, out var grant) ? grant : parent.Personal;
            Grant? group = groups.TryGetValue(id, out var roleGrant) ? roleGrant : parent.Group;
            Grant? publicGrant = folder.IsPublic
                ? new Grant(DocAccessLevel.Read, folder.PublicCanDownload) : parent.Public;
            var result = new FolderState(individual, group, publicGrant);
            states[id] = result;
            visiting.Remove(id);
            return result;
        }
        foreach (var folder in folders) Resolve(folder.Id, new HashSet<int>());
        return states;
    }

    public async Task<Dictionary<int, (DocAccessLevel Level, bool Download)>> FolderAccessMapAsync(int userId, bool isManager)
    {
        if (userId <= 0) return new();
        if (isManager)
            return (await _db.DocFolders.AsNoTracking().Select(f => f.Id).ToListAsync())
                .ToDictionary(id => id, _ => (DocAccessLevel.Full, true));
        var states = await FolderStatesAsync(userId, await ActiveRoleIdsAsync(userId));
        return states.ToDictionary(x => x.Key, x => (x.Value.Effective.Level, x.Value.Effective.Download));
    }

    public async Task<(DocAccessLevel Level, bool Download)> FolderAccessAsync(int userId, bool isManager, int folderId)
    {
        var map = await FolderAccessMapAsync(userId, isManager);
        return map.TryGetValue(folderId, out var grant) ? grant : (DocAccessLevel.None, false);
    }

    public async Task<(DocAccessLevel Level, bool Download)> DocumentAccessAsync(int userId, bool isManager, int documentId)
    {
        var map = await DocumentAccessMapAsync(userId, isManager, new[] { documentId });
        return map.TryGetValue(documentId, out var grant) ? grant : (DocAccessLevel.None, false);
    }

    public async Task<Dictionary<int, (DocAccessLevel Level, bool Download)>> DocumentAccessMapAsync(
        int userId, bool isManager, IEnumerable<int> documentIds)
    {
        var result = new Dictionary<int, (DocAccessLevel Level, bool Download)>();
        var ids = documentIds.Where(id => id > 0).Distinct().ToArray();
        if (userId <= 0 || ids.Length == 0) return result;
        var docs = await _db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id))
            .Select(d => new { d.Id, d.FolderId, d.CreatedByUserId, d.IsPublic, d.PublicCanDownload }).ToListAsync();
        if (isManager) return docs.ToDictionary(d => d.Id, _ => (DocAccessLevel.Full, true));

        var roles = await ActiveRoleIdsAsync(userId);
        var folders = await FolderStatesAsync(userId, roles);
        var permissions = await _db.DocumentPermissions.AsNoTracking()
            .Where(p => ids.Contains(p.DocumentId) && ((p.RoleId == 0 && p.UserId == userId)
                        || (p.RoleId > 0 && roles.Contains(p.RoleId)))).ToListAsync();
        var byDocument = permissions.ToLookup(p => p.DocumentId);
        var now = DateTime.UtcNow;
        var temporary = (await _db.DocTemporaryGrants.AsNoTracking()
            .Where(g => ids.Contains(g.DocumentId) && g.UserId == userId
                     && g.RevokedAtUtc == null && g.ExpiresAtUtc > now).ToListAsync())
            .ToLookup(g => g.DocumentId);

        foreach (var doc in docs)
        {
            var rows = byDocument[doc.Id].ToList();
            var personal = rows.Where(p => p.RoleId == 0 && p.UserId == userId).ToList();
            var groups = rows.Where(p => p.RoleId > 0).ToList();
            var temp = temporary[doc.Id].ToList();
            var folder = folders.TryGetValue(doc.FolderId, out var state) ? state : Empty;
            Grant grant;
            if (personal.Count > 0)
                grant = Combine(personal.Select(p => (p.Level, p.CanDownload)));
            else if (temp.Count > 0)
                grant = Combine(temp.Select(g => (DocAccessLevel.Read, g.CanDownload)));
            else if (folder.Personal is Grant individualFolder)
                grant = individualFolder;
            else if (groups.Count > 0)
                grant = Combine(groups.Select(p => (p.Level, p.CanDownload)));
            else if (folder.Group is Grant inherited)
                grant = inherited;
            else if (doc.CreatedByUserId == userId)
                grant = new Grant(DocAccessLevel.Full, true);
            else if (doc.IsPublic)
                grant = new Grant(DocAccessLevel.Read, doc.PublicCanDownload);
            else
                grant = folder.Public ?? new Grant(DocAccessLevel.None, false);
            result[doc.Id] = (grant.Level, grant.Download);
        }
        return result;
    }

    public async Task<IQueryable<ArchiveDocument>> AccessibleDocumentsAsync(
        int userId, bool isManager, DocAccessLevel minimum = DocAccessLevel.View)
    {
        var q = _db.Documents.AsNoTracking();
        if (userId <= 0) return q.Where(_ => false);
        if (minimum < DocAccessLevel.View || minimum > DocAccessLevel.Full)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        if (isManager) return q;

        var roles = await ActiveRoleIdsAsync(userId);
        var folders = await FolderStatesAsync(userId, roles);
        var personalFolders = folders.Where(x => x.Value.Personal.HasValue).Select(x => x.Key).ToArray();
        var allowedPersonalFolders = folders.Where(x => x.Value.Personal is Grant g && g.Level >= minimum).Select(x => x.Key).ToArray();
        var groupFolders = folders.Where(x => x.Value.Group.HasValue).Select(x => x.Key).ToArray();
        var allowedGroupFolders = folders.Where(x => x.Value.Group is Grant g && g.Level >= minimum).Select(x => x.Key).ToArray();
        var publicFolders = folders.Where(x => x.Value.Public is Grant g && g.Level >= minimum).Select(x => x.Key).ToArray();
        var personal = _db.DocumentPermissions.Where(p => p.RoleId == 0 && p.UserId == userId);
        var group = _db.DocumentPermissions.Where(p => p.RoleId > 0 && roles.Contains(p.RoleId));
        var now = DateTime.UtcNow;
        var temporary = _db.DocTemporaryGrants.Where(g => g.UserId == userId && g.RevokedAtUtc == null && g.ExpiresAtUtc > now);
        var allowsPublicRead = minimum <= DocAccessLevel.Read;

        // NOT EXISTS در هر شاخه مانع می‌شود یک grant اختصاصیِ محدودتر با OR بعدی دور زده شود.
        // هیچ لیست مدارکی در حافظه بارگذاری نمی‌شود؛ Count/Skip/Take همچنان در SQL اجرا می‌شوند.
        return q.Where(d =>
            personal.Any(p => p.DocumentId == d.Id && p.Level >= minimum && p.Level <= DocAccessLevel.Full)
            || (!personal.Any(p => p.DocumentId == d.Id) && (
                (allowsPublicRead && temporary.Any(g => g.DocumentId == d.Id))
                || (!temporary.Any(g => g.DocumentId == d.Id) && (
                    allowedPersonalFolders.Contains(d.FolderId)
                    || (!personalFolders.Contains(d.FolderId) && (
                        group.Any(p => p.DocumentId == d.Id && p.Level >= minimum && p.Level <= DocAccessLevel.Full)
                        || (!group.Any(p => p.DocumentId == d.Id) && (
                            allowedGroupFolders.Contains(d.FolderId)
                            || (!groupFolders.Contains(d.FolderId) && (
                                d.CreatedByUserId == userId
                                || (allowsPublicRead && d.IsPublic)
                                || publicFolders.Contains(d.FolderId))))))))))));
    }

    public async Task<List<int>> UsersWithFullAccessAsync(int documentId)
    {
        var doc = await _db.Documents.AsNoTracking().Where(d => d.Id == documentId)
            .Select(d => new { d.FolderId, d.CreatedByUserId }).FirstOrDefaultAsync();
        if (doc == null) return new();
        var ids = new HashSet<int> { doc.CreatedByUserId };
        var roleIds = new HashSet<int>();
        var direct = await _db.DocumentPermissions.AsNoTracking()
            .Where(p => p.DocumentId == documentId && p.Level == DocAccessLevel.Full)
            .Select(p => new { p.UserId, p.RoleId }).ToListAsync();
        foreach (var row in direct)
        {
            if (row.RoleId == 0 && row.UserId > 0) ids.Add(row.UserId);
            if (row.RoleId > 0) roleIds.Add(row.RoleId);
        }
        var parents = await _db.DocFolders.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.ParentId);
        var chain = new HashSet<int>();
        int? current = doc.FolderId;
        while (current is int id && chain.Add(id))
            current = parents.TryGetValue(id, out var parent) ? parent : null;
        var inherited = await _db.DocFolderPermissions.AsNoTracking()
            .Where(p => chain.Contains(p.FolderId) && p.Level == DocAccessLevel.Full)
            .Select(p => new { p.UserId, p.RoleId }).ToListAsync();
        foreach (var row in inherited)
        {
            if (row.RoleId == 0 && row.UserId > 0) ids.Add(row.UserId);
            if (row.RoleId > 0) roleIds.Add(row.RoleId);
        }
        if (roleIds.Count > 0)
        {
            var members = await _db.UserRoles.AsNoTracking()
                .Where(ur => roleIds.Contains(ur.RoleId) && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
                .Select(ur => ur.UserId).ToListAsync();
            foreach (var id in members) ids.Add(id);
        }
        // اجتماع خامِ Full کافی نیست: کاربر ممکن است روی خود مدرک/زیرپوشه Read گرفته باشد.
        var users = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.Role }).ToListAsync();
        var result = new List<int>();
        foreach (var user in users)
        {
            var manager = await DocArchiveAuthorization.IsManagerAsync(_db, user.Id, user.Role == "Admin");
            if ((await DocumentAccessAsync(user.Id, manager, documentId)).Level >= DocAccessLevel.Full)
                result.Add(user.Id);
        }
        return result;
    }
}
