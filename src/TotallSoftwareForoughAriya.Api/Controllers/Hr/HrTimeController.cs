using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>API تکمیلی حضوروغیاب و زمان‌بندی: قوانین، موجودی، تصویب چندمرحله‌ای، اضافه‌کاری، روستِر، دستگاه</summary>
[ApiController]
[Route("api/hr-time")]
[Authorize]
public class HrTimeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HrTimeService _svc;
    private readonly FaHrPersonnelService _personnel;

    // این ماژول به سرویس کارگزینیِ منابع انسانی اصلی (IHrCoreService) وابسته نیست؛ پرسنل در جدول اختصاصی FaHrPersonnel است.
    public HrTimeController(AppDbContext db, HrTimeService svc, FaHrPersonnelService personnel) { _db = db; _svc = svc; _personnel = personnel; }

    /// <summary>کاربرِ دیگر (غیر از خودم) فقط وقتی قابل مشاهده/ویرایش است که فعال و دارای دسترسی به ماژول باشد.</summary>
    private async Task<bool> UserOkAsync(int uid) => uid == MyUserId || await _svc.Access.IsAllowedUserAsync(uid);
    private async Task<List<int>> AllowedUsersAsync() => (await _svc.Access.UserIdsAsync()).ToList();
    private async Task<List<int>> AllowedEmpAsync() => (await _svc.Access.PersonnelIdsAsync()).ToList();
    private const string UserNotAllowed = "کاربر فعال نیست یا به «منابع انسانی فروغ آریا» دسترسی ندارد.";

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyName => User.FindFirstValue(ClaimTypes.Name) ?? "";

    private bool? _isHr;
    private async Task<bool> IsHrAsync()
    {
        if (_isHr.HasValue) return _isHr.Value;
        if (User.IsInRole("Admin")) { _isHr = true; return true; }
        if (User.HasClaim("permission", "LeaveRequests.Approve")) { _isHr = true; return true; }
        _isHr = await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, pm => pm.Id, (rp, pm) => pm)
            .AnyAsync(pm => pm.Module == "LeaveRequests" && pm.Action == "Approve");
        return _isHr.Value;
    }

    // ================= قوانین =================

    [HttpGet("rules")]
    public async Task<IActionResult> Rules([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsHrAsync()) return Forbid();
        var pagination = new Paging.Request(skip, take);
        var rules = await _svc.GetRulesAsync(pagination);
        return Ok(pagination.DictionaryResult(rules));
    }

    [HttpPut("rules")]
    public async Task<IActionResult> SaveRules([FromBody] Dictionary<string, string> values)
    {
        if (!await IsHrAsync()) return Forbid();
        await _svc.SaveRulesAsync(values ?? new());
        return Ok(new { ok = true });
    }

    // ================= موجودی مرخصی =================

    [HttpGet("balances")]
    public async Task<IActionResult> Balances([FromQuery] int? userId, [FromQuery] int? year, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var uid = userId ?? MyUserId;
        if (uid != MyUserId && !await IsHrAsync()) return Forbid();
        if (!await UserOkAsync(uid)) return BadRequest(new { message = UserNotAllowed });
        var jy = year ?? HrTimeService.JalaliYear(DateTime.Now);
        // صفحه‌بندی در سرویس انجام می‌شود؛ شکل پاسخ { year, items } و در حالت صفحه‌بندی { year, total, items } است.
        var pagination = new Paging.Request(skip, take);
        var items = await _svc.GetBalancesAsync(uid, jy, pagination);
        if (pagination.IsPaged) return Ok(new { year = jy, total = pagination.Total ?? items.Count, items });
        return Ok(new { year = jy, items });
    }

    public class GrantInput { public int UserId { get; set; } public int Year { get; set; } public string Category { get; set; } = "Annual"; public double Days { get; set; } }

    [HttpPost("balances/grant")]
    public async Task<IActionResult> Grant([FromBody] GrantInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        if (input.Category is not ("Annual" or "Sick" or "Maternity" or "Marriage" or "Bereavement" or "Hajj" or "Unpaid"))
            return BadRequest(new { message = "دسته مرخصی نامعتبر است." });
        if (!await _svc.Access.IsAllowedUserAsync(input.UserId)) return BadRequest(new { message = UserNotAllowed });
        await _svc.SetGrantAsync(input.UserId, input.Year, input.Category, Math.Max(0, input.Days));
        return Ok(new { ok = true });
    }

    // ================= مراحل تصویب =================

    [HttpGet("steps/pending")]
    public async Task<IActionResult> PendingSteps([FromQuery] bool all = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var hr = await IsHrAsync();
        var q = _db.HrRequestSteps.AsNoTracking().Where(s => s.Status == "Pending");
        if (all && hr) { /* همه */ }
        else if (hr) q = q.Where(s => s.Role == "Hr" || s.ApproverUserId == MyUserId);
        else q = q.Where(s => s.ApproverUserId == MyUserId);
        q = q.Where(s => s.RequestType != "Leave" || _db.LeaveRequests.Any(l => l.Id == s.RequestId && l.Status == "Pending"));
        // فقط درخواست‌های کاربرانِ فعالِ دارای دسترسی به ماژول
        var allowedU = await AllowedUsersAsync();
        q = q.Where(s => (s.RequestType == "Leave" && _db.LeaveRequests.Any(l => l.Id == s.RequestId && allowedU.Contains(l.RequesterUserId)))
                      || (s.RequestType == "Overtime" && _db.HrOvertimeRequests.Any(o => o.Id == s.RequestId && allowedU.Contains(o.RequesterUserId)))
                      || (s.RequestType != "Leave" && s.RequestType != "Overtime"));
        var steps = await q.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).ToPageListAsync(pagination, defaultCap: 200);
        var leaveIds = steps.Where(s => s.RequestType == "Leave").Select(s => s.RequestId).Distinct().ToList();
        var otIds = steps.Where(s => s.RequestType == "Overtime").Select(s => s.RequestId).Distinct().ToList();
        var leaves = await _db.LeaveRequests.AsNoTracking().Where(l => leaveIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id);
        var ots = await _db.HrOvertimeRequests.AsNoTracking().Where(o => otIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id);
        return Ok(pagination.Result(steps.Select(s =>
        {
            leaves.TryGetValue(s.RequestId, out var l);
            ots.TryGetValue(s.RequestId, out var o);
            var isLeave = s.RequestType == "Leave";
            return new
            {
                s.Id, s.RequestType, s.RequestId, s.StepNo, s.Role, s.ApproverName, s.CreatedAt,
                Number = isLeave ? l?.Number : o?.Number,
                Requester = isLeave ? l?.RequesterName : o?.RequesterName,
                Title = isLeave
                    ? l?.Type switch { "Hourly" => "مرخصی ساعتی", "HourlyMission" => "ماموریت ساعتی", "Mission" => "ماموریت", _ => "مرخصی روزانه" }
                    : "اضافه‌کاری",
                Detail = isLeave
                    ? $"{l?.StartDate:yyyy/MM/dd} تا {l?.EndDate:yyyy/MM/dd}" + (string.IsNullOrWhiteSpace(l?.Destination) ? "" : $" — {l.Destination}")
                    : $"{o?.WorkDate:yyyy/MM/dd} {o?.StartTime:hh\\:mm}–{o?.EndTime:hh\\:mm}",
            };
        })));
    }

    [HttpGet("steps")]
    public async Task<IActionResult> Timeline([FromQuery] string requestType, [FromQuery] int requestId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var hr = await IsHrAsync();
        var steps = await _db.HrRequestSteps.AsNoTracking()
            .Where(s => s.RequestType == requestType && s.RequestId == requestId)
            .OrderBy(s => s.StepNo).ThenBy(x => x.Id).ToPageListAsync(pagination);
        if (!hr)
        {
            var mine = requestType == "Overtime"
                ? await _db.HrOvertimeRequests.AnyAsync(o => o.Id == requestId && o.RequesterUserId == MyUserId)
                : await _db.LeaveRequests.AnyAsync(l => l.Id == requestId && l.RequesterUserId == MyUserId);
            var involved = await _db.HrRequestSteps.AnyAsync(s => s.RequestType == requestType
                && s.RequestId == requestId && s.ApproverUserId == MyUserId);
            if (!mine && !involved) return Forbid();
        }
        return Ok(pagination.Result(steps));
    }

    public class DecideInput { public bool Approve { get; set; } public string? Note { get; set; } }

    [HttpPost("steps/{id:int}/decide")]
    public async Task<IActionResult> Decide(int id, [FromBody] DecideInput input)
    {
        var r = await _svc.DecideStepAsync(id, MyUserId, MyName, await IsHrAsync(), input?.Approve == true, input?.Note);
        if (!r.Ok) return BadRequest(new { message = r.Message });
        return Ok(new { ok = true, message = r.Message });
    }

    // ================= اضافه‌کاری =================

    [HttpGet("overtime")]
    public async Task<IActionResult> Overtime([FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? userId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var hr = await IsHrAsync();
        var q = _db.HrOvertimeRequests.AsNoTracking().AsQueryable();
        if (!hr || userId == null) { if (!hr) q = q.Where(o => o.RequesterUserId == MyUserId); }
        else q = q.Where(o => o.RequesterUserId == userId);
        if (hr)
        {
            var allowedU = await AllowedUsersAsync();
            q = q.Where(o => o.RequesterUserId == MyUserId || allowedU.Contains(o.RequesterUserId));
        }
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(o => o.Status == status);
        if (from != null) q = q.Where(o => o.WorkDate >= from.Value.Date);
        if (to != null) q = q.Where(o => o.WorkDate <= to.Value.Date);
        return Ok((await Paging.QueryAsync(q.OrderByDescending(o => o.WorkDate), skip, take, 500)).Result());
    }

    public class OvertimeInput
    {
        public DateTime WorkDate { get; set; }
        public string StartTime { get; set; } = "";
        public string EndTime { get; set; } = "";
        public string Type { get; set; } = "Normal";
        public string? Reason { get; set; }
    }

    [HttpPost("overtime")]
    public async Task<IActionResult> CreateOvertime([FromBody] OvertimeInput input)
    {
        if (input.Type is not ("Normal" or "Holiday" or "Night"))
            return BadRequest(new { message = "نوع اضافه‌کاری نامعتبر است." });
        if (!TimeSpan.TryParse(input.StartTime, out var st) || !TimeSpan.TryParse(input.EndTime, out var en))
            return BadRequest(new { message = "قالب ساعت نامعتبر است (HH:mm)." });
        try
        {
            var o = await _svc.CreateOvertimeAsync(MyUserId, MyName, input.WorkDate, st, en, input.Type, input.Reason);
            return Ok(new { id = o.Id, number = o.Number });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("overtime/{id:int}")]
    public async Task<IActionResult> DeleteOvertime(int id)
    {
        var o = await _db.HrOvertimeRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound();
        if (o.RequesterUserId != MyUserId && !await IsHrAsync()) return Forbid();
        if (o.Status != "Pending") return BadRequest(new { message = "فقط درخواست در انتظار قابل حذف است." });
        _db.HrRequestSteps.RemoveRange(_db.HrRequestSteps.Where(s => s.RequestType == "Overtime" && s.RequestId == id));
        _db.HrOvertimeRequests.Remove(o);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= روستِر =================

    [HttpGet("roster")]
    public async Task<IActionResult> Roster([FromQuery] int? userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var uid = userId ?? MyUserId;
        if (uid != MyUserId && !await IsHrAsync()) return Forbid();
        if (!await UserOkAsync(uid)) return BadRequest(new { message = UserNotAllowed });
        var f = (from ?? DateTime.Today).Date; var t = (to ?? DateTime.Today.AddDays(30)).Date;
        var pg = await Paging.QueryAsync(_db.HrShiftRosters.AsNoTracking()
            .Where(r => r.UserId == uid && r.Date >= f && r.Date <= t).OrderBy(r => r.Date), skip, take);
        var rows = pg.Rows;
        var shifts = await _db.ShiftGroups.AsNoTracking().ToDictionaryAsync(s => s.Id);
        return Ok(pg.Result(rows.Select(r => new
        {
            r.Id, r.UserId, r.Date, r.ShiftGroupId,
            ShiftName = shifts.TryGetValue(r.ShiftGroupId, out var s) ? s.Name : null,
        })));
    }

    public class RosterItem { public int UserId { get; set; } public DateTime Date { get; set; } public int ShiftGroupId { get; set; } }

    [HttpPost("roster")]
    public async Task<IActionResult> SaveRoster([FromBody] List<RosterItem> items)
    {
        if (!await IsHrAsync()) return Forbid();
        if (items == null || items.Count == 0 || items.Count > 1000)
            return BadRequest(new { message = "فهرست نامعتبر است." });
        var shiftIds = items.Select(i => i.ShiftGroupId).Distinct().ToList();
        var ok = await _db.ShiftGroups.CountAsync(s => shiftIds.Contains(s.Id));
        if (ok != shiftIds.Count) return BadRequest(new { message = "یکی از شیفت‌ها معتبر نیست." });
        var allowedSet = await _svc.Access.UserIdsAsync();
        if (items.Any(i => !allowedSet.Contains(i.UserId))) return BadRequest(new { message = UserNotAllowed });
        var n = 0;
        foreach (var it in items)
        {
            var d = it.Date.Date;
            var ex = await _db.HrShiftRosters.FirstOrDefaultAsync(r => r.UserId == it.UserId && r.Date == d);
            if (ex == null) { _db.HrShiftRosters.Add(new HrShiftRoster { UserId = it.UserId, Date = d, ShiftGroupId = it.ShiftGroupId }); n++; }
            else if (ex.ShiftGroupId != it.ShiftGroupId) { ex.ShiftGroupId = it.ShiftGroupId; n++; }
        }
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, count = n });
    }

    public class GenerateInput
    {
        public List<int> UserIds { get; set; } = new();
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<int> Pattern { get; set; } = new();
        public int StartOffset { get; set; }
    }

    [HttpPost("roster/generate")]
    public async Task<IActionResult> GenerateRoster([FromBody] GenerateInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            var n = await _svc.GenerateRosterAsync(input.UserIds, input.From, input.To, input.Pattern, input.StartOffset);
            return Ok(new { ok = true, count = n });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("roster/{id:int}")]
    public async Task<IActionResult> DeleteRoster(int id)
    {
        if (!await IsHrAsync()) return Forbid();
        var r = await _db.HrShiftRosters.FindAsync(id);
        if (r == null) return NotFound();
        _db.HrShiftRosters.Remove(r);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= دستگاه =================

    [HttpGet("device/mappings")]
    public async Task<IActionResult> Mappings([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsHrAsync()) return Forbid();
        var allowedU = await AllowedUsersAsync();
        return Ok((await Paging.QueryAsync(_db.HrDeviceUserMaps.AsNoTracking().Where(m => allowedU.Contains(m.SystemUserId)).OrderBy(m => m.UserCode), skip, take, 1000)).Result());
    }

    public class MapInput { public string? DeviceCode { get; set; } public string UserCode { get; set; } = ""; public int SystemUserId { get; set; } }

    [HttpPost("device/mappings")]
    public async Task<IActionResult> SaveMapping([FromBody] MapInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        if (string.IsNullOrWhiteSpace(input.UserCode)) return BadRequest(new { message = "کد پرسنلی دستگاه را وارد کنید." });
        if (!await _db.Users.AnyAsync(u => u.Id == input.SystemUserId)) return BadRequest(new { message = "کاربر معتبر نیست." });
        if (!await _svc.Access.IsAllowedUserAsync(input.SystemUserId)) return BadRequest(new { message = UserNotAllowed });
        var ex = await _db.HrDeviceUserMaps.FirstOrDefaultAsync(m => m.UserCode == input.UserCode && (m.DeviceCode ?? "") == (input.DeviceCode ?? ""));
        if (ex == null) _db.HrDeviceUserMaps.Add(new HrDeviceUserMap { DeviceCode = input.DeviceCode, UserCode = input.UserCode.Trim(), SystemUserId = input.SystemUserId });
        else ex.SystemUserId = input.SystemUserId;
        // ترددهای قبلیِ بدون نگاشت را هم به‌روزرسانی کن
        var olds = await _db.HrDevicePunches.Where(p => p.UserCode == input.UserCode && p.MappedUserId == null
            && (input.DeviceCode == null || p.DeviceCode == input.DeviceCode)).ToListAsync();
        foreach (var p in olds) p.MappedUserId = input.SystemUserId;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("device/mappings/{id:int}")]
    public async Task<IActionResult> DeleteMapping(int id)
    {
        if (!await IsHrAsync()) return Forbid();
        var m = await _db.HrDeviceUserMaps.FindAsync(id);
        if (m == null) return NotFound();
        _db.HrDeviceUserMaps.Remove(m);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("device/upload")]
    public async Task<IActionResult> UploadPunches([FromForm] IFormFile file, [FromForm] string? deviceCode)
    {
        if (!await IsHrAsync()) return Forbid();
        if (file == null || file.Length == 0) return BadRequest(new { message = "فایل انتخاب نشده است." });
        deviceCode = string.IsNullOrWhiteSpace(deviceCode) ? Request.Query["deviceCode"].ToString() : deviceCode;
        if (string.IsNullOrWhiteSpace(deviceCode)) return BadRequest(new { message = "کد دستگاه را وارد کنید." });
        var name = file.FileName.ToLowerInvariant();
        if (!name.EndsWith(".xlsx") && !name.EndsWith(".csv") && !name.EndsWith(".txt"))
            return BadRequest(new { message = "فقط فایل Excel (.xlsx) یا CSV پذیرفته می‌شود." });
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var r = await _svc.ImportPunchesAsync(ms, file.FileName, deviceCode.Trim());
        return Ok(new { ok = true, imported = r.Imported, skipped = r.Skipped, errors = r.Errors });
    }

    public class ApplyInput { public DateTime From { get; set; } public DateTime To { get; set; } }

    [HttpPost("device/apply")]
    public async Task<IActionResult> ApplyPunches([FromBody] ApplyInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            var r = await _svc.ApplyPunchesAsync(input.From, input.To);
            return Ok(new { ok = true, punches = r.Punches, records = r.Records, unmapped = r.Unmapped });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("device/punches")]
    public async Task<IActionResult> Punches([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] bool unmappedOnly = false, [FromQuery] bool unappliedOnly = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsHrAsync()) return Forbid();
        var q = _db.HrDevicePunches.AsNoTracking().AsQueryable();
        if (from != null) q = q.Where(p => p.PunchTime >= from.Value.Date);
        if (to != null) q = q.Where(p => p.PunchTime < to.Value.Date.AddDays(1));
        if (unmappedOnly) q = q.Where(p => p.MappedUserId == null);
        if (unappliedOnly) q = q.Where(p => p.AppliedRecordId == null);
        // ترددهای نگاشت‌شده به کاربرِ غیرفعال/بدون دسترسی نمایش داده نمی‌شوند (ترددهای بدون نگاشت برای نگاشت‌کردن باقی می‌مانند)
        var allowedU = await AllowedUsersAsync();
        q = q.Where(p => p.MappedUserId == null || allowedU.Contains(p.MappedUserId.Value));
        return Ok((await Paging.QueryAsync(q.OrderByDescending(p => p.PunchTime), skip, take, 500)).Result());
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!await IsHrAsync()) return Forbid();
        // فقط کاربرانِ فعالِ دارای دسترسی به «منابع انسانی فروغ آریا»
        var allowedU = await AllowedUsersAsync();
        var list = await _db.Users.AsNoTracking().Where(u => u.IsActive && allowedU.Contains(u.Id)).OrderBy(u => u.Username).ThenBy(x => x.Id)
            .Select(u => new { u.Id, u.Username, FullName = string.IsNullOrWhiteSpace(u.FirstName) ? u.Username : (u.FirstName + " " + u.LastName).Trim() }).ToPageListAsync(pagination, defaultCap: 2000);
        return Ok(pagination.Result(list));
    }

    [HttpGet("manager")]
    public async Task<IActionResult> MyManager()
    {
        var (id, name) = await _svc.ResolveManagerAsync(MyUserId);
        return Ok(new { userId = id, name });
    }

    // ================= پیوند کاربر ورود به پرونده =================

    public class LinkInput { public int EmployeeId { get; set; } public int UserId { get; set; } }

    [HttpPost("user-links")]
    public async Task<IActionResult> LinkUser([FromBody] LinkInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            await _svc.LinkUserAsync(input.EmployeeId, input.UserId);
            return Ok(new { ok = true });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("user-links")]
    public async Task<IActionResult> UserLinks([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsHrAsync()) return Forbid();
        var allowedU = await AllowedUsersAsync();
        return Ok((await Paging.QueryAsync(_db.HrUserLinks.AsNoTracking()
            .Where(x => allowedU.Contains(x.UserId) && _db.FaHrPersonnel.Any(e => e.Id == x.EmployeeId))
            .OrderBy(x => x.Id), skip, take, 2000)).Result());
    }

    // ================= درخواست مرخصی/ماموریت از مسیر ماژول جدید =================

    public class LeaveInput
    {
        public string Type { get; set; } = "Daily";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public string? Destination { get; set; }
        public string? Reason { get; set; }
        public string? Category { get; set; }
        public string? MissionKind { get; set; }
    }

    [HttpPost("leaves")]
    public async Task<IActionResult> CreateLeave([FromBody] LeaveInput input)
    {
        if (input == null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        var r = await _svc.CreateLeaveAsync(MyUserId, MyName, input.Type, input.StartDate, input.EndDate,
            input.StartTime, input.EndTime, input.Destination, input.Reason, input.Category, input.MissionKind);
        if (!r.Ok) return BadRequest(new { message = r.Message });
        return Ok(new { id = r.Id, number = r.Number });
    }

    [HttpGet("leaves/mine")]
    public async Task<IActionResult> MyLeaves([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pg = await Paging.QueryAsync(_db.LeaveRequests.AsNoTracking()
            .Where(l => l.RequesterUserId == MyUserId).OrderByDescending(l => l.Id), skip, take, 200);
        var rows = pg.Rows;
        var ids = rows.Select(l => l.Id).ToList();
        var extras = ids.Count == 0 ? new Dictionary<int, HrLeaveExtra>()
            : await _db.HrLeaveExtras.AsNoTracking().Where(x => ids.Contains(x.LeaveRequestId))
                .ToDictionaryAsync(x => x.LeaveRequestId);
        return Ok(pg.Result(rows.Select(l => new
        {
            l.Id, l.Number, l.Type, l.StartDate, l.EndDate, l.Days, l.Hours,
            l.Destination, l.Reason, l.Status, l.ApprovedByName, l.ApprovedAt, l.CreatedAt,
            Category = extras.TryGetValue(l.Id, out var x) ? x.Category : "Annual",
            MissionKind = extras.TryGetValue(l.Id, out var x2) ? x2.MissionKind : null,
            AllowanceAmount = extras.TryGetValue(l.Id, out var x3) ? x3.AllowanceAmount : 0,
        })));
    }

    [HttpGet("leaves/pending")]
    public async Task<IActionResult> PendingLeaves([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsHrAsync()) return Forbid();
        await _svc.EnsureStepsForPendingAsync();
        var allowedU = await AllowedUsersAsync();
        var pg = await Paging.QueryAsync(_db.LeaveRequests.AsNoTracking()
            .Where(l => l.Status == "Pending" && allowedU.Contains(l.RequesterUserId)).OrderBy(l => l.CreatedAt), skip, take, 300);
        var rows = pg.Rows;
        var ids = rows.Select(l => l.Id).ToList();
        var extras = ids.Count == 0 ? new Dictionary<int, HrLeaveExtra>()
            : await _db.HrLeaveExtras.AsNoTracking().Where(x => ids.Contains(x.LeaveRequestId))
                .ToDictionaryAsync(x => x.LeaveRequestId);
        var steped = await _db.HrRequestSteps.AsNoTracking()
            .Where(x => x.RequestType == "Leave" && ids.Contains(x.RequestId) && x.Status == "Pending")
            .GroupBy(x => x.RequestId).ToDictionaryAsync(g => g.Key, g => g.Min(x => x.StepNo));
        return Ok(pg.Result(rows.Select(l => new
        {
            l.Id, l.Number, l.Type, l.RequesterName, l.StartDate, l.EndDate, l.Days, l.Hours,
            l.Destination, l.Reason, l.CreatedAt,
            Category = extras.TryGetValue(l.Id, out var x) ? x.Category : "Annual",
            MissionKind = extras.TryGetValue(l.Id, out var x2) ? x2.MissionKind : null,
            AllowanceAmount = extras.TryGetValue(l.Id, out var x3) ? x3.AllowanceAmount : 0,
            CurrentStep = steped.TryGetValue(l.Id, out var n) ? n : 0,
        })));
    }

    public class ExtraInput { public string? Category { get; set; } public string? MissionKind { get; set; } }

    [HttpPost("leaves/{id:int}/extra")]
    public async Task<IActionResult> SaveLeaveExtra(int id, [FromBody] ExtraInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        var req = await _db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (req == null) return NotFound();
        var ex = await _db.HrLeaveExtras.FirstOrDefaultAsync(x => x.LeaveRequestId == id);
        if (ex == null) { ex = new HrLeaveExtra { LeaveRequestId = id }; _db.HrLeaveExtras.Add(ex); }
        if (input?.Category is "Annual" or "Sick" or "Maternity" or "Unpaid") ex.Category = input.Category;
        if (req.Type is "Mission" or "HourlyMission")
        {
            ex.MissionKind = input?.MissionKind == "Outer" ? "Outer" : "Inner";
            var rules = await _svc.GetRulesAsync();
            var perDay = rules.TryGetValue(ex.MissionKind == "Outer" ? "mission.allowance.outer" : "mission.allowance.inner", out var av)
                && decimal.TryParse(av, out var ad) ? ad : 0;
            var mDays = req.Type == "HourlyMission" ? req.Hours / 8.0 : Math.Max(req.Days, 1);
            ex.AllowanceAmount = perDay * (decimal)Math.Max(mDays, 0);
        }
        else { ex.MissionKind = null; ex.AllowanceAmount = 0; }
        ex.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, allowanceAmount = ex.AllowanceAmount });
    }

    public class GpsInput { public DateTime Date { get; set; } public bool IsEnter { get; set; } public double Lat { get; set; } public double Lng { get; set; } }

    /// <summary>ثبت مختصات ورود/خروج موبایل در جدول مجزای ماژول</summary>
    [HttpPost("clock/gps")]
    public async Task<IActionResult> SaveGps([FromBody] GpsInput input)
    {
        if (input == null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        await _svc.SaveGpsAsync(MyUserId, input.Date == default ? DateTime.Today : input.Date, input.IsEnter, input.Lat, input.Lng);
        return Ok(new { ok = true });
    }

    // ================= فاز ۱ قانونی =================

    public class CarryoverInput { public int UserId { get; set; } public int Year { get; set; } }

    /// <summary>بستن سال مرخصی: انتقال مانده استحقاقی تا سقف ۹ روز (ماده ۶۶) به سال بعد</summary>
    [HttpPost("balances/carryover")]
    public async Task<IActionResult> Carryover([FromBody] CarryoverInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        if (!await _svc.Access.IsAllowedUserAsync(input.UserId)) return BadRequest(new { message = UserNotAllowed });
        var r = await _svc.CarryoverCloseAsync(input.UserId, input.Year);
        return Ok(new { ok = true, remaining = r.Remaining, carry = r.Carry, nextGrant = r.NextGrant });
    }

    [HttpGet("probations")]
    public async Task<IActionResult> Probations([FromQuery] int? employeeId, [FromQuery] int endingWithinDays = 0, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsHrAsync()) return Forbid();
        if (endingWithinDays > 0)
        {
            var pagination = new Paging.Request(skip, take);
            var soon = await _svc.ProbationsEndingSoonAsync(endingWithinDays, pagination);
            var empIds = soon.Select(x => x.EmployeeId).Distinct().ToList();
            var names = await _db.FaHrPersonnel.AsNoTracking().Where(e => empIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}".Trim());
            return Ok(pagination.Result(soon.Select(x => new { x.Id, x.EmployeeId, Name = names.TryGetValue(x.EmployeeId, out var n) ? n : "", x.StartDate, x.EndDate, x.Months, x.SkillLevel, x.Status }).ToList()));
        }
        var allowedEmp = await AllowedEmpAsync();
        var q = _db.HrProbations.AsNoTracking().Where(x => allowedEmp.Contains(x.EmployeeId));
        if (employeeId != null) q = q.Where(x => x.EmployeeId == employeeId);
        return Ok((await Paging.QueryAsync(q.OrderByDescending(x => x.Id), skip, take, 500)).Result());
    }

    public class ProbationInput { public int EmployeeId { get; set; } public DateTime StartDate { get; set; } public int Months { get; set; } = 3; public string SkillLevel { get; set; } = "Skilled"; }

    [HttpPost("probations")]
    public async Task<IActionResult> StartProbation([FromBody] ProbationInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try { return Ok(await _svc.StartProbationAsync(input.EmployeeId, input.StartDate == default ? DateTime.Today : input.StartDate, input.Months, input.SkillLevel)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class ProbationDecideInput { public bool Pass { get; set; } public string? Note { get; set; } }

    [HttpPost("probations/{id:int}/complete")]
    public async Task<IActionResult> CompleteProbation(int id, [FromBody] ProbationDecideInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try { return Ok(await _svc.CompleteProbationAsync(id, input?.Pass == true, input?.Note, MyName)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>اعتبارسنجی کد ملی با الگوریتم check-digit</summary>
    [HttpGet("validate-national/{code}")]
    public async Task<IActionResult> ValidateNational(string code)
    {
        if (!await IsHrAsync()) return Forbid();
        return Ok(new { code, valid = HrTimeService.IsValidIranianNationalCode(code) });
    }

    /// <summary>ثبت پرسنل با اعتبارسنجی کد ملی (بدون هیچ تغییری در سرویس کارگزینی)</summary>
    [HttpPost("employees")]
    public async Task<IActionResult> CreateEmployee([FromBody] HrEmployeeSaveDto dto)
    {
        if (!await IsHrAsync()) return Forbid();
        if (dto == null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        if (!HrTimeService.IsValidIranianNationalCode(dto.NationalCode))
            return BadRequest(new { message = "کد ملی معتبر نیست (رقم کنترل اشتباه است)." });
        if (dto.SystemUserId is not > 0)
            return BadRequest(new { message = "کاربر سیستمِ پرسنل را انتخاب کنید (فقط کاربرانِ فعالِ دارای دسترسی به این ماژول)." });
        try
        {
            return Ok(await _personnel.CreateAsync(new FaHrPersonnelSaveDto
            {
                Code = dto.Code, FirstName = dto.FirstName, LastName = dto.LastName, NationalCode = dto.NationalCode,
                BirthDate = dto.BirthDate, Gender = dto.Gender, MaritalStatus = dto.MaritalStatus,
                Mobile = dto.Mobile, Email = dto.Email, Address = dto.Address, HireDate = dto.HireDate,
                PostTitle = dto.PostTitle, ManagerId = dto.ManagerId, EmploymentType = dto.EmploymentType,
                Status = dto.Status, BaseSalary = dto.BaseSalary, IsActive = dto.IsActive,
                Sheba = dto.Sheba, BankName = dto.BankName, InsuranceNo = dto.InsuranceNo,
                UserId = dto.SystemUserId.Value,
            }));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("minwages")]
    public async Task<IActionResult> MinWages([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await IsHrAsync()) return Forbid();
        return Ok(await Paging.ResultAsync(_db.HrMinWages.AsNoTracking().OrderByDescending(x => x.Year), skip, take));
    }

    public class MinWageInput { public int Year { get; set; } public decimal MonthlyWage { get; set; } public DateTime? EffectiveDate { get; set; } public string? Note { get; set; } }

    [HttpPost("minwages")]
    public async Task<IActionResult> SaveMinWage([FromBody] MinWageInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try { return Ok(await _svc.SaveMinWageAsync(input.Year, input.MonthlyWage, input.EffectiveDate, input.Note)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("nursing")]
    public async Task<IActionResult> Nursing([FromQuery] int employeeId)
    {
        if (!await IsHrAsync()) return Forbid();
        if (!await _svc.Access.IsAllowedPersonnelAsync(employeeId)) return NotFound();
        var nb = await _svc.ActiveNursingAsync(employeeId);
        if (nb == null) return NotFound();
        return Ok(nb);
    }

    public class NursingInput { public int EmployeeId { get; set; } public DateTime ChildBirthDate { get; set; } }

    [HttpPost("nursing")]
    public async Task<IActionResult> SetNursing([FromBody] NursingInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try { return Ok(await _svc.SetNursingAsync(input.EmployeeId, input.ChildBirthDate)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>موظفی روزانه با لحاظ ارفاق شیردهی</summary>
    [HttpGet("obligation")]
    public async Task<IActionResult> Obligation([FromQuery] int? userId, [FromQuery] DateTime? date)
    {
        var uid = userId ?? MyUserId;
        if (uid != MyUserId && !await IsHrAsync()) return Forbid();
        if (!await UserOkAsync(uid)) return BadRequest(new { message = UserNotAllowed });
        var r = await _svc.DailyObligationAsync(uid, date ?? DateTime.Today);
        return Ok(new { scheduledMin = r.ScheduledMin, nursingMin = r.NursingMin, requiredMin = r.RequiredMin });
    }

    // ================= فاز ۲: کارکرد ماهانه، تأیید اضافه‌کار، بستن ماه =================

    [HttpGet("attendance")]
    public async Task<IActionResult> Attendance([FromQuery] int? userId, [FromQuery] int jy, [FromQuery] int jm)
    {
        var uid = userId ?? MyUserId;
        if (uid != MyUserId && !await IsHrAsync()) return Forbid();
        if (!await UserOkAsync(uid)) return BadRequest(new { message = UserNotAllowed });
        if (jy is < 1300 or > 1500 || jm is < 1 or > 12)
            return BadRequest(new { message = "سال/ماه نامعتبر است." });
        return Ok(await _svc.MonthAttendanceAsync(uid, jy, jm));
    }

    [HttpGet("ot-approvals")]
    public async Task<IActionResult> OtApprovals([FromQuery] int? userId, [FromQuery] int jy, [FromQuery] int jm, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var uid = userId ?? MyUserId;
        if (uid != MyUserId && !await IsHrAsync()) return Forbid();
        if (!await UserOkAsync(uid)) return BadRequest(new { message = UserNotAllowed });
        if (jy is < 1300 or > 1500 || jm is < 1 or > 12)
            return BadRequest(new { message = "سال/ماه نامعتبر است." });
        var pagination = new Paging.Request(skip, take);
        var rows = await _svc.OtApprovalsAsync(uid == MyUserId && !await IsHrAsync() ? MyUserId : uid, jy, jm, pagination);
        return Ok(pagination.Result(rows));
    }

    public class OtDecideInput
    {
        public int UserId { get; set; }
        public DateTime Date { get; set; }
        public bool Approve { get; set; } = true;
        public int N { get; set; }
        public int H { get; set; }
        public int Ni { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost("ot-approvals/decide")]
    public async Task<IActionResult> DecideOt([FromBody] OtDecideInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            if (!await _svc.Access.IsAllowedUserAsync(input.UserId)) return BadRequest(new { message = UserNotAllowed });
            var a = await _svc.DecideOtAsync(input.UserId, input.Date, input.Approve,
                input.N, input.H, input.Ni, MyUserId, MyName, input.Note);
            return Ok(a);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class OtDecideMonthInput
    {
        public int UserId { get; set; }
        public int Jy { get; set; }
        public int Jm { get; set; }
        public bool Approve { get; set; } = true;
    }

    [HttpPost("ot-approvals/decide-month")]
    public async Task<IActionResult> DecideOtMonth([FromBody] OtDecideMonthInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            if (!await _svc.Access.IsAllowedUserAsync(input.UserId)) return BadRequest(new { message = UserNotAllowed });
            var n = await _svc.DecideOtMonthAsync(input.UserId, input.Jy, input.Jm, input.Approve, MyUserId, MyName);
            return Ok(new { ok = true, count = n });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class AttendCloseInput
    {
        public int Jy { get; set; }
        public int Jm { get; set; }
        public bool Closed { get; set; } = true;
        public string? Note { get; set; }
    }

    [HttpPost("attend-close")]
    public async Task<IActionResult> AttendClose([FromBody] AttendCloseInput input)
    {
        if (!await IsHrAsync()) return Forbid();
        try
        {
            var c = await _svc.SetMonthClosedAsync(input.Jy, input.Jm, input.Closed, MyName, input.Note);
            return Ok(c);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("attend-close")]
    public async Task<IActionResult> AttendCloseStatus([FromQuery] int jy, [FromQuery] int jm)
    {
        if (!await IsHrAsync()) return Forbid();
        if (jy is < 1300 or > 1500 || jm is < 1 or > 12)
            return BadRequest(new { message = "سال/ماه نامعتبر است." });
        return Ok(new { jy, jm, closed = await _svc.IsMonthClosedAsync(jy, jm) });
    }
}
