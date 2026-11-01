using System.Net.Http.Json;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IDevTeamClient
{
    Task<DtAccessDto> GetAccessAsync();
    Task<DtIntegrationStatusDto> GetIntegrationStatusAsync();
    Task<DtLookupsDto> GetLookupsAsync();
    Task<DtDashboardDto> GetDashboardAsync(int? sprintId = null);

    Task<List<DtWorkflowStatusDto>> GetStatusesAsync(bool all = false);
    Task<DtWorkflowStatusDto> UpsertStatusAsync(DtWorkflowStatusDto dto);
    Task DeleteStatusAsync(int id);

    Task<List<DtProductModuleDto>> GetModulesAsync(bool all = false);
    Task<DtProductModuleDto> UpsertModuleAsync(DtProductModuleDto dto);
    Task DeleteModuleAsync(int id);

    Task<List<DtSprintDto>> GetSprintsAsync();
    Task<DtSprintDto> UpsertSprintAsync(DtSprintDto dto);
    Task DeleteSprintAsync(int id);

    Task<DtPagedResult<DtTaskListItemDto>> QueryTasksAsync(DtTaskQuery q);
    Task<List<DtTaskListItemDto>> BoardAsync(int? sprintId = null, int? moduleId = null, int? assigneeUserId = null);
    Task<DtTaskDetailDto?> GetTaskAsync(int id);
    Task<DtTaskDetailDto> CreateTaskAsync(DtTaskUpsertDto dto);
    Task<DtTaskDetailDto> UpdateTaskAsync(int id, DtTaskUpsertDto dto);
    Task MoveTaskAsync(int id, int statusId, int? beforeTaskId = null);
    Task DeleteTaskAsync(int id);
    Task<DtTaskCommentDto> AddCommentAsync(int taskId, string text);
    Task<DtTaskGitLinkDto> AddGitLinkAsync(int taskId, DtTaskGitLinkCreateDto dto);
    Task DeleteGitLinkAsync(int linkId);
    Task<DtTimeEntryDto> AddTimeAsync(int taskId, DtTimeEntryCreateDto dto);

    Task ReorderSubTasksAsync(int parentId, IReadOnlyList<int> orderedIds);
    Task<DtTimerStateDto> StartTimerAsync(int taskId);
    Task<DtTimerStateDto> StopTimerAsync(int taskId, string? note = null);
    Task<DtTimerStateDto?> GetTimerAsync(int taskId);
    Task<DtTimerStateDto?> GetMyTimerAsync();
    Task<DtBurndownDto> GetBurndownAsync(int? sprintId = null);

    Task<List<DtProblemDto>> QueryProblemsAsync(string? severity = null, string? status = null, int? moduleId = null, int? taskId = null);
    Task<DtProblemDto> CreateProblemAsync(DtProblemUpsertDto dto);
    Task<DtProblemDto> UpdateProblemAsync(int id, DtProblemUpsertDto dto);
    Task DeleteProblemAsync(int id);

    Task<List<DtModuleChangeDto>> QueryChangesAsync(int? moduleId = null, int take = 50);
    Task<DtModuleChangeDto> CreateChangeAsync(DtModuleChangeCreateDto dto);
    Task DeleteChangeAsync(int id);

    Task<DtWorkOrderLinkDto> CreateWorkOrderFromTaskAsync(int taskId, DtCreateWorkOrderDto? dto = null);
    Task<DtWorkOrderLinkDto> LinkWorkOrderAsync(int taskId, int workOrderId);
    Task UnlinkWorkOrderAsync(int taskId);

    Task<List<DtTaskListItemDto>> SearchTasksAsync(string? q, int? excludeId = null, int take = 20);
    Task<DtTaskDetailDto> CreateSubTaskAsync(int parentId, DtTaskUpsertDto dto);
    Task MakeSubTaskAsync(int taskId, int parentTaskId);
    Task PromoteAsync(int taskId);
    Task<DtDependencyDto> AddDependencyAsync(int taskId, DtDependencyCreateDto dto);
    Task RemoveDependencyAsync(int dependencyId);

    Task<DtChecklistItemDto> AddChecklistItemAsync(int taskId, string title);
    Task<DtChecklistItemDto> ToggleChecklistItemAsync(int itemId);
    Task DeleteChecklistItemAsync(int itemId);
    Task ReorderChecklistAsync(int taskId, IReadOnlyList<int> orderedIds);
    Task<DtTaskDetailDto> CreateTaskFromProblemAsync(int problemId, DtCreateTaskFromProblemDto? dto = null);
}

public class DevTeamClient : IDevTeamClient
{
    private readonly HttpClient _http;
    private const string Base = "api/dev-team";

    public DevTeamClient(HttpClient http) => _http = http;

    private static async Task EnsureOk(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync();
        string msg = ApiClient.DescribeBody(body);
        try
        {
            var err = await res.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            if (err != null && err.TryGetValue("message", out var m)) msg = m?.ToString() ?? msg;
        }
        catch { /* keep summarized raw */ }
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? res.ReasonPhrase : msg);
    }

    public async Task<DtAccessDto> GetAccessAsync() =>
        await _http.GetFromJsonAsync<DtAccessDto>($"{Base}/my-access") ?? new();

    public async Task<DtIntegrationStatusDto> GetIntegrationStatusAsync() =>
        await _http.GetFromJsonAsync<DtIntegrationStatusDto>($"{Base}/integration-status") ?? new();

    public async Task<DtLookupsDto> GetLookupsAsync() =>
        await _http.GetFromJsonAsync<DtLookupsDto>($"{Base}/lookups") ?? new();

    public async Task<DtDashboardDto> GetDashboardAsync(int? sprintId = null)
    {
        var url = sprintId is int s ? $"{Base}/dashboard?sprintId={s}" : $"{Base}/dashboard";
        return await _http.GetFromJsonAsync<DtDashboardDto>(url) ?? new();
    }

    public async Task<List<DtWorkflowStatusDto>> GetStatusesAsync(bool all = false) =>
        await _http.GetFromJsonAsync<List<DtWorkflowStatusDto>>($"{Base}/statuses?all={all}") ?? new();

    public async Task<DtWorkflowStatusDto> UpsertStatusAsync(DtWorkflowStatusDto dto)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/statuses", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtWorkflowStatusDto>())!;
    }

    public async Task DeleteStatusAsync(int id)
    {
        var res = await _http.DeleteAsync($"{Base}/statuses/{id}");
        await EnsureOk(res);
    }

    public async Task<List<DtProductModuleDto>> GetModulesAsync(bool all = false) =>
        await _http.GetFromJsonAsync<List<DtProductModuleDto>>($"{Base}/modules?all={all}") ?? new();

    public async Task<DtProductModuleDto> UpsertModuleAsync(DtProductModuleDto dto)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/modules", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtProductModuleDto>())!;
    }

    public async Task DeleteModuleAsync(int id)
    {
        var res = await _http.DeleteAsync($"{Base}/modules/{id}");
        await EnsureOk(res);
    }

    public async Task<List<DtSprintDto>> GetSprintsAsync() =>
        await _http.GetFromJsonAsync<List<DtSprintDto>>($"{Base}/sprints") ?? new();

    public async Task<DtSprintDto> UpsertSprintAsync(DtSprintDto dto)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/sprints", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtSprintDto>())!;
    }

    public async Task DeleteSprintAsync(int id)
    {
        var res = await _http.DeleteAsync($"{Base}/sprints/{id}");
        await EnsureOk(res);
    }

    public async Task<DtPagedResult<DtTaskListItemDto>> QueryTasksAsync(DtTaskQuery q)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(q.Q)) qs.Add($"q={Uri.EscapeDataString(q.Q)}");
        if (q.StatusId is int sid) qs.Add($"statusId={sid}");
        if (q.ModuleId is int mid) qs.Add($"moduleId={mid}");
        if (q.SprintId is int sp) qs.Add($"sprintId={sp}");
        if (q.AssigneeUserId is int a) qs.Add($"assigneeUserId={a}");
        if (q.Priority is int p) qs.Add($"priority={p}");
        if (!string.IsNullOrWhiteSpace(q.Type)) qs.Add($"type={Uri.EscapeDataString(q.Type)}");
        if (q.OnlyMine is bool om) qs.Add($"onlyMine={om}");
        if (q.OnlyOverdue is bool oo) qs.Add($"onlyOverdue={oo}");
        if (q.IncludeDone is bool idn) qs.Add($"includeDone={idn}");
        qs.Add($"page={q.Page}");
        qs.Add($"pageSize={q.PageSize}");
        var url = $"{Base}/tasks?{string.Join("&", qs)}";
        return await _http.GetFromJsonAsync<DtPagedResult<DtTaskListItemDto>>(url) ?? new();
    }

    public async Task<List<DtTaskListItemDto>> BoardAsync(int? sprintId = null, int? moduleId = null, int? assigneeUserId = null)
    {
        var qs = new List<string>();
        if (sprintId is int s) qs.Add($"sprintId={s}");
        if (moduleId is int m) qs.Add($"moduleId={m}");
        if (assigneeUserId is int a) qs.Add($"assigneeUserId={a}");
        var url = qs.Count == 0 ? $"{Base}/board" : $"{Base}/board?{string.Join("&", qs)}";
        return await _http.GetFromJsonAsync<List<DtTaskListItemDto>>(url) ?? new();
    }

    public async Task<DtTaskDetailDto?> GetTaskAsync(int id) =>
        await _http.GetFromJsonAsync<DtTaskDetailDto>($"{Base}/tasks/{id}");

    public async Task<DtTaskDetailDto> CreateTaskAsync(DtTaskUpsertDto dto)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtTaskDetailDto>())!;
    }

    public async Task<DtTaskDetailDto> UpdateTaskAsync(int id, DtTaskUpsertDto dto)
    {
        var res = await _http.PutAsJsonAsync($"{Base}/tasks/{id}", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtTaskDetailDto>())!;
    }

    public async Task MoveTaskAsync(int id, int statusId, int? beforeTaskId = null)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{id}/move",
            new DtTaskMoveDto { StatusId = statusId, BeforeTaskId = beforeTaskId });
        await EnsureOk(res);
    }

    public async Task ReorderSubTasksAsync(int parentId, IReadOnlyList<int> orderedIds)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{parentId}/subtasks/reorder",
            new DtReorderSubTasksDto { OrderedIds = orderedIds.ToList() });
        await EnsureOk(res);
    }

    public async Task<DtTimerStateDto> StartTimerAsync(int taskId)
    {
        var res = await _http.PostAsync($"{Base}/tasks/{taskId}/timer/start", null);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtTimerStateDto>())!;
    }

    public async Task<DtTimerStateDto> StopTimerAsync(int taskId, string? note = null)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{taskId}/timer/stop",
            new DtTimeEntryCreateDto { Hours = 0, Note = note });
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtTimerStateDto>())!;
    }

    public async Task<DtTimerStateDto?> GetTimerAsync(int taskId) =>
        await _http.GetFromJsonAsync<DtTimerStateDto>($"{Base}/tasks/{taskId}/timer");

    public async Task<DtTimerStateDto?> GetMyTimerAsync()
    {
        var s = await _http.GetFromJsonAsync<DtTimerStateDto>($"{Base}/my-timer");
        if (s is null || !s.Running || s.TaskId <= 0) return null;
        return s;
    }

    public async Task<DtBurndownDto> GetBurndownAsync(int? sprintId = null)
    {
        var url = sprintId is int s ? $"{Base}/burndown?sprintId={s}" : $"{Base}/burndown";
        return (await _http.GetFromJsonAsync<DtBurndownDto>(url))!;
    }

    public async Task DeleteTaskAsync(int id)
    {
        var res = await _http.DeleteAsync($"{Base}/tasks/{id}");
        await EnsureOk(res);
    }

    public async Task<DtTaskCommentDto> AddCommentAsync(int taskId, string text)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{taskId}/comments", new DtTaskCommentDto { Text = text });
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtTaskCommentDto>())!;
    }

    public async Task<DtTaskGitLinkDto> AddGitLinkAsync(int taskId, DtTaskGitLinkCreateDto dto)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{taskId}/git-links", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtTaskGitLinkDto>())!;
    }

    public async Task DeleteGitLinkAsync(int linkId)
    {
        var res = await _http.DeleteAsync($"{Base}/git-links/{linkId}");
        await EnsureOk(res);
    }

    public async Task<DtTimeEntryDto> AddTimeAsync(int taskId, DtTimeEntryCreateDto dto)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{taskId}/time", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtTimeEntryDto>())!;
    }

    public async Task<List<DtProblemDto>> QueryProblemsAsync(string? severity = null, string? status = null, int? moduleId = null, int? taskId = null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(severity)) qs.Add($"severity={Uri.EscapeDataString(severity)}");
        if (!string.IsNullOrWhiteSpace(status)) qs.Add($"status={Uri.EscapeDataString(status)}");
        if (moduleId is int m) qs.Add($"moduleId={m}");
        if (taskId is int t) qs.Add($"taskId={t}");
        var url = qs.Count == 0 ? $"{Base}/problems" : $"{Base}/problems?{string.Join("&", qs)}";
        return await _http.GetFromJsonAsync<List<DtProblemDto>>(url) ?? new();
    }

    public async Task<DtProblemDto> CreateProblemAsync(DtProblemUpsertDto dto)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/problems", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtProblemDto>())!;
    }

    public async Task<DtProblemDto> UpdateProblemAsync(int id, DtProblemUpsertDto dto)
    {
        var res = await _http.PutAsJsonAsync($"{Base}/problems/{id}", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtProblemDto>())!;
    }

    public async Task DeleteProblemAsync(int id)
    {
        var res = await _http.DeleteAsync($"{Base}/problems/{id}");
        await EnsureOk(res);
    }

    public async Task<List<DtModuleChangeDto>> QueryChangesAsync(int? moduleId = null, int take = 50)
    {
        var qs = new List<string> { $"take={take}" };
        if (moduleId is int m) qs.Add($"moduleId={m}");
        return await _http.GetFromJsonAsync<List<DtModuleChangeDto>>($"{Base}/changes?{string.Join("&", qs)}") ?? new();
    }

    public async Task<DtModuleChangeDto> CreateChangeAsync(DtModuleChangeCreateDto dto)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/changes", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtModuleChangeDto>())!;
    }

    public async Task DeleteChangeAsync(int id)
    {
        var res = await _http.DeleteAsync($"{Base}/changes/{id}");
        await EnsureOk(res);
    }

    public async Task<DtWorkOrderLinkDto> CreateWorkOrderFromTaskAsync(int taskId, DtCreateWorkOrderDto? dto = null)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{taskId}/work-order", dto ?? new DtCreateWorkOrderDto());
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtWorkOrderLinkDto>())!;
    }

    public async Task<DtWorkOrderLinkDto> LinkWorkOrderAsync(int taskId, int workOrderId)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{taskId}/work-order/link",
            new DtWorkOrderLinkDto { WorkOrderId = workOrderId });
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtWorkOrderLinkDto>())!;
    }

    public async Task UnlinkWorkOrderAsync(int taskId)
    {
        var res = await _http.DeleteAsync($"{Base}/tasks/{taskId}/work-order");
        await EnsureOk(res);
    }

    public async Task<List<DtTaskListItemDto>> SearchTasksAsync(string? q, int? excludeId = null, int take = 20)
    {
        var qs = new List<string> { $"take={take}" };
        if (!string.IsNullOrWhiteSpace(q)) qs.Add($"q={Uri.EscapeDataString(q)}");
        if (excludeId is int e) qs.Add($"excludeId={e}");
        return await _http.GetFromJsonAsync<List<DtTaskListItemDto>>($"{Base}/tasks/search?{string.Join("&", qs)}") ?? new();
    }

    public async Task<DtTaskDetailDto> CreateSubTaskAsync(int parentId, DtTaskUpsertDto dto)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{parentId}/subtasks", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtTaskDetailDto>())!;
    }

    public async Task MakeSubTaskAsync(int taskId, int parentTaskId)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{taskId}/make-subtask", new { parentTaskId });
        await EnsureOk(res);
    }

    public async Task PromoteAsync(int taskId)
    {
        var res = await _http.PostAsync($"{Base}/tasks/{taskId}/promote", null);
        await EnsureOk(res);
    }

    public async Task<DtDependencyDto> AddDependencyAsync(int taskId, DtDependencyCreateDto dto)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{taskId}/dependencies", dto);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtDependencyDto>())!;
    }

    public async Task RemoveDependencyAsync(int dependencyId)
    {
        var res = await _http.DeleteAsync($"{Base}/dependencies/{dependencyId}");
        await EnsureOk(res);
    }

    public async Task<DtChecklistItemDto> AddChecklistItemAsync(int taskId, string title)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{taskId}/checklist", new DtChecklistItemCreateDto { Title = title });
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtChecklistItemDto>())!;
    }

    public async Task<DtChecklistItemDto> ToggleChecklistItemAsync(int itemId)
    {
        var res = await _http.PostAsync($"{Base}/checklist/{itemId}/toggle", null);
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtChecklistItemDto>())!;
    }

    public async Task DeleteChecklistItemAsync(int itemId)
    {
        var res = await _http.DeleteAsync($"{Base}/checklist/{itemId}");
        await EnsureOk(res);
    }

    public async Task ReorderChecklistAsync(int taskId, IReadOnlyList<int> orderedIds)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/tasks/{taskId}/checklist/reorder",
            new DtChecklistReorderDto { OrderedIds = orderedIds.ToList() });
        await EnsureOk(res);
    }

    public async Task<DtTaskDetailDto> CreateTaskFromProblemAsync(int problemId, DtCreateTaskFromProblemDto? dto = null)
    {
        var res = await _http.PostAsJsonAsync($"{Base}/problems/{problemId}/create-task", dto ?? new DtCreateTaskFromProblemDto());
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<DtTaskDetailDto>())!;
    }
}
