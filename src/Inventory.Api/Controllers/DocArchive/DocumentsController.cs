using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== آرشیو اسناد — مدارک، ورژن‌ها، گردش و لینک ==================
/// </summary>
[Route("api/doc-archive/documents")]
public class DocumentsController : RbacControllerBase
{
    private readonly IDocAccessService _access;
    private readonly IDocumentService _svc;
    private readonly DocExpiryWatcher _expiry;
    private readonly IDocDownloadConfirmService _confirm;
    private readonly INotifyService _notify;

    /// <summary>نام فرم — در نوتیفیکیشن‌های ارسالی به کار می‌رود</summary>
    private const string FormName = "آرشیو اسناد و مدارک";

    public DocumentsController(AppDbContext db, IDocAccessService access, IDocumentService svc,
        DocExpiryWatcher expiry, IDocDownloadConfirmService confirm, INotifyService notify) : base(db)
    { _access = access; _svc = svc; _expiry = expiry; _confirm = confirm; _notify = notify; }

    private const string Mod = "DocArchive";
    private Task<bool> IsManagerAsync() => HasAsync(Mod, "Manage");

    // ---------------------------- لیست ----------------------------

    [HttpGet]
    /// <param name="status">وضعیت مدرک: active (پیش‌فرض) | inactive | all</param>
    public async Task<IActionResult> List(int? folderId = null, string? search = null,
        bool onlyExpiring = false, string status = "active",
        string? expiry = null, int expiringDays = 60,
        [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();

        var q = await DocQuery.AccessibleAsync(Db, _access, MyUserId, manager);

        // فیلتر وضعیت — «مدارک غیرفعال» و «سطل بازیافت» زیرمنوی جداگانه دارند
        q = status?.ToLowerInvariant() switch
        {
            "deleted" => q.Where(d => d.IsDeleted),
            "inactive" => q.Where(d => !d.IsDeleted && !d.IsActive),
            "all" => q.Where(d => !d.IsDeleted),
            _ => q.Where(d => !d.IsDeleted && d.IsActive)
        };

        if (folderId is > 0) q = q.Where(d => d.FolderId == folderId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(d => d.Title.Contains(s) || d.Code.Contains(s)
                          || (d.CustomerCode ?? "").Contains(s) || (d.Description ?? "").Contains(s));
        }

        var today = DateTime.Today;
        var horizon = today.AddDays((double)expiringDays + 1);
        var expiryFilter = expiry?.ToLowerInvariant();
        if (onlyExpiring && expiryFilter == null) expiryFilter = "expiring";
        q = expiryFilter switch
        {
            "expiring" => q.Where(d => d.ExpireDate >= today && d.ExpireDate < horizon),
            "expired" => q.Where(d => d.ExpireDate < today),
            "all" => q.Where(d => d.ExpireDate < today || (d.ExpireDate >= today && d.ExpireDate < horizon)),
            _ => q
        };
        var pagination = new Paging.Request(skip, take);
        var docs = await q.OrderByDescending(d => d.Id).ToPageListAsync(pagination);

        var ids = docs.Select(d => d.Id).ToList();

        var folders = await Db.DocFolders.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        // منبع واحد: اولویت مجوزها باید در لیست، جزئیات و عملیات سرور یکسان باشد.
        var documentAccess = await _access.DocumentAccessMapAsync(MyUserId, manager, ids);
        var versions = await Db.DocumentVersions.AsNoTracking().Where(v => ids.Contains(v.DocumentId)).ToListAsync();
        var links = await Db.DocumentLinks.AsNoTracking().Where(l => ids.Contains(l.DocumentId))
            .GroupBy(l => l.DocumentId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);

        // تگ‌های مدارک
        var allDocTags = await (from dt in Db.DocumentTags.AsNoTracking()
                                where ids.Contains(dt.DocumentId)
                                join t in Db.DocTags.AsNoTracking() on dt.TagId equals t.Id
                                select new { dt.DocumentId, Tag = new DocTagDto
                                {
                                    Id = t.Id, Name = t.Name, Color = t.Color, Description = t.Description, CreatedAt = t.CreatedAt
                                }}).ToListAsync();
        var docTagMap = allDocTags.GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Tag).ToList());

        var indexedDocIds = (await Db.DocExtractedTexts.AsNoTracking()
            .Where(e => ids.Contains(e.DocumentId) && e.Status == "Indexed")
            .Select(e => e.DocumentId)
            .Distinct().ToListAsync()).ToHashSet();

        var result = new List<DocumentListDto>();
        foreach (var d in docs)
        {
            var (level, dl) = documentAccess.TryGetValue(d.Id, out var grant)
                ? grant : (DocAccessLevel.None, false);
            if (level == DocAccessLevel.None) continue;

            var vs = versions.Where(v => v.DocumentId == d.Id).ToList();
            var last = vs.OrderByDescending(v => v.VersionNo).FirstOrDefault();
            var activeV = vs.Where(v => v.IsActive).OrderByDescending(v => v.VersionNo).FirstOrDefault();
            var expired = d.ExpireDate.HasValue && d.ExpireDate.Value.Date < DateTime.Today;
            int? daysLeft = d.ExpireDate.HasValue ? (d.ExpireDate.Value.Date - DateTime.Today).Days : null;
            var expiringSoon = daysLeft is >= 0 && daysLeft <= expiringDays;

            result.Add(new DocumentListDto
            {
                Id = d.Id,
                FolderId = d.FolderId,
                FolderName = folders.TryGetValue(d.FolderId, out var fn) ? fn : "",
                Title = d.Title,
                Code = d.Code,
                CustomerCode = d.CustomerCode,
                ExpireDate = d.ExpireDate,
                IsExpired = expired,
                DaysToExpire = daysLeft,
                IsExpiringSoon = expiringSoon,
                AllowMultipleActiveVersions = d.AllowMultipleActiveVersions,
                IsPublic = d.IsPublic,
                RequireDownloadConfirm = d.RequireDownloadConfirm,
                WatermarkPreview = d.WatermarkPreview,
                CreatedByName = d.CreatedByName,
                CreatedAt = d.CreatedAt,
                IsActive = d.IsActive,
                DeactivatedAt = d.DeactivatedAt,
                DeactivatedByName = d.DeactivatedByName,
                DeactivateReason = d.DeactivateReason,
                IsDeleted = d.IsDeleted,
                DeletedAt = d.DeletedAt,
                DeletedByName = d.DeletedByName,
                VersionCount = vs.Count,
                ActiveVersionNo = activeV?.VersionNo ?? 0,
                LastVersionStatus = (DocVersionStatusDto)(int)(last?.Status ?? DocVersionStatus.Draft),
                LinkCount = links.TryGetValue(d.Id, out var lc) ? lc : 0,
                MyLevel = (DocAccessLevelDto)(int)level,
                MyCanDownload = dl,
                Tags = docTagMap.TryGetValue(d.Id, out var tlist) ? tlist : new(),
                HasIndexedContent = indexedDocIds.Contains(d.Id),
            });
        }

        return Ok(pagination.Result(result));
    }

    // ---------------------------- جزئیات ----------------------------

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (level, dl) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (level == DocAccessLevel.None) return StatusCode(403, new { message = "به این مدرک دسترسی ندارید." });

        var d = await Db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return NotFound(new { message = "مدرک یافت نشد." });

        var dto = new DocumentDto
        {
            Id = d.Id,
            FolderId = d.FolderId,
            FolderName = await Db.DocFolders.Where(x => x.Id == d.FolderId).Select(x => x.Name).FirstOrDefaultAsync() ?? "",
            Title = d.Title,
            Code = d.Code,
            CustomerCode = d.CustomerCode,
            Description = d.Description,
            ExpireDate = d.ExpireDate,
            AllowMultipleActiveVersions = d.AllowMultipleActiveVersions,
            IsPublic = d.IsPublic,
            PublicCanDownload = d.PublicCanDownload,
            RequireDownloadConfirm = d.RequireDownloadConfirm,
            WatermarkPreview = d.WatermarkPreview,
            CreatedByName = d.CreatedByName,
            CreatedAt = d.CreatedAt,
            IsActive = d.IsActive,
            DeactivatedAt = d.DeactivatedAt,
            DeactivatedByName = d.DeactivatedByName,
            DeactivateReason = d.DeactivateReason,
            MyLevel = (DocAccessLevelDto)(int)level,
            MyCanDownload = dl
        };

        // سطح «مشاهده» فقط سرفصل را می‌بیند
        if (level == DocAccessLevel.View) return Ok(dto);

        var approversAll = await Db.DocumentApprovers.AsNoTracking().Where(a => a.DocumentId == id).ToListAsync();
        var users = await Db.Users.AsNoTracking()
            .ToDictionaryAsync(u => u.Id, u => new { Name = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : (u.FirstName + " " + u.LastName).Trim(), u.PhotoPath });

        DocApproverDto ToDto(DocumentApprover a) => new()
        {
            Id = a.Id,
            UserId = a.UserId,
            UserName = users.TryGetValue(a.UserId, out var u) ? u.Name : a.UserName,
            AvatarUrl = users.TryGetValue(a.UserId, out var u2) && u2.PhotoPath != null ? "/uploads/" + u2.PhotoPath : null,
            Order = a.Order,
            Status = a.Status,
            Comment = a.Comment,
            ActedAt = a.ActedAt
        };

        dto.Approvers = approversAll.Where(a => a.VersionId == null).OrderBy(a => a.Order).Select(ToDto).ToList();

        var attCounts = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "DocVersion")
            .GroupBy(a => a.RefId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);

        dto.Versions = (await Db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == id).OrderByDescending(v => v.VersionNo).ToListAsync())
            .Select(v => new DocVersionDto
            {
                Id = v.Id,
                DocumentId = v.DocumentId,
                VersionNo = v.VersionNo,
                Title = v.Title,
                ChangeNote = v.ChangeNote,
                ExpireDate = v.ExpireDate,
                Status = (DocVersionStatusDto)(int)v.Status,
                IsActive = v.IsActive,
                IsFrozen = v.IsFrozen,
                ActivateOnApprove = v.ActivateOnApprove,
                CreatedByName = v.CreatedByName,
                CreatedAt = v.CreatedAt,
                ApprovedAt = v.ApprovedAt,
                AttachmentCount = attCounts.TryGetValue(v.Id, out var c) ? c : 0,
                Approvers = approversAll.Where(a => a.VersionId == v.Id).OrderBy(a => a.Order).Select(ToDto).ToList(),
                IsMyTurn = approversAll.Any(a => a.VersionId == v.Id && a.UserId == MyUserId && a.Status == 0)
                           && v.Status == DocVersionStatus.InReview
            }).ToList();

        dto.Links = await Db.DocumentLinks.AsNoTracking().Where(l => l.DocumentId == id)
            .Join(Db.Documents, l => l.LinkedDocumentId, x => x.Id, (l, x) => new DocLinkDto
            {
                Id = l.Id,
                LinkedDocumentId = x.Id,
                Code = x.Code,
                CustomerCode = x.CustomerCode,
                Title = x.Title,
                Note = l.Note,
                IsActive = x.IsActive,
                ActiveVersionNo = Db.DocumentVersions.Where(v => v.DocumentId == x.Id && v.IsActive)
                                    .Select(v => v.VersionNo).OrderByDescending(n => n).FirstOrDefault()
            }).ToListAsync();

        // تگ‌های مدرک
        dto.Tags = await (from dt in Db.DocumentTags.AsNoTracking()
                          where dt.DocumentId == id
                          join t in Db.DocTags.AsNoTracking() on dt.TagId equals t.Id
                          select new DocTagDto
                          {
                              Id = t.Id, Name = t.Name, Color = t.Color, Description = t.Description, CreatedAt = t.CreatedAt
                          }).ToListAsync();
        dto.TagIds = dto.Tags.Select(t => t.Id).ToList();

        dto.ExtractedTextCount = await Db.DocExtractedTexts.AsNoTracking()
            .CountAsync(e => e.DocumentId == id && e.Status == "Indexed");

        if (level >= DocAccessLevel.Full)
        {
            dto.Permissions = await Db.DocumentPermissions.AsNoTracking().Where(p => p.DocumentId == id)
                .Select(p => new DocPermissionDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    RoleId = p.RoleId,
                    UserName = "",
                    Level = (DocAccessLevelDto)(int)p.Level,
                    CanDownload = p.CanDownload
                }).ToListAsync();
            var roleNames = await Db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name);
            foreach (var p in dto.Permissions)
            {
                if (p.RoleId > 0) p.RoleName = roleNames.TryGetValue(p.RoleId, out var rn) ? rn : $"#{p.RoleId}";
                else if (users.TryGetValue(p.UserId, out var u)) p.UserName = u.Name;
            }
        }

        dto.Logs = await Db.DocumentLogs.AsNoTracking().Where(l => l.DocumentId == id)
            .OrderByDescending(l => l.Id).Take(100)
            .Select(l => new DocLogDto
            {
                Id = l.Id, Action = l.Action, Detail = l.Detail,
                UserName = l.UserName, CreatedAt = l.CreatedAt, VersionId = l.VersionId
            }).ToListAsync();

        // کاربر بدون «دسترسی کامل» نباید بفهمد چه کسانی/چه گروه‌هایی روی این مدرک دسترسی
        // دارند یا چه کسی درخواست دسترسی داده است؛ این رویدادها فقط برای دارندگان دسترسی کامل
        // (و مدیر آرشیو) نمایش داده می‌شوند.
        if (level < DocAccessLevel.Full)
        {
            string[] hiddenActions = { "Permissions", "AccessRequest", "AccessRequestApproved", "AccessRequestRejected", "TemporaryAccess", "TemporaryAccessRevoked", "RenewalPolicy" };
            dto.Logs = dto.Logs.Where(l => !hiddenActions.Contains(l.Action)).ToList();
        }

        // ثبت بازدید برای «اخیراً دیده‌شده» — بدون مسدود کردن پاسخ در صورت خطا
        try
        {
            Db.DocumentLogs.Add(new DocumentLog
            {
                DocumentId = id,
                Action = level == DocAccessLevel.View ? "View" : "Read",
                Detail = "بازدید مدرک",
                UserId = MyUserId,
                UserName = MyUsername
            });
            await Db.SaveChangesAsync();
        }
        catch { /* لاگ بازدید نباید پاسخ را خراب کند */ }

        return Ok(dto);
    }

    // ---------------------------- ثبت / ویرایش ----------------------------

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DocumentDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Create") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.FolderAccessAsync(MyUserId, manager, dto.FolderId);
        if (lvl < DocAccessLevel.Write)
            return StatusCode(403, new { message = "برای ثبت مدرک در این پوشه، دسترسی نوشتن لازم است." });

        var id = await _svc.CreateDocumentAsync(dto, MyUserId, MyUsername);
        return Ok(new { id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] DocumentDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Write)
            return StatusCode(403, new { message = "ویرایش مدرک نیازمند دسترسی نوشتن است." });

        var existing = await Db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (existing == null) return NotFound(new { message = "مدرک یافت نشد." });
        if (dto.FolderId != existing.FolderId)
        {
            var (destinationLevel, _) = await _access.FolderAccessAsync(MyUserId, manager, dto.FolderId);
            if (destinationLevel < DocAccessLevel.Write)
                return StatusCode(403, new { message = "انتقال مدرک نیازمند دسترسی نوشتن روی پوشه مقصد است." });
        }

        // مدیریت دسترسی فقط با «دسترسی کامل» ممکن است. کاربر بدون دسترسی کامل حتی با
        // ارسال دستی فهرست دسترسی‌ها نمی‌تواند دسترسی‌های موجود را پاک یا تغییر دهد:
        // null یعنی «دست نزن» (در سرویس، ردیف‌های موجود حفظ می‌شوند).
        if (lvl < DocAccessLevel.Full)
        {
            dto.Permissions = null!;
            // Write اجازه عمومی‌کردن مدرک، بازکردن دانلود یا برداشتن حفاظت فایل را نمی‌دهد.
            dto.IsPublic = existing.IsPublic;
            dto.PublicCanDownload = existing.PublicCanDownload;
            dto.RequireDownloadConfirm = existing.RequireDownloadConfirm;
            dto.WatermarkPreview = existing.WatermarkPreview;
        }
        else
        {
            // محافظ: ذخیره‌کنندهٔ دارای Full نباید خودش را حذف کند و مدرک را بدون
            // مدیرِ دارای Full رها کند.
            dto.Permissions ??= new List<DocPermissionDto>();
            var roleIdsForMe = await Db.UserRoles.AsNoTracking()
                .Where(ur => ur.UserId == MyUserId)
                .Select(ur => ur.RoleId)
                .ToListAsync();
            var hasFullForMe = dto.Permissions.Any(p =>
                (p.RoleId == 0 && p.UserId == MyUserId && p.Level == DocAccessLevelDto.Full)
             || (p.RoleId != 0 && p.Level == DocAccessLevelDto.Full && roleIdsForMe.Contains(p.RoleId)));
            if (!hasFullForMe)
                dto.Permissions.Add(new DocPermissionDto { UserId = MyUserId, RoleId = 0, Level = DocAccessLevelDto.Full, CanDownload = true });
        }

        await _svc.UpdateDocumentAsync(id, dto, MyUserId, MyUsername);
        return Ok();
    }

    [HttpPut("{id:int}/permissions")]
    public async Task<IActionResult> SavePermissions(int id, [FromBody] DocPermissionsSaveDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full)
            return StatusCode(403, new { message = "مدیریت دسترسی نیازمند دسترسی کامل است." });

        var doc = await Db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });

        doc.IsPublic = dto.IsPublic;
        doc.PublicCanDownload = dto.PublicCanDownload;
        doc.RequireDownloadConfirm = dto.RequireDownloadConfirm;
        doc.WatermarkPreview = dto.WatermarkPreview;

        // محافظ: کاربرِ دارای «دسترسی کامل» نباید خودش را از فهرست حذف کند و مدرک را
        // بدون مدیرِ دارای Full رها کند (تا مدرک برای همیشه «قفل» نشود).
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

        var old = await Db.DocumentPermissions.Where(p => p.DocumentId == id).ToListAsync();
        Db.DocumentPermissions.RemoveRange(old);
        // ردیف فردی (RoleId=0) یا گروهی (RoleId>0) — هر کدام فقط یک‌بار؛ ردیف گروهی UserId=0 دارد
        var newItems = dto.Items
            .Where(x => x.RoleId > 0 || x.UserId > 0)
            .GroupBy(x => x.RoleId > 0 ? $"R:{x.RoleId}" : $"U:{x.UserId}")
            .Select(g => g.First())
            .ToList();
        foreach (var p in newItems)
            Db.DocumentPermissions.Add(new DocumentPermission
            {
                DocumentId = id,
                UserId = p.RoleId > 0 ? 0 : p.UserId,
                RoleId = p.RoleId > 0 ? p.RoleId : 0,
                Level = (DocAccessLevel)(int)p.Level, CanDownload = p.CanDownload
            });

        var userCount = newItems.Count(x => x.RoleId == 0);
        var roleCount = newItems.Count(x => x.RoleId > 0);
        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = id,
            Action = "Permissions",
            Detail = $"دسترسی‌ها به‌روزرسانی شد: {userCount} کاربر"
                     + (roleCount > 0 ? $" + {roleCount} گروه/نقش" : "")
                     + (dto.IsPublic ? " — نمایش برای همه: فعال" : "")
                     + (dto.RequireDownloadConfirm ? " — محرمانه (تایید رمز برای فایل): فعال" : "")
                     + (old.Count != newItems.Count ? $" (قبلاً {old.Count} ردیف)" : ""),
            UserId = MyUserId, UserName = MyUsername
        });

        await Db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>تایید مجدد رمز عبور کاربر برای باز کردن قفل دانلود/مشاهده فایل‌های مدرک محرمانه (۱۵ دقیقه اعتبار).</summary>
    public class ConfirmDownloadDto { public string Password { get; set; } = ""; }

    [HttpPost("{id:int}/confirm-download")]
    public async Task<IActionResult> ConfirmDownload(int id, [FromBody] ConfirmDownloadDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (level, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (level == DocAccessLevel.None)
            return StatusCode(403, new { message = "به این مدرک دسترسی ندارید." });

        var user = await Db.Users.FirstOrDefaultAsync(u => u.Id == MyUserId);
        if (user == null || !Inventory.Api.Services.AuthService.VerifyPassword(dto.Password ?? "", user.PasswordHash))
        {
            Db.DocumentLogs.Add(new DocumentLog
            {
                DocumentId = id, Action = "DownloadConfirmFailed",
                Detail = "تلاش ناموفق تایید رمز برای دانلود/مشاهده فایل",
                UserId = MyUserId, UserName = MyUsername
            });
            await Db.SaveChangesAsync();
            return StatusCode(403, new { message = "رمز عبور اشتباه است." });
        }

        _confirm.Confirm(MyUserId, id);
        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = id, Action = "DownloadConfirm",
            Detail = "تایید مجدد رمز برای دانلود/مشاهده فایل (اعتبار: ۱۵ دقیقه)",
            UserId = MyUserId, UserName = MyUsername
        });
        await Db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>گزارش مشاهده/دانلود فایل‌های این مدرک — فقط برای دارندگان دسترسی کامل.</summary>
    [HttpGet("{id:int}/access-logs")]
    public async Task<IActionResult> AccessLogs(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (level, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (level < DocAccessLevel.Full)
            return StatusCode(403, new { message = "مشاهده گزارش دانلود فایل‌ها نیازمند دسترسی کامل است." });

        var versionIds = await Db.DocumentVersions.Where(v => v.DocumentId == id)
            .Select(v => v.Id).ToListAsync();

        var logs = await Db.AppAttachmentAccessLogs.AsNoTracking()
            .Where(l => l.Module == "DocVersion" && versionIds.Contains(l.RefId))
            .OrderByDescending(l => l.Id)
            .Select(l => new DocAttachmentAccessLogDto
            {
                AttachmentId = l.AttachmentId,
                FileName = l.FileName,
                Action = l.Action,
                UserName = l.UserName,
                Ip = l.Ip,
                At = l.At
            }).ToPageListAsync(pagination, defaultCap: 200);

        return Ok(pagination.Result(logs));
    }

    /// <summary>آیا این کد مدرک قبلاً ثبت شده است؟ — برای هشدار زنده در فرم ساخت/ویرایش.</summary>
    [HttpGet("check-code")]
    public async Task<IActionResult> CheckCode([FromQuery] string? code, [FromQuery] int excludingId = 0)
    {
        var c = (code ?? "").Trim();
        if (c.Length == 0) return Ok(new { exists = false });
        var exists = await Db.Documents.AnyAsync(d => d.Code == c && d.Id != excludingId);
        return Ok(new { exists });
    }

    // ---------------------------- درخواست دسترسی ----------------------------

    /// <summary>کاربر بدون دسترسی کافی، برای این مدرک درخواست دسترسی ثبت می‌کند؛ مدیران مدرک نوتیف می‌شوند.</summary>
    [HttpPost("{id:int}/request-access")]
    public async Task<IActionResult> RequestAccess(int id, [FromBody] DocAccessRequestSaveDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var doc = await Db.Documents.AsNoTracking().Where(d => d.Id == id && !d.IsDeleted)
            .Select(d => new { d.Id, d.Code, d.Title }).FirstOrDefaultAsync();
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });

        var manager = await IsManagerAsync();
        var (level, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (level == DocAccessLevel.Full)
            return BadRequest(new { message = "شما دسترسی کامل به این مدرک دارید؛ نیازی به درخواست نیست." });
        if ((int)dto.Level <= (int)DocAccessLevel.View || (int)dto.Level <= (int)level)
            return BadRequest(new { message = "سطح دسترسی درخواستی بالاتر از دسترسی فعلی شما نیست." });

        var me = await Db.Users.AsNoTracking().Where(u => u.Id == MyUserId)
            .Select(u => string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : (u.FirstName + " " + u.LastName).Trim())
            .FirstOrDefaultAsync() ?? MyUsername;

        // اگر درخواست درانتظارِ همین کاربر وجود دارد، همان به‌روزرسانی می‌شود
        var pending = await Db.DocAccessRequests.FirstOrDefaultAsync(r =>
            r.DocumentId == id && r.RequesterUserId == MyUserId && r.Status == DocAccessRequestStatus.Pending);
        if (pending != null)
        {
            pending.RequestedLevel = (DocAccessLevel)(int)dto.Level;
            pending.Note = dto.Note?.Trim();
            pending.CreatedAt = DateTime.Now;
            await Db.SaveChangesAsync();
            return Ok(new { id = pending.Id });
        }

        var req = new DocAccessRequest
        {
            DocumentId = id,
            RequesterUserId = MyUserId,
            RequesterName = me,
            RequestedLevel = (DocAccessLevel)(int)dto.Level,
            Note = dto.Note?.Trim()
        };
        Db.DocAccessRequests.Add(req);

        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = id,
            Action = "AccessRequest",
            Detail = $"«{me}» درخواست دسترسی «{LevelFa((DocAccessLevel)(int)dto.Level)}» ثبت کرد.",
            UserId = MyUserId, UserName = MyUsername
        });
        await Db.SaveChangesAsync();

        try
        {
            var fullUsers = await _access.UsersWithFullAccessAsync(id);
            fullUsers.Remove(MyUserId);
            await _notify.SendManyAsync(fullUsers,
                "درخواست دسترسی به مدرک",
                $"«{me}» برای مدرک {doc.Code} — {doc.Title} سطح «{LevelFa((DocAccessLevel)(int)dto.Level)}» درخواست کرده است.",
                me, FormName, $"/doc-archive/documents/{id}");
        }
        catch { /* خطای نوتیف نباید جریان را متوقف کند */ }

        return Ok(new { id = req.Id });
    }

    /// <summary>فهرست درخواست‌های دسترسی این مدرک — فقط مدیران مدرک (دسترسی کامل).</summary>
    [HttpGet("{id:int}/access-requests")]
    public async Task<IActionResult> AccessRequests(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (level, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (level < DocAccessLevel.Full)
            return StatusCode(403, new { message = "مشاهده درخواست‌های دسترسی نیازمند دسترسی کامل است." });

        var list = await Db.DocAccessRequests.AsNoTracking()
            .Where(r => r.DocumentId == id)
            .OrderBy(r => r.Status != DocAccessRequestStatus.Pending)
            .ThenByDescending(r => r.Id)
            .Select(r => ToRequestDto(r))
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(list));
    }

    /// <summary>درخواست‌های من برای این مدرک — برای نمایش وضعیت به خودِ درخواست‌کننده.</summary>
    [HttpGet("{id:int}/access-requests/mine")]
    public async Task<IActionResult> MyAccessRequests(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var list = await Db.DocAccessRequests.AsNoTracking()
            .Where(r => r.DocumentId == id && r.RequesterUserId == MyUserId)
            .OrderByDescending(r => r.Id)
            .Select(r => ToRequestDto(r))
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(list));
    }

    private static DocAccessRequestDto ToRequestDto(DocAccessRequest r) => new()
    {
        Id = r.Id,
        DocumentId = r.DocumentId,
        RequesterUserId = r.RequesterUserId,
        RequesterName = r.RequesterName,
        RequestedLevel = (DocAccessLevelDto)(int)r.RequestedLevel,
        Note = r.Note,
        Status = (int)r.Status,
        CreatedAt = r.CreatedAt,
        HandledByName = r.HandledByName,
        HandlerNote = r.HandlerNote,
        HandledAt = r.HandledAt
    };

    internal static string LevelFa(DocAccessLevel lvl) => lvl switch
    {
        DocAccessLevel.Read => "خواندن",
        DocAccessLevel.Write => "نوشتن",
        DocAccessLevel.Full => "دسترسی کامل",
        DocAccessLevel.View => "مشاهده",
        _ => lvl.ToString()
    };

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Delete") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full) return StatusCode(403, new { message = "حذف مدرک نیازمند دسترسی کامل است." });

        var doc = await Db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });
        if (await Db.DocumentVersions.AnyAsync(v => v.DocumentId == id && v.Status == DocVersionStatus.InReview))
            return BadRequest(new { message = "ورژنی از این مدرک در گردش تایید است؛ حذف ممکن نیست." });

        doc.IsDeleted = true;
        doc.DeletedAt = DateTime.Now;
        doc.DeletedByName = MyUsername;

        // کارهای باز کارتابل این مدرک بسته می‌شوند
        var openTasks = await Db.DocCartableTasks.Where(t => t.DocumentId == id && t.Status == 0).ToListAsync();
        foreach (var t in openTasks) { t.Status = 1; t.DoneAt = DateTime.Now; }

        Db.DocumentLogs.Add(new DocumentLog { DocumentId = id, Action = "Delete", Detail = "مدرک به سطل بازیافت منتقل شد.", UserId = MyUserId, UserName = MyUsername });
        await Db.SaveChangesAsync();
        return Ok();
    }

    // ---------------------------- ورژن ----------------------------

    [HttpPost("versions")]
    public async Task<IActionResult> CreateVersion([FromBody] DocVersionCreateDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, dto.DocumentId);
        if (lvl < DocAccessLevel.Write)
            return StatusCode(403, new { message = "ثبت ورژن نیازمند دسترسی نوشتن است." });

        var id = await _svc.CreateVersionAsync(dto, MyUserId, MyUsername);
        return Ok(new { id });
    }

    [HttpPut("versions/{versionId:int}/active")]
    public async Task<IActionResult> SetActive(int versionId, [FromQuery] bool active)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var ver = await Db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId);
        if (ver == null) return NotFound(new { message = "ورژن یافت نشد." });

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, ver.DocumentId);
        if (lvl < DocAccessLevel.Full) return StatusCode(403, new { message = "تغییر وضعیت ورژن نیازمند دسترسی کامل است." });

        await _svc.SetVersionActiveAsync(versionId, active, MyUserId, MyUsername);
        return Ok();
    }

    // ---------------------------- گردش تایید ----------------------------

    [HttpPost("approval")]
    public async Task<IActionResult> Approval([FromBody] DocApprovalActionDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        await _svc.ActApprovalAsync(dto, MyUserId, MyUsername);
        return Ok();
    }

    // ---------------------------- لینک مدارک ----------------------------

    public class LinkRequest { public int DocumentId { get; set; } public int LinkedDocumentId { get; set; } public string? Note { get; set; } }

    [HttpPost("links")]
    public async Task<IActionResult> Link([FromBody] LinkRequest req)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, req.DocumentId);
        if (lvl < DocAccessLevel.Write) return StatusCode(403, new { message = "لینک کردن نیازمند دسترسی نوشتن است." });

        await _svc.LinkAsync(req.DocumentId, req.LinkedDocumentId, req.Note, MyUserId, MyUsername);
        return Ok();
    }

    /// <summary>لینک چندتایی مدارک مرتبط (کمبوی چندانتخابی سرچ‌دار در فرم).</summary>
    [HttpPost("links/many")]
    public async Task<IActionResult> LinkMany([FromBody] DocLinkSaveDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, dto.DocumentId);
        if (lvl < DocAccessLevel.Write) return StatusCode(403, new { message = "لینک کردن نیازمند دسترسی نوشتن است." });

        await _svc.LinkManyAsync(dto, MyUserId, MyUsername);
        return Ok();
    }

    // ---------------------------- فعال / غیرفعال کردن مدرک ----------------------------

    /// <summary>تغییر وضعیت مدرک به فعال/غیرفعال — نیازمند دسترسی کامل.</summary>
    [HttpPut("{id:int}/active")]
    public async Task<IActionResult> SetDocumentActive(int id, [FromBody] DocSetActiveDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full)
            return StatusCode(403, new { message = "تغییر وضعیت مدرک نیازمند دسترسی کامل است." });

        await _svc.SetDocumentActiveAsync(id, dto, MyUserId, MyUsername);
        return Ok();
    }

    [HttpDelete("links")]
    public async Task<IActionResult> Unlink([FromQuery] int documentId, [FromQuery] int linkedDocumentId)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, documentId);
        if (lvl < DocAccessLevel.Write) return StatusCode(403, new { message = "حذف لینک نیازمند دسترسی نوشتن است." });

        await _svc.UnlinkAsync(documentId, linkedDocumentId, MyUserId);
        return Ok();
    }

    // ---------------------------- مقایسه دو ورژن ----------------------------

    /// <summary>مقایسه دو ورژن یک مدرک — تفاوت فیلدها، نفرات گردش، پیوست‌ها و رویدادهای بین آن دو.</summary>
    [HttpGet("{id:int}/compare")]
    public async Task<IActionResult> Compare(int id, [FromQuery] int from, [FromQuery] int to)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Read)
            return StatusCode(403, new { message = "مشاهده مقایسه نیازمند دسترسی خواندن است." });

        var doc = await Db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });

        if (from == to)
            return BadRequest(new { message = "دو ورژن متفاوت انتخاب کنید." });

        var vs = await Db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == id && (v.Id == from || v.Id == to)).ToListAsync();
        var vL = vs.FirstOrDefault(v => v.Id == from);
        var vR = vs.FirstOrDefault(v => v.Id == to);
        if (vL == null || vR == null)
            return NotFound(new { message = "یکی از ورژن‌های انتخاب‌شده متعلق به این مدرک نیست." });

        // همیشه قدیمی‌تر سمت راست (مبدأ) و جدیدتر سمت چپ نمایش داده می‌شود
        if (vL.VersionNo > vR.VersionNo) (vL, vR) = (vR, vL);

        var apprs = await Db.DocumentApprovers.AsNoTracking()
            .Where(a => a.DocumentId == id && (a.VersionId == vL.Id || a.VersionId == vR.Id)).ToListAsync();
        var atts = await Db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == "DocVersion" && (a.RefId == vL.Id || a.RefId == vR.Id))
            .Select(a => new { a.RefId, a.FileName }).ToListAsync();

        var res = new DocVersionCompareDto
        {
            DocumentId = id,
            DocumentCode = doc.Code,
            DocumentTitle = doc.Title,
            Left = MapVersion(vL, apprs, atts.Count(a => a.RefId == vL.Id)),
            Right = MapVersion(vR, apprs, atts.Count(a => a.RefId == vR.Id))
        };

        // ---------- فیلدهای اصلی ----------
        void Row(List<DocVersionDiffRow> list, string field, string? l, string? r)
        {
            var kind = (string.IsNullOrWhiteSpace(l), string.IsNullOrWhiteSpace(r)) switch
            {
                (true, true) => "same",
                (true, false) => "added",
                (false, true) => "removed",
                _ => l == r ? "same" : "changed"
            };
            list.Add(new DocVersionDiffRow { Field = field, Left = l, Right = r, Kind = kind });
        }

        Row(res.Fields, "عنوان", vL.Title, vR.Title);
        Row(res.Fields, "شرح تغییر", vL.ChangeNote, vR.ChangeNote);
        Row(res.Fields, "تاریخ انقضا",
            vL.ExpireDate?.ToString("yyyy/MM/dd"), vR.ExpireDate?.ToString("yyyy/MM/dd"));
        Row(res.Fields, "وضعیت", StatusName(vL.Status), StatusName(vR.Status));
        Row(res.Fields, "ورژن فعال", vL.IsActive ? "بله" : "خیر", vR.IsActive ? "بله" : "خیر");
        Row(res.Fields, "فریز شده", vL.IsFrozen ? "بله" : "خیر", vR.IsFrozen ? "بله" : "خیر");
        Row(res.Fields, "فعال‌سازی خودکار پس از تایید",
            vL.ActivateOnApprove ? "بله" : "خیر", vR.ActivateOnApprove ? "بله" : "خیر");
        Row(res.Fields, "ثبت‌کننده", vL.CreatedByName, vR.CreatedByName);
        Row(res.Fields, "تاریخ ثبت",
            vL.CreatedAt.ToString("yyyy/MM/dd HH:mm"), vR.CreatedAt.ToString("yyyy/MM/dd HH:mm"));
        Row(res.Fields, "تاریخ تایید",
            vL.ApprovedAt?.ToString("yyyy/MM/dd HH:mm"), vR.ApprovedAt?.ToString("yyyy/MM/dd HH:mm"));

        // ---------- نفرات گردش ----------
        var apL = apprs.Where(a => a.VersionId == vL.Id).ToList();
        var apR = apprs.Where(a => a.VersionId == vR.Id).ToList();
        foreach (var uid in apL.Select(a => a.UserId).Union(apR.Select(a => a.UserId)).Distinct())
        {
            var a1 = apL.FirstOrDefault(a => a.UserId == uid);
            var a2 = apR.FirstOrDefault(a => a.UserId == uid);
            var name = a1?.UserName ?? a2?.UserName ?? uid.ToString();
            res.Approvers.Add(new DocVersionDiffRow
            {
                Field = name,
                Left = a1 == null ? null : ApproverState(a1.Status),
                Right = a2 == null ? null : ApproverState(a2.Status),
                Kind = a1 == null ? "added" : a2 == null ? "removed"
                       : a1.Status == a2.Status ? "same" : "changed"
            });
        }

        // ---------- پیوست‌ها ----------
        var atL = atts.Where(a => a.RefId == vL.Id).Select(a => a.FileName).ToList();
        var atR = atts.Where(a => a.RefId == vR.Id).Select(a => a.FileName).ToList();
        foreach (var name in atL.Union(atR).Distinct())
        {
            var inL = atL.Contains(name);
            var inR = atR.Contains(name);
            res.Attachments.Add(new DocVersionDiffRow
            {
                Field = name,
                Left = inL ? "دارد" : null,
                Right = inR ? "دارد" : null,
                Kind = inL && inR ? "same" : inL ? "added" : "removed"
            });
        }

        // ---------- رویدادهای بین دو ورژن ----------
        var t1 = vL.CreatedAt < vR.CreatedAt ? vL.CreatedAt : vR.CreatedAt;
        var t2 = vL.CreatedAt < vR.CreatedAt ? vR.CreatedAt : vL.CreatedAt;
        res.Between = await Db.DocumentLogs.AsNoTracking()
            .Where(l => l.DocumentId == id && l.CreatedAt >= t1 && l.CreatedAt <= t2)
            .OrderBy(l => l.Id)
            .Select(l => new DocLogDto
            {
                Id = l.Id, Action = l.Action, Detail = l.Detail,
                UserName = l.UserName, CreatedAt = l.CreatedAt, VersionId = l.VersionId
            }).ToListAsync();

        if (lvl < DocAccessLevel.Full)
            res.Between = res.Between.Where(l => l.Action != "Permissions" && !l.Action.StartsWith("AccessRequest") && !l.Action.StartsWith("TemporaryAccess") && l.Action != "RenewalPolicy").ToList();
        res.ChangeCount = res.Fields.Count(x => x.Kind != "same")
                        + res.Approvers.Count(x => x.Kind != "same")
                        + res.Attachments.Count(x => x.Kind != "same");
        return Ok(res);
    }

    private static string StatusName(DocVersionStatus s) => s switch
    {
        DocVersionStatus.Draft => "پیش‌نویس",
        DocVersionStatus.InReview => "در گردش تایید",
        DocVersionStatus.Approved => "تایید شده",
        DocVersionStatus.Rejected => "رد شده",
        DocVersionStatus.Archived => "بایگانی",
        _ => s.ToString()
    };

    private static string ApproverState(int st) => st switch
    {
        1 => "تایید کرد",
        2 => "رد کرد",
        _ => "در انتظار"
    };

    private DocVersionDto MapVersion(DocumentVersion v,
        List<DocumentApprover> apprs, int attachCount) => new()
    {
        Id = v.Id,
        DocumentId = v.DocumentId,
        VersionNo = v.VersionNo,
        Title = v.Title,
        ChangeNote = v.ChangeNote,
        ExpireDate = v.ExpireDate,
        Status = (DocVersionStatusDto)(int)v.Status,
        IsActive = v.IsActive,
        IsFrozen = v.IsFrozen,
        ActivateOnApprove = v.ActivateOnApprove,
        CreatedByName = v.CreatedByName,
        CreatedAt = v.CreatedAt,
        ApprovedAt = v.ApprovedAt,
        AttachmentCount = attachCount,
        Approvers = apprs.Where(a => a.VersionId == v.Id).OrderBy(a => a.Order)
            .Select(a => new DocApproverDto
            {
                Id = a.Id, UserId = a.UserId, UserName = a.UserName,
                Order = a.Order, Status = a.Status, Comment = a.Comment, ActedAt = a.ActedAt
            }).ToList()
    };

    // ---------------------------- سطل بازیافت ----------------------------

    /// <summary>بازگرداندن مدرک حذف‌شده — نیازمند دسترسی کامل.</summary>
    [HttpPut("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Delete") is { } f) return f;

        var manager = await IsManagerAsync();
        var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, id);
        if (lvl < DocAccessLevel.Full)
            return StatusCode(403, new { message = "بازگرداندن مدرک نیازمند دسترسی کامل است." });

        var doc = await Db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });
        if (!doc.IsDeleted) return BadRequest(new { message = "این مدرک حذف نشده است." });

        // پوشه مقصد هنوز هست؟ اگر نه، به ریشه برگردان
        var folderExists = await Db.DocFolders.AnyAsync(x => x.Id == doc.FolderId);
        var note = "";
        if (!folderExists)
        {
            var root = await Db.DocFolders.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (root == null) return BadRequest(new { message = "پوشه‌ای برای بازگرداندن مدرک وجود ندارد." });
            doc.FolderId = root.Id;
            note = $" (پوشه اصلی حذف شده بود؛ به «{root.Name}» منتقل شد)";
        }

        // کد مدرک نباید با مدرک فعال دیگری تداخل داشته باشد
        if (await Db.Documents.AnyAsync(d => d.Id != id && !d.IsDeleted && d.Code == doc.Code))
            return BadRequest(new { message = $"کد «{doc.Code}» اکنون توسط مدرک دیگری استفاده می‌شود؛ ابتدا آن را تغییر دهید." });

        doc.IsDeleted = false;
        doc.DeletedAt = null;
        doc.DeletedByName = null;

        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = id, Action = "Restore",
            Detail = "مدرک از سطل بازیافت بازگردانده شد." + note,
            UserId = MyUserId, UserName = MyUsername
        });
        await Db.SaveChangesAsync();
        return Ok(new { message = "مدرک بازگردانده شد." + note });
    }

    /// <summary>حذف قطعی مدرک و همه وابسته‌هایش — فقط مدیر ماژول.</summary>
    [HttpDelete("{id:int}/purge")]
    public async Task<IActionResult> Purge(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;

        var doc = await Db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { message = "مدرک یافت نشد." });
        if (!doc.IsDeleted)
            return BadRequest(new { message = "فقط مدارک داخل سطل بازیافت قابل حذف قطعی هستند." });

        var versionIds = await Db.DocumentVersions.Where(v => v.DocumentId == id).Select(v => v.Id).ToListAsync();

        // پیوست‌های ورژن‌ها
        var attaches = await Db.AppAttachments
            .Where(a => a.Module == "DocVersion" && versionIds.Contains(a.RefId)).ToListAsync();
        var attachmentIds = attaches.Select(a => a.Id).ToArray();
        Db.DocIndexJobs.RemoveRange(await Db.DocIndexJobs.Where(j => attachmentIds.Contains(j.AttachmentId)).ToListAsync());
        Db.DocTemporaryGrants.RemoveRange(await Db.DocTemporaryGrants.Where(g => g.DocumentId == id).ToListAsync());
        Db.DocRenewalPolicies.RemoveRange(await Db.DocRenewalPolicies.Where(p => p.DocumentId == id).ToListAsync());
        Db.AppAttachments.RemoveRange(attaches);

        Db.DocumentApprovers.RemoveRange(await Db.DocumentApprovers.Where(a => a.DocumentId == id).ToListAsync());
        Db.DocumentVersions.RemoveRange(await Db.DocumentVersions.Where(v => v.DocumentId == id).ToListAsync());
        Db.DocumentPermissions.RemoveRange(await Db.DocumentPermissions.Where(p => p.DocumentId == id).ToListAsync());
        Db.DocumentLinks.RemoveRange(await Db.DocumentLinks
            .Where(l => l.DocumentId == id || l.LinkedDocumentId == id).ToListAsync());
        Db.DocCartableTasks.RemoveRange(await Db.DocCartableTasks
            .Where(t => t.DocumentId == id || t.SourceDocumentId == id).ToListAsync());
        Db.DocExpiryAlerts.RemoveRange(await Db.DocExpiryAlerts.Where(a => a.DocumentId == id).ToListAsync());
        Db.DocumentLogs.RemoveRange(await Db.DocumentLogs.Where(l => l.DocumentId == id).ToListAsync());
        Db.Documents.Remove(doc);

        await Db.SaveChangesAsync();

        // حذف قطعی یعنی فایل فیزیکی روی دیسک سرور هم پاک شود (وگرنه پوشهٔ ذخیره با فایل بی‌صاحب پر می‌شود).
        // فقط بعد از ثبت موفق دیتابیس، و فقط فایلی که هیچ پیوست دیگری به آن اشاره نمی‌کند.
        var store = HttpContext?.RequestServices?.GetService<FileStore>();
        if (store is not null)
        {
            var paths = attaches.Select(a => a.FilePath).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
            var stillUsed = paths.Count == 0 ? new List<string?>() :
                await Db.AppAttachments.Where(a => paths.Contains(a.FilePath)).Select(a => a.FilePath).Distinct().ToListAsync();
            foreach (var path in paths.Except(stillUsed)) store.Delete(path);
        }

        return Ok(new { message = $"مدرک {doc.Code} برای همیشه حذف شد." });
    }

    /// <summary>خالی کردن کامل سطل بازیافت — فقط مدیر ماژول.</summary>
    [HttpDelete("/api/doc-archive/trash")]
    public async Task<IActionResult> EmptyTrash()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;

        var ids = await Db.Documents.Where(d => d.IsDeleted).Select(d => d.Id).ToListAsync();
        foreach (var id in ids) await Purge(id);
        return Ok(new { message = $"{ids.Count} مدرک برای همیشه حذف شد.", count = ids.Count });
    }

    // ---------------------------- انقضا ----------------------------

    /// <summary>شمارش مدارک منقضی‌شده و رو به انقضا (برای بج‌های کنار درخت).</summary>
    [HttpGet("/api/doc-archive/expiry-summary")]
    public async Task<IActionResult> ExpirySummary(int expiringDays = 60)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var today = DateTime.Today;
        var horizon = today.AddDays((double)expiringDays + 1);
        var q = await DocQuery.AccessibleAsync(Db, _access, MyUserId, await IsManagerAsync());
        q = q.Where(d => d.IsActive && !d.IsDeleted && d.ExpireDate != null && d.ExpireDate < horizon);
        var expired = await q.CountAsync(d => d.ExpireDate < today);
        var soon = await q.CountAsync(d => d.ExpireDate >= today);
        return Ok(new { expired, expiringSoon = soon, expiringDays });
    }

    /// <summary>اجرای دستی بررسی انقضا (فقط مدیر ماژول) — برای تست و اجرای فوری.</summary>
    [HttpPost("/api/doc-archive/expiry-check")]
    public async Task<IActionResult> RunExpiryCheck()
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        var n = await _expiry.RunOnceAsync(HttpContext.RequestAborted);
        return Ok(new { created = n, message = n == 0 ? "مدرکی در آستانه انقضا نبود یا قبلاً هشدار داده شده." : $"{n} کار کارتابل ساخته شد." });
    }

    // ---------------------------- کارتابل آرشیو ----------------------------

    [HttpGet("/api/doc-archive/cartable")]
    public async Task<IActionResult> Cartable(bool includeDone = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var q = Db.DocCartableTasks.AsNoTracking().Where(t => t.UserId == MyUserId);
        if (!includeDone) q = q.Where(t => t.Status == 0);

        var rows = await q.OrderByDescending(t => t.Id).ToPageListAsync(pagination, defaultCap: 300);
        var docIds = rows.Select(t => t.DocumentId)
            .Concat(rows.Where(t => t.SourceDocumentId != null).Select(t => t.SourceDocumentId!.Value))
            .Distinct().ToList();

        var docs = await Db.Documents.AsNoTracking().Where(d => docIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => new { d.Code, d.Title });
        var vers = await Db.DocumentVersions.AsNoTracking()
            .Where(v => rows.Select(r => r.VersionId).Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.VersionNo);

        var result = rows.Select(t => new DocCartableItemDto
        {
            Id = t.Id,
            Kind = t.Kind,
            DocumentId = t.DocumentId,
            VersionId = t.VersionId,
            DocumentCode = docs.TryGetValue(t.DocumentId, out var d) ? d.Code : "",
            DocumentTitle = docs.TryGetValue(t.DocumentId, out var d2) ? d2.Title : "",
            VersionNo = t.VersionId is int vid && vers.TryGetValue(vid, out var n) ? n : 0,
            SourceDocumentCode = t.SourceDocumentId is int sid && docs.TryGetValue(sid, out var sd) ? sd.Code : null,
            Title = t.Title,
            Status = t.Status,
            CreatedAt = t.CreatedAt
        }).ToList();

        return Ok(pagination.Result(result));
    }

    /// <summary>بستن دستی یک کار کارتابل (اعلان ورژن / بررسی مدرک مرتبط).</summary>
    [HttpPut("/api/doc-archive/cartable/{id:int}/done")]
    public async Task<IActionResult> CloseTask(int id)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;

        var t = await Db.DocCartableTasks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == MyUserId);
        if (t == null) return NotFound(new { message = "کار یافت نشد." });
        if (t.Kind == "Approval") return BadRequest(new { message = "کار تایید باید از طریق دکمه تایید/رد بسته شود." });

        t.Status = 1; t.DoneAt = DateTime.Now;
        await Db.SaveChangesAsync();
        return Ok();
    }
}
