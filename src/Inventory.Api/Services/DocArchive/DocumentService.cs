using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// منطق ماژول آرشیو اسناد و مدارک: مدرک، ورژن، گردش تایید، لینک مدارک و کارتابل.
/// </summary>
public interface IDocumentService
{
    Task<int> CreateDocumentAsync(DocumentDto dto, int userId, string userName);
    Task UpdateDocumentAsync(int id, DocumentDto dto, int userId, string userName);
    Task<int> CreateVersionAsync(DocVersionCreateDto dto, int userId, string userName);
    Task ActApprovalAsync(DocApprovalActionDto dto, int userId, string userName);
    Task SetVersionActiveAsync(int versionId, bool active, int userId, string userName);
    Task LinkAsync(int documentId, int linkedDocumentId, string? note, int userId, string userName);

    /// <summary>لینک چندتایی — همه مدارک انتخاب‌شده به این مدرک لینک می‌شوند.</summary>
    Task LinkManyAsync(DocLinkSaveDto dto, int userId, string userName);

    Task UnlinkAsync(int documentId, int linkedDocumentId, int userId);

    /// <summary>فعال/غیرفعال کردن مدرک.</summary>
    Task SetDocumentActiveAsync(int id, DocSetActiveDto dto, int userId, string userName);

    /// <summary>اگر مدرک غیرفعال باشد استثنا پرتاب می‌کند.</summary>
    Task EnsureActiveAsync(int documentId);
}

public class DocumentService : IDocumentService
{
    private readonly AppDbContext _db;
    private readonly IDocAccessService _access;
    private readonly INotifyService _notify;

    public DocumentService(AppDbContext db, IDocAccessService access, INotifyService notify)
    {
        _db = db; _access = access; _notify = notify;
    }

    private const string FormName = "آرشیو اسناد و مدارک";

    // ============================ مدرک ============================

    public async Task<int> CreateDocumentAsync(DocumentDto dto, int userId, string userName)
    {
        var code = (dto.Code ?? "").Trim();
        var title = (dto.Title ?? "").Trim();

        if (string.IsNullOrWhiteSpace(title)) throw new Exception("عنوان مدرک اجباری است.");
        if (string.IsNullOrWhiteSpace(code))
        {
            // اگر کد خالی بماند و شماره‌گذار خودکار فعال باشد، کد به‌صورت اتمیک تخصیص می‌یابد
            code = await DocAutoCode.AllocateNextCodeAsync(_db) ?? throw new Exception("کد مدرک اجباری است.");
        }
        if (await _db.Documents.AnyAsync(d => d.Code == code))
            throw new Exception($"کد مدرک «{code}» قبلاً ثبت شده است. کد باید یکتا باشد.");
        if (!await _db.DocFolders.AnyAsync(f => f.Id == dto.FolderId))
            throw new Exception("پوشه انتخاب‌شده معتبر نیست.");

        var doc = new ArchiveDocument
        {
            FolderId = dto.FolderId,
            Title = title,
            Code = code,
            CustomerCode = string.IsNullOrWhiteSpace(dto.CustomerCode) ? null : dto.CustomerCode.Trim(),
            Description = dto.Description?.Trim(),
            ExpireDate = dto.ExpireDate,
            AllowMultipleActiveVersions = dto.AllowMultipleActiveVersions,
            IsPublic = dto.IsPublic,
            PublicCanDownload = dto.PublicCanDownload,
            RequireDownloadConfirm = dto.RequireDownloadConfirm,
            WatermarkPreview = dto.WatermarkPreview,
            CreatedByUserId = userId,
            CreatedByName = userName,
            IsActive = true
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        // نفرات گردش (الگوی مدرک)
        await SaveApproverTemplateAsync(doc.Id, dto.Approvers);

        // دسترسی‌های مستقیم
        await SaveDocPermissionsAsync(doc.Id, dto.Permissions);

        // سازنده همیشه باید یک ردیف «دسترسی کامل» مستقیم در جدول داشته باشد؛
        // بدون آن، فهرست دسترسی‌ها در UI خالی می‌ماند و اولین ذخیرهٔ بعدی دسترسی‌ها
        // (که جایگزین کامل است) می‌تواند عملاً هیچ مدیری را در جدول باقی نگذارد.
        // (اگر سازنده از طریق فهرستِ dto.Permissions ردیف Full مستقیم دارد، ردیف تکراری ساخته نمی‌شود.)
        if (!await _db.DocumentPermissions.AnyAsync(p => p.DocumentId == doc.Id && p.UserId == userId))
        {
            _db.DocumentPermissions.Add(new DocumentPermission
            {
                DocumentId = doc.Id,
                UserId = userId,
                RoleId = 0,
                Level = DocAccessLevel.Full,
                CanDownload = true
            });
            await _db.SaveChangesAsync();
        }

        // ورژن ۱
        var v1 = new DocumentVersion
        {
            DocumentId = doc.Id,
            VersionNo = 1,
            Title = title,
            ChangeNote = "ایجاد اولیه مدرک",
            ExpireDate = dto.ExpireDate,
            CreatedByUserId = userId,
            CreatedByName = userName,
            ActivateOnApprove = true
        };

        var hasFlow = dto.Approvers.Any();
        if (hasFlow)
        {
            // گردش دارد ⇒ فریز و به کارتابل تاییدکنندگان
            v1.Status = DocVersionStatus.InReview;
            v1.IsFrozen = true;
            v1.IsActive = false;
        }
        else
        {
            v1.Status = DocVersionStatus.Approved;
            v1.IsActive = dto.FirstVersionActive;
            v1.ApprovedAt = DateTime.Now;
        }

        _db.DocumentVersions.Add(v1);
        await _db.SaveChangesAsync();

        if (hasFlow)
        {
            // کپی الگوی نفرات گردش روی ورژن ۱
            var tpl = await _db.DocumentApprovers
                .Where(a => a.DocumentId == doc.Id && a.VersionId == null)
                .OrderBy(a => a.Order).ToListAsync();
            foreach (var t in tpl)
                _db.DocumentApprovers.Add(new DocumentApprover
                {
                    DocumentId = doc.Id,
                    VersionId = v1.Id,
                    UserId = t.UserId,
                    UserName = t.UserName,
                    Order = t.Order
                });
            await _db.SaveChangesAsync();

            await StartFlowAsync(doc, v1, userName);
        }

        // مدارک مرتبط انتخاب‌شده در فرم (چندانتخابی)
        var linkIds = dto.LinkedDocumentIds.Where(i => i > 0).Distinct().ToList();
        if (linkIds.Count > 0)
        {
            var targets = await _db.Documents
                .Where(d => linkIds.Contains(d.Id) && d.IsActive && !d.IsDeleted).ToListAsync();
            foreach (var t in targets)
            {
                _db.DocumentLinks.Add(new DocumentLink { DocumentId = doc.Id, LinkedDocumentId = t.Id, CreatedByUserId = userId });
                _db.DocumentLinks.Add(new DocumentLink { DocumentId = t.Id, LinkedDocumentId = doc.Id, CreatedByUserId = userId });
                await LogAsync(t.Id, null, "Link", $"به مدرک {code} لینک شد.", userId, userName);
            }
            if (targets.Count > 0)
                await LogAsync(doc.Id, null, "Link",
                    $"لینک به {targets.Count} مدرک: {string.Join("، ", targets.Select(t => t.Code))}", userId, userName);
        }

        // تگ‌های مدرک
        if (dto.TagIds is { Count: > 0 })
        {
            var validTagIds = await _db.DocTags.Where(t => dto.TagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();
            foreach (var tid in validTagIds)
            {
                _db.DocumentTags.Add(new DocumentTag { DocumentId = doc.Id, TagId = tid });
            }
        }

        await LogAsync(doc.Id, v1.Id, "Create", $"مدرک «{title}» با کد {code} ثبت شد.", userId, userName);
        await _db.SaveChangesAsync();
        return doc.Id;
    }

    public async Task UpdateDocumentAsync(int id, DocumentDto dto, int userId, string userName)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id)
                  ?? throw new Exception("مدرک یافت نشد.");

        var code = (dto.Code ?? "").Trim();
        var title = (dto.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title)) throw new Exception("عنوان مدرک اجباری است.");
        if (string.IsNullOrWhiteSpace(code)) throw new Exception("کد مدرک اجباری است.");
        if (await _db.Documents.AnyAsync(d => d.Code == code && d.Id != id))
            throw new Exception($"کد مدرک «{code}» قبلاً ثبت شده است. کد باید یکتا باشد.");

        if (!doc.IsActive)
            throw new Exception("این مدرک غیرفعال است؛ برای ویرایش ابتدا آن را فعال کنید.");

        doc.Title = title;
        doc.Code = code;
        doc.CustomerCode = string.IsNullOrWhiteSpace(dto.CustomerCode) ? null : dto.CustomerCode.Trim();
        doc.Description = dto.Description?.Trim();
        doc.ExpireDate = dto.ExpireDate;
        doc.FolderId = dto.FolderId;
        doc.IsPublic = dto.IsPublic;
        doc.PublicCanDownload = dto.PublicCanDownload;
        doc.RequireDownloadConfirm = dto.RequireDownloadConfirm;
        doc.WatermarkPreview = dto.WatermarkPreview;
        doc.AllowMultipleActiveVersions = dto.AllowMultipleActiveVersions;

        // اگر از چند-ورژن‌فعال به تک‌ورژن تغییر کرد، فقط آخرین ورژنِ فعال بماند
        if (!doc.AllowMultipleActiveVersions)
        {
            var actives = await _db.DocumentVersions
                .Where(v => v.DocumentId == id && v.IsActive)
                .OrderByDescending(v => v.VersionNo).ToListAsync();
            foreach (var v in actives.Skip(1)) v.IsActive = false;
        }

        await SaveApproverTemplateAsync(id, dto.Approvers);
        await SaveDocPermissionsAsync(id, dto.Permissions);

        // تگ‌های مدرک
        if (dto.TagIds != null)
        {
            var oldTags = await _db.DocumentTags.Where(t => t.DocumentId == id).ToListAsync();
            _db.DocumentTags.RemoveRange(oldTags);
            var validTagIds = await _db.DocTags.Where(t => dto.TagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();
            foreach (var tid in validTagIds)
            {
                _db.DocumentTags.Add(new DocumentTag { DocumentId = id, TagId = tid });
            }
        }

        await LogAsync(id, null, "Update", "اطلاعات مدرک ویرایش شد.", userId, userName);
        await _db.SaveChangesAsync();
    }

    private async Task SaveApproverTemplateAsync(int documentId, List<DocApproverDto> approvers)
    {
        var old = await _db.DocumentApprovers
            .Where(a => a.DocumentId == documentId && a.VersionId == null).ToListAsync();
        _db.DocumentApprovers.RemoveRange(old);

        var order = 0;
        foreach (var a in approvers.Where(x => x.UserId > 0).DistinctBy(x => x.UserId))
        {
            _db.DocumentApprovers.Add(new DocumentApprover
            {
                DocumentId = documentId,
                VersionId = null,
                UserId = a.UserId,
                UserName = a.UserName,
                Order = order++
            });
        }
        await _db.SaveChangesAsync();
    }

    // تخصیص اتمیک کد بعدی به DocAutoCode منتقل شد تا ورود انبوه هم از همان استفاده کند.

    /// <summary>
    /// ذخیره دسترسی‌های اختصاصی مدرک.
    /// اگر <paramref name="items"/> برابر null باشد یعنی «فهرست دسترسی‌ها ارسال نشده» و
    /// ردیف‌های موجود دست‌نخورده باقی می‌مانند (مثلاً کاربری که دسترسی کامل ندارد و
    /// اجازه مدیریت دسترسی ندارد). فهرست خالی (نه null) یعنی حذف همه دسترسی‌ها.
    /// </summary>
    private async Task SaveDocPermissionsAsync(int documentId, List<DocPermissionDto>? items)
    {
        if (items == null) return;

        var old = await _db.DocumentPermissions.Where(p => p.DocumentId == documentId).ToListAsync();
        _db.DocumentPermissions.RemoveRange(old);

        // ردیف فردی (RoleId=0) یا گروهی (RoleId>0) — هر کدام فقط یک‌بار؛ ردیف گروهی UserId=0 دارد
        foreach (var p in items
                     .Where(x => x.RoleId > 0 || x.UserId > 0)
                     .GroupBy(x => x.RoleId > 0 ? $"R:{x.RoleId}" : $"U:{x.UserId}")
                     .Select(g => g.First()))
        {
            _db.DocumentPermissions.Add(new DocumentPermission
            {
                DocumentId = documentId,
                UserId = p.RoleId > 0 ? 0 : p.UserId,
                RoleId = p.RoleId > 0 ? p.RoleId : 0,
                Level = (DocAccessLevel)(int)p.Level,
                CanDownload = p.CanDownload
            });
        }
        await _db.SaveChangesAsync();
    }

    // ============================ ورژن ============================

    public async Task<int> CreateVersionAsync(DocVersionCreateDto dto, int userId, string userName)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == dto.DocumentId)
                  ?? throw new Exception("مدرک یافت نشد.");

        if (!doc.IsActive)
            throw new Exception("این مدرک غیرفعال است؛ ثبت ورژن جدید روی آن ممکن نیست.");

        // اگر ورژنی در حال گردش است، ورژن جدید ممنوع
        if (await _db.DocumentVersions.AnyAsync(v => v.DocumentId == doc.Id && v.Status == DocVersionStatus.InReview))
            throw new Exception("یک ورژن در حال گردش تایید است؛ تا پایان گردش، ورژن جدید ثبت نمی‌شود.");

        var lastNo = await _db.DocumentVersions.Where(v => v.DocumentId == doc.Id)
            .MaxAsync(v => (int?)v.VersionNo) ?? 0;

        var ver = new DocumentVersion
        {
            DocumentId = doc.Id,
            VersionNo = lastNo + 1,
            Title = string.IsNullOrWhiteSpace(dto.Title) ? doc.Title : dto.Title!.Trim(),
            ChangeNote = dto.ChangeNote?.Trim(),
            ExpireDate = dto.ExpireDate ?? doc.ExpireDate,
            ActivateOnApprove = dto.ActivateOnApprove,
            CreatedByUserId = userId,
            CreatedByName = userName
        };

        // نفرات گردش: ورودی فرم، وگرنه الگوی مدرک
        var approverIds = dto.ApproverUserIds.Where(i => i > 0).Distinct().ToList();
        if (approverIds.Count == 0)
            approverIds = await _db.DocumentApprovers
                .Where(a => a.DocumentId == doc.Id && a.VersionId == null)
                .OrderBy(a => a.Order).Select(a => a.UserId).ToListAsync();

        var hasFlow = approverIds.Count > 0 && dto.SendToFlow;

        if (hasFlow)
        {
            // فریز — تا تایید نشود به گردش نمی‌آید و فعال نیست
            ver.Status = DocVersionStatus.InReview;
            ver.IsFrozen = true;
            ver.IsActive = false;
        }
        else
        {
            ver.Status = DocVersionStatus.Approved;
            ver.ApprovedAt = DateTime.Now;
            ver.IsActive = dto.ActivateOnApprove;
        }

        _db.DocumentVersions.Add(ver);
        await _db.SaveChangesAsync();

        if (hasFlow)
        {
            var users = await _db.Users.Where(u => approverIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : (u.FirstName + " " + u.LastName).Trim() }).ToListAsync();
            var order = 0;
            foreach (var uid in approverIds)
            {
                var u = users.FirstOrDefault(x => x.Id == uid);
                _db.DocumentApprovers.Add(new DocumentApprover
                {
                    DocumentId = doc.Id,
                    VersionId = ver.Id,
                    UserId = uid,
                    UserName = u?.Name ?? "",
                    Order = order++
                });
            }
            await _db.SaveChangesAsync();
            await StartFlowAsync(doc, ver, userName);
        }
        else if (ver.IsActive)
        {
            await ApplyActivationPolicyAsync(doc, ver);
        }

        await LogAsync(doc.Id, ver.Id, "NewVersion",
            $"ورژن {ver.VersionNo} ثبت شد." + (hasFlow ? " (در گردش تایید — فریز)" : ""), userId, userName);
        await _db.SaveChangesAsync();

        // کار در کارتابل دارندگان دسترسی کامل + مدارک مرتبط
        await NotifyVersionCreatedAsync(doc, ver, userId, userName);

        return ver.Id;
    }

    /// <summary>شروع گردش: کار در کارتابل تاییدکنندگان + اعلان.</summary>
    private async Task StartFlowAsync(ArchiveDocument doc, DocumentVersion ver, string actorName)
    {
        var approvers = await _db.DocumentApprovers
            .Where(a => a.DocumentId == doc.Id && a.VersionId == ver.Id).ToListAsync();

        foreach (var a in approvers)
        {
            _db.DocCartableTasks.Add(new DocCartableTask
            {
                Kind = "Approval",
                UserId = a.UserId,
                DocumentId = doc.Id,
                VersionId = ver.Id,
                Title = $"تایید ورژن {ver.VersionNo} مدرک {doc.Code} — {doc.Title}"
            });
        }
        await _db.SaveChangesAsync();

        await _notify.SendManyAsync(approvers.Select(a => a.UserId).ToList(),
            $"تایید مدرک {doc.Code}",
            $"ورژن {ver.VersionNo} مدرک «{doc.Title}» در انتظار تایید شماست.",
            actorName, FormName, $"/doc-archive/documents/{doc.Id}");
    }

    /// <summary>ورژن خورد ⇒ کار در کارتابل دارندگان دسترسی کامل + به‌روزرسانی مدارک مرتبط.</summary>
    private async Task NotifyVersionCreatedAsync(ArchiveDocument doc, DocumentVersion ver, int userId, string userName)
    {
        var fullUsers = (await _access.UsersWithFullAccessAsync(doc.Id))
            .Where(u => u != userId).ToList();

        foreach (var uid in fullUsers)
        {
            _db.DocCartableTasks.Add(new DocCartableTask
            {
                Kind = "VersionNotice",
                UserId = uid,
                DocumentId = doc.Id,
                VersionId = ver.Id,
                Title = $"ورژن {ver.VersionNo} مدرک {doc.Code} ثبت شد — {doc.Title}"
            });
        }

        // مدارک مرتبط: صاحبان دسترسی کامل باید بررسی/به‌روزرسانی کنند
        var linked = await _db.DocumentLinks.Where(l => l.DocumentId == doc.Id)
            .Select(l => l.LinkedDocumentId).ToListAsync();

        foreach (var lid in linked)
        {
            var lDoc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == lid);
            if (lDoc == null || !lDoc.IsActive || lDoc.IsDeleted) continue; // مدرک غیرفعال کار نمی‌گیرد
            var owners = (await _access.UsersWithFullAccessAsync(lid)).Distinct();
            foreach (var uid in owners)
            {
                _db.DocCartableTasks.Add(new DocCartableTask
                {
                    Kind = "RelatedUpdate",
                    UserId = uid,
                    DocumentId = lid,
                    SourceDocumentId = doc.Id,
                    Title = $"مدرک مرتبط {doc.Code} ورژن خورد — بررسی/به‌روزرسانی مدرک {lDoc.Code}"
                });
            }
            await LogAsync(lid, null, "RelatedUpdate",
                $"مدرک مرتبط {doc.Code} به ورژن {ver.VersionNo} رسید.", userId, userName);
        }

        await _db.SaveChangesAsync();

        if (fullUsers.Count > 0)
            await _notify.SendManyAsync(fullUsers,
                $"ورژن جدید مدرک {doc.Code}",
                $"ورژن {ver.VersionNo} مدرک «{doc.Title}» ثبت شد.",
                userName, FormName, $"/doc-archive/documents/{doc.Id}");
    }

    // ============================ تایید / رد ============================

    public async Task ActApprovalAsync(DocApprovalActionDto dto, int userId, string userName)
    {
        var ver = await _db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == dto.VersionId)
                  ?? throw new Exception("ورژن یافت نشد.");
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == ver.DocumentId)
                  ?? throw new Exception("مدرک یافت نشد.");

        if (!doc.IsActive)
            throw new Exception("این مدرک غیرفعال است؛ گردش تایید روی آن انجام نمی‌شود.");

        var mine = await _db.DocumentApprovers
            .FirstOrDefaultAsync(a => a.VersionId == ver.Id && a.UserId == userId)
            ?? throw new Exception("شما در گردش این ورژن نیستید.");

        if (mine.Status != 0) throw new Exception("شما قبلاً نظر خود را ثبت کرده‌اید.");
        if (ver.Status != DocVersionStatus.InReview) throw new Exception("این ورژن در گردش تایید نیست.");

        mine.Status = dto.Approve ? 1 : 2;
        mine.Comment = dto.Comment?.Trim();
        mine.ActedAt = DateTime.Now;
        // امضای PWA آفلاین + بیومتریک
        if (!string.IsNullOrWhiteSpace(dto.SignatureDataUrl) && dto.SignatureDataUrl.Length < 500_000)
            mine.SignatureDataUrl = dto.SignatureDataUrl;
        mine.BiometricVerified = dto.BiometricVerified;
        if (!string.IsNullOrWhiteSpace(dto.DeviceInfo)) mine.DeviceInfo = dto.DeviceInfo.Length > 300 ? dto.DeviceInfo[..300] : dto.DeviceInfo;
        if (dto.ClientTimestamp.HasValue && (DateTime.Now - dto.ClientTimestamp.Value).Duration().TotalMinutes > 2)
        {
            // همگام‌سازی آفلاین — زمان کلاینت را در لاگ ثبت کن
            await LogAsync(doc.Id, ver.Id, "OfflineSync", $"اقدام آفلاین در {dto.ClientTimestamp:yyyy/MM/dd HH:mm} همگام شد. {(dto.BiometricVerified ? "بیومتریک تایید" : "")} {dto.DeviceInfo}", userId, userName);
        }

        // بستن کار کارتابل خودش
        var task = await _db.DocCartableTasks
            .FirstOrDefaultAsync(t => t.Kind == "Approval" && t.VersionId == ver.Id && t.UserId == userId && t.Status == 0);
        if (task != null) { task.Status = dto.Approve ? 1 : 2; task.DoneAt = DateTime.Now; }

        await _db.SaveChangesAsync();

        var all = await _db.DocumentApprovers.Where(a => a.VersionId == ver.Id).ToListAsync();

        if (!dto.Approve)
        {
            // رد ⇒ ورژن رد و از فریز خارج (قابل اصلاح و ارسال مجدد)
            ver.Status = DocVersionStatus.Rejected;
            ver.IsFrozen = false;
            ver.IsActive = false;

            foreach (var t in _db.DocCartableTasks.Where(t => t.VersionId == ver.Id && t.Status == 0))
            { t.Status = 2; t.DoneAt = DateTime.Now; }

            await LogAsync(doc.Id, ver.Id, "Reject", $"ورژن {ver.VersionNo} توسط {userName} رد شد. {mine.Comment}", userId, userName);
            await _db.SaveChangesAsync();

            await _notify.SendAsync(ver.CreatedByUserId,
                $"رد ورژن مدرک {doc.Code}",
                $"ورژن {ver.VersionNo} توسط {userName} رد شد.",
                userName, FormName, $"/doc-archive/documents/{doc.Id}");
            return;
        }

        await LogAsync(doc.Id, ver.Id, "Approve", $"ورژن {ver.VersionNo} توسط {userName} تایید شد.", userId, userName);

        if (all.All(a => a.Status == 1))
        {
            // همه تایید کردند ⇒ آزادسازی از فریز و فعال‌سازی خودکار
            ver.Status = DocVersionStatus.Approved;
            ver.IsFrozen = false;
            ver.ApprovedAt = DateTime.Now;

            if (ver.ActivateOnApprove)
            {
                ver.IsActive = true;
                await ApplyActivationPolicyAsync(doc, ver);
            }

            await LogAsync(doc.Id, ver.Id, "Approved",
                $"گردش کامل شد؛ ورژن {ver.VersionNo}" + (ver.IsActive ? " فعال شد." : " تایید شد (غیرفعال)."),
                userId, userName);
            await _db.SaveChangesAsync();

            var owners = (await _access.UsersWithFullAccessAsync(doc.Id)).ToList();
            await _notify.SendManyAsync(owners,
                $"تایید نهایی مدرک {doc.Code}",
                $"ورژن {ver.VersionNo} مدرک «{doc.Title}» تایید و فعال شد.",
                userName, FormName, $"/doc-archive/documents/{doc.Id}");
        }
        else
        {
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>اعمال سیاست فعال بودن ورژن‌ها (تک‌ورژن یا چند ورژن هم‌زمان).</summary>
    private async Task ApplyActivationPolicyAsync(ArchiveDocument doc, DocumentVersion active)
    {
        if (doc.AllowMultipleActiveVersions) return;

        var others = await _db.DocumentVersions
            .Where(v => v.DocumentId == doc.Id && v.Id != active.Id && v.IsActive).ToListAsync();
        foreach (var v in others) v.IsActive = false;
        await _db.SaveChangesAsync();
    }

    public async Task SetVersionActiveAsync(int versionId, bool active, int userId, string userName)
    {
        var ver = await _db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == versionId)
                  ?? throw new Exception("ورژن یافت نشد.");
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == ver.DocumentId)!
                  ?? throw new Exception("مدرک یافت نشد.");

        if (!doc.IsActive)
            throw new Exception("این مدرک غیرفعال است؛ تغییر وضعیت ورژن ممکن نیست.");

        if (active && ver.IsFrozen)
            throw new Exception("این ورژن فریز و در گردش تایید است؛ تا پایان تایید فعال نمی‌شود.");
        if (active && ver.Status == DocVersionStatus.Rejected)
            throw new Exception("ورژن رد شده قابل فعال‌سازی نیست.");

        ver.IsActive = active;
        if (active) await ApplyActivationPolicyAsync(doc, ver);

        await LogAsync(doc.Id, ver.Id, active ? "Activate" : "Deactivate",
            $"ورژن {ver.VersionNo} {(active ? "فعال" : "غیرفعال")} شد.", userId, userName);
        await _db.SaveChangesAsync();
    }

    // ============================ لینک مدارک ============================

    public async Task LinkAsync(int documentId, int linkedDocumentId, string? note, int userId, string userName)
    {
        if (documentId == linkedDocumentId) throw new Exception("مدرک را نمی‌توان به خودش لینک کرد.");
        var a = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId) ?? throw new Exception("مدرک یافت نشد.");
        var b = await _db.Documents.FirstOrDefaultAsync(d => d.Id == linkedDocumentId) ?? throw new Exception("مدرک مقصد یافت نشد.");

        if (!a.IsActive) throw new Exception("این مدرک غیرفعال است؛ امکان لینک کردن نیست.");
        if (!b.IsActive) throw new Exception($"مدرک «{b.Code}» غیرفعال است و قابل لینک شدن نیست.");

        if (await _db.DocumentLinks.AnyAsync(l => l.DocumentId == documentId && l.LinkedDocumentId == linkedDocumentId))
            throw new Exception("این دو مدرک قبلاً لینک شده‌اند.");

        _db.DocumentLinks.Add(new DocumentLink { DocumentId = documentId, LinkedDocumentId = linkedDocumentId, Note = note?.Trim(), CreatedByUserId = userId });
        _db.DocumentLinks.Add(new DocumentLink { DocumentId = linkedDocumentId, LinkedDocumentId = documentId, Note = note?.Trim(), CreatedByUserId = userId });

        await LogAsync(documentId, null, "Link", $"به مدرک {b.Code} لینک شد.", userId, userName);
        await LogAsync(linkedDocumentId, null, "Link", $"به مدرک {a.Code} لینک شد.", userId, userName);
        await _db.SaveChangesAsync();
    }

    /// <summary>لینک چندتایی — همه مدارک انتخاب‌شده به این مدرک لینک می‌شوند (تکراری‌ها نادیده گرفته می‌شوند).</summary>
    public async Task LinkManyAsync(DocLinkSaveDto dto, int userId, string userName)
    {
        var a = await _db.Documents.FirstOrDefaultAsync(d => d.Id == dto.DocumentId)
                ?? throw new Exception("مدرک یافت نشد.");
        if (!a.IsActive) throw new Exception("این مدرک غیرفعال است؛ امکان لینک کردن نیست.");

        var ids = dto.LinkedDocumentIds.Where(i => i > 0 && i != dto.DocumentId).Distinct().ToList();
        if (ids.Count == 0) throw new Exception("حداقل یک مدرک برای لینک انتخاب کنید.");

        var targets = await _db.Documents.Where(d => ids.Contains(d.Id) && !d.IsDeleted).ToListAsync();

        var inactive = targets.Where(t => !t.IsActive).Select(t => t.Code).ToList();
        if (inactive.Count > 0)
            throw new Exception($"مدارک غیرفعال قابل لینک شدن نیستند: {string.Join("، ", inactive)}");

        var existing = await _db.DocumentLinks
            .Where(l => l.DocumentId == dto.DocumentId && ids.Contains(l.LinkedDocumentId))
            .Select(l => l.LinkedDocumentId).ToListAsync();

        var added = 0;
        foreach (var t in targets.Where(t => !existing.Contains(t.Id)))
        {
            _db.DocumentLinks.Add(new DocumentLink
            { DocumentId = a.Id, LinkedDocumentId = t.Id, Note = dto.Note?.Trim(), CreatedByUserId = userId });
            _db.DocumentLinks.Add(new DocumentLink
            { DocumentId = t.Id, LinkedDocumentId = a.Id, Note = dto.Note?.Trim(), CreatedByUserId = userId });

            await LogAsync(a.Id, null, "Link", $"به مدرک {t.Code} لینک شد.", userId, userName);
            await LogAsync(t.Id, null, "Link", $"به مدرک {a.Code} لینک شد.", userId, userName);
            added++;
        }

        if (added == 0) throw new Exception("همه مدارک انتخاب‌شده قبلاً لینک شده‌اند.");
        await _db.SaveChangesAsync();
    }

    // ============================ فعال / غیرفعال کردن مدرک ============================

    public async Task SetDocumentActiveAsync(int id, DocSetActiveDto dto, int userId, string userName)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id)
                  ?? throw new Exception("مدرک یافت نشد.");

        if (doc.IsActive == dto.IsActive)
            throw new Exception(dto.IsActive ? "این مدرک هم‌اکنون فعال است." : "این مدرک هم‌اکنون غیرفعال است.");

        if (!dto.IsActive)
        {
            // غیرفعال‌سازی: نباید ورژنی در گردش باز باشد
            if (await _db.DocumentVersions.AnyAsync(v => v.DocumentId == id && v.Status == DocVersionStatus.InReview))
                throw new Exception("ورژنی از این مدرک در گردش تایید است؛ ابتدا گردش را تعیین تکلیف کنید.");

            doc.IsActive = false;
            doc.DeactivatedAt = DateTime.Now;
            doc.DeactivatedByName = userName;
            doc.DeactivateReason = dto.Reason?.Trim();

            // بستن کارهای باز کارتابل مربوط به این مدرک
            foreach (var t in _db.DocCartableTasks.Where(t => t.DocumentId == id && t.Status == 0))
            { t.Status = 1; t.DoneAt = DateTime.Now; }

            await LogAsync(id, null, "Deactivate",
                $"مدرک غیرفعال شد." + (string.IsNullOrWhiteSpace(dto.Reason) ? "" : $" دلیل: {dto.Reason}"),
                userId, userName);
        }
        else
        {
            doc.IsActive = true;
            doc.DeactivatedAt = null;
            doc.DeactivatedByName = null;
            doc.DeactivateReason = null;
            await LogAsync(id, null, "Activate", "مدرک دوباره فعال شد.", userId, userName);
        }

        await _db.SaveChangesAsync();

        // اطلاع به دارندگان دسترسی کامل
        var owners = (await _access.UsersWithFullAccessAsync(id)).Where(u => u != userId).ToList();
        if (owners.Count > 0)
            await _notify.SendManyAsync(owners,
                dto.IsActive ? $"فعال‌سازی مدرک {doc.Code}" : $"غیرفعال‌سازی مدرک {doc.Code}",
                $"مدرک «{doc.Title}» {(dto.IsActive ? "فعال" : "غیرفعال")} شد.",
                userName, FormName, $"/doc-archive/documents/{doc.Id}");
    }

    public async Task EnsureActiveAsync(int documentId)
    {
        var active = await _db.Documents.Where(d => d.Id == documentId).Select(d => d.IsActive).FirstOrDefaultAsync();
        if (!active) throw new Exception("این مدرک غیرفعال است و امکان انجام این عملیات روی آن وجود ندارد.");
    }

    public async Task UnlinkAsync(int documentId, int linkedDocumentId, int userId)
    {
        var rows = await _db.DocumentLinks.Where(l =>
            (l.DocumentId == documentId && l.LinkedDocumentId == linkedDocumentId) ||
            (l.DocumentId == linkedDocumentId && l.LinkedDocumentId == documentId)).ToListAsync();
        _db.DocumentLinks.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }

    // ============================ لاگ ============================

    private Task LogAsync(int documentId, int? versionId, string action, string detail, int userId, string userName)
    {
        _db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = documentId,
            VersionId = versionId,
            Action = action,
            Detail = detail,
            UserId = userId,
            UserName = userName
        });
        return Task.CompletedTask;
    }
}
