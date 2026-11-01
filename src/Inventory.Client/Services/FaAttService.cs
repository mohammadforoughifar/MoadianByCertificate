using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public interface IFaAttService
{
    Task<List<FaAttShiftDto>> GetShiftsAsync(bool? onlyActive = null);
    Task<FaAttShiftDto> SaveShiftAsync(int? id, FaAttShiftSaveDto dto);
    Task DeleteShiftAsync(int id);

    Task<List<FaAttShiftAssignDto>> GetAssignsAsync(int? employeeId = null, int? shiftId = null);
    Task<FaAttShiftPlanResultDto> PlanShiftsAsync(FaAttShiftPlanSaveDto dto);
    Task<FaAttShiftAssignDto> SaveAssignAsync(int? id, FaAttShiftAssignSaveDto dto);
    Task DeleteAssignAsync(int id);

    Task<List<FaAttDeviceDto>> GetDevicesAsync(bool? onlyActive = null);
    Task<FaAttDeviceDto> SaveDeviceAsync(int? id, FaAttDeviceSaveDto dto);
    Task DeleteDeviceAsync(int id);
    Task<FaAttImportResultDto> ImportLogsAsync(Stream stream, string fileName, string? contentType, int? deviceId);
    Task<(byte[] Data, string FileName, string ContentType)> GetImportTemplateAsync();

    Task<FaAttLogDto> ClockAsync(FaAttClockSaveDto dto);
    Task<FaAttLogDto> SaveLogAsync(FaAttLogSaveDto dto);
    Task DeleteLogAsync(int id);
    Task<List<FaAttLogDto>> GetEmployeeLogsAsync(int employeeId, DateTime from, DateTime to);

    Task<FaAttDailyDto> RecalcAsync(int employeeId, DateTime date);
    Task<int> RecalcRangeAsync(DateTime from, DateTime to, int? employeeId = null, int? orgUnitId = null);
    Task<List<FaAttDailyDto>> GetDailyAsync(DateTime from, DateTime to, int? employeeId = null, int? orgUnitId = null, int? status = null);
    Task<FaAttMyTodayDto> GetMyTodayAsync();

    Task<List<FaAttMissionDto>> GetMissionsAsync(int? employeeId = null, int? status = null);
    Task<FaAttMissionDto> SaveMissionAsync(int? id, FaAttMissionSaveDto dto);
    Task<FaAttMissionDto> DecideMissionAsync(int id, bool approve);
    Task<FaAttBatchResultDto> DecideMissionBatchAsync(List<int> ids, bool approve);
    Task DeleteMissionAsync(int id);
    Task<List<FaAttMissionDto>> MyMissionsAsync();
    Task<FaAttMissionDto> RequestMyMissionAsync(FaAttMissionSaveDto dto);
    Task<FaAttMissionDto> UpdateMyMissionAsync(int id, FaAttMissionSaveDto dto);

    Task<List<FaAttLeaveTypeDto>> GetLeaveTypesAsync();
    Task<FaAttLeaveTypeDto> SaveLeaveTypeAsync(int? id, FaAttLeaveTypeSaveDto dto);

    Task<List<FaAttLeaveDto>> GetLeavesAsync(int? employeeId = null, int? status = null);
    Task<FaAttLeaveDto> SaveLeaveAsync(int? id, FaAttLeaveSaveDto dto);
    Task<FaAttLeaveDto> RequestMyLeaveAsync(FaAttLeaveSaveDto dto);
    Task<FaAttLeaveDto> UpdateMyLeaveAsync(int id, FaAttLeaveSaveDto dto);
    Task<FaAttLeaveDto> ManagerDecideAsync(int id, bool approve);
    Task<FaAttLeaveDto> HrDecideAsync(int id, bool approve);
    Task<FaAttBatchResultDto> ManagerDecideBatchAsync(List<int> ids, bool approve);
    Task<FaAttBatchResultDto> HrDecideBatchAsync(List<int> ids, bool approve);
    Task<List<FaAttLeaveDto>> GetLeavesByUnitAsync(int orgUnitId, DateTime from, DateTime to);
    Task<List<FaAttCalendarEventDto>> GetAbsenceCalendarAsync(DateTime from, DateTime to, int? employeeId, int? orgUnitId);
    Task<List<FaAttLeaveBalanceDto>> GetBalancesAsync(int? employeeId = null, int? year = null, int? leaveTypeId = null);
    Task<FaAttLeaveBalanceDto> SaveBalanceAsync(int employeeId, int year, int leaveTypeId, FaAttLeaveBalanceSaveDto dto);
    Task<FaAttLeaveBalanceDto> CarryOverAsync(int employeeId, int leaveTypeId, FaAttLeaveCarryDto dto);
    Task<FaAttLeaveBalanceDto> CashOutAsync(int balanceId, FaAttLeaveCashDto dto);
    Task DeleteLeaveAsync(int id);
    Task<List<FaAttLeaveDto>> MyLeavesAsync();
    Task<List<FaAttLeaveDto>> TeamLeavesAsync();
    Task CancelMyLeaveAsync(int id);
    Task<List<FaAttLeaveBalanceDto>> MyBalancesAsync(int? year = null);
    Task<int> InitYearBalancesAsync(int year, int? leaveTypeId = null);

    Task<List<FaAttMonthSummaryDto>> GetMonthSummaryAsync(int year, int month, int? orgUnitId = null);
    Task<(byte[] Data, string FileName, string ContentType)> DownloadMonthExcelAsync(int year, int month, int? orgUnitId = null);
}

public class FaAttService : IFaAttService
{
    private readonly IApiClient _api;
    public FaAttService(IApiClient api) => _api = api;

    private const string Root = "api/fa-att";

    private static string F(DateTime d) => d.ToString("yyyy-MM-dd");

    public Task<List<FaAttShiftDto>> GetShiftsAsync(bool? onlyActive = null)
        => _api.GetAsync<List<FaAttShiftDto>>($"{Root}/shifts{(onlyActive == true ? "?onlyActive=true" : "")}");

    public Task<FaAttShiftDto> SaveShiftAsync(int? id, FaAttShiftSaveDto dto)
        => id is > 0
            ? _api.PutAsync<FaAttShiftDto>($"{Root}/shifts/{id}", dto)
            : _api.PostAsync<FaAttShiftDto>($"{Root}/shifts", dto);

    public Task DeleteShiftAsync(int id)
        => _api.DeleteAsync($"{Root}/shifts/{id}");

    public Task<FaAttShiftPlanResultDto> PlanShiftsAsync(FaAttShiftPlanSaveDto dto)
        => _api.PostAsync<FaAttShiftPlanResultDto>($"{Root}/assigns/plan", dto);

    public Task<List<FaAttShiftAssignDto>> GetAssignsAsync(int? employeeId = null, int? shiftId = null)
    {
        var qs = new List<string>();
        if (employeeId is > 0) qs.Add($"employeeId={employeeId}");
        if (shiftId is > 0) qs.Add($"shiftId={shiftId}");
        var s = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<FaAttShiftAssignDto>>($"{Root}/assigns{s}");
    }

    public Task<FaAttShiftAssignDto> SaveAssignAsync(int? id, FaAttShiftAssignSaveDto dto)
        => id is > 0
            ? _api.PutAsync<FaAttShiftAssignDto>($"{Root}/assigns/{id}", dto)
            : _api.PostAsync<FaAttShiftAssignDto>($"{Root}/assigns", dto);

    public Task DeleteAssignAsync(int id)
        => _api.DeleteAsync($"{Root}/assigns/{id}");

    public Task<List<FaAttDeviceDto>> GetDevicesAsync(bool? onlyActive = null)
        => _api.GetAsync<List<FaAttDeviceDto>>($"{Root}/devices{(onlyActive == true ? "?onlyActive=true" : "")}");

    public Task<FaAttDeviceDto> SaveDeviceAsync(int? id, FaAttDeviceSaveDto dto)
        => id is > 0
            ? _api.PutAsync<FaAttDeviceDto>($"{Root}/devices/{id}", dto)
            : _api.PostAsync<FaAttDeviceDto>($"{Root}/devices", dto);

    public Task DeleteDeviceAsync(int id)
        => _api.DeleteAsync($"{Root}/devices/{id}");

    public Task<FaAttImportResultDto> ImportLogsAsync(Stream stream, string fileName, string? contentType, int? deviceId)
        => _api.PostFileAsync<FaAttImportResultDto>($"{Root}/logs/import{(deviceId is > 0 ? $"?deviceId={deviceId}" : "")}", stream, fileName, "file", contentType);

    public Task<(byte[] Data, string FileName, string ContentType)> GetImportTemplateAsync()
        => _api.GetFileAsync($"{Root}/logs/import-template");

    public Task<FaAttLogDto> ClockAsync(FaAttClockSaveDto dto)
        => _api.PostAsync<FaAttLogDto>($"{Root}/clock", dto);

    public Task<FaAttLogDto> SaveLogAsync(FaAttLogSaveDto dto)
        => _api.PostAsync<FaAttLogDto>($"{Root}/logs", dto);

    public Task DeleteLogAsync(int id)
        => _api.DeleteAsync($"{Root}/logs/{id}");

    public Task<List<FaAttLogDto>> GetEmployeeLogsAsync(int employeeId, DateTime from, DateTime to)
        => _api.GetAsync<List<FaAttLogDto>>($"{Root}/employees/{employeeId}/logs?from={F(from)}&to={F(to)}");

    public async Task<FaAttDailyDto> RecalcAsync(int employeeId, DateTime date)
        => await _api.PostAsync<FaAttDailyDto>($"{Root}/daily/recalc?employeeId={employeeId}&date={F(date)}", null);

    public async Task<int> RecalcRangeAsync(DateTime from, DateTime to, int? employeeId = null, int? orgUnitId = null)
    {
        var qs = $"?from={F(from)}&to={F(to)}";
        if (employeeId is > 0) qs += $"&employeeId={employeeId}";
        if (orgUnitId is > 0) qs += $"&orgUnitId={orgUnitId}";
        var r = await _api.PostAsync<Dictionary<string, int>>($"{Root}/daily/recalc-range{qs}", null);
        return r.TryGetValue("count", out var c) ? c : 0;
    }

    public Task<List<FaAttDailyDto>> GetDailyAsync(DateTime from, DateTime to, int? employeeId = null, int? orgUnitId = null, int? status = null)
    {
        var qs = $"?from={F(from)}&to={F(to)}";
        if (employeeId is > 0) qs += $"&employeeId={employeeId}";
        if (orgUnitId is > 0) qs += $"&orgUnitId={orgUnitId}";
        if (status is >= 0) qs += $"&status={status}";
        return _api.GetAsync<List<FaAttDailyDto>>($"{Root}/daily{qs}");
    }

    public Task<FaAttMyTodayDto> GetMyTodayAsync()
        => _api.GetAsync<FaAttMyTodayDto>($"{Root}/my/today");

    public Task<List<FaAttMissionDto>> GetMissionsAsync(int? employeeId = null, int? status = null)
    {
        var qs = new List<string>();
        if (employeeId is > 0) qs.Add($"employeeId={employeeId}");
        if (status is >= 0) qs.Add($"status={status}");
        var s = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<FaAttMissionDto>>($"{Root}/missions{s}");
    }

    public Task<FaAttMissionDto> SaveMissionAsync(int? id, FaAttMissionSaveDto dto)
        => id is > 0
            ? _api.PutAsync<FaAttMissionDto>($"{Root}/missions/{id}", dto)
            : _api.PostAsync<FaAttMissionDto>($"{Root}/missions", dto);

    public Task<FaAttMissionDto> DecideMissionAsync(int id, bool approve)
        => _api.PostAsync<FaAttMissionDto>($"{Root}/missions/{id}/decide?approve={approve}", null);

    public Task<FaAttBatchResultDto> DecideMissionBatchAsync(List<int> ids, bool approve)
        => _api.PostAsync<FaAttBatchResultDto>($"{Root}/missions/decide-batch", new FaAttDecideBatchDto { Ids = ids, Approve = approve });

    public Task DeleteMissionAsync(int id)
        => _api.DeleteAsync($"{Root}/missions/{id}");

    public Task<List<FaAttMissionDto>> MyMissionsAsync()
        => _api.GetAsync<List<FaAttMissionDto>>($"{Root}/missions/my");

    public Task<FaAttMissionDto> RequestMyMissionAsync(FaAttMissionSaveDto dto)
        => _api.PostAsync<FaAttMissionDto>($"{Root}/missions/my", dto);

    public Task<FaAttMissionDto> UpdateMyMissionAsync(int id, FaAttMissionSaveDto dto)
        => _api.PutAsync<FaAttMissionDto>($"{Root}/missions/my/{id}", dto);

    public Task<List<FaAttLeaveTypeDto>> GetLeaveTypesAsync()
        => _api.GetAsync<List<FaAttLeaveTypeDto>>($"{Root}/leavetypes");

    public Task<FaAttLeaveTypeDto> SaveLeaveTypeAsync(int? id, FaAttLeaveTypeSaveDto dto)
        => id is > 0
            ? _api.PutAsync<FaAttLeaveTypeDto>($"{Root}/leavetypes/{id}", dto)
            : _api.PostAsync<FaAttLeaveTypeDto>($"{Root}/leavetypes", dto);

    public Task<List<FaAttLeaveDto>> GetLeavesAsync(int? employeeId = null, int? status = null)
    {
        var qs = new List<string>();
        if (employeeId is > 0) qs.Add($"employeeId={employeeId}");
        if (status is >= 0) qs.Add($"status={status}");
        var s = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<FaAttLeaveDto>>($"{Root}/leaves{s}");
    }

    public Task<FaAttLeaveDto> SaveLeaveAsync(int? id, FaAttLeaveSaveDto dto)
        => id is > 0
            ? _api.PutAsync<FaAttLeaveDto>($"{Root}/leaves/{id}", dto)
            : _api.PostAsync<FaAttLeaveDto>($"{Root}/leaves", dto);

    public Task<FaAttLeaveDto> RequestMyLeaveAsync(FaAttLeaveSaveDto dto)
        => _api.PostAsync<FaAttLeaveDto>($"{Root}/leaves/my", dto);

    public Task<FaAttLeaveDto> UpdateMyLeaveAsync(int id, FaAttLeaveSaveDto dto)
        => _api.PutAsync<FaAttLeaveDto>($"{Root}/leaves/my/{id}", dto);

    public Task<FaAttLeaveDto> ManagerDecideAsync(int id, bool approve)
        => _api.PostAsync<FaAttLeaveDto>($"{Root}/leaves/{id}/manager-decide?approve={approve}", null);

    public Task<FaAttLeaveDto> HrDecideAsync(int id, bool approve)
        => _api.PostAsync<FaAttLeaveDto>($"{Root}/leaves/{id}/hr-decide?approve={approve}", null);

    public Task<FaAttBatchResultDto> ManagerDecideBatchAsync(List<int> ids, bool approve)
        => _api.PostAsync<FaAttBatchResultDto>($"{Root}/leaves/manager-decide-batch", new FaAttDecideBatchDto { Ids = ids, Approve = approve });

    public Task<FaAttBatchResultDto> HrDecideBatchAsync(List<int> ids, bool approve)
        => _api.PostAsync<FaAttBatchResultDto>($"{Root}/leaves/hr-decide-batch", new FaAttDecideBatchDto { Ids = ids, Approve = approve });

    public Task<List<FaAttLeaveDto>> GetLeavesByUnitAsync(int orgUnitId, DateTime from, DateTime to)
        => _api.GetAsync<List<FaAttLeaveDto>>($"{Root}/leaves/by-unit?orgUnitId={orgUnitId}&from={F(from)}&to={F(to)}");

    public Task<List<FaAttCalendarEventDto>> GetAbsenceCalendarAsync(DateTime from, DateTime to, int? employeeId, int? orgUnitId)
        => _api.GetAsync<List<FaAttCalendarEventDto>>($"{Root}/absence-calendar?from={F(from)}&to={F(to)}&employeeId={employeeId}&orgUnitId={orgUnitId}");

    public Task<List<FaAttLeaveBalanceDto>> GetBalancesAsync(int? employeeId = null, int? year = null, int? leaveTypeId = null)
    {
        var qs = new List<string>();
        if (employeeId is > 0) qs.Add($"employeeId={employeeId}");
        if (year is > 1000) qs.Add($"year={year}");
        if (leaveTypeId is > 0) qs.Add($"leaveTypeId={leaveTypeId}");
        var s = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
        return _api.GetAsync<List<FaAttLeaveBalanceDto>>($"{Root}/balances{s}");
    }

    public Task<FaAttLeaveBalanceDto> SaveBalanceAsync(int employeeId, int year, int leaveTypeId, FaAttLeaveBalanceSaveDto dto)
        => _api.PostAsync<FaAttLeaveBalanceDto>($"{Root}/balances?employeeId={employeeId}&year={year}&leaveTypeId={leaveTypeId}", dto);

    public Task<FaAttLeaveBalanceDto> CarryOverAsync(int employeeId, int leaveTypeId, FaAttLeaveCarryDto dto)
        => _api.PostAsync<FaAttLeaveBalanceDto>($"{Root}/balances/carry?employeeId={employeeId}&leaveTypeId={leaveTypeId}", dto);

    public Task<FaAttLeaveBalanceDto> CashOutAsync(int balanceId, FaAttLeaveCashDto dto)
        => _api.PostAsync<FaAttLeaveBalanceDto>($"{Root}/balances/{balanceId}/cash", dto);

    public Task DeleteLeaveAsync(int id)
        => _api.DeleteAsync($"{Root}/leaves/{id}");

    public Task<List<FaAttLeaveDto>> MyLeavesAsync()
        => _api.GetAsync<List<FaAttLeaveDto>>($"{Root}/leaves/my");

    public Task<List<FaAttLeaveDto>> TeamLeavesAsync()
        => _api.GetAsync<List<FaAttLeaveDto>>($"{Root}/leaves/team");

    public Task CancelMyLeaveAsync(int id)
        => _api.PostAsync<object>($"{Root}/leaves/{id}/cancel-my");

    public Task<List<FaAttLeaveBalanceDto>> MyBalancesAsync(int? year = null)
        => _api.GetAsync<List<FaAttLeaveBalanceDto>>($"{Root}/balances/my{(year is > 1000 ? $"?year={year}" : "")}");

    public Task<int> InitYearBalancesAsync(int year, int? leaveTypeId = null)
        => _api.PostAsync<int>($"{Root}/balances/init-year", new FaAttInitYearDto { Year = year, LeaveTypeId = leaveTypeId });

    public Task<List<FaAttMonthSummaryDto>> GetMonthSummaryAsync(int year, int month, int? orgUnitId = null)
    {
        var qs = $"?year={year}&month={month}";
        if (orgUnitId is > 0) qs += $"&orgUnitId={orgUnitId}";
        return _api.GetAsync<List<FaAttMonthSummaryDto>>($"{Root}/reports/month{qs}");
    }

    public Task<(byte[] Data, string FileName, string ContentType)> DownloadMonthExcelAsync(int year, int month, int? orgUnitId = null)
    {
        var qs = $"?year={year}&month={month}";
        if (orgUnitId is > 0) qs += $"&orgUnitId={orgUnitId}";
        return _api.GetFileAsync($"{Root}/reports/month-excel{qs}");
    }
}
