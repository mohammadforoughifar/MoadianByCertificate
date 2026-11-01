using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.DocArchive;

/// <summary>
/// ================== رسیدگی به درخواست دسترسی به مدارک ==================
/// پذیرش/رد توسط مدیران مدرک (دسترسی کامل) — لغو توسط خودِ درخواست‌کننده.
/// پذیرش ⇒ افزودن/به‌روزرسانی ردیف DocumentPermission برای کاربر درخواست‌کننده + نوتیف.
/// </summary>
[ApiController]
[Route("api/doc-archive/access-requests")]
public class DocAccessRequestsController : RbacControllerBase
{
    private readonly IDocAccessService _access;
    private readonly INotifyService _notify;
    private const string Mod = "DocArchive";
    private const string FormName = "آرشیو اسناد و مدارک";

    public DocAccessRequestsController(AppDbContext db, IDocAccessService access, INotifyService notify) : base(db)
    { _access = access; _notify = notify; }

    private async Task<(DocAccessRequest? Req, IActionResult? Err)> LoadPendingAsync(int requestId, bool requireFull)
    {
        var req = await Db.DocAccessRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return (null, NotFound(new { message = "درخواست یافت نشد." }));
        if (req.Status != DocAccessRequestStatus.Pending)
            return (null, Conflict(new { message = "این درخواست قبلاً رسیدگی شده است." }));
        if (requireFull)
        {
            var manager = await HasAsync(Mod, "Manage");
            var (lvl, _) = await _access.DocumentAccessAsync(MyUserId, manager, req.DocumentId);
            if (lvl < DocAccessLevel.Full)
                return (null, StatusCode(403, new { message = "رسیدگی به درخواست دسترسی نیازمند دسترسی کامل به مدرک است." }));
        }
        return (req, null);
    }

    /// <summary>پذیرش درخواست — اعطای سطح دسترسی انتخابی به درخواست‌کننده.</summary>
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, [FromBody] DocAccessRequestApproveDto dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        var (req, err) = await LoadPendingAsync(id, requireFull: true);
        if (err != null || req == null) return err!;

        var me = await Db.Users.AsNoTracking().Where(u => u.Id == MyUserId)
            .Select(u => string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : (u.FirstName + " " + u.LastName).Trim())
            .FirstOrDefaultAsync() ?? MyUsername;

        // مجوز فردی جایگزین قبلی و مقدم بر مجوز گروه/والد است؛ دانلود جداگانه تعیین می‌شود.
        var old = await Db.DocumentPermissions
            .Where(p => p.DocumentId == req.DocumentId && p.UserId == req.RequesterUserId && p.RoleId == 0)
            .ToListAsync();
        Db.DocumentPermissions.RemoveRange(old);
        Db.DocumentPermissions.Add(new DocumentPermission
        {
            DocumentId = req.DocumentId,
            UserId = req.RequesterUserId,
            RoleId = 0,
            Level = (DocAccessLevel)(int)dto.Level,
            CanDownload = dto.CanDownload
        });

        req.Status = DocAccessRequestStatus.Approved;
        req.HandledByUserId = MyUserId;
        req.HandledByName = me;
        req.HandledAt = DateTime.Now;

        var docCode = await Db.Documents.Where(d => d.Id == req.DocumentId)
            .Select(d => d.Code + " — " + d.Title).FirstOrDefaultAsync() ?? req.DocumentId.ToString();

        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = req.DocumentId,
            Action = "AccessRequestApproved",
            Detail = $"درخواست دسترسی «{req.RequesterName}» توسط «{me}» پذیرفته شد — سطح: {DocumentsController.LevelFa((DocAccessLevel)(int)dto.Level)}" +
                     (dto.CanDownload ? " (با دانلود)" : " (بدون دانلود)"),
            UserId = MyUserId, UserName = MyUsername
        });
        await Db.SaveChangesAsync();

        try
        {
            await _notify.SendAsync(req.RequesterUserId,
                "درخواست دسترسی شما پذیرفته شد",
                $"دسترسی «{DocumentsController.LevelFa((DocAccessLevel)(int)dto.Level)}» به مدرک {docCode} برای شما داده شد.",
                me, FormName, $"/doc-archive/documents/{req.DocumentId}");
        }
        catch { /* خطای نوتیف نباید جریان را متوقف کند */ }

        return Ok();
    }

    /// <summary>رد درخواست — با توضیح (اختیاری) برای درخواست‌کننده.</summary>
    public class RejectRequest { public string? Note { get; set; } }

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectRequest dto)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        var (req, err) = await LoadPendingAsync(id, requireFull: true);
        if (err != null || req == null) return err!;

        var me = await Db.Users.AsNoTracking().Where(u => u.Id == MyUserId)
            .Select(u => string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : (u.FirstName + " " + u.LastName).Trim())
            .FirstOrDefaultAsync() ?? MyUsername;

        req.Status = DocAccessRequestStatus.Rejected;
        req.HandlerNote = dto.Note?.Trim();
        req.HandledByUserId = MyUserId;
        req.HandledByName = me;
        req.HandledAt = DateTime.Now;

        var docCode = await Db.Documents.Where(d => d.Id == req.DocumentId)
            .Select(d => d.Code + " — " + d.Title).FirstOrDefaultAsync() ?? req.DocumentId.ToString();

        Db.DocumentLogs.Add(new DocumentLog
        {
            DocumentId = req.DocumentId,
            Action = "AccessRequestRejected",
            Detail = $"درخواست دسترسی «{req.RequesterName}» توسط «{me}» رد شد.",
            UserId = MyUserId, UserName = MyUsername
        });
        await Db.SaveChangesAsync();

        try
        {
            await _notify.SendAsync(req.RequesterUserId,
                "درخواست دسترسی شما رد شد",
                $"درخواست دسترسی به مدرک {docCode} پذیرفته نشد." +
                (string.IsNullOrWhiteSpace(req.HandlerNote) ? "" : $" دلیل: {req.HandlerNote}"),
                me, FormName, null);
        }
        catch { /* خطای نوتیف نباید جریان را متوقف کند */ }

        return Ok();
    }

    /// <summary>لغو درخواست توسط خودِ درخواست‌کننده (فقط در حالت درانتظار).</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        if (await ForbiddenUnlessDocArchiveAsync(Mod, "Read") is { } f) return f;
        var (req, err) = await LoadPendingAsync(id, requireFull: false);
        if (err != null || req == null) return err!;
        if (req.RequesterUserId != MyUserId)
            return StatusCode(403, new { message = "فقط خودِ درخواست‌کننده می‌تواند درخواست را لغو کند." });

        req.Status = DocAccessRequestStatus.Canceled;
        req.HandledAt = DateTime.Now;
        await Db.SaveChangesAsync();
        return Ok();
    }
}
