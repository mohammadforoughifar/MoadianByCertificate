namespace Inventory.Shared.Dtos;

// ============================================================
//  DTOهای ماژول «میز کار توسعه» (DevTeam)
// ============================================================

public class DtAccessDto
{
    public int UserId { get; set; }
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
    public bool CanManage { get; set; }
    public bool CanAssign { get; set; }
}

public class DtWorkflowStatusDto
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string NameFa { get; set; } = "";
    public string Color { get; set; } = "#64748b";
    public int SortOrder { get; set; }
    public bool IsInitial { get; set; }
    public bool IsDone { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>سقف WIP ستون؛ null/0 = نامحدود</summary>
    public int? WipLimit { get; set; }
}

public class DtProductModuleDto
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string NameFa { get; set; } = "";
    public string? Icon { get; set; }
    public string Color { get; set; } = "#4f46e5";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>تعداد تسک‌های باز (غیر done)</summary>
    public int OpenTaskCount { get; set; }
    public int OpenProblemCount { get; set; }
    public int RecentChangeCount { get; set; }
}

public class DtSprintDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Goal { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = "Planned";
    public string StatusFa { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string CreatedByName { get; set; } = "";

    public int TaskCount { get; set; }
    public int DoneTaskCount { get; set; }
    public decimal TotalEstimate { get; set; }
    public decimal TotalSpent { get; set; }
}

public class DtTaskListItemDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string TypeFa { get; set; } = "";
    public int Priority { get; set; }
    public string PriorityFa { get; set; } = "";

    public int StatusId { get; set; }
    public string StatusKey { get; set; } = "";
    public string StatusName { get; set; } = "";
    public string StatusColor { get; set; } = "";
    public bool StatusIsDone { get; set; }
    public bool StatusIsBlocked { get; set; }

    public int? ModuleId { get; set; }
    public string? ModuleKey { get; set; }
    public string? ModuleName { get; set; }
    public string? ModuleColor { get; set; }

    public int? SprintId { get; set; }
    public string? SprintName { get; set; }

    public int? AssigneeUserId { get; set; }
    public string? AssigneeName { get; set; }
    public string ReporterName { get; set; } = "";

    public DateTime? DueAt { get; set; }
    public bool IsOverdue { get; set; }
    public decimal? EstimateHours { get; set; }
    public decimal SpentHours { get; set; }
    public int Progress { get; set; }
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int CommentCount { get; set; }
    public int GitLinkCount { get; set; }
    public int OpenProblemCount { get; set; }

    public int? WorkOrderId { get; set; }
    public string? WorkOrderNumber { get; set; }

    /// <summary>شناسه والد — null = تسک ریشه</summary>
    public int? ParentTaskId { get; set; }
    public string? ParentTaskNumber { get; set; }
    public string? ParentTaskTitle { get; set; }

    public int SubTaskCount { get; set; }
    public int SubTaskDoneCount { get; set; }

    /// <summary>تعداد وابستگی‌های باز (Blocks) که هنوز تمام نشده‌اند</summary>
    public int OpenBlockerCount { get; set; }

    /// <summary>به‌خاطر وابستگی هنوز باز، منطقاً مسدود است</summary>
    public bool IsDependencyBlocked { get; set; }

    /// <summary>ترتیب در ستون کانبان / بین خواهر-برادرها</summary>
    public int SortOrder { get; set; }

    /// <summary>تایمر زنده در حال اجراست</summary>
    public bool TimerRunning { get; set; }
    public DateTime? TimerStartedAt { get; set; }
    public int? TimerStartedByUserId { get; set; }
}

public class DtTaskDetailDto : DtTaskListItemDto
{
    public string? Description { get; set; }
    public int ReporterUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<DtTaskCommentDto> Comments { get; set; } = new();
    public List<DtTaskActivityDto> Activities { get; set; } = new();
    public List<DtTaskGitLinkDto> GitLinks { get; set; } = new();
    public List<DtTimeEntryDto> TimeEntries { get; set; } = new();

    public List<DtTaskListItemDto> SubTasks { get; set; } = new();
    /// <summary>تسک‌هایی که این تسک را مسدود کرده‌اند (باید اول تمام شوند)</summary>
    public List<DtDependencyDto> BlockedBy { get; set; } = new();
    /// <summary>تسک‌هایی که منتظر این تسک‌اند</summary>
    public List<DtDependencyDto> Blocking { get; set; } = new();

    public List<DtChecklistItemDto> Checklist { get; set; } = new();
    public int ChecklistTotal { get; set; }
    public int ChecklistDone { get; set; }
}

public class DtTaskUpsertDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Type { get; set; } = "Feature";
    public int Priority { get; set; } = 1;
    public int? StatusId { get; set; }
    public int? ModuleId { get; set; }
    public int? SprintId { get; set; }
    public int? AssigneeUserId { get; set; }
    public DateTime? DueAt { get; set; }
    public decimal? EstimateHours { get; set; }
    public int Progress { get; set; }
    public string? Tags { get; set; }

    /// <summary>برای ساخت ساب‌تسک — فقط در Create</summary>
    public int? ParentTaskId { get; set; }
}

public class DtDependencyDto
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string TaskNumber { get; set; } = "";
    public string TaskTitle { get; set; } = "";
    public int DependsOnTaskId { get; set; }
    public string DependsOnNumber { get; set; } = "";
    public string DependsOnTitle { get; set; } = "";
    public string Kind { get; set; } = "Blocks";
    public string KindFa { get; set; } = "";
    public bool DependsOnIsDone { get; set; }
    public string? DependsOnStatusName { get; set; }
    public string? DependsOnStatusColor { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class DtDependencyCreateDto
{
    /// <summary>تسکی که باید اول تمام شود (مسدودکننده)</summary>
    public int DependsOnTaskId { get; set; }
    public string Kind { get; set; } = "Blocks";
}

public class DtTaskMoveDto
{
    public int StatusId { get; set; }
    /// <summary>اختیاری — قرارگیری قبل از این تسک در ستون مقصد (null = انتهای ستون)</summary>
    public int? BeforeTaskId { get; set; }
}

public class DtReorderSubTasksDto
{
    /// <summary>شناسه‌های فرزندان به ترتیب جدید (همهٔ خواهر/برادرهای همان والد)</summary>
    public List<int> OrderedIds { get; set; } = new();
}

public class DtTimerStateDto
{
    public int TaskId { get; set; }
    public string? TaskNumber { get; set; }
    public string? TaskTitle { get; set; }
    public bool Running { get; set; }
    public DateTime? StartedAt { get; set; }
    public int? StartedByUserId { get; set; }
    public string? StartedByName { get; set; }
    /// <summary>ثانیه‌های سپری‌شده از شروع (تقریبی)</summary>
    public int ElapsedSeconds { get; set; }
    public decimal SpentHours { get; set; }
    public DtTimeEntryDto? LastEntry { get; set; }
}

public class DtBurndownDto
{
    public int? SprintId { get; set; }
    public string? SprintName { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; }
    public decimal TotalEstimateHours { get; set; }
    public decimal TotalSpentHours { get; set; }
    public int TotalTasks { get; set; }
    public int DoneTasks { get; set; }
    public double ProgressPct { get; set; }
    /// <summary>نقاط روزانه burndown</summary>
    public List<DtBurndownPointDto> Points { get; set; } = new();
}

public class DtBurndownPointDto
{
    public DateTime Date { get; set; }
    /// <summary>ساعت باقی‌مانده ایده‌آل (خط مستقیم)</summary>
    public decimal IdealRemaining { get; set; }
    /// <summary>ساعت باقی‌مانده واقعی (تخمین تسک‌های ناتمام در آن روز)</summary>
    public decimal ActualRemaining { get; set; }
    public int DoneTasksCumulative { get; set; }
}

public class DtCiBuildEventDto
{
    /// <summary>github-actions | gitlab-ci | generic</summary>
    public string Provider { get; set; } = "generic";
    public string? PipelineId { get; set; }
    public string? JobName { get; set; }
    public string? Repo { get; set; }
    public string? Branch { get; set; }
    public string? CommitSha { get; set; }
    public string? CommitMessage { get; set; }
    public string? Url { get; set; }
    /// <summary>success | failure | cancelled</summary>
    public string Status { get; set; } = "failure";
    public string? Conclusion { get; set; }
    /// <summary>کلید ماژول اختیاری</summary>
    public string? ModuleKey { get; set; }
    /// <summary>شماره/شناسه تسک اختیاری</summary>
    public string? TaskRef { get; set; }
}

public class DtTaskCommentDto
{
    public int Id { get; set; }
    public int AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class DtTaskActivityDto
{
    public int Id { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string ActionFa { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DtTaskGitLinkDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public string KindFa { get; set; } = "";
    public string Ref { get; set; } = "";
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string AddedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class DtTaskGitLinkCreateDto
{
    public string Kind { get; set; } = "Commit";
    public string Ref { get; set; } = "";
    public string? Url { get; set; }
    public string? Title { get; set; }
}

public class DtTimeEntryDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public decimal Hours { get; set; }
    public DateTime WorkDate { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DtTimeEntryCreateDto
{
    public decimal Hours { get; set; }
    public DateTime? WorkDate { get; set; }
    public string? Note { get; set; }
}

public class DtProblemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Severity { get; set; } = "";
    public string SeverityFa { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusFa { get; set; } = "";
    public int? ModuleId { get; set; }
    public string? ModuleName { get; set; }
    public string? ModuleColor { get; set; }
    public int? TaskId { get; set; }
    public string? TaskNumber { get; set; }
    public string? TaskTitle { get; set; }
    public int? AssigneeUserId { get; set; }
    public string? AssigneeName { get; set; }
    public string ReporterName { get; set; } = "";
    public string? StackTrace { get; set; }
    public string? Environment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
}

public class DtProblemUpsertDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Severity { get; set; } = "Error";
    public string? Status { get; set; }
    public int? ModuleId { get; set; }
    public int? TaskId { get; set; }
    public int? AssigneeUserId { get; set; }
    public string? StackTrace { get; set; }
    public string? Environment { get; set; }
    public string? ResolutionNote { get; set; }
}

public class DtModuleChangeDto
{
    public int Id { get; set; }
    public int ModuleId { get; set; }
    public string ModuleName { get; set; } = "";
    public string ModuleKey { get; set; } = "";
    public string ModuleColor { get; set; } = "";
    public int? TaskId { get; set; }
    public string? TaskNumber { get; set; }
    public string Summary { get; set; } = "";
    public string? Details { get; set; }
    public string? CommitSha { get; set; }
    public string? Branch { get; set; }
    public string? CommitUrl { get; set; }
    public string AuthorName { get; set; } = "";
    public DateTime ChangedAt { get; set; }
}

public class DtModuleChangeCreateDto
{
    public int ModuleId { get; set; }
    public int? TaskId { get; set; }
    public string Summary { get; set; } = "";
    public string? Details { get; set; }
    public string? CommitSha { get; set; }
    public string? Branch { get; set; }
    public string? CommitUrl { get; set; }
    public DateTime? ChangedAt { get; set; }
}

public class DtUserLiteDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
}

public class DtLookupsDto
{
    public List<DtWorkflowStatusDto> Statuses { get; set; } = new();
    public List<DtProductModuleDto> Modules { get; set; } = new();
    public List<DtSprintDto> Sprints { get; set; } = new();
    public List<DtUserLiteDto> Users { get; set; } = new();
    public List<string> TaskTypes { get; set; } = new();
    public List<string> Severities { get; set; } = new();
    public List<string> ProblemStatuses { get; set; } = new();
    public List<string> GitLinkKinds { get; set; } = new();
}

public class DtDashboardDto
{
    public int TotalOpenTasks { get; set; }
    public int TotalDoneThisSprint { get; set; }
    public int OverdueTasks { get; set; }
    public int BlockedTasks { get; set; }
    public int OpenErrors { get; set; }
    public int OpenWarnings { get; set; }
    public decimal SprintEstimateHours { get; set; }
    public decimal SprintSpentHours { get; set; }
    public string? ActiveSprintName { get; set; }
    public int? ActiveSprintId { get; set; }
    public double? ActiveSprintProgressPct { get; set; }

    public List<DtMemberLoadDto> MemberLoads { get; set; } = new();
    public List<DtModuleLoadDto> ModuleLoads { get; set; } = new();
    public List<DtStatusCountDto> StatusCounts { get; set; } = new();
    public List<DtTaskListItemDto> RecentTasks { get; set; } = new();
    public List<DtProblemDto> RecentProblems { get; set; } = new();
    public List<DtModuleChangeDto> RecentChanges { get; set; } = new();

    /// <summary>خلاصه burndown اسپرینت فعال (در صورت وجود)</summary>
    public DtBurndownDto? Burndown { get; set; }
}

public class DtMemberLoadDto
{
    public int? UserId { get; set; }
    public string Name { get; set; } = "";
    public int OpenTasks { get; set; }
    public int DoingTasks { get; set; }
    public int OverdueTasks { get; set; }
    public decimal EstimateHours { get; set; }
    public decimal SpentHours { get; set; }
}

public class DtModuleLoadDto
{
    public int ModuleId { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public int OpenTasks { get; set; }
    public int OpenProblems { get; set; }
    public int ChangesLast7Days { get; set; }
}

public class DtStatusCountDto
{
    public int StatusId { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public int Count { get; set; }
}

public class DtTaskQuery
{
    public string? Q { get; set; }
    public int? StatusId { get; set; }
    public int? ModuleId { get; set; }
    public int? SprintId { get; set; }
    public int? AssigneeUserId { get; set; }
    public int? Priority { get; set; }
    public string? Type { get; set; }
    public bool? OnlyMine { get; set; }
    public bool? OnlyOverdue { get; set; }
    public bool? IncludeDone { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class DtPagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>درخواست ساخت دستور کار از روی تسک DevTeam.</summary>
public class DtCreateWorkOrderDto
{
    /// <summary>مهلت دستور — اگر خالی باشد از DueAt تسک یا +۲ روز استفاده می‌شود.</summary>
    public DateTime? DueAt { get; set; }

    /// <summary>گیرندگان اضافه علاوه بر مسئول تسک (اختیاری).</summary>
    public List<int>? ExtraAssigneeUserIds { get; set; }
}

public class DtWorkOrderLinkDto
{
    public int TaskId { get; set; }
    public int WorkOrderId { get; set; }
    public string WorkOrderNumber { get; set; } = "";
    public string Link { get; set; } = "";
}

/// <summary>نتیجه پردازش وب‌هوک گیت.</summary>
public class DtGitWebhookResultDto
{
    public bool Ok { get; set; }
    public string Provider { get; set; } = "";
    public int CommitsProcessed { get; set; }
    public int TasksLinked { get; set; }
    public int ChangesCreated { get; set; }
    public int ProblemsCreated { get; set; }
    public List<string> Messages { get; set; } = new();
}

/// <summary>وضعیت یکپارچه‌سازی گیت/CI برای تب تنظیمات (مسیر A).</summary>
public class DtIntegrationStatusDto
{
    public bool WebhookEnabled { get; set; }
    public bool SecretConfigured { get; set; }
    /// <summary>true اگر secret از env خوانده شود (نه appsettings خالی)</summary>
    public bool SecretFromEnvironment { get; set; }
    public string? PublicBaseUrl { get; set; }
    public List<DtWebhookEndpointInfoDto> Endpoints { get; set; } = new();
    public List<string> CommitHints { get; set; } = new();
    public List<string> SetupStepsFa { get; set; } = new();
    public DtRbacSummaryDto Rbac { get; set; } = new();
    /// <summary>نمونه curl تست CI (بدون secret واقعی)</summary>
    public string SampleCurlCi { get; set; } = "";
    public string SampleCommitMessage { get; set; } = "";
}

public class DtWebhookEndpointInfoDto
{
    public string Key { get; set; } = "";
    public string Method { get; set; } = "POST";
    public string Path { get; set; } = "";
    public string FullUrl { get; set; } = "";
    public string TitleFa { get; set; } = "";
    public string AuthHintFa { get; set; } = "";
    public string EventsHintFa { get; set; } = "";
}

public class DtRbacSummaryDto
{
    public bool DeveloperRoleExists { get; set; }
    public int DeveloperRoleUserCount { get; set; }
    public int DevTeamPermissionCount { get; set; }
    public bool AdminHasDevTeamPerms { get; set; }
    public string DeveloperRoleName { get; set; } = "DevDeveloper";
    public string HintFa { get; set; } = "";
}

public class DtChecklistItemDto
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string Title { get; set; } = "";
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? DoneAt { get; set; }
    public string? DoneByName { get; set; }
}

public class DtChecklistItemCreateDto
{
    public string Title { get; set; } = "";
}

public class DtChecklistReorderDto
{
    public List<int> OrderedIds { get; set; } = new();
}

public class DtCreateTaskFromProblemDto
{
    public int? StatusId { get; set; }
    public int? AssigneeUserId { get; set; }
    public int? SprintId { get; set; }
    public int Priority { get; set; } = 2;
}
