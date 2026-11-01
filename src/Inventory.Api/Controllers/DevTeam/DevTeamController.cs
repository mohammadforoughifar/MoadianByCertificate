using Inventory.Api.Services;
using System.Security.Claims;
using Inventory.Api.Services.DevTeam;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.DevTeam;

/// <summary>میز کار توسعه — مدیریت تیم نرم‌افزار، تسک، مشکل، changelog و گیت.</summary>
[ApiController]
[Route("api/dev-team")]
[Authorize]
public class DevTeamController : ControllerBase
{
    private readonly IDevTeamService _svc;

    public DevTeamController(IDevTeamService svc) => _svc = svc;

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";
    private bool IsLegacyAdmin => User.IsInRole("Admin");
    private IEnumerable<string> LegacyRoles => User.FindAll(ClaimTypes.Role).Select(c => c.Value);

    private Task<string> DisplayNameAsync()
    {
        // نام نمایشی از claim یا username
        var given = User.FindFirstValue(ClaimTypes.GivenName);
        var family = User.FindFirstValue(ClaimTypes.Surname);
        var full = $"{given} {family}".Trim();
        return Task.FromResult(string.IsNullOrWhiteSpace(full) ? MyUsername : full);
    }

    private async Task<DtAccessDto> AccessAsync() =>
        await _svc.GetAccessAsync(MyUserId, IsLegacyAdmin, LegacyRoles);

    private async Task EnsureAsync(Func<DtAccessDto, bool> check, string msg = "دسترسی مجاز نیست.")
    {
        var a = await AccessAsync();
        if (!a.CanView || !check(a))
            throw new InvalidOperationException(msg);
    }

    // ---------- access / lookups / dashboard ----------

    [HttpGet("my-access")]
    public async Task<ActionResult<DtAccessDto>> MyAccess() => Ok(await AccessAsync());

    /// <summary>وضعیت وب‌هوک گیت/CI + RBAC برای تب تنظیمات (مسیر A).</summary>
    [HttpGet("integration-status")]
    public async Task<ActionResult<DtIntegrationStatusDto>> IntegrationStatus()
    {
        await EnsureAsync(a => a.CanView);
        // Prefer public origin from reverse proxy headers when present
        var scheme = Request.Headers.TryGetValue("X-Forwarded-Proto", out var xp) && !string.IsNullOrWhiteSpace(xp)
            ? xp.ToString().Split(',')[0].Trim()
            : Request.Scheme;
        var host = Request.Headers.TryGetValue("X-Forwarded-Host", out var xh) && !string.IsNullOrWhiteSpace(xh)
            ? xh.ToString().Split(',')[0].Trim()
            : Request.Host.Value;
        var baseUrl = $"{scheme}://{host}";
        return Ok(await _svc.GetIntegrationStatusAsync(baseUrl));
    }

    [HttpGet("lookups")]
    public async Task<ActionResult<DtLookupsDto>> Lookups()
    {
        await EnsureAsync(a => a.CanView);
        return Ok(await _svc.GetLookupsAsync());
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<DtDashboardDto>> Dashboard([FromQuery] int? sprintId = null)
    {
        await EnsureAsync(a => a.CanView);
        return Ok(await _svc.GetDashboardAsync(sprintId));
    }

    // ---------- statuses ----------

    [HttpGet("statuses")]
    public async Task<ActionResult<List<DtWorkflowStatusDto>>> Statuses([FromQuery] bool all = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        await EnsureAsync(a => a.CanView);
        return Ok(await Paging.ResultAsync(async pagination => await _svc.GetStatusesAsync(all, pagination: pagination), skip, take));
    }

    [HttpPost("statuses")]
    public async Task<ActionResult<DtWorkflowStatusDto>> UpsertStatus([FromBody] DtWorkflowStatusDto dto)
    {
        await EnsureAsync(a => a.CanManage, "فقط مدیر ماژول می‌تواند وضعیت‌ها را تنظیم کند.");
        return Ok(await _svc.UpsertStatusAsync(dto));
    }

    [HttpDelete("statuses/{id:int}")]
    public async Task<IActionResult> DeleteStatus(int id)
    {
        await EnsureAsync(a => a.CanManage);
        await _svc.DeleteStatusAsync(id);
        return Ok(new { message = "حذف شد" });
    }

    // ---------- modules ----------

    [HttpGet("modules")]
    public async Task<ActionResult<List<DtProductModuleDto>>> Modules([FromQuery] bool all = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        await EnsureAsync(a => a.CanView);
        return Ok(await Paging.ResultAsync(async pagination => await _svc.GetModulesAsync(all, pagination: pagination), skip, take));
    }

    [HttpPost("modules")]
    public async Task<ActionResult<DtProductModuleDto>> UpsertModule([FromBody] DtProductModuleDto dto)
    {
        await EnsureAsync(a => a.CanManage);
        return Ok(await _svc.UpsertModuleAsync(dto));
    }

    [HttpDelete("modules/{id:int}")]
    public async Task<IActionResult> DeleteModule(int id)
    {
        await EnsureAsync(a => a.CanManage);
        await _svc.DeleteModuleAsync(id);
        return Ok(new { message = "حذف/غیرفعال شد" });
    }

    // ---------- sprints ----------

    [HttpGet("sprints")]
    public async Task<ActionResult<List<DtSprintDto>>> Sprints([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        await EnsureAsync(a => a.CanView);
        return Ok(await Paging.ResultAsync(async pagination => await _svc.GetSprintsAsync(pagination: pagination), skip, take));
    }

    [HttpPost("sprints")]
    public async Task<ActionResult<DtSprintDto>> UpsertSprint([FromBody] DtSprintDto dto)
    {
        await EnsureAsync(a => a.CanManage || a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.UpsertSprintAsync(dto, MyUserId, name));
    }

    [HttpDelete("sprints/{id:int}")]
    public async Task<IActionResult> DeleteSprint(int id)
    {
        await EnsureAsync(a => a.CanManage);
        await _svc.DeleteSprintAsync(id);
        return Ok(new { message = "حذف شد" });
    }

    // ---------- tasks ----------

    [HttpGet("tasks")]
    public async Task<ActionResult<DtPagedResult<DtTaskListItemDto>>> Tasks(
        [FromQuery] string? q = null,
        [FromQuery] int? statusId = null,
        [FromQuery] int? moduleId = null,
        [FromQuery] int? sprintId = null,
        [FromQuery] int? assigneeUserId = null,
        [FromQuery] int? priority = null,
        [FromQuery] string? type = null,
        [FromQuery] bool? onlyMine = null,
        [FromQuery] bool? onlyOverdue = null,
        [FromQuery] bool? includeDone = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, [FromQuery] int? skip = null, [FromQuery] int? take = null)
    {
        page = Paging.ToPage(skip, take, page);
        pageSize = Paging.ToPageSize(take, pageSize);
        await EnsureAsync(a => a.CanView);
        var query = new DtTaskQuery
        {
            Q = q, StatusId = statusId, ModuleId = moduleId, SprintId = sprintId,
            AssigneeUserId = assigneeUserId, Priority = priority, Type = type,
            OnlyMine = onlyMine, OnlyOverdue = onlyOverdue, IncludeDone = includeDone,
            Page = page, PageSize = pageSize
        };
        return Ok(await _svc.QueryTasksAsync(query, MyUserId));
    }

    [HttpGet("board")]
    public async Task<ActionResult<List<DtTaskListItemDto>>> Board(
        [FromQuery] int? sprintId = null,
        [FromQuery] int? moduleId = null,
        [FromQuery] int? assigneeUserId = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        await EnsureAsync(a => a.CanView);
        return Ok(await Paging.ResultAsync(pagination => _svc.BoardTasksAsync(sprintId, moduleId, assigneeUserId, pagination), skip, take));
    }

    [HttpGet("tasks/{id:int}")]
    public async Task<ActionResult<DtTaskDetailDto>> GetTask(int id)
    {
        await EnsureAsync(a => a.CanView);
        var t = await _svc.GetTaskAsync(id);
        if (t is null) return NotFound(new { message = "تسک یافت نشد" });
        return Ok(t);
    }

    [HttpPost("tasks")]
    public async Task<ActionResult<DtTaskDetailDto>> CreateTask([FromBody] DtTaskUpsertDto dto)
    {
        await EnsureAsync(a => a.CanCreate);
        // تخصیص به دیگران نیاز به Assign دارد
        if (dto.AssigneeUserId is int aid && aid != MyUserId)
        {
            var a = await AccessAsync();
            if (!a.CanAssign && !a.CanManage)
                throw new InvalidOperationException("مجوز تخصیص به دیگران را ندارید.");
        }
        var name = await DisplayNameAsync();
        return Ok(await _svc.CreateTaskAsync(dto, MyUserId, name));
    }

    [HttpPut("tasks/{id:int}")]
    public async Task<ActionResult<DtTaskDetailDto>> UpdateTask(int id, [FromBody] DtTaskUpsertDto dto)
    {
        await EnsureAsync(a => a.CanUpdate);
        if (dto.AssigneeUserId is int aid && aid != MyUserId)
        {
            var a = await AccessAsync();
            if (!a.CanAssign && !a.CanManage)
                throw new InvalidOperationException("مجوز تخصیص به دیگران را ندارید.");
        }
        var name = await DisplayNameAsync();
        return Ok(await _svc.UpdateTaskAsync(id, dto, MyUserId, name));
    }

    [HttpPost("tasks/{id:int}/move")]
    public async Task<IActionResult> MoveTask(int id, [FromBody] DtTaskMoveDto dto)
    {
        await EnsureAsync(a => a.CanUpdate);
        var name = await DisplayNameAsync();
        await _svc.MoveTaskAsync(id, dto.StatusId, MyUserId, name, dto.BeforeTaskId);
        return Ok(new { message = "وضعیت به‌روز شد" });
    }

    /// <summary>مرتب‌سازی ساب‌تسک‌های یک والد (فاز ۳).</summary>
    [HttpPost("tasks/{id:int}/subtasks/reorder")]
    public async Task<IActionResult> ReorderSubTasks(int id, [FromBody] DtReorderSubTasksDto dto)
    {
        await EnsureAsync(a => a.CanUpdate);
        var name = await DisplayNameAsync();
        await _svc.ReorderSubTasksAsync(id, dto.OrderedIds ?? new List<int>(), MyUserId, name);
        return Ok(new { message = "ترتیب ساب‌تسک‌ها ذخیره شد" });
    }

    [HttpPost("tasks/{id:int}/timer/start")]
    public async Task<ActionResult<DtTimerStateDto>> StartTimer(int id)
    {
        await EnsureAsync(a => a.CanUpdate || a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.StartTimerAsync(id, MyUserId, name));
    }

    [HttpPost("tasks/{id:int}/timer/stop")]
    public async Task<ActionResult<DtTimerStateDto>> StopTimer(int id, [FromBody] DtTimeEntryCreateDto? body = null)
    {
        await EnsureAsync(a => a.CanUpdate || a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.StopTimerAsync(id, MyUserId, name, body?.Note));
    }

    [HttpGet("tasks/{id:int}/timer")]
    public async Task<ActionResult<DtTimerStateDto>> GetTimer(int id)
    {
        await EnsureAsync(a => a.CanView);
        var s = await _svc.GetTimerAsync(id);
        if (s is null) return NotFound();
        return Ok(s);
    }

    /// <summary>تایمر زندهٔ کاربر جاری (برای نوار سراسری صفحه).</summary>
    [HttpGet("my-timer")]
    public async Task<ActionResult<DtTimerStateDto>> MyTimer()
    {
        await EnsureAsync(a => a.CanView);
        var s = await _svc.GetMyTimerAsync(MyUserId);
        if (s is null) return Ok(new DtTimerStateDto { Running = false });
        return Ok(s);
    }

    /// <summary>گزارش burndown اسپرینت (فاز ۳).</summary>
    [HttpGet("burndown")]
    public async Task<ActionResult<DtBurndownDto>> Burndown([FromQuery] int? sprintId = null)
    {
        await EnsureAsync(a => a.CanView);
        return Ok(await _svc.GetBurndownAsync(sprintId));
    }

    [HttpDelete("tasks/{id:int}")]
    public async Task<IActionResult> DeleteTask(int id)
    {
        await EnsureAsync(a => a.CanDelete);
        var name = await DisplayNameAsync();
        await _svc.DeleteTaskAsync(id, MyUserId, name);
        return Ok(new { message = "حذف شد" });
    }

    [HttpPost("tasks/{id:int}/comments")]
    public async Task<ActionResult<DtTaskCommentDto>> AddComment(int id, [FromBody] DtTaskCommentDto body)
    {
        await EnsureAsync(a => a.CanView); // هر بیننده‌ای بتواند نظر بگذارد
        var name = await DisplayNameAsync();
        return Ok(await _svc.AddCommentAsync(id, body.Text, MyUserId, name));
    }

    [HttpPost("tasks/{id:int}/git-links")]
    public async Task<ActionResult<DtTaskGitLinkDto>> AddGitLink(int id, [FromBody] DtTaskGitLinkCreateDto dto)
    {
        await EnsureAsync(a => a.CanUpdate || a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.AddGitLinkAsync(id, dto, MyUserId, name));
    }

    [HttpDelete("git-links/{id:int}")]
    public async Task<IActionResult> DeleteGitLink(int id)
    {
        await EnsureAsync(a => a.CanUpdate || a.CanDelete);
        await _svc.DeleteGitLinkAsync(id);
        return Ok(new { message = "حذف شد" });
    }

    [HttpPost("tasks/{id:int}/time")]
    public async Task<ActionResult<DtTimeEntryDto>> AddTime(int id, [FromBody] DtTimeEntryCreateDto dto)
    {
        await EnsureAsync(a => a.CanUpdate || a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.AddTimeAsync(id, dto, MyUserId, name));
    }

    // ---------- checklist (path C) ----------

    [HttpPost("tasks/{id:int}/checklist")]
    public async Task<ActionResult<DtChecklistItemDto>> AddChecklist(int id, [FromBody] DtChecklistItemCreateDto dto)
    {
        await EnsureAsync(a => a.CanUpdate || a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.AddChecklistItemAsync(id, dto.Title, MyUserId, name));
    }

    [HttpPost("checklist/{itemId:int}/toggle")]
    public async Task<ActionResult<DtChecklistItemDto>> ToggleChecklist(int itemId)
    {
        await EnsureAsync(a => a.CanUpdate || a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.ToggleChecklistItemAsync(itemId, MyUserId, name));
    }

    [HttpDelete("checklist/{itemId:int}")]
    public async Task<IActionResult> DeleteChecklist(int itemId)
    {
        await EnsureAsync(a => a.CanUpdate || a.CanDelete);
        var name = await DisplayNameAsync();
        await _svc.DeleteChecklistItemAsync(itemId, MyUserId, name);
        return Ok(new { message = "حذف شد" });
    }

    [HttpPost("tasks/{id:int}/checklist/reorder")]
    public async Task<IActionResult> ReorderChecklist(int id, [FromBody] DtChecklistReorderDto dto)
    {
        await EnsureAsync(a => a.CanUpdate);
        var name = await DisplayNameAsync();
        await _svc.ReorderChecklistAsync(id, dto.OrderedIds ?? new List<int>(), MyUserId, name);
        return Ok(new { message = "ترتیب ذخیره شد" });
    }

    /// <summary>ساخت تسک از مشکل + چک‌لیست پیش‌فرض.</summary>
    [HttpPost("problems/{id:int}/create-task")]
    public async Task<ActionResult<DtTaskDetailDto>> CreateTaskFromProblem(int id, [FromBody] DtCreateTaskFromProblemDto? dto = null)
    {
        await EnsureAsync(a => a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.CreateTaskFromProblemAsync(id, dto, MyUserId, name));
    }

    /// <summary>جستجوی سبک تسک‌ها (برای انتخاب والد/وابستگی).</summary>
    [HttpGet("tasks/search")]
    public async Task<ActionResult<List<DtTaskListItemDto>>> SearchTasks(
        [FromQuery] string? q = null,
        [FromQuery] int? excludeId = null,
        [FromQuery] int take = 20, [FromQuery] int skip = 0)
    {
        await EnsureAsync(a => a.CanView);
        return Ok(await _svc.SearchTasksAsync(q, excludeId, take, skip));
    }

    /// <summary>ساخت ساب‌تسک زیر یک تسک.</summary>
    [HttpPost("tasks/{id:int}/subtasks")]
    public async Task<ActionResult<DtTaskDetailDto>> CreateSubTask(int id, [FromBody] DtTaskUpsertDto dto)
    {
        await EnsureAsync(a => a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.CreateSubTaskAsync(id, dto, MyUserId, name));
    }

    public class DtParentDto { public int ParentTaskId { get; set; } }

    /// <summary>تبدیل تسک موجود به ساب‌تسکِ ParentTaskId.</summary>
    [HttpPost("tasks/{id:int}/make-subtask")]
    public async Task<IActionResult> MakeSubTask(int id, [FromBody] DtParentDto body)
    {
        await EnsureAsync(a => a.CanUpdate);
        if (body.ParentTaskId <= 0) throw new InvalidOperationException("والد نامعتبر است.");
        var name = await DisplayNameAsync();
        await _svc.ConvertToSubTaskAsync(id, body.ParentTaskId, MyUserId, name);
        return Ok(new { message = "تبدیل شد" });
    }

    /// <summary>ارتقا ساب‌تسک به تسک ریشه.</summary>
    [HttpPost("tasks/{id:int}/promote")]
    public async Task<IActionResult> Promote(int id)
    {
        await EnsureAsync(a => a.CanUpdate);
        var name = await DisplayNameAsync();
        await _svc.PromoteSubTaskAsync(id, MyUserId, name);
        return Ok(new { message = "ارتقا یافت" });
    }

    /// <summary>افزودن وابستگی: این تسک منتظر dependsOn می‌ماند.</summary>
    [HttpPost("tasks/{id:int}/dependencies")]
    public async Task<ActionResult<DtDependencyDto>> AddDependency(int id, [FromBody] DtDependencyCreateDto dto)
    {
        await EnsureAsync(a => a.CanUpdate || a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.AddDependencyAsync(id, dto, MyUserId, name));
    }

    [HttpDelete("dependencies/{id:int}")]
    public async Task<IActionResult> RemoveDependency(int id)
    {
        await EnsureAsync(a => a.CanUpdate || a.CanDelete);
        var name = await DisplayNameAsync();
        await _svc.RemoveDependencyAsync(id, MyUserId, name);
        return Ok(new { message = "حذف شد" });
    }

    /// <summary>ساخت دستور کار از روی تسک و اتصال دوطرفه (SourceModule=DevTeam).</summary>
    [HttpPost("tasks/{id:int}/work-order")]
    public async Task<ActionResult<DtWorkOrderLinkDto>> CreateWorkOrder(int id, [FromBody] DtCreateWorkOrderDto? dto = null)
    {
        await EnsureAsync(a => a.CanCreate || a.CanUpdate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.CreateWorkOrderFromTaskAsync(id, dto ?? new DtCreateWorkOrderDto(), MyUserId, name));
    }

    /// <summary>اتصال دستی به یک دستور کار موجود.</summary>
    [HttpPost("tasks/{id:int}/work-order/link")]
    public async Task<ActionResult<DtWorkOrderLinkDto>> LinkWorkOrder(int id, [FromBody] DtWorkOrderLinkDto body)
    {
        await EnsureAsync(a => a.CanUpdate);
        if (body.WorkOrderId <= 0) throw new InvalidOperationException("شناسه دستور کار نامعتبر است.");
        var name = await DisplayNameAsync();
        return Ok(await _svc.LinkWorkOrderAsync(id, body.WorkOrderId, MyUserId, name));
    }

    [HttpDelete("tasks/{id:int}/work-order")]
    public async Task<IActionResult> UnlinkWorkOrder(int id)
    {
        await EnsureAsync(a => a.CanUpdate);
        var name = await DisplayNameAsync();
        await _svc.UnlinkWorkOrderAsync(id, MyUserId, name);
        return Ok(new { message = "اتصال قطع شد" });
    }

    // ---------- problems ----------

    [HttpGet("problems")]
    public async Task<ActionResult<List<DtProblemDto>>> Problems(
        [FromQuery] string? severity = null,
        [FromQuery] string? status = null,
        [FromQuery] int? moduleId = null,
        [FromQuery] int? taskId = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        await EnsureAsync(a => a.CanView);
        return Ok(await Paging.ResultAsync(async pagination => await _svc.QueryProblemsAsync(severity, status, moduleId, taskId, pagination: pagination), skip, take));
    }

    [HttpPost("problems")]
    public async Task<ActionResult<DtProblemDto>> CreateProblem([FromBody] DtProblemUpsertDto dto)
    {
        await EnsureAsync(a => a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.UpsertProblemAsync(null, dto, MyUserId, name));
    }

    [HttpPut("problems/{id:int}")]
    public async Task<ActionResult<DtProblemDto>> UpdateProblem(int id, [FromBody] DtProblemUpsertDto dto)
    {
        await EnsureAsync(a => a.CanUpdate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.UpsertProblemAsync(id, dto, MyUserId, name));
    }

    [HttpDelete("problems/{id:int}")]
    public async Task<IActionResult> DeleteProblem(int id)
    {
        await EnsureAsync(a => a.CanDelete);
        await _svc.DeleteProblemAsync(id);
        return Ok(new { message = "حذف شد" });
    }

    // ---------- changelog ----------

    [HttpGet("changes")]
    public async Task<ActionResult<List<DtModuleChangeDto>>> Changes(
        [FromQuery] int? moduleId = null,
        [FromQuery] int take = 50, [FromQuery] int skip = 0)
    {
        await EnsureAsync(a => a.CanView);
        return Ok(await _svc.QueryChangesAsync(moduleId, take, skip));
    }

    [HttpPost("changes")]
    public async Task<ActionResult<DtModuleChangeDto>> CreateChange([FromBody] DtModuleChangeCreateDto dto)
    {
        await EnsureAsync(a => a.CanCreate);
        var name = await DisplayNameAsync();
        return Ok(await _svc.CreateChangeAsync(dto, MyUserId, name));
    }

    [HttpDelete("changes/{id:int}")]
    public async Task<IActionResult> DeleteChange(int id)
    {
        await EnsureAsync(a => a.CanDelete || a.CanManage);
        await _svc.DeleteChangeAsync(id);
        return Ok(new { message = "حذف شد" });
    }
}
