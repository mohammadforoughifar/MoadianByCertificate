using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;
namespace Inventory.Api.Controllers.DocArchive;

[Route("api/doc-archive")]
public class DocEvolutionController(AppDbContext db, IDocAccessService access, DocRenewalService renewal) : RbacControllerBase(db)
{
    private async Task<bool> Full(int id) => await Db.Documents.AnyAsync(d => d.Id == id && !d.IsDeleted) &&
        (await access.DocumentAccessAsync(MyUserId, await HasAsync("DocArchive", "Manage"), id)).Level >= DocAccessLevel.Full;
    private void Log(int id, string action, string detail) => Db.DocumentLogs.Add(new DocumentLog { DocumentId = id, UserId = MyUserId, UserName = MyUsername, Action = action, Detail = detail });

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        if (await ForbiddenUnlessDocArchiveAsync("DocArchive", "Read") is { } denied) return denied;
        var q = await DocQuery.AccessibleAsync(Db, access, MyUserId, await HasAsync("DocArchive", "Manage"));
        var today = DocClock.Today; var horizon = today.AddDays(61);
        return Ok(new DocArchiveStatsDto
        {
            Inactive = await q.CountAsync(d => !d.IsDeleted && !d.IsActive),
            Deleted = await q.CountAsync(d => d.IsDeleted),
            Expired = await q.CountAsync(d => !d.IsDeleted && d.IsActive && d.ExpireDate < today),
            Expiring = await q.CountAsync(d => !d.IsDeleted && d.IsActive && d.ExpireDate >= today && d.ExpireDate < horizon)
        });
    }
    [HttpGet("documents/{id:int}/temporary-grants")]
    public async Task<IActionResult> Grants(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await Full(id)) return Forbid();
        return Ok(await Paging.ResultAsync((from g in Db.DocTemporaryGrants.AsNoTracking()
                         join u in Db.Users on g.UserId equals u.Id
                         where g.DocumentId == id
                         orderby g.ExpiresAtUtc descending
                         select new DocTemporaryGrantDto { Id = g.Id, UserId = g.UserId, UserName = u.Username, ExpiresAtUtc = DateTime.SpecifyKind(g.ExpiresAtUtc, DateTimeKind.Utc), RevokedAtUtc = g.RevokedAtUtc, CanDownload = g.CanDownload }), skip, take));
    }
    [HttpPost("documents/{id:int}/temporary-grants")]
    public async Task<IActionResult> Grant(int id, DocTemporaryGrantDto dto)
    {
        if (!await Full(id)) return Forbid();
        if (dto.ExpiresAtUtc.Kind != DateTimeKind.Utc || dto.ExpiresAtUtc <= DateTime.UtcNow || dto.ExpiresAtUtc > DateTime.UtcNow.AddYears(1))
            return BadRequest(new { message = "پایان دسترسی باید زمانی در آینده و حداکثر یک سال، با منطقه زمانی UTC باشد." });
        if (!await Db.Users.AnyAsync(u => u.Id == dto.UserId && u.IsActive)) return BadRequest(new { message = "کاربر فعال را انتخاب کنید." });
        await using var tx = await Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var grant = await Db.DocTemporaryGrants.FirstOrDefaultAsync(g => g.DocumentId == id && g.UserId == dto.UserId);
        if (grant == null) { grant = new DocTemporaryGrant { DocumentId = id, UserId = dto.UserId }; Db.DocTemporaryGrants.Add(grant); }
        grant.ExpiresAtUtc = dto.ExpiresAtUtc; grant.RevokedAtUtc = null; grant.CanDownload = dto.CanDownload;
        grant.GrantedByUserId = MyUserId; grant.CreatedAtUtc = DateTime.UtcNow;
        Log(id, "TemporaryAccess", $"کاربر {dto.UserId}؛ پایان UTC: {dto.ExpiresAtUtc:O}؛ دانلود: {dto.CanDownload}");
        await Db.SaveChangesAsync(); await tx.CommitAsync();
        return Ok(new { grant.Id, url = $"/doc-archive/documents/{id}" });
    }
    [HttpDelete("documents/{id:int}/temporary-grants/{grantId:int}")]
    public async Task<IActionResult> Revoke(int id, int grantId)
    {
        if (!await Full(id)) return Forbid();
        var grant = await Db.DocTemporaryGrants.SingleOrDefaultAsync(g => g.Id == grantId && g.DocumentId == id);
        if (grant == null) return NotFound();
        if (grant.RevokedAtUtc == null) { grant.RevokedAtUtc = DateTime.UtcNow; Log(id, "TemporaryAccessRevoked", $"کاربر {grant.UserId}"); await Db.SaveChangesAsync(); }
        return Ok(new { message = "دسترسی موقت لغو شد؛ دسترسی‌های دائمی مستقل باقی می‌مانند." });
    }
    [HttpGet("documents/{id:int}/renewal")]
    public async Task<IActionResult> GetRenewal(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await Full(id)) return Forbid();
        var p = await Db.DocRenewalPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.DocumentId == id);
        var result = new DocRenewalPolicyDto { Enabled = p?.Enabled ?? false, AssigneeUserId = p?.AssigneeUserId ?? 0, LeadDays = p?.LeadDays ?? 30 };
        var pagination = new Paging.Request(skip, take);
        result.Orders = await (from r in Db.DocRenewalRuns
                               join w in Db.WorkOrders.IgnoreQueryFilters() on r.WorkOrderId equals w.Id
                               where r.DocumentId == id && (w.OwnerUserId == MyUserId || Db.WorkOrderAssignees.Any(a => a.OrderId == w.Id && a.UserId == MyUserId))
                               orderby r.Id descending
                               select new DocRenewalOrderDto { Id = w.Id, Number = w.Number, Status = w.DeletedAt != null ? "Deleted" : w.Status, ExpiryDate = r.ExpiryDate }).ToPageListAsync(pagination, defaultCap: 20);
        if (Paging.Requested(skip, take))
            return Ok(new { result.Enabled, result.AssigneeUserId, result.LeadDays, total = pagination.Total, orders = result.Orders });
        return Ok(result);
    }
    [HttpPut("documents/{id:int}/renewal")]
    public async Task<IActionResult> SetRenewal(int id, DocRenewalPolicyDto dto)
    {
        if (!await Full(id)) return Forbid();
        if (dto.LeadDays is < 0 or > 365) return BadRequest(new { message = "مهلت بین صفر تا ۳۶۵ روز باشد." });
        if (dto.Enabled)
        {
            if (!await Db.Documents.AnyAsync(d => d.Id == id && d.IsActive && d.ExpireDate != null)) return BadRequest(new { message = "مدرک فعال با تاریخ انقضا لازم است." });
            if (!await renewal.CanAssignAsync(MyUserId, dto.AssigneeUserId)) return BadRequest(new { message = "مجوز ایجاد دستور کار و ارجاع به این مسئول را ندارید." });
            var manager = await renewal.HasPermissionAsync(dto.AssigneeUserId, "DocArchive", "Manage");
            if ((await access.DocumentAccessAsync(dto.AssigneeUserId, manager, id)).Level < DocAccessLevel.Read) return BadRequest(new { message = "مسئول باید دسترسی خواندن مدرک را داشته باشد." });
        }
        var row = await Db.DocRenewalPolicies.FindAsync(id);
        if (row == null) { row = new DocRenewalPolicy { DocumentId = id }; Db.DocRenewalPolicies.Add(row); }
        row.Enabled = dto.Enabled; row.AssigneeUserId = dto.AssigneeUserId; row.LeadDays = dto.LeadDays; row.OwnerUserId = MyUserId;
        Log(id, "RenewalPolicy", $"فعال: {dto.Enabled}؛ مسئول: {dto.AssigneeUserId}؛ {dto.LeadDays} روز قبل");
        await Db.SaveChangesAsync();
        return Ok(new { message = "تنظیمات تمدید ذخیره شد." });
    }
    [HttpGet("index-queue")]
    public async Task<IActionResult> Queue([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await HasAsync("DocArchive", "Manage")) return Forbid();
        var q = Db.DocIndexJobs.AsNoTracking();
        var pagination = new Paging.Request(skip, take);
        var dto = new DocIndexQueueDto
        {
            Pending = await q.CountAsync(j => j.Status == "Pending"),
            Working = await q.CountAsync(j => j.Status == "Working"),
            Failed = await q.CountAsync(j => j.Status == "Failed"),
            Done = await q.CountAsync(j => j.Status == "Done"),
            Failures = await (from j in q
                              join a in Db.AppAttachments on j.AttachmentId equals a.Id
                              where j.Status == "Failed"
                              orderby j.UpdatedAtUtc descending, j.AttachmentId descending
                              select new DocIndexFailureDto { AttachmentId = j.AttachmentId, FileName = a.FileName, Error = j.Error }).ToPageListAsync(pagination, defaultCap: 30)
        };
        if (Paging.Requested(skip, take))
            return Ok(new { dto.Pending, dto.Working, dto.Failed, dto.Done, total = pagination.Total, failures = dto.Failures });
        return Ok(dto);
    }
    [HttpPost("index-queue/{attachmentId:int}/retry")]
    public async Task<IActionResult> Retry(int attachmentId)
    {
        if (!await HasAsync("DocArchive", "Manage")) return Forbid();
        if (!await Db.AppAttachments.AnyAsync(a => a.Id == attachmentId && a.Module == "DocVersion")) return NotFound();
        await DocIndexQueue.EnqueueAsync(Db, attachmentId); return Ok(new { message = "در صف قرار گرفت." });
    }
}
