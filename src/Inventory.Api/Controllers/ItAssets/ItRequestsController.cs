using System.Globalization;
using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>درخواست خدمت از واحد آی‌تی — ثبت، ارجاع، گزارش کارشناس، تایید/رد مدیر و تکمیل.
/// درخواست‌دهنده فرایند داخلی واحد IT را نمی‌بیند (فقط پاسخ نهایی مدیر).</summary>
[ApiController]
[Route("api/itrequests")]
[Authorize]
public class ItRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly FileStore _store;
    private readonly ItExternalAuthService _externalAuth;
    public ItRequestsController(AppDbContext db, INotifyService notify, FileStore store, ItExternalAuthService externalAuth)
    { _db = db; _notify = notify; _store = store; _externalAuth = externalAuth; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";

    /// <summary>بررسی دسترسی RBAC کاربر جاری — با سازگاری عقب‌رو برای کاربران بدون نقش RBAC.</summary>
    private async Task<bool> HasAsync(string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy == "Operator") return action is "Create" or "ViewDepartment";
            return false;
        }

        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == "ItRequests" && p.Action == action);
    }

    /// <summary>کاربران دارای مجوز مدیر IT (برای نوتیفیکیشن).</summary>
    private async Task<List<int>> ManagerUserIdsAsync()
    {
        var rbac = await _db.UserRoles
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == "ItRequests" && x.Action == "Manage")
            .Select(x => x.UserId).Distinct().ToListAsync();
        // ادمین‌های قدیمی بدون نقش RBAC
        var legacyAdmins = await _db.Users
            .Where(u => u.Role == "Admin" && u.IsActive && !_db.UserRoles.Any(ur => ur.UserId == u.Id))
            .Select(u => u.Id).ToListAsync();
        return rbac.Concat(legacyAdmins).Distinct().ToList();
    }

    /// <summary>مدیرِ واحد IT (یا ادمین سامانه) — برای مدیریت شرکت‌های مشتری و کارتابل‌ها.</summary>
    private async Task<bool> IsManagerAsync()
        => await HasAsync("Manage") || string.Equals(User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase);

    private void Log(int reqId, string role, string action, string? text, bool internalOnly = true) =>
        _db.ItRequestLogs.Add(new ItRequestLog
        {
            RequestId = reqId, ActorName = MyUsername, ActorRole = role,
            Action = action, Text = text, InternalOnly = internalOnly
        });

    /// <summary>ثبت لاگ روی درخواست بدون نیاز به کاربر لاگین (کانال بیرونی)</summary>
    private void ItRequestLogSafe(int reqId, string actorName, string actorRole, string action, string text, bool internalOnly)
        => _db.ItRequestLogs.Add(new ItRequestLog
        {
            RequestId = reqId, ActorName = actorName, ActorRole = actorRole,
            Action = action, Text = text, InternalOnly = internalOnly
        });

    private Task<SystemUser?> MySystemUserAsync() =>
        _db.SystemUsers.FirstOrDefaultAsync(su => su.Username == MyUsername);

    // ================== دسترسی‌های من ==================
    [HttpGet("my-access")]
    public async Task<IActionResult> MyAccess() => Ok(new
    {
        userId = MyUserId,
        canCreate = await HasAsync("Create"),
        isExpert = await HasAsync("Expert"),
        isManager = await HasAsync("Manage")
    });

    // ================== سیستم‌های قابل انتخاب (با IP) ==================
    [HttpGet("systems")]
    public async Task<IActionResult> Systems([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var viewCompany = await HasAsync("ViewCompany");
        var viewDepartment = await HasAsync("ViewDepartment");
        var me = await MySystemUserAsync();

        var q = _db.SystemInfos.AsNoTracking().Where(s => s.IsApproved);

        if (viewCompany)
        {
            if (me?.CompanyId is > 0) q = q.Where(s => s.CompanyId == me.CompanyId);
        }
        else if (viewDepartment)
        {
            if (me?.DepartmentId is > 0) q = q.Where(s => s.DepartmentId == me.DepartmentId);
            else q = q.Where(s => false);
        }
        else
        {
            var myId = me?.Id ?? -1;
            q = q.Where(s => s.UserId == myId);
        }

        var systems = await q.OrderBy(x => x.Id)
            .Select(s => new
            {
                s.Id,
                Label = s.AgentId ?? "",
                // آی‌پی به جای مشخصات ویندوز
                Ip = _db.SystemNetAdapters.Where(n => n.SystemInfoId == s.Id && n.Ipv4 != "")
                    .Select(n => n.Ipv4).FirstOrDefault() ?? "",
                UserName = _db.SystemUsers.Where(u => u.Id == s.UserId)
                    .Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefault(),
                DepartmentName = _db.SystemDepartments.Where(d => d.Id == s.DepartmentId)
                    .Select(d => d.Name).FirstOrDefault()
            })
            .ToPageListAsync(pagination);

        return Ok(pagination.Result(systems));
    }

    // ================== کارشناسان (برای ارجاع) ==================
    [HttpGet("experts")]
    public async Task<IActionResult> Experts([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!await HasAsync("Manage")) return Forbid();

        var expertUserIds = await _db.UserRoles
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == "ItRequests" && x.Action == "Expert")
            .Select(x => x.UserId).Distinct().ToListAsync();

        var experts = await _db.Users.Where(u => expertUserIds.Contains(u.Id) && u.IsActive).OrderBy(x => x.Id)
            .Select(u => new { u.Id, u.Username }).ToPageListAsync(pagination);
        return Ok(pagination.Result(experts));
    }

    // ================== ثبت درخواست ==================
    public class CreateDto
    {
        public string RequesterName { get; set; } = "";
        public int? SystemInfoId { get; set; }
        public string? SystemLabel { get; set; }
        public string RequestType { get; set; } = "Hardware";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDto dto)
    {
        if (!await HasAsync("Create")) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { message = "موضوع درخواست را وارد کنید." });

        var req = new ItRequest
        {
            RequesterName = string.IsNullOrWhiteSpace(dto.RequesterName) ? MyUsername : dto.RequesterName.Trim(),
            RequesterUserId = MyUserId,
            SystemInfoId = dto.SystemInfoId,
            SystemLabel = dto.SystemLabel,
            RequestType = dto.RequestType is "Software" or "Network" or "Telecom" ? dto.RequestType : "Hardware",
            Title = dto.Title.Trim(),
            Description = dto.Description ?? "",
            Status = "New"
        };
        _db.ItRequests.Add(req);
        await _db.SaveChangesAsync();

        // شماره منحصربه‌فرد: IT/سال شمسی/سریال — سریال هر سال از ۱ شروع می‌شود
        var pc = new PersianCalendar();
        var py = pc.GetYear(DateTime.Now);
        var prefix = $"IT/{py}/";
        var serial = await _db.ItRequests.CountAsync(r => r.Number.StartsWith(prefix)) + 1;
        req.Number = $"{prefix}{serial}";
        Log(req.Id, "Requester", "Created", $"درخواست {req.Number} «{req.Title}» ثبت شد.", internalOnly: false);
        await _db.SaveChangesAsync();

        // نوتیفیکیشن به مدیران IT — لینک مستقیم به همان درخواست
        await _notify.SendManyAsync(await ManagerUserIdsAsync(),
            "درخواست IT جدید", $"{req.Number} — «{req.Title}» توسط {req.RequesterName}",
            req.RequesterName, "درخواست خدمت IT", $"/it-requests?open={req.Id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok(new { id = req.Id });
    }

    // ================== فهرست‌ها ==================
    private object ToItem(ItRequest r, List<ItRequestAssignment> asg, int attachCount, bool internalView, HashSet<int>? seenIds = null) => new
    {
        r.Id, r.Number, Seen = seenIds == null || seenIds.Contains(r.Id),
        r.RequesterName, r.RequesterUserId, r.SystemInfoId, r.SystemLabel,
        r.RequestType, r.Title, r.Description, r.Status,
        // ---------- کانال شرکت‌های راه‌دور ----------
        IsExternal = r.SourceCompanyId.HasValue || !string.IsNullOrEmpty(r.ExternalRequesterKey),
        r.SourceCompanyId,
        CompanyName = r.SourceCompany?.Name,
        CompanyCode = r.SourceCompany?.Code,
        r.RequesterPhone, r.RequesterEmail,
        ExternalRequesterKey = internalView ? r.ExternalRequesterKey : null,
        // درخواست‌دهنده فرایند داخلی را نمی‌بیند
        ManagerNote = internalView ? r.ManagerNote : null,
        r.FinalResponse,
        r.CreatedAt, r.AssignedAt, r.ApprovedAt, r.CompletedAt,
        AttachmentCount = attachCount,
        Assignments = internalView
            ? asg.Select(a => new
            {
                a.Id, a.ExpertUserId, a.ExpertName, a.ManagerInstruction,
                a.ExpertReport, a.ReportSubmitted, a.Done, a.ManagerDecision, a.ManagerDecisionNote,
                a.IncludeInFinal, a.RepliedAt
            }).Cast<object>().ToList()
            : new List<object>()
    };

    private async Task<List<object>> BuildList(IQueryable<ItRequest> q, bool internalView, Paging.Request? pagination = null)
    {
        var reqs = await q.Include(r => r.SourceCompany).OrderByDescending(r => r.Id).ToPageListAsync(pagination);
        var ids = reqs.Select(r => r.Id).ToList();
        var asgs = await _db.ItRequestAssignments.Where(a => ids.Contains(a.RequestId)).ToListAsync();
        var attCounts = await _db.ItRequestAttachments.Where(a => ids.Contains(a.RequestId))
            .GroupBy(a => a.RequestId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        // رویت‌شده‌های کاربر جاری — برای نشانگر «جدید»
        var seenIds = (await _db.ItRequestSeens.Where(sn => sn.UserId == MyUserId && ids.Contains(sn.RequestId))
            .Select(sn => sn.RequestId).ToListAsync()).ToHashSet();
        return reqs.Select(r => ToItem(r,
            asgs.Where(a => a.RequestId == r.Id).ToList(),
            attCounts.FirstOrDefault(c => c.Key == r.Id)?.C ?? 0, internalView, seenIds)).ToList();
    }

    // ================== رویت درخواست (نشانگر «جدید» برداشته می‌شود) ==================
    [HttpPost("{id:int}/seen")]
    public async Task<IActionResult> MarkSeen(int id)
    {
        if (!await _db.ItRequestSeens.AnyAsync(sn => sn.RequestId == id && sn.UserId == MyUserId))
        {
            _db.ItRequestSeens.Add(new ItRequestSeen { RequestId = id, UserId = MyUserId });
            await _db.SaveChangesAsync();
        }
        return Ok();
    }

    /// <summary>کارتابل درخواست‌دهنده — بدون جزئیات داخلی واحد IT.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(pagination => BuildList(_db.ItRequests.Where(r => r.RequesterUserId == MyUserId), internalView: false, pagination: pagination), skip, take));

    /// <summary>کارتابل مدیر آی‌تی — با فیلترِ «فقط درخواست‌های شرکت‌های راه‌دور» و انتخاب شرکت.</summary>
    [HttpGet("manager")]
    public async Task<IActionResult> ManagerInbox([FromQuery] bool onlyExternal = false,
        [FromQuery] int? companyId = null, [FromQuery] string? q2 = null,
        [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await HasAsync("Manage")) return Forbid();
        IQueryable<ItRequest> query = _db.ItRequests;
        if (onlyExternal) query = query.Where(r => r.SourceCompanyId != null || r.ExternalRequesterKey != null);
        if (companyId is > 0) query = query.Where(r => r.SourceCompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(q2))
        {
            var t = q2.Trim();
            query = query.Where(r => r.Title.Contains(t) || r.Number.Contains(t)
                                     || r.RequesterName.Contains(t) || (r.SystemLabel != null && r.SystemLabel.Contains(t)));
        }
        return Ok(await Paging.ResultAsync(pagination => BuildList(query, internalView: true, pagination: pagination), skip, take));
    }

    /// <summary>کارتابل کارشناس.</summary>
    [HttpGet("expert")]
    public async Task<IActionResult> ExpertInbox([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await HasAsync("Expert")) return Forbid();
        var myReqIds = _db.ItRequestAssignments.Where(a => a.ExpertUserId == MyUserId).Select(a => a.RequestId);
        return Ok(await Paging.ResultAsync(pagination => BuildList(_db.ItRequests.Where(r => myReqIds.Contains(r.Id)), internalView: true, pagination: pagination), skip, take));
    }

    // ================== آرشیو رفت‌وبرگشت‌ها ==================
    [HttpGet("{id:int}/logs")]
    public async Task<IActionResult> Logs(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound();

        var isIt = await HasAsync("Manage") || await HasAsync("Expert");
        var q = _db.ItRequestLogs.Where(l => l.RequestId == id);
        if (!isIt) q = q.Where(l => !l.InternalOnly); // درخواست‌دهنده فقط رویدادهای عمومی

        return Ok(await Paging.ResultAsync(q.OrderBy(l => l.Id)
            .Select(l => new { l.Id, l.ActorName, l.ActorRole, l.Action, l.Text, l.CreatedAt }), skip, take));
    }

    // ================== ارجاع مدیر ==================
    public class AssignDto
    {
        public string? ManagerNote { get; set; }
        public List<AssignItem> Experts { get; set; } = new();
        public class AssignItem { public int UserId { get; set; } public string? Instruction { get; set; } }
    }

    [HttpPost("{id:int}/assign")]
    public async Task<IActionResult> Assign(int id, [FromBody] AssignDto dto)
    {
        if (!await HasAsync("Manage")) return Forbid();
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (dto.Experts.Count == 0) return BadRequest(new { message = "حداقل یک کارشناس انتخاب کنید." });

        var old = await _db.ItRequestAssignments.Where(a => a.RequestId == id).ToListAsync();
        _db.ItRequestAssignments.RemoveRange(old);

        var users = await _db.Users.Where(u => dto.Experts.Select(e => e.UserId).Contains(u.Id)).ToListAsync();
        var names = new List<string>();
        foreach (var e in dto.Experts)
        {
            var u = users.FirstOrDefault(x => x.Id == e.UserId);
            if (u == null) continue;
            names.Add(u.Username);
            _db.ItRequestAssignments.Add(new ItRequestAssignment
            {
                RequestId = id, ExpertUserId = u.Id, ExpertName = u.Username,
                ManagerInstruction = e.Instruction?.Trim()
            });
        }

        req.ManagerNote = dto.ManagerNote?.Trim();
        req.Status = "Assigned";
        req.AssignedAt = DateTime.Now;

        // آرشیو: نام کارشناسان در ارجاع ثبت می‌شود
        Log(id, "Manager", "Assigned", $"ارجاع به: {string.Join("، ", names)}" +
            (string.IsNullOrWhiteSpace(dto.ManagerNote) ? "" : $" — توضیح: {dto.ManagerNote}"));
        await _db.SaveChangesAsync();

        // نوتیفیکیشن به هر کارشناس
        foreach (var e in dto.Experts)
        {
            var inst = dto.Experts.FirstOrDefault(x => x.UserId == e.UserId)?.Instruction;
            await _notify.SendAsync(e.UserId, "ارجاع درخواست IT",
                $"{req.Number} — «{req.Title}»" + (string.IsNullOrWhiteSpace(inst) ? "" : $" — {inst}"),
                MyUsername, "درخواست خدمت IT", $"/it-requests?open={req.Id}");
        }
        await _notify.BroadcastChangedAsync("itrequests");
        return Ok();
    }

    // ================== گزارش کارشناس (انجام شد / نشد) ==================
    public class ReportDto
    {
        public string Report { get; set; } = "";
        public bool Done { get; set; } = true;
    }

    [HttpPost("{id:int}/report")]
    public async Task<IActionResult> SubmitReport(int id, [FromBody] ReportDto dto)
    {
        if (!await HasAsync("Expert")) return Forbid();
        var asg = await _db.ItRequestAssignments
            .FirstOrDefaultAsync(a => a.RequestId == id && a.ExpertUserId == MyUserId);
        if (asg == null) return NotFound(new { message = "این درخواست به شما ارجاع نشده است." });

        var req = await _db.ItRequests.FindAsync(id);

        asg.ExpertReport = dto.Report?.Trim();
        asg.Done = dto.Done;
        asg.ReportSubmitted = true;
        asg.ManagerDecision = null; // گزارش جدید = تصمیم قبلی مدیر باطل
        asg.RepliedAt = DateTime.Now;

        Log(id, "Expert", "Report", $"{MyUsername}: {(dto.Done ? "✅ انجام شد" : "❌ انجام نشد")} — {asg.ExpertReport}");
        await _db.SaveChangesAsync();

        // مدیر متوجه شود کدام کارشناس پاسخ داده (بند ۱۱)
        await _notify.SendManyAsync(await ManagerUserIdsAsync(),
            "گزارش کارشناس IT",
            $"{MyUsername} برای {req?.Number} «{req?.Title}» گزارش ثبت کرد: {(dto.Done ? "انجام شد" : "انجام نشد")}",
            MyUsername, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok();
    }

    // ================== تصمیم مدیر روی گزارش هر کارشناس (تایید/رد — بند ۱۲) ==================
    public class DecisionDto { public bool Approved { get; set; } public string? Note { get; set; } }

    [HttpPost("{id:int}/assignments/{asgId:int}/decide")]
    public async Task<IActionResult> Decide(int id, int asgId, [FromBody] DecisionDto dto)
    {
        if (!await HasAsync("Manage")) return Forbid();
        var asg = await _db.ItRequestAssignments.FirstOrDefaultAsync(a => a.Id == asgId && a.RequestId == id);
        if (asg == null) return NotFound(new { message = "ارجاع پیدا نشد." });
        if (!asg.ReportSubmitted) return BadRequest(new { message = "این کارشناس هنوز گزارشی ثبت نکرده است." });

        var req = await _db.ItRequests.FindAsync(id);

        asg.ManagerDecision = dto.Approved ? "Approved" : "Rejected";
        asg.ManagerDecisionNote = dto.Note?.Trim();
        asg.IncludeInFinal = dto.Approved;

        if (!dto.Approved)
        {
            // رد شد: کارشناس باید دوباره اقدام/گزارش کند — وضعیت کلی تغییر نمی‌کند (بند ۱۳/۱۴)
            asg.ReportSubmitted = false;
        }

        Log(id, "Manager", dto.Approved ? "Approved" : "Rejected",
            $"گزارش {asg.ExpertName} {(dto.Approved ? "تایید" : "رد")} شد" +
            (string.IsNullOrWhiteSpace(dto.Note) ? "" : $" — {dto.Note}"));
        await _db.SaveChangesAsync();

        await _notify.SendAsync(asg.ExpertUserId,
            dto.Approved ? "گزارش شما تایید شد ✅" : "گزارش شما رد شد ❌",
            $"{req?.Number} — «{req?.Title}»" + (string.IsNullOrWhiteSpace(dto.Note) ? "" : $" — {dto.Note}"),
            MyUsername, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok();
    }

    // ================== تایید نهایی مدیر (فقط وقتی همه گزارش‌ها تایید شده‌اند — بند ۱۳) ==================
    public class ApproveDto { public string? FinalResponse { get; set; } }

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, [FromBody] ApproveDto dto)
    {
        if (!await HasAsync("Manage")) return Forbid();
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });

        var asgs = await _db.ItRequestAssignments.Where(a => a.RequestId == id).ToListAsync();
        if (asgs.Count == 0)
            return BadRequest(new { message = "ابتدا درخواست را به کارشناس ارجاع دهید." });
        if (asgs.Any(a => a.ManagerDecision != "Approved"))
            return BadRequest(new { message = "تا زمانی که گزارش همه کارشناسان تایید نشده، امکان تایید نهایی نیست." });

        if (string.IsNullOrWhiteSpace(dto.FinalResponse))
            return BadRequest(new { message = "پاسخ نهایی برای درخواست‌کننده را بنویسید." });

        req.FinalResponse = dto.FinalResponse.Trim();
        req.Status = "ManagerApproved";
        req.ApprovedAt = DateTime.Now;

        Log(id, "Manager", "Finalized", $"پاسخ نهایی: {req.FinalResponse}", internalOnly: false);
        await _db.SaveChangesAsync();

        await _notify.SendAsync(req.RequesterUserId, "پاسخ درخواست IT شما",
            $"{req.Number} — «{req.Title}» — {req.FinalResponse}", MyUsername, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok();
    }

    // ================== بستن توسط مدیر (انجام کار توسط خود مدیر) ==================
    public class CloseDto { public string FinalResponse { get; set; } = ""; }

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> CloseByManager(int id, [FromBody] CloseDto dto)
    {
        if (!await HasAsync("Manage")) return Forbid();
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (req.Status is "Completed" or "Rejected")
            return BadRequest(new { message = "این درخواست قبلاً بسته شده است." });
        if (string.IsNullOrWhiteSpace(dto.FinalResponse))
            return BadRequest(new { message = "پاسخ را بنویسید." });

        req.FinalResponse = dto.FinalResponse.Trim();
        req.Status = "ManagerApproved";
        req.ApprovedAt = DateTime.Now;

        Log(id, "Manager", "Finalized", $"انجام و بسته‌شده توسط مدیر — {req.FinalResponse}", internalOnly: false);
        await _db.SaveChangesAsync();

        await _notify.SendAsync(req.RequesterUserId, "پاسخ درخواست IT شما",
            $"{req.Number} — «{req.Title}» — {req.FinalResponse}", MyUsername, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");
        return Ok();
    }

    // ================== رد درخواست توسط مدیر ==================
    public class RejectDto { public string Reason { get; set; } = ""; }

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectDto dto)
    {
        if (!await HasAsync("Manage")) return Forbid();
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (req.Status is "Completed" or "Rejected")
            return BadRequest(new { message = "این درخواست قبلاً بسته شده است." });
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new { message = "دلیل رد را بنویسید." });

        req.FinalResponse = dto.Reason.Trim();
        req.Status = "Rejected";
        req.ApprovedAt = DateTime.Now;

        Log(id, "Manager", "RejectedRequest", $"درخواست رد شد — {req.FinalResponse}", internalOnly: false);
        await _db.SaveChangesAsync();

        await _notify.SendAsync(req.RequesterUserId, "درخواست IT شما رد شد",
            $"{req.Number} — «{req.Title}» — {req.FinalResponse}", MyUsername, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");
        return Ok();
    }

    // ================== کانفیگ اتصال به سرور مرکزی (برای همه کاربران) ==================
    /// <summary>اگر ItServerUrl تنظیم شده باشد، این نصب «شعبه» است و درخواست‌ها به سرور مرکزی می‌روند.</summary>
    [HttpGet("config")]
    public async Task<IActionResult> Config()
    {
        var st = await _db.AppSettings.FirstOrDefaultAsync();
        return Ok(new
        {
            itServerUrl = st?.ItServerUrl ?? "",
            itCompanyName = st?.ItCompanyName ?? ""
        });
    }

    // ======================================================================
    //  کانال شرکت‌های راه‌دور (نسخه‌ی نرم‌افزارِ نصب‌شده در شرکت مشتری → سرور مرکزی)
    //  احراز هویت: هدر X-It-Api-Key (کلید هر شرکت) — کاربران آن شرکت در این سرور «کاربر» نیستند.
    // ======================================================================

    public class ExternalCreateDto
    {
        /// <summary>کد شرکت فرستنده (همان کدی که سرور مرکزی تعریف کرده) — اختیاری، برای بررسی تطبیق با کلید</summary>
        public string? CompanyCode { get; set; }

        /// <summary>کلید پایدار کاربر در نرم‌افزار خودش — با آن «درخواست‌های من» را می‌بیند</summary>
        public string? RequesterKey { get; set; }

        /// <summary>شناسه یکتای درخواست در سیستم فرستنده — کلید idempotency</summary>
        public string? ExternalId { get; set; }

        public string RequesterName { get; set; } = "";
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? SystemLabel { get; set; }
        /// <summary>کد سیستم/کامپیوتر در نرم‌افزار خودشان (اختیاری) — فقط برای نمایش</summary>
        public string? LocalSystemRef { get; set; }
        public string RequestType { get; set; } = "Hardware";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>خطای یکدست برای کانال بیرونی</summary>
    private ActionResult ExtErr(int code, string message) => StatusCode(code, new { message, external = true });

    /// <summary>احراز هویت شرکت + بررسی سقف نرخ. null = معتبر.</summary>
    private async Task<ItClientCompany?> AuthExternalAsync()
    {
        var key = Request.Headers[ItExternalAuthService.HeaderName].FirstOrDefault();
        var code = Request.Headers[ItExternalAuthService.CodeHeaderName].FirstOrDefault();
        var (company, error) = await _externalAuth.AuthenticateAsync(key, code);
        if (company == null)
        {
            ExtStatus = 401;
            ExtError = error;
            return null;
        }
        if (!_externalAuth.AllowRequest(company))
        {
            ExtStatus = 429;
            ExtError = $"سقف درخواستِ ساعتیِ شرکت «{company.Name}» تکمیل شده است. کمی بعد دوباره تلاش کنید.";
            return null;
        }
        _externalAuth.Touch(company);
        return company;
    }

    private int ExtStatus;
    private string? ExtError;

    /// <summary>
    /// تست اتصال: فقط با کلیدِ API. نرم‌افزارِ شرکت هنگام راه‌اندازی این را صدا می‌زند تا
    /// مطمئن شود آدرس سرور و کلید درست است — هیچ داده‌ای جابه‌جا نمی‌شود.
    /// </summary>
    [HttpGet("ping")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalPing()
    {
        ExtStatus = 0; ExtError = null;
        var company = await AuthExternalAsync();
        if (company == null) return ExtErr(ExtStatus == 0 ? 401 : ExtStatus, ExtError ?? "احراز هویت ناموفق.");
        return Ok(new
        {
            ok = true,
            companyName = company.Name,
            companyCode = company.Code,
            server = "واحد IT — سرور مرکزی",
            message = $"اتصال برقرار است. شرکت «{company.Name}» معتبر شناخته شد."
        });
    }

    /// <summary>
    /// ثبت درخواست از شرکتِ راه‌دور. با کلیدِ API احراز هویت می‌شود و «idempotent» است:
    /// اگر همان <c>ExternalId</c> دوباره فرستاده شود (تلاش مجدد در شبکه) درخواست تکراری ساخته نمی‌شود
    /// و همان شمارهٔ قبلی برگردانده می‌شود.
    /// </summary>
    [HttpPost("external")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalCreate([FromBody] ExternalCreateDto dto)
    {
        ExtStatus = 0; ExtError = null;
        var company = await AuthExternalAsync();
        if (company == null) return ExtErr(ExtStatus == 0 ? 401 : ExtStatus, ExtError ?? "احراز هویت ناموفق.");

        // ---------- اعتبارسنجی ورودی ----------
        if (string.IsNullOrWhiteSpace(dto.RequesterName))
            return ExtErr(422, "نام درخواست‌کننده الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Title))
            return ExtErr(422, "موضوع درخواست الزامی است.");
        if (dto.RequesterName.Trim().Length > 150)
            return ExtErr(422, "نام درخواست‌کننده حداکثر ۱۵۰ کاراکتر است.");
        if (dto.Title.Trim().Length > 200)
            return ExtErr(422, "موضوع درخواست حداکثر ۲۰۰ کاراکتر است.");
        if ((dto.Description ?? "").Length > 1800)
            return ExtErr(422, "شرح درخواست حداکثر ۱۸۰۰ کاراکتر است.");

        // ---------- idempotency ----------
        var externalId = (dto.ExternalId ?? "").Trim();
        if (externalId.Length > 40) return ExtErr(422, "شناسهٔ خارجی حداکثر ۴۰ کاراکتر است.");
        if (externalId.Length > 0)
        {
            var existing = await _db.ItRequests
                .FirstOrDefaultAsync(r => r.SourceCompanyId == company.Id && r.ExternalId == externalId);
            if (existing != null)
            {
                return Ok(new
                {
                    id = existing.Id, number = existing.Number, trackToken = existing.TrackToken,
                    duplicate = true, message = "این درخواست قبلاً ثبت شده است."
                });
            }
        }

        var requesterKey = (dto.RequesterKey ?? "").Trim();
        if (requesterKey.Length > 40) return ExtErr(422, "کلید کاربر حداکثر ۴۰ کاراکتر است.");

        var requesterName = dto.RequesterName.Trim();
        var sysLabel = new List<string>();
        if (!string.IsNullOrWhiteSpace(dto.SystemLabel)) sysLabel.Add(dto.SystemLabel.Trim());
        if (!string.IsNullOrWhiteSpace(dto.LocalSystemRef)) sysLabel.Add(dto.LocalSystemRef.Trim());
        sysLabel.Add($"شرکت: {company.Name}");
        var systemLabel = string.Join(" — ", sysLabel);
        if (systemLabel.Length > 250) systemLabel = systemLabel[..250];

        var contact = new List<string>();
        if (!string.IsNullOrWhiteSpace(dto.Phone)) contact.Add($"☎ تماس: {dto.Phone.Trim()}");
        if (!string.IsNullOrWhiteSpace(dto.Email)) contact.Add($"✉ {dto.Email.Trim()}");
        var contactHtml = contact.Count > 0 ? "<div>" + string.Join(" — ", contact) + "</div>" : "";

        var desc = (dto.Description ?? "").Trim() + contactHtml;
        if (desc.Length > 2000) desc = desc[..2000];

        var req = new ItRequest
        {
            RequesterName = requesterName,
            RequesterUserId = 0,                 // کاربر بیرونی — در این سرور کاربر نیست
            SourceCompanyId = company.Id,
            SourceCompany = company,
            ExternalRequesterKey = requesterKey.Length > 0 ? requesterKey : null,
            ExternalId = externalId.Length > 0 ? externalId : null,
            RequesterPhone = dto.Phone?.Trim(),
            RequesterEmail = dto.Email?.Trim(),
            SystemLabel = systemLabel,
            RequestType = dto.RequestType is "Software" or "Network" or "Telecom" ? dto.RequestType : "Hardware",
            Title = dto.Title.Trim(),
            Description = desc,
            Status = "New",
            TrackToken = Guid.NewGuid().ToString("N")[..24],
        };
        _db.ItRequests.Add(req);
        await _db.SaveChangesAsync();

        // شماره منحصربه‌فرد: IT/سال شمسی/سریال (سریال هر سال از ۱ شروع می‌شود)
        var pc = new PersianCalendar();
        var prefix = $"IT/{pc.GetYear(DateTime.Now)}/";
        req.Number = $"{prefix}{await _db.ItRequests.CountAsync(r => r.Number.StartsWith(prefix)) + 1}";

        _db.ItRequestLogs.Add(new ItRequestLog
        {
            RequestId = req.Id, ActorName = req.RequesterName, ActorRole = "Requester",
            Action = "Created",
            Text = $"درخواست بیرونی {req.Number} از شرکت «{company.Name}» ثبت شد."
                   + (requesterKey.Length > 0 ? $" (کلید کاربر: {requesterKey})" : ""),
            InternalOnly = false
        });
        await _db.SaveChangesAsync();

        await _notify.SendManyAsync(await ManagerUserIdsAsync(),
            "درخواست IT بیرونی 🌐",
            $"{req.Number} — «{req.Title}» از {req.RequesterName} · شرکت {company.Name}",
            req.RequesterName, "درخواست خدمت IT", $"/it-requests?open={req.Id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok(new
        {
            id = req.Id, number = req.Number, trackToken = req.TrackToken,
            duplicate = false,
            message = "درخواست ثبت شد. برای ارسال پیوست از همین شماره و توکن استفاده کنید."
        });
    }

    /// <summary>آپلود پیوست برای درخواستِ بیرونی — با همان کلید API و توکنِ پیگیری.</summary>
    [HttpPost("external/{number}/attachments")]
    [AllowAnonymous]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> ExternalUpload(string number, IFormFile file)
    {
        ExtStatus = 0; ExtError = null;
        var company = await AuthExternalAsync();
        if (company == null) return ExtErr(ExtStatus == 0 ? 401 : ExtStatus, ExtError ?? "احراز هویت ناموفق.");

        var num = (number ?? "").Trim();
        var req = await _db.ItRequests.FirstOrDefaultAsync(r => r.Number == num && r.SourceCompanyId == company.Id);
        if (req == null) return ExtErr(404, "درخواستی با این شماره برای شرکت شما پیدا نشد.");
        if (file == null || file.Length == 0) return ExtErr(422, "فایلی انتخاب نشده است.");
        if (file.Length > 10 * 1024 * 1024) return ExtErr(422, "حداکثر حجم فایل ۱۰ مگابایت است.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var relPath = await _store.SaveAsync("ItRequests", req.Id, ms, file.FileName);

        _db.ItRequestAttachments.Add(new ItRequestAttachment
        {
            RequestId = req.Id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType ?? "application/octet-stream",
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderRole = "Requester",
            UploaderName = req.RequesterName,
            UploaderUserId = 0,
        });
        ItRequestLogSafe(req.Id, req.RequesterName, "Requester", "Created",
            $"پیوست «{Path.GetFileName(file.FileName)}» از شرکت {company.Name} ارسال شد.", false);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>«درخواست‌های من» برای کاربرِ بیرونی — فقط درخواست‌های همان کلیدِ کاربر و نمایان‌شده به مشتری.</summary>
    [HttpGet("external/mine")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalMine([FromQuery] string requesterKey,
        [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        ExtStatus = 0; ExtError = null;
        var company = await AuthExternalAsync();
        if (company == null) return ExtErr(ExtStatus == 0 ? 401 : ExtStatus, ExtError ?? "احراز هویت ناموفق.");

        var key = (requesterKey ?? "").Trim();
        if (key.Length == 0) return ExtErr(422, "کلید کاربر ارسال نشده است.");
        if (key.Length > 40) return ExtErr(422, "کلید کاربر حداکثر ۴۰ کاراکتر است.");

        var q = _db.ItRequests.AsNoTracking()
            .Where(r => r.SourceCompanyId == company.Id && r.ExternalRequesterKey == key);

        var pg = await Paging.QueryAsync(q.OrderByDescending(r => r.Id), skip, take, 500);
        var rows = pg.Rows.Select(r => new
        {
            r.Number,
            r.Title,
            r.RequestType,
            r.Status,
            r.CreatedAt,
            r.CompletedAt,
            r.ApprovedAt,
            trackToken = r.TrackToken,
            // فقط پاسخ نهایی — فرایند داخلیِ واحد IT محرمانه می‌ماند
            FinalResponse = r.Status is "ManagerApproved" or "Completed" or "Rejected" ? r.FinalResponse : null,
        });
        return Ok(pg.Result(rows));
    }

    /// <summary>پیگیری وضعیت با شماره. برای درخواست‌های بیرونی، توکنِ پیگیری هم لازم است.</summary>
    [HttpGet("track")]
    [AllowAnonymous]
    public async Task<IActionResult> Track([FromQuery] string number, [FromQuery] string? token = null)
    {
        var num = (number ?? "").Trim();
        if (num.Length == 0) return BadRequest(new { message = "شماره درخواست را وارد کنید." });

        var req = await _db.ItRequests.Include(r => r.SourceCompany).FirstOrDefaultAsync(r => r.Number == num);
        if (req == null) return NotFound(new { message = "درخواستی با این شماره پیدا نشد." });

        // درخواست بیرونی: بدون توکنِ درست، هیچ چیزی فاش نمی‌شود (شماره قابل حدس است)
        if (!string.IsNullOrEmpty(req.TrackToken) &&
            !FixedEquals(req.TrackToken, (token ?? "").Trim()))
            return Unauthorized(new { message = "توکنِ پیگیری نامعتبر است.", needToken = true });

        return Ok(new
        {
            req.Number, req.Title, req.RequestType, req.Status,
            req.CreatedAt, req.ApprovedAt, req.CompletedAt,
            Company = req.SourceCompanyId.HasValue ? req.SourceCompany?.Name : null,
            FinalResponse = req.Status is "ManagerApproved" or "Completed" or "Rejected" ? req.FinalResponse : null
        });
    }

    /// <summary>تأیید نهاییِ تکمیل توسط درخواست‌کننده (بدون لاگین) — با توکنِ پیگیری.</summary>
    [HttpPost("track/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> TrackComplete([FromQuery] string number, [FromQuery] string? token = null)
    {
        var num = (number ?? "").Trim();
        var req = await _db.ItRequests.FirstOrDefaultAsync(r => r.Number == num);
        if (req == null) return NotFound(new { message = "درخواستی با این شماره پیدا نشد." });
        if (!string.IsNullOrEmpty(req.TrackToken) &&
            !FixedEquals(req.TrackToken, (token ?? "").Trim()))
            return Unauthorized(new { message = "توکنِ پیگیری نامعتبر است.", needToken = true });
        if (req.Status != "ManagerApproved")
            return BadRequest(new { message = "این درخواست هنوز پاسخ نهایی نگرفته است." });

        req.Status = "Completed";
        req.CompletedAt = DateTime.Now;
        _db.ItRequestLogs.Add(new ItRequestLog
        {
            RequestId = req.Id, ActorName = req.RequesterName, ActorRole = "Requester",
            Action = "Completed", Text = "تایید نهایی توسط درخواست‌کننده (از راه دور).", InternalOnly = false
        });
        await _db.SaveChangesAsync();

        await _notify.SendManyAsync(await ManagerUserIdsAsync(),
            "درخواست IT تکمیل شد", $"{req.Number} — «{req.Title}» توسط {req.RequesterName} تایید شد.",
            req.RequesterName, "درخواست خدمت IT", $"/it-requests?open={req.Id}");
        await _notify.BroadcastChangedAsync("itrequests");
        return Ok(new { ok = true });
    }

    // ================== مدیریت شرکت‌های مشتری (سرور مرکزی) ==================

    public class ClientCompanyDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public bool IsActive { get; set; } = true;
        public int HourlyLimit { get; set; } = 60;
        public string? Note { get; set; }
    }

    [HttpGet("client-companies")]
    public async Task<IActionResult> GetClientCompanies()
    {
        if (!await IsManagerAsync()) return Forbid();
        var list = await _db.ItClientCompanies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        var counts = await _db.ItRequests.AsNoTracking()
            .Where(r => r.SourceCompanyId != null)
            .GroupBy(r => r.SourceCompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count);
        var openCounts = await _db.ItRequests.AsNoTracking()
            .Where(r => r.SourceCompanyId != null && r.Status != "Completed" && r.Status != "Rejected")
            .GroupBy(r => r.SourceCompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count);

        return Ok(list.Select(c => new
        {
            c.Id, c.Code, c.Name, c.Phone, c.IsActive, c.HourlyLimit, c.Note,
            c.ApiKeyHint, c.CreatedAt, c.KeyRotatedAt,
            RequestCount = counts.TryGetValue(c.Id, out var n) ? n : 0,
            OpenCount = openCounts.TryGetValue(c.Id, out var o) ? o : 0,
        }));
    }

    /// <summary>ساخت/ویرایش شرکت. کلید فقط هنگام ساخت (یا تعویض) یک‌بار برگردانده می‌شود.</summary>
    [HttpPost("client-companies")]
    public async Task<IActionResult> SaveClientCompany([FromBody] ClientCompanyDto dto)
    {
        if (!await IsManagerAsync()) return Forbid();
        var code = (dto.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 50)
            return BadRequest(new { message = "کد شرکت باید بین ۲ تا ۵۰ کاراکتر باشد." });
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "نام شرکت الزامی است." });
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9_-]+$"))
            return BadRequest(new { message = "کد شرکت فقط می‌تواند شامل حروف انگلیسی بزرگ، عدد، خط تیره و زیرخط باشد." });

        var dup = await _db.ItClientCompanies.AnyAsync(c => c.Code == code && c.Id != dto.Id);
        if (dup) return BadRequest(new { message = "این کد قبلاً ثبت شده است." });

        ItClientCompany c;
        string? plainKey = null;
        if (dto.Id > 0)
        {
            var existing = await _db.ItClientCompanies.FindAsync(dto.Id);
            if (existing == null) return NotFound(new { message = "شرکت یافت نشد." });
            c = existing;
            plainKey = RotateIfEmptyKey(c);
        }
        else
        {
            plainKey = ItExternalAuthService.NewKey();
            c = new ItClientCompany
            {
                ApiKeyHash = ItExternalAuthService.HashKey(plainKey),
                ApiKeyHint = ItExternalAuthService.Hint(plainKey),
                KeyRotatedAt = DateTime.Now,
            };
            _db.ItClientCompanies.Add(c);
        }
        c.Code = code;
        c.Name = dto.Name.Trim();
        c.Phone = dto.Phone?.Trim();
        c.IsActive = dto.IsActive;
        c.HourlyLimit = Math.Clamp(dto.HourlyLimit, 0, 10_000);
        c.Note = dto.Note?.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { c.Id, c.Code, c.ApiKeyHint, c.IsActive, apiKey = plainKey });
    }

    /// <summary>تعویض کلید API یک شرکت — کلید قبلی بلافاصله از کار می‌افتد.</summary>
    [HttpPost("client-companies/{id:int}/rotate-key")]
    public async Task<IActionResult> RotateClientKey(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var c = await _db.ItClientCompanies.FindAsync(id);
        if (c == null) return NotFound();

        var plain = ItExternalAuthService.NewKey();
        c.ApiKeyHash = ItExternalAuthService.HashKey(plain);
        c.ApiKeyHint = ItExternalAuthService.Hint(plain);
        c.KeyRotatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { apiKey = plain, c.ApiKeyHint, c.KeyRotatedAt });
    }

    [HttpDelete("client-companies/{id:int}")]
    public async Task<IActionResult> DeleteClientCompany(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var c = await _db.ItClientCompanies.FindAsync(id);
        if (c == null) return NotFound();
        if (await _db.ItRequests.AnyAsync(r => r.SourceCompanyId == id))
            return BadRequest(new { message = "این شرکت درخواست ثبت‌شده دارد و قابل حذف نیست؛ آن را غیرفعال کنید." });
        _db.ItClientCompanies.Remove(c);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static string RotateIfEmptyKey(ItClientCompany c)
    {
        if (!string.IsNullOrEmpty(c.ApiKeyHash)) return "";
        var k = ItExternalAuthService.NewKey();
        c.ApiKeyHash = ItExternalAuthService.HashKey(k);
        c.ApiKeyHint = ItExternalAuthService.Hint(k);
        c.KeyRotatedAt = DateTime.Now;
        return k;
    }

    private static bool FixedEquals(string a, string b)
    {
        var x = System.Text.Encoding.UTF8.GetBytes(a);
        var y = System.Text.Encoding.UTF8.GetBytes(b);
        return x.Length == y.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(x, y);
    }

    // ================== آمار درخواست‌ها (داشبورد سخت‌افزار) ==================
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var total = await _db.ItRequests.CountAsync();
        var completed = await _db.ItRequests.CountAsync(r => r.Status == "Completed");
        var inProgress = await _db.ItRequests.CountAsync(r => r.Status == "Assigned");
        // در انتظار مدیر: جدید (قبل از ارجاع) + گزارش‌های ثبت‌شده منتظر تایید/رد مدیر
        var newOnes = await _db.ItRequests.CountAsync(r => r.Status == "New");
        var waitingDecision = await _db.ItRequests.CountAsync(r =>
            r.Status == "Assigned" &&
            _db.ItRequestAssignments.Any(a => a.RequestId == r.Id && a.ReportSubmitted && a.ManagerDecision == null));
        var rejected = await _db.ItRequests.CountAsync(r => r.Status == "Rejected");
        var waitingRequester = await _db.ItRequests.CountAsync(r => r.Status == "ManagerApproved");

        return Ok(new
        {
            total,
            completed,
            inProgress,
            waitingManager = newOnes + waitingDecision,
            newOnes,
            waitingDecision,
            waitingRequester,
            rejected
        });
    }

    // ================== سوابق درخواست‌های یک سیستم (برای شناسنامه سیستم) ==================
    [HttpGet("by-system/{sysId:int}")]
    public async Task<IActionResult> BySystem(int sysId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pg = await Paging.QueryAsync(_db.ItRequests.Where(r => r.SystemInfoId == sysId)
            .OrderByDescending(r => r.Id), skip, take);
        var reqs = pg.Rows;
        var ids = reqs.Select(r => r.Id).ToList();
        var asgs = await _db.ItRequestAssignments.Where(a => ids.Contains(a.RequestId)).ToListAsync();

        return Ok(pg.Result(reqs.Select(r => new
        {
            r.Id, r.Number, r.Title, r.RequestType, r.Status, r.RequesterName,
            r.CreatedAt, r.CompletedAt, r.FinalResponse,
            Experts = asgs.Where(a => a.RequestId == r.Id).Select(a => a.ExpertName).ToList()
        })));
    }

    // ================== تکمیل توسط درخواست‌دهنده ==================
    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> Complete(int id)
    {
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (req.RequesterUserId != MyUserId) return Forbid();
        if (req.Status != "ManagerApproved")
            return BadRequest(new { message = "درخواست هنوز توسط مدیر تایید نشده است." });

        req.Status = "Completed";
        req.CompletedAt = DateTime.Now;

        Log(id, "Requester", "Completed", "درخواست توسط درخواست‌دهنده تایید و تکمیل شد.", internalOnly: false);
        await _db.SaveChangesAsync();

        await _notify.SendManyAsync(await ManagerUserIdsAsync(),
            "درخواست IT تکمیل شد", $"{req.Number} — «{req.Title}» توسط {req.RequesterName} تایید نهایی شد.",
            req.RequesterName, "درخواست خدمت IT", $"/it-requests?open={id}");
        await _notify.BroadcastChangedAsync("itrequests");

        return Ok();
    }

    // ================== پیوست‌ها ==================
    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> Attachments(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pg = await Paging.QueryAsync(_db.ItRequestAttachments.Where(a => a.RequestId == id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.UploaderRole, a.UploaderName, a.UploadedAt, a.FilePath, a.Data }), skip, take);
        var rows = pg.Rows;
        return Ok(pg.Result(rows.Select(a => new { a.Id, a.FileName, a.ContentType, a.UploaderRole, a.UploaderName, a.UploadedAt,
            Size = a.FilePath is not null ? _store.Size(a.FilePath) : (long)a.Data.Length })));
    }

    [HttpPost("{id:int}/attachments")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(int id, IFormFile file, [FromQuery] string role = "Requester")
    {
        var req = await _db.ItRequests.FindAsync(id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (file == null || file.Length == 0) return BadRequest(new { message = "فایلی انتخاب نشده است." });
        if (file.Length > 10 * 1024 * 1024) return BadRequest(new { message = "حداکثر حجم فایل ۱۰ مگابایت است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var relPath = await _store.SaveAsync("ItRequests", id, ms, file.FileName);

        _db.ItRequestAttachments.Add(new ItRequestAttachment
        {
            RequestId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType ?? "application/octet-stream",
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderRole = role is "Expert" or "Manager" ? role : "Requester",
            UploaderName = MyUsername,
            UploaderUserId = MyUserId
        });
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("attachments/{attId:int}/download")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(int attId)
    {
        var att = await _db.ItRequestAttachments.FindAsync(attId);
        if (att == null) return NotFound();
        var bytes = _store.ReadBytes(att.FilePath) ?? (att.Data is { Length: > 0 } ? att.Data : null);
        if (bytes is null) return NotFound(new { message = "فایل در دسترس نیست." });
        return File(bytes, att.ContentType, att.FileName);
    }
}
