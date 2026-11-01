using System.Security.Claims;
using System.Text;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// حضور و غیاب پرسنل — ورود/خروج روزانه + مدیریت شیفت‌ها + گزارش کسری کار
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private const string Module = "Attendance";

    /// <summary>ساعت کاری استاندارد روزانه (دقیقه) — برای محاسبه کسری روزانه بدون شیفت</summary>
    private const int StandardWorkdayMinutes = 8 * 60;

    /// <summary>حداکثر تعداد جفت ورود/خروج در یک روز</summary>
    private const int MaxDailyPairs = 5;

    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly AttendanceRecalcService _recalc;
    private readonly AttendanceSecurityService _security;
    private readonly ForoughAryaHrUsers _fa;

    public AttendanceController(AppDbContext db, INotifyService notify, AttendanceRecalcService recalc, AttendanceSecurityService security, ForoughAryaHrUsers fa)
    {
        _db = db; _notify = notify; _recalc = recalc; _security = security; _fa = fa;
    }

    /// <summary>
    /// «منابع انسانی فروغ آریا»: در همهٔ فهرست‌ها، گزارش‌ها و کمبوهای مدیریتی فقط کاربرانی که
    /// هم فعال‌اند و هم به این ماژول دسترسی دارند نمایش داده می‌شوند.
    /// </summary>
    private async Task<List<int>> AllowedUsersAsync() => (await _fa.UserIdsAsync()).ToList();
    private Task<bool> UserAllowedAsync(int userId) => _fa.IsAllowedUserAsync(userId);
    private const string UserNotAllowed = "کاربر فعال نیست یا به «منابع انسانی فروغ آریا» دسترسی ندارد.";

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyName => User.FindFirstValue(ClaimTypes.Name) ?? "";
    private string? MyIp => HttpContext?.Connection?.RemoteIpAddress?.ToString();

    /// <summary>بررسی دسترسی RBAC (سازگاری با کاربران قدیمی بدون نقش RBAC).</summary>
    private async Task<bool> HasAsync(string action)
    {
        var hasRoles = await _db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive));
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy is "Operator" or "Accountant") return action is "SelfCheckin";
            return action is "SelfCheckin" && legacy == "Referrer";
        }
        return await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == Module && p.Action == action);
    }

    private bool? _isAdmin;
    private async Task<bool> IsAdminAsync()
    {
        if (_isAdmin.HasValue) return _isAdmin.Value;
        if (User.IsInRole("Admin")) { _isAdmin = true; return true; }
        _isAdmin = await HasAsync("ManageShifts") || await HasAsync("ViewAll") || await HasAsync("Report");
        return _isAdmin.Value;
    }

    // ================== شیفت‌ها ==================
    [HttpGet("shifts")]
    public async Task<IActionResult> GetShifts([FromQuery] int skip = 0, [FromQuery] int? take = null) => Ok(await Paging.ResultAsync(_db.ShiftGroups.OrderBy(s => s.StartTime).ThenBy(s => s.Id), skip, take));

    [HttpPost("shifts")]
    public async Task<IActionResult> SaveShift([FromBody] ShiftInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { message = "نام شیفت الزامی است." });

        if (!TimeSpan.TryParse(input.StartTime, out var st)) return BadRequest(new { message = "ساعت شروع نامعتبر است." });
        if (!TimeSpan.TryParse(input.EndTime, out var et)) return BadRequest(new { message = "ساعت پایان نامعتبر است." });

        // شیفت دوپاره: پنجره‌ی دوم اختیاری است ولی اگر تنظیم شود باید جفتِ کامل و معتبر باشد
        TimeSpan? st2 = null, et2 = null;
        var hasS2 = !string.IsNullOrWhiteSpace(input.StartTime2);
        var hasE2 = !string.IsNullOrWhiteSpace(input.EndTime2);
        if (hasS2 != hasE2)
            return BadRequest(new { message = "پنجره‌ی دومِ شیفت دوپاره را کامل (شروع و پایان) وارد کنید یا خالی بگذارید." });
        if (hasS2)
        {
            if (!TimeSpan.TryParse(input.StartTime2, out var s2v)) return BadRequest(new { message = "ساعت شروعِ پنجره‌ی دوم نامعتبر است." });
            if (!TimeSpan.TryParse(input.EndTime2, out var e2v)) return BadRequest(new { message = "ساعت پایانِ پنجره‌ی دوم نامعتبر است." });
            if (e2v <= s2v) return BadRequest(new { message = "پایانِ پنجره‌ی دوم باید بعد از شروعِ آن باشد." });
            if (s2v < et) return BadRequest(new { message = "شروعِ پنجره‌ی دوم باید بعد از پایانِ پنجره‌ی اول باشد." });
            st2 = s2v; et2 = e2v;
        }

        ShiftGroup sg;
        if (input.Id > 0)
        {
            sg = await _db.ShiftGroups.FindAsync(input.Id);
            if (sg == null) return NotFound(new { message = "شیفت یافت نشد." });
        }
        else
        {
            sg = new ShiftGroup();
            _db.ShiftGroups.Add(sg);
        }
        sg.Name = input.Name.Trim();
        sg.Description = input.Description;
        sg.StartTime = st;
        sg.EndTime = et;
        sg.StartTime2 = st2;
        sg.EndTime2 = et2;
        sg.GraceMinutes = Math.Max(0, input.GraceMinutes);
        sg.IncludeFriday = input.IncludeFriday;
        sg.IsActive = input.IsActive;
        sg.RequireExtraApproval = input.RequireExtraApproval;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { id = sg.Id });
    }

    [HttpDelete("shifts/{id:int}")]
    public async Task<IActionResult> DeleteShift(int id)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var sg = await _db.ShiftGroups.FindAsync(id);
        if (sg == null) return NotFound();
        if (await _db.Users.AnyAsync(u => u.ShiftGroupId == id) || await _db.AttendanceRecords.AnyAsync(a => a.ShiftGroupId == id))
            return BadRequest(new { message = "این شیفت به پرسنل یا رکوردی متصل است و قابل حذف نیست." });
        _db.ShiftGroups.Remove(sg);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }

    public class ShiftInput
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string StartTime { get; set; } = "08:00";
        public string EndTime { get; set; } = "16:30";
        /// <summary>شروعِ پنجره‌ی دومِ شیفت دوپاره (اختیاری — مثل 17:00)</summary>
        public string? StartTime2 { get; set; }
        /// <summary>پایانِ پنجره‌ی دومِ شیفت دوپاره (اختیاری — مثل 21:30)</summary>
        public string? EndTime2 { get; set; }
        public int GraceMinutes { get; set; } = 10;
        public bool IncludeFriday { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>مازادِ این شیفت «در انتظار تصمیم مدیر» ثبت شود؟ null = پیروی از تنظیم سراسری تقویم کاری</summary>
        public bool? RequireExtraApproval { get; set; }
    }

    // ================== تنظیمات تقویم کاری ==================

    /// <summary>خواندن تنظیمات تقویم کاری (رکورد یکتا) — در نبود، با مقادیر پیش‌فرض ساخته می‌شود.
    /// توجه: Id ستون identity است و نباید مقدار صریح داد (خطای IDENTITY_INSERT در SQL Server).</summary>
    private async Task<WorkCalendarSettings> GetSettingsAsync()
    {
        var s = await _db.WorkCalendarSettings.FirstOrDefaultAsync();
        if (s == null)
        {
            s = new WorkCalendarSettings();
            _db.WorkCalendarSettings.Add(s);
            await _db.SaveChangesAsync();
        }
        return s;
    }

    public class CalendarSettingsInput
    {
        public string DefaultStart { get; set; } = "08:00";
        public string DefaultEnd { get; set; } = "16:30";
        public int GraceMinutes { get; set; } = 10;
        /// <summary>بیت‌های روزهای تعطیل هفته: Sunday=1…Saturday=64 (جمعه=32)</summary>
        public int RestDayFlags { get; set; } = 32;
        public bool ApplyOfficialHolidays { get; set; } = true;
        /// <summary>ماندن بعد از پایان شیفت «در انتظار تصمیم مدیر» ثبت شود (نه کسری، نه اضافه‌کار خودکار)</summary>
        public bool RequireExtraApproval { get; set; } = true;
        /// <summary>ساعت شیفتِ اختصاصیِ کاربر بر ساعتِ تقویم کاری مقدم باشد</summary>
        public bool ShiftOverridesCalendarTimes { get; set; } = true;
    }

    [HttpGet("calendar-settings")]
    public async Task<IActionResult> GetCalendarSettings()
    {
        var s = await GetSettingsAsync();
        return Ok(new
        {
            s.DefaultStart,
            s.DefaultEnd,
            s.GraceMinutes,
            s.RestDayFlags,
            s.ApplyOfficialHolidays,
            s.RequireExtraApproval,
            s.ShiftOverridesCalendarTimes,
        });
    }

    [HttpPut("calendar-settings")]
    public async Task<IActionResult> SaveCalendarSettings([FromBody] CalendarSettingsInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        if (!TimeSpan.TryParse(input.DefaultStart, out var st)) return BadRequest(new { message = "ساعت شروع پیش‌فرض معتبر نیست." });
        if (!TimeSpan.TryParse(input.DefaultEnd, out var en)) return BadRequest(new { message = "ساعت پایان پیش‌فرض معتبر نیست." });
        if (input.RestDayFlags < 0 || input.RestDayFlags > 127) return BadRequest(new { message = "روزهای تعطیل هفته معتبر نیست." });
        if (input.GraceMinutes < 0 || input.GraceMinutes > 240) return BadRequest(new { message = "تاخیر مجاز باید بین ۰ تا ۲۴۰ دقیقه باشد." });

        var s = await GetSettingsAsync();
        s.DefaultStart = st;
        s.DefaultEnd = en;
        s.GraceMinutes = input.GraceMinutes;
        s.RestDayFlags = input.RestDayFlags;
        s.ApplyOfficialHolidays = input.ApplyOfficialHolidays;
        s.RequireExtraApproval = input.RequireExtraApproval;
        s.ShiftOverridesCalendarTimes = input.ShiftOverridesCalendarTimes;
        s.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }

    // ================== تعطیلات رسمی کشور ==================

    public class OfficialHolidayInput
    {
        public DateTime HolidayDate { get; set; }
        public string? Name { get; set; }
    }

    public class ImportOfficialHolidaysInput
    {
        /// <summary>سال شمسی موردنظر (مثلاً 1405)</summary>
        public int Year { get; set; }
        /// <summary>true = تعطیلات رسمیِ قبلیِ همین سال حذف و از نو وارد می‌شود (به‌روزرسانی)</summary>
        public bool Replace { get; set; }
    }

    /// <summary>تعطیلات رسمی یک سال شمسی (از جدول تعطیلات — فقط IsOfficial=true)</summary>
    [HttpGet("official-holidays")]
    public async Task<IActionResult> GetOfficialHolidays([FromQuery] int jy, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        DateTime from, to;
        if (jy > 0)
        {
            from = PersianDate.ToGregorian(jy, 1, 1);
            to = PersianDate.ToGregorian(jy + 1, 1, 1);
        }
        else
        {
            (var y, var m, _) = PersianDate.FromGregorian(DateTime.Now);
            from = PersianDate.ToGregorian(y, m, 1);
            to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);
        }

        var list = await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.IsOfficial && h.HolidayDate >= from && h.HolidayDate < to)
            .OrderBy(h => h.HolidayDate).ThenBy(x => x.Id).ToPageListAsync(pagination);
        return Ok(pagination.Result(list.Select(h => new { h.Id, h.HolidayDate, h.Name, h.CreatedByName, h.CreatedAt }).ToList()));
    }

    /// <summary>افزودن دستی یک تعطیل رسمی (یا اصلاح عنوان تاریخ تکراری)</summary>
    [HttpPost("official-holidays")]
    public async Task<IActionResult> AddOfficialHoliday([FromBody] OfficialHolidayInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        if (input == null || input.HolidayDate == default) return BadRequest(new { message = "تاریخ تعطیل نامعتبر است." });
        var name = string.IsNullOrWhiteSpace(input.Name) ? "تعطیل رسمی" : input.Name.Trim();
        if (name.Length > 100) name = name[..100];

        var date = input.HolidayDate.Date;
        var existing = await _db.CompanyHolidays.FirstOrDefaultAsync(h => h.HolidayDate.Date == date);
        if (existing != null)
        {
            existing.IsOfficial = true;
            existing.Name = name;
            await _db.SaveChangesAsync();
            await _notify.BroadcastChangedAsync("attendance");
            return Ok(new { id = existing.Id, updated = true });
        }

        var h = new CompanyHoliday
        {
            HolidayDate = date,
            Name = name,
            IsOfficial = true,
            CreatedByName = MyName,
            CreatedAt = DateTime.Now,
        };
        _db.CompanyHolidays.Add(h);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { id = h.Id });
    }

    /// <summary>
    /// ورود گروهی تعطیلات رسمی کشور برای یک سال شمسی از کاتالوگ (تعطیلات ثابت شمسی + تعطیلات قمریِ
    /// سال‌های دارای داده: ۱۴۰۴ و ۱۴۰۵). تاریخ‌های موجود رد می‌شوند مگر Replace=true.
    /// </summary>
    [HttpPost("official-holidays/import")]
    public async Task<IActionResult> ImportOfficialHolidays([FromBody] ImportOfficialHolidaysInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        if (input.Year is < 1300 or > 1500) return BadRequest(new { message = "سال شمسی معتبر نیست." });

        var from = PersianDate.ToGregorian(input.Year, 1, 1);
        var to = PersianDate.ToGregorian(input.Year + 1, 1, 1);
        var items = OfficialHolidayCatalog.GetForYear(input.Year);
        var hasLunar = OfficialHolidayCatalog.HasLunarData(input.Year);

        if (input.Replace)
        {
            var olds = await _db.CompanyHolidays.Where(h => h.IsOfficial && h.HolidayDate >= from && h.HolidayDate < to).ToListAsync();
            _db.CompanyHolidays.RemoveRange(olds);
            await _db.SaveChangesAsync();
        }

        var existingDates = (await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate < to).Select(h => h.HolidayDate).ToListAsync())
            .Select(d => d.Date).ToHashSet();

        var added = 0;
        var skipped = 0;
        foreach (var (m, d, name) in items)
        {
            var g = PersianDate.ToGregorian(input.Year, m, d);
            if (g == DateTime.MinValue) continue;
            if (existingDates.Contains(g)) { skipped++; continue; }
            _db.CompanyHolidays.Add(new CompanyHoliday
            {
                HolidayDate = g,
                Name = name,
                IsOfficial = true,
                CreatedByName = "سیستم (کاتالوگ تعطیلات)",
                CreatedAt = DateTime.Now,
            });
            existingDates.Add(g);
            added++;
        }
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true, added, skipped, hasLunarData = hasLunar });
    }

    [HttpDelete("official-holidays/{id:int}")]
    public async Task<IActionResult> DeleteOfficialHoliday(int id)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var h = await _db.CompanyHolidays.FirstOrDefaultAsync(x => x.Id == id);
        if (h == null) return NotFound(new { message = "تعطیل رسمی پیدا نشد." });
        _db.CompanyHolidays.Remove(h);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }

    // ================== تقویم کاری — نمای سالانه (۱۲ ماه) ==================

    /// <summary>
    /// تقویم کامل یک سال شمسی — برای هر ماه: همه‌ی روزها با قاعده‌ی حل‌شده + آمار ماه؛
    /// + خلاصه‌ی کل سال. مبنای نمای «۱۲ ماهِ یک سال» در صفحه‌ی تقویم کاری.
    /// </summary>
    [HttpGet("calendar-year")]
    public async Task<IActionResult> GetCalendarYear([FromQuery] int jy)
    {
        int y;
        if (jy > 0) y = jy;
        else (y, _, _) = PersianDate.FromGregorian(DateTime.Now);
        if (y is < 1300 or > 1500) return BadRequest(new { message = "سال شمسی معتبر نیست." });

        var from = PersianDate.ToGregorian(y, 1, 1);
        var to = PersianDate.ToGregorian(y + 1, 1, 1);

        var settings = await GetSettingsAsync();
        var calDays = await _db.WorkCalendarDays.AsNoTracking()
            .Where(d => d.Date >= from && d.Date < to).ToDictionaryAsync(d => d.Date.Date);
        var holidays = await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate < to).ToDictionaryAsync(h => h.HolidayDate.Date);

        var months = new List<object>();
        int totalWorkDays = 0, totalOfficial = 0, totalCompanyOff = 0, totalRestDays = 0, totalCustom = 0;
        double totalWorkMinutes = 0;

        for (var m = 1; m <= 12; m++)
        {
            var mFrom = m == 1 ? from : PersianDate.ToGregorian(y, m, 1);
            var mTo = m == 12 ? to : PersianDate.ToGregorian(y, m + 1, 1);

            var days = new List<object>();
            int workDays = 0, offDays = 0, officialDays = 0, companyDays = 0, customDays = 0;
            double workMinutes = 0;

            for (var d = mFrom; d < mTo; d = d.AddDays(1))
            {
                var date = d.Date;
                calDays.TryGetValue(date, out var cal);
                holidays.TryGetValue(date, out var hol);
                var rule = WorkRules.Resolve(date, null, cal, hol, settings);

                if (rule.IsWorkday) { workDays++; workMinutes += WorkRules.ShiftDuration(rule).TotalMinutes; }
                else
                {
                    offDays++;
                    if (rule.Source == "Holiday")
                    {
                        if (rule.IsOfficial) officialDays++;
                        else companyDays++;
                    }
                    else if (cal == null) totalRestDays++;
                }
                if (cal != null) customDays++;

                days.Add(new
                {
                    date,
                    isToday = date == DateTime.Today,
                    rule.IsWorkday,
                    start = Tm(rule.Start),
                    end = Tm(rule.End),
                    graceMinutes = rule.GraceMinutes,
                    overtimeHours = rule.OvertimeHours,
                    overtimeMode = rule.OvertimeMode,
                    overtimeStart = rule.OvertimeStart.HasValue ? Tm(rule.OvertimeStart.Value) : null,
                    overtimeEnd = rule.OvertimeEnd.HasValue ? Tm(rule.OvertimeEnd.Value) : null,
                    rule.Source,
                    rule.IsOfficial,
                    holidayName = rule.Source == "Holiday" ? rule.Note : null,
                    note = rule.Source == "Calendar" ? rule.Note : null,
                    hasRule = cal != null,
                });
            }

            totalWorkDays += workDays;
            totalCustom += customDays;
            totalOfficial += ruleCount(holidays, mFrom, mTo, officialOnly: true);
            totalCompanyOff += ruleCount(holidays, mFrom, mTo, officialOnly: false);
            totalWorkMinutes += workMinutes;

            months.Add(new
            {
                year = y,
                month = m,
                monthName = PersianDate.MonthName(m),
                from = mFrom,
                to = mTo,
                firstDayColumn = PersianDate.FirstDayColumn(y, m),
                daysInMonth = PersianDate.DaysInMonth(y, m),
                workDays,
                offDays,
                officialDays,
                companyDays,
                customDays,
                workHours = Math.Round(workMinutes / 60, 1),
                days,
            });
        }

        return Ok(new
        {
            year = y,
            settings = new
            {
                settings.DefaultStart,
                settings.DefaultEnd,
                settings.GraceMinutes,
                settings.RestDayFlags,
                settings.ApplyOfficialHolidays,
            },
            summary = new
            {
                totalWorkDays,
                totalOfficialHolidays = totalOfficial,
                totalCompanyHolidays = totalCompanyOff,
                totalRestDays,
                totalCustomDays = totalCustom,
                totalWorkHours = Math.Round(totalWorkMinutes / 60, 1),
                hasLunarCatalog = OfficialHolidayCatalog.HasLunarData(y),
            },
            months,
        });

        static int ruleCount(Dictionary<DateTime, CompanyHoliday> hol, DateTime f, DateTime t, bool officialOnly)
            => hol.Values.Count(h => h.HolidayDate.Date >= f && h.HolidayDate.Date < t && h.IsOfficial == officialOnly);
    }

    /// <summary>قالب‌بندی ساعت HH:mm</summary>
    private static string Tm(TimeSpan t) => $"{t.Hours:D2}:{t.Minutes:D2}";

    // ================== تقویم کاری (روزبه‌روز ماه) ==================

    public class CalendarDayInput
    {
        public DateTime Date { get; set; }
        public bool IsWorkday { get; set; } = true;
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public int GraceMinutes { get; set; }
        public double OvertimeHours { get; set; }
        public string? Note { get; set; }
        /// <summary>حالت اضافه‌کاری: 0=بدون | 1=بازه زمانی | 2=کل روز | 3=سقف ساعتی</summary>
        public int OvertimeMode { get; set; }
        public string? OvertimeStart { get; set; }
        public string? OvertimeEnd { get; set; }
        /// <summary>true = این روز را در تقویم ذخیره/به‌روزرسانی کن — false = به حالت پیش‌فرض برگردان (ردیف را حذف کن)</summary>
        public bool HasRule { get; set; } = true;
    }

    public class CalendarMonthInput
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public List<CalendarDayInput> Days { get; set; } = new();
    }

    [HttpGet("calendar")]
    public async Task<IActionResult> GetCalendar([FromQuery] int? jy, [FromQuery] int? jm)
    {
        int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        var from = PersianDate.ToGregorian(y, m, 1);
        var to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);

        var settings = await GetSettingsAsync();
        var user = await _db.Users.Include(u => u.ShiftGroup).AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        var calDays = await _db.WorkCalendarDays.AsNoTracking()
            .Where(d => d.Date >= from && d.Date < to).ToDictionaryAsync(d => d.Date.Date);
        var holidays = await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate < to).ToDictionaryAsync(h => h.HolidayDate.Date);

        var days = new List<object>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            var date = d.Date;
            calDays.TryGetValue(date, out var cal);
            holidays.TryGetValue(date, out var hol);
            var rule = WorkRules.Resolve(date, user?.ShiftGroup, cal, hol, settings);
            days.Add(new
            {
                date,
                isToday = date == DateTime.Today,
                rule.IsWorkday,
                start = Tm(rule.Start),
                end = Tm(rule.End),
                rule.GraceMinutes,
                rule.OvertimeHours,
                overtimeMode = rule.OvertimeMode,
                overtimeStart = rule.OvertimeStart.HasValue ? Tm(rule.OvertimeStart.Value) : null,
                overtimeEnd = rule.OvertimeEnd.HasValue ? Tm(rule.OvertimeEnd.Value) : null,
                rule.Source,
                rule.IsOfficial,
                rule.Note,
                hasRule = cal != null,
            });
        }
        return Ok(new { year = y, month = m, from, to, days });
    }

    [HttpPut("calendar")]
    public async Task<IActionResult> SaveCalendar([FromBody] CalendarMonthInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        if (input == null || input.Days == null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });

        foreach (var d in input.Days)
        {
            bool stOk = TimeSpan.TryParse(d.StartTime, out var st);
            bool enOk = TimeSpan.TryParse(d.EndTime, out var en);

            if (!d.HasRule)
            {
                // برگشت به حالت پیش‌فرض: ردیف تقویم را حذف کن
                d.StartTime = null; d.EndTime = null;
                var existingOff = await _db.WorkCalendarDays.FirstOrDefaultAsync(x => x.Date.Date == d.Date.Date);
                if (existingOff != null)
                {
                    _db.WorkCalendarDays.Remove(existingOff);
                    await _db.SaveChangesAsync();
                }
                continue;
            }

            if (d.OvertimeHours < 0 || d.OvertimeHours > 24)
                return BadRequest(new { message = "سقف ساعتیِ اضافه‌کاری معتبر نیست (۰ تا ۲۴)." });
            if (d.OvertimeMode < WorkRules.OT_None || d.OvertimeMode > WorkRules.OT_HourCap)
                return BadRequest(new { message = "حالت اضافه‌کاری معتبر نیست." });
            if (d.IsWorkday && (!stOk || !enOk))
                return BadRequest(new { message = $"ساعت شروع/پایان روز {PersianDate.ToShort(d.Date.Date)} معتبر نیست." });
            // شیفت شب (ساعت پایان قبل از ساعت شروع = عبور از نیمه‌شب) مجاز است

            TimeSpan? otStart = null, otEnd = null;
            if (d.OvertimeMode == WorkRules.OT_Window)
            {
                bool o1 = TimeSpan.TryParse(d.OvertimeStart, out var ost);
                bool o2 = TimeSpan.TryParse(d.OvertimeEnd, out var oen);
                if (!o1 || !o2)
                    return BadRequest(new { message = $"ساعت‌های بازه‌ی اضافه‌کاری روز {PersianDate.ToShort(d.Date.Date)} معتبر نیست." });
                otStart = ost;
                otEnd = oen;
                // بازه‌ی اضافه‌کاری می‌تواند از نیمه‌شب عبور کند (پایان قبل از شروع = شیفت شب)
            }

            var existing = await _db.WorkCalendarDays.FirstOrDefaultAsync(x => x.Date.Date == d.Date.Date);

            if (existing == null)
            {
                _db.WorkCalendarDays.Add(new WorkCalendarDay
                {
                    Date = d.Date.Date,
                    IsWorkday = d.IsWorkday,
                    StartTime = TimeSpan.TryParse(d.StartTime, out var s2) ? s2 : null,
                    EndTime = TimeSpan.TryParse(d.EndTime, out var e2) ? e2 : null,
                    GraceMinutes = d.GraceMinutes,
                    OvertimeHours = d.OvertimeMode == WorkRules.OT_HourCap ? d.OvertimeHours : 0,
                    OvertimeMode = d.OvertimeMode,
                    OvertimeStart = otStart,
                    OvertimeEnd = otEnd,
                    Note = d.Note,
                });
            }
            else
            {
                existing.IsWorkday = d.IsWorkday;
                existing.StartTime = TimeSpan.TryParse(d.StartTime, out var s3) ? s3 : null;
                existing.EndTime = TimeSpan.TryParse(d.EndTime, out var e3) ? e3 : null;
                existing.GraceMinutes = d.GraceMinutes;
                existing.OvertimeHours = d.OvertimeMode == WorkRules.OT_HourCap ? d.OvertimeHours : 0;
                existing.OvertimeMode = d.OvertimeMode;
                existing.OvertimeStart = otStart;
                existing.OvertimeEnd = otEnd;
                existing.Note = d.Note;
                existing.UpdatedAt = DateTime.Now;
            }
        }

        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true, saved = input.Days.Count });
    }

    // ================== تخصیص شیفت به پرسنل ==================
    [HttpPost("assign-shift")]
    public async Task<IActionResult> AssignShift([FromBody] AssignShiftInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var user = await _db.Users.FindAsync(input.UserId);
        if (user == null) return NotFound(new { message = "کاربر یافت نشد." });
        if (!await UserAllowedAsync(input.UserId)) return BadRequest(new { message = UserNotAllowed });
        if (input.ShiftGroupId.HasValue && !await _db.ShiftGroups.AnyAsync(s => s.Id == input.ShiftGroupId))
            return BadRequest(new { message = "شیفت معتبر نیست." });
        user.ShiftGroupId = input.ShiftGroupId;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }
    public class AssignShiftInput { public int UserId { get; set; } public int? ShiftGroupId { get; set; } }

    // ================== لیست پرسنل ==================
    [HttpGet("personnel")]
    public async Task<IActionResult> GetPersonnel([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var canManage = await HasAsync("ManageShifts") || User.IsInRole("Admin");
        var canView = await IsAdminAsync();
        if (!canManage && !canView) return Forbid();
        var allowedU = await AllowedUsersAsync();
        var list = await _db.Users.Where(u => u.IsActive && allowedU.Contains(u.Id)).OrderBy(u => u.Username).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var shifts = await _db.ShiftGroups.ToDictionaryAsync(s => s.Id);
        return Ok(pagination.Result(list.Select(u => new
        {
            u.Id,
            u.Username,
            FullName = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim(),
            u.ShiftGroupId,
            ShiftName = u.ShiftGroupId.HasValue && shifts.TryGetValue(u.ShiftGroupId.Value, out var s) ? s.Name : null,
        })));
    }

    // ================== شیفت پیش‌فرض کاربر جاری ==================
    [HttpGet("my-shift")]
    public async Task<IActionResult> MyShift()
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        var user = await _db.Users.Include(u => u.ShiftGroup).FirstOrDefaultAsync(u => u.Id == MyUserId);
        if (user?.ShiftGroup == null) return Ok(null);
        var sg = user.ShiftGroup;
        return Ok(new { sg.Id, sg.Name, sg.Description, sg.StartTime, sg.EndTime, sg.GraceMinutes, sg.IncludeFriday });
    }

    // ================== کمک‌محاسبه کسری کار ==================
    private static int ScheduledMinutes(ShiftGroup? sg)
    {
        if (sg == null) return StandardWorkdayMinutes;
        var m = (int)(sg.EndTime - sg.StartTime).TotalMinutes;
        return m > 0 ? m : StandardWorkdayMinutes;
    }

    /// <summary>
    /// محاسبه و بروزرسانی فیلدهای محاسبه‌شده رکورد (کارکرد، تاخیر، تعجیل، کسری).
    /// rule = قاعده‌ی کاری روز (تقویم کاری > تعطیلی شرکتی > شیفت > پیش‌فرض).
    /// hasDailyLeave = مرخصی «روزانه» تاییدشده برای کل روز (مرخصی ساعتی از طریق CoveredGapMinutes کسر می‌شود).
    /// </summary>
    private static void RecalcRecord(AttendanceRecord rec, WorkDayRule rule, bool hasDailyLeave)
    {
        // مرخصی روزانه تاییدشده دارد → کسری صفر و وضعیت روز مرخصی است
        if (hasDailyLeave)
        {
            rec.DeficitMinutes = 0;
            if (string.IsNullOrEmpty(rec.FinalStatus) || rec.FinalStatus == "Present")
                rec.FinalStatus = "LeaveDay";
            return;
        }

        // تعطیل/جمعه → موظفیت روزانه ندارد، کسری صفر (تردد غیرمجاز جداگانه ثبت شده)
        if (!rule.IsWorkday)
        {
            rec.DeficitMinutes = 0;
            if (string.IsNullOrEmpty(rec.FinalStatus)) rec.FinalStatus = "Present";
            return;
        }

        // کسری = موظفی - کارکرد - غیبتِ پوشش‌شده با مرخصی/ماموریت ساعتی تاییدشده
        int deficit = AttendanceMath.ScheduledMinutes(rule) - rec.WorkMinutes - rec.CoveredGapMinutes;
        rec.DeficitMinutes = Math.Max(0, deficit);

        // تعیین FinalStatus درصورتی که به طور صریح تنظیم نشده
        if (string.IsNullOrEmpty(rec.FinalStatus))
        {
            if (!rec.EnterAt.HasValue && !rec.ExitAt.HasValue)
                rec.FinalStatus = "Absent";
            else
                rec.FinalStatus = "Present";
        }
    }

    /// <summary>آیا در این روز مرخصی تاییدشده (روزانه/ساعتی) دارد؟ (برای نمایش «مرخصی دارید»)</summary>
    private async Task<bool> HasApprovedLeaveAsync(int userId, DateTime date)
    {
        var leaves = await _db.LeaveRequests
            .Where(l => l.RequesterUserId == userId && l.Status == "Approved"
                        && (l.Type == "Daily" || l.Type == "Hourly"))
            .ToListAsync();
        foreach (var l in leaves)
        {
            if (l.Type == "Daily" && date >= l.StartDate.Date && date <= l.EndDate.Date) return true;
            if (l.Type == "Hourly" && l.StartDate.Date == date) return true;
        }
        return false;
    }

    /// <summary>آیا در این روز مرخصی «روزانه» تاییدشده دارد؟ (وضعیت کل روز = مرخصی)</summary>
    private async Task<bool> HasDailyLeaveAsync(int userId, DateTime date)
    {
        return await _db.LeaveRequests
            .AnyAsync(l => l.RequesterUserId == userId && l.Status == "Approved" && l.Type == "Daily"
                           && l.StartDate.Date <= date.Date && l.EndDate.Date >= date.Date);
    }

    /// <summary>درخواست‌های مرخصی/ماموریت ساعتی تاییدشده‌ی کاربر برای آن روز (کاندیداهای پوشش بازه‌ی غیبت).</summary>
    private async Task<List<LeaveRequest>> HourlyCoversAsync(int userId, DateTime date)
    {
        return await _db.LeaveRequests
            .Where(l => l.RequesterUserId == userId
                        && l.Status == "Approved"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate == date.Date
                        && l.StartTime != null
                        && l.EndTime != null)
            .ToListAsync();
    }

    /// <summary>درخواستی پیدا می‌کند که کل بازه‌ی [gapStart, gapEnd] را پوشش دهد (وگرنه null).</summary>
    private static LeaveRequest? FindCoveringLeave(List<LeaveRequest> covers, DateTime gapStart, DateTime gapEnd)
    {
        var gs = gapStart.TimeOfDay;
        var ge = gapEnd.TimeOfDay;
        return covers.FirstOrDefault(l => l.StartTime <= gs && l.EndTime >= ge);
    }

    /// <summary>
    /// بازسازی رکورد تجمیعی روز از روی بازه‌های ورود/خروج (کارکرد، غیبت پوشش‌شده، تاخیر، تعجیل، کسری).
    /// </summary>
    /// <summary>
    /// تجمیع رکورد روزانه از روی بازه‌ها بر اساس قاعده‌ی کاری روز (تقویم کاری > شیفت > پیش‌فرض).
    /// </summary>
    private async Task AggregateAsync(AttendanceRecord rec, List<AttendanceSegment> segs, WorkDayRule rule, DateTime asOf)
    {
        var hourlies = await HourlyCoversAsync(rec.UserId, rec.WorkDate);
        var hasDaily = await HasDailyLeaveAsync(rec.UserId, rec.WorkDate);
        AttendanceMath.Recompute(rec, segs, rule, hourlies, hasDaily, asOf);
    }

    /// <summary>حل‌شدن قاعده‌ی کاری یک روز برای یک کاربر (تقویم کاری > تعطیلی شرکتی > شیفت > پیش‌فرض)</summary>
    private async Task<WorkDayRule> ResolveRuleAsync(int userId, DateTime date, ShiftGroup? shift)
    {
        var cal = await _db.WorkCalendarDays.AsNoTracking().FirstOrDefaultAsync(d => d.Date.Date == date.Date);
        var hol = await _db.CompanyHolidays.AsNoTracking().FirstOrDefaultAsync(h => h.HolidayDate.Date == date.Date);
        var settings = await GetSettingsAsync();
        return WorkRules.Resolve(date, shift, cal, hol, settings);
    }

    /// <summary>
    /// بررسی پوشش بازه‌ی غیبتِ بازه‌ی داده‌شده و به‌روزرسانی فیلدهای مربوطه‌ی آن بازه.
    /// returns: آیا پوشش کامل دارد؟
    /// </summary>
    private async Task<bool> EvaluateGapAsync(AttendanceSegment seg, DateTime gapEnd)
    {
        if (!seg.ExitAt.HasValue) return false;
        var covers = await HourlyCoversAsync(seg.UserId, seg.WorkDate);
        var cover = FindCoveringLeave(covers, seg.ExitAt.Value, gapEnd);
        seg.ExitCovered = cover != null;
        seg.LinkedLeaveRequestId = cover?.Id;
        seg.LinkedLeaveNumber = cover?.Number;
        return cover != null;
    }

    /// <summary>پوشش موقت هنگام خروج: آیا درخواست تاییدشده‌ای «فعال» است که لحظه‌ی خروج را داخل خودش داشته باشد؟</summary>
    private async Task<bool> IsCoveredAtAsync(int userId, DateTime date, DateTime at)
    {
        var covers = await HourlyCoversAsync(userId, date);
        var t = at.TimeOfDay;
        return covers.Any(l => l.StartTime <= t && l.EndTime >= t);
    }

    // ================== ورود ==================
    [HttpPost("check-in")]
    public async Task<IActionResult> CheckIn([FromBody] CheckInInput? input)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        var today = DateTime.Today;
        var now = DateTime.Now;
        var user = await _db.Users.Include(u => u.ShiftGroup).FirstOrDefaultAsync(u => u.Id == MyUserId);
        if (user == null) return Unauthorized();

        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .FirstOrDefaultAsync(a => a.UserId == MyUserId && a.WorkDate == today);
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == MyUserId && s.WorkDate == today)
            .OrderBy(s => s.Seq)
            .ToListAsync();

        var last = segs.LastOrDefault();
        if (last != null && !last.ExitAt.HasValue)
            return BadRequest(new { message = "شما در حال حاضر در محل حضور دارید. ابتدا خروج بزنید." });
        if (segs.Count >= MaxDailyPairs)
            return BadRequest(new { message = $"حداکثر تعداد ورود/خروج در روز ({Fa.Digits(MaxDailyPairs)} بار) تمام شده است." });

        // شیفت شب: اگر ورودِ دیروز هنوز باز باشد و الان هنوز در محدوده‌ی همان شیفت باشیم، ورود جدید ممکن نیست
        var yst = today.AddDays(-1);
        var yRecIn = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.UserId == MyUserId && a.WorkDate == yst);
        if (yRecIn != null)
        {
            var yOpenIn = await _db.AttendanceSegments
                .Where(s => s.UserId == MyUserId && s.WorkDate == yst)
                .OrderByDescending(s => s.Seq)
                .FirstOrDefaultAsync(s => !s.ExitAt.HasValue);
            if (yOpenIn?.EnterAt != null)
            {
                var yRuleIn = await ResolveRuleAsync(MyUserId, yst, yRecIn.ShiftGroup);
                if (now < WorkRules.ShiftEnd(yst, yRuleIn).AddMinutes(yRuleIn.GraceMinutes + 30))
                    return BadRequest(new { message = "شما در شیفتِ دیروز (شیفت شب) هنوز در محل هستید. ابتدا خروج بزنید." });
            }
        }

        ShiftGroup? shift = null;
        if (input?.ShiftGroupId is > 0)
            shift = await _db.ShiftGroups.FindAsync(input.ShiftGroupId) ?? user.ShiftGroup;
        else
            shift = rec?.ShiftGroup ?? user.ShiftGroup;

        // قاعده‌ی کاری روز (تقویم کاری > تعطیلی شرکتی > شیفت > پیش‌فرض)
        var ruleIn = await ResolveRuleAsync(MyUserId, today, shift);

        var seq = segs.Count + 1;
        var seg = new AttendanceSegment
        {
            UserId = MyUserId,
            UserName = string.IsNullOrWhiteSpace(user.FirstName) ? user.Username : $"{user.FirstName} {user.LastName}".Trim(),
            WorkDate = today,
            Seq = seq,
            EnterAt = now,
            EnterIp = MyIp,
            EnterDevice = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? (ua.Length > 250 ? ua[..250] : ua) : null,
            Note = input?.Note
        };

        // ارزیابی ورود بر اساس قاعده‌ی روز
        // (پایانِ مؤثرِ روز = پایانِ آخرین پنجره — در شیفت دوپاره مثل «۸–۱۳ + ۱۷–۲۱:۳۰» یعنی ۲۱:۳۰)
        var dayEndIn = WorkRules.ShiftEnd(today, ruleIn).AddMinutes(ruleIn.GraceMinutes);
        if (!ruleIn.IsWorkday && !WorkRules.OvertimeAllowedAt(now, today, ruleIn))
        {
            // حضور در تعطیل/جمعه بدون اضافه‌کاری مجاز = تردد غیرمجاز
            // (اضافه‌کاریِ «کلِ روز»، یا «بازه‌ی زمانی» در صورت ورودِ داخلِ بازه، یا «سقفِ ساعتی» مجاز است)
            seg.IsUnauthorized = true;
            seg.EnterStatus = seq == 1 ? "Unauthorized" : "Return";
        }
        else if (ruleIn.IsWorkday && ruleIn.OvertimeMode != WorkRules.OT_WholeDay
                 && now > dayEndIn && !WorkRules.InOvertimeWindow(now, today, ruleIn)
                 && !WorkRules.NeedsDecision(ruleIn))
        {
            // ورود بعد از پایان کامل روز = تردد غیرمجاز
            // (مگر اضافه‌کاریِ «کلِ روز» باشد، لحظه‌ی ورود داخلِ بازه‌ی اضافه‌کاری باشد،
            //  یا مازاد «در انتظار تصمیم» فعال باشد — آن‌وقت تصمیم با مدیر است، نه تردد قطعی)
            seg.IsUnauthorized = true;
            seg.EnterStatus = seq == 1 ? "Unauthorized" : "Return";
        }
        else if (seq == 1 && ruleIn.IsWorkday && ruleIn.OvertimeMode != WorkRules.OT_WholeDay)
        {
            // تاخیر فقط برای اولین ورود روز کاری محاسبه می‌شود
            var scheduledEnter = today.Add(ruleIn.Start);
            var diff = (int)(now - scheduledEnter).TotalMinutes;
            if (diff > ruleIn.GraceMinutes)
            {
                seg.LateMinutes = diff - ruleIn.GraceMinutes;
                seg.EnterStatus = "Late";
            }
            else
            {
                seg.EnterStatus = "OnTime";
            }
        }
        else
        {
            seg.EnterStatus = seq == 1 ? "OnTime" : "Return";
        }

        // بازه‌ی غیبتِ قبل (خروج قبلی تا اکنون) — حالا که کامل شد، پوشش آن را قطعی می‌کنیم
        if (last != null && last.ExitAt.HasValue)
            await EvaluateGapAsync(last, now);

        if (rec == null)
        {
            rec = new AttendanceRecord
            {
                WorkDate = today,
                UserId = MyUserId,
                UserName = seg.UserName,
                ShiftGroupId = shift?.Id,
                FinalStatus = "Present",
            };
            _db.AttendanceRecords.Add(rec);
        }
        else
        {
            if (shift != null) rec.ShiftGroupId = shift.Id;
            rec.UpdatedAt = now;
        }

        _db.AttendanceSegments.Add(seg);
        segs.Add(seg);
        await AggregateAsync(rec, segs, ruleIn, now);
        await _db.SaveChangesAsync();

        if (seq == 1 && seg.LateMinutes > 5)
        {
            try
            {
                var hrIds = await GetAttendanceManagerIds();
                await _notify.SendManyAsync(hrIds,
                    "تاخیر در ورود",
                    $"{rec.UserName} با {Fa.Digits(seg.LateMinutes)} دقیقه تاخیر در {now:HH:mm} وارد شد.",
                    rec.UserName, "حضور و غیاب", "/attendance-admin");
            }
            catch { }
        }

        // ---------- تشخیص دستگاه جدید: اگر این UA قبلاً برای کاربر ثبت نشده باشد، به مدیران پیام می‌رود ----------
        try
        {
            var uaNow = Request.Headers.UserAgent.ToString();
            if (!string.IsNullOrWhiteSpace(uaNow))
            {
                var hasHistory = await _db.AttendanceSegments.AsNoTracking()
                    .AnyAsync(s => s.UserId == MyUserId && s.Id != seg.Id && s.EnterDevice != null && s.EnterDevice != "");
                var seenBefore = await _db.AttendanceSegments.AsNoTracking()
                    .AnyAsync(s => s.UserId == MyUserId && s.Id != seg.Id && s.EnterDevice == uaNow);
                if (hasHistory && !seenBefore)
                {
                    var adminIds = await GetAttendanceManagerIds();
                    var deviceShort = uaNow.Length > 90 ? uaNow[..90] : uaNow;
                    await _notify.SendManyAsync(adminIds,
                        "ورود با دستگاه جدید",
                        $"{rec.UserName} با دستگاه جدیدی وارد شد.\nدستگاه: {deviceShort}\nIP: {MyIp}\nزمان: {PersianDate.ToShortWithTime(now)}\n— لطفاً بررسی کنید.",
                        rec.UserName, "حضور و غیاب", "/attendance-admin");
                }
            }
        }
        catch { }

        await _notify.BroadcastChangedAsync("attendance");

        // ---------- امنیت: دستگاه جدید / دستگاه مشترک / خارج از محدوده ----------
        try
        {
            var adminIds = await GetAttendanceManagerIds();
            await _security.ValidateAsync(
                MyUserId, rec.UserName,
                input?.DeviceId, MyIp, Request.Headers.UserAgent.ToString(),
                input?.Lat, input?.Lng, adminIds);
        }
        catch { /* امنیت ثانویه است؛ خطا در آن مانع ثبت حضور نمی‌شود */ }

        return Ok(new { ok = true, record = Map(rec, segs) });
    }
    public class CheckInInput
    {
        public int? ShiftGroupId { get; set; }
        public string? Note { get; set; }
        /// <summary>شناسه یکتای دستگاه (از localStorage مرورگر)</summary>
        public string? DeviceId { get; set; }
        /// <summary>عرض جغرافیایی (اختیاری — از Geolocation API)</summary>
        public double? Lat { get; set; }
        /// <summary>طول جغرافیایی (اختیاری — از Geolocation API)</summary>
        public double? Lng { get; set; }
    }

    // ================== خروج ==================
    [HttpPost("check-out")]
    public async Task<IActionResult> CheckOut([FromBody] CheckOutInput? input)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        var today = DateTime.Today;
        var now = DateTime.Now;

        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup).FirstOrDefaultAsync(a => a.UserId == MyUserId && a.WorkDate == today);
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == MyUserId && s.WorkDate == today)
            .OrderBy(s => s.Seq)
            .ToListAsync();

        var last = segs.LastOrDefault();
        if (rec == null || last == null || last.EnterAt == null)
        {
            // شیفت شب: ورودِ دیروز هنوز باز است و الان صبحِ بعدِ نیمه‌شب می‌خواهیم خروج بزنیم
            var yst = today.AddDays(-1);
            var yRec = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
                .FirstOrDefaultAsync(a => a.UserId == MyUserId && a.WorkDate == yst);
            if (yRec != null)
            {
                var ySegs = await _db.AttendanceSegments
                    .Where(s => s.UserId == MyUserId && s.WorkDate == yst)
                    .OrderBy(s => s.Seq).ToListAsync();
                var yLast = ySegs.LastOrDefault(s => !s.ExitAt.HasValue);
                if (yLast != null && yLast.EnterAt.HasValue)
                {
                    var yRule = await ResolveRuleAsync(MyUserId, yst, yRec.ShiftGroup);
                    var yEnd = WorkRules.ShiftEnd(yst, yRule);
                    if (now <= yEnd.AddMinutes(yRule.GraceMinutes + 30))
                    {
                        today = yst;
                        rec = yRec;
                        segs = ySegs;
                        last = yLast;
                    }
                }
            }
        }
        if (rec == null || last == null || last.EnterAt == null)
            return BadRequest(new { message = "ابتدا ورود خود را ثبت کنید." });
        if (last.ExitAt.HasValue)
            return BadRequest(new { message = "شما در حال حاضر در محل حضور دارید. ابتدا ورود بزنید." });

        var ruleOut = await ResolveRuleAsync(MyUserId, today, rec.ShiftGroup);

        last.ExitAt = now;
        last.ExitIp = MyIp;
        if (!string.IsNullOrWhiteSpace(input?.Note)) last.Note = input.Note;

        // پوشش موقت: لحظه‌ی خروج باید داخل بازه‌ی درخواست تاییدشده باشد
        // (بررسی قطعی‌ی «پوشش کامل بازه» هنگام ورود مجدد یا پایان شیفت انجام می‌شود)
        last.ExitCovered = await IsCoveredAtAsync(MyUserId, today, now);
        last.LinkedLeaveRequestId = null;
        last.LinkedLeaveNumber = null;

        // ارزیابی بازه بر اساس قاعده‌ی کاری روز: اضافه‌کاری، مازادِ در انتظار تصمیم و تردد غیرمجاز
        var evOut = WorkRules.EvaluateSegment(ruleOut, today, last.EnterAt.Value, now, now,
            (ExtraDecision)last.ExtraDecision);
        if (evOut.IsUnauthorized) last.IsUnauthorized = true;
        last.OvertimeMinutes = evOut.OvertimeMin;
        last.PendingMinutes = evOut.PendingMin;

        rec.UpdatedAt = now;
        await AggregateAsync(rec, segs, ruleOut, now);
        await _db.SaveChangesAsync();

        // ---------- امنیت: دستگاه جدید / دستگاه مشترک / خارج از محدوده (در خروج) ----------
        try
        {
            var adminIds = await GetAttendanceManagerIds();
            await _security.ValidateAsync(
                MyUserId, rec.UserName,
                input?.DeviceId, MyIp, Request.Headers.UserAgent.ToString(),
                input?.Lat, input?.Lng, adminIds);
        }
        catch { /* امنیت ثانویه است؛ خطا در آن مانع ثبت خروج نمی‌شود */ }

        await _notify.BroadcastChangedAsync("attendance");

        var covered = last.ExitCovered;
        string msg;
        if (evOut.IsUnauthorized)
            msg = "خروج ثبت شد. توجه: این تردد خارج از بازه‌ی مجازِ تقویم کاری است و به‌عنوان «تردد غیرمجاز» ثبت شده است.";
        else if (evOut.PendingMin > 0)
            msg = $"خروج ثبت شد. {Fa.Digits(evOut.PendingMin)} دقیقه ماندنِ اضافه بر ساعت شیفت به‌عنوان «مازادِ در انتظار تصمیم» ثبت شد — نه کسری و نه اضافه‌کار. مدیر آن را تأیید (اضافه‌کار) یا رد (تردد غیرمجاز) می‌کند.";
        else if (covered)
            msg = "خروج ثبت شد. بازه‌ی غیبت شما با درخواست تاییدشده پوشش دارد.";
        else
            msg = "خروج ثبت شد. توجه: اگر بازه‌ی غیبت با مرخصی/ماموریت ساعتی تاییدشده پوشش نداشته باشد، در گزارش به‌عنوان کسری حساب می‌شود.";
        return Ok(new
        {
            ok = true,
            record = Map(rec, segs),
            covered,
            unauthorized = evOut.IsUnauthorized,
            overtimeMinutes = evOut.OvertimeMin,
            pendingMinutes = evOut.PendingMin,
            message = msg,
        });
    }
    public class CheckOutInput
    {
        public string? Note { get; set; }
        /// <summary>شناسه یکتای دستگاه (از localStorage مرورگر)</summary>
        public string? DeviceId { get; set; }
        /// <summary>عرض جغرافیایی (اختیاری)</summary>
        public double? Lat { get; set; }
        /// <summary>طول جغرافیایی (اختیاری)</summary>
        public double? Lng { get; set; }
    }

    // ================== وضعیت امروز من ==================
    [HttpGet("my-today")]
    public async Task<IActionResult> MyToday()
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        var today = DateTime.Today;
        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .FirstOrDefaultAsync(a => a.UserId == MyUserId && a.WorkDate == today);
        if (rec == null)
        {
            // شیفت شب: ورودِ دیروز هنوز باز است (صبحِ بعد از نیمه‌شب)
            var yst = today.AddDays(-1);
            var yRec = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
                .FirstOrDefaultAsync(a => a.UserId == MyUserId && a.WorkDate == yst);
            if (yRec != null)
            {
                var ySegs = await _db.AttendanceSegments
                    .Where(s => s.UserId == MyUserId && s.WorkDate == yst)
                    .OrderBy(s => s.Seq).ToListAsync();
                var yOpen = ySegs.LastOrDefault(s => !s.ExitAt.HasValue);
                if (yOpen?.EnterAt != null)
                {
                    var yRule = await ResolveRuleAsync(MyUserId, yst, yRec.ShiftGroup);
                    if (DateTime.Now <= WorkRules.ShiftEnd(yst, yRule).AddMinutes(yRule.GraceMinutes + 30))
                    {
                        today = yst;
                        rec = yRec;
                    }
                }
            }
            if (rec == null) return Ok(null);
        }
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == MyUserId && s.WorkDate == today)
            .OrderBy(s => s.Seq)
            .ToListAsync();
        // بازه‌ی بازِ آخر (اگر خروج زودهنگام داشته و هنوز نیامده باشد) را تا «اکنون» ارزیابی می‌کنیم
        var last = segs.LastOrDefault();
        if (last != null && last.ExitAt.HasValue)
            await EvaluateGapAsync(last, DateTime.Now);
        var ruleMt = await ResolveRuleAsync(MyUserId, today, rec.ShiftGroup);
        await AggregateAsync(rec, segs, ruleMt, DateTime.Now);
        await _db.SaveChangesAsync();
        return Ok(Map(rec, segs));
    }

    // ================== خلاصه ماه من (برای کارتابل) ==================
    [HttpGet("my-summary")]
    public async Task<IActionResult> MySummary([FromQuery] int? jy, [FromQuery] int? jm)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        var from = PersianDate.ToGregorian(y, m, 1);
        var to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);

        var list = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .Where(a => a.UserId == MyUserId && a.WorkDate >= from && a.WorkDate < to).ToListAsync();

        // محاسبه برای روزهایی که رکورد ندارند
        var recalc = false;
        var workdays = WorkdayCount(from, to);
        var calSummary = await _db.WorkCalendarDays.AsNoTracking()
            .Where(c => c.Date >= from && c.Date < to).ToDictionaryAsync(c => c.Date.Date);
        var holSummary = await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate < to).ToDictionaryAsync(h => h.HolidayDate.Date);
        var settingsS = await GetSettingsAsync();
        foreach (var r in list)
        {
            var h = await HasDailyLeaveAsync(MyUserId, r.WorkDate);
            calSummary.TryGetValue(r.WorkDate.Date, out var calS);
            holSummary.TryGetValue(r.WorkDate.Date, out var holS);
            var ruleS = WorkRules.Resolve(r.WorkDate, r.ShiftGroup, calS, holS, settingsS);
            var before = r.DeficitMinutes;
            RecalcRecord(r, ruleS, h);
            if (r.DeficitMinutes != before) recalc = true;
        }
        // روزهای بدون رکورد ولی با مرخصی «روزانه» تاییدشده → ایجاد رکورد LeaveDay
        var allLeaves = await _db.LeaveRequests
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved" && l.Type == "Daily"
                        && l.StartDate < to && l.EndDate >= from).ToListAsync();
        foreach (var l in allLeaves)
        {
            var s = l.StartDate.Date < from ? from : l.StartDate.Date;
            var e = l.EndDate.Date >= to ? to.AddDays(-1) : l.EndDate.Date;
            for (var d = s; d <= e; d = d.AddDays(1))
            {
                if (d < from || d >= to) continue;
                if (list.Any(r => r.WorkDate == d)) continue;
                var r = new AttendanceRecord
                {
                    WorkDate = d, UserId = MyUserId, UserName = MyName, FinalStatus = "LeaveDay",
                    HasApprovedLeave = true, CreatedAt = DateTime.Now
                };
                _db.AttendanceRecords.Add(r);
                list.Add(r);
                recalc = true;
            }
        }
        if (recalc) await _db.SaveChangesAsync();

        var summary = new
        {
            Year = y,
            Month = m,
            Present = list.Count(x => x.FinalStatus == "Present"),
            Absent = list.Count(x => x.FinalStatus == "Absent"),
            Leave = list.Count(x => x.FinalStatus == "LeaveDay"),
            Late = list.Count(x => x.EnterStatus == "Late"),
            WorkMinutes = list.Sum(x => x.WorkMinutes),
            DeficitMinutes = list.Sum(x => x.DeficitMinutes),
            OvertimeMinutes = list.Sum(x => x.OvertimeMinutes),
            UnauthorizedMinutes = list.Sum(x => x.UnauthorizedMinutes),
            PendingOvertimeMinutes = list.Sum(x => x.PendingOvertimeMinutes),
            TotalLateMinutes = list.Sum(x => x.LateMinutes),
            Workdays = workdays
        };
        return Ok(summary);
    }

    // ================== تاریخچه من ==================
    [HttpGet("my-history")]
    public async Task<IActionResult> MyHistory([FromQuery] int? jy, [FromQuery] int? jm, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        DateTime from, to; int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        from = PersianDate.ToGregorian(y, m, 1);
        to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);

        var pagination = new Paging.Request(skip, take);
        var list = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .Where(a => a.UserId == MyUserId && a.WorkDate >= from && a.WorkDate < to)
            .OrderByDescending(a => a.WorkDate).ThenBy(a => a.Id)
            .ToPageListAsync(pagination);
        var dates = list.Select(a => a.WorkDate).ToList();
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == MyUserId && dates.Contains(s.WorkDate))
            .OrderBy(s => s.Seq)
            .ToListAsync();
        var byDate = segs.GroupBy(s => s.WorkDate).ToDictionary(g => g.Key, g => g.ToList());
        var items = list.Select(r => Map(r, byDate.GetValueOrDefault(r.WorkDate))).ToList();
        if (Paging.Requested(skip, take)) return Ok(new { year = y, month = m, total = pagination.Total, items });
        return Ok(new { year = y, month = m, items });
    }

    // ================== روزهای دارای کسری + بازه‌های بدون پوشش (برای درخواست آسان مرخصی) ==================
    [HttpGet("my-deficit-ranges")]
    public async Task<IActionResult> MyDeficitRanges([FromQuery] int? jy, [FromQuery] int? jm, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        var from = PersianDate.ToGregorian(y, m, 1);
        var to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);

        var user = await _db.Users.Include(u => u.ShiftGroup).AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        if (user == null) return Unauthorized();
        var sg = user.ShiftGroup;
        var shiftStart = sg?.StartTime ?? new TimeSpan(8, 0, 0);
        var shiftEndTs = sg?.EndTime ?? new TimeSpan(16, 30, 0);
        var scheduled = ScheduledMinutes(sg);
        var now = DateTime.Now;

        var recs = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.UserId == MyUserId && a.WorkDate >= from && a.WorkDate < to)
            .ToDictionaryAsync(a => a.WorkDate);
        var segs = await _db.AttendanceSegments.AsNoTracking()
            .Where(s => s.UserId == MyUserId && s.WorkDate >= from && s.WorkDate < to)
            .OrderBy(s => s.Seq)
            .ToListAsync();
        var covers = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate >= from && l.StartDate < to
                        && l.StartTime != null && l.EndTime != null)
            .ToListAsync();
        var pendings = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Pending"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate >= from && l.StartDate < to
                        && l.StartTime != null && l.EndTime != null)
            .ToListAsync();
        var calDaysR = await _db.WorkCalendarDays.AsNoTracking()
            .Where(c => c.Date >= from && c.Date < to).ToDictionaryAsync(c => c.Date.Date);
        var holDaysR = await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate < to).ToDictionaryAsync(h => h.HolidayDate.Date);
        var settingsR = await GetSettingsAsync();

        var items = new List<object>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            calDaysR.TryGetValue(d.Date, out var calR);
            holDaysR.TryGetValue(d.Date, out var holR);
            var dayRuleR = WorkRules.Resolve(d.Date, sg, calR, holR, settingsR);
            if (!dayRuleR.IsWorkday) continue; // تعطیل/جمعه کسری ندارد
            var scheduledR = AttendanceMath.ScheduledMinutes(dayRuleR);

            recs.TryGetValue(d.Date, out var rec);
            var dSegs = segs.Where(s => s.WorkDate == d.Date).OrderBy(s => s.Seq).ToList();
            var dayCovers = covers.Where(l => l.StartDate.Date == d.Date).ToList();
            var dayPendings = pendings.Where(l => l.StartDate.Date == d.Date).ToList();

            // کسری روز: از رکورد (برای روزهای گذشته) یا محاسبه‌ی زنده برای امروز
            int deficit;
            if (rec != null) deficit = d.Date == DateTime.Today ? LiveDeficit(rec, dSegs, dayRuleR, now) : rec.DeficitMinutes;
            else deficit = dSegs.Count == 0 ? scheduledR : 0;
            if (deficit <= 0) continue;

            var ranges = new List<object>();

            // ۱) روز کامل بدون حضور و بدون رکورد
            if (rec == null && dSegs.Count == 0)
            {
                var pending = dayPendings.FirstOrDefault(l =>
                    (l.EndTime!.Value - l.StartTime!.Value).TotalMinutes >= scheduled);
                ranges.Add(new { start = shiftStart, end = shiftEndTs, minutes = scheduled, kind = "Day", pendingNumber = pending?.Number });
                items.Add(new { date = d.Date, deficitMinutes = deficit, ranges });
                continue;
            }

            // ۲) بازه‌های غیبتِ بدون پوشش (خروج ← ورود بعدی / خروج آخر ← پایان شیفت)
            for (var i = 0; i < dSegs.Count; i++)
            {
                var s = dSegs[i];
                if (s.ExitAt == null) continue;
                var gapEnd = (i + 1 < dSegs.Count && dSegs[i + 1].EnterAt.HasValue)
                    ? dSegs[i + 1].EnterAt!.Value
                    : d.Date.Add(shiftEndTs);
                var gapMin = (int)(gapEnd - s.ExitAt.Value).TotalMinutes;
                if (gapMin <= 0) continue;
                var gs = s.ExitAt.Value.TimeOfDay;
                var ge = gapEnd.TimeOfDay;
                var covered = dayCovers.Any(l => l.StartTime <= gs && l.EndTime >= ge);
                if (covered) continue;
                var pending = dayPendings.FirstOrDefault(l => l.StartTime <= gs && l.EndTime >= ge);
                ranges.Add(new { start = gs, end = ge, minutes = gapMin, kind = "Gap", pendingNumber = pending?.Number });
            }

            // ۳) بخش دیررس بودن [شروع شیفت، اولین ورود] — این هم با مرخصی/ماموریت ساعتی قابل پوشش است
            var firstEnter = dSegs.FirstOrDefault(s => s.EnterAt.HasValue)?.EnterAt;
            if (firstEnter.HasValue && sg != null)
            {
                var schedStart = d.Date.Add(shiftStart);
                if (firstEnter.Value > schedStart)
                {
                    var lateMin = (int)(firstEnter.Value - schedStart).TotalMinutes;
                    var gs2 = shiftStart;
                    var ge2 = firstEnter.Value.TimeOfDay;
                    var coveredLate = dayCovers.Any(l => l.StartTime <= gs2 && l.EndTime >= ge2);
                    if (!coveredLate)
                    {
                        var pending = dayPendings.FirstOrDefault(l => l.StartTime <= gs2 && l.EndTime >= ge2);
                        ranges.Add(new { start = gs2, end = ge2, minutes = lateMin, kind = "Late", pendingNumber = pending?.Number });
                    }
                }
            }

            if (ranges.Count == 0)
                ranges.Add(new { start = shiftStart, end = shiftEndTs, minutes = deficit, kind = "Day", pendingNumber = (string?)null });

            items.Add(new { date = d.Date, deficitMinutes = deficit, ranges });
        }

        if (Paging.Requested(skip, take)) return Ok(new { year = y, month = m, total = items.Count, items = Paging.Slice(items, skip, take) });
        return Ok(new { year = y, month = m, items });
    }

    /// <summary>کسری زنده برای امروز (با درنظر گرفتن زمان اکنون برای بازه‌ی باز).</summary>
    /// از همان موتور ارزیابیِ بازه‌ها استفاده می‌کند تا ماندنِ بعد از پایان شیفت
    /// (اضافه‌کار/مازادِ در انتظار/غیرمجاز) هرگز به‌جای کسری حساب نشود.
    /// </summary>
    private static int LiveDeficit(AttendanceRecord rec, List<AttendanceSegment> segs, WorkDayRule rule, DateTime now)
    {
        if (!rule.IsWorkday) return 0;
        var scheduled = AttendanceMath.ScheduledMinutes(rule);
        int work = 0;
        foreach (var s in segs)
        {
            if (!s.EnterAt.HasValue) continue;
            work += WorkRules.EvaluateSegment(rule, rec.WorkDate, s.EnterAt.Value, s.ExitAt, now,
                (ExtraDecision)s.ExtraDecision).WorkMin;
        }
        return Math.Max(0, scheduled - work - rec.CoveredGapMinutes);
    }

    // ================== کل تردد ماه (همه‌ی روزها با وضعیت رنگی) ==================
    [HttpGet("my-month-view")]
    public async Task<IActionResult> MyMonthView([FromQuery] int? jy, [FromQuery] int? jm)
    {
        if (!await HasAsync("SelfCheckin")) return Forbid();
        int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        var from = PersianDate.ToGregorian(y, m, 1);
        var to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);
        var today = DateTime.Today;

        var user = await _db.Users.Include(u => u.ShiftGroup).AsNoTracking().FirstOrDefaultAsync(u => u.Id == MyUserId);
        if (user == null) return Unauthorized();
        var sg = user.ShiftGroup;
        var shiftStart = sg?.StartTime ?? new TimeSpan(8, 0, 0);
        var shiftEndTs = sg?.EndTime ?? new TimeSpan(16, 30, 0);
        var scheduled = ScheduledMinutes(sg);

        var recs = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.UserId == MyUserId && a.WorkDate >= from && a.WorkDate < to)
            .ToDictionaryAsync(a => a.WorkDate);
        var segs = await _db.AttendanceSegments.AsNoTracking()
            .Where(s => s.UserId == MyUserId && s.WorkDate >= from && s.WorkDate < to)
            .OrderBy(s => s.Seq).ToListAsync();
        var hourlyApproved = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate >= from && l.StartDate < to
                        && l.StartTime != null && l.EndTime != null).ToListAsync();
        var dailyApproved = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved" && l.Type == "Daily"
                        && l.EndDate >= from && l.StartDate < to).ToListAsync();
        var missionApproved = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved" && l.Type == "Mission"
                        && l.EndDate >= from && l.StartDate < to).ToListAsync();
        var pendings = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Pending"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate >= from && l.StartDate < to
                        && l.StartTime != null && l.EndTime != null).ToListAsync();
        var holidays = (await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate < to).ToListAsync())
            .ToDictionary(h => h.HolidayDate);
        var calDays = await _db.WorkCalendarDays.AsNoTracking()
            .Where(c => c.Date >= from && c.Date < to).ToDictionaryAsync(c => c.Date.Date);
        var settingsM = await GetSettingsAsync();

        var items = new List<object>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            var date = d.Date;
            var isFuture = date > today;
            var isToday = date == today;
            recs.TryGetValue(date, out var rec);
            var dSegs = segs.Where(s => s.WorkDate == date).OrderBy(s => s.Seq).ToList();
            var dayHourly = hourlyApproved.Where(l => l.StartDate.Date == date).ToList();
            var dayPend = pendings.Where(l => l.StartDate.Date == date).ToList();

            // قاعده‌ی کاری روز (تقویم کاری > تعطیل رسمی/شرکتی > شیفت > پیش‌فرض)
            calDays.TryGetValue(date, out var calDay);
            holidays.TryGetValue(date, out var holDay);
            var dayRule = WorkRules.Resolve(date, sg, calDay, holDay, settingsM);
            var dayScheduled = AttendanceMath.ScheduledMinutes(dayRule);

            string status;
            string leaveType = "None";
            string? holidayName = null;
            int deficit;

            // روزهای آینده → سفید
            if (isFuture)
            {
                status = "White";
                deficit = 0;
            }
            // امروز → زرد (در حال انجام)
            else if (isToday)
            {
                status = "Yellow";
                leaveType = dayHourly.Count > 0 ? (dayHourly[0].Type == "Mission" ? "HourlyMission" : dayHourly[0].Type) : "None";
                deficit = LiveDeficit(rec ?? new AttendanceRecord { WorkDate = date, CoveredGapMinutes = 0 }, dSegs, dayRule, DateTime.Now);
            }
            // روزهای گذشته
            else
            {
                // اگر روز گذشته ولی بازه‌ی آخرش باز مانده (خروج نزده)، ابتدا نهایی‌اش می‌کنیم
                if (rec != null)
                {
                    var lastSeg = dSegs.LastOrDefault();
                    if (lastSeg != null && lastSeg.EnterAt.HasValue && lastSeg.ExitAt == null)
                    {
                        await _recalc.RecalcDayAsync(MyUserId, date, DateTime.Now);
                        recs.TryGetValue(date, out var fresh);
                        rec = fresh;
                    }
                }
                bool isUnauthorized = (rec != null && rec.UnauthorizedMinutes > 0) || dSegs.Any(x => x.IsUnauthorized);
                deficit = rec?.DeficitMinutes ?? (dSegs.Count == 0 ? (dayRule.IsWorkday ? dayScheduled : 0) : 0);

                if (isUnauthorized)
                {
                    // تردد غیرمجاز اولویت نمایش دارد (حتی اگر روز تعطیل/مرخصی باشد)
                    status = "Unauthorized";
                    if (dayHourly.Count > 0) leaveType = dayHourly[0].Type;
                    else if (dayPend.Count > 0) leaveType = dayPend[0].Type;
                }
                else if (holidays.TryGetValue(date, out var hol))
                {
                    status = "Green"; leaveType = "Holiday"; holidayName = hol.Name; deficit = 0;
                }
                else if (dailyApproved.Any(l => l.StartDate.Date <= date && l.EndDate.Date >= date))
                {
                    status = "Green"; leaveType = "Daily"; deficit = 0;
                }
                else if (missionApproved.Any(l => l.StartDate.Date <= date && l.EndDate.Date >= date))
                {
                    status = "Green"; leaveType = "Mission"; deficit = 0;
                }
                else if (dayRule.Source == "Calendar" && !dayRule.IsWorkday)
                {
                    status = "Green"; leaveType = "Holiday"; holidayName = dayRule.Note;
                }
                else
                {
                    status = deficit <= 0 ? "Green" : "Red";
                    if (dayHourly.Count > 0) leaveType = dayHourly[0].Type;
                    else if (dayPend.Count > 0) leaveType = dayPend[0].Type;
                }
            }

            // بازه‌های بدون پوشش (فقط برای روزهای قرمز — برای دکمه‌ی درخواست)
            var ranges = new List<object>();
            if (status == "Red")
            {
                if (rec == null && dSegs.Count == 0)
                {
                    var pending = dayPend.FirstOrDefault(l => (l.EndTime!.Value - l.StartTime!.Value).TotalMinutes >= dayScheduled);
                    ranges.Add(new { start = dayRule.Start, end = dayRule.End, minutes = dayScheduled, kind = "Day", pendingNumber = pending?.Number });
                }
                else
                {
                    for (var i = 0; i < dSegs.Count; i++)
                    {
                        var s = dSegs[i];
                        if (s.ExitAt == null) continue;
                        var gapEnd = (i + 1 < dSegs.Count && dSegs[i + 1].EnterAt.HasValue)
                            ? dSegs[i + 1].EnterAt!.Value
                            : date.Add(dayRule.End);
                        var gapMin = (int)(gapEnd - s.ExitAt.Value).TotalMinutes;
                        if (gapMin <= 0) continue;
                        var gs = s.ExitAt.Value.TimeOfDay;
                        var ge = gapEnd.TimeOfDay;
                        if (dayHourly.Any(l => l.StartTime <= gs && l.EndTime >= ge)) continue;
                        var pending = dayPend.FirstOrDefault(l => l.StartTime <= gs && l.EndTime >= ge);
                        ranges.Add(new { start = gs, end = ge, minutes = gapMin, kind = "Gap", pendingNumber = pending?.Number });
                    }
                    var firstEnter = dSegs.FirstOrDefault(s => s.EnterAt.HasValue)?.EnterAt;
                    if (firstEnter.HasValue && dayRule.IsWorkday)
                    {
                        var schedStart = date.Add(dayRule.Start);
                        if (firstEnter.Value > schedStart)
                        {
                            var lateMin = (int)(firstEnter.Value - schedStart).TotalMinutes;
                            var gs2 = dayRule.Start; var ge2 = firstEnter.Value.TimeOfDay;
                            if (!dayHourly.Any(l => l.StartTime <= gs2 && l.EndTime >= ge2))
                            {
                                var pending = dayPend.FirstOrDefault(l => l.StartTime <= gs2 && l.EndTime >= ge2);
                                ranges.Add(new { start = gs2, end = ge2, minutes = lateMin, kind = "Late", pendingNumber = pending?.Number });
                            }
                        }
                    }
                    if (ranges.Count == 0)
                        ranges.Add(new { start = dayRule.Start, end = dayRule.End, minutes = deficit, kind = "Day", pendingNumber = (string?)null });
                }
            }

            items.Add(new
            {
                date,
                isFuture,
                isToday,
                status,
                leaveType,
                holidayName,
                deficitMinutes = deficit,
                overtimeMinutes = rec?.OvertimeMinutes ?? 0,
                unauthorizedMinutes = rec?.UnauthorizedMinutes ?? 0,
                pendingOvertimeMinutes = rec?.PendingOvertimeMinutes ?? 0,
                dayStart = dayRule.Start,
                dayEnd = dayRule.End,
                isWorkday = dayRule.IsWorkday,
                ruleSource = dayRule.Source,
                enterTimes = dSegs.Where(s => s.EnterAt.HasValue).Select(s => s.EnterAt!.Value.ToString("HH:mm")).ToList(),
                exitTimes = dSegs.Where(s => s.ExitAt.HasValue).Select(s => s.ExitAt!.Value.ToString("HH:mm")).ToList(),
                segments = dSegs.Where(s => s.EnterAt.HasValue).Select(MapSegment).ToList(),
                ranges
            });
        }

        return Ok(new { year = y, month = m, items });
    }

    // ================== گزارش ادمین (جزئیات روزانه) ==================
    [HttpGet("report")]
    public async Task<IActionResult> Report([FromQuery] int? jy, [FromQuery] int? jm, [FromQuery] int? userId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsAdminAsync()) return Forbid();
        int y, m; DateTime from, to;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        from = PersianDate.ToGregorian(y, m, 1);
        to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);

        var allowedU = await AllowedUsersAsync();
        var q = _db.AttendanceRecords.Include(a => a.ShiftGroup).Where(a => a.WorkDate >= from && a.WorkDate < to && allowedU.Contains(a.UserId));
        if (userId.HasValue && userId > 0) q = q.Where(a => a.UserId == userId);
        var pagination = new Paging.Request(skip, take);
        var list = await q.OrderByDescending(a => a.WorkDate).ThenBy(a => a.UserId).ThenBy(a => a.Id).ToPageListAsync(pagination);
        var userIds = list.Select(a => a.UserId).Distinct().ToList();
        var dates = list.Select(a => a.WorkDate).Distinct().ToList();
        var segq = _db.AttendanceSegments.Where(s => userIds.Contains(s.UserId) && dates.Contains(s.WorkDate));
        if (userId.HasValue && userId > 0) segq = segq.Where(s => s.UserId == userId);
        var segs = await segq.OrderBy(s => s.Seq).ToListAsync();
        var byDateUser = segs.GroupBy(s => (s.UserId, s.WorkDate)).ToDictionary(g => g.Key, g => g.ToList());
        var items = list.Select(r => Map(r, byDateUser.GetValueOrDefault((r.UserId, r.WorkDate)))).ToList();
        if (Paging.Requested(skip, take)) return Ok(new { year = y, month = m, total = pagination.Total, items });
        return Ok(new { year = y, month = m, items });
    }

    // ================== گزارش روزانهٔ ماه — یک سطر به ازای هر پرسنل در هر روز ==================
    // برخلاف report (که فقط رکوردهای موجود را برمی‌گرداند)، این گزارش همهٔ پرسنل فعال را
    // برای همهٔ روزهای ماه نشان می‌دهد: غایب‌ها (بدون رکورد)، تعطیلات و مرخصی‌ها هم دیده می‌شوند.
    [HttpGet("month-day-report")]
    public async Task<IActionResult> MonthDayReport([FromQuery] int? jy, [FromQuery] int? jm,
        [FromQuery] int? userId, [FromQuery] bool onlyDeficit = false)
    {
        if (!await IsAdminAsync()) return Forbid();
        int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        var from = PersianDate.ToGregorian(y, m, 1);
        var to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);
        if (from == DateTime.MinValue || to == DateTime.MinValue || to <= from)
            return BadRequest(new { message = "ماه درخواستی معتبر نیست." });

        // روزهای آینده در گزارش نمی‌آیند (هنوز اتفاق نیافتاده‌اند)
        var today = DateTime.Today;
        var toExclusive = to <= today.AddDays(1) ? to : today.AddDays(1);
        var empty = new List<object>();

        var allowedSet = await _fa.UserIdsAsync();
        var users = (await _db.Users.Include(u => u.ShiftGroup).Where(u => u.IsActive).ToListAsync())
            .Where(u => allowedSet.Contains(u.Id)).ToList();
        if (userId.HasValue && userId > 0) users = users.Where(u => u.Id == userId.Value).ToList();
        if (users.Count == 0 || toExclusive <= from)
            return Ok(new { year = y, month = m, monthName = PersianDate.MonthName(m), items = empty });

        var recBy = (await _db.AttendanceRecords
            .Where(a => a.WorkDate >= from && a.WorkDate < toExclusive).ToListAsync())
            .ToDictionary(r => (r.UserId, r.WorkDate.Date));
        var inOut = (await _db.AttendanceSegments
            .Where(s => s.WorkDate >= from && s.WorkDate < toExclusive && s.EnterAt != null)
            .GroupBy(s => new { s.UserId, s.WorkDate })
            .Select(g => new { g.Key.UserId, g.Key.WorkDate, Cnt = g.Count() })
            .ToListAsync())
            .ToDictionary(x => (x.UserId, x.WorkDate.Date), x => x.Cnt);
        // مرخصی‌های تاییدشدهٔ هم‌پوشان با بازهٔ گزارش (فیلتر دقیق در حافظه)
        var leaves = await _db.LeaveRequests
            .Where(l => l.Status == "Approved" && (l.Type == "Daily" || l.Type == "Hourly")
                        && l.StartDate < toExclusive && l.EndDate >= from)
            .ToListAsync();
        var calDays = await _db.WorkCalendarDays.AsNoTracking()
            .Where(c => c.Date >= from && c.Date < toExclusive).ToDictionaryAsync(c => c.Date.Date);
        var hols = (await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate < toExclusive).ToListAsync())
            .ToDictionary(h => h.HolidayDate.Date);
        var settings = await GetSettingsAsync();
        var now = DateTime.Now;

        var items = new List<object>();
        foreach (var u in users)
        {
            var uLeaves = leaves.Where(l => l.RequesterUserId == u.Id).ToList();
            for (var day = from.Date; day < toExclusive.Date; day = day.AddDays(1))
            {
                recBy.TryGetValue((u.Id, day), out var r);
                string? finalStatus;
                var deficitMinutes = 0;
                var hasApprovedLeave = false;
                if (r != null)
                {
                    finalStatus = r.FinalStatus;
                    deficitMinutes = r.DeficitMinutes;
                    hasApprovedLeave = r.HasApprovedLeave;
                }
                else
                {
                    var hasDaily = uLeaves.Any(l => l.Type == "Daily" && day >= l.StartDate.Date && day <= l.EndDate.Date);
                    var hasHourly = uLeaves.Any(l => l.Type == "Hourly" && l.StartDate.Date == day);
                    var rule = WorkRules.Resolve(day, u.ShiftGroup, calDays.GetValueOrDefault(day), hols.GetValueOrDefault(day), settings);
                    var shiftStart = u.ShiftGroup?.StartTime ?? new TimeSpan(8, 0, 0);
                    var grace = u.ShiftGroup?.GraceMinutes ?? 10;
                    var deadline = day.Add(shiftStart).AddMinutes(grace + 30);
                    if (hasDaily || hasHourly) { finalStatus = "LeaveDay"; hasApprovedLeave = true; }
                    else if (!rule.IsWorkday) finalStatus = "Holiday";
                    else if (day == today && now < deadline) finalStatus = "Pending";
                    else { finalStatus = "Absent"; deficitMinutes = AttendanceMath.ScheduledMinutes(rule); }
                }

                if (onlyDeficit && deficitMinutes <= 0) continue;

                items.Add(new
                {
                    date = day,
                    weekday = PersianDate.WeekdayName(day),
                    userId = u.Id,
                    fullName = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim(),
                    shiftName = r?.ShiftGroup?.Name ?? u.ShiftGroup?.Name ?? "—",
                    enterAt = r?.EnterAt,
                    exitAt = r?.ExitAt,
                    enterStatus = r?.EnterStatus,
                    lateMinutes = r?.LateMinutes ?? 0,
                    earlyLeaveMinutes = r?.EarlyLeaveMinutes ?? 0,
                    workMinutes = r?.WorkMinutes ?? 0,
                    deficitMinutes,
                    overtimeMinutes = r?.OvertimeMinutes ?? 0,
                    pendingOvertimeMinutes = r?.PendingOvertimeMinutes ?? 0,
                    unauthorizedMinutes = r?.UnauthorizedMinutes ?? 0,
                    hasApprovedLeave,
                    finalStatus,
                    note = r?.Note,
                    inOutCount = inOut.TryGetValue((u.Id, day), out var cnt) ? cnt : 0
                });
            }
        }
        // تازه‌ترین روزها اول
        items = items.OrderByDescending(x => ((dynamic)x).date).ThenBy(x => ((dynamic)x).fullName).ToList();
        return Ok(new { year = y, month = m, monthName = PersianDate.MonthName(m), items });
    }

    // ================== گزارش خلاصه ماهانه (به ازای هر کاربر) ==================
    [HttpGet("monthly-report")]
    public async Task<IActionResult> MonthlyReport([FromQuery] int? jy, [FromQuery] int? jm, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsAdminAsync()) return Forbid();
        int y, m;
        if (jy.HasValue && jm.HasValue) { y = jy.Value; m = jm.Value; }
        else { (y, m, _) = PersianDate.FromGregorian(DateTime.Now); }
        var from = PersianDate.ToGregorian(y, m, 1);
        var to = m == 12 ? PersianDate.ToGregorian(y + 1, 1, 1) : PersianDate.ToGregorian(y, m + 1, 1);
        var daysInMonth = PersianDate.DaysInMonth(y, m);

        var pagination = new Paging.Request(skip, take);
        var allowedU = await AllowedUsersAsync();
        var users = await _db.Users.Include(u => u.ShiftGroup).Where(u => u.IsActive && allowedU.Contains(u.Id))
            .OrderBy(u => u.Username).ThenBy(u => u.Id).ToPageListAsync(pagination);
        var userIds = users.Select(u => u.Id).ToList();
        var recsAll = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .Where(a => userIds.Contains(a.UserId) && a.WorkDate >= from && a.WorkDate < to).ToListAsync();
        var leavesAll = await _db.LeaveRequests
            .Where(l => userIds.Contains(l.RequesterUserId) && l.Status == "Approved" && (l.Type == "Daily" || l.Type == "Hourly") && l.StartDate < to && l.EndDate >= from).ToListAsync();
        var holidaysAll = await _db.CompanyHolidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate < to).ToListAsync();
        var holidayDates = holidaysAll.Select(h => h.HolidayDate).ToHashSet();
        var calDaysMonth = await _db.WorkCalendarDays.AsNoTracking()
            .Where(c => c.Date >= from && c.Date < to).ToDictionaryAsync(c => c.Date.Date);

        var rows = new List<object>();
        foreach (var u in users)
        {
            var mine = recsAll.Where(r => r.UserId == u.Id).ToList();
            var uLeaves = leavesAll.Where(l => l.RequesterUserId == u.Id).ToList();

            // محاسبه تعداد روزهای مرخصی تاییدشده در ماه
            double leaveDays = 0;
            foreach (var l in uLeaves)
            {
                var s = l.StartDate.Date < from ? from : l.StartDate.Date;
                var e = l.EndDate.Date >= to ? to.AddDays(-1) : l.EndDate.Date;
                if (l.Type == "Daily") leaveDays += (e - s).TotalDays + 1;
                else if (l.Type == "Hourly") leaveDays += l.Hours / 8.0;
            }

            int present = mine.Count(r => r.FinalStatus == "Present");
            int leaveRec = mine.Count(r => r.FinalStatus == "LeaveDay");
            int absent = mine.Count(r => r.FinalStatus == "Absent");
            int late = mine.Count(r => r.EnterStatus == "Late");
            int workMin = mine.Sum(r => r.WorkMinutes);
            int lateMin = mine.Sum(r => r.LateMinutes);
            int earlyMin = mine.Sum(r => r.EarlyLeaveMinutes);
            int overtimeMin = mine.Sum(r => r.OvertimeMinutes);
            int unauthorizedMin = mine.Sum(r => r.UnauthorizedMinutes);
            int pendingMin = mine.Sum(r => r.PendingOvertimeMinutes);

            // محاسبه کسری با درنظر گرفتن مرخصی
            int deficit = 0;
            int workdayCount = 0;
            var uShift = u.ShiftGroup;
            for (var d = from; d < to; d = d.AddDays(1))
            {
                if (!IsWorkday(d, uShift)) continue;
                workdayCount++;
                var rec = mine.FirstOrDefault(r => r.WorkDate == d);
                // تعطیلی شرکتی کل روز را از کسری مستثنا می‌کند
                if (holidayDates.Contains(d.Date)) continue;
                // روزهای تعطیلِ تعریف‌شده در تقویم کاری هم موظفیت ندارند
                if (calDaysMonth.TryGetValue(d.Date, out var calDayRep) && !calDayRep.IsWorkday) continue;
                // فقط مرخصی «روزانه» کل روز را از کسری مستثنا می‌کند؛ مرخصی ساعتی از طریق غیبت پوشش‌شده در DeficitMinutes لحاظ شده است
                var dayHasDailyLeave = uLeaves.Any(l =>
                    l.Type == "Daily" && d >= l.StartDate.Date && d <= l.EndDate.Date);
                if (dayHasDailyLeave) continue;
                if (rec != null) deficit += rec.DeficitMinutes;
                else deficit += ScheduledMinutes(uShift); // روز کاری بدون ورود/خروج = کسری کامل
            }

            rows.Add(new
            {
                UserId = u.Id,
                UserName = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim(),
                ShiftName = uShift?.Name ?? "—",
                Workdays = workdayCount,
                Present = present,
                Absent = absent,
                LeaveDays = Math.Round(leaveDays + leaveRec, 2),
                LateDays = late,
                TotalLateMinutes = lateMin,
                TotalEarlyLeaveMinutes = earlyMin,
                WorkMinutes = workMin,
                DeficitMinutes = deficit,
                DeficitHours = Math.Round(deficit / 60.0, 2),
                OvertimeMinutes = overtimeMin,
                UnauthorizedMinutes = unauthorizedMin,
                PendingOvertimeMinutes = pendingMin,
                PendingExtraDays = mine.Count(r => r.PendingOvertimeMinutes > 0)
            });
        }

        if (Paging.Requested(skip, take))
            return Ok(new
            {
                year = y,
                month = m,
                monthName = PersianDate.MonthName(m),
                days = daysInMonth,
                total = pagination.Total,
                items = rows
            });
        return Ok(new
        {
            year = y,
            month = m,
            monthName = PersianDate.MonthName(m),
            days = daysInMonth,
            items = rows
        });
    }

    // ================== وضعیت امروز همه ==================
    [HttpGet("today-status")]
    public async Task<IActionResult> TodayStatus([FromQuery] int skip = 0, [FromQuery] int? take = null)
        => await DayStatusCore(DateTime.Today, skip, take);

    // ================== گزارش وضعیت هر روز — ورود/خروج و غیبت هر پرسنل در یک سطر ==================
    [HttpGet("day-status")]
    public async Task<IActionResult> DayStatus([FromQuery] DateTime? date, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => await DayStatusCore(date?.Date ?? DateTime.Today, skip, take);

    /// <summary>
    /// سطر «سبک» وضعیت روز — همهٔ فیلدها به‌جز Segments (بخش سنگین/نمایشی) که فقط
    /// برای سطرهای صفحهٔ جاری ساخته می‌شود. ارجاع uSegs برای همان mapping نگه داشته می‌شود.
    /// </summary>
    private sealed record DayStatusLightRow(
        int UserId, string FullName, string ShiftName, TimeSpan? ShiftStart, TimeSpan? ShiftEnd,
        DateTime? EnterAt, DateTime? ExitAt, string? EnterStatus, int LateMinutes, int EarlyLeaveMinutes,
        int WorkMinutes, int DeficitMinutes, int CoveredGapMinutes, int OvertimeMinutes,
        int PendingOvertimeMinutes,
        string? FinalStatus, bool HasApprovedLeave, string? Note, int InOutCount, bool IsInNow,
        int UncoveredGaps, List<AttendanceSegment> Segs);

    private async Task<IActionResult> DayStatusCore(DateTime day, int skip = 0, int? take = null)
    {
        if (!await IsAdminAsync()) return Forbid();
        day = day.Date;
        var isToday = day == DateTime.Today;
        var allowedSet = await _fa.UserIdsAsync();
        var users = (await _db.Users.Include(u => u.ShiftGroup).Where(u => u.IsActive).ToListAsync())
            .Where(u => allowedSet.Contains(u.Id)).ToList();
        var recs = await _db.AttendanceRecords.Include(a => a.ShiftGroup)
            .Where(a => a.WorkDate == day).ToDictionaryAsync(a => a.UserId);
        var allSegs = await _db.AttendanceSegments
            .Where(s => s.WorkDate == day)
            .OrderBy(s => s.Seq)
            .ToListAsync();

        // مرخصی‌های تاییدشدهٔ هم‌پوشان با این روز — یک کوئری به‌جای به‌ازای هر پرسنل؛
        // بازهٔ کوئری عام است و فیلتر دقیق (مانند HasApprovedLeaveAsync) در حافظه انجام می‌شود.
        var leaves = await _db.LeaveRequests
            .Where(l => l.Status == "Approved" && (l.Type == "Daily" || l.Type == "Hourly")
                        && l.StartDate < day.AddDays(1) && l.EndDate >= day)
            .ToListAsync();
        leaves = leaves.Where(l =>
            (l.Type == "Daily" && day >= l.StartDate.Date && day <= l.EndDate.Date) ||
            (l.Type == "Hourly" && l.StartDate.Date == day)).ToList();

        // قاعدهٔ کاری روز (تقویم کاری > تعطیلی شرکتی > شیفت > پیش‌فرض) — یک‌بار برای همه
        var cal = await _db.WorkCalendarDays.AsNoTracking().FirstOrDefaultAsync(d => d.Date.Date == day);
        var hol = await _db.CompanyHolidays.AsNoTracking().FirstOrDefaultAsync(h => h.HolidayDate.Date == day);
        var settings = await GetSettingsAsync();

        // سطرهای «سبک» برای همهٔ پرسنل فعال (بازمحاسبهٔ امروز و تجمیع برای همه انجام می‌شود؛
        // فقط ساخت Segments نمایشی به صفحهٔ جاری موکول شده است).
        var lightRows = new List<DayStatusLightRow>();
        var now = DateTime.Now;
        foreach (var u in users)
        {
            recs.TryGetValue(u.Id, out var r);
            var hasLeave = leaves.Any(l => l.RequesterUserId == u.Id);
            var uSegs = allSegs.Where(s => s.UserId == u.Id).OrderBy(s => s.Seq).ToList();
            var rule = WorkRules.Resolve(day, u.ShiftGroup, cal, hol, settings);
            // بازمحاسبهٔ زنده فقط برای «امروز» و فقط برای رکوردهایی که بازهٔ ورود/خروج (Segment)
            // دارند. رکوردهای بدون بازه (داده‌های قدیمی یا اصلاح‌شدهٔ دستی) باید با مقادیر
            // ذخیره‌شده نمایش داده شوند — Recompute با بازهٔ خالی ورود/خروج و کارکرد را پاک می‌کند.
            if (r != null && isToday && uSegs.Count > 0)
            {
                // بازه‌ی غیبتِ بازِ آخر را تا «اکنون» ارزیابی و تجمیعی را به‌روز می‌کنیم
                var uLast = uSegs.LastOrDefault();
                if (uLast != null && uLast.ExitAt.HasValue)
                    await EvaluateGapAsync(uLast, now);
                var ruleTs = await ResolveRuleAsync(u.Id, day, u.ShiftGroup);
                await AggregateAsync(r, uSegs, ruleTs, now);
            }

            // تعیین وضعیت نهایی با درنظر گرفتن زمان فعلی (قبل از پایان مهلت ورود، غیبت قطعی نیست)
            string? finalStatus = r?.FinalStatus;
            var deficitMinutes = r?.DeficitMinutes ?? 0;
            if (r == null)
            {
                var shiftStart = u.ShiftGroup?.StartTime ?? new TimeSpan(8, 0, 0);
                var grace = u.ShiftGroup?.GraceMinutes ?? 10;
                var deadline = day.Add(shiftStart).AddMinutes(grace + 30); // ۳۰ دقیقه فرصت اضافه
                if (hasLeave) finalStatus = "LeaveDay";
                else if (!rule.IsWorkday) finalStatus = "Holiday";      // جمعه/تعطیلی شرکتی/تقویم کاری
                else if (day > DateTime.Today) finalStatus = "Pending"; // روز آینده هنوز نرسیده
                else if (isToday && now < deadline) finalStatus = "Pending";
                else
                {
                    finalStatus = "Absent";
                    // غیبتِ کامل در روزِ کاری = کسریِ کل موظفی آن روز (هم‌راستا با گزارش خلاصه ماهانه)
                    deficitMinutes = AttendanceMath.ScheduledMinutes(rule);
                }
            }

            lightRows.Add(new DayStatusLightRow(
                u.Id,
                string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : $"{u.FirstName} {u.LastName}".Trim(),
                u.ShiftGroup?.Name ?? "—",
                (TimeSpan?)u.ShiftGroup?.StartTime,
                (TimeSpan?)u.ShiftGroup?.EndTime,
                r?.EnterAt,
                r?.ExitAt,
                r?.EnterStatus,
                r?.LateMinutes ?? 0,
                r?.EarlyLeaveMinutes ?? 0,
                r?.WorkMinutes ?? 0,
                deficitMinutes,
                r?.CoveredGapMinutes ?? 0,
                r?.OvertimeMinutes ?? 0,
                r?.PendingOvertimeMinutes ?? 0,
                finalStatus,
                r?.HasApprovedLeave ?? hasLeave,
                r?.Note,
                uSegs.Count(s => s.EnterAt.HasValue),
                isToday && uSegs.LastOrDefault() is { EnterAt: not null, ExitAt: null },
                uSegs.Count(s => s.ExitAt.HasValue && !s.ExitCovered),
                uSegs));
        }

        // ترتیب پایدار: حاضرها اول، سپس نام؛ برابری‌ها با UserId قطعی می‌شود.
        lightRows = lightRows
            .OrderBy(x => x.FinalStatus == "Present" ? 0 : 1)
            .ThenBy(x => x.FullName)
            .ThenBy(x => x.UserId)
            .ToList();

        // آمار کل روز — روی «همهٔ سطرها» (نه فقط صفحهٔ جاری)، مانند قبل.
        var stats = new
        {
            Total = lightRows.Count,
            Present = lightRows.Count(x => x.FinalStatus == "Present"),
            Absent = lightRows.Count(x => x.FinalStatus == "Absent"),
            Pending = lightRows.Count(x => x.FinalStatus == "Pending"),
            Late = lightRows.Count(x => x.EnterStatus == "Late"),
            OnLeave = lightRows.Count(x => x.HasApprovedLeave),
            Holiday = lightRows.Count(x => x.FinalStatus == "Holiday"),
        };

        // Segments (بخش سنگین) فقط برای سطرهای صفحهٔ جاری ساخته می‌شود.
        var pageRows = Paging.Slice(lightRows, skip, take);
        var items = pageRows.Select(x => new
        {
            x.UserId,
            x.FullName,
            x.ShiftName,
            x.ShiftStart,
            x.ShiftEnd,
            x.EnterAt,
            x.ExitAt,
            x.EnterStatus,
            x.LateMinutes,
            x.EarlyLeaveMinutes,
            x.WorkMinutes,
            x.DeficitMinutes,
            x.CoveredGapMinutes,
            x.OvertimeMinutes,
            x.PendingOvertimeMinutes,
            x.FinalStatus,
            x.HasApprovedLeave,
            x.Note,
            x.InOutCount,
            x.IsInNow,
            x.UncoveredGaps,
            Segments = x.Segs.Where(s => s.EnterAt.HasValue).OrderBy(s => s.Seq).Select(MapSegment).ToList()
        }).ToList();

        // pagination (سازگار با استاندارد بقیهٔ endpointها) + فیلدهای روز برای گزارش تاریخ‌دار
        if (Paging.Requested(skip, take))
            return Ok(new { date = day, isToday, weekday = PersianDate.WeekdayName(day), stats, total = lightRows.Count, items });
        return Ok(new
        {
            date = day,
            isToday,
            weekday = PersianDate.WeekdayName(day),
            stats,
            items
        });
    }

    // ================== اصلاح رکورد ==================
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AdminUpdateInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup).FirstOrDefaultAsync(a => a.Id == id);
        if (rec == null) return NotFound();
        if (!await UserAllowedAsync(rec.UserId)) return BadRequest(new { message = UserNotAllowed });
        rec.ShiftGroupId = input.ShiftGroupId == 0 ? null : input.ShiftGroupId;
        if (input.FinalStatus != null) rec.FinalStatus = input.FinalStatus;

        // بارگذاری شیفت جدید
        if (rec.ShiftGroupId.HasValue && (rec.ShiftGroup == null || rec.ShiftGroup.Id != rec.ShiftGroupId.Value))
            rec.ShiftGroup = await _db.ShiftGroups.FindAsync(rec.ShiftGroupId.Value);

        // اگر بازه‌های ورود/خروج وجود دارد، ویرایش را روی آن‌ها اعمال و تجمیعی را بازسازی می‌کنیم
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == rec.UserId && s.WorkDate == rec.WorkDate)
            .OrderBy(s => s.Seq)
            .ToListAsync();
        if (segs.Count > 0)
        {
            var first = segs.First();
            if (input.ClearEnter == true) first.EnterAt = null;
            else if (input.EnterAt.HasValue) first.EnterAt = input.EnterAt.Value;
            var last = segs.Last();
            last.ExitAt = input.ExitAt;
            if (!string.IsNullOrWhiteSpace(input.Note)) last.Note = input.Note;
            // اگر بازه‌ی غیبتی بسته شده، پوشش آن را دوباره ارزیابی کن
            for (var i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (s.ExitAt == null) continue;
                var gapEnd = i + 1 < segs.Count ? segs[i + 1].EnterAt : null;
                if (gapEnd.HasValue)
                    await EvaluateGapAsync(s, gapEnd.Value);
            }
            var ruleMt = await ResolveRuleAsync(rec.UserId, rec.WorkDate, rec.ShiftGroup);
            await AggregateAsync(rec, segs, ruleMt, DateTime.Now);
        }
        else
        {
            // رفتار قدیمی (رکورد بدون بازه)
            if (input.EnterAt.HasValue) rec.EnterAt = input.EnterAt.Value;
            if (input.ClearEnter == true) rec.EnterAt = null;
            rec.ExitAt = input.ExitAt;
            rec.Note = input.Note;
            if (rec.EnterAt.HasValue && rec.ExitAt.HasValue)
                rec.WorkMinutes = Math.Max(0, (int)(rec.ExitAt.Value - rec.EnterAt.Value).TotalMinutes);
            var hasDaily = await HasDailyLeaveAsync(rec.UserId, rec.WorkDate);
            rec.HasApprovedLeave = await HasApprovedLeaveAsync(rec.UserId, rec.WorkDate);
            var ruleUpd = await ResolveRuleAsync(rec.UserId, rec.WorkDate, rec.ShiftGroup);
            RecalcRecord(rec, ruleUpd, hasDaily);
        }

        rec.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }
    public class AdminUpdateInput
    {
        public DateTime? EnterAt { get; set; }
        public bool? ClearEnter { get; set; }
        public DateTime? ExitAt { get; set; }
        public string? Note { get; set; }
        public int? ShiftGroupId { get; set; }
        public string? FinalStatus { get; set; }
    }

    // ================== مدیریت بازه‌های ورود/خروج (ادمین) ==================
    [HttpPut("segments/{id:int}")]
    public async Task<IActionResult> UpdateSegment(int id, [FromBody] SegmentUpdateInput input)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var seg = await _db.AttendanceSegments.FirstOrDefaultAsync(s => s.Id == id);
        if (seg == null) return NotFound();
        if (!await UserAllowedAsync(seg.UserId)) return BadRequest(new { message = UserNotAllowed });

        // به‌روزرسانی شرطی: مقدار = تنظیم، پرچپ Clear = حذف، نبود = بدون تغییر
        if (input.ClearEnter == true) seg.EnterAt = null;
        else if (input.EnterAt.HasValue) seg.EnterAt = input.EnterAt.Value;
        if (input.ClearExit == true) seg.ExitAt = null;
        else if (input.ExitAt.HasValue) seg.ExitAt = input.ExitAt.Value;
        if (input.Note != null) seg.Note = input.Note;

        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup).FirstOrDefaultAsync(a => a.UserId == seg.UserId && a.WorkDate == seg.WorkDate);
        if (rec != null)
        {
            var segs = await _db.AttendanceSegments
                .Where(s => s.UserId == seg.UserId && s.WorkDate == seg.WorkDate)
                .OrderBy(s => s.Seq).ToListAsync();
            for (var i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (s.ExitAt == null) continue;
                var gapEnd = i + 1 < segs.Count ? segs[i + 1].EnterAt : null;
                if (gapEnd.HasValue) await EvaluateGapAsync(s, gapEnd.Value);
            }
            var ruleMt = await ResolveRuleAsync(rec.UserId, rec.WorkDate, rec.ShiftGroup);
            await AggregateAsync(rec, segs, ruleMt, DateTime.Now);
            rec.UpdatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }

    [HttpDelete("segments/{id:int}")]
    public async Task<IActionResult> DeleteSegment(int id)
    {
        if (!await HasAsync("ManageShifts")) return Forbid();
        var seg = await _db.AttendanceSegments.FirstOrDefaultAsync(s => s.Id == id);
        if (seg == null) return NotFound();
        if (!await UserAllowedAsync(seg.UserId)) return BadRequest(new { message = UserNotAllowed });

        _db.AttendanceSegments.Remove(seg);
        // شماره‌گذاری مجدد بازه‌های باقی‌مانده
        var rest = await _db.AttendanceSegments
            .Where(s => s.UserId == seg.UserId && s.WorkDate == seg.WorkDate)
            .OrderBy(s => s.Seq).ToListAsync();
        for (var i = 0; i < rest.Count; i++) rest[i].Seq = i + 1;

        var rec = await _db.AttendanceRecords.Include(a => a.ShiftGroup).FirstOrDefaultAsync(a => a.UserId == seg.UserId && a.WorkDate == seg.WorkDate);
        if (rec != null)
        {
            for (var i = 0; i < rest.Count; i++)
            {
                var s = rest[i];
                if (s.ExitAt == null) continue;
                var gapEnd = i + 1 < rest.Count ? rest[i + 1].EnterAt : null;
                if (gapEnd.HasValue) await EvaluateGapAsync(s, gapEnd.Value);
            }
            var ruleDs = await ResolveRuleAsync(seg.UserId, seg.WorkDate, rec.ShiftGroup);
            await AggregateAsync(rec, rest, ruleDs, DateTime.Now);
            rec.UpdatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true });
    }
    public class SegmentUpdateInput
    {
        public DateTime? EnterAt { get; set; }
        public bool? ClearEnter { get; set; }
        public DateTime? ExitAt { get; set; }
        public bool? ClearExit { get; set; }
        public string? Note { get; set; }
    }

    // ================== خروجی اکسل/CSV گزارش ==================
    [HttpGet("monthly-report.csv")]
    public async Task<IActionResult> MonthlyReportCsv([FromQuery] int? jy, [FromQuery] int? jm)
    {
        if (!await IsAdminAsync()) return Forbid();
        var data = await MonthlyReport(jy, jm) as OkObjectResult;
        if (data?.Value == null) return BadRequest();
        var wrap = (dynamic)data.Value;
        int y = (int)wrap.year, m = (int)wrap.month;
        var items = (IEnumerable<dynamic>)wrap.items;

        var sb = new StringBuilder();
        sb.Append('\uFEFF'); // BOM برای فارسی در Excel
        sb.AppendLine("نام کاربر,شیفت,روزکاری,حاضر,غایب,مرخصی(روز),با تاخیر,تاخیر(دقیقه),تعجیل(دقیقه),کارکرد(دقیقه),کسری(دقیقه),کسری(ساعت),اضافه‌کار(دقیقه),مازادِ در انتظار تصمیم(دقیقه),تردد غیرمجاز(دقیقه)");
        foreach (var r in items)
        {
            sb.AppendJoin(',',
                r.UserName, r.ShiftName, r.Workdays, r.Present, r.Absent, r.LeaveDays,
                r.LateDays, r.TotalLateMinutes, r.TotalEarlyLeaveMinutes,
                r.WorkMinutes, r.DeficitMinutes, r.DeficitHours,
                r.OvertimeMinutes, r.PendingOvertimeMinutes, r.UnauthorizedMinutes);
            sb.AppendLine();
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"attendance-monthly-{y}-{m}.csv");
    }

    // ================== کمک‌ها ==================
    private async Task<List<int>> GetAttendanceManagerIds()
    {
        var rbac = await _db.UserRoles
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == Module && (x.Action == "ManageShifts" || x.Action == "ViewAll" || x.Action == "Report"))
            .Select(x => x.UserId).Distinct().ToListAsync();
        var legacyAdmins = await _db.Users.Where(u => u.Role == "Admin" && u.IsActive && !_db.UserRoles.Any(ur => ur.UserId == u.Id)).Select(u => u.Id).ToListAsync();
        var allowed = await _fa.UserIdsAsync();
        return rbac.Concat(legacyAdmins).Distinct().Where(allowed.Contains).ToList();
    }

    private static bool IsWorkday(DateTime date, ShiftGroup? sg)
    {
        if (sg != null && !sg.IncludeFriday && date.DayOfWeek == DayOfWeek.Friday) return false;
        return true;
    }

    private static int WorkdayCount(DateTime from, DateTime to)
    {
        int c = 0;
        for (var d = from; d < to; d = d.AddDays(1))
            if (d.DayOfWeek != DayOfWeek.Friday) c++;
        return c;
    }

    private static object Map(AttendanceRecord r, List<AttendanceSegment>? segs = null) => new
    {
        r.Id,
        r.WorkDate,
        r.UserId,
        r.UserName,
        r.ShiftGroupId,
        ShiftName = r.ShiftGroup?.Name,
        ShiftStart = r.ShiftGroup?.StartTime,
        ShiftEnd = r.ShiftGroup?.EndTime,
        r.EnterAt,
        r.ExitAt,
        r.EnterIp, r.ExitIp,
        r.EnterStatus,
        r.LateMinutes,
        r.EarlyLeaveMinutes,
        r.WorkMinutes,
        r.DeficitMinutes,
        r.CoveredGapMinutes,
        r.OvertimeMinutes,
        r.UnauthorizedMinutes,
        r.PendingOvertimeMinutes,
        r.HasApprovedLeave,
        r.FinalStatus,
        r.Note,
        r.CreatedAt, r.UpdatedAt,
        InOutCount = segs?.Count(s => s.EnterAt.HasValue) ?? 0,
        Segments = segs?.Where(s => s.EnterAt.HasValue).OrderBy(s => s.Seq).Select(MapSegment).ToList() ?? new List<object>()
    };

    private static object MapSegment(AttendanceSegment s) => new
    {
        s.Id,
        s.Seq,
        s.EnterAt,
        s.ExitAt,
        s.EnterStatus,
        s.LateMinutes,
        s.ExitCovered,
        s.IsUnauthorized,
        s.OvertimeMinutes,
        s.PendingMinutes,
        s.ExtraDecision,
        ExtraDecisionFa = ExtraDecisionFa(s.ExtraDecision),
        s.ExtraDecidedAt,
        s.ExtraDecisionNote,
        s.LinkedLeaveNumber,
        s.Note
    };

    /// <summary>عنوان فارسی تصمیم مازاد (برای نمایش در رابط کاربری)</summary>
    private static string ExtraDecisionFa(int v) => v switch
    {
        (int)ExtraDecision.Approved => "تأیید شد (اضافه‌کار)",
        (int)ExtraDecision.Rejected => "رد شد (تردد غیرمجاز)",
        _ => "در انتظار تصمیم"
    };

    // ================== تصمیم مدیر درباره‌ی «مازاد» (ماندن بعد از پایان شیفت) ==================

    /// <summary>
    /// کارتابل مازاد: بازه‌هایی که پرسنل بعد از پایان شیفت در محل مانده‌اند و هنوز تصمیمی درباره‌ی
    /// آن‌ها گرفته نشده (نه کسری، نه اضافه‌کار، نه تردد غیرمجاز قطعی).
    /// مدیر می‌تواند «تأیید» کند (→ اضافه‌کاری) یا «رد» کند (→ تردد غیرمجاز).
    /// </summary>
    [HttpGet("extra-worklog")]
    public async Task<IActionResult> GetExtraWorklog([FromQuery] bool pendingOnly = true, [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await HasAsync("ManageShifts") && !await IsAdminAsync()) return Forbid();

        var f = (from ?? DateTime.Today.AddDays(-30)).Date;
        var t = (to ?? DateTime.Today).Date.AddDays(1);

        var allowedU = await AllowedUsersAsync();
        var segs = await _db.AttendanceSegments.AsNoTracking()
            .Where(s => s.WorkDate >= f && s.WorkDate < t && s.EnterAt != null && allowedU.Contains(s.UserId))
            .OrderByDescending(s => s.WorkDate).ThenBy(s => s.Seq)
            .ToListAsync();

        // فقط بازه‌هایی که مازادِ بعد از پایان شیفت داشته‌اند (منتظر تصمیم یا تصمیم‌گرفته‌شده)
        var rows = segs
            .Where(s => s.PendingMinutes > 0
                        || s.OvertimeMinutes > 0
                        || s.IsUnauthorized
                        || s.ExtraDecision != (int)ExtraDecision.Pending)
            .Where(s => !pendingOnly || s.ExtraDecision == (int)ExtraDecision.Pending)
            .ToList();

        var recMap = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => r.WorkDate >= f && r.WorkDate < t)
            .ToDictionaryAsync(r => new { r.UserId, r.WorkDate });

        var shiftMap = await _db.ShiftGroups.AsNoTracking().ToDictionaryAsync(s => s.Id);

        var items = rows.Select(s =>
        {
            recMap.TryGetValue(new { s.UserId, s.WorkDate }, out var rec);
            var sgName = rec?.ShiftGroupId != null && shiftMap.TryGetValue(rec.ShiftGroupId.Value, out var sg) ? sg.Name : null;
            var sgWindow = rec?.ShiftGroupId != null && shiftMap.TryGetValue(rec.ShiftGroupId.Value, out var sg2)
                ? $"{Tm(sg2.StartTime)}–{Tm(sg2.EndTime)}" : null;
            return new
            {
                segmentId = s.Id,
                s.UserId,
                s.UserName,
                s.WorkDate,
                dateFa = Shared.PersianDate.ToShort(s.WorkDate),
                s.Seq,
                s.EnterAt,
                s.ExitAt,
                shiftName = sgName,
                shiftWindow = sgWindow,
                s.PendingMinutes,
                s.OvertimeMinutes,
                s.IsUnauthorized,
                s.ExtraDecision,
                decisionFa = ExtraDecisionFa(s.ExtraDecision),
                s.ExtraDecidedAt,
                s.ExtraDecisionNote,
                recordId = rec?.Id,
                workMinutes = rec?.WorkMinutes ?? 0,
                deficitMinutes = rec?.DeficitMinutes ?? 0,
            };
        }).ToList();

        return Ok(Paging.Result(items, skip, take));
    }

    public class ExtraDecisionInput
    {
        /// <summary>شناسه‌ی بازه (اگر خالی باشد، همه‌ی بازه‌های مازادِ آن روز اعمال می‌شود)</summary>
        public int? SegmentId { get; set; }
        public int UserId { get; set; }
        public DateTime Date { get; set; }
        /// <summary>۰ = بازگشت به «در انتظار تصمیم» | ۱ = تأیید (اضافه‌کار) | ۲ = رد (تردد غیرمجاز)</summary>
        public int Decision { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>
    /// ثبت تصمیم مدیر روی مازاد: تأیید → اضافه‌کاری، رد → تردد غیرمجاز، بازگشت → در انتظار تصمیم.
    /// تصمیم روی خودِ بازه ذخیره و بلافاصله رکوردِ روز بازحساب می‌شود.
    /// </summary>
    [HttpPost("extra-decision")]
    public async Task<IActionResult> SetExtraDecision([FromBody] ExtraDecisionInput input)
    {
        if (!await HasAsync("ManageShifts") && !await IsAdminAsync()) return Forbid();
        if (input.Decision is < 0 or > 2)
            return BadRequest(new { message = "تصمیم نامعتبر است." });
        if (!await UserAllowedAsync(input.UserId)) return BadRequest(new { message = UserNotAllowed });

        var date = input.Date.Date;
        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == input.UserId && s.WorkDate == date)
            .OrderBy(s => s.Seq)
            .ToListAsync();
        if (segs.Count == 0) return NotFound(new { message = "بازه‌ی ترددی برای این روز یافت نشد." });

        var targets = input.SegmentId is > 0
            ? segs.Where(s => s.Id == input.SegmentId.Value).ToList()
            : segs.Where(s => s.ExtraDecision == (int)ExtraDecision.Pending
                              || s.PendingMinutes > 0
                              || s.OvertimeMinutes > 0
                              || s.IsUnauthorized).ToList();
        if (targets.Count == 0) return BadRequest(new { message = "بازه‌ای برای اعمال تصمیم یافت نشد." });

        foreach (var s in targets)
        {
            s.ExtraDecision = input.Decision;
            s.ExtraDecidedByUserId = input.Decision == (int)ExtraDecision.Pending ? null : MyUserId;
            s.ExtraDecidedAt = input.Decision == (int)ExtraDecision.Pending ? null : DateTime.Now;
            s.ExtraDecisionNote = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        }
        await _db.SaveChangesAsync();

        await _recalc.RecalcDayAsync(input.UserId, date, DateTime.Now);
        var rec = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.UserId == input.UserId && a.WorkDate == date);
        var fresh = await _db.AttendanceSegments
            .Where(s => s.UserId == input.UserId && s.WorkDate == date).OrderBy(s => s.Seq).ToListAsync();

        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new
        {
            ok = true,
            applied = targets.Count,
            message = input.Decision switch
            {
                (int)ExtraDecision.Approved => $"مازادِ {Fa.Digits(rec?.OvertimeMinutes ?? 0)} دقیقه به اضافه‌کاری منتقل شد.",
                (int)ExtraDecision.Rejected => $"مازاد به تردد غیرمجاز منتقل شد.",
                _ => "مازاد به حالت «در انتظار تصمیم» بازگشت.",
            },
            record = rec == null ? null : Map(rec, fresh),
        });
    }

    /// <summary>بازحساب اجباری یک روز (مثلاً بعد از تغییر شیفت یا تقویم کاری) — پرسنل و مدیر.</summary>
    [HttpPost("recalc-day")]
    public async Task<IActionResult> RecalcDay([FromBody] ExtraDecisionInput input)
    {
        if (!await HasAsync("ManageShifts") && !await IsAdminAsync()) return Forbid();
        if (!await UserAllowedAsync(input.UserId)) return BadRequest(new { message = UserNotAllowed });
        await _recalc.RecalcDayAsync(input.UserId, input.Date.Date, DateTime.Now);
        var rec = await _db.AttendanceRecords.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == input.UserId && a.WorkDate == input.Date.Date);
        var segs = await _db.AttendanceSegments.AsNoTracking()
            .Where(s => s.UserId == input.UserId && s.WorkDate == input.Date.Date).OrderBy(s => s.Seq).ToListAsync();
        return Ok(new { ok = true, record = rec == null ? null : Map(rec, segs) });
    }

    public class RecalcMonthInput
    {
        public int Year { get; set; }
        public int Month { get; set; }
        /// <summary>خالی = همه‌ی پرسنل</summary>
        public int? UserId { get; set; }
    }

    /// <summary>
    /// بازحسابِ کلِ ماه — بعد از تغییرِ قاعده (مثلاً اصلاح اولویت شیفت بر تقویم یا فعال‌شدن
    /// «مازاد در انتظار تصمیم») لازم است تا رکوردهای روزهای گذشته هم با قاعده‌ی جدید هماهنگ شوند.
    /// تصمیم‌های ثبت‌شده‌ی مازاد دست‌نخورده می‌مانند.
    /// </summary>
    [HttpPost("recalc-month")]
    public async Task<IActionResult> RecalcMonth([FromBody] RecalcMonthInput input)
    {
        if (!await HasAsync("ManageShifts") && !await IsAdminAsync()) return Forbid();
        if (input.Month is < 1 or > 12) return BadRequest(new { message = "ماه نامعتبر است." });
        var from = PersianDate.ToGregorian(input.Year, input.Month, 1);
        if (from == DateTime.MinValue) return BadRequest(new { message = "سال/ماه نامعتبر است." });
        var to = input.Month == 12 ? PersianDate.ToGregorian(input.Year + 1, 1, 1) : PersianDate.ToGregorian(input.Year, input.Month + 1, 1);
        if (to <= from) return BadRequest(new { message = "بازه‌ی ماه معتبر نیست." });

        var allowedU = await AllowedUsersAsync();
        var q = _db.AttendanceRecords.AsNoTracking().Where(r => r.WorkDate >= from && r.WorkDate < to && allowedU.Contains(r.UserId));
        if (input.UserId is > 0) q = q.Where(r => r.UserId == input.UserId);
        var targets = await q.Select(r => new { r.UserId, r.WorkDate }).Distinct().ToListAsync();

        var now = DateTime.Now;
        var done = 0;
        foreach (var t in targets)
        {
            try { await _recalc.RecalcDayAsync(t.UserId, t.WorkDate, now); done++; }
            catch (Exception ex) { Console.WriteLine($"[Attendance] بازحساب {t.UserId}/{t.WorkDate:yyyy-MM-dd}: {ex.Message}"); }
        }
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true, recalculated = done, total = targets.Count });
    }

    // ================== امنیت حضور و غیاب (مدیر) ==================

    /// <summary>لیست هشدارهای امنیتی (دستگاه جدید / دستگاه مشترک / خارج از محدوده).</summary>
    [HttpGet("security/alerts")]
    public async Task<IActionResult> GetSecurityAlerts([FromQuery] bool pendingOnly = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsAdminAsync()) return Forbid();
        var allowedU = await AllowedUsersAsync();
        var q = _db.AttendanceAlerts.AsNoTracking().Where(a => allowedU.Contains(a.UserId));
        if (pendingOnly) q = q.Where(a => a.Status == AttendanceSecurityService.StatusPending);
        var pg = await Paging.QueryAsync(q.OrderByDescending(a => a.CreatedAt), skip, take, 100);
        var list = pg.Rows;
        return Ok(pg.Result(list.Select(a => new
        {
            a.Id,
            a.UserId,
            a.UserName,
            a.AlertType,
            a.Message,
            a.DeviceId,
            a.Ip,
            a.Lat,
            a.Lng,
            a.DistanceMeters,
            a.Status,
            a.CreatedAt,
            a.HandledAt,
            PersianDate = Shared.PersianDate.ToShortWithTime(a.CreatedAt),
            TypeFa = a.AlertType switch
            {
                AttendanceSecurityService.AlertNewDevice => "دستگاه جدید",
                AttendanceSecurityService.AlertSharedDevice => "دستگاه مشترک",
                AttendanceSecurityService.AlertOutOfRange => "خارج از محدوده",
                _ => a.AlertType
            },
            StatusFa = a.Status switch
            {
                AttendanceSecurityService.StatusPending => "در انتظار بررسی",
                AttendanceSecurityService.StatusApproved => "تأیید شده",
                _ => "رد شده"
            }
        })));
    }

    /// <summary>تأیید دستگاه جدید توسط مدیر — دستگاهِ جدید، اصلی می‌شود.</summary>
    [HttpPost("security/alerts/{id:int}/approve")]
    public async Task<IActionResult> ApproveSecurityAlert(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        var ok = await _security.ApproveDeviceAsync(id, MyUserId);
        if (!ok) return NotFound(new { message = "هشدار یافت نشد یا دیگر در انتظار بررسی نیست." });
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true, message = "دستگاه جدید به‌عنوان دستگاه اصلی تأیید شد." });
    }

    /// <summary>رد دستگاه جدید توسط مدیر.</summary>
    [HttpPost("security/alerts/{id:int}/reject")]
    public async Task<IActionResult> RejectSecurityAlert(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        var ok = await _security.RejectDeviceAsync(id, MyUserId);
        if (!ok) return NotFound(new { message = "هشدار یافت نشد یا دیگر در انتظار بررسی نیست." });
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true, message = "درخواست دستگاه جدید رد شد." });
    }

    /// <summary>بستن هشدار بدون تغییر دستگاه (فقط رسیدگی‌شده).</summary>
    [HttpPost("security/alerts/{id:int}/dismiss")]
    public async Task<IActionResult> DismissSecurityAlert(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        var ok = await _security.DismissAlertAsync(id, MyUserId);
        if (!ok) return NotFound(new { message = "هشدار یافت نشد." });
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new { ok = true, message = "هشدار بسته شد." });
    }

    /// <summary>محدوده‌ی مکانی مجاز برای ثبت حضور و غیاب.</summary>
    [HttpGet("security/area")]
    public async Task<IActionResult> GetSecurityArea()
    {
        if (!await IsAdminAsync()) return Forbid();
        var area = await _security.GetAreaAsync();
        return Ok(area == null
            ? new { configured = false, Latitude = (double?)null, Longitude = (double?)null, RadiusMeters = 1000, LocationName = "" }
            : new { configured = true, area.Latitude, area.Longitude, area.RadiusMeters, area.LocationName, area.UpdatedAt });
    }

    /// <summary>تنظیم محدوده‌ی مکانی مجاز (مرکز + شعاع به متر).</summary>
    [HttpPost("security/area")]
    public async Task<IActionResult> SaveSecurityArea([FromBody] AreaInput input)
    {
        if (!await IsAdminAsync()) return Forbid();
        if (input == null || !input.Latitude.HasValue || !input.Longitude.HasValue)
            return BadRequest(new { message = "مختصات مرکز (طول و عرض جغرافیایی) الزامی است." });
        var radius = input.RadiusMeters ?? 1000;
        if (radius < 50) radius = 50;
        var area = await _security.SaveAreaAsync(input.Latitude.Value, input.Longitude.Value, radius, input.LocationName, MyUserId);
        await _notify.BroadcastChangedAsync("attendance");
        return Ok(new
        {
            ok = true,
            message = "محدوده‌ی مکانی مجاز ذخیره شد.",
            area.Latitude, area.Longitude, area.RadiusMeters, area.LocationName
        });
    }

    public class AreaInput
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? RadiusMeters { get; set; }
        public string? LocationName { get; set; }
    }
}
