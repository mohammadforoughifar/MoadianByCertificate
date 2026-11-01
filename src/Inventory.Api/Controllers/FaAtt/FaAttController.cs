using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.FaAtt;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.FaAtt;

/// <summary>
/// ================== حضور و غیاب فروغ آریا (ماژول جدید و مستقل) ==================
/// شیفت‌ها، تخصیص شیفت، دستگاه‌ها، تردد چندمسیره، محاسبه روزانه، مأموریت، مرخصی، گزارش‌ها.
/// مجوزها: FaAtt.Read / Create / Update / Delete / Manage
/// </summary>
[Route("api/fa-att")]
public class FaAttController : RbacControllerBase
{
    private const string Mod = "FaAtt";
    private readonly IFaAttService _svc;

    public FaAttController(AppDbContext db, IFaAttService svc) : base(db) => _svc = svc;

    // ------------------- شیفت‌ها -------------------

    [HttpGet("shifts")]
    public async Task<IActionResult> Shifts([FromQuery] bool? onlyActive, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListShiftsAsync(onlyActive, pagination: pagination), skip, take));
    }

    [HttpPost("shifts")]
    public async Task<IActionResult> CreateShift([FromBody] FaAttShiftSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveShiftAsync(null, dto));
    }

    [HttpPut("shifts/{id:int}")]
    public async Task<IActionResult> UpdateShift(int id, [FromBody] FaAttShiftSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveShiftAsync(id, dto));
    }

    [HttpDelete("shifts/{id:int}")]
    public async Task<IActionResult> DeleteShift(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteShiftAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- تخصیص شیفت -------------------

    [HttpGet("assigns")]
    public async Task<IActionResult> Assigns([FromQuery] int? employeeId, [FromQuery] int? shiftId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListAssignsAsync(employeeId, shiftId, pagination: pagination), skip, take));
    }

    [HttpPost("assigns")]
    public async Task<IActionResult> CreateAssign([FromBody] FaAttShiftAssignSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveAssignAsync(null, dto));
    }

    [HttpPut("assigns/{id:int}")]
    public async Task<IActionResult> UpdateAssign(int id, [FromBody] FaAttShiftAssignSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveAssignAsync(id, dto));
    }

    [HttpDelete("assigns/{id:int}")]
    public async Task<IActionResult> DeleteAssign(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteAssignAsync(id);
        return Ok(new { ok = true });
    }

    /// <summary>برنامه‌ریزی روزانه‌ی شیفت (شبکه‌ی گرافیکی هفتگی/ماهانه) — اعمال گروهی خانه‌های انتخاب‌شده</summary>
    [HttpPost("assigns/plan")]
    public async Task<IActionResult> PlanShifts([FromBody] FaAttShiftPlanSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Create", "Update") is { } f) return f;
        return Ok(await _svc.PlanShiftsAsync(dto));
    }

    // ------------------- دستگاه‌ها -------------------

    [HttpGet("devices")]
    public async Task<IActionResult> Devices([FromQuery] bool? onlyActive, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListDevicesAsync(onlyActive, pagination: pagination), skip, take));
    }

    [HttpPost("devices")]
    public async Task<IActionResult> CreateDevice([FromBody] FaAttDeviceSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveDeviceAsync(null, dto));
    }

    [HttpPut("devices/{id:int}")]
    public async Task<IActionResult> UpdateDevice(int id, [FromBody] FaAttDeviceSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveDeviceAsync(id, dto));
    }

    [HttpDelete("devices/{id:int}")]
    public async Task<IActionResult> DeleteDevice(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteDeviceAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- تردد -------------------

    [HttpPost("clock")]
    public async Task<IActionResult> Clock([FromBody] FaAttClockSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.ClockAsync(MyUserId, MyUsername, dto));
    }

    [HttpPost("logs")]
    public async Task<IActionResult> CreateLog([FromBody] FaAttLogSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveLogAsync(dto, MyUserId, MyUsername));
    }

    [HttpDelete("logs/{id:int}")]
    public async Task<IActionResult> DeleteLog(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteLogAsync(id);
        return Ok(new { ok = true });
    }

    [HttpGet("employees/{id:int}/logs")]
    public async Task<IActionResult> EmployeeLogs(int id, [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.EmployeeLogsAsync(id, from, to, pagination: pagination), skip, take));
    }

    [HttpGet("my/today")]
    public async Task<IActionResult> MyToday()
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.MyTodayAsync(MyUserId));
    }

    // ------------------- وضعیت روزانه -------------------

    [HttpGet("daily")]
    public async Task<IActionResult> Daily([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] int? employeeId, [FromQuery] int? orgUnitId, [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.DailyListAsync(from, to, employeeId, orgUnitId, status, pagination: pagination), skip, take));
    }

    [HttpPost("daily/recalc")]
    public async Task<IActionResult> Recalc([FromQuery] int employeeId, [FromQuery] DateTime date)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.RecalcAsync(employeeId, date));
    }

    [HttpPost("daily/recalc-range")]
    public async Task<IActionResult> RecalcRange([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] int? employeeId, [FromQuery] int? orgUnitId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(new { count = await _svc.RecalcRangeAsync(from, to, employeeId, orgUnitId) });
    }

    [HttpPost("logs/import")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> ImportLogs(IFormFile? file, [FromQuery] int? deviceId)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Create", "Daily") is { } f) return f;
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "فایلی ارسال نشد." });
        await using var stream = file.OpenReadStream();
        try { return Ok(await _svc.ImportLogsAsync(stream, file.FileName, deviceId, MyUserId, MyUsername)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("logs/import-template")]
    public async Task<IActionResult> ImportTemplate()
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Daily") is { } f) return f;
        var t = await _svc.ImportTemplateAsync();
        return File(t.Data, t.ContentType, t.FileName);
    }

    // ------------------- ماموریت -------------------

    [HttpGet("missions/my")]
    public async Task<IActionResult> MyMissions([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.MyMissionsAsync(MyUserId, pagination: pagination), skip, take));
    }

    [HttpPost("missions/my")]
    public async Task<IActionResult> RequestMyMission([FromBody] FaAttMissionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.RequestMyMissionAsync(MyUserId, dto));
    }

    [HttpPut("missions/my/{id:int}")]
    public async Task<IActionResult> UpdateMyMission(int id, [FromBody] FaAttMissionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.UpdateMyMissionAsync(id, MyUserId, dto));
    }

    [HttpGet("missions")]
    public async Task<IActionResult> Missions([FromQuery] int? employeeId, [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListMissionsAsync(employeeId, status, pagination: pagination), skip, take));
    }

    [HttpPost("missions")]
    public async Task<IActionResult> CreateMission([FromBody] FaAttMissionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveMissionAsync(null, dto));
    }

    [HttpPut("missions/{id:int}")]
    public async Task<IActionResult> UpdateMission(int id, [FromBody] FaAttMissionSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveMissionAsync(id, dto));
    }

    [HttpPost("missions/{id:int}/decide")]
    public async Task<IActionResult> DecideMission(int id, [FromQuery] bool approve)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.DecideMissionAsync(id, approve, MyUserId, MyUsername));
    }

    [HttpPost("missions/decide-batch")]
    public async Task<IActionResult> DecideMissionBatch([FromBody] FaAttDecideBatchDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.DecideMissionBatchAsync(dto.Ids, dto.Approve, MyUserId, MyUsername));
    }

    [HttpDelete("missions/{id:int}")]
    public async Task<IActionResult> DeleteMission(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteMissionAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- مرخصی -------------------

    [HttpGet("leavetypes")]
    public async Task<IActionResult> LeaveTypes([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListLeaveTypesAsync(pagination: pagination), skip, take));
    }

    [HttpPost("leavetypes")]
    public async Task<IActionResult> CreateLeaveType([FromBody] FaAttLeaveTypeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveLeaveTypeAsync(null, dto));
    }

    [HttpPut("leavetypes/{id:int}")]
    public async Task<IActionResult> UpdateLeaveType(int id, [FromBody] FaAttLeaveTypeSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveLeaveTypeAsync(id, dto));
    }

    [HttpGet("leaves")]
    public async Task<IActionResult> Leaves([FromQuery] int? employeeId, [FromQuery] int? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListLeavesAsync(employeeId, status, pagination: pagination), skip, take));
    }

    [HttpPost("leaves")]
    public async Task<IActionResult> CreateLeave([FromBody] FaAttLeaveSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.SaveLeaveAsync(null, dto));
    }

    [HttpPut("leaves/{id:int}")]
    public async Task<IActionResult> UpdateLeave(int id, [FromBody] FaAttLeaveSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Update") is { } f) return f;
        return Ok(await _svc.SaveLeaveAsync(id, dto));
    }

    [HttpGet("leaves/my")]
    public async Task<IActionResult> MyLeaves([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.MyLeavesAsync(MyUserId, pagination: pagination), skip, take));
    }

    [HttpGet("leaves/team")]
    public async Task<IActionResult> TeamLeaves([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.TeamLeavesAsync(MyUserId, pagination: pagination), skip, take));
    }

    [HttpPost("leaves/my")]
    public async Task<IActionResult> RequestMyLeave([FromBody] FaAttLeaveSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.RequestMyLeaveAsync(MyUserId, MyUsername, dto));
    }

    [HttpPut("leaves/my/{id:int}")]
    public async Task<IActionResult> UpdateMyLeave(int id, [FromBody] FaAttLeaveSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Create") is { } f) return f;
        return Ok(await _svc.UpdateMyLeaveAsync(id, MyUserId, dto));
    }

    [HttpPost("leaves/{id:int}/cancel-my")]
    public async Task<IActionResult> CancelMyLeave(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        await _svc.CancelMyLeaveAsync(id, MyUserId);
        return Ok(new { ok = true });
    }

    [HttpPost("leaves/{id:int}/manager-decide")]
    public async Task<IActionResult> ManagerDecide(int id, [FromQuery] bool approve)
    {
        bool isHr = await ForbiddenUnlessAsync(Mod, "Manage") == null;
        if (!isHr && await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ManagerDecideAsync(id, approve, MyUserId, MyUsername, isHr));
    }

    [HttpPost("leaves/{id:int}/hr-decide")]
    public async Task<IActionResult> HrDecide(int id, [FromQuery] bool approve)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.HrDecideAsync(id, approve, MyUserId, MyUsername));
    }

    [HttpPost("leaves/manager-decide-batch")]
    public async Task<IActionResult> ManagerDecideBatch([FromBody] FaAttDecideBatchDto dto)
    {
        bool isHr = await ForbiddenUnlessAsync(Mod, "Manage") == null;
        if (!isHr && await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await _svc.ManagerDecideBatchAsync(dto.Ids, dto.Approve, MyUserId, MyUsername, isHr));
    }

    [HttpPost("leaves/hr-decide-batch")]
    public async Task<IActionResult> HrDecideBatch([FromBody] FaAttDecideBatchDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.HrDecideBatchAsync(dto.Ids, dto.Approve, MyUserId, MyUsername));
    }

    [HttpGet("leaves/by-unit")]
    public async Task<IActionResult> LeavesByUnit([FromQuery] int orgUnitId, [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.LeavesByUnitAsync(orgUnitId, from, to, pagination: pagination), skip, take));
    }

    [HttpGet("absence-calendar")]
    public async Task<IActionResult> AbsenceCalendar([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] int? employeeId, [FromQuery] int? orgUnitId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(
            pagination => _svc.AbsenceCalendarAsync(from, to, employeeId, orgUnitId, pagination), skip, take));
    }

    [HttpGet("balances/my")]
    public async Task<IActionResult> MyBalances([FromQuery] int? year, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.MyBalancesAsync(MyUserId, year, pagination: pagination), skip, take));
    }

    [HttpGet("balances")]
    public async Task<IActionResult> Balances([FromQuery] int? employeeId, [FromQuery] int? year, [FromQuery] int? leaveTypeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.GetBalancesAsync(employeeId, year, leaveTypeId, pagination: pagination), skip, take));
    }

    [HttpPost("balances")]
    public async Task<IActionResult> SaveBalance([FromQuery] int employeeId, [FromQuery] int year, [FromQuery] int leaveTypeId, [FromBody] FaAttLeaveBalanceSaveDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.SaveBalanceAsync(employeeId, year, leaveTypeId, dto));
    }

    [HttpPost("balances/carry")]
    public async Task<IActionResult> CarryOver([FromQuery] int employeeId, [FromQuery] int leaveTypeId, [FromBody] FaAttLeaveCarryDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.CarryOverAsync(employeeId, leaveTypeId, dto));
    }

    [HttpPost("balances/{id:int}/cash")]
    public async Task<IActionResult> CashOut(int id, [FromBody] FaAttLeaveCashDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.CashOutAsync(id, dto));
    }

    [HttpPost("balances/init-year")]
    public async Task<IActionResult> InitYearBalances([FromBody] FaAttInitYearDto dto)
    {
        if (await ForbiddenUnlessAsync(Mod, "Manage") is { } f) return f;
        return Ok(await _svc.InitYearBalancesAsync(dto.Year, dto.LeaveTypeId));
    }

    [HttpDelete("leaves/{id:int}")]
    public async Task<IActionResult> DeleteLeave(int id)
    {
        if (await ForbiddenUnlessAsync(Mod, "Delete") is { } f) return f;
        await _svc.DeleteLeaveAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- گزارش‌ها -------------------

    [HttpGet("reports/month")]
    public async Task<IActionResult> MonthReport([FromQuery] int year, [FromQuery] int month, [FromQuery] int? orgUnitId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.MonthSummaryAsync(year, month, orgUnitId, pagination: pagination), skip, take));
    }

    [HttpGet("reports/month-excel")]
    public async Task<IActionResult> MonthExcel([FromQuery] int year, [FromQuery] int month, [FromQuery] int? orgUnitId)
    {
        if (await ForbiddenUnlessAsync(Mod, "Read") is { } f) return f;
        var bytes = await _svc.ExportMonthExcelAsync(year, month, orgUnitId);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"attendance-{year}-{month}.xlsx");
    }
}
