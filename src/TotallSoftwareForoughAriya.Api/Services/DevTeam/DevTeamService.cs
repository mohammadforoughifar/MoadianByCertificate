using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Inventory.Api.Services.DevTeam;

public interface IDevTeamService
{
    Task<DtAccessDto> GetAccessAsync(int userId, bool isLegacyAdmin, IEnumerable<string> legacyRoles);
    Task<DtLookupsDto> GetLookupsAsync();
    Task<DtDashboardDto> GetDashboardAsync(int? sprintId = null);

    // Statuses
    Task<List<DtWorkflowStatusDto>> GetStatusesAsync(bool includeInactive = false, Paging.Request? pagination = null);
    Task<DtWorkflowStatusDto> UpsertStatusAsync(DtWorkflowStatusDto dto);
    Task DeleteStatusAsync(int id);

    // Modules
    Task<List<DtProductModuleDto>> GetModulesAsync(bool includeInactive = false, Paging.Request? pagination = null);
    Task<DtProductModuleDto> UpsertModuleAsync(DtProductModuleDto dto);
    Task DeleteModuleAsync(int id);

    // Sprints
    Task<List<DtSprintDto>> GetSprintsAsync(Paging.Request? pagination = null);
    Task<DtSprintDto> UpsertSprintAsync(DtSprintDto dto, int userId, string userName);
    Task DeleteSprintAsync(int id);

    // Tasks
    Task<DtPagedResult<DtTaskListItemDto>> QueryTasksAsync(DtTaskQuery q, int currentUserId, Paging.Request? pagination = null);
    Task<List<DtTaskListItemDto>> BoardTasksAsync(int? sprintId, int? moduleId, int? assigneeUserId, Paging.Request? pagination = null);
    Task<DtTaskDetailDto?> GetTaskAsync(int id);
    Task<DtTaskDetailDto> CreateTaskAsync(DtTaskUpsertDto dto, int userId, string userName);
    Task<DtTaskDetailDto> UpdateTaskAsync(int id, DtTaskUpsertDto dto, int userId, string userName);
    Task MoveTaskAsync(int id, int statusId, int userId, string userName, int? beforeTaskId = null);

    // Path C — checklist + task-from-problem
    Task<DtChecklistItemDto> AddChecklistItemAsync(int taskId, string title, int userId, string userName);
    Task<DtChecklistItemDto> ToggleChecklistItemAsync(int itemId, int userId, string userName);
    Task DeleteChecklistItemAsync(int itemId, int userId, string userName);
    Task ReorderChecklistAsync(int taskId, IReadOnlyList<int> orderedIds, int userId, string userName);
    Task<DtTaskDetailDto> CreateTaskFromProblemAsync(int problemId, DtCreateTaskFromProblemDto? dto, int userId, string userName);
    Task DeleteTaskAsync(int id, int userId, string userName);
    Task<DtTaskCommentDto> AddCommentAsync(int taskId, string text, int userId, string userName);
    Task<DtTaskGitLinkDto> AddGitLinkAsync(int taskId, DtTaskGitLinkCreateDto dto, int userId, string userName);
    Task DeleteGitLinkAsync(int linkId);
    Task<DtTimeEntryDto> AddTimeAsync(int taskId, DtTimeEntryCreateDto dto, int userId, string userName);

    // Phase 3 — timer / reorder / burndown / CI
    Task ReorderSubTasksAsync(int parentId, IReadOnlyList<int> orderedIds, int userId, string userName);
    Task<DtTimerStateDto> StartTimerAsync(int taskId, int userId, string userName);
    Task<DtTimerStateDto> StopTimerAsync(int taskId, int userId, string userName, string? note = null);
    Task<DtTimerStateDto?> GetTimerAsync(int taskId);
    Task<DtTimerStateDto?> GetMyTimerAsync(int userId);
    Task<DtBurndownDto> GetBurndownAsync(int? sprintId = null);
    Task<DtGitWebhookResultDto> ProcessCiEventAsync(DtCiBuildEventDto dto);

    // Sub-tasks & dependencies
    Task<DtTaskDetailDto> CreateSubTaskAsync(int parentId, DtTaskUpsertDto dto, int userId, string userName);
    Task ConvertToSubTaskAsync(int taskId, int parentId, int userId, string userName);
    Task PromoteSubTaskAsync(int taskId, int userId, string userName);
    Task<DtDependencyDto> AddDependencyAsync(int taskId, DtDependencyCreateDto dto, int userId, string userName);
    Task RemoveDependencyAsync(int dependencyId, int userId, string userName);
    Task<List<DtTaskListItemDto>> SearchTasksAsync(string? q, int? excludeId, int take = 20, int skip = 0);

    // WorkOrder bridge
    Task<DtWorkOrderLinkDto> CreateWorkOrderFromTaskAsync(int taskId, DtCreateWorkOrderDto dto, int userId, string userName);
    Task<DtWorkOrderLinkDto> LinkWorkOrderAsync(int taskId, int workOrderId, int userId, string userName);
    Task UnlinkWorkOrderAsync(int taskId, int userId, string userName);

    // Problems
    Task<List<DtProblemDto>> QueryProblemsAsync(string? severity, string? status, int? moduleId, int? taskId, Paging.Request? pagination = null);
    Task<DtProblemDto> UpsertProblemAsync(int? id, DtProblemUpsertDto dto, int userId, string userName);
    Task DeleteProblemAsync(int id);

    // Changelog
    Task<List<DtModuleChangeDto>> QueryChangesAsync(int? moduleId, int take = 50, int skip = 0);
    Task<DtModuleChangeDto> CreateChangeAsync(DtModuleChangeCreateDto dto, int userId, string userName);
    Task DeleteChangeAsync(int id);

    // Git webhook
    Task<DtGitWebhookResultDto> ProcessGitHubPushAsync(JsonElement payload);
    Task<DtGitWebhookResultDto> ProcessGitLabPushAsync(JsonElement payload);

    // Path A — operational integration
    Task<DtIntegrationStatusDto> GetIntegrationStatusAsync(string? publicBaseUrl = null);
}

public class DevTeamService : IDevTeamService
{
    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly IConfiguration _config;

    /// <summary>شناسه تسک داخل پیام کامیت: DT-1405-0001 یا #DT-1405-0001 یا task:123</summary>
    private static readonly Regex TaskRefRx = new(
        @"\b(?:#)?(DT-\d{4}-\d{3,})\b|\btask[:\s#]*(\d{1,7})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModuleKeyRx = new(
        @"\bmod(?:ule)?[:\s/]*([A-Za-z][A-Za-z0-9_]{1,39})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DevTeamService(AppDbContext db, INotifyService notify, IConfiguration config)
    {
        _db = db;
        _notify = notify;
        _config = config;
    }

    private async Task NotifyAssigneeAsync(int? assigneeUserId, string title, string body, string fromName, int taskId)
    {
        if (assigneeUserId is not int uid || uid <= 0) return;
        try
        {
            await _notify.SendAsync(uid, title, body, fromName, "میز کار توسعه", $"/dev-team?open={taskId}");
            await _notify.BroadcastChangedAsync("devteam");
        }
        catch { /* اعلان نباید جریان اصلی را بشکند */ }
    }

    // ---------- helpers ----------
    private async Task<bool> HasPermAsync(int userId, string action, bool isLegacyAdmin, IEnumerable<string> legacyRoles)
    {
        if (isLegacyAdmin) return true;

        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == userId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = legacyRoles.FirstOrDefault() ?? "";
            if (legacy == "Admin") return true;
            if (legacy == "Operator") return action is "View" or "Create" or "Update";
            return action == "View";
        }

        return await _db.UserRoles
            .Where(ur => ur.UserId == userId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == "DevTeam" && p.Action == action);
    }

    private static string ActivityFa(string action) => action switch
    {
        "Created" => "ایجاد شد",
        "StatusChanged" => "تغییر وضعیت",
        "Assigned" => "تخصیص",
        "Updated" => "ویرایش",
        "Commented" => "نظر",
        "GitLinked" => "لینک گیت",
        "TimeLogged" => "ثبت زمان",
        "Deleted" => "حذف",
        "WorkOrderLinked" => "اتصال دستور کار",
        "WorkOrderUnlinked" => "قطع دستور کار",
        "GitWebhook" => "وب‌هوک گیت",
        "SubTaskCreated" => "ساب‌تسک",
        "ConvertedToSubTask" => "تبدیل به ساب‌تسک",
        "Promoted" => "ارتقا به تسک اصلی",
        "DependencyAdded" => "وابستگی",
        "DependencyRemoved" => "حذف وابستگی",
        _ => action
    };

    private Task LogAsync(int taskId, int userId, string userName, string action, string? detail)
    {
        _db.DtTaskActivities.Add(new DtTaskActivity
        {
            TaskId = taskId,
            ActorUserId = userId,
            ActorName = userName,
            Action = action,
            Detail = detail,
            CreatedAt = DateTime.Now
        });
        return Task.CompletedTask;
    }

    private async Task<string> NextNumberAsync()
    {
        var jy = PersianDate.FromGregorian(DateTime.Now).Year;
        var prefix = $"DT-{jy}-";
        var last = await _db.DtTasks.AsNoTracking()
            .Where(t => t.Number.StartsWith(prefix))
            .OrderByDescending(t => t.Number)
            .Select(t => t.Number)
            .FirstOrDefaultAsync();

        var serial = 1;
        if (!string.IsNullOrEmpty(last) && last.Length > prefix.Length &&
            int.TryParse(last[prefix.Length..], out var n))
            serial = n + 1;

        return prefix + serial.ToString("D4");
    }

    private async Task<(string? name, string username)> UserNameAsync(int? userId)
    {
        if (userId is null or 0) return (null, "");
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (u is null) return (null, "");
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return (string.IsNullOrWhiteSpace(full) ? u.Username : full, u.Username);
    }

    private IQueryable<DtTask> TasksBase() =>
        _db.DtTasks.AsNoTracking().Where(t => !t.IsDeleted);

    private DtTaskListItemDto MapList(DtTask t, DtWorkflowStatus? st, DtProductModule? mod, DtSprint? sp,
        int commentCount = 0, int gitCount = 0, int openProblems = 0,
        int? parentId = null, string? parentNumber = null, string? parentTitle = null,
        int subCount = 0, int subDone = 0, int openBlockers = 0)
    {
        var now = DateTime.Now;
        return new DtTaskListItemDto
        {
            Id = t.Id,
            Number = t.Number,
            Title = t.Title,
            Type = t.Type,
            TypeFa = DtTaskType.ToFa(t.Type),
            Priority = t.Priority,
            PriorityFa = DtPriority.ToFa(t.Priority),
            StatusId = t.StatusId,
            StatusKey = st?.Key ?? "",
            StatusName = st?.NameFa ?? "",
            StatusColor = st?.Color ?? "#64748b",
            StatusIsDone = st?.IsDone ?? false,
            StatusIsBlocked = st?.IsBlocked ?? false,
            ModuleId = t.ModuleId,
            ModuleKey = mod?.Key,
            ModuleName = mod?.NameFa,
            ModuleColor = mod?.Color,
            SprintId = t.SprintId,
            SprintName = sp?.Name,
            AssigneeUserId = t.AssigneeUserId,
            AssigneeName = t.AssigneeName,
            ReporterName = t.ReporterName,
            DueAt = t.DueAt,
            IsOverdue = t.DueAt.HasValue && t.DueAt.Value < now && !(st?.IsDone ?? false),
            EstimateHours = t.EstimateHours,
            SpentHours = t.SpentHours,
            Progress = t.Progress,
            Tags = t.Tags,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            CommentCount = commentCount,
            GitLinkCount = gitCount,
            OpenProblemCount = openProblems,
            WorkOrderId = t.WorkOrderId,
            WorkOrderNumber = t.WorkOrderNumber,
            ParentTaskId = parentId ?? t.ParentTaskId,
            ParentTaskNumber = parentNumber,
            ParentTaskTitle = parentTitle,
            SubTaskCount = subCount,
            SubTaskDoneCount = subDone,
            OpenBlockerCount = openBlockers,
            IsDependencyBlocked = openBlockers > 0 && !(st?.IsDone ?? false),
            SortOrder = t.SortOrder,
            TimerRunning = t.TimerStartedAt != null,
            TimerStartedAt = t.TimerStartedAt,
            TimerStartedByUserId = t.TimerStartedByUserId
        };
    }

    private const int MaxSubTaskDepth = 3;

    private async Task<int> GetDepthAsync(int? parentId)
    {
        var depth = 0;
        var cur = parentId;
        while (cur is int pid && depth < MaxSubTaskDepth + 2)
        {
            depth++;
            cur = await _db.DtTasks.AsNoTracking()
                .Where(t => t.Id == pid).Select(t => t.ParentTaskId).FirstOrDefaultAsync();
        }
        return depth;
    }

    private async Task EnsureNoCycleParentAsync(int taskId, int newParentId)
    {
        if (taskId == newParentId)
            throw new InvalidOperationException("تسک نمی‌تواند والد خودش باشد.");
        var cur = (int?)newParentId;
        var guard = 0;
        while (cur is int pid && guard++ < 20)
        {
            if (pid == taskId)
                throw new InvalidOperationException("حلقه در سلسله‌مراتب ساب‌تسک ایجاد می‌شود.");
            cur = await _db.DtTasks.AsNoTracking()
                .Where(t => t.Id == pid).Select(t => t.ParentTaskId).FirstOrDefaultAsync();
        }
    }

    private async Task EnsureNoDependencyCycleAsync(int taskId, int dependsOnId)
    {
        if (taskId == dependsOnId)
            throw new InvalidOperationException("تسک نمی‌تواند به خودش وابسته باشد.");

        // اگر dependsOn (مستقیم/غیرمستقیم) به task وابسته باشد → حلقه
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(dependsOnId);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!visited.Add(cur)) continue;
            if (cur == taskId)
                throw new InvalidOperationException("این وابستگی حلقه ایجاد می‌کند.");
            var next = await _db.DtTaskDependencies.AsNoTracking()
                .Where(d => d.TaskId == cur && d.Kind == DtDependencyKind.Blocks)
                .Select(d => d.DependsOnTaskId).ToListAsync();
            foreach (var n in next) queue.Enqueue(n);
        }
    }


    private async Task EnsureWipAllowsAsync(int statusId, int? excludeTaskId = null)
    {
        var st = await _db.DtWorkflowStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == statusId);
        if (st is null || st.WipLimit is null || st.WipLimit <= 0) return;
        // Done/Blocked columns usually unlimited unless explicitly set
        var count = await _db.DtTasks.CountAsync(t =>
            !t.IsDeleted && t.ParentTaskId == null && t.StatusId == statusId &&
            (excludeTaskId == null || t.Id != excludeTaskId));
        if (count >= st.WipLimit.Value)
            throw new InvalidOperationException(
                $"سقف WIP ستون «{st.NameFa}» ({st.WipLimit}) پر است. ابتدا تسکی را جابه‌جا یا تمام کنید.");
    }

    private async Task EnsureNoOpenBlockersAsync(int taskId)
    {
        var doneIds = await _db.DtWorkflowStatuses.AsNoTracking().Where(s => s.IsDone).Select(s => s.Id).ToListAsync();
        var openBlockers = await (
            from d in _db.DtTaskDependencies.AsNoTracking()
            where d.TaskId == taskId && d.Kind == DtDependencyKind.Blocks
            join dep in _db.DtTasks.AsNoTracking() on d.DependsOnTaskId equals dep.Id
            where !dep.IsDeleted && !doneIds.Contains(dep.StatusId)
            select dep.Number
        ).ToListAsync();
        if (openBlockers.Count > 0)
            throw new InvalidOperationException(
                $"ابتدا وابستگی‌های باز را تمام کنید: {string.Join("، ", openBlockers.Take(5))}");
    }

    private async Task<(Dictionary<int, (int total, int done)> subs, Dictionary<int, int> openBlockers, Dictionary<int, (string Number, string Title)> parents)>
        LoadRelationMapsAsync(List<int> taskIds, HashSet<int> doneStatusIds)
    {
        var parentIds = await _db.DtTasks.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id) && t.ParentTaskId != null)
            .Select(t => new { t.Id, ParentId = t.ParentTaskId!.Value }).ToListAsync();

        var pIds = parentIds.Select(x => x.ParentId).Distinct().ToList();
        var parentRows = pIds.Count == 0
            ? new List<(int Id, string Number, string Title)>()
            : (await _db.DtTasks.AsNoTracking().Where(t => pIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Number, t.Title }).ToListAsync())
              .Select(t => (t.Id, t.Number, t.Title)).ToList();
        var parents = parentRows.ToDictionary(x => x.Id, x => (x.Number, x.Title));

        // children of these tasks
        var children = await _db.DtTasks.AsNoTracking()
            .Where(t => !t.IsDeleted && t.ParentTaskId != null && taskIds.Contains(t.ParentTaskId.Value))
            .Select(t => new { Parent = t.ParentTaskId!.Value, t.StatusId }).ToListAsync();
        var subs = children.GroupBy(c => c.Parent)
            .ToDictionary(g => g.Key, g => (total: g.Count(), done: g.Count(x => doneStatusIds.Contains(x.StatusId))));

        // open blockers: BlockedBy links where dependsOn is not done
        var deps = await _db.DtTaskDependencies.AsNoTracking()
            .Where(d => taskIds.Contains(d.TaskId) && d.Kind == DtDependencyKind.Blocks)
            .Select(d => new { d.TaskId, d.DependsOnTaskId }).ToListAsync();
        var depTargetIds = deps.Select(d => d.DependsOnTaskId).Distinct().ToList();
        var depStatuses = depTargetIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.DtTasks.AsNoTracking().Where(t => depTargetIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.StatusId);

        var openBlockers = deps.GroupBy(d => d.TaskId).ToDictionary(
            g => g.Key,
            g => g.Count(d =>
                !depStatuses.TryGetValue(d.DependsOnTaskId, out var sid) || !doneStatusIds.Contains(sid)));

        return (subs, openBlockers, parents);
    }

    // ---------- access ----------
    public async Task<DtAccessDto> GetAccessAsync(int userId, bool isLegacyAdmin, IEnumerable<string> legacyRoles)
    {
        var roles = legacyRoles.ToList();
        return new DtAccessDto
        {
            UserId = userId,
            CanView = await HasPermAsync(userId, "View", isLegacyAdmin, roles),
            CanCreate = await HasPermAsync(userId, "Create", isLegacyAdmin, roles),
            CanUpdate = await HasPermAsync(userId, "Update", isLegacyAdmin, roles),
            CanDelete = await HasPermAsync(userId, "Delete", isLegacyAdmin, roles),
            CanManage = await HasPermAsync(userId, "Manage", isLegacyAdmin, roles),
            CanAssign = await HasPermAsync(userId, "Assign", isLegacyAdmin, roles),
        };
    }
    public async Task<DtIntegrationStatusDto> GetIntegrationStatusAsync(string? publicBaseUrl = null)
    {
        var enabled = string.Equals(_config["DevTeam:GitWebhookEnabled"] ?? "true", "true", StringComparison.OrdinalIgnoreCase);
        var cfgSecret = _config["DevTeam:GitWebhookSecret"] ?? _config["DevTeam__GitWebhookSecret"];
        var envSecret = Environment.GetEnvironmentVariable("DEVTEAM_GIT_WEBHOOK_SECRET");
        var secretConfigured = !string.IsNullOrWhiteSpace(cfgSecret) || !string.IsNullOrWhiteSpace(envSecret);
        var fromEnv = !string.IsNullOrWhiteSpace(envSecret);

        var baseUrl = (publicBaseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "https://YOUR-HOST";

        DtWebhookEndpointInfoDto Ep(string key, string path, string title, string auth, string events) => new()
        {
            Key = key,
            Method = "POST",
            Path = path,
            FullUrl = baseUrl + path,
            TitleFa = title,
            AuthHintFa = auth,
            EventsHintFa = events
        };

        var endpoints = new List<DtWebhookEndpointInfoDto>
        {
            Ep("github", "/api/dev-team/hooks/github", "GitHub Push",
                "X-Hub-Signature-256 (HMAC) یا هدر X-DevTeam-Token", "push"),
            Ep("gitlab", "/api/dev-team/hooks/gitlab", "GitLab Push",
                "X-Gitlab-Token یا X-DevTeam-Token", "Push Hook"),
            Ep("ci", "/api/dev-team/hooks/ci", "CI عمومی (JSON)",
                "X-DevTeam-Token", "body: DtCiBuildEventDto — فقط failure"),
            Ep("github-ci", "/api/dev-team/hooks/github-ci", "GitHub Actions",
                "X-Hub-Signature-256 یا X-DevTeam-Token", "workflow_run / check_suite"),
            Ep("gitlab-ci", "/api/dev-team/hooks/gitlab-ci", "GitLab Pipeline",
                "X-Gitlab-Token یا X-DevTeam-Token", "Pipeline Hook"),
            new DtWebhookEndpointInfoDto
            {
                Key = "health", Method = "GET", Path = "/api/dev-team/hooks/health",
                FullUrl = baseUrl + "/api/dev-team/hooks/health",
                TitleFa = "سلامت وب‌هوک",
                AuthHintFa = "عمومی (بدون توکن)",
                EventsHintFa = "—"
            }
        };

        // RBAC summary
        var devPerms = await _db.Permissions.AsNoTracking()
            .Where(p => p.Module == "DevTeam").Select(p => p.Id).ToListAsync();
        var admin = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Name == "Admin");
        var adminHas = false;
        if (admin != null && devPerms.Count > 0)
        {
            var adminPermIds = await _db.RolePermissions.AsNoTracking()
                .Where(rp => rp.RoleId == admin.Id && devPerms.Contains(rp.PermissionId))
                .Select(rp => rp.PermissionId).ToListAsync();
            adminHas = adminPermIds.Count >= devPerms.Count;
        }

        var devRole = await _db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == "DevDeveloper");
        var devUsers = 0;
        if (devRole != null)
        {
            devUsers = await _db.UserRoles.AsNoTracking().CountAsync(ur => ur.RoleId == devRole.Id);
        }

        var steps = new List<string>
        {
            "۱) متغیر DEVTEAM_GIT_WEBHOOK_SECRET (یا DevTeam:GitWebhookSecret) را روی سرور تنظیم کنید.",
            "۲) در GitHub/GitLab وب‌هوک push را به URLهای زیر وصل کنید و همان secret را بگذارید.",
            "۳) برای CI، workflow نمونه در tools/dev-team را کپی کنید و راز DEVTEAM_GIT_WEBHOOK_SECRET را در Secrets ریپو بگذارید.",
            "۴) در پیام کامیت بنویسید: DT-1405-0001 یا task:12 و اختیاری mod:Office",
            "۵) نقش «DevDeveloper» را از تنظیمات ← نقش‌ها به اعضای تیم توسعه بدهید.",
            "۶) با GET /hooks/health و smoke-hooks.sh صحت اتصال را چک کنید."
        };

        var curl = "curl -sS -X POST '" + baseUrl + "/api/dev-team/hooks/ci' \\\n"
            + "  -H 'Content-Type: application/json' \\\n"
            + "  -H 'X-DevTeam-Token: YOUR_SECRET' \\\n"
            + "  -d '{\"provider\":\"generic\",\"status\":\"failure\",\"jobName\":\"build\","
            + "\"repo\":\"org/app\",\"branch\":\"main\",\"commitMessage\":\"fix: build DT-1405-0001\","
            + "\"url\":\"https://ci.example/job/1\"}'";

        return new DtIntegrationStatusDto
        {
            WebhookEnabled = enabled,
            SecretConfigured = secretConfigured,
            SecretFromEnvironment = fromEnv,
            PublicBaseUrl = baseUrl,
            Endpoints = endpoints,
            CommitHints = new List<string>
            {
                "DT-1405-0001  یا  #DT-1405-0001  یا  task:12",
                "mod:Office  (کلید ماژول اختیاری)",
                "کلمات fail/error/باگ/خطا در پیام → Problem"
            },
            SetupStepsFa = steps,
            SampleCurlCi = curl,
            SampleCommitMessage = "fix(office): pagination DT-1405-0003 mod:Office",
            Rbac = new DtRbacSummaryDto
            {
                DeveloperRoleExists = devRole != null,
                DeveloperRoleUserCount = devUsers,
                DevTeamPermissionCount = devPerms.Count,
                AdminHasDevTeamPerms = adminHas,
                DeveloperRoleName = "DevDeveloper",
                HintFa = devRole == null
                    ? "نقش DevDeveloper هنوز seed نشده — یک‌بار API را ری‌استارت کنید."
                    : (devUsers == 0
                        ? "نقش DevDeveloper هست ولی هنوز به کاربری وصل نیست. از «نقش‌ها و دسترسی‌ها» عضو اضافه کنید."
                        : $"نقش DevDeveloper به {devUsers} کاربر وصل است.")
            }
        };
    }


    public async Task<DtLookupsDto> GetLookupsAsync()
    {
        var statuses = await GetStatusesAsync();
        var modules = await GetModulesAsync();
        var sprints = await GetSprintsAsync();
        // نام را بعد از واکشی می‌سازیم تا expression tree EF درگیر Trim/شرط تو در تو نشود
        var userRows = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName })
            .ToListAsync();
        var users = userRows.Select(u =>
        {
            var full = $"{u.FirstName} {u.LastName}".Trim();
            return new DtUserLiteDto
            {
                Id = u.Id,
                Username = u.Username,
                Name = string.IsNullOrWhiteSpace(full) ? u.Username : full
            };
        }).ToList();

        return new DtLookupsDto
        {
            Statuses = statuses,
            Modules = modules,
            Sprints = sprints,
            Users = users,
            TaskTypes = DtTaskType.All.ToList(),
            Severities = DtProblemSeverity.All.ToList(),
            ProblemStatuses = DtProblemStatus.All.ToList(),
            GitLinkKinds = DtGitLinkKind.All.ToList()
        };
    }

    // ---------- dashboard ----------
    public async Task<DtDashboardDto> GetDashboardAsync(int? sprintId = null)
    {
        var statuses = await _db.DtWorkflowStatuses.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        var doneIds = statuses.Where(s => s.IsDone).Select(s => s.Id).ToHashSet();
        var blockedIds = statuses.Where(s => s.IsBlocked).Select(s => s.Id).ToHashSet();
        var doingId = statuses.FirstOrDefault(s => s.Key == "doing")?.Id;

        var sprint = sprintId.HasValue
            ? await _db.DtSprints.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sprintId)
            : await _db.DtSprints.AsNoTracking().FirstOrDefaultAsync(s => s.Status == DtSprintStatus.Active);

        var tasks = await TasksBase().ToListAsync();
        if (sprint is not null)
            tasks = tasks.Where(t => t.SprintId == sprint.Id || (t.SprintId == null && !doneIds.Contains(t.StatusId))).ToList();

        var open = tasks.Where(t => !doneIds.Contains(t.StatusId)).ToList();
        var now = DateTime.Now;

        var modules = await _db.DtProductModules.AsNoTracking().Where(m => m.IsActive).ToListAsync();
        var problems = await _db.DtProblems.AsNoTracking().Where(p => !p.IsDeleted).ToListAsync();
        var openProblems = problems.Where(p => p.Status is DtProblemStatus.Open or DtProblemStatus.Investigating).ToList();

        var since7 = now.AddDays(-7);
        var changes = await _db.DtModuleChanges.AsNoTracking()
            .Where(c => c.ChangedAt >= since7).ToListAsync();

        var dto = new DtDashboardDto
        {
            TotalOpenTasks = open.Count,
            TotalDoneThisSprint = sprint is null ? 0 : tasks.Count(t => t.SprintId == sprint.Id && doneIds.Contains(t.StatusId)),
            OverdueTasks = open.Count(t => t.DueAt.HasValue && t.DueAt < now),
            BlockedTasks = open.Count(t => blockedIds.Contains(t.StatusId)),
            OpenErrors = openProblems.Count(p => p.Severity == DtProblemSeverity.Error),
            OpenWarnings = openProblems.Count(p => p.Severity == DtProblemSeverity.Warning),
            ActiveSprintId = sprint?.Id,
            ActiveSprintName = sprint?.Name,
            SprintEstimateHours = open.Sum(t => t.EstimateHours ?? 0),
            SprintSpentHours = open.Sum(t => t.SpentHours),
        };

        if (sprint is not null)
        {
            var sprintTasks = tasks.Where(t => t.SprintId == sprint.Id).ToList();
            if (sprintTasks.Count > 0)
                dto.ActiveSprintProgressPct = Math.Round(100.0 * sprintTasks.Count(t => doneIds.Contains(t.StatusId)) / sprintTasks.Count, 1);
        }

        dto.StatusCounts = statuses.OrderBy(s => s.SortOrder).Select(s => new DtStatusCountDto
        {
            StatusId = s.Id,
            Key = s.Key,
            Name = s.NameFa,
            Color = s.Color,
            Count = tasks.Count(t => t.StatusId == s.Id)
        }).ToList();

        dto.MemberLoads = open
            .GroupBy(t => new { t.AssigneeUserId, Name = t.AssigneeName ?? "تخصیص‌نیافته" })
            .Select(g => new DtMemberLoadDto
            {
                UserId = g.Key.AssigneeUserId,
                Name = string.IsNullOrWhiteSpace(g.Key.Name) ? "تخصیص‌نیافته" : g.Key.Name,
                OpenTasks = g.Count(),
                DoingTasks = doingId is null ? 0 : g.Count(t => t.StatusId == doingId),
                OverdueTasks = g.Count(t => t.DueAt.HasValue && t.DueAt < now),
                EstimateHours = g.Sum(t => t.EstimateHours ?? 0),
                SpentHours = g.Sum(t => t.SpentHours)
            })
            .OrderByDescending(m => m.OpenTasks)
            .ToList();

        dto.ModuleLoads = modules.Select(m => new DtModuleLoadDto
        {
            ModuleId = m.Id,
            Key = m.Key,
            Name = m.NameFa,
            Color = m.Color,
            OpenTasks = open.Count(t => t.ModuleId == m.Id),
            OpenProblems = openProblems.Count(p => p.ModuleId == m.Id),
            ChangesLast7Days = changes.Count(c => c.ModuleId == m.Id)
        })
        .Where(m => m.OpenTasks > 0 || m.OpenProblems > 0 || m.ChangesLast7Days > 0)
        .OrderByDescending(m => m.OpenTasks + m.OpenProblems)
        .Take(12)
        .ToList();

        var stMap = statuses.ToDictionary(s => s.Id);
        var modMap = modules.ToDictionary(m => m.Id);
        var recent = await TasksBase().OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt).Take(8).ToListAsync();
        dto.RecentTasks = recent.Select(t => MapList(t,
            stMap.GetValueOrDefault(t.StatusId),
            t.ModuleId is int mid ? modMap.GetValueOrDefault(mid) : null,
            null)).ToList();

        dto.RecentProblems = openProblems.OrderByDescending(p => p.CreatedAt).Take(8)
            .Select(p => MapProblem(p, modules.FirstOrDefault(m => m.Id == p.ModuleId), null)).ToList();

        var recentChanges = await _db.DtModuleChanges.AsNoTracking()
            .OrderByDescending(c => c.ChangedAt).Take(8).ToListAsync();
        dto.RecentChanges = recentChanges.Select(c => MapChange(c, modules.FirstOrDefault(m => m.Id == c.ModuleId), null)).ToList();

        try { dto.Burndown = await GetBurndownAsync(sprint?.Id); } catch { /* non-fatal */ }

        return dto;
    }

    // ---------- statuses ----------
    public async Task<List<DtWorkflowStatusDto>> GetStatusesAsync(bool includeInactive = false, Paging.Request? pagination = null)
    {
        var q = _db.DtWorkflowStatuses.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(s => s.IsActive);
        return await q.OrderBy(s => s.SortOrder).ThenBy(x => x.Id).Select(s => new DtWorkflowStatusDto
        {
            Id = s.Id, Key = s.Key, NameFa = s.NameFa, Color = s.Color,
            SortOrder = s.SortOrder, IsInitial = s.IsInitial, IsDone = s.IsDone,
            IsBlocked = s.IsBlocked, IsActive = s.IsActive,
            WipLimit = s.WipLimit
        }).ToPageListAsync(pagination);
    }

    public async Task<DtWorkflowStatusDto> UpsertStatusAsync(DtWorkflowStatusDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Key) || string.IsNullOrWhiteSpace(dto.NameFa))
            throw new InvalidOperationException("کلید و نام وضعیت الزامی است.");

        DtWorkflowStatus entity;
        if (dto.Id > 0)
        {
            entity = await _db.DtWorkflowStatuses.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("وضعیت یافت نشد.");
        }
        else
        {
            if (await _db.DtWorkflowStatuses.AnyAsync(s => s.Key == dto.Key))
                throw new InvalidOperationException("کلید وضعیت تکراری است.");
            entity = new DtWorkflowStatus();
            _db.DtWorkflowStatuses.Add(entity);
        }

        entity.Key = dto.Key.Trim();
        entity.NameFa = dto.NameFa.Trim();
        entity.Color = string.IsNullOrWhiteSpace(dto.Color) ? "#64748b" : dto.Color.Trim();
        entity.SortOrder = dto.SortOrder;
        entity.IsInitial = dto.IsInitial;
        entity.IsDone = dto.IsDone;
        entity.IsBlocked = dto.IsBlocked;
        entity.IsActive = dto.IsActive;
        entity.WipLimit = dto.WipLimit is int w && w > 0 ? w : null;

        if (entity.IsInitial)
        {
            var others = await _db.DtWorkflowStatuses.Where(s => s.Id != entity.Id && s.IsInitial).ToListAsync();
            foreach (var o in others) o.IsInitial = false;
        }

        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        return dto;
    }

    public async Task DeleteStatusAsync(int id)
    {
        var s = await _db.DtWorkflowStatuses.FindAsync(id)
            ?? throw new InvalidOperationException("وضعیت یافت نشد.");
        if (await _db.DtTasks.AnyAsync(t => t.StatusId == id && !t.IsDeleted))
            throw new InvalidOperationException("وضعیت روی تسک‌های موجود استفاده شده؛ غیرفعال کنید.");
        _db.DtWorkflowStatuses.Remove(s);
        await _db.SaveChangesAsync();
    }

    // ---------- modules ----------
    public async Task<List<DtProductModuleDto>> GetModulesAsync(bool includeInactive = false, Paging.Request? pagination = null)
    {
        var q = _db.DtProductModules.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(m => m.IsActive);
        var list = await q.OrderBy(m => m.SortOrder).ThenBy(x => x.Id).ToPageListAsync(pagination);

        var doneIds = await _db.DtWorkflowStatuses.AsNoTracking().Where(s => s.IsDone).Select(s => s.Id).ToListAsync();
        var openTasks = await TasksBase().Where(t => !doneIds.Contains(t.StatusId) && t.ModuleId != null)
            .GroupBy(t => t.ModuleId!.Value).Select(g => new { Id = g.Key, C = g.Count() }).ToListAsync();
        var openProbs = await _db.DtProblems.AsNoTracking()
            .Where(p => !p.IsDeleted && p.ModuleId != null &&
                        (p.Status == DtProblemStatus.Open || p.Status == DtProblemStatus.Investigating))
            .GroupBy(p => p.ModuleId!.Value).Select(g => new { Id = g.Key, C = g.Count() }).ToListAsync();
        var since = DateTime.Now.AddDays(-14);
        var chg = await _db.DtModuleChanges.AsNoTracking().Where(c => c.ChangedAt >= since)
            .GroupBy(c => c.ModuleId).Select(g => new { Id = g.Key, C = g.Count() }).ToListAsync();

        return list.Select(m => new DtProductModuleDto
        {
            Id = m.Id, Key = m.Key, NameFa = m.NameFa, Icon = m.Icon, Color = m.Color,
            Description = m.Description, SortOrder = m.SortOrder, IsActive = m.IsActive,
            OpenTaskCount = openTasks.FirstOrDefault(x => x.Id == m.Id)?.C ?? 0,
            OpenProblemCount = openProbs.FirstOrDefault(x => x.Id == m.Id)?.C ?? 0,
            RecentChangeCount = chg.FirstOrDefault(x => x.Id == m.Id)?.C ?? 0
        }).ToList();
    }

    public async Task<DtProductModuleDto> UpsertModuleAsync(DtProductModuleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Key) || string.IsNullOrWhiteSpace(dto.NameFa))
            throw new InvalidOperationException("کلید و نام ماژول الزامی است.");

        DtProductModule entity;
        if (dto.Id > 0)
        {
            entity = await _db.DtProductModules.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("ماژول یافت نشد.");
        }
        else
        {
            if (await _db.DtProductModules.AnyAsync(m => m.Key == dto.Key))
                throw new InvalidOperationException("کلید ماژول تکراری است.");
            entity = new DtProductModule();
            _db.DtProductModules.Add(entity);
        }

        entity.Key = dto.Key.Trim();
        entity.NameFa = dto.NameFa.Trim();
        entity.Icon = dto.Icon;
        entity.Color = string.IsNullOrWhiteSpace(dto.Color) ? "#4f46e5" : dto.Color.Trim();
        entity.Description = dto.Description;
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        return dto;
    }

    public async Task DeleteModuleAsync(int id)
    {
        var m = await _db.DtProductModules.FindAsync(id)
            ?? throw new InvalidOperationException("ماژول یافت نشد.");
        if (await _db.DtTasks.AnyAsync(t => t.ModuleId == id && !t.IsDeleted) ||
            await _db.DtProblems.AnyAsync(p => p.ModuleId == id && !p.IsDeleted) ||
            await _db.DtModuleChanges.AnyAsync(c => c.ModuleId == id))
        {
            m.IsActive = false;
            await _db.SaveChangesAsync();
            return;
        }
        _db.DtProductModules.Remove(m);
        await _db.SaveChangesAsync();
    }

    // ---------- sprints ----------
    public async Task<List<DtSprintDto>> GetSprintsAsync(Paging.Request? pagination = null)
    {
        var list = await _db.DtSprints.AsNoTracking().OrderByDescending(s => s.StartDate).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var doneIds = await _db.DtWorkflowStatuses.AsNoTracking().Where(s => s.IsDone).Select(s => s.Id).ToListAsync();
        var tasks = await TasksBase().Where(t => t.SprintId != null)
            .Select(t => new { t.SprintId, t.StatusId, t.EstimateHours, t.SpentHours }).ToListAsync();

        return list.Select(s =>
        {
            var st = tasks.Where(t => t.SprintId == s.Id).ToList();
            return new DtSprintDto
            {
                Id = s.Id, Name = s.Name, Goal = s.Goal,
                StartDate = s.StartDate, EndDate = s.EndDate,
                Status = s.Status, StatusFa = DtSprintStatus.ToFa(s.Status),
                CreatedAt = s.CreatedAt, CreatedByName = s.CreatedByName,
                TaskCount = st.Count,
                DoneTaskCount = st.Count(t => doneIds.Contains(t.StatusId)),
                TotalEstimate = st.Sum(t => t.EstimateHours ?? 0),
                TotalSpent = st.Sum(t => t.SpentHours)
            };
        }).ToList();
    }

    public async Task<DtSprintDto> UpsertSprintAsync(DtSprintDto dto, int userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام اسپرینت الزامی است.");
        if (dto.EndDate < dto.StartDate)
            throw new InvalidOperationException("تاریخ پایان باید بعد از شروع باشد.");
        if (!DtSprintStatus.IsValid(dto.Status))
            dto.Status = DtSprintStatus.Planned;

        DtSprint entity;
        if (dto.Id > 0)
        {
            entity = await _db.DtSprints.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("اسپرینت یافت نشد.");
        }
        else
        {
            entity = new DtSprint
            {
                CreatedAt = DateTime.Now,
                CreatedByUserId = userId,
                CreatedByName = userName
            };
            _db.DtSprints.Add(entity);
        }

        entity.Name = dto.Name.Trim();
        entity.Goal = dto.Goal;
        entity.StartDate = dto.StartDate.Date;
        entity.EndDate = dto.EndDate.Date;
        entity.Status = dto.Status;

        if (entity.Status == DtSprintStatus.Active)
        {
            var others = await _db.DtSprints.Where(s => s.Id != entity.Id && s.Status == DtSprintStatus.Active).ToListAsync();
            foreach (var o in others) o.Status = DtSprintStatus.Closed;
        }

        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        dto.StatusFa = DtSprintStatus.ToFa(entity.Status);
        dto.CreatedByName = entity.CreatedByName;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task DeleteSprintAsync(int id)
    {
        var s = await _db.DtSprints.FindAsync(id)
            ?? throw new InvalidOperationException("اسپرینت یافت نشد.");
        var linked = await _db.DtTasks.Where(t => t.SprintId == id && !t.IsDeleted).ToListAsync();
        foreach (var t in linked) t.SprintId = null;
        _db.DtSprints.Remove(s);
        await _db.SaveChangesAsync();
    }

    // ---------- tasks ----------
    public async Task<DtPagedResult<DtTaskListItemDto>> QueryTasksAsync(DtTaskQuery q, int currentUserId, Paging.Request? pagination = null)
    {
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var statuses = await _db.DtWorkflowStatuses.AsNoTracking().ToListAsync();
        var doneIds = statuses.Where(s => s.IsDone).Select(s => s.Id).ToHashSet();
        var modules = await _db.DtProductModules.AsNoTracking().ToListAsync();
        var sprints = await _db.DtSprints.AsNoTracking().ToListAsync();

        var query = TasksBase();
        if (q.StatusId is int sid) query = query.Where(t => t.StatusId == sid);
        if (q.ModuleId is int mid) query = query.Where(t => t.ModuleId == mid);
        if (q.SprintId is int spid) query = query.Where(t => t.SprintId == spid);
        if (q.AssigneeUserId is int aid) query = query.Where(t => t.AssigneeUserId == aid);
        if (q.Priority is int pr) query = query.Where(t => t.Priority == pr);
        if (!string.IsNullOrWhiteSpace(q.Type)) query = query.Where(t => t.Type == q.Type);
        if (q.OnlyMine == true) query = query.Where(t => t.AssigneeUserId == currentUserId);
        if (q.IncludeDone != true) query = query.Where(t => !doneIds.Contains(t.StatusId));
        if (q.OnlyOverdue == true)
        {
            var now = DateTime.Now;
            query = query.Where(t => t.DueAt != null && t.DueAt < now && !doneIds.Contains(t.StatusId));
        }
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(t => t.Title.Contains(term) || t.Number.Contains(term) ||
                                     (t.Tags != null && t.Tags.Contains(term)) ||
                                     (t.AssigneeName != null && t.AssigneeName.Contains(term)));
        }

        // در لیست/کانبان فقط ریشه‌ها (بدون ساب‌تسک) مگر فیلتر صریح parent
        // — ساب‌تسک‌ها داخل جزئیات والد دیده می‌شوند
        query = query.Where(t => t.ParentTaskId == null);

        var total = await query.CountAsync();
        var ordered = query
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DueAt ?? DateTime.MaxValue)
            .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt).ThenBy(t => t.Id);
        var items = pagination?.IsPaged == true
            ? await ordered.ToPageListAsync(pagination)
            : await ordered.Skip(Paging.Offset(page, size)).Take(size).ToListAsync();

        var ids = items.Select(t => t.Id).ToList();
        var commentCounts = await _db.DtTaskComments.AsNoTracking().Where(c => ids.Contains(c.TaskId))
            .GroupBy(c => c.TaskId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        var gitCounts = await _db.DtTaskGitLinks.AsNoTracking().Where(c => ids.Contains(c.TaskId))
            .GroupBy(c => c.TaskId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        var probCounts = await _db.DtProblems.AsNoTracking()
            .Where(p => !p.IsDeleted && p.TaskId != null && ids.Contains(p.TaskId.Value) &&
                        (p.Status == DtProblemStatus.Open || p.Status == DtProblemStatus.Investigating))
            .GroupBy(p => p.TaskId!.Value).Select(g => new { g.Key, C = g.Count() }).ToListAsync();

        var stMap = statuses.ToDictionary(s => s.Id);
        var modMap = modules.ToDictionary(m => m.Id);
        var spMap = sprints.ToDictionary(s => s.Id);
        var (subs, openBlockers, parents) = await LoadRelationMapsAsync(ids, doneIds);

        return new DtPagedResult<DtTaskListItemDto>
        {
            Page = page, PageSize = size, Total = total,
            Items = items.Select(t =>
            {
                var sub = subs.GetValueOrDefault(t.Id);
                parents.TryGetValue(t.ParentTaskId ?? -1, out var pinfo);
                return MapList(t,
                    stMap.GetValueOrDefault(t.StatusId),
                    t.ModuleId is int m ? modMap.GetValueOrDefault(m) : null,
                    t.SprintId is int s ? spMap.GetValueOrDefault(s) : null,
                    commentCounts.FirstOrDefault(x => x.Key == t.Id)?.C ?? 0,
                    gitCounts.FirstOrDefault(x => x.Key == t.Id)?.C ?? 0,
                    probCounts.FirstOrDefault(x => x.Key == t.Id)?.C ?? 0,
                    t.ParentTaskId,
                    pinfo.Number,
                    pinfo.Title,
                    sub.total,
                    sub.done,
                    openBlockers.GetValueOrDefault(t.Id));
            }).ToList()
        };
    }

    public async Task<List<DtTaskListItemDto>> BoardTasksAsync(int? sprintId, int? moduleId, int? assigneeUserId, Paging.Request? pagination = null)
    {
        var q = new DtTaskQuery
        {
            SprintId = sprintId,
            ModuleId = moduleId,
            AssigneeUserId = assigneeUserId,
            IncludeDone = true,
            Page = 1,
            PageSize = 500
        };
        var result = await QueryTasksAsync(q, 0, pagination);
        return result.Items;
    }

    public async Task<DtTaskDetailDto?> GetTaskAsync(int id)
    {
        var t = await _db.DtTasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (t is null) return null;

        var statuses = await _db.DtWorkflowStatuses.AsNoTracking().ToListAsync();
        var doneIds = statuses.Where(s => s.IsDone).Select(s => s.Id).ToHashSet();
        var stMap = statuses.ToDictionary(s => s.Id);

        var st = stMap.GetValueOrDefault(t.StatusId);
        var mod = t.ModuleId is int mid
            ? await _db.DtProductModules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mid) : null;
        var sp = t.SprintId is int sid
            ? await _db.DtSprints.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid) : null;

        var comments = await _db.DtTaskComments.AsNoTracking().Where(c => c.TaskId == id)
            .OrderByDescending(c => c.CreatedAt).ToListAsync();
        var acts = await _db.DtTaskActivities.AsNoTracking().Where(a => a.TaskId == id)
            .OrderByDescending(a => a.CreatedAt).Take(50).ToListAsync();
        var gits = await _db.DtTaskGitLinks.AsNoTracking().Where(g => g.TaskId == id)
            .OrderByDescending(g => g.CreatedAt).ToListAsync();
        var times = await _db.DtTimeEntries.AsNoTracking().Where(e => e.TaskId == id)
            .OrderByDescending(e => e.WorkDate).ToListAsync();
        var openProbs = await _db.DtProblems.AsNoTracking()
            .CountAsync(p => !p.IsDeleted && p.TaskId == id &&
                             (p.Status == DtProblemStatus.Open || p.Status == DtProblemStatus.Investigating));

        var (subs, openBlockers, parents) = await LoadRelationMapsAsync(new List<int> { id }, doneIds);
        parents.TryGetValue(t.ParentTaskId ?? -1, out var pinfo);
        var subInfo = subs.GetValueOrDefault(id);

        var list = MapList(t, st, mod, sp, comments.Count, gits.Count, openProbs,
            t.ParentTaskId, pinfo.Number, pinfo.Title, subInfo.total, subInfo.done,
            openBlockers.GetValueOrDefault(id));

        // children
        var childEntities = await _db.DtTasks.AsNoTracking()
            .Where(c => !c.IsDeleted && c.ParentTaskId == id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync();
        var childIds = childEntities.Select(c => c.Id).ToList();
        var (childSubs, childBlockers, _) = await LoadRelationMapsAsync(childIds, doneIds);
        var modules = await _db.DtProductModules.AsNoTracking().ToListAsync();
        var modMap = modules.ToDictionary(m => m.Id);
        var childDtos = childEntities.Select(c =>
        {
            var cs = childSubs.GetValueOrDefault(c.Id);
            return MapList(c, stMap.GetValueOrDefault(c.StatusId),
                c.ModuleId is int m ? modMap.GetValueOrDefault(m) : null, null,
                0, 0, 0, c.ParentTaskId, t.Number, t.Title, cs.total, cs.done,
                childBlockers.GetValueOrDefault(c.Id));
        }).ToList();

        // dependencies
        var blockedByRows = await _db.DtTaskDependencies.AsNoTracking()
            .Where(d => d.TaskId == id).OrderByDescending(d => d.Id).ToListAsync();
        var blockingRows = await _db.DtTaskDependencies.AsNoTracking()
            .Where(d => d.DependsOnTaskId == id).OrderByDescending(d => d.Id).ToListAsync();
        var depTaskIds = blockedByRows.Select(d => d.DependsOnTaskId)
            .Concat(blockingRows.Select(d => d.TaskId))
            .Concat(blockedByRows.Select(d => d.TaskId))
            .Distinct().ToList();
        var depTasks = await _db.DtTasks.AsNoTracking().Where(x => depTaskIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        DtDependencyDto MapDep(DtTaskDependency d)
        {
            depTasks.TryGetValue(d.TaskId, out var left);
            depTasks.TryGetValue(d.DependsOnTaskId, out var right);
            var rightSt = right is null ? null : stMap.GetValueOrDefault(right.StatusId);
            return new DtDependencyDto
            {
                Id = d.Id,
                TaskId = d.TaskId,
                TaskNumber = left?.Number ?? "",
                TaskTitle = left?.Title ?? "",
                DependsOnTaskId = d.DependsOnTaskId,
                DependsOnNumber = right?.Number ?? "",
                DependsOnTitle = right?.Title ?? "",
                Kind = d.Kind,
                KindFa = DtDependencyKind.ToFa(d.Kind),
                DependsOnIsDone = rightSt?.IsDone ?? false,
                DependsOnStatusName = rightSt?.NameFa,
                DependsOnStatusColor = rightSt?.Color,
                CreatedByName = d.CreatedByName,
                CreatedAt = d.CreatedAt
            };
        }

        var checklist = await _db.DtTaskChecklistItems.AsNoTracking()
            .Where(c => c.TaskId == id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync();

        return new DtTaskDetailDto
        {
            Id = list.Id, Number = list.Number, Title = list.Title, Type = list.Type, TypeFa = list.TypeFa,
            Priority = list.Priority, PriorityFa = list.PriorityFa,
            StatusId = list.StatusId, StatusKey = list.StatusKey, StatusName = list.StatusName,
            StatusColor = list.StatusColor, StatusIsDone = list.StatusIsDone, StatusIsBlocked = list.StatusIsBlocked,
            ModuleId = list.ModuleId, ModuleKey = list.ModuleKey, ModuleName = list.ModuleName, ModuleColor = list.ModuleColor,
            SprintId = list.SprintId, SprintName = list.SprintName,
            AssigneeUserId = list.AssigneeUserId, AssigneeName = list.AssigneeName,
            ReporterName = list.ReporterName, ReporterUserId = t.ReporterUserId,
            DueAt = list.DueAt, IsOverdue = list.IsOverdue,
            EstimateHours = list.EstimateHours, SpentHours = list.SpentHours,
            Progress = list.Progress, Tags = list.Tags,
            CreatedAt = list.CreatedAt, UpdatedAt = list.UpdatedAt, CompletedAt = t.CompletedAt,
            Description = t.Description,
            CommentCount = list.CommentCount, GitLinkCount = list.GitLinkCount, OpenProblemCount = list.OpenProblemCount,
            WorkOrderId = t.WorkOrderId, WorkOrderNumber = t.WorkOrderNumber,
            ParentTaskId = list.ParentTaskId, ParentTaskNumber = list.ParentTaskNumber, ParentTaskTitle = list.ParentTaskTitle,
            SubTaskCount = list.SubTaskCount, SubTaskDoneCount = list.SubTaskDoneCount,
            OpenBlockerCount = list.OpenBlockerCount, IsDependencyBlocked = list.IsDependencyBlocked,
            SortOrder = list.SortOrder,
            TimerRunning = list.TimerRunning, TimerStartedAt = list.TimerStartedAt, TimerStartedByUserId = list.TimerStartedByUserId,
            Comments = comments.Select(c => new DtTaskCommentDto
            {
                Id = c.Id, AuthorUserId = c.AuthorUserId, AuthorName = c.AuthorName,
                Text = c.Text, CreatedAt = c.CreatedAt
            }).ToList(),
            Activities = acts.Select(a => new DtTaskActivityDto
            {
                Id = a.Id, ActorName = a.ActorName, Action = a.Action,
                ActionFa = ActivityFa(a.Action), Detail = a.Detail, CreatedAt = a.CreatedAt
            }).ToList(),
            GitLinks = gits.Select(g => new DtTaskGitLinkDto
            {
                Id = g.Id, Kind = g.Kind, KindFa = DtGitLinkKind.ToFa(g.Kind),
                Ref = g.Ref, Url = g.Url, Title = g.Title,
                AddedByName = g.AddedByName, CreatedAt = g.CreatedAt
            }).ToList(),
            TimeEntries = times.Select(e => new DtTimeEntryDto
            {
                Id = e.Id, UserId = e.UserId, UserName = e.UserName,
                Hours = e.Hours, WorkDate = e.WorkDate, Note = e.Note, CreatedAt = e.CreatedAt
            }).ToList(),
            SubTasks = childDtos,
            BlockedBy = blockedByRows.Select(MapDep).ToList(),
            Blocking = blockingRows.Select(MapDep).ToList(),
            Checklist = checklist.Select(MapChecklist).ToList(),
            ChecklistTotal = checklist.Count,
            ChecklistDone = checklist.Count(c => c.IsDone)
        };
    }

    public async Task<DtTaskDetailDto> CreateTaskAsync(DtTaskUpsertDto dto, int userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new InvalidOperationException("عنوان تسک الزامی است.");
        if (!DtTaskType.IsValid(dto.Type)) dto.Type = DtTaskType.Feature;
        if (!DtPriority.IsValid(dto.Priority)) dto.Priority = DtPriority.Normal;

        var initial = dto.StatusId is int sid
            ? await _db.DtWorkflowStatuses.FirstOrDefaultAsync(s => s.Id == sid && s.IsActive)
            : await _db.DtWorkflowStatuses.FirstOrDefaultAsync(s => s.IsInitial && s.IsActive);
        if (initial is null)
            initial = await _db.DtWorkflowStatuses.Where(s => s.IsActive).OrderBy(s => s.SortOrder).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("هیچ وضعیتی تعریف نشده است.");

        // WIP فقط برای تسک ریشه
        if (dto.ParentTaskId is null or <= 0)
            await EnsureWipAllowsAsync(initial.Id);

        var (assigneeName, _) = await UserNameAsync(dto.AssigneeUserId);

        int? parentId = null;
        if (dto.ParentTaskId is int pid && pid > 0)
        {
            var parent = await _db.DtTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == pid && !t.IsDeleted)
                ?? throw new InvalidOperationException("تسک والد یافت نشد.");
            var depth = await GetDepthAsync(pid);
            if (depth >= MaxSubTaskDepth)
                throw new InvalidOperationException($"حداکثر عمق ساب‌تسک {MaxSubTaskDepth} سطح است.");
            parentId = pid;
            // ارث‌بری ماژول/اسپرینت اگر خالی باشد
            dto.ModuleId ??= parent.ModuleId;
            dto.SprintId ??= parent.SprintId;
        }

        var sort = 0;
        if (parentId is int pp)
            sort = await _db.DtTasks.Where(t => t.ParentTaskId == pp).Select(t => (int?)t.SortOrder).MaxAsync() ?? 0;

        var entity = new DtTask
        {
            Number = await NextNumberAsync(),
            Title = dto.Title.Trim(),
            Description = dto.Description,
            Type = dto.Type,
            Priority = dto.Priority,
            StatusId = initial.Id,
            ModuleId = dto.ModuleId,
            SprintId = dto.SprintId,
            AssigneeUserId = dto.AssigneeUserId,
            AssigneeName = assigneeName,
            ReporterUserId = userId,
            ReporterName = userName,
            DueAt = dto.DueAt,
            EstimateHours = dto.EstimateHours,
            Progress = Math.Clamp(dto.Progress, 0, 100),
            Tags = dto.Tags,
            ParentTaskId = parentId,
            SortOrder = sort + 10,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        if (initial.IsDone) entity.CompletedAt = DateTime.Now;

        _db.DtTasks.Add(entity);
        await _db.SaveChangesAsync();
        await LogAsync(entity.Id, userId, userName, parentId is null ? "Created" : "SubTaskCreated",
            parentId is null ? entity.Title : $"زیر {dto.ParentTaskId}: {entity.Title}");
        if (parentId is int parentLogId)
        {
            await LogAsync(parentLogId, userId, userName, "SubTaskCreated", entity.Number);
            var parentEntity = await _db.DtTasks.FindAsync(parentLogId);
            if (parentEntity is not null) parentEntity.UpdatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();

        if (entity.AssigneeUserId is int aid && aid != userId)
        {
            await NotifyAssigneeAsync(aid, "تسک جدید محول شد 🧩",
                $"{entity.Number} — «{entity.Title}»", userName, entity.Id);
        }
        else
        {
            try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
        }

        return (await GetTaskAsync(entity.Id))!;
    }

    public async Task<DtTaskDetailDto> UpdateTaskAsync(int id, DtTaskUpsertDto dto, int userId, string userName)
    {
        var entity = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");

        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new InvalidOperationException("عنوان تسک الزامی است.");
        if (!DtTaskType.IsValid(dto.Type)) dto.Type = entity.Type;
        if (!DtPriority.IsValid(dto.Priority)) dto.Priority = entity.Priority;

        var changes = new List<string>();
        if (entity.Title != dto.Title.Trim()) { changes.Add("عنوان"); entity.Title = dto.Title.Trim(); }
        entity.Description = dto.Description;
        if (entity.Type != dto.Type) { changes.Add("نوع"); entity.Type = dto.Type; }
        if (entity.Priority != dto.Priority) { changes.Add("اولویت"); entity.Priority = dto.Priority; }
        if (entity.ModuleId != dto.ModuleId) { changes.Add("ماژول"); entity.ModuleId = dto.ModuleId; }
        if (entity.SprintId != dto.SprintId) { changes.Add("اسپرینت"); entity.SprintId = dto.SprintId; }
        if (entity.DueAt != dto.DueAt) { changes.Add("مهلت"); entity.DueAt = dto.DueAt; }
        if (entity.EstimateHours != dto.EstimateHours) { changes.Add("تخمین"); entity.EstimateHours = dto.EstimateHours; }
        entity.Progress = Math.Clamp(dto.Progress, 0, 100);
        entity.Tags = dto.Tags;

        if (entity.AssigneeUserId != dto.AssigneeUserId)
        {
            var oldAssignee = entity.AssigneeUserId;
            var (name, _) = await UserNameAsync(dto.AssigneeUserId);
            entity.AssigneeUserId = dto.AssigneeUserId;
            entity.AssigneeName = name;
            await LogAsync(id, userId, userName, "Assigned", name ?? "برداشته شد");
            if (dto.AssigneeUserId is int newAid && newAid != userId && newAid != oldAssignee)
            {
                await NotifyAssigneeAsync(newAid, "تسک به شما محول شد 🧩",
                    $"{entity.Number} — «{entity.Title}»", userName, id);
            }
        }

        if (dto.StatusId is int newSt && newSt != entity.StatusId)
        {
            var st = await _db.DtWorkflowStatuses.FirstOrDefaultAsync(s => s.Id == newSt && s.IsActive)
                ?? throw new InvalidOperationException("وضعیت نامعتبر است.");
            if (st.IsDone)
                await EnsureNoOpenBlockersAsync(id);
            await EnsureWipAllowsAsync(newSt, excludeTaskId: id);
            var old = await _db.DtWorkflowStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == entity.StatusId);
            entity.StatusId = st.Id;
            entity.CompletedAt = st.IsDone ? DateTime.Now : null;
            if (st.IsDone && entity.Progress < 100) entity.Progress = 100;
            await LogAsync(id, userId, userName, "StatusChanged", $"{old?.NameFa} ← {st.NameFa}");
        }

        entity.UpdatedAt = DateTime.Now;
        if (changes.Count > 0)
            await LogAsync(id, userId, userName, "Updated", string.Join("، ", changes));

        await _db.SaveChangesAsync();
        return (await GetTaskAsync(id))!;
    }

    public async Task MoveTaskAsync(int id, int statusId, int userId, string userName, int? beforeTaskId = null)
    {
        var entity = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");

        var st = await _db.DtWorkflowStatuses.FirstOrDefaultAsync(s => s.Id == statusId && s.IsActive)
            ?? throw new InvalidOperationException("وضعیت نامعتبر است.");
        var old = await _db.DtWorkflowStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == entity.StatusId);

        var statusChanged = entity.StatusId != statusId;
        if (statusChanged && st.IsDone)
            await EnsureNoOpenBlockersAsync(id);
        if (statusChanged)
            await EnsureWipAllowsAsync(statusId, excludeTaskId: id);

        if (statusChanged)
        {
            entity.StatusId = st.Id;
            entity.CompletedAt = st.IsDone ? DateTime.Now : null;
            if (st.IsDone && entity.Progress < 100) entity.Progress = 100;
        }

        // ترتیب در ستون مقصد (ریشه‌ها؛ ساب‌تسک‌ها با ReorderSubTasks)
        await ApplyColumnSortAsync(entity, statusId, beforeTaskId);

        entity.UpdatedAt = DateTime.Now;
        if (statusChanged)
            await LogAsync(id, userId, userName, "StatusChanged", $"{old?.NameFa} ← {st.NameFa}");
        else
            await LogAsync(id, userId, userName, "Reordered", "جابجایی در ستون");

        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
    }

    /// <summary>بازنویسی SortOrder در ستون وضعیت برای ریشه‌ها.</summary>
    private async Task ApplyColumnSortAsync(DtTask entity, int statusId, int? beforeTaskId)
    {
        // فقط ریشه‌ها در کانبان مرتب می‌شوند
        if (entity.ParentTaskId is not null)
        {
            if (beforeTaskId is null) return;
        }

        var siblings = await _db.DtTasks
            .Where(t => !t.IsDeleted && t.StatusId == statusId && t.ParentTaskId == null && t.Id != entity.Id)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync();

        var ordered = new List<DtTask>();
        var inserted = false;
        foreach (var s in siblings)
        {
            if (beforeTaskId is int bid && s.Id == bid && !inserted)
            {
                ordered.Add(entity);
                inserted = true;
            }
            ordered.Add(s);
        }
        if (!inserted) ordered.Add(entity);

        for (var i = 0; i < ordered.Count; i++)
            ordered[i].SortOrder = (i + 1) * 10;
    }

    public async Task ReorderSubTasksAsync(int parentId, IReadOnlyList<int> orderedIds, int userId, string userName)
    {
        if (orderedIds is null || orderedIds.Count == 0)
            throw new InvalidOperationException("لیست ترتیب خالی است.");

        var parent = await _db.DtTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == parentId && !t.IsDeleted)
            ?? throw new InvalidOperationException("والد یافت نشد.");

        var children = await _db.DtTasks
            .Where(t => !t.IsDeleted && t.ParentTaskId == parentId)
            .ToListAsync();
        if (children.Count == 0) return;

        var set = children.Select(c => c.Id).ToHashSet();
        if (orderedIds.Any(id => !set.Contains(id)))
            throw new InvalidOperationException("شناسهٔ نامعتبر در ترتیب ساب‌تسک‌ها.");
        if (orderedIds.Distinct().Count() != orderedIds.Count)
            throw new InvalidOperationException("شناسه تکراری در ترتیب.");

        // موارد جاافتاده را انتهای لیست بگذار
        var final = orderedIds.ToList();
        foreach (var c in children.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
            if (!final.Contains(c.Id)) final.Add(c.Id);

        var map = children.ToDictionary(c => c.Id);
        for (var i = 0; i < final.Count; i++)
            map[final[i]].SortOrder = (i + 1) * 10;

        await LogAsync(parentId, userId, userName, "SubTasksReordered", $"{final.Count} مورد");
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
    }

    public async Task<DtTimerStateDto> StartTimerAsync(int taskId, int userId, string userName)
    {
        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");

        // توقف تایمرهای دیگر همین کاربر
        var others = await _db.DtTasks
            .Where(t => !t.IsDeleted && t.TimerStartedAt != null && t.TimerStartedByUserId == userId && t.Id != taskId)
            .ToListAsync();
        foreach (var o in others)
        {
            await StopTimerCoreAsync(o, userId, userName, "توقف خودکار — شروع تایمر دیگر");
        }

        if (task.TimerStartedAt != null && task.TimerStartedByUserId == userId)
            return await BuildTimerStateAsync(task);

        if (task.TimerStartedAt != null && task.TimerStartedByUserId != userId)
            throw new InvalidOperationException("تایمر این تسک توسط کاربر دیگری در حال اجراست.");

        task.TimerStartedAt = DateTime.Now;
        task.TimerStartedByUserId = userId;
        task.UpdatedAt = DateTime.Now;
        await LogAsync(taskId, userId, userName, "TimerStarted", null);
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
        return await BuildTimerStateAsync(task);
    }

    public async Task<DtTimerStateDto> StopTimerAsync(int taskId, int userId, string userName, string? note = null)
    {
        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");
        if (task.TimerStartedAt is null)
            return await BuildTimerStateAsync(task);

        // فقط شروع‌کننده یا Manage می‌تواند متوقف کند — در عمل userId چک نرم
        var entry = await StopTimerCoreAsync(task, userId, userName, note);
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
        var state = await BuildTimerStateAsync(task);
        state.LastEntry = entry;
        return state;
    }

    public async Task<DtTimerStateDto?> GetTimerAsync(int taskId)
    {
        var task = await _db.DtTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted);
        if (task is null) return null;
        return await BuildTimerStateAsync(task);
    }

    private async Task<DtTimeEntryDto?> StopTimerCoreAsync(DtTask task, int userId, string userName, string? note)
    {
        if (task.TimerStartedAt is null) return null;
        var started = task.TimerStartedAt.Value;
        var elapsed = DateTime.Now - started;
        // حداقل ۱ دقیقه → ۰.۰۲ ساعت؛ رند به ۰.۲۵
        var hours = (decimal)Math.Max(elapsed.TotalHours, 1.0 / 60.0);
        hours = Math.Round(hours * 4m, MidpointRounding.AwayFromZero) / 4m;
        if (hours < 0.25m) hours = 0.25m;
        if (hours > 24m) hours = 24m;

        var e = new DtTimeEntry
        {
            TaskId = task.Id,
            UserId = task.TimerStartedByUserId ?? userId,
            UserName = userName,
            Hours = hours,
            WorkDate = started.Date,
            Note = string.IsNullOrWhiteSpace(note)
                ? $"تایمر {started:HH:mm}–{DateTime.Now:HH:mm}"
                : note,
            CreatedAt = DateTime.Now
        };
        _db.DtTimeEntries.Add(e);
        task.SpentHours += hours;
        task.TimerStartedAt = null;
        task.TimerStartedByUserId = null;
        task.UpdatedAt = DateTime.Now;
        await LogAsync(task.Id, userId, userName, "TimerStopped", $"{hours} ساعت");
        return new DtTimeEntryDto
        {
            Id = e.Id, UserId = e.UserId, UserName = e.UserName,
            Hours = e.Hours, WorkDate = e.WorkDate, Note = e.Note, CreatedAt = e.CreatedAt
        };
    }

    private Task<DtTimerStateDto> BuildTimerStateAsync(DtTask task)
    {
        var running = task.TimerStartedAt != null;
        var elapsed = running
            ? (int)Math.Max(0, (DateTime.Now - task.TimerStartedAt!.Value).TotalSeconds)
            : 0;
        return Task.FromResult(new DtTimerStateDto
        {
            TaskId = task.Id,
            TaskNumber = task.Number,
            TaskTitle = task.Title,
            Running = running,
            StartedAt = task.TimerStartedAt,
            StartedByUserId = task.TimerStartedByUserId,
            ElapsedSeconds = elapsed,
            SpentHours = task.SpentHours
        });
    }

    public async Task<DtTimerStateDto?> GetMyTimerAsync(int userId)
    {
        if (userId <= 0) return null;
        var task = await _db.DtTasks.AsNoTracking()
            .Where(t => !t.IsDeleted && t.TimerStartedAt != null && t.TimerStartedByUserId == userId)
            .OrderByDescending(t => t.TimerStartedAt)
            .FirstOrDefaultAsync();
        if (task is null) return null;
        return await BuildTimerStateAsync(task);
    }

    private static DtChecklistItemDto MapChecklist(DtTaskChecklistItem c) => new()
    {
        Id = c.Id,
        TaskId = c.TaskId,
        Title = c.Title,
        IsDone = c.IsDone,
        SortOrder = c.SortOrder,
        CreatedByName = c.CreatedByName,
        CreatedAt = c.CreatedAt,
        DoneAt = c.DoneAt,
        DoneByName = c.DoneByName
    };

    public async Task<DtChecklistItemDto> AddChecklistItemAsync(int taskId, string title, int userId, string userName)
    {
        title = (title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("عنوان آیتم چک‌لیست الزامی است.");
        if (title.Length > 300) title = title[..300];

        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");

        var max = await _db.DtTaskChecklistItems.Where(c => c.TaskId == taskId)
            .Select(c => (int?)c.SortOrder).MaxAsync() ?? 0;

        var item = new DtTaskChecklistItem
        {
            TaskId = taskId,
            Title = title,
            IsDone = false,
            SortOrder = max + 10,
            CreatedByUserId = userId,
            CreatedByName = userName,
            CreatedAt = DateTime.Now
        };
        _db.DtTaskChecklistItems.Add(item);
        task.UpdatedAt = DateTime.Now;
        await LogAsync(taskId, userId, userName, "ChecklistAdded", title);
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
        return MapChecklist(item);
    }

    public async Task<DtChecklistItemDto> ToggleChecklistItemAsync(int itemId, int userId, string userName)
    {
        var item = await _db.DtTaskChecklistItems.FirstOrDefaultAsync(c => c.Id == itemId)
            ?? throw new InvalidOperationException("آیتم یافت نشد.");
        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == item.TaskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");

        item.IsDone = !item.IsDone;
        if (item.IsDone)
        {
            item.DoneAt = DateTime.Now;
            item.DoneByUserId = userId;
            item.DoneByName = userName;
        }
        else
        {
            item.DoneAt = null;
            item.DoneByUserId = null;
            item.DoneByName = null;
        }
        task.UpdatedAt = DateTime.Now;
        await LogAsync(task.Id, userId, userName, item.IsDone ? "ChecklistDone" : "ChecklistUndone", item.Title);
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
        return MapChecklist(item);
    }

    public async Task DeleteChecklistItemAsync(int itemId, int userId, string userName)
    {
        var item = await _db.DtTaskChecklistItems.FirstOrDefaultAsync(c => c.Id == itemId)
            ?? throw new InvalidOperationException("آیتم یافت نشد.");
        var taskId = item.TaskId;
        var title = item.Title;
        _db.DtTaskChecklistItems.Remove(item);
        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task != null) task.UpdatedAt = DateTime.Now;
        await LogAsync(taskId, userId, userName, "ChecklistRemoved", title);
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
    }

    public async Task ReorderChecklistAsync(int taskId, IReadOnlyList<int> orderedIds, int userId, string userName)
    {
        if (orderedIds is null || orderedIds.Count == 0)
            throw new InvalidOperationException("لیست ترتیب خالی است.");
        _ = await _db.DtTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");

        var items = await _db.DtTaskChecklistItems.Where(c => c.TaskId == taskId).ToListAsync();
        var set = items.Select(i => i.Id).ToHashSet();
        if (orderedIds.Any(id => !set.Contains(id)))
            throw new InvalidOperationException("شناسه نامعتبر در ترتیب چک‌لیست.");

        var final = orderedIds.ToList();
        foreach (var it in items.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
            if (!final.Contains(it.Id)) final.Add(it.Id);

        var map = items.ToDictionary(i => i.Id);
        for (var i = 0; i < final.Count; i++)
            map[final[i]].SortOrder = (i + 1) * 10;

        await LogAsync(taskId, userId, userName, "ChecklistReordered", $"{final.Count} مورد");
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
    }

    public async Task<DtTaskDetailDto> CreateTaskFromProblemAsync(int problemId, DtCreateTaskFromProblemDto? dto, int userId, string userName)
    {
        var p = await _db.DtProblems.FirstOrDefaultAsync(x => x.Id == problemId && !x.IsDeleted)
            ?? throw new InvalidOperationException("مشکل یافت نشد.");

        if (p.TaskId is int existing)
        {
            var linked = await GetTaskAsync(existing);
            if (linked != null) return linked;
        }

        var type = string.Equals(p.Severity, "Error", StringComparison.OrdinalIgnoreCase) ? "Bug" : "Bug";
        var priority = dto?.Priority ?? (string.Equals(p.Severity, "Error", StringComparison.OrdinalIgnoreCase) ? 3 : 2);
        if (!DtPriority.IsValid(priority)) priority = 2;

        var upsert = new DtTaskUpsertDto
        {
            Title = p.Title.Length > 200 ? p.Title[..200] : p.Title,
            Description = string.IsNullOrWhiteSpace(p.Description)
                ? $"ساخته‌شده از مشکل #{p.Id} ({p.Severity})"
                : p.Description + $"\n\n— از مشکل #{p.Id}",
            Type = type,
            Priority = priority,
            ModuleId = p.ModuleId,
            StatusId = dto?.StatusId,
            AssigneeUserId = dto?.AssigneeUserId ?? p.AssigneeUserId,
            SprintId = dto?.SprintId,
            Tags = "from-problem"
        };

        var task = await CreateTaskAsync(upsert, userId, userName);
        p.TaskId = task.Id;
        if (string.Equals(p.Status, "Open", StringComparison.OrdinalIgnoreCase))
            p.Status = "Investigating";
        await _db.SaveChangesAsync();

        await AddChecklistItemAsync(task.Id, "بازتولید مشکل", userId, userName);
        await AddChecklistItemAsync(task.Id, "رفع و تست", userId, userName);
        await AddChecklistItemAsync(task.Id, "بررسی regression", userId, userName);

        return (await GetTaskAsync(task.Id))!;
    }

    public async Task<DtBurndownDto> GetBurndownAsync(int? sprintId = null)
    {
        var sprint = sprintId.HasValue
            ? await _db.DtSprints.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sprintId)
            : await _db.DtSprints.AsNoTracking().FirstOrDefaultAsync(s => s.Status == DtSprintStatus.Active);

        var statuses = await _db.DtWorkflowStatuses.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        var doneIds = statuses.Where(s => s.IsDone).Select(s => s.Id).ToHashSet();

        IQueryable<DtTask> tq = _db.DtTasks.AsNoTracking().Where(t => !t.IsDeleted && t.ParentTaskId == null);
        if (sprint is not null)
            tq = tq.Where(t => t.SprintId == sprint.Id);
        var tasks = await tq.ToListAsync();

        var totalEst = tasks.Sum(t => t.EstimateHours ?? 0);
        if (totalEst <= 0)
            totalEst = tasks.Count; // واحد «تسک» اگر تخمین نباشد
        var useHours = tasks.Any(t => t.EstimateHours is > 0);

        decimal TaskWeight(DtTask t) => useHours ? (t.EstimateHours ?? 0) : 1m;

        var start = (sprint?.StartDate ?? tasks.MinBy(t => t.CreatedAt)?.CreatedAt ?? DateTime.Today).Date;
        var end = (sprint?.EndDate ?? DateTime.Today.AddDays(7)).Date;
        if (end < start) end = start;
        // سقف ۳۰ روز برای نمودار
        if ((end - start).TotalDays > 45) end = start.AddDays(45);

        var points = new List<DtBurndownPointDto>();
        var dayCount = Math.Max(1, (int)(end - start).TotalDays);
        var today = DateTime.Today;

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var dayIdx = (int)(d - start).TotalDays;
            var ideal = totalEst * (1m - (decimal)dayIdx / dayCount);
            if (ideal < 0) ideal = 0;

            // تسک‌هایی که تا پایان این روز «done» شده‌اند
            decimal remaining;
            int doneCum;
            if (d > today)
            {
                // آینده: فقط ideal
                remaining = -1; // UI نادیده می‌گیرد
                doneCum = tasks.Count(t => t.CompletedAt is DateTime c && c.Date <= today && doneIds.Contains(t.StatusId));
                // remaining actual = null-ish → keep last known
                remaining = tasks.Where(t => !(t.CompletedAt is DateTime c && c.Date <= today && doneIds.Contains(t.StatusId))
                                             && !(doneIds.Contains(t.StatusId) && t.CompletedAt == null && d >= today))
                    .Sum(TaskWeight);
                // simpler: for future days use today's remaining
                remaining = tasks.Where(t => !doneIds.Contains(t.StatusId) || (t.CompletedAt is DateTime c && c.Date > today))
                    .Where(t => !(doneIds.Contains(t.StatusId) && (t.CompletedAt == null || t.CompletedAt.Value.Date <= today)))
                    .Sum(TaskWeight);
            }
            else
            {
                doneCum = tasks.Count(t =>
                    doneIds.Contains(t.StatusId) &&
                    (t.CompletedAt is DateTime c ? c.Date <= d : t.UpdatedAt?.Date <= d || t.CreatedAt.Date <= d));

                remaining = tasks
                    .Where(t =>
                    {
                        var isDoneByDay = doneIds.Contains(t.StatusId) &&
                            (t.CompletedAt is DateTime c ? c.Date <= d : (t.UpdatedAt ?? t.CreatedAt).Date <= d);
                        return !isDoneByDay;
                    })
                    .Sum(TaskWeight);
            }

            points.Add(new DtBurndownPointDto
            {
                Date = d,
                IdealRemaining = Math.Round(ideal, 2),
                ActualRemaining = Math.Round(remaining, 2),
                DoneTasksCumulative = doneCum
            });
        }

        var doneNow = tasks.Count(t => doneIds.Contains(t.StatusId));
        var remNow = tasks.Where(t => !doneIds.Contains(t.StatusId)).Sum(TaskWeight);
        return new DtBurndownDto
        {
            SprintId = sprint?.Id,
            SprintName = sprint?.Name,
            StartDate = start,
            EndDate = end,
            Status = sprint?.Status,
            TotalEstimateHours = Math.Round(totalEst, 2),
            TotalSpentHours = Math.Round(tasks.Sum(t => t.SpentHours), 2),
            TotalTasks = tasks.Count,
            DoneTasks = doneNow,
            ProgressPct = tasks.Count == 0 ? 0 : Math.Round(100.0 * doneNow / tasks.Count, 1),
            Points = points
        };
    }

    public async Task<DtGitWebhookResultDto> ProcessCiEventAsync(DtCiBuildEventDto dto)
    {
        var result = new DtGitWebhookResultDto
        {
            Ok = true,
            Provider = string.IsNullOrWhiteSpace(dto.Provider) ? "CI" : dto.Provider
        };

        var status = (dto.Status ?? dto.Conclusion ?? "").Trim().ToLowerInvariant();
        var failed = status is "failure" or "failed" or "error" or "cancelled" or "canceled" or "timed_out";
        if (!failed)
        {
            result.Messages.Add($"وضعیت «{status}» نادیده گرفته شد (فقط failure → Problem).");
            return result;
        }

        var modules = await _db.DtProductModules.AsNoTracking().Where(m => m.IsActive).ToListAsync();
        var modByKey = modules.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);

        int? moduleId = null;
        if (!string.IsNullOrWhiteSpace(dto.ModuleKey) && modByKey.TryGetValue(dto.ModuleKey!, out var mk))
            moduleId = mk.Id;

        DtTask? task = null;
        if (!string.IsNullOrWhiteSpace(dto.TaskRef))
        {
            var pref = dto.TaskRef!.Trim();
            if (int.TryParse(pref, out var tid))
                task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == tid && !t.IsDeleted);
            if (task is null)
                task = await _db.DtTasks.FirstOrDefaultAsync(t => !t.IsDeleted && t.Number == pref);
            // DT- pattern in ref or commit message
            if (task is null)
            {
                var m = System.Text.RegularExpressions.Regex.Match(pref + " " + (dto.CommitMessage ?? ""),
                    @"DT-\d{4}-\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                    task = await _db.DtTasks.FirstOrDefaultAsync(t => !t.IsDeleted && t.Number == m.Value.ToUpperInvariant());
            }
        }
        if (task is null && !string.IsNullOrWhiteSpace(dto.CommitMessage))
        {
            var m = System.Text.RegularExpressions.Regex.Match(dto.CommitMessage!,
                @"DT-\d{4}-\d+|task:(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success)
            {
                if (m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out var tid2))
                    task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == tid2 && !t.IsDeleted);
                else
                    task = await _db.DtTasks.FirstOrDefaultAsync(t => !t.IsDeleted && t.Number == m.Value.ToUpperInvariant());
            }
        }
        if (moduleId is null && task?.ModuleId is int tm) moduleId = tm;
        if (moduleId is null && modByKey.TryGetValue("Other", out var other)) moduleId = other.Id;
        if (moduleId is null && modules.Count > 0) moduleId = modules[0].Id;

        var sha = dto.CommitSha ?? "";
        var shaShort = sha.Length <= 8 ? sha : sha[..8];
        var title = $"CI شکست: {(dto.JobName ?? dto.PipelineId ?? "pipeline")}";
        if (!string.IsNullOrEmpty(shaShort)) title += $" ({shaShort})";
        if (title.Length > 200) title = title[..200];

        // dedupe
        var already = await _db.DtProblems.AnyAsync(p =>
            !p.IsDeleted &&
            p.Title == title &&
            (p.Status == DtProblemStatus.Open || p.Status == DtProblemStatus.Investigating));
        if (already)
        {
            result.Messages.Add("Problem تکراری — ساخته نشد.");
            return result;
        }

        var desc = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(dto.Repo)) desc.AppendLine($"Repo: {dto.Repo}");
        if (!string.IsNullOrWhiteSpace(dto.Branch)) desc.AppendLine($"Branch: {dto.Branch}");
        if (!string.IsNullOrWhiteSpace(dto.PipelineId)) desc.AppendLine($"Pipeline: {dto.PipelineId}");
        if (!string.IsNullOrWhiteSpace(dto.JobName)) desc.AppendLine($"Job: {dto.JobName}");
        if (!string.IsNullOrWhiteSpace(dto.CommitMessage)) desc.AppendLine(dto.CommitMessage);
        if (!string.IsNullOrWhiteSpace(dto.Url)) desc.AppendLine(dto.Url);

        _db.DtProblems.Add(new DtProblem
        {
            Title = title,
            Description = desc.ToString(),
            Severity = DtProblemSeverity.Error,
            Status = DtProblemStatus.Open,
            ModuleId = moduleId,
            TaskId = task?.Id,
            ReporterUserId = 0,
            ReporterName = $"CI/{dto.Provider}",
            Environment = dto.Branch,
            StackTrace = dto.Url,
            CreatedAt = DateTime.Now
        });
        result.ProblemsCreated++;
        result.Messages.Add($"Problem ثبت شد: {title}");

        if (task is not null)
        {
            await LogAsync(task.Id, 0, $"CI/{dto.Provider}", "CiFailed", title);
            task.UpdatedAt = DateTime.Now;
            result.TasksLinked++;
        }

        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
        return result;
    }

    // ---------- sub-tasks & dependencies ----------

    public async Task<DtTaskDetailDto> CreateSubTaskAsync(int parentId, DtTaskUpsertDto dto, int userId, string userName)
    {
        dto.ParentTaskId = parentId;
        return await CreateTaskAsync(dto, userId, userName);
    }

    public async Task ConvertToSubTaskAsync(int taskId, int parentId, int userId, string userName)
    {
        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");
        var parent = await _db.DtTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == parentId && !t.IsDeleted)
            ?? throw new InvalidOperationException("والد یافت نشد.");

        await EnsureNoCycleParentAsync(taskId, parentId);
        var depth = await GetDepthAsync(parentId);
        // عمق والد + ۱ (خودش) نباید از سقف بگذرد؛ همچنین عمق فرزندانش
        if (depth >= MaxSubTaskDepth)
            throw new InvalidOperationException($"حداکثر عمق ساب‌تسک {MaxSubTaskDepth} سطح است.");

        var childDepth = await MaxChildDepthAsync(taskId);
        if (depth + 1 + childDepth > MaxSubTaskDepth)
            throw new InvalidOperationException("با انتقال، عمق فرزندان از سقف مجاز بیشتر می‌شود.");

        task.ParentTaskId = parentId;
        task.UpdatedAt = DateTime.Now;
        if (task.ModuleId is null) task.ModuleId = parent.ModuleId;
        if (task.SprintId is null) task.SprintId = parent.SprintId;

        await LogAsync(taskId, userId, userName, "ConvertedToSubTask", $"والد: {parent.Number}");
        await LogAsync(parentId, userId, userName, "SubTaskCreated", task.Number);
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
    }

    private async Task<int> MaxChildDepthAsync(int taskId)
    {
        var kids = await _db.DtTasks.AsNoTracking()
            .Where(t => !t.IsDeleted && t.ParentTaskId == taskId).Select(t => t.Id).ToListAsync();
        if (kids.Count == 0) return 0;
        var max = 0;
        foreach (var k in kids)
            max = Math.Max(max, 1 + await MaxChildDepthAsync(k));
        return max;
    }

    public async Task PromoteSubTaskAsync(int taskId, int userId, string userName)
    {
        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");
        if (task.ParentTaskId is null)
            throw new InvalidOperationException("این تسک از قبل ریشه است.");
        var oldParent = task.ParentTaskId.Value;
        task.ParentTaskId = null;
        task.UpdatedAt = DateTime.Now;
        await LogAsync(taskId, userId, userName, "Promoted", null);
        await LogAsync(oldParent, userId, userName, "Updated", $"ساب‌تسک {task.Number} ارتقا یافت");
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
    }

    public async Task<DtDependencyDto> AddDependencyAsync(int taskId, DtDependencyCreateDto dto, int userId, string userName)
    {
        if (dto.DependsOnTaskId <= 0)
            throw new InvalidOperationException("تسک وابستگی را مشخص کنید.");
        if (!DtDependencyKind.IsValid(dto.Kind)) dto.Kind = DtDependencyKind.Blocks;

        var task = await _db.DtTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");
        var dep = await _db.DtTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.DependsOnTaskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک وابستگی یافت نشد.");

        await EnsureNoDependencyCycleAsync(taskId, dto.DependsOnTaskId);

        if (await _db.DtTaskDependencies.AnyAsync(d => d.TaskId == taskId && d.DependsOnTaskId == dto.DependsOnTaskId))
            throw new InvalidOperationException("این وابستگی قبلاً ثبت شده است.");

        var row = new DtTaskDependency
        {
            TaskId = taskId,
            DependsOnTaskId = dto.DependsOnTaskId,
            Kind = dto.Kind,
            CreatedByUserId = userId,
            CreatedByName = userName,
            CreatedAt = DateTime.Now
        };
        _db.DtTaskDependencies.Add(row);
        await LogAsync(taskId, userId, userName, "DependencyAdded",
            $"{DtDependencyKind.ToFa(dto.Kind)} ← {dep.Number}");
        await LogAsync(dto.DependsOnTaskId, userId, userName, "DependencyAdded",
            $"مسدودکنندهٔ {task.Number}");
        var tEntity = await _db.DtTasks.FindAsync(taskId);
        if (tEntity is not null) tEntity.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }

        var st = await _db.DtWorkflowStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == dep.StatusId);
        return new DtDependencyDto
        {
            Id = row.Id,
            TaskId = taskId,
            TaskNumber = task.Number,
            TaskTitle = task.Title,
            DependsOnTaskId = dep.Id,
            DependsOnNumber = dep.Number,
            DependsOnTitle = dep.Title,
            Kind = row.Kind,
            KindFa = DtDependencyKind.ToFa(row.Kind),
            DependsOnIsDone = st?.IsDone ?? false,
            DependsOnStatusName = st?.NameFa,
            DependsOnStatusColor = st?.Color,
            CreatedByName = userName,
            CreatedAt = row.CreatedAt
        };
    }

    public async Task RemoveDependencyAsync(int dependencyId, int userId, string userName)
    {
        var d = await _db.DtTaskDependencies.FindAsync(dependencyId)
            ?? throw new InvalidOperationException("وابستگی یافت نشد.");
        var taskId = d.TaskId;
        var other = d.DependsOnTaskId;
        _db.DtTaskDependencies.Remove(d);
        await LogAsync(taskId, userId, userName, "DependencyRemoved", $"#{dependencyId}");
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
    }

    public async Task<List<DtTaskListItemDto>> SearchTasksAsync(string? q, int? excludeId, int take = 20, int skip = 0)
    {
        take = Math.Clamp(take, 1, 50);
        var query = TasksBase();
        if (excludeId is int ex) query = query.Where(t => t.Id != ex);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(t => t.Title.Contains(term) || t.Number.Contains(term));
        }
        var items = await query.OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt).Skip(Math.Max(0, skip)).Take(take).ToListAsync();
        var statuses = await _db.DtWorkflowStatuses.AsNoTracking().ToDictionaryAsync(s => s.Id);
        return items.Select(t => MapList(t, statuses.GetValueOrDefault(t.StatusId), null, null)).ToList();
    }

    public async Task DeleteTaskAsync(int id, int userId, string userName)
    {
        var entity = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.Now;
        entity.DeletedByUserId = userId;
        entity.UpdatedAt = DateTime.Now;
        await LogAsync(id, userId, userName, "Deleted", null);
        await _db.SaveChangesAsync();
    }

    public async Task<DtTaskCommentDto> AddCommentAsync(int taskId, string text, int userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("متن نظر خالی است.");
        _ = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");

        var c = new DtTaskComment
        {
            TaskId = taskId,
            AuthorUserId = userId,
            AuthorName = userName,
            Text = text.Trim(),
            CreatedAt = DateTime.Now
        };
        _db.DtTaskComments.Add(c);
        await LogAsync(taskId, userId, userName, "Commented", text.Trim().Length > 80 ? text.Trim()[..80] + "…" : text.Trim());
        var task = await _db.DtTasks.FindAsync(taskId);
        if (task is not null) task.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return new DtTaskCommentDto
        {
            Id = c.Id, AuthorUserId = c.AuthorUserId, AuthorName = c.AuthorName,
            Text = c.Text, CreatedAt = c.CreatedAt
        };
    }

    public async Task<DtTaskGitLinkDto> AddGitLinkAsync(int taskId, DtTaskGitLinkCreateDto dto, int userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.Ref))
            throw new InvalidOperationException("مرجع گیت (SHA / شاخه / PR) الزامی است.");
        if (!DtGitLinkKind.IsValid(dto.Kind)) dto.Kind = DtGitLinkKind.Commit;

        _ = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");

        var g = new DtTaskGitLink
        {
            TaskId = taskId,
            Kind = dto.Kind,
            Ref = dto.Ref.Trim(),
            Url = dto.Url?.Trim(),
            Title = dto.Title?.Trim(),
            AddedByUserId = userId,
            AddedByName = userName,
            CreatedAt = DateTime.Now
        };
        _db.DtTaskGitLinks.Add(g);
        await LogAsync(taskId, userId, userName, "GitLinked", $"{DtGitLinkKind.ToFa(dto.Kind)}: {dto.Ref}");
        var task = await _db.DtTasks.FindAsync(taskId);
        if (task is not null) task.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return new DtTaskGitLinkDto
        {
            Id = g.Id, Kind = g.Kind, KindFa = DtGitLinkKind.ToFa(g.Kind),
            Ref = g.Ref, Url = g.Url, Title = g.Title,
            AddedByName = g.AddedByName, CreatedAt = g.CreatedAt
        };
    }

    public async Task DeleteGitLinkAsync(int linkId)
    {
        var g = await _db.DtTaskGitLinks.FindAsync(linkId)
            ?? throw new InvalidOperationException("لینک یافت نشد.");
        _db.DtTaskGitLinks.Remove(g);
        await _db.SaveChangesAsync();
    }

    public async Task<DtTimeEntryDto> AddTimeAsync(int taskId, DtTimeEntryCreateDto dto, int userId, string userName)
    {
        if (dto.Hours <= 0 || dto.Hours > 24)
            throw new InvalidOperationException("ساعت باید بین ۰ و ۲۴ باشد.");
        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");

        var e = new DtTimeEntry
        {
            TaskId = taskId,
            UserId = userId,
            UserName = userName,
            Hours = dto.Hours,
            WorkDate = (dto.WorkDate ?? DateTime.Today).Date,
            Note = dto.Note,
            CreatedAt = DateTime.Now
        };
        _db.DtTimeEntries.Add(e);
        task.SpentHours += dto.Hours;
        task.UpdatedAt = DateTime.Now;
        await LogAsync(taskId, userId, userName, "TimeLogged", $"{dto.Hours} ساعت");
        await _db.SaveChangesAsync();

        return new DtTimeEntryDto
        {
            Id = e.Id, UserId = e.UserId, UserName = e.UserName,
            Hours = e.Hours, WorkDate = e.WorkDate, Note = e.Note, CreatedAt = e.CreatedAt
        };
    }

    // ---------- problems ----------
    private static DtProblemDto MapProblem(DtProblem p, DtProductModule? mod, DtTask? task) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Description = p.Description,
        Severity = p.Severity,
        SeverityFa = DtProblemSeverity.ToFa(p.Severity),
        Status = p.Status,
        StatusFa = DtProblemStatus.ToFa(p.Status),
        ModuleId = p.ModuleId,
        ModuleName = mod?.NameFa,
        ModuleColor = mod?.Color,
        TaskId = p.TaskId,
        TaskNumber = task?.Number,
        TaskTitle = task?.Title,
        AssigneeUserId = p.AssigneeUserId,
        AssigneeName = p.AssigneeName,
        ReporterName = p.ReporterName,
        StackTrace = p.StackTrace,
        Environment = p.Environment,
        CreatedAt = p.CreatedAt,
        ResolvedAt = p.ResolvedAt,
        ResolutionNote = p.ResolutionNote
    };

    public async Task<List<DtProblemDto>> QueryProblemsAsync(string? severity, string? status, int? moduleId, int? taskId, Paging.Request? pagination = null)
    {
        var q = _db.DtProblems.AsNoTracking().Where(p => !p.IsDeleted);
        if (!string.IsNullOrWhiteSpace(severity)) q = q.Where(p => p.Severity == severity);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == status);
        if (moduleId is int mid) q = q.Where(p => p.ModuleId == mid);
        if (taskId is int tid) q = q.Where(p => p.TaskId == tid);

        var list = await q.OrderByDescending(p => p.CreatedAt).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 200);
        var mods = await _db.DtProductModules.AsNoTracking().ToListAsync();
        var taskIds = list.Where(p => p.TaskId != null).Select(p => p.TaskId!.Value).Distinct().ToList();
        var tasks = await _db.DtTasks.AsNoTracking().Where(t => taskIds.Contains(t.Id)).ToListAsync();

        return list.Select(p => MapProblem(p,
            mods.FirstOrDefault(m => m.Id == p.ModuleId),
            tasks.FirstOrDefault(t => t.Id == p.TaskId))).ToList();
    }

    public async Task<DtProblemDto> UpsertProblemAsync(int? id, DtProblemUpsertDto dto, int userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new InvalidOperationException("عنوان مشکل الزامی است.");
        if (!DtProblemSeverity.IsValid(dto.Severity)) dto.Severity = DtProblemSeverity.Error;

        DtProblem entity;
        if (id is int existing)
        {
            entity = await _db.DtProblems.FirstOrDefaultAsync(p => p.Id == existing && !p.IsDeleted)
                ?? throw new InvalidOperationException("مشکل یافت نشد.");
        }
        else
        {
            entity = new DtProblem
            {
                ReporterUserId = userId,
                ReporterName = userName,
                CreatedAt = DateTime.Now,
                Status = DtProblemStatus.Open
            };
            _db.DtProblems.Add(entity);
        }

        entity.Title = dto.Title.Trim();
        entity.Description = dto.Description;
        entity.Severity = dto.Severity;
        entity.ModuleId = dto.ModuleId;
        entity.TaskId = dto.TaskId;
        entity.StackTrace = dto.StackTrace;
        entity.Environment = dto.Environment;

        if (dto.AssigneeUserId != entity.AssigneeUserId)
        {
            var (name, _) = await UserNameAsync(dto.AssigneeUserId);
            entity.AssigneeUserId = dto.AssigneeUserId;
            entity.AssigneeName = name;
        }

        if (!string.IsNullOrWhiteSpace(dto.Status) && DtProblemStatus.IsValid(dto.Status))
        {
            var wasOpen = entity.Status is DtProblemStatus.Open or DtProblemStatus.Investigating;
            entity.Status = dto.Status;
            if (dto.Status is DtProblemStatus.Resolved or DtProblemStatus.WontFix)
            {
                entity.ResolvedAt ??= DateTime.Now;
                entity.ResolutionNote = dto.ResolutionNote;
            }
            else if (wasOpen == false)
            {
                entity.ResolvedAt = null;
            }
        }
        else if (!string.IsNullOrWhiteSpace(dto.ResolutionNote))
        {
            entity.ResolutionNote = dto.ResolutionNote;
        }

        await _db.SaveChangesAsync();
        var mod = entity.ModuleId is int mid
            ? await _db.DtProductModules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mid) : null;
        var task = entity.TaskId is int tid
            ? await _db.DtTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid) : null;
        return MapProblem(entity, mod, task);
    }

    public async Task DeleteProblemAsync(int id)
    {
        var p = await _db.DtProblems.FindAsync(id)
            ?? throw new InvalidOperationException("مشکل یافت نشد.");
        p.IsDeleted = true;
        await _db.SaveChangesAsync();
    }

    // ---------- changelog ----------
    private static DtModuleChangeDto MapChange(DtModuleChange c, DtProductModule? mod, DtTask? task) => new()
    {
        Id = c.Id,
        ModuleId = c.ModuleId,
        ModuleName = mod?.NameFa ?? "",
        ModuleKey = mod?.Key ?? "",
        ModuleColor = mod?.Color ?? "#64748b",
        TaskId = c.TaskId,
        TaskNumber = task?.Number,
        Summary = c.Summary,
        Details = c.Details,
        CommitSha = c.CommitSha,
        Branch = c.Branch,
        CommitUrl = c.CommitUrl,
        AuthorName = c.AuthorName,
        ChangedAt = c.ChangedAt
    };

    public async Task<List<DtModuleChangeDto>> QueryChangesAsync(int? moduleId, int take = 50, int skip = 0)
    {
        take = Math.Clamp(take, 1, 200);
        var q = _db.DtModuleChanges.AsNoTracking().AsQueryable();
        if (moduleId is int mid) q = q.Where(c => c.ModuleId == mid);
        var list = await q.OrderByDescending(c => c.ChangedAt).Skip(Math.Max(0, skip)).Take(take).ToListAsync();
        var mods = await _db.DtProductModules.AsNoTracking().ToListAsync();
        var taskIds = list.Where(c => c.TaskId != null).Select(c => c.TaskId!.Value).Distinct().ToList();
        var tasks = await _db.DtTasks.AsNoTracking().Where(t => taskIds.Contains(t.Id)).ToListAsync();
        return list.Select(c => MapChange(c,
            mods.FirstOrDefault(m => m.Id == c.ModuleId),
            tasks.FirstOrDefault(t => t.Id == c.TaskId))).ToList();
    }

    public async Task<DtModuleChangeDto> CreateChangeAsync(DtModuleChangeCreateDto dto, int userId, string userName)
    {
        if (dto.ModuleId <= 0) throw new InvalidOperationException("ماژول الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Summary)) throw new InvalidOperationException("خلاصه تغییر الزامی است.");
        _ = await _db.DtProductModules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == dto.ModuleId)
            ?? throw new InvalidOperationException("ماژول یافت نشد.");

        var c = new DtModuleChange
        {
            ModuleId = dto.ModuleId,
            TaskId = dto.TaskId,
            Summary = dto.Summary.Trim(),
            Details = dto.Details,
            CommitSha = dto.CommitSha?.Trim(),
            Branch = dto.Branch?.Trim(),
            CommitUrl = dto.CommitUrl?.Trim(),
            AuthorUserId = userId,
            AuthorName = userName,
            ChangedAt = dto.ChangedAt ?? DateTime.Now,
            CreatedAt = DateTime.Now
        };
        _db.DtModuleChanges.Add(c);
        await _db.SaveChangesAsync();

        var mod = await _db.DtProductModules.AsNoTracking().FirstAsync(m => m.Id == c.ModuleId);
        var task = c.TaskId is int tid
            ? await _db.DtTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid) : null;
        return MapChange(c, mod, task);
    }

    public async Task DeleteChangeAsync(int id)
    {
        var c = await _db.DtModuleChanges.FindAsync(id)
            ?? throw new InvalidOperationException("رکورد یافت نشد.");
        _db.DtModuleChanges.Remove(c);
        await _db.SaveChangesAsync();
    }

    // ---------- WorkOrder bridge ----------

    private static int MapWoPriority(int dtPriority) => dtPriority switch
    {
        DtPriority.Critical => WorkOrderPriority.Urgent,
        DtPriority.High => WorkOrderPriority.High,
        DtPriority.Low => WorkOrderPriority.Low,
        _ => WorkOrderPriority.Normal
    };

    public async Task<DtWorkOrderLinkDto> CreateWorkOrderFromTaskAsync(int taskId, DtCreateWorkOrderDto dto, int userId, string userName)
    {
        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");
        if (task.WorkOrderId is int existing)
            throw new InvalidOperationException($"این تسک قبلاً به دستور کار {task.WorkOrderNumber ?? existing.ToString()} متصل است.");

        var assignees = new HashSet<int>();
        if (task.AssigneeUserId is int aid && aid > 0) assignees.Add(aid);
        if (dto.ExtraAssigneeUserIds != null)
            foreach (var x in dto.ExtraAssigneeUserIds.Where(i => i > 0)) assignees.Add(x);
        if (assignees.Count == 0) assignees.Add(userId);

        var users = await _db.Users.Where(u => assignees.Contains(u.Id) && u.IsActive).ToListAsync();
        if (users.Count == 0)
            throw new InvalidOperationException("گیرندهٔ معتبری برای دستور کار یافت نشد.");

        var due = dto.DueAt ?? task.DueAt ?? DateTime.Now.AddDays(2);
        if (due <= DateTime.Now) due = DateTime.Now.AddHours(4);

        var mod = task.ModuleId is int mid
            ? await _db.DtProductModules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mid) : null;

        var desc = task.Description ?? "";
        if (mod != null) desc = $"[ماژول: {mod.NameFa}]\n" + desc;
        desc = $"از تسک {task.Number} — نوع: {DtTaskType.ToFa(task.Type)}\n" + desc;
        if (desc.Length > 8000) desc = desc[..8000];

        var tags = new List<string> { "DevTeam", task.Type };
        if (mod != null) tags.Add(mod.Key);
        if (!string.IsNullOrWhiteSpace(task.Tags))
            tags.AddRange(task.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var tagStr = "," + string.Join(",", tags.Distinct().Take(5).Select(t => t.Length > 30 ? t[..30] : t)) + ",";

        var wo = new WorkOrder
        {
            Title = task.Title.Length > 200 ? task.Title[..200] : task.Title,
            Description = desc,
            OwnerUserId = userId,
            OwnerName = userName,
            DueAt = due,
            Priority = MapWoPriority(task.Priority),
            Recurrence = WorkOrderRecurrence.None,
            SourceModule = "DevTeam",
            SourceId = task.Id,
            Tags = tagStr
        };
        _db.WorkOrders.Add(wo);
        await _db.SaveChangesAsync();

        wo.Number = $"WO/{new PersianCalendar().GetYear(wo.CreatedAt)}/{wo.Id}";
        foreach (var u in users)
            _db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = wo.Id, UserId = u.Id, Name = UserDisplay.Name(u) });

        _db.WorkOrderLogs.Add(new WorkOrderLog
        {
            OrderId = wo.Id,
            ActorName = userName,
            Action = "Created",
            Text = $"از تسک DevTeam {task.Number} ساخته شد"
        });

        task.WorkOrderId = wo.Id;
        task.WorkOrderNumber = wo.Number;
        task.UpdatedAt = DateTime.Now;
        await LogAsync(task.Id, userId, userName, "WorkOrderLinked", wo.Number);
        await _db.SaveChangesAsync();

        foreach (var uid in users.Select(u => u.Id).Where(i => i != userId))
        {
            try
            {
                await _notify.SendAsync(uid, "دستور کار از تسک توسعه 📋",
                    $"{wo.Number} — «{wo.Title}» (از {task.Number})",
                    userName, "دستور کار", $"/work-orders?open={wo.Id}");
            }
            catch { }
        }
        try
        {
            await _notify.BroadcastChangedAsync("workorders");
            await _notify.BroadcastChangedAsync("devteam");
        }
        catch { }

        return new DtWorkOrderLinkDto
        {
            TaskId = task.Id,
            WorkOrderId = wo.Id,
            WorkOrderNumber = wo.Number,
            Link = $"/work-orders?open={wo.Id}"
        };
    }

    public async Task<DtWorkOrderLinkDto> LinkWorkOrderAsync(int taskId, int workOrderId, int userId, string userName)
    {
        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");
        var wo = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId)
            ?? throw new InvalidOperationException("دستور کار یافت نشد.");

        task.WorkOrderId = wo.Id;
        task.WorkOrderNumber = wo.Number;
        task.UpdatedAt = DateTime.Now;
        await LogAsync(taskId, userId, userName, "WorkOrderLinked", wo.Number);
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }

        return new DtWorkOrderLinkDto
        {
            TaskId = taskId,
            WorkOrderId = wo.Id,
            WorkOrderNumber = wo.Number,
            Link = $"/work-orders?open={wo.Id}"
        };
    }

    public async Task UnlinkWorkOrderAsync(int taskId, int userId, string userName)
    {
        var task = await _db.DtTasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted)
            ?? throw new InvalidOperationException("تسک یافت نشد.");
        var old = task.WorkOrderNumber;
        task.WorkOrderId = null;
        task.WorkOrderNumber = null;
        task.UpdatedAt = DateTime.Now;
        await LogAsync(taskId, userId, userName, "WorkOrderUnlinked", old);
        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
    }

    // ---------- Git webhook ----------

    private sealed record GitCommitInfo(string Sha, string Message, string? Url, string? AuthorName, string? Branch, DateTime When);

    public Task<DtGitWebhookResultDto> ProcessGitHubPushAsync(JsonElement payload) =>
        ProcessPushAsync("GitHub", ExtractGitHubCommits(payload));

    public Task<DtGitWebhookResultDto> ProcessGitLabPushAsync(JsonElement payload) =>
        ProcessPushAsync("GitLab", ExtractGitLabCommits(payload));

    private static List<GitCommitInfo> ExtractGitHubCommits(JsonElement payload)
    {
        var list = new List<GitCommitInfo>();
        var branch = "";
        if (payload.TryGetProperty("ref", out var refEl))
        {
            var r = refEl.GetString() ?? "";
            branch = r.StartsWith("refs/heads/") ? r["refs/heads/".Length..] : r;
        }
        if (!payload.TryGetProperty("commits", out var commits) || commits.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var c in commits.EnumerateArray())
        {
            var sha = c.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
            var msg = c.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            var url = c.TryGetProperty("url", out var u) ? u.GetString() : null;
            string? author = null;
            if (c.TryGetProperty("author", out var a))
            {
                if (a.TryGetProperty("name", out var n)) author = n.GetString();
                else if (a.TryGetProperty("username", out var un)) author = un.GetString();
            }
            var when = DateTime.Now;
            if (c.TryGetProperty("timestamp", out var ts) && DateTime.TryParse(ts.GetString(), out var parsed))
                when = parsed.ToLocalTime();
            if (!string.IsNullOrWhiteSpace(sha) && !string.IsNullOrWhiteSpace(msg))
                list.Add(new GitCommitInfo(sha, msg, url, author, branch, when));
        }
        return list;
    }

    private static List<GitCommitInfo> ExtractGitLabCommits(JsonElement payload)
    {
        var list = new List<GitCommitInfo>();
        var branch = "";
        if (payload.TryGetProperty("ref", out var refEl))
        {
            var r = refEl.GetString() ?? "";
            branch = r.StartsWith("refs/heads/") ? r["refs/heads/".Length..] : r;
        }
        if (!payload.TryGetProperty("commits", out var commits) || commits.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var c in commits.EnumerateArray())
        {
            var sha = c.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
            var msg = c.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            var url = c.TryGetProperty("url", out var u) ? u.GetString() : null;
            string? author = null;
            if (c.TryGetProperty("author", out var a) && a.TryGetProperty("name", out var n))
                author = n.GetString();
            var when = DateTime.Now;
            if (c.TryGetProperty("timestamp", out var ts) && DateTime.TryParse(ts.GetString(), out var parsed))
                when = parsed.ToLocalTime();
            if (!string.IsNullOrWhiteSpace(sha) && !string.IsNullOrWhiteSpace(msg))
                list.Add(new GitCommitInfo(sha, msg, url, author, branch, when));
        }
        return list;
    }

    private async Task<DtGitWebhookResultDto> ProcessPushAsync(string provider, List<GitCommitInfo> commits)
    {
        var result = new DtGitWebhookResultDto { Ok = true, Provider = provider };
        if (commits.Count == 0)
        {
            result.Messages.Add("کامیتی در payload نبود.");
            return result;
        }

        var modules = await _db.DtProductModules.AsNoTracking().Where(m => m.IsActive).ToListAsync();
        var modByKey = modules.ToDictionary(m => m.Key, m => m, StringComparer.OrdinalIgnoreCase);

        // system author for webhook-created rows
        const int sysUserId = 0;
        var sysName = $"{provider} Webhook";

        foreach (var commit in commits)
        {
            result.CommitsProcessed++;
            var summary = FirstLine(commit.Message);
            var taskIds = new HashSet<int>();
            var taskNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in TaskRefRx.Matches(commit.Message))
            {
                if (match.Groups[1].Success) taskNumbers.Add(match.Groups[1].Value.ToUpperInvariant());
                if (match.Groups[2].Success && int.TryParse(match.Groups[2].Value, out var tid))
                    taskIds.Add(tid);
            }

            var tasks = new List<DtTask>();
            if (taskNumbers.Count > 0)
            {
                var numList = taskNumbers.ToList();
                // مقایسه بدون حساسیت به حروف با نرمال‌سازی سمت کلاینت کوئری
                var byNum = await _db.DtTasks.Where(t => !t.IsDeleted && numList.Contains(t.Number)).ToListAsync();
                // اگر کیس فرق داشت، fallback در حافظه
                if (byNum.Count == 0)
                {
                    var allOpen = await _db.DtTasks.Where(t => !t.IsDeleted && t.Number.StartsWith("DT-")).ToListAsync();
                    byNum = allOpen.Where(t => numList.Contains(t.Number, StringComparer.OrdinalIgnoreCase)).ToList();
                }
                tasks.AddRange(byNum);
            }
            if (taskIds.Count > 0)
            {
                var idList = taskIds.ToList();
                var existing = tasks.Select(t => t.Id).ToHashSet();
                var byId = await _db.DtTasks.Where(t => !t.IsDeleted && idList.Contains(t.Id)).ToListAsync();
                tasks.AddRange(byId.Where(t => !existing.Contains(t.Id)));
            }

            // module detection: explicit mod:Key or from linked tasks
            int? moduleId = null;
            var modMatch = ModuleKeyRx.Match(commit.Message);
            if (modMatch.Success && modByKey.TryGetValue(modMatch.Groups[1].Value, out var modHit))
                moduleId = modHit.Id;
            if (moduleId is null)
                moduleId = tasks.Select(t => t.ModuleId).FirstOrDefault(m => m != null);

            // fallback module: Other
            if (moduleId is null && modByKey.TryGetValue("Other", out var other))
                moduleId = other.Id;
            if (moduleId is null && modules.Count > 0)
                moduleId = modules[0].Id;

            // changelog (dedupe by sha+module)
            if (moduleId is int mid)
            {
                var shaShort = commit.Sha.Length > 40 ? commit.Sha[..40] : commit.Sha;
                var exists = await _db.DtModuleChanges.AnyAsync(c => c.CommitSha == shaShort && c.ModuleId == mid);
                if (!exists)
                {
                    _db.DtModuleChanges.Add(new DtModuleChange
                    {
                        ModuleId = mid,
                        TaskId = tasks.FirstOrDefault()?.Id,
                        Summary = summary.Length > 300 ? summary[..300] : summary,
                        Details = commit.Message.Length > 2000 ? commit.Message[..2000] : commit.Message,
                        CommitSha = shaShort,
                        Branch = commit.Branch,
                        CommitUrl = commit.Url,
                        AuthorUserId = sysUserId,
                        AuthorName = string.IsNullOrWhiteSpace(commit.AuthorName) ? sysName : commit.AuthorName!,
                        ChangedAt = commit.When,
                        CreatedAt = DateTime.Now
                    });
                    result.ChangesCreated++;
                }
            }

            foreach (var task in tasks)
            {
                // git link (dedupe by ref)
                var sha = commit.Sha;
                var linked = await _db.DtTaskGitLinks.AnyAsync(g => g.TaskId == task.Id && g.Ref == sha);
                if (!linked)
                {
                    _db.DtTaskGitLinks.Add(new DtTaskGitLink
                    {
                        TaskId = task.Id,
                        Kind = DtGitLinkKind.Commit,
                        Ref = sha.Length > 200 ? sha[..200] : sha,
                        Url = commit.Url,
                        Title = summary.Length > 300 ? summary[..300] : summary,
                        AddedByUserId = sysUserId,
                        AddedByName = sysName,
                        CreatedAt = DateTime.Now
                    });
                    result.TasksLinked++;
                }

                await LogAsync(task.Id, sysUserId, sysName, "GitWebhook",
                    $"{provider} {sha[..Math.Min(8, sha.Length)]}: {summary}");
                task.UpdatedAt = DateTime.Now;

                // failed / fix markers → problem
                var lower = commit.Message.ToLowerInvariant();
                if (lower.Contains("fix") || lower.Contains("hotfix") || lower.Contains("error") || lower.Contains("باگ") || lower.Contains("خطا"))
                {
                    // range/index نباید داخل expression tree مربوط به EF برود
                    var shaShort8 = sha.Length <= 8 ? sha : sha.Substring(0, 8);
                    var already = await _db.DtProblems.AnyAsync(p =>
                        !p.IsDeleted && p.TaskId == task.Id &&
                        p.Title.Contains(shaShort8));
                    if (!already)
                    {
                        _db.DtProblems.Add(new DtProblem
                        {
                            Title = $"کامیت {shaShort8}: {summary}",
                            Description = commit.Message,
                            Severity = (lower.Contains("fail") || lower.Contains("error") || lower.Contains("خطا"))
                                ? DtProblemSeverity.Error : DtProblemSeverity.Warning,
                            Status = DtProblemStatus.Open,
                            ModuleId = task.ModuleId ?? moduleId,
                            TaskId = task.Id,
                            AssigneeUserId = task.AssigneeUserId,
                            AssigneeName = task.AssigneeName,
                            ReporterUserId = sysUserId,
                            ReporterName = sysName,
                            Environment = commit.Branch,
                            CreatedAt = DateTime.Now
                        });
                        result.ProblemsCreated++;
                    }
                }
            }

            if (tasks.Count == 0)
                result.Messages.Add($"کامیت {commit.Sha[..Math.Min(8, commit.Sha.Length)]}: ارجاع تسک نداشت — فقط changelog.");
            else
                result.Messages.Add($"کامیت {commit.Sha[..Math.Min(8, commit.Sha.Length)]}: {tasks.Count} تسک لینک شد.");
        }

        await _db.SaveChangesAsync();
        try { await _notify.BroadcastChangedAsync("devteam"); } catch { }
        return result;
    }

    private static string FirstLine(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return "(بدون پیام)";
        var line = msg.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? msg;
        return line.Trim();
    }
}
