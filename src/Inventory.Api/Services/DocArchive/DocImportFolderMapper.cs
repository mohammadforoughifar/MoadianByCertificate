using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// نگاشت مسیر پوشهٔ فایل (داخل ریشهٔ ورود) به پوشهٔ درخت برنامه.
/// تطبیق نام با نرمال‌سازی انجام می‌شود تا «ی» و «ي» پوشهٔ دوتایی نسازند؛
/// خود برنامه فقط تطبیق دقیق نام را بررسی می‌کند.
/// </summary>
public sealed class DocImportFolderMapper
{
    private readonly AppDbContext _db;
    private readonly Dictionary<string, int?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _createdPaths = new();
    private readonly HashSet<string> _wouldCreate = new(StringComparer.OrdinalIgnoreCase);

    public DocImportFolderMapper(AppDbContext db) => _db = db;

    /// <summary>مسیر پوشه‌هایی که در این اجرا ساخته شدند.</summary>
    public IReadOnlyList<string> CreatedPaths => _createdPaths;

    /// <summary>تعداد پوشه‌هایی که در حالت پیش‌نمایش ساخته خواهند شد.</summary>
    public int WouldCreateCount => _wouldCreate.Count;

    public static string NormalizeName(string name) => DocImportNameParser.NormalizeTitle(name);

    /// <summary>تجزیهٔ مسیر نسبی به بخش‌ها (جداکنندهٔ «/» یا «\»).</summary>
    public static string[] SplitPath(string relativeFolder)
        => (relativeFolder ?? "")
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// پیدا کردن پوشهٔ مقصد.
    /// در حالت پیش‌نمایش (dryRun) هیچ پوشه‌ای ساخته نمی‌شود و فقط شمرده می‌شود.
    /// </summary>
    public async Task<(int? FolderId, string? ErrorCode, bool Created)> ResolveAsync(
        string relativeFolder, int? rootFolderId, bool createMissing, bool dryRun, int userId, string userName)
    {
        var segments = SplitPath(relativeFolder);
        if (segments.Length == 0) return (rootFolderId, null, false);

        var scope = rootFolderId?.ToString() ?? "root";
        var parentId = rootFolderId;
        var walked = "";
        var createdAny = false;

        foreach (var segment in segments)
        {
            walked = walked.Length == 0 ? segment : $"{walked}/{segment}";

            // سقف DocFolder.Name برابر ۲۰۰ کاراکتر است
            if (segment.Length > 200) return (null, DocImportErrorCodes.FolderNameLong, false);

            var cacheKey = $"{scope}|{walked}";
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.HasValue)
            {
                parentId = cached;
                continue;
            }

            var normalized = NormalizeName(segment);
            var siblings = await _db.DocFolders.AsNoTracking()
                .Where(f => f.IsActive && f.ParentId == parentId)
                .Select(f => new { f.Id, f.Name })
                .ToListAsync();

            // اول تطبیق دقیق (همان قاعدهٔ خود برنامه)، بعد تطبیق نرمال‌شده
            var match = siblings.FirstOrDefault(s => s.Name == segment)
                        ?? siblings.FirstOrDefault(s => NormalizeName(s.Name) == normalized);

            if (match != null)
            {
                parentId = match.Id;
                _cache[cacheKey] = match.Id;
                continue;
            }

            if (!createMissing) return (null, DocImportErrorCodes.FolderNotFound, false);

            if (dryRun)
            {
                _wouldCreate.Add($"{scope}|{walked}");
                // در پیش‌نمایش شناسه‌ای وجود ندارد؛ بقیهٔ مسیر هم جدید شمرده می‌شود
                parentId = null;
                _cache[cacheKey] = null;
                continue;
            }

            var createdId = await CreateFolderAsync(parentId, segment, userId, userName);
            parentId = createdId;
            _cache[cacheKey] = createdId;
            _createdPaths.Add(walked);
            createdAny = true;
        }

        return (parentId, null, createdAny);
    }

    /// <summary>
    /// ساخت پوشه با همان قواعد DocFoldersController: سازنده دسترسی کامل می‌گیرد و پوشه عمومی نیست.
    /// </summary>
    private async Task<int> CreateFolderAsync(int? parentId, string name, int userId, string userName)
    {
        var folder = new DocFolder
        {
            ParentId = parentId,
            Name = name.Trim(),
            IsPublic = false,
            PublicCanDownload = false,
            CreatedByUserId = userId,
            CreatedByName = userName
        };
        _db.DocFolders.Add(folder);
        await _db.SaveChangesAsync();

        _db.DocFolderPermissions.Add(new DocFolderPermission
        {
            FolderId = folder.Id,
            UserId = userId,
            RoleId = 0,
            Level = DocAccessLevel.Full,
            CanDownload = true
        });
        await _db.SaveChangesAsync();
        return folder.Id;
    }
}
