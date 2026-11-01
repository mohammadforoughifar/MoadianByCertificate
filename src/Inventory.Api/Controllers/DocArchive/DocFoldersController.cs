using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== آرشیو اسناد — پوشه‌ها ==================
/// درخت پوشه مثل بایگانی + اختصاص نفرات و سطح دسترسی به هر پوشه.
/// </summary>
[Route("api/doc-archive/folders")]
public class DocFoldersController : RbacControllerBase
{
    private readonly IDocAccessService _access;
    private readonly IDocFolderZipService _zipService;

    public DocFoldersController(AppDbContext db, IDocAccessService access, IDocFolderZipService zipService) : base(db)
    {
        _access = access;
        _zipService = zipService;
    }

    private const string Mod = "DocArchive";
    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    /// <summary>درخت پوشه‌ها — فقط پوشه‌هایی که کاربر حداقل «مشاهده» دارد.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var map = await _access.FolderAccessMapAsync(MyUserId, manager);

        var counts = await Db.Documents.Where(d => d.IsActive)
            .GroupBy(d => d.FolderId)
            .Select(g => new { FolderId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.C);

        var visibleIds = map.Where(x => x.Value.Level > DocAccessLevel.None).Select(x => x.Key).ToList();
        var rows = await Db.DocFolders.AsNoTracking()
            .Where(x => x.IsActive && visibleIds.Contains(x.Id))
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .ToPageListAsync(pagination);

        var result = rows
            .Select(x => new DocFolderDto
            {
                Id = x.Id,
                ParentId = x.ParentId,
                Name = x.Name,
                Description = x.Description,
                IsPublic = x.IsPublic,
                PublicCanDownload = x.PublicCanDownload,
                CreatedByName = x.CreatedByName,
                CreatedAt = x.CreatedAt,
                IsActive = x.IsActive,
                DocumentCount = counts.TryGetValue(x.Id, out var c) ? c : 0,
                MyLevel = (DocAccessLevelDto)(int)map[x.Id].Level,
                MyCanDownload = map[x.Id].Download
            }).ToList();

        return Ok(pagination.Result(result));
    }

    /// <summary>یک پوشه با فهرست دسترسی‌ها (برای فرم مدیریت دسترسی).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var x = await Db.DocFolders.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
        if (x == null) return NotFound(new { message = "پوشه یافت نشد." });

        var manager = await IsManagerAsync();
        var (level, dl) = await _access.FolderAccessAsync(MyUserId, manager, id);
        if (level == DocAccessLevel.None) return StatusCode(403, new { message = "به این پوشه دسترسی ندارید." });

        // فهرست «چه کسی به این پوشه دسترسی دارد» فقط برای دارندگان «دسترسی کامل» روی پوشه
        // (یا مدیر آرشیو) برمی‌گردد؛ بقیه حتی با صدا زدن مستقیم API هم آن را نمی‌بینند.
        var perms = new List<DocPermissionDto>();
        if (level >= DocAccessLevel.Full)
        {
            perms = await Db.DocFolderPermissions.AsNoTracking().Where(p => p.FolderId == id)
                .Join(Db.Users, p => p.UserId, u => u.Id, (p, u) => new DocPermissionDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    RoleId = p.RoleId,
                    UserName = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : (u.FirstName + " " + u.LastName).Trim(),
                    Level = (DocAccessLevelDto)(int)p.Level,
                    CanDownload = p.CanDownload
                }).ToListAsync();

            // سطرهای گروهی (UserId=0 — دسترسی نقش‌محور)
            var roleRows = await Db.DocFolderPermissions.AsNoTracking()
                .Where(p => p.FolderId == id && p.UserId == 0)
                .Join(Db.Roles, p => p.RoleId, r => r.Id, (p, r) => new DocPermissionDto
                {
                    Id = p.Id,
                    UserId = 0,
                    RoleId = p.RoleId,
                    RoleName = r.Name,
                    Level = (DocAccessLevelDto)(int)p.Level,
                    CanDownload = p.CanDownload
                }).ToListAsync();
            perms.AddRange(roleRows);
        }

        return Ok(new DocFolderDto
        {
            Id = x.Id,
            ParentId = x.ParentId,
            Name = x.Name,
            Description = x.Description,
            IsPublic = x.IsPublic,
            PublicCanDownload = x.PublicCanDownload,
            CreatedByName = x.CreatedByName,
            CreatedAt = x.CreatedAt,
            IsActive = x.IsActive,
            MyLevel = (DocAccessLevelDto)(int)level,
            MyCanDownload = dl,
            Permissions = perms
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DocFolderDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Create") is { } f) return f;
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "نام پوشه اجباری است." });

        // برای ساخت زیرپوشه باید حداقل «نوشتن» روی والد داشته باشد
        var manager = await IsManagerAsync();
        if (dto.ParentId is int pid)
        {
            var (lvl, _) = await _access.FolderAccessAsync(MyUserId, manager, pid);
            if (lvl < DocAccessLevel.Write)
                return StatusCode(403, new { message = "برای ساخت زیرپوشه، دسترسی نوشتن روی پوشه والد لازم است." });
        }
        else if (!manager)
        {
            return StatusCode(403, new { message = "ساخت پوشه ریشه فقط توسط مدیر آرشیو ممکن است." });
        }

        if (await Db.DocFolders.AnyAsync(x => x.ParentId == dto.ParentId && x.Name == dto.Name.Trim() && x.IsActive))
            return BadRequest(new { message = "پوشه‌ای با این نام در همین سطح وجود دارد." });

        var entity = new DocFolder
        {
            ParentId = dto.ParentId,
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            IsPublic = dto.IsPublic,
            PublicCanDownload = dto.PublicCanDownload,
            CreatedByUserId = MyUserId,
            CreatedByName = MyUsername
        };
        Db.DocFolders.Add(entity);
        await Db.SaveChangesAsync();

        // سازنده همیشه دسترسی کامل می‌گیرد (حتی اگر در لیست نفرات فرم نباشد)؛
        // چون SavePermissionsAsync جایگزین کامل دسترسی‌هاست، رکورد سازنده را هم داخل همان لیست می‌گذاریم
        // تا با یک ذخیره‌سازی، هم پوشه و هم دسترسی‌ها ثبت شوند و خطای کاذب رخ ندهد.
        var items = (dto.Permissions ?? new List<DocPermissionDto>())
            .Where(x => (x.RoleId > 0) || (x.UserId > 0 && x.UserId != MyUserId))
            .GroupBy(x => x.RoleId > 0 ? $"R:{x.RoleId}" : $"U:{x.UserId}")
            .Select(g => g.First())
            .ToList();
        items.Add(new DocPermissionDto { UserId = MyUserId, Level = DocAccessLevelDto.Full, CanDownload = true });
        await SavePermissionsAsync(entity.Id, items);

        return Ok(new { id = entity.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] DocFolderDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var entity = await Db.DocFolders.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound(new { message = "پوشه یافت نشد." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.FolderAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full)
            return StatusCode(403, new { message = "ویرایش پوشه نیازمند دسترسی کامل است." });

        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "نام پوشه اجباری است." });
        if (dto.ParentId == id) return BadRequest(new { message = "پوشه نمی‌تواند والد خودش باشد." });

        entity.Name = dto.Name.Trim();
        entity.Description = dto.Description?.Trim();
        entity.ParentId = dto.ParentId;
        entity.IsPublic = dto.IsPublic;
        entity.PublicCanDownload = dto.PublicCanDownload;
        await Db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>ذخیره دسترسی‌های پوشه (نفرات + سطح + دانلود + نمایش برای همه).</summary>
    [HttpPut("{id:int}/permissions")]
    public async Task<IActionResult> SavePermissions(int id, [FromBody] DocPermissionsSaveDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var entity = await Db.DocFolders.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound(new { message = "پوشه یافت نشد." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.FolderAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full)
            return StatusCode(403, new { message = "مدیریت دسترسی نیازمند دسترسی کامل است." });

        entity.IsPublic = dto.IsPublic;
        entity.PublicCanDownload = dto.PublicCanDownload;

        // محافظ: کاربرِ دارای «دسترسی کامل» نباید خودش را از فهرست حذف کند و پوشه را
        // بدون مدیرِ دارای Full رها کند (تا پوشه برای همیشه «قفل» نشود).
        dto.Items ??= new List<DocPermissionDto>();
        var roleIdsForMe = await Db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == MyUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync();
        var hasFullForMe = dto.Items.Any(p =>
            (p.RoleId == 0 && p.UserId == MyUserId && p.Level == DocAccessLevelDto.Full)
         || (p.RoleId != 0 && p.Level == DocAccessLevelDto.Full && roleIdsForMe.Contains(p.RoleId)));
        if (!hasFullForMe)
        {
            dto.Items.Add(new DocPermissionDto { UserId = MyUserId, RoleId = 0, Level = DocAccessLevelDto.Full, CanDownload = true });
        }

        await SavePermissionsAsync(id, dto.Items);
        return Ok();
    }

    private async Task SavePermissionsAsync(int folderId, List<DocPermissionDto>? items)
    {
        items ??= new();
        var old = await Db.DocFolderPermissions.Where(p => p.FolderId == folderId).ToListAsync();
        Db.DocFolderPermissions.RemoveRange(old);
        // ردیف فردی (RoleId=0) یا گروهی (RoleId>0) — هر کدام فقط یک‌بار؛ ردیف گروهی UserId=0 دارد
        foreach (var p in items
                     .Where(x => x.RoleId > 0 || x.UserId > 0)
                     .GroupBy(x => x.RoleId > 0 ? $"R:{x.RoleId}" : $"U:{x.UserId}")
                     .Select(g => g.First()))
        {
            Db.DocFolderPermissions.Add(new DocFolderPermission
            {
                FolderId = folderId,
                UserId = p.RoleId > 0 ? 0 : p.UserId,
                RoleId = p.RoleId > 0 ? p.RoleId : 0,
                Level = (DocAccessLevel)(int)p.Level,
                CanDownload = p.CanDownload
            });
        }
        await Db.SaveChangesAsync();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Delete") is { } f) return f;

        var entity = await Db.DocFolders.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound(new { message = "پوشه یافت نشد." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.FolderAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full)
            return StatusCode(403, new { message = "حذف پوشه نیازمند دسترسی کامل است." });

        if (await Db.DocFolders.AnyAsync(x => x.ParentId == id && x.IsActive))
            return BadRequest(new { message = "ابتدا زیرپوشه‌ها را حذف کنید." });
        if (await Db.Documents.AnyAsync(d => d.FolderId == id && !d.IsDeleted))
            return BadRequest(new { message = "این پوشه مدرک دارد و حذف نمی‌شود." });

        entity.IsActive = false;
        await Db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>لیست کاربران فعال — برای کمبوی چندانتخابی سرچ‌دار.</summary>
    [HttpGet("/api/doc-archive/lookups")]
    public async Task<IActionResult> Lookups()
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var users = await Db.Users.AsNoTracking().Where(u => u.IsActive)
            .OrderBy(u => u.FirstName ?? u.Username)
            .Select(u => new LookupItem
            {
                Id = u.Id,
                Name = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : (u.FirstName + " " + u.LastName).Trim(),
                AvatarUrl = u.PhotoPath == null ? null : "/uploads/" + u.PhotoPath
            }).ToListAsync();

        // فقط مدارک فعال قابل لینک شدن هستند
        var visibleDocs = await DocQuery.AccessibleAsync(Db, _access, MyUserId, await IsManagerAsync());
        var docs = await visibleDocs.Where(d => d.IsActive && !d.IsDeleted)
            .OrderByDescending(d => d.Id)
            .Select(d => new LookupItem
            {
                Id = d.Id,
                Name = d.Code + " — " + d.Title +
                       (d.CustomerCode == null || d.CustomerCode == "" ? "" : " (مشتری: " + d.CustomerCode + ")")
            })
            .ToListAsync();

        // نقش‌های فعال RBAC — برای تعریف «دسترسی گروهی» روی پوشه/مدرک
        var roles = await Db.Roles.AsNoTracking().Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new LookupItem { Id = r.Id, Name = r.Name })
            .ToListAsync();

        // اگر شماره‌گذار خودکار فعال باشد، فرم ساخت مدرک می‌تواند کد را خالی رها کند
        var numberingEnabled = await Db.DocCodeSettings.AsNoTracking()
            .Where(s => s.Id == 1).Select(s => s.Enabled).FirstOrDefaultAsync();

        return Ok(new DocArchiveLookups { Users = users, Documents = docs, Roles = roles, CodeNumberingEnabled = numberingEnabled });
    }

    /// <summary>
    /// دانلود درختی یک پوشه و زیرپوشه‌ها به صورت یک فایل ZIP کامل به همراه پیوست‌ها و شناسنامه Excel.
    /// </summary>
    [HttpGet("{id:int}/export-zip")]
    public async Task<IActionResult> ExportFolderZip(
        int id,
        [FromQuery] bool includeSubfolders = true,
        [FromQuery] bool onlyActiveVersions = true,
        [FromQuery] bool includeManifest = true)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        try
        {
            var manager = await IsManagerAsync();
            var (zipBytes, fileName) = await _zipService.ExportZipAsync(
                id, includeSubfolders, onlyActiveVersions, includeManifest, MyUserId, manager);

            return File(zipBytes, "application/zip", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"خطا در ایجاد فایل ZIP: {ex.Message}" });
        }
    }

    /// <summary>
    /// دانلود درختی کل آرشیو مجاز یا پوشه فیلترشده در قالب فایل ZIP.
    /// </summary>
    [HttpGet("/api/doc-archive/export-zip")]
    public async Task<IActionResult> ExportAllZip(
        [FromQuery] int? folderId = null,
        [FromQuery] bool includeSubfolders = true,
        [FromQuery] bool onlyActiveVersions = true,
        [FromQuery] bool includeManifest = true)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        try
        {
            var manager = await IsManagerAsync();
            var (zipBytes, fileName) = await _zipService.ExportZipAsync(
                folderId, includeSubfolders, onlyActiveVersions, includeManifest, MyUserId, manager);

            return File(zipBytes, "application/zip", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"خطا در ایجاد فایل ZIP: {ex.Message}" });
        }
    }
}
