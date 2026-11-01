using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using ClosedXML.Excel;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Inventory.Api.Services.FaAtt;

/// <summary>نام‌های فارسی ماژول حضور و غیاب (تک‌منبع سمت سرور)</summary>
public static class FaAttTexts
{
    public static string ShiftType(int t) => t switch
    {
        0 => "ثابت", 1 => "چرخشی", 2 => "شب‌کاری", 3 => "شناور", _ => "نامشخص"
    };
    public static string LogType(int t) => t switch { 0 => "ورود", 1 => "خروج", _ => "نامشخص" };
    public static string LogSource(int s) => s switch
    {
        0 => "دستگاه", 1 => "موبایل", 2 => "وب‌کلاک", 3 => "دستی", _ => "نامشخص"
    };
    public static string DayStatus(int s) => s switch
    {
        0 => "حاضر", 1 => "تأخیر", 2 => "تعجیل", 3 => "تأخیر و تعجیل", 4 => "غیبت",
        5 => "مأموریت", 6 => "مرخصی", 7 => "تعطیل رسمی", 8 => "روز استراحت",
        9 => "تعطیل‌کار", 10 => "ناقص", _ => "نامشخص"
    };
    public static string ReqStatus(int s) => s switch
    {
        0 => "در انتظار", 1 => "تأیید شده", 2 => "رد شده", _ => "نامشخص"
    };
    public static string DeviceType(int t) => t switch
    {
        0 => "اثر انگشت", 1 => "تشخیص چهره", 2 => "کارتی", _ => "نامشخص"
    };
}

public interface IFaAttService
{
    // شیفت‌ها
    Task<List<FaAttShiftDto>> ListShiftsAsync(bool? onlyActive, Paging.Request? pagination = null);
    Task<FaAttShiftDto> SaveShiftAsync(int? id, FaAttShiftSaveDto dto);
    Task DeleteShiftAsync(int id);
    Task<List<FaAttShiftAssignDto>> ListAssignsAsync(int? employeeId, int? shiftId, Paging.Request? pagination = null);
    Task<FaAttShiftAssignDto> SaveAssignAsync(int? id, FaAttShiftAssignSaveDto dto);
    Task DeleteAssignAsync(int id);
    /// <summary>برنامه‌ریزی روزانه‌ی شیفت: برای هر آیتم، تخصیص‌های هم‌پوشانِ همان روز برش/حذف و شیفت جدید درج می‌شود</summary>
    Task<FaAttShiftPlanResultDto> PlanShiftsAsync(FaAttShiftPlanSaveDto dto);

    // دستگاه‌ها
    Task<List<FaAttDeviceDto>> ListDevicesAsync(bool? onlyActive, Paging.Request? pagination = null);
    Task<FaAttDeviceDto> SaveDeviceAsync(int? id, FaAttDeviceSaveDto dto);
    Task DeleteDeviceAsync(int id);

    // تردد
    Task<FaAttLogDto> ClockAsync(int userId, string userName, FaAttClockSaveDto dto);
    Task<FaAttLogDto> SaveLogAsync(FaAttLogSaveDto dto, int byUserId, string byName);
    Task<FaAttImportResultDto> ImportLogsAsync(Stream stream, string fileName, int? deviceId, int byUserId, string byName);
    Task<(byte[] Data, string FileName, string ContentType)> ImportTemplateAsync();
    Task DeleteLogAsync(int id);
    Task<List<FaAttLogDto>> EmployeeLogsAsync(int employeeId, DateTime from, DateTime to, Paging.Request? pagination = null);

    // محاسبه روزانه
    Task<FaAttDailyDto> RecalcAsync(int employeeId, DateTime date);
    Task<int> RecalcRangeAsync(DateTime from, DateTime to, int? employeeId, int? orgUnitId);
    Task<List<FaAttDailyDto>> DailyListAsync(DateTime from, DateTime to, int? employeeId, int? orgUnitId, int? status, Paging.Request? pagination = null);
    Task<FaAttMyTodayDto> MyTodayAsync(int userId);

    // ماموریت
    Task<List<FaAttMissionDto>> ListMissionsAsync(int? employeeId, int? status, Paging.Request? pagination = null);
    Task<FaAttMissionDto> SaveMissionAsync(int? id, FaAttMissionSaveDto dto);
    Task<FaAttMissionDto> DecideMissionAsync(int id, bool approve, int byUserId, string byName);
    Task DeleteMissionAsync(int id);

    // مرخصی
    Task<List<FaAttLeaveTypeDto>> ListLeaveTypesAsync(Paging.Request? pagination = null);
    Task<FaAttLeaveTypeDto> SaveLeaveTypeAsync(int? id, FaAttLeaveTypeSaveDto dto);
    Task<List<FaAttLeaveDto>> ListLeavesAsync(int? employeeId, int? status, Paging.Request? pagination = null);
    Task<FaAttLeaveDto> SaveLeaveAsync(int? id, FaAttLeaveSaveDto dto);
    Task<FaAttLeaveDto> RequestMyLeaveAsync(int userId, string userName, FaAttLeaveSaveDto dto);
    Task<FaAttLeaveDto> ManagerDecideAsync(int id, bool approve, int byUserId, string byName, bool isHr);
    Task<FaAttLeaveDto> HrDecideAsync(int id, bool approve, int byUserId, string byName);
    Task<FaAttBatchResultDto> ManagerDecideBatchAsync(List<int> ids, bool approve, int byUserId, string byName, bool isHr);
    Task<FaAttBatchResultDto> HrDecideBatchAsync(List<int> ids, bool approve, int byUserId, string byName);
    Task<FaAttBatchResultDto> DecideMissionBatchAsync(List<int> ids, bool approve, int byUserId, string byName);
    Task<List<FaAttLeaveDto>> LeavesByUnitAsync(int orgUnitId, DateTime from, DateTime to, Paging.Request? pagination = null);
    Task<List<FaAttCalendarEventDto>> AbsenceCalendarAsync(DateTime from, DateTime to, int? employeeId, int? orgUnitId, Paging.Request? pagination = null);
    Task<List<FaAttLeaveBalanceDto>> GetBalancesAsync(int? employeeId, int? year, int? leaveTypeId, Paging.Request? pagination = null);
    Task<FaAttLeaveBalanceDto> SaveBalanceAsync(int employeeId, int year, int leaveTypeId, FaAttLeaveBalanceSaveDto dto);
    Task<FaAttLeaveBalanceDto> CarryOverAsync(int employeeId, int leaveTypeId, FaAttLeaveCarryDto dto);
    Task<FaAttLeaveBalanceDto> CashOutAsync(int balanceId, FaAttLeaveCashDto dto);
    Task DeleteLeaveAsync(int id);
    Task<List<FaAttLeaveDto>> MyLeavesAsync(int userId, Paging.Request? pagination = null);
    Task<List<FaAttLeaveDto>> TeamLeavesAsync(int userId, Paging.Request? pagination = null);
    Task CancelMyLeaveAsync(int id, int userId);
    Task<List<FaAttLeaveBalanceDto>> MyBalancesAsync(int userId, int? year, Paging.Request? pagination = null);
    Task<int> InitYearBalancesAsync(int year, int? leaveTypeId);
    Task<List<FaAttMissionDto>> MyMissionsAsync(int userId, Paging.Request? pagination = null);
    Task<FaAttMissionDto> RequestMyMissionAsync(int userId, FaAttMissionSaveDto dto);
    Task<FaAttLeaveDto> UpdateMyLeaveAsync(int id, int userId, FaAttLeaveSaveDto dto);
    Task<FaAttMissionDto> UpdateMyMissionAsync(int id, int userId, FaAttMissionSaveDto dto);

    // گزارش‌ها (مبنای حقوق)
    Task<List<FaAttMonthSummaryDto>> MonthSummaryAsync(int year, int month, int? orgUnitId, Paging.Request? pagination = null);
    Task<byte[]> ExportMonthExcelAsync(int year, int month, int? orgUnitId);
}

public class FaAttService : IFaAttService
{
    private readonly AppDbContext _db;
    public FaAttService(AppDbContext db) => _db = db;

    // ==================== شیفت‌ها ====================

    public async Task<List<FaAttShiftDto>> ListShiftsAsync(bool? onlyActive, Paging.Request? pagination = null)
    {
        var q = _db.FaAttShifts.AsNoTracking().AsQueryable();
        if (onlyActive == true) q = q.Where(s => s.IsActive);
        return await q.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ThenBy(x => x.Id)
            .Select(s => new FaAttShiftDto
            {
                Id = s.Id, Code = s.Code, Name = s.Name, Type = (int)s.Type,
                StartTime = s.StartTime, EndTime = s.EndTime,
                LateToleranceMin = s.LateToleranceMin, EarlyToleranceMin = s.EarlyToleranceMin,
                OvertimeGraceMin = s.OvertimeGraceMin, RequiredMinutes = s.RequiredMinutes,
                OffDays = s.OffDays, Color = s.Color, IsActive = s.IsActive,
                AllowancePercent = s.AllowancePercent, SortOrder = s.SortOrder
            }).ToPageListAsync(pagination);
    }

    public async Task<FaAttShiftDto> SaveShiftAsync(int? id, FaAttShiftSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("نام شیفت الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new InvalidOperationException("کد شیفت الزامی است.");
        if (await _db.FaAttShifts.AnyAsync(s => s.Id != (id ?? 0) && s.Code == dto.Code.Trim()))
            throw new InvalidOperationException("کد شیفت تکراری است.");
        FaAttShift s;
        if (id is > 0)
        {
            s = await _db.FaAttShifts.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("شیفت یافت نشد.");
        }
        else { s = new FaAttShift(); _db.FaAttShifts.Add(s); }
        s.Code = dto.Code.Trim(); s.Name = dto.Name.Trim(); s.Type = (FaAttShiftType)dto.Type;
        s.StartTime = dto.StartTime; s.EndTime = dto.EndTime;
        s.LateToleranceMin = Math.Max(0, dto.LateToleranceMin);
        s.EarlyToleranceMin = Math.Max(0, dto.EarlyToleranceMin);
        s.OvertimeGraceMin = Math.Max(0, dto.OvertimeGraceMin);
        s.RequiredMinutes = Math.Max(0, dto.RequiredMinutes);
        s.OffDays = string.IsNullOrWhiteSpace(dto.OffDays) ? null : dto.OffDays.Trim();
        s.Color = string.IsNullOrWhiteSpace(dto.Color) ? null : dto.Color.Trim();
        s.IsActive = dto.IsActive; s.SortOrder = dto.SortOrder;
        s.AllowancePercent = Math.Max(0, dto.AllowancePercent);
        await _db.SaveChangesAsync();
        return (await ListShiftsAsync(null)).First(x => x.Id == s.Id);
    }

    public async Task DeleteShiftAsync(int id)
    {
        var s = await _db.FaAttShifts.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("شیفت یافت نشد.");
        if (await _db.FaAttShiftAssigns.AnyAsync(a => a.ShiftId == id))
            throw new InvalidOperationException("این شیفت به پرسنل تخصیص داده شده؛ ابتدا تخصیص‌ها را حذف کنید.");
        _db.FaAttShifts.Remove(s);
        await _db.SaveChangesAsync();
    }

    public async Task<List<FaAttShiftAssignDto>> ListAssignsAsync(int? employeeId, int? shiftId, Paging.Request? pagination = null)
    {
        var q = _db.FaAttShiftAssigns.AsNoTracking().AsQueryable();
        if (employeeId is > 0) q = q.Where(a => a.EmployeeId == employeeId.Value);
        if (shiftId is > 0) q = q.Where(a => a.ShiftId == shiftId.Value);
        var rows = await q.OrderByDescending(a => a.FromDate).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 2000);
        var list = new List<FaAttShiftAssignDto>();
        foreach (var a in rows)
        {
            var empName = await _db.HrEmployees.Where(e => e.Id == a.EmployeeId)
                .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync() ?? "";
            var shiftName = await _db.FaAttShifts.Where(s => s.Id == a.ShiftId)
                .Select(s => s.Name).FirstOrDefaultAsync() ?? "";
            list.Add(new FaAttShiftAssignDto
            {
                Id = a.Id, EmployeeId = a.EmployeeId, EmployeeName = empName,
                ShiftId = a.ShiftId, ShiftName = shiftName, FromDate = a.FromDate, ToDate = a.ToDate
            });
        }
        return list;
    }

    public async Task<FaAttShiftAssignDto> SaveAssignAsync(int? id, FaAttShiftAssignSaveDto dto)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId))
            throw new InvalidOperationException("پرسنل نامعتبر است.");
        if (!await _db.FaAttShifts.AnyAsync(s => s.Id == dto.ShiftId))
            throw new InvalidOperationException("شیفت نامعتبر است.");
        var from = dto.FromDate.Date;
        var to = dto.ToDate?.Date;
        if (to != null && to < from) throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از شروع باشد.");
        // عدم هم‌پوشانی تخصیص‌های یک نفر
        var others = await _db.FaAttShiftAssigns
            .Where(a => a.EmployeeId == dto.EmployeeId && a.Id != (id ?? 0)).ToListAsync();
        var newEnd = to ?? DateTime.MaxValue.Date;
        if (others.Any(a => from <= (a.ToDate?.Date ?? DateTime.MaxValue.Date) && a.FromDate.Date <= newEnd))
            throw new InvalidOperationException("این بازه با تخصیص دیگری از همین پرسنل هم‌پوشانی دارد.");
        FaAttShiftAssign a;
        if (id is > 0)
        {
            a = await _db.FaAttShiftAssigns.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("تخصیص یافت نشد.");
        }
        else { a = new FaAttShiftAssign(); _db.FaAttShiftAssigns.Add(a); }
        a.EmployeeId = dto.EmployeeId; a.ShiftId = dto.ShiftId; a.FromDate = from; a.ToDate = to;
        await _db.SaveChangesAsync();
        return (await ListAssignsAsync(dto.EmployeeId, null)).First(x => x.Id == a.Id);
    }

    public async Task DeleteAssignAsync(int id)
    {
        var a = await _db.FaAttShiftAssigns.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("تخصیص یافت نشد.");
        _db.FaAttShiftAssigns.Remove(a);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// برنامه‌ریزی روزانه‌ی شیفت (شبکه‌ی هفتگی/ماهانی): برای هر «پرسنل + روز»، تخصیص‌های هم‌پوشان
    /// برش می‌خورند (قبل/بعدِ روز محفوظ می‌ماند) و در صورت تعیین شیفت، تخصیص یک‌روزه‌ی جدید درج می‌شود.
    /// ShiftId تهی یعنی فقط خالی‌کردن آن روز. همه در یک تراکنش.
    /// </summary>
    public async Task<FaAttShiftPlanResultDto> PlanShiftsAsync(FaAttShiftPlanSaveDto dto)
    {
        if (dto.Items.Count == 0) throw new InvalidOperationException("آیتمی برای اعمال وجود ندارد.");
        if (dto.Items.Count > 2000) throw new InvalidOperationException("حداکثر ۲۰۰۰ خانه در هر اعمال.");
        var empIds = dto.Items.Select(i => i.EmployeeId).Distinct().ToList();
        var shiftIds = dto.Items.Where(i => i.ShiftId is > 0).Select(i => i.ShiftId!.Value).Distinct().ToList();
        if (await _db.HrEmployees.CountAsync(e => empIds.Contains(e.Id)) != empIds.Count)
            throw new InvalidOperationException("برخی پرسنل نامعتبرند.");
        var validShifts = shiftIds.Count == 0
            ? new List<int>()
            : await _db.FaAttShifts.Where(s => shiftIds.Contains(s.Id)).Select(s => s.Id).ToListAsync();
        if (validShifts.Count != shiftIds.Count)
            throw new InvalidOperationException("برخی شیفت‌ها نامعتبر یا غیرفعالند.");

        var applied = 0;
        foreach (var item in dto.Items)
        {
            var day = item.Date.Date;
            var next = day.AddDays(1);
            var overlaps = await _db.FaAttShiftAssigns
                .Where(a => a.EmployeeId == item.EmployeeId && a.FromDate < next && (a.ToDate == null || a.ToDate.Value >= day))
                .ToListAsync();
            foreach (var a in overlaps)
            {
                var aFrom = a.FromDate.Date;
                var aTo = a.ToDate?.Date;
                var coversBefore = aFrom < day;
                var coversAfter = aTo is null || aTo.Value > day;
                if (coversBefore && coversAfter)
                {
                    // وسط بازه را برش می‌دهیم: تکه‌ی قبل کوتاه می‌شود و تکه‌ی بعد رکورد جدید می‌گیرد
                    var after = new FaAttShiftAssign { EmployeeId = a.EmployeeId, ShiftId = a.ShiftId, FromDate = next, ToDate = aTo };
                    _db.FaAttShiftAssigns.Add(after);
                    a.ToDate = day.AddDays(-1);
                }
                else if (coversBefore) a.ToDate = day.AddDays(-1);
                else if (coversAfter) a.FromDate = next;
                else _db.FaAttShiftAssigns.Remove(a);
            }
            if (item.ShiftId is > 0)
                _db.FaAttShiftAssigns.Add(new FaAttShiftAssign
                {
                    EmployeeId = item.EmployeeId, ShiftId = item.ShiftId!.Value, FromDate = day, ToDate = day
                });
            applied++;
        }
        await _db.SaveChangesAsync();
        return new FaAttShiftPlanResultDto { Applied = applied };
    }

    // ==================== دستگاه‌ها ====================

    public async Task<List<FaAttDeviceDto>> ListDevicesAsync(bool? onlyActive, Paging.Request? pagination = null)
    {
        var q = _db.FaAttDevices.AsNoTracking().AsQueryable();
        if (onlyActive == true) q = q.Where(d => d.IsActive);
        var rows = await q.OrderBy(d => d.Code).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var list = new List<FaAttDeviceDto>();
        foreach (var d in rows)
        {
            list.Add(new FaAttDeviceDto
            {
                Id = d.Id, Code = d.Code, Name = d.Name, Type = (int)d.Type,
                Location = d.Location, SerialNo = d.SerialNo, IpAddress = d.IpAddress,
                IsActive = d.IsActive, LastSyncAt = d.LastSyncAt,
                LogsCount = await _db.FaAttLogs.CountAsync(l => l.DeviceId == d.Id)
            });
        }
        return list;
    }

    public async Task<FaAttDeviceDto> SaveDeviceAsync(int? id, FaAttDeviceSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("نام دستگاه الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new InvalidOperationException("کد دستگاه الزامی است.");
        if (await _db.FaAttDevices.AnyAsync(d => d.Id != (id ?? 0) && d.Code == dto.Code.Trim()))
            throw new InvalidOperationException("کد دستگاه تکراری است.");
        FaAttDevice d;
        if (id is > 0)
        {
            d = await _db.FaAttDevices.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("دستگاه یافت نشد.");
        }
        else { d = new FaAttDevice(); _db.FaAttDevices.Add(d); }
        d.Code = dto.Code.Trim(); d.Name = dto.Name.Trim(); d.Type = (FaAttDeviceType)dto.Type;
        d.Location = dto.Location?.Trim(); d.SerialNo = dto.SerialNo?.Trim(); d.IpAddress = dto.IpAddress?.Trim();
        d.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return (await ListDevicesAsync(null)).First(x => x.Id == d.Id);
    }

    public async Task DeleteDeviceAsync(int id)
    {
        var d = await _db.FaAttDevices.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("دستگاه یافت نشد.");
        if (await _db.FaAttLogs.AnyAsync(l => l.DeviceId == id))
            throw new InvalidOperationException("برای این دستگاه تردد ثبت شده است؛ به‌جای حذف، آن را غیرفعال کنید.");
        _db.FaAttDevices.Remove(d);
        await _db.SaveChangesAsync();
    }

    // ==================== تردد ====================

    public async Task<FaAttLogDto> ClockAsync(int userId, string userName, FaAttClockSaveDto dto)
    {
        var emp = await _db.HrEmployees.FirstOrDefaultAsync(e => e.SystemUserId == userId && e.IsActive)
            ?? throw new InvalidOperationException("کاربر شما به هیچ پرسنل فعالی متصل نیست؛ با مدیر سیستم هماهنگ کنید.");
        if (dto.Type is not (0 or 1))
            throw new InvalidOperationException("نوع تردد نامعتبر است.");
        var now = DateTime.Now;
        var dup = await _db.FaAttLogs.AnyAsync(l => l.EmployeeId == emp.Id && (int)l.Type == dto.Type
            && l.Timestamp >= now.AddMinutes(-3));
        if (dup) throw new InvalidOperationException("تردد تکراری ثبت نشد؛ کمتر از ۳ دقیقه از تردد مشابه گذشته است.");
        var log = new FaAttLog
        {
            EmployeeId = emp.Id, Timestamp = now, Type = (FaAttLogType)dto.Type,
            Source = FaAttLogSource.Web, Latitude = dto.Latitude, Longitude = dto.Longitude,
            CreatedByUserId = userId, CreatedByName = userName
        };
        _db.FaAttLogs.Add(log);
        await _db.SaveChangesAsync();
        await RecalcAsync(emp.Id, now.Date);
        return await MapLogAsync(log);
    }

    public async Task<(byte[] Data, string FileName, string ContentType)> ImportTemplateAsync()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("تردد دستگاه");
        ws.RightToLeft = true;
        var headers = new[] { "کد پرسنلی", "تاریخ (شمسی)", "ساعت", "نوع (ورود/خروج)" };
        for (var i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
            c.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }
        ws.SheetView.FreezeRows(1);
        ws.Cell(2, 1).Value = "1001"; ws.Cell(2, 2).Value = "1405/02/15"; ws.Cell(2, 3).Value = "08:05"; ws.Cell(2, 4).Value = "ورود";
        ws.Cell(3, 1).Value = "1001"; ws.Cell(3, 2).Value = "1405/02/15"; ws.Cell(3, 3).Value = "17:10"; ws.Cell(3, 4).Value = "خروج";
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return (ms.ToArray(), "FaAtt-Import-Template.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    public async Task<FaAttImportResultDto> ImportLogsAsync(Stream stream, string fileName, int? deviceId, int byUserId, string byName)
    {
        var res = new FaAttImportResultDto();
        void Err(string m) { if (res.Errors.Count < 30) res.Errors.Add(m); }
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls" or ".csv" or ".txt"))
            throw new InvalidOperationException("فرمت فایل معتبر نیست (xlsx یا csv).");
        if (deviceId is > 0 && !await _db.FaAttDevices.AnyAsync(d => d.Id == deviceId.Value))
            throw new InvalidOperationException("دستگاه نامعتبر است.");
        var grid = ext is ".csv" or ".txt" ? ReadCsvGrid(stream) : ReadXlsxGrid(stream);
        if (grid.Count < 2) throw new InvalidOperationException("فایل خالی است یا سطری ندارد.");
        int ciCode = -1, ciDate = -1, ciTime = -1, ciType = -1;
        var head = grid[0];
        for (var i = 0; i < head.Count; i++)
        {
            var h = head[i].Trim();
            var hl = h.ToLowerInvariant();
            if (h.Contains("پرسنل") || (hl.Contains("code") && !hl.Contains("national")) || h == "کد") ciCode = i;
            else if (h.Contains("تاریخ") || hl == "date") ciDate = i;
            else if (h.Contains("ساعت") || h.Contains("زمان") || hl == "time") ciTime = i;
            else if (h.Contains("نوع") || h.Contains("جهت") || hl == "type") ciType = i;
        }
        if (ciCode < 0 || ciDate < 0)
            throw new InvalidOperationException("ستون‌های «کد پرسنلی» و «تاریخ» در سطر اول یافت نشد؛ از فایل نمونه استفاده کنید.");
        var emps = await _db.HrEmployees.AsNoTracking().ToListAsync();
        var byCode = emps.GroupBy(e => (e.Code ?? "").Trim()).Where(g => g.Key.Length > 0).ToDictionary(g => g.Key, g => g.First());
        var parsed = new List<(int EmpId, string EmpName, DateTime Ts, int Type)>();
        var r = 1;
        foreach (var row in grid.Skip(1))
        {
            r++;
            if (r > 20001) { Err("بیش از ۲۰۰۰۰ سطر پشتیبانی نمی‌شود؛ بقیه نادیده گرفته شد."); break; }
            string Cell(int i) => i < 0 || i >= row.Count ? "" : row[i].Trim();
            var code = Fa.ToEn(Cell(ciCode)).Trim();
            if (code.Length == 0) continue;
            if (!byCode.TryGetValue(code, out var emp)) { Err($"سطر {Fa.Digits(r.ToString())}: کد پرسنلی «{code}» یافت نشد."); continue; }
            var dt = ParseImportDateTime(Cell(ciDate), Cell(ciTime));
            if (dt == null) { Err($"سطر {Fa.Digits(r.ToString())}: تاریخ/ساعت نامعتبر است."); continue; }
            parsed.Add((emp.Id, $"{emp.FirstName} {emp.LastName}".Trim(), dt.Value, ParseLogType(Cell(ciType))));
        }
        if (parsed.Count == 0)
        {
            if (res.Errors.Count == 0) res.Errors.Add("سطری برای ورود یافت نشد.");
            return res;
        }
        var empIds = parsed.Select(x => x.EmpId).Distinct().ToList();
        var minAll = parsed.Min(x => x.Ts.Date);
        var maxAll = parsed.Max(x => x.Ts.Date).AddDays(1);
        var existing = await _db.FaAttLogs.AsNoTracking()
            .Where(l => empIds.Contains(l.EmployeeId) && l.Timestamp >= minAll && l.Timestamp < maxAll)
            .Select(l => new { l.EmployeeId, l.Timestamp, l.Type }).ToListAsync();
        var keys = existing.Select(x => $"{x.EmployeeId}|{x.Timestamp:yyyyMMddHHmm}|{(int)x.Type}").ToHashSet();
        var fn = fileName ?? "فایل";
        var shortName = fn.Length > 60 ? fn[..60] : fn;
        foreach (var x in parsed)
        {
            var key = $"{x.EmpId}|{x.Ts:yyyyMMddHHmm}|{x.Type}";
            if (keys.Contains(key)) { res.Skipped++; continue; }
            keys.Add(key);
            _db.FaAttLogs.Add(new FaAttLog
            {
                EmployeeId = x.EmpId, Timestamp = x.Ts, Type = (FaAttLogType)x.Type,
                Source = FaAttLogSource.Device, DeviceId = deviceId,
                Note = $"ایمپورت: {shortName}", CreatedByUserId = byUserId, CreatedByName = byName
            });
            res.Added++;
        }
        await _db.SaveChangesAsync();
        if (deviceId is > 0)
        {
            var dev = await _db.FaAttDevices.FirstOrDefaultAsync(d => d.Id == deviceId.Value);
            if (dev != null) { dev.LastSyncAt = DateTime.Now; await _db.SaveChangesAsync(); }
        }
        foreach (var g in parsed.GroupBy(x => x.EmpId))
        {
            try { await RecalcRangeAsync(g.Min(x => x.Ts.Date), g.Max(x => x.Ts.Date), g.Key, null); }
            catch (Exception ex)
            {
                if (res.Errors.Count < 30) res.Errors.Add($"خطای محاسبه مجدد {g.First().EmpName}: {ex.Message}");
            }
        }
        res.From = minAll; res.To = maxAll.AddDays(-1);
        return res;
    }

    private static DateTime? ParseImportDateTime(string dateRaw, string timeRaw)
    {
        var d = Fa.ToEn(dateRaw ?? "").Trim();
        var t = Fa.ToEn(timeRaw ?? "").Trim();
        if (d.Length == 0) return null;
        var dp = d.Split(' ', 'T');
        if (dp.Length > 1 && t.Length == 0) { d = dp[0]; t = dp[1]; }
        var date = PersianDate.TryParse(d);
        if (date == null)
        {
            if (!DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.None, out var g)) return null;
            date = g.Date;
        }
        var hh = 0; var mm = 0; var ss = 0;
        if (t.Length > 0)
        {
            t = new string(t.Where(c => char.IsDigit(c) || c == ':').ToArray());
            var tp = t.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (tp.Length == 0) return null;
            if (tp.Length == 1 && tp[0].Length is 3 or 4)
            {
                if (!int.TryParse(tp[0][..^2], out hh) || !int.TryParse(tp[0][^2..], out mm)) return null;
            }
            else
            {
                if (!int.TryParse(tp[0], out hh)) return null;
                if (tp.Length > 1 && !int.TryParse(tp[1], out mm)) return null;
                if (tp.Length > 2 && !int.TryParse(tp[2], out ss)) return null;
            }
            if (hh > 23 || mm > 59 || ss > 59) return null;
        }
        return date.Value.Date.AddHours(hh).AddMinutes(mm).AddSeconds(ss);
    }

    private static int ParseLogType(string raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (s is "ورود" or "ورودی" or "in" or "i" or "0" or "checkin" or "check-in") return 0;
        if (s is "خروج" or "out" or "o" or "1" or "checkout" or "check-out") return 1;
        return 2;
    }

    private static List<List<string>> ReadXlsxGrid(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.FirstOrDefault() ?? throw new InvalidOperationException("شیت یافت نشد.");
        var grid = new List<List<string>>();
        var lastRow = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 0, 20005);
        var lastCol = Math.Min(ws.LastColumnUsed()?.ColumnNumber() ?? 0, 20);
        for (var r = 1; r <= lastRow; r++)
        {
            var row = new List<string>();
            for (var c = 1; c <= lastCol; c++)
                row.Add(ws.Cell(r, c).GetFormattedString() ?? "");
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            grid.Add(row);
        }
        return grid;
    }

    private static List<List<string>> ReadCsvGrid(Stream stream)
    {
        using var sr = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = sr.ReadToEnd();
        var lines = text.Split('\n');
        var first = lines.FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        var delim = first.Count(c => c == ',') >= first.Count(c => c == ';') ? ',' : ';';
        var grid = new List<List<string>>();
        foreach (var ln in lines)
        {
            if (string.IsNullOrWhiteSpace(ln)) continue;
            grid.Add(SplitCsv(ln.TrimEnd('\r'), delim));
            if (grid.Count > 20005) break;
        }
        return grid;
    }

    private static List<string> SplitCsv(string line, char delim)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        var inQ = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQ && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQ = !inQ;
            }
            else if (ch == delim && !inQ) { list.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        list.Add(sb.ToString());
        return list.Select(s => s.Trim()).ToList();
    }

    public async Task<FaAttLogDto> SaveLogAsync(FaAttLogSaveDto dto, int byUserId, string byName)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId))
            throw new InvalidOperationException("پرسنل نامعتبر است.");
        if (dto.DeviceId is > 0 && !await _db.FaAttDevices.AnyAsync(d => d.Id == dto.DeviceId.Value))
            throw new InvalidOperationException("دستگاه نامعتبر است.");
        var log = new FaAttLog
        {
            EmployeeId = dto.EmployeeId, Timestamp = dto.Timestamp,
            Type = (FaAttLogType)dto.Type, Source = (FaAttLogSource)dto.Source,
            DeviceId = dto.DeviceId, Latitude = dto.Latitude, Longitude = dto.Longitude,
            Note = dto.Note?.Trim(), CreatedByUserId = byUserId, CreatedByName = byName
        };
        _db.FaAttLogs.Add(log);
        if (dto.DeviceId is > 0)
        {
            var dev = await _db.FaAttDevices.FirstOrDefaultAsync(d => d.Id == dto.DeviceId.Value);
            if (dev != null) dev.LastSyncAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        await RecalcAsync(dto.EmployeeId, dto.Timestamp.Date);
        return await MapLogAsync(log);
    }

    public async Task DeleteLogAsync(int id)
    {
        var log = await _db.FaAttLogs.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("تردد یافت نشد.");
        var empId = log.EmployeeId;
        var date = log.Timestamp.Date;
        _db.FaAttLogs.Remove(log);
        await _db.SaveChangesAsync();
        await RecalcAsync(empId, date);
    }

    public async Task<List<FaAttLogDto>> EmployeeLogsAsync(int employeeId, DateTime from, DateTime to, Paging.Request? pagination = null)
    {
        from = from.Date; to = to.Date.AddDays(1);
        var rows = await _db.FaAttLogs.AsNoTracking()
            .Where(l => l.EmployeeId == employeeId && l.Timestamp >= from && l.Timestamp < to)
            .OrderBy(l => l.Timestamp).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 2000);
        var list = new List<FaAttLogDto>();
        foreach (var l in rows) list.Add(await MapLogAsync(l));
        return list;
    }

    private async Task<FaAttLogDto> MapLogAsync(FaAttLog l)
    {
        var empName = await _db.HrEmployees.Where(e => e.Id == l.EmployeeId)
            .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync() ?? "";
        var devName = l.DeviceId is > 0
            ? await _db.FaAttDevices.Where(d => d.Id == l.DeviceId!.Value).Select(d => d.Name).FirstOrDefaultAsync()
            : null;
        return new FaAttLogDto
        {
            Id = l.Id, EmployeeId = l.EmployeeId, EmployeeName = empName, Timestamp = l.Timestamp,
            Type = (int)l.Type, Source = (int)l.Source, DeviceId = l.DeviceId, DeviceName = devName,
            Latitude = l.Latitude, Longitude = l.Longitude, Note = l.Note, CreatedByName = l.CreatedByName
        };
    }

    // ==================== محاسبه روزانه ====================

    /// <summary>
    /// موتور محاسبه وضعیت روزانه یک نفر در یک تاریخ.
    /// قواعد: شب‌کاری خروجِ صبح روز بعد را هم می‌بیند؛ پانچ‌های بدون جهت یکی‌درمیان جفت می‌شوند؛
    /// شناور تأخیر/تعجیل ندارد؛ کار در تعطیل/استراحت = تعطیل‌کار با اضافه‌کاری کامل.
    /// </summary>
    public async Task<FaAttDailyDto> RecalcAsync(int employeeId, DateTime date)
    {
        date = date.Date;
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == employeeId))
            throw new InvalidOperationException("پرسنل یافت نشد.");

        var assign = await _db.FaAttShiftAssigns.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.FromDate.Date <= date && (a.ToDate == null || a.ToDate.Value.Date >= date))
            .OrderByDescending(a => a.FromDate).FirstOrDefaultAsync();
        FaAttShift? shift = null;
        if (assign != null)
            shift = await _db.FaAttShifts.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == assign.ShiftId && s.IsActive);

        // ساعات کاری پیش‌فرض سازمانی (HrMainRules): پرسنلی که شیفت اختصاصی FaAtt ندارد،
        // از «ساعت شروع/پایان کار پیش‌فرض + روزهای تعطیل هفتگی + ارفاق تأخیر» تنظیمات پایه منابع
        // انسانی اصلی پیروی می‌کند. شیفتِ مجازی فقط برای محاسبه است و ذخیره نمی‌شود (ShiftId = null می‌ماند).
        var isDefaultShift = false;
        if (shift == null)
        {
            var rules = await _db.HrMainRules.AsNoTracking().FirstOrDefaultAsync();
            if (rules != null && rules.DefaultWorkStartTime != rules.DefaultWorkEndTime)
            {
                shift = new FaAttShift
                {
                    Name = "پیش‌فرض سازمان",
                    Type = FaAttShiftType.Fixed,
                    StartTime = rules.DefaultWorkStartTime,
                    EndTime = rules.DefaultWorkEndTime,
                    LateToleranceMin = Math.Max(0, rules.LateGraceMinutes),
                    EarlyToleranceMin = Math.Max(0, rules.LateGraceMinutes),
                    OvertimeGraceMin = 15,
                    OffDays = rules.DefaultWeeklyOffDays,
                    IsActive = true
                };
                isDefaultShift = true;
            }
        }

        var next = date.AddDays(1);
        var logs = await _db.FaAttLogs.AsNoTracking()
            .Where(l => l.EmployeeId == employeeId && l.Timestamp >= date && l.Timestamp < next)
            .OrderBy(l => l.Timestamp).ToListAsync();

        // شب‌کاری: خروجِ صبحِ روز بعد (تا پایان شیفت + ۳ ساعت) جزو همین روز
        if (shift != null && shift.EndTime <= shift.StartTime)
        {
            var cutoff = next.Add(shift.EndTime).AddHours(3);
            var extra = await _db.FaAttLogs.AsNoTracking()
                .Where(l => l.EmployeeId == employeeId && l.Timestamp >= next && l.Timestamp <= cutoff)
                .OrderBy(l => l.Timestamp).ToListAsync();
            logs.AddRange(extra);
        }

        var (firstIn, lastOut, workMin, incomplete) = PairLogs(logs);

        var onMission = await _db.FaAttMissions.AsNoTracking().AnyAsync(m =>
            m.EmployeeId == employeeId && m.Status == FaAttRequestStatus.Approved &&
            m.FromDate.Date <= date && m.ToDate.Date >= date);
        var onLeave = await _db.FaAttLeaves.AsNoTracking().AnyAsync(l =>
            l.EmployeeId == employeeId && l.Status == FaAttRequestStatus.Approved &&
            l.FromDate.Date <= date && l.ToDate.Date >= date && l.HoursPerDay == null);
        var hourlyLeave = await _db.FaAttLeaves.AsNoTracking()
            .Where(l => l.EmployeeId == employeeId && l.Status == FaAttRequestStatus.Approved &&
                l.FromDate.Date <= date && l.ToDate.Date >= date && l.HoursPerDay != null)
            .SumAsync(l => l.HoursPerDay ?? 0);
        var holiday = await _db.HrMainHolidays.AsNoTracking()
            .FirstOrDefaultAsync(h => h.HolidayDate.Date == date);
        var isOffDay = shift != null && IsOffDay(shift.OffDays, date.DayOfWeek);

        var status = FaAttDayStatus.Absent;
        var notes = new List<string>();
        int late = 0, early = 0, ot = 0;

        if (firstIn == null && lastOut == null)
        {
            if (holiday != null) { status = FaAttDayStatus.Holiday; notes.Add(holiday.Name); }
            else if (onMission) status = FaAttDayStatus.Mission;
            else if (onLeave) status = FaAttDayStatus.Leave;
            else if (isOffDay) status = FaAttDayStatus.OffDay;
            else status = FaAttDayStatus.Absent;
        }
        else if (shift == null)
        {
            status = incomplete && workMin == 0 ? FaAttDayStatus.Incomplete : FaAttDayStatus.Present;
            notes.Add("بدون شیفت");
        }
        else if (shift.Type == FaAttShiftType.Flexible)
        {
            status = incomplete && workMin == 0 ? FaAttDayStatus.Incomplete : FaAttDayStatus.Present;
            ot = Math.Max(0, workMin - shift.RequiredMinutes);
            if (ot < shift.OvertimeGraceMin) ot = 0;
        }
        else
        {
            var expIn = date.Add(shift.StartTime);
            var expOut = shift.EndTime <= shift.StartTime ? next.Add(shift.EndTime) : date.Add(shift.EndTime);
            if (firstIn != null)
                late = Math.Max(0, (int)(firstIn.Value - expIn.AddMinutes(shift.LateToleranceMin)).TotalMinutes);
            if (lastOut != null)
                early = Math.Max(0, (int)(expOut.AddMinutes(-shift.EarlyToleranceMin) - lastOut.Value).TotalMinutes);
            var earlyArr = firstIn == null ? 0 : Math.Max(0, (int)(expIn - firstIn.Value).TotalMinutes);
            var lateOut = lastOut == null ? 0 : Math.Max(0, (int)(lastOut.Value - expOut).TotalMinutes);
            ot = (earlyArr >= shift.OvertimeGraceMin ? earlyArr : 0)
               + (lateOut >= shift.OvertimeGraceMin ? lateOut : 0);

            if (holiday != null || isOffDay)
            {
                status = FaAttDayStatus.WorkedOff;
                ot = workMin;
                late = 0; early = 0;
                if (holiday != null) notes.Add(holiday.Name);
            }
            else if (incomplete && workMin == 0) status = FaAttDayStatus.Incomplete;
            else if (late > 0 && early > 0) status = FaAttDayStatus.LateAndEarly;
            else if (late > 0) status = FaAttDayStatus.Late;
            else if (early > 0) status = FaAttDayStatus.EarlyLeave;
            else status = FaAttDayStatus.Present;
        }

        if (status != FaAttDayStatus.Mission && onMission) notes.Add("مأموریت");
        if (status != FaAttDayStatus.Leave && onLeave) notes.Add("مرخصی");
        if (hourlyLeave > 0) notes.Add($"مرخصی ساعتی {hourlyLeave:0.#} ساعت");
        if (isDefaultShift) notes.Add("شیفت پیش‌فرض سازمان");

        var row = await _db.FaAttDailies.FirstOrDefaultAsync(d => d.EmployeeId == employeeId && d.Date == date);
        if (row == null) { row = new FaAttDaily { EmployeeId = employeeId, Date = date }; _db.FaAttDailies.Add(row); }
        row.ShiftId = isDefaultShift ? null : shift?.Id;
        row.FirstIn = firstIn; row.LastOut = lastOut;
        row.WorkMinutes = workMin; row.LateMinutes = late; row.EarlyMinutes = early; row.OvertimeMinutes = ot;
        row.NightMinutes = NightOverlapMinutes(firstIn, lastOut);
        row.Status = status; row.IsIncomplete = incomplete;
        row.Note = notes.Count == 0 ? null : string.Join("، ", notes);
        row.CalculatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return await MapDailyAsync(row);
    }

    /// <summary>هم‌پوشانی بازه حضور با پنجره شب (۲۲ تا ۶ صبح) به دقیقه — مبنای فوق‌العاده شب‌کاری.</summary>
    private static int NightOverlapMinutes(DateTime? firstIn, DateTime? lastOut)
    {
        if (firstIn == null || lastOut == null || lastOut <= firstIn) return 0;
        var total = 0;
        var day = firstIn.Value.Date;
        while (day <= lastOut.Value.Date.AddDays(1))
        {
            var winStart = day.AddHours(22);
            var winEnd = day.AddDays(1).AddHours(6);
            var ovStart = firstIn.Value > winStart ? firstIn.Value : winStart;
            var ovEnd = lastOut.Value < winEnd ? lastOut.Value : winEnd;
            if (ovEnd > ovStart) total += (int)(ovEnd - ovStart).TotalMinutes;
            day = day.AddDays(1);
            if (day > lastOut.Value.Date.AddDays(2)) break;
        }
        return Math.Max(0, total);
    }

    /// <summary>جفت‌سازی پانچ‌ها: ورود/خروجِ مشخص با هم جفت می‌شوند؛ نامشخص‌ها یکی‌درمیان.</summary>
    private static (DateTime? FirstIn, DateTime? LastOut, int WorkMin, bool Incomplete) PairLogs(List<FaAttLog> logs)
    {
        if (logs.Count == 0) return (null, null, 0, false);
        if (logs.Count == 1)
            return logs[0].Type == FaAttLogType.Out
                ? (null, logs[0].Timestamp, 0, true)
                : (logs[0].Timestamp, null, 0, true);

        DateTime? firstIn = null, lastOut = null, openIn = null;
        var work = 0;
        var expectIn = true;
        foreach (var l in logs)
        {
            var isIn = l.Type switch
            {
                FaAttLogType.In => true,
                FaAttLogType.Out => false,
                _ => expectIn
            };
            if (l.Type == FaAttLogType.Unknown) expectIn = !expectIn;
            if (isIn)
            {
                firstIn ??= l.Timestamp;
                openIn ??= l.Timestamp;
            }
            else
            {
                lastOut = l.Timestamp;
                if (openIn != null)
                {
                    work += Math.Max(0, (int)(l.Timestamp - openIn.Value).TotalMinutes);
                    openIn = null;
                }
            }
        }
        return (firstIn, lastOut, work, openIn != null);
    }

    private static bool IsOffDay(string? offDays, DayOfWeek dow)
    {
        if (string.IsNullOrWhiteSpace(offDays)) return false;
        return offDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(p => int.TryParse(p, out var n) && n == (int)dow);
    }

    public async Task<int> RecalcRangeAsync(DateTime from, DateTime to, int? employeeId, int? orgUnitId)
    {
        from = from.Date; to = to.Date;
        if (to < from) throw new InvalidOperationException("بازه نامعتبر است.");
        if ((to - from).TotalDays > 62) throw new InvalidOperationException("حداکثر بازه محاسبه ۶۲ روز است.");
        List<int> empIds;
        if (employeeId is > 0)
        {
            if (!await _db.HrEmployees.AnyAsync(e => e.Id == employeeId.Value))
                throw new InvalidOperationException("پرسنل یافت نشد.");
            empIds = new List<int> { employeeId.Value };
        }
        else
        {
            var q = _db.HrEmployees.AsNoTracking().Where(e => e.IsActive);
            if (orgUnitId is > 0) q = q.Where(e => e.OrgUnitId == orgUnitId.Value);
            empIds = await q.Select(e => e.Id).Take(500).ToListAsync();
        }
        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            foreach (var empId in empIds)
            {
                await RecalcAsync(empId, d);
                count++;
            }
        return count;
    }

    public async Task<List<FaAttDailyDto>> DailyListAsync(DateTime from, DateTime to, int? employeeId, int? orgUnitId, int? status, Paging.Request? pagination = null)
    {
        from = from.Date; to = to.Date;
        var q = _db.FaAttDailies.AsNoTracking()
            .Where(d => d.Date >= from && d.Date <= to);
        if (employeeId is > 0) q = q.Where(d => d.EmployeeId == employeeId.Value);
        if (status is >= 0) q = q.Where(d => (int)d.Status == status.Value);
        if (orgUnitId is > 0)
        {
            var ids = await _db.HrEmployees.AsNoTracking()
                .Where(e => e.OrgUnitId == orgUnitId.Value).Select(e => e.Id).ToListAsync();
            q = q.Where(d => ids.Contains(d.EmployeeId));
        }
        var rows = await q.OrderBy(d => d.Date).ThenBy(d => d.EmployeeId).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 5000);
        var list = new List<FaAttDailyDto>();
        foreach (var d in rows) list.Add(await MapDailyAsync(d));
        return list;
    }

    public async Task<FaAttMyTodayDto> MyTodayAsync(int userId)
    {
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.SystemUserId == userId && e.IsActive)
            ?? throw new InvalidOperationException("کاربر شما به هیچ پرسنل فعالی متصل نیست؛ با مدیر سیستم هماهنگ کنید.");
        var today = DateTime.Today;
        var daily = await RecalcAsync(emp.Id, today);
        FaAttShift? shift = null;
        if (daily.ShiftId is > 0)
            shift = await _db.FaAttShifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == daily.ShiftId!.Value);
        return new FaAttMyTodayDto
        {
            EmployeeId = emp.Id,
            EmployeeName = emp.FirstName + " " + emp.LastName,
            Date = today,
            ShiftName = shift?.Name,
            ShiftStart = shift?.StartTime,
            ShiftEnd = shift?.EndTime,
            Daily = daily,
            Logs = await EmployeeLogsAsync(emp.Id, today, today)
        };
    }

    private async Task<FaAttDailyDto> MapDailyAsync(FaAttDaily d)
    {
        var emp = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.Id == d.EmployeeId)
            .Select(e => new { e.Code, N = e.FirstName + " " + e.LastName })
            .FirstOrDefaultAsync();
        var shiftName = d.ShiftId is > 0
            ? await _db.FaAttShifts.AsNoTracking().Where(s => s.Id == d.ShiftId!.Value).Select(s => s.Name).FirstOrDefaultAsync()
            : null;
        return new FaAttDailyDto
        {
            Id = d.Id, EmployeeId = d.EmployeeId, EmployeeName = emp?.N, EmployeeCode = emp?.Code,
            Date = d.Date, ShiftId = d.ShiftId, ShiftName = shiftName,
            FirstIn = d.FirstIn, LastOut = d.LastOut,
            WorkMinutes = d.WorkMinutes, LateMinutes = d.LateMinutes,
            EarlyMinutes = d.EarlyMinutes, OvertimeMinutes = d.OvertimeMinutes,
            NightMinutes = d.NightMinutes,
            Status = (int)d.Status, IsIncomplete = d.IsIncomplete, Note = d.Note
        };
    }

    // ==================== ماموریت ====================

    public async Task<List<FaAttMissionDto>> ListMissionsAsync(int? employeeId, int? status, Paging.Request? pagination = null)
    {
        var q = _db.FaAttMissions.AsNoTracking().AsQueryable();
        if (employeeId is > 0) q = q.Where(m => m.EmployeeId == employeeId.Value);
        if (status is >= 0) q = q.Where(m => (int)m.Status == status.Value);
        var rows = await q.OrderByDescending(m => m.FromDate).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 1000);
        var list = new List<FaAttMissionDto>();
        foreach (var m in rows)
        {
            var empName = await _db.HrEmployees.Where(e => e.Id == m.EmployeeId)
                .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync() ?? "";
            list.Add(new FaAttMissionDto
            {
                Id = m.Id, EmployeeId = m.EmployeeId, EmployeeName = empName,
                FromDate = m.FromDate, ToDate = m.ToDate, Destination = m.Destination, Reason = m.Reason,
                Status = (int)m.Status, DecidedByName = m.DecidedByName, DecidedAt = m.DecidedAt
            });
        }
        return list;
    }

    public async Task<FaAttMissionDto> SaveMissionAsync(int? id, FaAttMissionSaveDto dto)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId))
            throw new InvalidOperationException("پرسنل نامعتبر است.");
        var from = dto.FromDate.Date; var to = dto.ToDate.Date;
        if (to < from) throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از شروع باشد.");
        await GuardMissionOverlapAsync(dto.EmployeeId, from, to, id);
        FaAttMission m;
        if (id is > 0)
        {
            m = await _db.FaAttMissions.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("مأموریت یافت نشد.");
            if (m.Status != FaAttRequestStatus.Pending)
                throw new InvalidOperationException("درخواست تعیین‌تکلیف‌شده قابل ویرایش نیست.");
        }
        else { m = new FaAttMission(); _db.FaAttMissions.Add(m); }
        m.EmployeeId = dto.EmployeeId; m.FromDate = from; m.ToDate = to;
        m.Destination = dto.Destination?.Trim(); m.Reason = dto.Reason?.Trim();
        await _db.SaveChangesAsync();
        return (await ListMissionsAsync(m.EmployeeId, null)).First(x => x.Id == m.Id);
    }

    public async Task<FaAttMissionDto> DecideMissionAsync(int id, bool approve, int byUserId, string byName)
    {
        var m = await _db.FaAttMissions.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("مأموریت یافت نشد.");
        m.Status = approve ? FaAttRequestStatus.Approved : FaAttRequestStatus.Rejected;
        m.DecidedByUserId = byUserId; m.DecidedByName = byName; m.DecidedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        var mUserId = await _db.HrEmployees.AsNoTracking().Where(e => e.Id == m.EmployeeId)
            .Select(e => e.SystemUserId).FirstOrDefaultAsync();
        if (mUserId is > 0)
            await NotifyAsync(mUserId!.Value, approve ? "تأیید مأموریت" : "رد مأموریت",
                approve ? $"مأموریت شما ({m.Destination}) توسط {byName} تأیید شد."
                        : $"مأموریت شما ({m.Destination}) توسط {byName} رد شد.",
                "fa-att/missions");
        await RecalcRangeAsync(m.FromDate, m.ToDate > m.FromDate.AddDays(62) ? m.FromDate.AddDays(62) : m.ToDate, m.EmployeeId, null);
        return (await ListMissionsAsync(m.EmployeeId, null)).First(x => x.Id == m.Id);
    }

    public async Task DeleteMissionAsync(int id)
    {
        var m = await _db.FaAttMissions.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("مأموریت یافت نشد.");
        var empId = m.EmployeeId; var from = m.FromDate; var to = m.ToDate;
        _db.FaAttMissions.Remove(m);
        await _db.SaveChangesAsync();
        await RecalcRangeAsync(from, to > from.AddDays(62) ? from.AddDays(62) : to, empId, null);
    }

    // ==================== مرخصی ====================

    public async Task<List<FaAttLeaveTypeDto>> ListLeaveTypesAsync(Paging.Request? pagination = null)
    {
        return await _db.FaAttLeaveTypes.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(x => x.Id)
            .Select(t => new FaAttLeaveTypeDto
            {
                Id = t.Id, Name = t.Name, AnnualLimitDays = t.AnnualLimitDays,
                IsActive = t.IsActive, SortOrder = t.SortOrder
            }).ToPageListAsync(pagination);
    }

    public async Task<FaAttLeaveTypeDto> SaveLeaveTypeAsync(int? id, FaAttLeaveTypeSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("نام نوع مرخصی الزامی است.");
        FaAttLeaveType t;
        if (id is > 0)
        {
            t = await _db.FaAttLeaveTypes.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("نوع مرخصی یافت نشد.");
        }
        else { t = new FaAttLeaveType(); _db.FaAttLeaveTypes.Add(t); }
        t.Name = dto.Name.Trim(); t.AnnualLimitDays = dto.AnnualLimitDays;
        t.IsActive = dto.IsActive; t.SortOrder = dto.SortOrder;
        await _db.SaveChangesAsync();
        return (await ListLeaveTypesAsync()).First(x => x.Id == t.Id);
    }

    public async Task<List<FaAttLeaveDto>> ListLeavesAsync(int? employeeId, int? status, Paging.Request? pagination = null)
    {
        var q = _db.FaAttLeaves.AsNoTracking().AsQueryable();
        if (employeeId is > 0) q = q.Where(l => l.EmployeeId == employeeId.Value);
        if (status is >= 0) q = q.Where(l => (int)l.Status == status.Value);
        var rows = await q.OrderByDescending(l => l.FromDate).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 1000);
        var list = new List<FaAttLeaveDto>();
        foreach (var l in rows)
        {
            var empName = await _db.HrEmployees.Where(e => e.Id == l.EmployeeId)
                .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync() ?? "";
            var typeName = await _db.FaAttLeaveTypes.Where(t => t.Id == l.LeaveTypeId)
                .Select(t => t.Name).FirstOrDefaultAsync() ?? "";
            list.Add(new FaAttLeaveDto
            {
                Id = l.Id, EmployeeId = l.EmployeeId, EmployeeName = empName,
                LeaveTypeId = l.LeaveTypeId, LeaveTypeName = typeName,
                FromDate = l.FromDate, ToDate = l.ToDate, HoursPerDay = l.HoursPerDay, Reason = l.Reason,
                Status = (int)l.Status, DecidedByName = l.DecidedByName, DecidedAt = l.DecidedAt,
                WorkflowStep = l.WorkflowStep, ManagerDecidedByName = l.ManagerDecidedByName, ManagerDecidedAt = l.ManagerDecidedAt
            });
        }
        return list;
    }

    public async Task<FaAttLeaveDto> SaveLeaveAsync(int? id, FaAttLeaveSaveDto dto)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == dto.EmployeeId))
            throw new InvalidOperationException("پرسنل نامعتبر است.");
        if (!await _db.FaAttLeaveTypes.AnyAsync(t => t.Id == dto.LeaveTypeId))
            throw new InvalidOperationException("نوع مرخصی نامعتبر است.");
        var from = dto.FromDate.Date; var to = dto.ToDate.Date;
        if (to < from) throw new InvalidOperationException("تاریخ پایان نمی‌تواند قبل از شروع باشد.");
        if (dto.HoursPerDay is <= 0 or > 24) throw new InvalidOperationException("ساعت مرخصی ساعتی باید بین ۰ تا ۲۴ باشد.");
        await GuardLeaveOverlapAsync(dto.EmployeeId, from, to, id);
        FaAttLeave l;
        if (id is > 0)
        {
            l = await _db.FaAttLeaves.FirstOrDefaultAsync(x => x.Id == id.Value)
                ?? throw new InvalidOperationException("مرخصی یافت نشد.");
            if (l.Status != FaAttRequestStatus.Pending)
                    if (l.Status != FaAttRequestStatus.Pending)
                throw new InvalidOperationException("درخواست تعیین‌تکلیف‌شده قابل ویرایش نیست.");
        }
        else { l = new FaAttLeave(); _db.FaAttLeaves.Add(l); }
        l.EmployeeId = dto.EmployeeId; l.LeaveTypeId = dto.LeaveTypeId;
        l.FromDate = from; l.ToDate = to; l.HoursPerDay = dto.HoursPerDay; l.Reason = dto.Reason?.Trim();
        if (id == null)
            await NotifyManagerNewRequestAsync(l);
        await _db.SaveChangesAsync();
        return (await ListLeavesAsync(l.EmployeeId, null)).First(x => x.Id == l.Id);
    }

    public async Task<FaAttLeaveDto> ManagerDecideAsync(int id, bool approve, int byUserId, string byName, bool isHr)
    {
        var l = await _db.FaAttLeaves.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("مرخصی یافت نشد.");
        if (l.Status != FaAttRequestStatus.Pending || l.WorkflowStep != 0)
            throw new InvalidOperationException("این درخواست در مرحله تأیید مدیر نیست.");
        if (!isHr)
        {
            var me = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.SystemUserId == byUserId);
            var owner = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == l.EmployeeId);
            if (me == null || owner?.ManagerId != me.Id)
                throw new InvalidOperationException("شما مدیر مستقیم این پرسنل نیستید.");
        }
        l.ManagerDecidedByUserId = byUserId; l.ManagerDecidedByName = byName; l.ManagerDecidedAt = DateTime.Now;
        string nTitle, nBody;
        if (approve)
        {
            l.WorkflowStep = 1;
            nTitle = "تأیید مرخصی توسط مدیر";
            nBody = $"درخواست مرخصی شما توسط {byName} تأیید و برای تأیید نهایی نزد منابع انسانی قرار گرفت.";
        }
        else
        {
            l.Status = FaAttRequestStatus.Rejected; l.WorkflowStep = 2;
            nTitle = "رد مرخصی توسط مدیر";
            nBody = $"درخواست مرخصی شما توسط {byName} رد شد.";
        }
        var reqUserId = await _db.HrEmployees.AsNoTracking().Where(e => e.Id == l.EmployeeId)
            .Select(e => e.SystemUserId).FirstOrDefaultAsync();
        if (reqUserId is > 0)
            await NotifyAsync(reqUserId!.Value, nTitle, nBody, "fa-att/my-leaves");
        await _db.SaveChangesAsync();
        if (!approve)
            await RecalcRangeAsync(l.FromDate, CappedTo(l.FromDate, l.ToDate), l.EmployeeId, null);
        return (await ListLeavesAsync(l.EmployeeId, null)).First(x => x.Id == l.Id);
    }

    public async Task<FaAttLeaveDto> HrDecideAsync(int id, bool approve, int byUserId, string byName)
    {
        var l = await _db.FaAttLeaves.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("مرخصی یافت نشد.");
        if (l.Status != FaAttRequestStatus.Pending || l.WorkflowStep != 1)
            throw new InvalidOperationException("این درخواست در مرحله تأیید نهایی منابع انسانی نیست.");
        l.DecidedByUserId = byUserId; l.DecidedByName = byName; l.DecidedAt = DateTime.Now;
        l.WorkflowStep = 2;
        if (approve)
        {
            await ConsumeLeaveAsync(l, +1);
            l.Status = FaAttRequestStatus.Approved;
        }
        else
            l.Status = FaAttRequestStatus.Rejected;
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == l.EmployeeId);
        var empName = emp == null ? "" : emp.FirstName + " " + emp.LastName;
        if (emp?.SystemUserId is > 0)
            await NotifyAsync(emp.SystemUserId!.Value,
                approve ? "تأیید نهایی مرخصی" : "رد نهایی مرخصی",
                approve ? $"درخواست مرخصی شما توسط منابع انسانی ({byName}) تأیید نهایی شد."
                        : $"درخواست مرخصی شما در تأیید نهایی توسط {byName} رد شد.",
                "fa-att/my-leaves");
        var mgr = await ManagerOfAsync(l.EmployeeId);
        if (mgr != null)
            await NotifyAsync(mgr.Value.UserId,
                approve ? "تأیید نهایی مرخصی" : "رد نهایی مرخصی",
                approve ? $"مرخصی {empName} تأیید نهایی شد." : $"مرخصی {empName} در تأیید نهایی رد شد.",
                "fa-att/approvals");
        await _db.SaveChangesAsync();
        await RecalcRangeAsync(l.FromDate, CappedTo(l.FromDate, l.ToDate), l.EmployeeId, null);
        return (await ListLeavesAsync(l.EmployeeId, null)).First(x => x.Id == l.Id);
    }

    public async Task DeleteLeaveAsync(int id)
    {
        var l = await _db.FaAttLeaves.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("مرخصی یافت نشد.");
        var empId = l.EmployeeId; var from = l.FromDate; var to = l.ToDate;
        if (l.Status == FaAttRequestStatus.Approved)
            await ConsumeLeaveAsync(l, -1);
        _db.FaAttLeaves.Remove(l);
        await _db.SaveChangesAsync();
        await RecalcRangeAsync(from, to > from.AddDays(62) ? from.AddDays(62) : to, empId, null);
    }

    // ==================== گزارش‌ها ====================

    private static (DateTime From, DateTime To) JalaliMonthRange(int jy, int jm)
    {
        if (jy is < 1300 or > 1500 || jm is < 1 or > 12)
            throw new InvalidOperationException("سال/ماه شمسی نامعتبر است.");
        var pc = new PersianCalendar();
        var from = pc.ToDateTime(jy, jm, 1, 0, 0, 0, 0);
        return (from, from.AddDays(pc.GetDaysInMonth(jy, jm) - 1));
    }

    public async Task<List<FaAttMonthSummaryDto>> MonthSummaryAsync(int year, int month, int? orgUnitId, Paging.Request? pagination = null)
    {
        var (from, to) = JalaliMonthRange(year, month);
        var empQ = _db.HrEmployees.AsNoTracking().Where(e => e.IsActive);
        if (orgUnitId is > 0) empQ = empQ.Where(e => e.OrgUnitId == orgUnitId.Value);
        var emps = await empQ.OrderBy(e => e.Code).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 2000);
        var empIds = emps.Select(e => e.Id).ToList();
        var dailies = await _db.FaAttDailies.AsNoTracking()
            .Where(d => d.Date >= from && d.Date <= to && empIds.Contains(d.EmployeeId))
            .ToListAsync();
        var list = new List<FaAttMonthSummaryDto>();
        foreach (var e in emps)
        {
            var ds = dailies.Where(d => d.EmployeeId == e.Id).ToList();
            list.Add(new FaAttMonthSummaryDto
            {
                EmployeeId = e.Id, EmployeeCode = e.Code, EmployeeName = e.FirstName + " " + e.LastName,
                Year = year, Month = month,
                PresentDays = ds.Count(d => d.Status is FaAttDayStatus.Present or FaAttDayStatus.Late
                    or FaAttDayStatus.EarlyLeave or FaAttDayStatus.LateAndEarly or FaAttDayStatus.Incomplete),
                AbsentDays = ds.Count(d => d.Status == FaAttDayStatus.Absent),
                LateCount = ds.Count(d => d.Status is FaAttDayStatus.Late or FaAttDayStatus.LateAndEarly),
                LateMinutes = ds.Sum(d => d.LateMinutes),
                EarlyMinutes = ds.Sum(d => d.EarlyMinutes),
                OvertimeMinutes = ds.Sum(d => d.OvertimeMinutes),
                WorkMinutes = ds.Sum(d => d.WorkMinutes),
                MissionDays = ds.Count(d => d.Status == FaAttDayStatus.Mission),
                LeaveDays = ds.Count(d => d.Status == FaAttDayStatus.Leave),
                HolidayWorkDays = ds.Count(d => d.Status == FaAttDayStatus.WorkedOff),
                IncompleteDays = ds.Count(d => d.IsIncomplete),
                NightMinutes = ds.Sum(d => d.NightMinutes)
            });
        }
        return list;
    }

    public async Task<byte[]> ExportMonthExcelAsync(int year, int month, int? orgUnitId)
    {
        var rows = await MonthSummaryAsync(year, month, orgUnitId);
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("خلاصه ماهانه");
        ws.RightToLeft = true;
        string[] head =
        {
            "کد", "نام و نام خانوادگی", "روزهای حاضر", "غیبت", "تعداد تأخیر",
            "دقیقه تأخیر", "دقیقه تعجیل", "اضافه‌کاری (دقیقه)", "کارکرد (دقیقه)",
            "مأموریت (روز)", "مرخصی (روز)", "تعطیل‌کار (روز)", "تردد ناقص (روز)", "شب‌کاری (دقیقه)"
        };
        for (var i = 0; i < head.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = head[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
        }
        var r = 2;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = x.EmployeeCode ?? "";
            ws.Cell(r, 2).Value = x.EmployeeName ?? "";
            ws.Cell(r, 3).Value = x.PresentDays;
            ws.Cell(r, 4).Value = x.AbsentDays;
            ws.Cell(r, 5).Value = x.LateCount;
            ws.Cell(r, 6).Value = x.LateMinutes;
            ws.Cell(r, 7).Value = x.EarlyMinutes;
            ws.Cell(r, 8).Value = x.OvertimeMinutes;
            ws.Cell(r, 9).Value = x.WorkMinutes;
            ws.Cell(r, 10).Value = x.MissionDays;
            ws.Cell(r, 11).Value = x.LeaveDays;
            ws.Cell(r, 12).Value = x.HolidayWorkDays;
            ws.Cell(r, 13).Value = x.IncompleteDays;
            ws.Cell(r, 14).Value = x.NightMinutes;
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ==================== §۷: درخواست من + تقویم واحد + مانده ====================

    public async Task<FaAttLeaveDto> RequestMyLeaveAsync(int userId, string userName, FaAttLeaveSaveDto dto)
    {
        var emp = await _db.HrEmployees.FirstOrDefaultAsync(e => e.SystemUserId == userId && e.IsActive)
            ?? throw new InvalidOperationException("کاربر شما به هیچ پرسنل فعالی متصل نیست؛ با مدیر سیستم هماهنگ کنید.");
        dto.EmployeeId = emp.Id;
        return await SaveLeaveAsync(null, dto);
    }

    public async Task<List<FaAttLeaveDto>> LeavesByUnitAsync(int orgUnitId, DateTime from, DateTime to, Paging.Request? pagination = null)
    {
        from = from.Date; to = to.Date;
        var empIds = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.IsActive && e.OrgUnitId == orgUnitId).Select(e => e.Id).ToListAsync();
        var rows = await _db.FaAttLeaves.AsNoTracking()
            .Where(l => empIds.Contains(l.EmployeeId) && l.FromDate.Date <= to && l.ToDate.Date >= from)
            .OrderBy(l => l.FromDate).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 2000);
        var list = new List<FaAttLeaveDto>();
        foreach (var l in rows) list.Add(await MapLeaveAsync(l));
        return list;
    }

    public async Task<List<FaAttLeaveBalanceDto>> GetBalancesAsync(int? employeeId, int? year, int? leaveTypeId, Paging.Request? pagination = null)
    {
        var q = _db.FaAttLeaveBalances.AsNoTracking().AsQueryable();
        if (employeeId is > 0) q = q.Where(b => b.EmployeeId == employeeId.Value);
        if (year is > 1000) q = q.Where(b => b.Year == year.Value);
        if (leaveTypeId is > 0) q = q.Where(b => b.LeaveTypeId == leaveTypeId.Value);
        var rows = await q.OrderBy(b => b.Year).ThenBy(b => b.LeaveTypeId).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 2000);
        var list = new List<FaAttLeaveBalanceDto>();
        foreach (var b in rows) list.Add(await MapBalanceAsync(b));
        return list;
    }

    public async Task<FaAttLeaveBalanceDto> SaveBalanceAsync(int employeeId, int year, int leaveTypeId, FaAttLeaveBalanceSaveDto dto)
    {
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == employeeId))
            throw new InvalidOperationException("پرسنل نامعتبر است.");
        var type = await _db.FaAttLeaveTypes.FirstOrDefaultAsync(t => t.Id == leaveTypeId)
            ?? throw new InvalidOperationException("نوع مرخصی نامعتبر است.");
        if (year is < 1300 or > 1500) throw new InvalidOperationException("سال شمسی نامعتبر است.");
        var bal = await EnsureBalanceAsync(employeeId, year, type);
        bal.EntitledDays = Math.Max(0, dto.EntitledDays);
        if (dto.Note != null) bal.Note = dto.Note.Trim();
        await _db.SaveChangesAsync();
        return await MapBalanceAsync(bal);
    }

    public async Task<FaAttLeaveBalanceDto> CarryOverAsync(int employeeId, int leaveTypeId, FaAttLeaveCarryDto dto)
    {
        var type = await _db.FaAttLeaveTypes.FirstOrDefaultAsync(t => t.Id == leaveTypeId)
            ?? throw new InvalidOperationException("نوع مرخصی نامعتبر است.");
        if (await ResolveEntitledAsync(type) == null)
            throw new InvalidOperationException("این نوع مرخصی سقف سالانه ندارد؛ انتقال معنا ندارد.");
        if (dto.FromYear is < 1300 or > 1500) throw new InvalidOperationException("سال مبدأ نامعتبر است.");
        if (dto.MaxDays <= 0) throw new InvalidOperationException("سقف انتقال باید بیشتر از صفر باشد.");
        var src = await _db.FaAttLeaveBalances.FirstOrDefaultAsync(b =>
            b.EmployeeId == employeeId && b.Year == dto.FromYear && b.LeaveTypeId == leaveTypeId)
            ?? throw new InvalidOperationException("مانده‌ای برای سال مبدأ ثبت نشده است.");
        var tgt = await EnsureBalanceAsync(employeeId, dto.FromYear + 1, type);
        var add = Math.Min(Math.Max(0, src.Remaining - tgt.CarriedDays), Math.Max(0, dto.MaxDays - tgt.CarriedDays));
        if (add <= 0) throw new InvalidOperationException("چیزی برای انتقال نمانده است (قبلاً منتقل شده یا مانده صفر است).");
        tgt.CarriedDays += add;
        await _db.SaveChangesAsync();
        return await MapBalanceAsync(tgt);
    }

    public async Task<FaAttLeaveBalanceDto> CashOutAsync(int balanceId, FaAttLeaveCashDto dto)
    {
        var bal = await _db.FaAttLeaveBalances.FirstOrDefaultAsync(b => b.Id == balanceId)
            ?? throw new InvalidOperationException("مانده یافت نشد.");
        if (dto.Days <= 0) throw new InvalidOperationException("تعداد روز باید بیشتر از صفر باشد.");
        if (dto.Amount < 0) throw new InvalidOperationException("مبلغ نمی‌تواند منفی باشد.");
        if (bal.Remaining + 1e-9 < dto.Days)
            throw new InvalidOperationException($"مانده کافی نیست (مانده: {bal.Remaining:0.#} روز).");
        bal.CashedDays += dto.Days;
        bal.CashAmount = (bal.CashAmount ?? 0) + dto.Amount;
        if (!string.IsNullOrWhiteSpace(dto.Note))
            bal.Note = string.IsNullOrWhiteSpace(bal.Note) ? dto.Note.Trim() : bal.Note + " | " + dto.Note.Trim();
        await _db.SaveChangesAsync();
        return await MapBalanceAsync(bal);
    }

    private async Task<FaAttLeaveDto> MapLeaveAsync(FaAttLeave l)
    {
        var empName = await _db.HrEmployees.Where(e => e.Id == l.EmployeeId)
            .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync() ?? "";
        var typeName = await _db.FaAttLeaveTypes.Where(t => t.Id == l.LeaveTypeId)
            .Select(t => t.Name).FirstOrDefaultAsync() ?? "";
        return new FaAttLeaveDto
        {
            Id = l.Id, EmployeeId = l.EmployeeId, EmployeeName = empName,
            LeaveTypeId = l.LeaveTypeId, LeaveTypeName = typeName,
            FromDate = l.FromDate, ToDate = l.ToDate, HoursPerDay = l.HoursPerDay, Reason = l.Reason,
            Status = (int)l.Status, DecidedByName = l.DecidedByName, DecidedAt = l.DecidedAt,
            WorkflowStep = l.WorkflowStep, ManagerDecidedByName = l.ManagerDecidedByName, ManagerDecidedAt = l.ManagerDecidedAt
        };
    }

    private async Task<FaAttLeaveBalanceDto> MapBalanceAsync(FaAttLeaveBalance b)
    {
        var emp = await _db.HrEmployees.AsNoTracking().Where(e => e.Id == b.EmployeeId)
            .Select(e => new { e.Code, N = e.FirstName + " " + e.LastName }).FirstOrDefaultAsync();
    var typeName = await _db.FaAttLeaveTypes.AsNoTracking().Where(t => t.Id == b.LeaveTypeId)
            .Select(t => t.Name).FirstOrDefaultAsync();
        return new FaAttLeaveBalanceDto
        {
            Id = b.Id, EmployeeId = b.EmployeeId, EmployeeName = emp?.N, EmployeeCode = emp?.Code,
            Year = b.Year, LeaveTypeId = b.LeaveTypeId, LeaveTypeName = typeName,
            EntitledDays = b.EntitledDays, UsedDays = b.UsedDays,
            CarriedDays = b.CarriedDays, CashedDays = b.CashedDays,
            Remaining = b.Remaining, CashAmount = b.CashAmount, Note = b.Note
        };
    }

    /// <summary>مدیر مستقیم پرسنل (خودارجاع HrEmployee.ManagerId).</summary>
    private async Task<(int UserId, string Name)?> ManagerOfAsync(int employeeId)
    {
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp?.ManagerId is not > 0) return null;
        var mgr = await _db.HrEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == emp.ManagerId!.Value && e.IsActive);
        if (mgr?.SystemUserId is not > 0) return null;
        return (mgr.SystemUserId!.Value, mgr.FirstName + " " + mgr.LastName);
    }

    private async Task NotifyAsync(int userId, string title, string body, string? link)
    {
        if (userId <= 0) return;
        _db.AppNotifications.Add(new AppNotification
        {
            UserId = userId, Title = title, Body = body, Link = link,
            FromName = "حضور و غیاب فروغ آریا", FormName = "FaAtt"
        });
        await Inventory.Api.Services.PushQueue.StageAsync(_db, new[] { userId }, title, body, link);
    }

    private async Task NotifyManagerNewRequestAsync(FaAttLeave l)
    {
        var mgr = await ManagerOfAsync(l.EmployeeId);
        if (mgr == null) return;
        var empName = await _db.HrEmployees.AsNoTracking().Where(e => e.Id == l.EmployeeId)
            .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync() ?? "";
        var typeName = await _db.FaAttLeaveTypes.AsNoTracking().Where(t => t.Id == l.LeaveTypeId)
            .Select(t => t.Name).FirstOrDefaultAsync() ?? "مرخصی";
        await NotifyAsync(mgr.Value.UserId, "درخواست مرخصی جدید",
            $"{empName} درخواست {typeName} ثبت کرد؛ نیازمند تأیید شماست.", "fa-att/approvals");
    }

    private static DateTime CappedTo(DateTime from, DateTime to)
        => to > from.AddDays(62) ? from.AddDays(62) : to;

    /// <summary>استحقاق پیش‌فرض نوع مرخصی؛ برای استحقاقی از تنظیمات پایه HrMain خوانده می‌شود.</summary>
    private async Task<double?> ResolveEntitledAsync(FaAttLeaveType type)
    {
        if (type.Name == "استحقاقی")
        {
            var rules = await _db.HrMainRules.FirstOrDefaultAsync();
            if (rules != null) return rules.AnnualLeaveDays;
        }
        return type.AnnualLimitDays;
    }

    private async Task<FaAttLeaveBalance> EnsureBalanceAsync(int employeeId, int year, FaAttLeaveType type)
    {
        var bal = await _db.FaAttLeaveBalances.FirstOrDefaultAsync(b =>
            b.EmployeeId == employeeId && b.Year == year && b.LeaveTypeId == type.Id);
        if (bal == null)
        {
            bal = new FaAttLeaveBalance
            {
                EmployeeId = employeeId, Year = year, LeaveTypeId = type.Id,
                EntitledDays = await ResolveEntitledAsync(type) ?? 0
            };
            _db.FaAttLeaveBalances.Add(bal);
        }
        return bal;
    }

    /// <summary>مصرف/برگشت مانده (sign: مثبت=مصرف، منفی=برگشت). ساعتی به معادل روزانه (÷۸) تبدیل می‌شود.</summary>
    private async Task ConsumeLeaveAsync(FaAttLeave l, int sign)
    {
        var type = await _db.FaAttLeaveTypes.FirstOrDefaultAsync(t => t.Id == l.LeaveTypeId);
        if (type == null) return;
        if (await ResolveEntitledAsync(type) == null) return;
        var jy = new PersianCalendar().GetYear(l.FromDate);
        var days = (l.ToDate.Date - l.FromDate.Date).Days + 1;
        var equiv = l.HoursPerDay != null ? l.HoursPerDay.Value * days / 8.0 : days;
        var bal = await EnsureBalanceAsync(l.EmployeeId, jy, type);
        if (sign > 0 && bal.Remaining + 1e-9 < equiv)
            throw new InvalidOperationException($"مانده مرخصی «{type.Name}» کافی نیست (مانده: {bal.Remaining:0.#} روز، نیاز: {equiv:0.#} روز).");
        bal.UsedDays = Math.Max(0, bal.UsedDays + sign * equiv);
    }

    // ==================== §۷ حرفه‌ای: سلف‌سرویس + تیم + صدور گروهی ====================

    public async Task<List<FaAttLeaveDto>> MyLeavesAsync(int userId, Paging.Request? pagination = null)
    {
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.SystemUserId == userId);
        if (emp == null) return new();
        return await ListLeavesAsync(emp.Id, null, pagination: pagination);
    }

    public async Task<List<FaAttLeaveDto>> TeamLeavesAsync(int userId, Paging.Request? pagination = null)
    {
        var me = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.SystemUserId == userId);
        if (me == null) return new();
        var reportIds = await _db.HrEmployees.AsNoTracking()
            .Where(e => e.IsActive && e.ManagerId == me.Id).Select(e => e.Id).ToListAsync();
        if (reportIds.Count == 0) return new();
        var rows = await _db.FaAttLeaves.AsNoTracking()
            .Where(l => reportIds.Contains(l.EmployeeId) && l.Status == FaAttRequestStatus.Pending && l.WorkflowStep == 0)
            .OrderBy(l => l.FromDate).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 500);
        var list = new List<FaAttLeaveDto>();
        foreach (var l in rows) list.Add(await MapLeaveAsync(l));
        return list;
    }

    public async Task CancelMyLeaveAsync(int id, int userId)
    {
        var l = await _db.FaAttLeaves.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("مرخصی یافت نشد.");
        var ownerUserId = await _db.HrEmployees.AsNoTracking().Where(e => e.Id == l.EmployeeId)
            .Select(e => e.SystemUserId).FirstOrDefaultAsync();
        if (ownerUserId != userId)
            throw new InvalidOperationException("این درخواست متعلق به شما نیست.");
        if (l.Status != FaAttRequestStatus.Pending)
            throw new InvalidOperationException("فقط درخواست در انتظار قابل انصراف است.");
        var empId = l.EmployeeId; var from = l.FromDate; var to = l.ToDate;
        _db.FaAttLeaves.Remove(l);
        await _db.SaveChangesAsync();
        await RecalcRangeAsync(from, CappedTo(from, to), empId, null);
    }

    public async Task<List<FaAttLeaveBalanceDto>> MyBalancesAsync(int userId, int? year, Paging.Request? pagination = null)
    {
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.SystemUserId == userId);
        if (emp == null) return new();
        return await GetBalancesAsync(emp.Id, year, null, pagination: pagination);
    }

    public async Task<int> InitYearBalancesAsync(int year, int? leaveTypeId)
    {
        if (year is < 1300 or > 1500) throw new InvalidOperationException("سال شمسی نامعتبر است.");
        var tQ = _db.FaAttLeaveTypes.AsNoTracking().Where(t => t.IsActive);
        if (leaveTypeId is > 0) tQ = tQ.Where(t => t.Id == leaveTypeId.Value);
        var types = await tQ.ToListAsync();
        if (types.Count == 0) throw new InvalidOperationException("نوع مرخصی فعالی یافت نشد.");
        var empIds = await _db.HrEmployees.AsNoTracking().Where(e => e.IsActive).Select(e => e.Id).ToListAsync();
        var typeIds = types.Select(t => t.Id).ToList();
        var have = await _db.FaAttLeaveBalances.AsNoTracking()
            .Where(b => b.Year == year && empIds.Contains(b.EmployeeId) && typeIds.Contains(b.LeaveTypeId))
            .Select(b => b.EmployeeId + ":" + b.LeaveTypeId).ToListAsync();
        var set = new HashSet<string>(have);
        int created = 0;
        foreach (var empId in empIds)
            foreach (var t in types)
            {
                if (!set.Add(empId + ":" + t.Id)) continue;
                _db.FaAttLeaveBalances.Add(new FaAttLeaveBalance
                {
                    EmployeeId = empId, Year = year, LeaveTypeId = t.Id,
                    EntitledDays = await ResolveEntitledAsync(t) ?? 0
                });
                created++;
            }
        await _db.SaveChangesAsync();
        return created;
    }

    public async Task<List<FaAttMissionDto>> MyMissionsAsync(int userId, Paging.Request? pagination = null)
    {
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.SystemUserId == userId);
        if (emp == null) return new();
        return await ListMissionsAsync(emp.Id, null, pagination: pagination);
    }

    public async Task<FaAttMissionDto> RequestMyMissionAsync(int userId, FaAttMissionSaveDto dto)
    {
        var emp = await _db.HrEmployees.FirstOrDefaultAsync(e => e.SystemUserId == userId && e.IsActive)
            ?? throw new InvalidOperationException("کاربر شما به هیچ پرسنل فعالی متصل نیست؛ با مدیر سیستم هماهنگ کنید.");
        dto.EmployeeId = emp.Id;
        return await SaveMissionAsync(null, dto);
    }

    public async Task<FaAttLeaveDto> UpdateMyLeaveAsync(int id, int userId, FaAttLeaveSaveDto dto)
    {
        var l = await _db.FaAttLeaves.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("مرخصی یافت نشد.");
        var ownerUserId = await _db.HrEmployees.AsNoTracking().Where(e => e.Id == l.EmployeeId)
            .Select(e => e.SystemUserId).FirstOrDefaultAsync();
        if (ownerUserId != userId)
            throw new InvalidOperationException("این درخواست متعلق به شما نیست.");
        dto.EmployeeId = l.EmployeeId;
        return await SaveLeaveAsync(id, dto);
    }

    public async Task<FaAttMissionDto> UpdateMyMissionAsync(int id, int userId, FaAttMissionSaveDto dto)
    {
        var m = await _db.FaAttMissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("مأموریت یافت نشد.");
        var ownerUserId = await _db.HrEmployees.AsNoTracking().Where(e => e.Id == m.EmployeeId)
            .Select(e => e.SystemUserId).FirstOrDefaultAsync();
        if (ownerUserId != userId)
            throw new InvalidOperationException("این درخواست متعلق به شما نیست.");
        dto.EmployeeId = m.EmployeeId;
        return await SaveMissionAsync(id, dto);
    }

    private async Task GuardLeaveOverlapAsync(int employeeId, DateTime from, DateTime to, int? excludeId)
    {
        var clash = await _db.FaAttLeaves.AsNoTracking().AnyAsync(l => l.EmployeeId == employeeId
            && l.Id != (excludeId ?? 0)
            && (l.Status == FaAttRequestStatus.Pending || l.Status == FaAttRequestStatus.Approved)
            && l.FromDate.Date <= to && l.ToDate.Date >= from);
        if (clash) throw new InvalidOperationException("با یک درخواست مرخصی دیگر در این بازه تداخل دارد.");
    }

    private async Task GuardMissionOverlapAsync(int employeeId, DateTime from, DateTime to, int? excludeId)
    {
        var clash = await _db.FaAttMissions.AsNoTracking().AnyAsync(m => m.EmployeeId == employeeId
            && m.Id != (excludeId ?? 0)
            && (m.Status == FaAttRequestStatus.Pending || m.Status == FaAttRequestStatus.Approved)
            && m.FromDate.Date <= to && m.ToDate.Date >= from);
        if (clash) throw new InvalidOperationException("با یک مأموریت دیگر در این بازه تداخل دارد.");
    }


    // ==================== تقویم یکپارچه غیبت ====================

    /// <summary>مرخصی + مأموریت + شیفت + آموزش + تعطیلات در یک نما (سقف بازه ۶۲ روز).</summary>
    public async Task<List<FaAttCalendarEventDto>> AbsenceCalendarAsync(
        DateTime from, DateTime to, int? employeeId, int? orgUnitId, Paging.Request? pagination = null)
    {
        from = from.Date; to = to.Date;
        if (to < from) (from, to) = (to, from);
        if ((to - from).Days > 62) to = from.AddDays(62);
        var empQ = _db.HrEmployees.AsNoTracking().Where(e => e.IsActive);
        if (employeeId is > 0) empQ = empQ.Where(e => e.Id == employeeId.Value);
        else if (orgUnitId is > 0) empQ = empQ.Where(e => e.OrgUnitId == orgUnitId.Value || e.HrMainNodeId == orgUnitId.Value);
        var emps = await empQ.OrderBy(e => e.Code).Take(500)
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName);
        var empIds = emps.Keys.ToList();
        var ev = new List<FaAttCalendarEventDto>();
        if (empIds.Count == 0 && employeeId == null && orgUnitId == null)
            return ev;

        var leaves = await _db.FaAttLeaves.AsNoTracking()
            .Where(l => empIds.Contains(l.EmployeeId)
                && l.Status != FaAttRequestStatus.Rejected
                && l.FromDate.Date <= to && l.ToDate.Date >= from)
            .OrderBy(l => l.FromDate).ThenBy(l => l.Id).Take(2000).ToListAsync();
        var typeNames = await _db.FaAttLeaveTypes.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name);
        foreach (var l in leaves)
            ev.Add(new FaAttCalendarEventDto
            {
                Kind = "Leave", FromDate = l.FromDate.Date, ToDate = l.ToDate.Date,
                EmployeeId = l.EmployeeId,
                EmployeeName = emps.TryGetValue(l.EmployeeId, out var n) ? n : null,
                Title = (typeNames.TryGetValue(l.LeaveTypeId, out var tn) ? tn : "مرخصی")
                    + (l.HoursPerDay == null ? "" : $" (ساعتی {l.HoursPerDay:0.#})"),
                Color = l.Status == FaAttRequestStatus.Approved ? "#16a34a" : "#d97706",
                Status = l.Status == FaAttRequestStatus.Approved ? 1 : 0
            });

        var missions = await _db.FaAttMissions.AsNoTracking()
            .Where(m => empIds.Contains(m.EmployeeId)
                && m.Status != FaAttRequestStatus.Rejected
                && m.FromDate.Date <= to && m.ToDate.Date >= from)
            .OrderBy(m => m.FromDate).ThenBy(m => m.Id).Take(1000).ToListAsync();
        foreach (var m in missions)
            ev.Add(new FaAttCalendarEventDto
            {
                Kind = "Mission", FromDate = m.FromDate.Date, ToDate = m.ToDate.Date,
                EmployeeId = m.EmployeeId,
                EmployeeName = emps.TryGetValue(m.EmployeeId, out var n2) ? n2 : null,
                Title = string.IsNullOrWhiteSpace(m.Destination) ? "مأموریت" : $"مأموریت: {m.Destination}",
                Color = m.Status == FaAttRequestStatus.Approved ? "#2563eb" : "#93c5fd",
                Status = m.Status == FaAttRequestStatus.Approved ? 1 : 0
            });

        var assigns = await _db.FaAttShiftAssigns.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId)
                && a.FromDate.Date <= to && (a.ToDate == null || a.ToDate.Value.Date >= from))
            .OrderBy(a => a.Id).Take(1000).ToListAsync();
        var shiftIds = assigns.Select(a => a.ShiftId).Distinct().ToList();
        var shifts = shiftIds.Count == 0 ? new Dictionary<int, FaAttShift>()
            : await _db.FaAttShifts.AsNoTracking().Where(s => shiftIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id);
        foreach (var a in assigns)
        {
            shifts.TryGetValue(a.ShiftId, out var sh);
            ev.Add(new FaAttCalendarEventDto
            {
                Kind = "Shift",
                FromDate = a.FromDate.Date < from ? from : a.FromDate.Date,
                ToDate = a.ToDate == null || a.ToDate.Value.Date > to ? to : a.ToDate.Value.Date,
                EmployeeId = a.EmployeeId,
                EmployeeName = emps.TryGetValue(a.EmployeeId, out var n3) ? n3 : null,
                Title = "شیفت: " + (sh?.Name ?? "—"),
                Color = string.IsNullOrWhiteSpace(sh?.Color) ? "#64748b" : sh.Color,
                Status = 1
            });
        }

        var sessions = await _db.FaLmsSessions.AsNoTracking()
            .Where(s => s.SessionDate.Date >= from && s.SessionDate.Date <= to)
            .OrderBy(s => s.Id).Take(500).ToListAsync();
        if (sessions.Count > 0)
        {
            var courseIds = sessions.Select(s => s.CourseId).Distinct().ToList();
            var courseNames = await _db.FaLmsCourses.AsNoTracking()
                .Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title);
            var enrolls = await _db.FaLmsEnrollments.AsNoTracking()
                .Where(x => courseIds.Contains(x.CourseId) && empIds.Contains(x.EmployeeId)
                    && x.Status == FaLmsEnrollStatus.Approved)
                .OrderBy(x => x.Id).Take(2000).ToListAsync();
            foreach (var s in sessions)
                foreach (var g in enrolls.Where(x => x.CourseId == s.CourseId))
                    ev.Add(new FaAttCalendarEventDto
                    {
                        Kind = "Training", FromDate = s.SessionDate.Date, ToDate = s.SessionDate.Date,
                        EmployeeId = g.EmployeeId,
                        EmployeeName = emps.TryGetValue(g.EmployeeId, out var n4) ? n4 : null,
                        Title = "آموزش: " + (courseNames.TryGetValue(s.CourseId, out var cn) ? cn : "")
                            + (string.IsNullOrWhiteSpace(s.Topic) ? "" : $" — {s.Topic}"),
                        Color = "#9333ea",
                        Status = 1
                    });
        }

        var holidays = await _db.HrMainHolidays.AsNoTracking()
            .Where(h => h.HolidayDate.Date >= from && h.HolidayDate.Date <= to)
            .OrderBy(h => h.HolidayDate).ThenBy(h => h.Id).Take(100).ToListAsync();
        foreach (var h in holidays)
            ev.Add(new FaAttCalendarEventDto
            {
                Kind = "Holiday", FromDate = h.HolidayDate.Date, ToDate = h.HolidayDate.Date,
                Title = h.Name ?? "تعطیل رسمی", Color = "#dc2626", Status = 1
            });

        // صفحه‌بندی «کل Dataset ترکیبی» (همهٔ نوع‌های رویداد با هم)، نه یکی از منابع؛
        // منابع بالا با سقف‌های ایمنی محدود شده‌اند و این سقف‌ها مستقل از total و صفحه‌بندی‌اند.
        // ترتیب پایدار: تاریخ، نوع رویداد، پرسنل، عنوان.
        ev = ev.OrderBy(e => e.FromDate).ThenBy(e => e.Kind).ThenBy(e => e.EmployeeId ?? 0)
            .ThenBy(e => e.Title).ToList();
        if (pagination is { IsPaged: true })
        {
            pagination.SetTotal(ev.Count);
            if (pagination.Skip > 0) ev = ev.Skip(pagination.Skip).ToList();
            if (pagination.Take is > 0) ev = ev.Take(pagination.Take.Value).ToList();
        }
        return ev;
    }

    // ==================== تأیید/رد گروهی ====================

    public async Task<FaAttBatchResultDto> ManagerDecideBatchAsync(
        List<int> ids, bool approve, int byUserId, string byName, bool isHr)
    {
        var res = new FaAttBatchResultDto();
        foreach (var id in (ids ?? new()).Distinct().Take(200))
        {
            try { await ManagerDecideAsync(id, approve, byUserId, byName, isHr); res.Ok++; }
            catch (Exception ex) { res.Failed++; res.Errors.Add($"درخواست {id}: {ex.Message}"); }
        }
        if (res.Ok == 0 && res.Failed == 0) throw new InvalidOperationException("موردی انتخاب نشده است.");
        return res;
    }

    public async Task<FaAttBatchResultDto> HrDecideBatchAsync(
        List<int> ids, bool approve, int byUserId, string byName)
    {
        var res = new FaAttBatchResultDto();
        foreach (var id in (ids ?? new()).Distinct().Take(200))
        {
            try { await HrDecideAsync(id, approve, byUserId, byName); res.Ok++; }
            catch (Exception ex) { res.Failed++; res.Errors.Add($"درخواست {id}: {ex.Message}"); }
        }
        if (res.Ok == 0 && res.Failed == 0) throw new InvalidOperationException("موردی انتخاب نشده است.");
        return res;
    }

    public async Task<FaAttBatchResultDto> DecideMissionBatchAsync(
        List<int> ids, bool approve, int byUserId, string byName)
    {
        var res = new FaAttBatchResultDto();
        foreach (var id in (ids ?? new()).Distinct().Take(200))
        {
            try { await DecideMissionAsync(id, approve, byUserId, byName); res.Ok++; }
            catch (Exception ex) { res.Failed++; res.Errors.Add($"مأموریت {id}: {ex.Message}"); }
        }
        if (res.Ok == 0 && res.Failed == 0) throw new InvalidOperationException("موردی انتخاب نشده است.");
        return res;
    }
}
