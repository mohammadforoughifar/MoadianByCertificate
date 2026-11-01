using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

/// <summary>
/// کارتابل بخش مدیریت پروژه — دو صف جدا:
///  • صف «مدیر»: پروژه‌های تازه‌ثبت‌شده (FlowStatus=0) → تایید (به کارتابل کارشناسی) یا رد (با دلیل).
///  • صف «کارشناسی»: پروژه‌های تاییدشدهٔ مدیر (FlowStatus=1) → ثبت «اتمام کارشناسی» (نهایی = 3).
/// همهٔ رویدادها: زنگ اعلان به گروه مجاز + پخش بلادرنگ (SignalR) برای رفرش زندهٔ لیست‌ها/کارتابل‌ها.
/// ماژول دسترسی: ProjectCartable — اکشن‌ها: Read (مشاهده/شمارش)، Manager (تایید/رد)، Expert (اتمام کارشناسی).
/// </summary>
[Route("api/projectcartable")]
public class ProjectCartableController : RbacControllerBase
{
    private const string CCModule = "ProjectCartable";
    private readonly INotifyService _notify;

    public ProjectCartableController(AppDbContext db, INotifyService notify) : base(db) => _notify = notify;

    private static string DisplayOf(User u)
    {
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    /// <summary>نام فارسی وضعیت گردش‌کار</summary>
    public static string FlowNameFa(int s) => s switch
    {
        0 => "در انتظار تایید مدیر",
        1 => "در انتظار کارشناسی",
        2 => "رد شده توسط مدیر",
        3 => "نهایی (کارشناسی انجام شد)",
        _ => "-"
    };

    private Task<bool> CanManagerAsync() => HasAsync(CCModule, "Manager");
    private Task<bool> CanExpertAsync() => HasAsync(CCModule, "Expert");

    // ==================== شمارش برای بج/عنوان صفحه ====================
    [HttpGet("counts")]
    public async Task<IActionResult> Counts()
    {
        if (await ForbiddenUnlessAsync(CCModule, "Read") is { } forbid) return forbid;
        return Ok(new ProjectCartableCountsDto
        {
            Manager = await Db.ProjectEntryExits.CountAsync(p => !p.IsDelete && p.FlowStatus == 0),
            Expert = await Db.ProjectEntryExits.CountAsync(p => !p.IsDelete && p.FlowStatus == 1),
            Changes = await Db.ProjectChangeRequests.CountAsync(c => c.Status == 0)
        });
    }

    // ==================== لیست صف‌ها ====================
    /// <param name="kind">manager (در انتظار تایید مدیر) | expert (در انتظار کارشناسی)</param>
    [HttpGet("queue")]
    public async Task<IActionResult> Queue([FromQuery] string? kind, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessAsync(CCModule, "Read") is { } forbid) return forbid;

        var isManager = string.Equals(kind, "manager", StringComparison.OrdinalIgnoreCase);
        var status = isManager ? 0 : 1;

        // کارتابل FIFO — قدیمی‌ترین در انتظار، بالای لیست
        var list = await Db.ProjectEntryExits.AsNoTracking()
            .Include(p => p.KarFarma)
            .Include(p => p.User)
            .Include(p => p.Attaches.Where(a => !a.IsDelete))
            .Where(p => !p.IsDelete && p.FlowStatus == status)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToPageListAsync(pagination);

        var today = DateTime.Today;
        return Ok(pagination.Result(list.Select(p => new
        {
            p.Id,
            p.CodeProject,
            p.ReturnProjectId,
            p.SerialNumber,
            p.ProjectName,
            KarFarmaName = p.KarFarma?.Name,
            p.EntryDate,
            p.ExitDate,
            p.CreatedAt,
            RegisterUser = p.User is null ? null : DisplayOf(p.User),
            DaysWaiting = Math.Max(0, (today - p.CreatedAt.Date).Days),
            AttachCount = p.Attaches.Count
        }).ToList()));
    }

    // ==================== اکشن‌های مدیر ====================
    /// <summary>تایید مدیر — پروژه به کارتابل کارشناسی می‌رود</summary>
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, [FromBody] ProjectFlowActionDto dto)
    {
        if (!await CanManagerAsync())
            return StatusCode(403, new { message = "شما مجوز تایید مدیر (ProjectCartable.Manager) را ندارید." });

        var p = await Db.ProjectEntryExits.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (p.FlowStatus != 0)
            return BadRequest(new { message = $"این پروژه دیگر در کارتابل مدیر نیست — وضعیت فعلی: «{FlowNameFa(p.FlowStatus)}»." });

        p.FlowStatus = 1;
        p.ManagerActionById = MyUserId;
        p.ManagerActionAt = DateTime.Now;
        p.ManagerNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        await Db.SaveChangesAsync();

        var me = await MyDisplayAsync();
        try
        {
            // زنگ به کارشناس‌ها: پروژه در صف کارشناسی آماده است
            var experts = await UsersWithPermissionAsync(CCModule, "Expert", excludeSelf: true);
            await _notify.SendManyAsync(experts, "پروژه در کارتابل کارشناسی",
                $"«{p.ProjectName}» (کد {p.CodeProject}) توسط مدیر تایید شد — آمادهٔ کارشناسی.",
                me, "مدیریت پروژه‌ها", $"/project-cartable?queue=expert");
            // زنگ به ثبت‌کننده: مدیر تایید کرد
            if (p.UserId != MyUserId)
                await _notify.SendAsync(p.UserId, "تایید مدیر",
                    $"پروژهٔ «{p.ProjectName}» (کد {p.CodeProject}) توسط مدیر تایید شد." +
                    (p.ManagerNote is null ? "" : $" یادداشت: {p.ManagerNote}"),
                    me, "مدیریت پروژه‌ها", "/projects");
            await _notify.BroadcastChangedAsync("projects");
        }
        catch { }

        return Ok(new { id = p.Id, flowStatus = p.FlowStatus });
    }

    /// <summary>رد مدیر — دلیل رد الزامی است</summary>
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] ProjectFlowActionDto dto)
    {
        if (!await CanManagerAsync())
            return StatusCode(403, new { message = "شما مجوز تایید/رد مدیر (ProjectCartable.Manager) را ندارید." });

        var note = dto.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note))
            return BadRequest(new { message = "دلیل رد پروژه را بنویسید — بدون دلیل، رد ممکن نیست." });

        var p = await Db.ProjectEntryExits.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (p.FlowStatus != 0)
            return BadRequest(new { message = $"این پروژه دیگر در کارتابل مدیر نیست — وضعیت فعلی: «{FlowNameFa(p.FlowStatus)}»." });

        p.FlowStatus = 2;
        p.ManagerActionById = MyUserId;
        p.ManagerActionAt = DateTime.Now;
        p.ManagerNote = note;
        await Db.SaveChangesAsync();

        var me = await MyDisplayAsync();
        try
        {
            if (p.UserId != MyUserId)
                await _notify.SendAsync(p.UserId, "رد پروژه توسط مدیر",
                    $"پروژهٔ «{p.ProjectName}» (کد {p.CodeProject}) رد شد. دلیل: {note}",
                    me, "مدیریت پروژه‌ها", "/projects");
            await _notify.BroadcastChangedAsync("projects");
        }
        catch { }

        return Ok(new { id = p.Id, flowStatus = p.FlowStatus });
    }

    /// <summary>
    /// ارسال مجدد پروژهٔ ردشده به کارتابل مدیر — بعد از اصلاح ایرادهایی که مدیر گرفته بود.
    /// وضعیت از «۲=رد شده» به «۰=در انتظار تایید مدیر» برمی‌گردد و یادداشت رد مدیر پاک می‌شود.
    /// مجوز لازم: Projects.Update (همان مجوز ویرایش پروژه — کارتابل لازم نیست).
    /// </summary>
    [HttpPost("{id:int}/resubmit")]
    public async Task<IActionResult> Resubmit(int id, [FromBody] ProjectFlowActionDto dto)
    {
        if (!await HasAsync("Projects", "Update"))
            return StatusCode(403, new { message = "شما مجوز ویرایش پروژه (Projects.Update) را ندارید." });

        var p = await Db.ProjectEntryExits.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (p.FlowStatus != 2)
            return BadRequest(new { message = $"فقط پروژهٔ «رد شده» را می‌توان دوباره ارسال کرد — وضعیت فعلی: «{FlowNameFa(p.FlowStatus)}»." });

        var rejectNote = p.ManagerNote;
        p.FlowStatus = 0;
        p.ManagerActionById = null;
        p.ManagerActionAt = null;
        p.ManagerNote = null;
        await Db.SaveChangesAsync();

        var me = await MyDisplayAsync();
        var fix = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        try
        {
            var managers = await UsersWithPermissionAsync(CCModule, "Manager", excludeSelf: true);
            await _notify.SendManyAsync(managers, "پروژهٔ اصلاح‌شده در کارتابل مدیر",
                $"«{p.ProjectName}» (کد {p.CodeProject}) پس از اصلاح دوباره برای تایید ارسال شد." +
                (rejectNote is null ? "" : $" دلیل رد قبلی: {rejectNote}") +
                (fix is null ? "" : $" توضیح اصلاح: {fix}"),
                me, "مدیریت پروژه‌ها", "/project-cartable?queue=manager");
            await _notify.BroadcastChangedAsync("projects");
        }
        catch { }

        return Ok(new { id = p.Id, flowStatus = p.FlowStatus });
    }

    // ==================== اکشن کارشناس ====================
    /// <summary>اتمام کارشناسی — پروژه نهایی و از کارتابل خارج می‌شود</summary>
    [HttpPost("{id:int}/expert-done")]
    public async Task<IActionResult> ExpertDone(int id, [FromBody] ProjectFlowActionDto dto)
    {
        if (!await CanExpertAsync())
            return StatusCode(403, new { message = "شما مجوز اتمام کارشناسی (ProjectCartable.Expert) را ندارید." });

        var p = await Db.ProjectEntryExits.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        if (p.FlowStatus != 1)
            return BadRequest(new { message = p.FlowStatus == 0
                ? "این پروژه هنوز در کارتابل مدیر است — ابتدا باید مدیر تایید کند."
                : $"این پروژه دیگر در کارتابل کارشناسی نیست — وضعیت فعلی: «{FlowNameFa(p.FlowStatus)}»." });

        p.FlowStatus = 3;
        p.ExpertActionById = MyUserId;
        p.ExpertActionAt = DateTime.Now;
        p.ExpertNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        await Db.SaveChangesAsync();

        var me = await MyDisplayAsync();
        try
        {
            var managers = await UsersWithPermissionAsync(CCModule, "Manager", excludeSelf: true);
            await _notify.SendManyAsync(managers, "کارشناسی پروژه انجام شد",
                $"«{p.ProjectName}» (کد {p.CodeProject}) از کارتابل کارشناسی خارج و نهایی شد.",
                me, "مدیریت پروژه‌ها", "/projects");
            if (p.UserId != MyUserId)
                await _notify.SendAsync(p.UserId, "پروژه نهایی شد",
                    $"کارشناسی «{p.ProjectName}» (کد {p.CodeProject}) انجام شد." +
                    (p.ExpertNote is null ? "" : $" یادداشت کارشناس: {p.ExpertNote}"),
                    me, "مدیریت پروژه‌ها", "/projects");
            await _notify.BroadcastChangedAsync("projects");
        }
        catch { }

        return Ok(new { id = p.Id, flowStatus = p.FlowStatus });
    }

    // ====================================================================
    //  صف سوم: درخواست‌های «ویرایش/حذف» پروژه که منتظر تایید مدیر هستند
    //  (بند درخواست کاربر: حذف یا ویرایش پروژه فقط با تایید مدیر انجام شود)
    // ====================================================================

    /// <summary>لیست درخواست‌های ویرایش/حذف در انتظار تایید مدیر</summary>
    [HttpGet("changes")]
    public async Task<IActionResult> Changes([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessAsync(CCModule, "Read") is { } forbid) return forbid;

        var list = await Db.ProjectChangeRequests.AsNoTracking()
            .Include(c => c.Project).ThenInclude(p => p!.KarFarma)
            .Include(c => c.RequestedBy)
            .Where(c => c.Status == 0)
            .OrderBy(c => c.RequestedAt).ThenBy(c => c.Id)
            .ToPageListAsync(pagination);

        var today = DateTime.Today;
        return Ok(pagination.Result(list.Select(c => new ProjectChangeRequestDto
        {
            Id = c.Id,
            ProjectId = c.ProjectId,
            Kind = c.Kind,
            Status = c.Status,
            CodeProject = c.Project?.CodeProject ?? "",
            ProjectName = c.Project?.ProjectName ?? "",
            KarFarmaName = c.Project?.KarFarma?.Name,
            Summary = c.Summary,
            RequestNote = c.RequestNote,
            ManagerNote = c.ManagerNote,
            RequestedByName = c.RequestedBy is null ? null : DisplayOf(c.RequestedBy),
            RequestedAt = c.RequestedAt,
            ManagerActionAt = c.ManagerActionAt,
            DaysWaiting = Math.Max(0, (today - c.RequestedAt.Date).Days)
        }).ToList()));
    }

    /// <summary>تایید مدیر → تغییر واقعاً اعمال می‌شود (ویرایش ذخیره یا پروژه حذف نرم می‌شود)</summary>
    [HttpPost("changes/{id:int}/approve")]
    public async Task<IActionResult> ApproveChange(int id, [FromBody] ProjectFlowActionDto dto,
        [FromServices] Services.IProjectFileProtection protect)
    {
        if (!await CanManagerAsync())
            return StatusCode(403, new { message = "شما مجوز تایید مدیر (ProjectCartable.Manager) را ندارید." });

        var req = await Db.ProjectChangeRequests.FirstOrDefaultAsync(c => c.Id == id);
        if (req is null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (req.Status != 0) return BadRequest(new { message = "این درخواست قبلاً تعیین تکلیف شده است." });

        var project = await Db.ProjectEntryExits.FirstOrDefaultAsync(p => p.Id == req.ProjectId && !p.IsDelete);
        if (project is null) return NotFound(new { message = "پروژه پیدا نشد (شاید قبلاً حذف شده باشد)." });

        var isDelete = req.Kind == 2;
        if (isDelete)
        {
            await ProjectsController.ApplyDeleteAsync(Db, project, protect);
        }
        else
        {
            var payload = string.IsNullOrWhiteSpace(req.PayloadJson)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<ProjectEntryExitDto>(req.PayloadJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null) return BadRequest(new { message = "اطلاعات درخواست ویرایش خوانا نیست." });

            var originUserId = project.UserId; // ثبت‌کننده اصلی حفظ می‌شود
            ProjectsController.Map(payload, project);
            project.UserId = originUserId;
        }

        req.Status = 1;
        req.ManagerActionById = MyUserId;
        req.ManagerActionAt = DateTime.Now;
        req.ManagerNote = string.IsNullOrWhiteSpace(dto?.Note) ? null : dto!.Note!.Trim();
        await Db.SaveChangesAsync();

        var me = await MyDisplayAsync();
        try
        {
            if (req.RequestedById != MyUserId)
                await _notify.SendAsync(req.RequestedById,
                    isDelete ? "تایید حذف پروژه" : "تایید ویرایش پروژه",
                    $"درخواست شما برای {(isDelete ? "حذف" : "ویرایش")} پروژهٔ «{project.ProjectName}» (کد {project.CodeProject}) توسط مدیر تایید و اعمال شد.",
                    me, "مدیریت پروژه‌ها", "/projects");
            await _notify.BroadcastChangedAsync("projects");
        }
        catch { }

        return Ok(new { id = req.Id, status = req.Status });
    }

    /// <summary>رد مدیر → هیچ تغییری اعمال نمی‌شود (دلیل الزامی است)</summary>
    [HttpPost("changes/{id:int}/reject")]
    public async Task<IActionResult> RejectChange(int id, [FromBody] ProjectFlowActionDto dto)
    {
        if (!await CanManagerAsync())
            return StatusCode(403, new { message = "شما مجوز تایید/رد مدیر (ProjectCartable.Manager) را ندارید." });

        var note = dto?.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note))
            return BadRequest(new { message = "دلیل رد درخواست را بنویسید — بدون دلیل، رد ممکن نیست." });

        var req = await Db.ProjectChangeRequests.Include(c => c.Project).FirstOrDefaultAsync(c => c.Id == id);
        if (req is null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (req.Status != 0) return BadRequest(new { message = "این درخواست قبلاً تعیین تکلیف شده است." });

        req.Status = 2;
        req.ManagerActionById = MyUserId;
        req.ManagerActionAt = DateTime.Now;
        req.ManagerNote = note;
        await Db.SaveChangesAsync();

        var me = await MyDisplayAsync();
        try
        {
            if (req.RequestedById != MyUserId)
                await _notify.SendAsync(req.RequestedById,
                    req.Kind == 2 ? "رد درخواست حذف پروژه" : "رد درخواست ویرایش پروژه",
                    $"درخواست شما برای {(req.Kind == 2 ? "حذف" : "ویرایش")} پروژهٔ «{req.Project?.ProjectName}» (کد {req.Project?.CodeProject}) رد شد. دلیل: {note}",
                    me, "مدیریت پروژه‌ها", "/projects");
            await _notify.BroadcastChangedAsync("projects");
        }
        catch { }

        return Ok(new { id = req.Id, status = req.Status });
    }

    private async Task<string> MyDisplayAsync()
    {
        var u = await Db.Users.FindAsync(MyUserId);
        if (u is null) return MyUsername;
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }
}
