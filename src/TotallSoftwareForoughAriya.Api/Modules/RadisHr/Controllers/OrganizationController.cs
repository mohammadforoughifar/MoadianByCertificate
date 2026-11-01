using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Shared.Calculations;
using RadisHr.Shared.Contracts;
using RadisHr.Shared.Models;
using Paging = Inventory.Api.Services.Paging;

namespace RadisHr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organization")]
public class OrganizationController : ControllerBase
{
    private readonly AppDbContext _db;
    public OrganizationController(AppDbContext db) => _db = db;

    // ───────── واحدها و پست‌های کاری ─────────
    [HttpGet("departments")]
    public async Task<ActionResult<List<Department>>> Departments([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.Departments.Include(d => d.Stations).AsNoTracking()
            .OrderBy(d => d.Ordinal), skip, take));

    [HttpGet("levels")]
    public ActionResult<string[]> Levels() => DbSeeder.ResponsibilityLevels;

    [Authorize(Roles = "hr,ceo")]
    [HttpPost("departments")]
    public async Task<ActionResult<IdResponse>> AddDepartment(Department department)
    {
        if (await _db.Departments.AnyAsync(d => d.Name == department.Name))
            return Conflict(new ApiMessage(false, "این واحد قبلاً ثبت شده است."));

        department.Id = 0;
        department.Uid = Guid.NewGuid().ToString("N");
        department.Ordinal = await _db.Departments.CountAsync();
        foreach (var s in department.Stations) { s.Id = 0; s.Uid = Guid.NewGuid().ToString("N"); }
        _db.Departments.Add(department);

        // هر واحد جدید، برنامهٔ کاری پیش‌فرض می‌گیرد
        if (!await _db.UnitSchedules.AnyAsync(u => u.Unit == department.Name))
            _db.UnitSchedules.Add(new UnitSchedule { Unit = department.Name });

        await _db.SaveChangesAsync();
        return new IdResponse(department.Id, "واحد ثبت شد.");
    }

    [Authorize(Roles = "hr,ceo")]
    [HttpPut("departments/{id:int}")]
    public async Task<ActionResult<ApiMessage>> UpdateDepartment(int id, Department input)
    {
        var department = await _db.Departments.Include(d => d.Stations).FirstOrDefaultAsync(d => d.Id == id);
        if (department == null) return NotFound(new ApiMessage(false, "واحد یافت نشد."));

        department.Name = input.Name;
        _db.WorkStations.RemoveRange(department.Stations);
        foreach (var s in input.Stations)
            _db.WorkStations.Add(new WorkStation
            {
                DepartmentId = id, Uid = Guid.NewGuid().ToString("N"), Name = s.Name
            });

        await _db.SaveChangesAsync();
        return new ApiMessage(true, "واحد به‌روزرسانی شد.");
    }

    [Authorize(Roles = "hr,ceo")]
    [HttpDelete("departments/{id:int}")]
    public async Task<ActionResult<ApiMessage>> DeleteDepartment(int id)
    {
        var department = await _db.Departments.FindAsync(id);
        if (department == null) return NotFound(new ApiMessage(false, "واحد یافت نشد."));

        if (await _db.Employees.AnyAsync(e => e.Unit == department.Name))
            return BadRequest(new ApiMessage(false, "این واحد دارای پرسنل است و حذف نمی‌شود."));

        _db.Departments.Remove(department);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "واحد حذف شد.");
    }

    /// <summary>ماتریس پرسنل: واحد ← پست کاری ← افراد</summary>
    [HttpGet("matrix")]
    public async Task<ActionResult<object>> Matrix([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var departments = await _db.Departments.Include(d => d.Stations).AsNoTracking()
            .OrderBy(d => d.Ordinal).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var employees = await _db.Employees.AsNoTracking().ToListAsync();

        return pagination.Result(departments.Select(d => new
        {
            unit = d.Name,
            stations = d.Stations.Select(s => new
            {
                station = s.Name,
                employees = employees
                    .Where(e => e.Unit == d.Name && e.WorkStation == s.Name)
                    .Select(e => new { e.Code, name = $"{e.First} {e.Last}", e.ResponsibilityLevel, e.PositionTitle })
                    .ToList()
            }).ToList(),
            unassigned = employees
                .Where(e => e.Unit == d.Name && !d.Stations.Select(s => s.Name).Contains(e.WorkStation))
                .Select(e => new { e.Code, name = $"{e.First} {e.Last}", e.ResponsibilityLevel })
                .ToList()
        }).ToList());
    }

    // ───────── برنامهٔ کاری واحدها ─────────
    [HttpGet("schedules")]
    public async Task<ActionResult<List<UnitSchedule>>> Schedules([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.UnitSchedules.AsNoTracking().OrderBy(s => s.Unit), skip, take));

    [Authorize(Roles = "hr,ceo")]
    [HttpPut("schedules")]
    public async Task<ActionResult<ApiMessage>> SaveSchedule(UnitSchedule input)
    {
        var schedule = await _db.UnitSchedules.FirstOrDefaultAsync(s => s.Unit == input.Unit);
        if (schedule == null)
        {
            input.Id = 0;
            _db.UnitSchedules.Add(input);
        }
        else
        {
            schedule.WorkStart = input.WorkStart;
            schedule.WorkEnd = input.WorkEnd;
            schedule.EntryGrace = input.EntryGrace;
            schedule.EarlyEntryGrace = input.EarlyEntryGrace;
            schedule.ExitGrace = input.ExitGrace;
            schedule.LateExitGrace = input.LateExitGrace;
            schedule.MaxLateWithoutLeave = input.MaxLateWithoutLeave;
            schedule.MaxEarlyWithoutLeave = input.MaxEarlyWithoutLeave;
            schedule.AbsenceStrategy = input.AbsenceStrategy;
        }
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "ساعات کاری ذخیره شد.");
    }

    // ───────── تقویم و تعطیلات ─────────
    [HttpGet("holidays")]
    public async Task<ActionResult<List<Holiday>>> Holidays([FromQuery] int? year, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.Holidays.AsNoTracking().AsQueryable();
        if (year.HasValue) query = query.Where(h => h.Date.StartsWith(year.Value.ToString()));
        return Ok(await Paging.ResultAsync(query.OrderBy(h => h.Date), skip, take));
    }

    [Authorize(Roles = "hr,ceo")]
    [HttpPost("holidays")]
    public async Task<ActionResult<ApiMessage>> AddHoliday(Holiday holiday)
    {
        if (await _db.Holidays.AnyAsync(h => h.Date == holiday.Date))
            return Conflict(new ApiMessage(false, "این تاریخ قبلاً ثبت شده است."));
        holiday.Id = 0;
        _db.Holidays.Add(holiday);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "روز تعطیل ثبت شد.");
    }

    [Authorize(Roles = "hr,ceo")]
    [HttpDelete("holidays/{id:int}")]
    public async Task<ActionResult<ApiMessage>> DeleteHoliday(int id)
    {
        var holiday = await _db.Holidays.FindAsync(id);
        if (holiday == null) return NotFound(new ApiMessage(false, "یافت نشد."));
        _db.Holidays.Remove(holiday);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "حذف شد.");
    }

    /// <summary>تولید خودکار جمعه‌های سال — همان دکمهٔ «افزودن جمعه‌ها»</summary>
    [Authorize(Roles = "hr,ceo")]
    [HttpPost("holidays/generate-fridays")]
    public async Task<ActionResult<ApiMessage>> GenerateFridays([FromQuery] int year)
    {
        var existing = (await _db.Holidays.Where(h => h.Date.StartsWith(year.ToString()))
            .Select(h => h.Date).ToListAsync()).ToHashSet();
        var added = 0;

        for (var month = 1; month <= 12; month++)
        {
            var days = PersianCalendarUtil.CalendarMonthDays(year, month);
            for (var day = 1; day <= days; day++)
            {
                if (PersianCalendarUtil.WeekdayIndex(year, month, day) != 6) continue;
                var key = PersianCalendarUtil.DateKey(year, month, day);
                if (existing.Contains(key)) continue;
                _db.Holidays.Add(new Holiday { Date = key, Title = "جمعه", Type = "friday" });
                existing.Add(key);
                added++;
            }
        }
        await _db.SaveChangesAsync();
        return new ApiMessage(true, $"{added} روز جمعه به تقویم افزوده شد.");
    }

    /// <summary>تقویم ماهانه برای نمایش شبکه‌ای</summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<object>> Calendar([FromQuery] int year, [FromQuery] int month)
    {
        var days = PersianCalendarUtil.CalendarMonthDays(year, month);
        var holidays = (await _db.Holidays.AsNoTracking()
                .Where(h => h.Date.StartsWith($"{year}/{month:00}")).ToListAsync())
            .ToDictionary(h => h.Date, h => h);

        var cells = Enumerable.Range(1, days).Select(day =>
        {
            var key = PersianCalendarUtil.DateKey(year, month, day);
            var weekday = PersianCalendarUtil.WeekdayIndex(year, month, day);
            holidays.TryGetValue(key, out var holiday);
            return new
            {
                date = key, day, weekday,
                isFriday = weekday == 6,
                isHoliday = holiday != null,
                title = holiday?.Title ?? "",
                type = holiday?.Type ?? ""
            };
        }).ToList();

        return new
        {
            year, month,
            monthName = PersianCalendarUtil.MonthNames[Math.Clamp(month - 1, 0, 11)],
            days,
            leadingBlanks = PersianCalendarUtil.WeekdayIndex(year, month, 1),
            cells
        };
    }
}
