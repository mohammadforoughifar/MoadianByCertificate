using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Api.Services;
using RadisHr.Shared.Calculations;
using RadisHr.Shared.Contracts;
using RadisHr.Shared.Models;
using Paging = Inventory.Api.Services.Paging;

namespace RadisHr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/attendance")]
public class AttendanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AttendanceService _engine;

    public AttendanceController(AppDbContext db, AttendanceService engine)
    {
        _db = db;
        _engine = engine;
    }

    [HttpGet("days")]
    public async Task<ActionResult<List<AttendanceDay>>> Days(
        [FromQuery] string? month, [FromQuery] string? code, [FromQuery] string? date, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.AttendanceDays.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(month)) query = query.Where(d => d.Month == month);
        if (!string.IsNullOrEmpty(code)) query = query.Where(d => d.Code == code);
        if (!string.IsNullOrEmpty(date)) query = query.Where(d => d.Date == date);
        return Ok((await Paging.QueryAsync(query.OrderBy(d => d.Date).ThenBy(d => d.Code), skip, take, 5000)).Result());
    }

    [HttpGet("months")]
    public async Task<ActionResult<List<string>>> Months([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.AttendanceDays.Select(d => d.Month).Distinct().OrderBy(m => m), skip, take));

    /// <summary>
    /// بارگذاری داده‌های دستگاه — سیاست append-only:
    /// رکورد تکراری با کلید {code}|{date} نادیده گرفته می‌شود، هرگز بازنویسی نمی‌شود.
    /// </summary>
    [Authorize(Roles = "hr,guard,ceo")]
    [HttpPost("import")]
    public async Task<ActionResult<AttendanceImportResult>> Import(
        [FromBody] List<AttendanceDay> rows, [FromQuery] string fileName = "ورودی دستی")
    {
        var existing = (await _db.AttendanceDays.Select(d => d.Key).ToListAsync()).ToHashSet();
        int inserted = 0, ignored = 0;

        foreach (var row in rows)
        {
            row.Key = $"{row.Code}|{row.Date}";
            row.Month = PersianCalendarUtil.MonthOf(row.Date);

            if (existing.Contains(row.Key)) { ignored++; continue; }

            row.Id = 0;
            _db.AttendanceDays.Add(row);
            existing.Add(row.Key);
            inserted++;
        }

        var dates = rows.Select(r => r.Date).Where(d => !string.IsNullOrEmpty(d)).OrderBy(d => d).ToList();
        var audit = new ImportAudit
        {
            FileName = fileName,
            Rows = rows.Count,
            Inserted = inserted,
            Ignored = ignored,
            From = dates.FirstOrDefault() ?? "",
            To = dates.LastOrDefault() ?? "",
            ImportedBy = User.Identity?.Name ?? "نامشخص"
        };
        _db.ImportAudits.Add(audit);
        await _db.SaveChangesAsync();

        return new AttendanceImportResult(rows.Count, inserted, ignored, audit.From, audit.To);
    }

    /// <summary>بارگذاری مستقیم فایل اکسل دستگاه (قالب بلوکی یا فهرست تردد)</summary>
    [Authorize(Roles = "hr,guard,ceo")]
    [HttpPost("import-workbook")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<AttendanceImportResult>> ImportWorkbook(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ApiMessage(false, "فایلی انتخاب نشده است."));

        var employees = await _db.Employees.AsNoTracking().ToListAsync();
        List<AttendanceDay> parsed;
        try
        {
            using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;
            parsed = new DeviceWorkbookParser(employees).Parse(buffer);
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiMessage(false, $"خواندن فایل ممکن نشد: {ex.Message}"));
        }

        if (parsed.Count == 0)
            return BadRequest(new ApiMessage(false, "هیچ رکورد معتبری در فایل یافت نشد."));

        return await Import(parsed, file.FileName);
    }

    [HttpGet("import-audits")]
    public async Task<ActionResult<List<ImportAudit>>> ImportAudits([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok((await Paging.QueryAsync(_db.ImportAudits.AsNoTracking().OrderByDescending(a => a.At), skip, take, 200)).Result());

    /// <summary>محاسبهٔ مجدد (idempotent) — تولید ردیف‌های حقوق از داده‌های حضور</summary>
    [Authorize(Roles = "hr,guard,ceo,accounting")]
    [HttpPost("recalculate")]
    public async Task<ActionResult<ApiMessage>> Recalculate([FromQuery] string? month)
    {
        var (months, rows) = await _engine.RecalculateAsync(month);
        return new ApiMessage(true, $"محاسبه انجام شد: {months} ماه، {rows} ردیف.");
    }

    // ───────── مرخصی و مأموریت ─────────
    [HttpGet("leaves")]
    public async Task<ActionResult<List<LeaveMission>>> Leaves([FromQuery] string? code, [FromQuery] string? month, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.LeaveMissions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(code)) query = query.Where(l => l.Employee == code);
        if (!string.IsNullOrEmpty(month)) query = query.Where(l => l.Date.StartsWith(month));
        return Ok((await Paging.QueryAsync(query.OrderByDescending(l => l.Date), skip, take, 1000)).Result());
    }

    [Authorize(Roles = "hr,guard,ceo")]
    [HttpPost("leaves")]
    public async Task<ActionResult<IdResponse>> AddLeave(LeaveMission record)
    {
        record.Id = 0;
        record.RegisteredBy = User.Identity?.Name ?? "نامشخص";
        _db.LeaveMissions.Add(record);
        await _db.SaveChangesAsync();
        return new IdResponse(record.Id, "مرخصی/مأموریت ثبت شد.");
    }

    [Authorize(Roles = "hr,guard,ceo")]
    [HttpDelete("leaves/{id:int}")]
    public async Task<ActionResult<ApiMessage>> DeleteLeave(int id)
    {
        var record = await _db.LeaveMissions.FindAsync(id);
        if (record == null) return NotFound(new ApiMessage(false, "رکورد یافت نشد."));
        _db.LeaveMissions.Remove(record);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "رکورد حذف شد.");
    }

    // ───────── تردد دستی (نیازمند تأیید) ─────────
    [HttpGet("punches")]
    public async Task<ActionResult<List<ManualPunch>>> Punches([FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.ManualPunches.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(p => p.Status == status);
        return Ok((await Paging.QueryAsync(query.OrderByDescending(p => p.CreatedAt), skip, take, 1000)).Result());
    }

    [Authorize(Roles = "guard,hr,ceo")]
    [HttpPost("punches")]
    public async Task<ActionResult<IdResponse>> AddPunch(ManualPunch punch)
    {
        punch.Id = 0;
        punch.Status = "pending";
        punch.RequestedBy = User.Identity?.Name ?? "نامشخص";
        _db.ManualPunches.Add(punch);
        await _db.SaveChangesAsync();
        return new IdResponse(punch.Id, "درخواست ثبت تردد دستی ارسال شد.");
    }

    [Authorize(Roles = "hr,ceo")]
    [HttpPost("punches/{id:int}/review")]
    public async Task<ActionResult<ApiMessage>> ReviewPunch(int id, [FromQuery] bool approve, [FromQuery] string? note)
    {
        var punch = await _db.ManualPunches.FindAsync(id);
        if (punch == null) return NotFound(new ApiMessage(false, "درخواست یافت نشد."));

        punch.Status = approve ? "approved" : "rejected";
        punch.ReviewedBy = User.Identity?.Name ?? "نامشخص";
        punch.ReviewNote = note;
        punch.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new ApiMessage(true, approve ? "تردد دستی تأیید شد." : "درخواست رد شد.");
    }

    // ───────── چرخهٔ نگهبانی ─────────
    [HttpGet("guard-cycles")]
    public async Task<ActionResult<List<GuardCycle>>> GuardCycles([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.GuardCycles.AsNoTracking(), skip, take));

    [Authorize(Roles = "guard,hr,ceo")]
    [HttpPost("guard-cycles")]
    public async Task<ActionResult<ApiMessage>> SetGuardCycle(GuardCycle cycle)
    {
        var existing = await _db.GuardCycles.FirstOrDefaultAsync(c => c.EmployeeCode == cycle.EmployeeCode);
        if (existing == null)
        {
            cycle.Id = 0;
            cycle.RegisteredBy = User.Identity?.Name ?? "نامشخص";
            _db.GuardCycles.Add(cycle);
        }
        else
        {
            existing.StartDate = cycle.StartDate;
            existing.StartShift = cycle.StartShift;
            existing.RegisteredBy = User.Identity?.Name ?? "نامشخص";
        }
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "چرخهٔ نگهبانی ثبت شد.");
    }

    /// <summary>شیفت نگهبان در یک تاریخ مشخص (چرخهٔ ۶ روزه)</summary>
    [HttpGet("guard-shift")]
    public async Task<ActionResult<GuardShiftInfo>> GuardShift([FromQuery] string code, [FromQuery] string date)
    {
        var cycle = await _db.GuardCycles.AsNoTracking().FirstOrDefaultAsync(c => c.EmployeeCode == code);
        var shift = AttendanceEngine.CycleFor(cycle, date);
        return shift == null ? NotFound(new ApiMessage(false, "چرخه‌ای تعریف نشده است.")) : shift;
    }

    /// <summary>گزارش روزانهٔ حضور — صفحهٔ productionDaily</summary>
    [HttpGet("daily-report")]
    public async Task<ActionResult<List<DailyReportRow>>> DailyReport([FromQuery] string date, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var month = PersianCalendarUtil.MonthOf(date);
        var dayRows = await _db.AttendanceDays.AsNoTracking().Where(d => d.Date == date).OrderBy(d => d.Code).ThenBy(d => d.Id).ToPageListAsync(pagination);
        var codes = dayRows.Select(d => d.Code).ToList();
        var monthRows = await _db.AttendanceDays.AsNoTracking().Where(d => d.Month == month && codes.Contains(d.Code)).ToListAsync();
        var monthByCode = monthRows.GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.ToList());

        return Ok(pagination.Result(dayRows.Select(d =>
        {
            var m = monthByCode.TryGetValue(d.Code, out var list) ? list : new List<AttendanceDay>();
            return new DailyReportRow(
                d.Code, d.Name,
                AttendanceEngine.Hm(d.Presence), d.Status,
                AttendanceEngine.Hm(d.Leave), AttendanceEngine.Hm(d.Shortfall), AttendanceEngine.Hm(d.Ot),
                AttendanceEngine.Hm(m.Sum(x => x.Presence)),
                AttendanceEngine.Hm(m.Sum(x => x.Leave)),
                AttendanceEngine.Hm(m.Sum(x => x.Shortfall)),
                AttendanceEngine.Hm(m.Sum(x => x.Ot)));
        }).OrderBy(r => r.Code).ToList()));
    }
}
