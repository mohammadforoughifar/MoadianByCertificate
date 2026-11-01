using System.Globalization;
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
/// ماژول منابع انسانی: مرخصی روزانه/ساعتی + ماموریت — بلادرنگ با SignalR.
/// سنجش موجودی: هر نیرو ۲ روز در ماه شمسی مرخصی دارد (مجموعاً ۲۴ روز در سال).
/// مرخصی ساعتی با نرخ ۸ ساعت = ۱ روز محاسبه می‌شود. ماموریت از موجودی کسر نمی‌شود.
/// موجودی قابل انتقال بین ماه‌هاست (مانده تا پایان سال شمسی جمع می‌شود).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LeaveRequestsController : ControllerBase
{
    /// <summary>موجودی مرخصی هر نفر در ماه (روز)</summary>
    public const double MonthlyQuotaDays = 2.0;

    /// <summary>سقف سالانه = 24 روز</summary>
    public const double YearlyQuotaDays = 24.0;

    /// <summary>تعداد ساعت هر روز کاری (برای تبدیل ساعتی به روز)</summary>
    public const int HourlyWorkdayHours = 8;

    /// <summary>scopeی که در BroadcastChanged برای پیام‌های بلادرنگ این ماژول ارسال می‌شود</summary>
    private const string RtScope = "leaverequests";

    private readonly AppDbContext _db;
    private readonly INotifyService _notify;
    private readonly ForoughAryaHrUsers _fa;
    public LeaveRequestsController(AppDbContext db, INotifyService notify, AttendanceRecalcService recalc, ILogger<LeaveRequestsController> logger, ForoughAryaHrUsers fa)
    { _db = db; _notify = notify; _recalc = recalc; _logger = logger; _fa = fa; }

    /// <summary>«منابع انسانی فروغ آریا»: فقط کاربرانِ فعالِ دارای دسترسی به ماژول در فهرست‌ها/گزارش‌ها/کمبوها</summary>
    private async Task<List<int>> AllowedUsersAsync() => (await _fa.UserIdsAsync()).ToList();
    private const string UserNotAllowed = "کاربر فعال نیست یا به «منابع انسانی فروغ آریا» دسترسی ندارد.";

    private readonly AttendanceRecalcService _recalc;
    private readonly ILogger<LeaveRequestsController> _logger;

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyName => User.FindFirstValue(ClaimTypes.Name) ?? "";

    // کش دسترسی مدیر برای هر درخواست
    private bool? _canManage;
    private bool? _canReport;

    private async Task<bool> CanManageAsync()
    {
        if (_canManage.HasValue) return _canManage.Value;
        if (User.IsInRole("Admin")) { _canManage = true; return true; }
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (roles.Count == 0) { _canManage = false; return false; }
        _canManage = await _db.RolePermissions
            .AnyAsync(rp => roles.Contains(rp.Role.Name)
                            && rp.Permission.Module == "LeaveRequests"
                            && rp.Permission.Action == "Approve");
        return _canManage.Value;
    }

    private async Task<bool> CanReportAsync()
    {
        if (_canReport.HasValue) return _canReport.Value;
        if (User.IsInRole("Admin")) { _canReport = true; return true; }
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (roles.Count == 0) { _canReport = false; return false; }
        _canReport = await _db.RolePermissions
            .AnyAsync(rp => roles.Contains(rp.Role.Name)
                            && rp.Permission.Module == "LeaveRequests"
                            && (rp.Permission.Action == "Approve" || rp.Permission.Action == "Report"));
        return _canReport.Value;
    }

    // ================== مدل‌های API ==================

    public class LeaveRequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; } = "";
        public string Type { get; set; } = "";
        public string TypeFa => Type switch
        {
            "Hourly" => "ساعتی",
            "HourlyMission" => "ماموریت ساعتی",
            "Mission" => "ماموریت",
            _ => "روزانه"
        };
        public int RequesterUserId { get; set; }
        public string RequesterName { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public double Days { get; set; }
        public double Hours { get; set; }
        public string? Destination { get; set; }
        public string? Reason { get; set; }
        public string Status { get; set; } = "";
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? ApproveNote { get; set; }
        public bool AdminCreated { get; set; }
        public DateTime CreatedAt { get; set; }
        public double QuotaDays => Type == "Hourly" ? Hours / (double)HourlyWorkdayHours : (Type == "Daily" ? Days : 0);
    }

    public class LeaveRequestInput
    {
        public string Type { get; set; } = "Daily";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        /// <summary>ساعت شروع به صورت رشته (مثلاً "08:00" یا "08:00:00")</summary>
        public string? StartTime { get; set; }
        /// <summary>ساعت پایان به صورت رشته (مثلاً "10:30")</summary>
        public string? EndTime { get; set; }
        public double Days { get; set; }
        public double Hours { get; set; }
        public string? Destination { get; set; }
        public string? Reason { get; set; }
    }

    public class BalanceInfo
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
        public double Quota { get; set; } = MonthlyQuotaDays;        // سقف این ماه (2 روز)
        public double Used { get; set; }                            // مصرف تاییدشده این ماه
        public double Pending { get; set; }                         // در انتظار این ماه
        public double Remaining => Math.Round(Quota - Used, 2);
        public double RemainingWithPending => Math.Round(Quota - Used - Pending, 2);
        public double MissionDays { get; set; }
        public int RequestCount { get; set; }

        // ------- اطلاعات سالانه -------
        public int YearlyQuota { get; set; } = (int)YearlyQuotaDays;
        public double YearlyUsed { get; set; }                      // مجموع مصرف تاییدشده سال جاری
        public double YearlyPending { get; set; }                   // در انتظار سال جاری
        public double YearlyRemaining => Math.Round(YearlyQuota - YearlyUsed, 2);
        public double YearlyRemainingWithPending => Math.Round(YearlyQuota - YearlyUsed - YearlyPending, 2);
        public double YearlyUsedByMonth { get; set; }               // مصرف تا پایان این ماه (ماه‌های کامل‌شده)
        public double YearlyEarnedToMonth { get; set; }             // موجودی استحقاقی از ابتدای سال تا پایان این ماه
        public List<MonthUsage> Months { get; set; } = new();       // خلاصه ۱۲ ماه
    }

    public class MonthUsage
    {
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
        public double Quota { get; set; }
        public double Used { get; set; }
        public double Pending { get; set; }
    }

    public class ReportRow
    {
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public string? Role { get; set; }
        public double DailyDays { get; set; }
        public double HourlyHours { get; set; }
        public double TotalQuotaDays { get; set; }
        public double MissionDays { get; set; }
        public int MissionCount { get; set; }
        public double Quota { get; set; } = MonthlyQuotaDays;
        public double Remaining => Math.Round(Quota - TotalQuotaDays, 2);
        public int PendingCount { get; set; }
        // سالانه
        public double YearlyQuota { get; set; } = YearlyQuotaDays;
        public double YearlyUsed { get; set; }
        public double YearlyRemaining => Math.Round(YearlyQuota - YearlyUsed, 2);
        public string StatusFa =>
            TotalQuotaDays > Quota ? "بیش از موجودی ماه" :
            YearlyUsed > YearlyQuota ? "بیش از موجودی سال" :
            PendingCount > 0 ? "در انتظار دارد" :
            TotalQuotaDays > 0 ? "مصرف کرده" : "—";
    }

    public class ReportResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
        public double Quota { get; set; } = MonthlyQuotaDays;
        public List<ReportRow> Rows { get; set; } = new();
        public double TotalUsed { get; set; }
        public int TotalMissions { get; set; }
    }

    // ================== کمک‌های اعلان بلادرنگ ==================

    private async Task<List<int>> HrManagerUserIdsAsync()
    {
        var rbac = await _db.UserRoles
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(_db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == "LeaveRequests" && x.Action == "Approve")
            .Select(x => x.UserId).Distinct().ToListAsync();
        var legacyAdmins = await _db.Users
            .Where(u => u.Role == "Admin" && u.IsActive && !_db.UserRoles.Any(ur => ur.UserId == u.Id))
            .Select(u => u.Id).ToListAsync();
        var allowed = await _fa.UserIdsAsync();
        return rbac.Concat(legacyAdmins).Distinct().Where(allowed.Contains).ToList();
    }

    // ================== کمک‌های تاریخ شمسی ==================

    private static (int y, int m) PersianYM(DateTime dt)
    {
        var (y, m, _) = PersianDate.FromGregorian(dt);
        return (y, m);
    }

    /// <summary>بازه میلادی برای یک ماه شمسی.</summary>
    private static (DateTime start, DateTime endExclusive) MonthRange(int jy, int jm)
    {
        var start = PersianDate.ToGregorian(jy, jm, 1);
        var end = jm == 12 ? PersianDate.ToGregorian(jy + 1, 1, 1) : PersianDate.ToGregorian(jy, jm + 1, 1);
        return (start, end);
    }

    /// <summary>بازه میلادی برای یک سال شمسی (از اول فروردین تا اول فروردین بعد).</summary>
    private static (DateTime start, DateTime endExclusive) YearRange(int jy)
    {
        var start = PersianDate.ToGregorian(jy, 1, 1);
        var end = PersianDate.ToGregorian(jy + 1, 1, 1);
        return (start, end);
    }

    /// <summary>مصرف موجودی در یک بازه میلادی برای کاربر معین (فقط مرخصی‌ها — ماموریت‌ها از موجودی کسر نمی‌شوند).</summary>
    private async Task<(double used, double pending)> UsageInRange(int userId, DateTime start, DateTime endExclusive, bool includePending)
    {
        var q = _db.LeaveRequests
            .Where(l => l.RequesterUserId == userId
                        && l.StartDate >= start && l.StartDate < endExclusive
                        && (l.Type == "Daily" || l.Type == "Hourly"));
        var approved = await q.Where(l => l.Status == "Approved")
            .Select(l => new { l.Days, l.Hours, l.Type }).ToListAsync();
        double used = approved.Sum(l => l.Type == "Hourly" ? l.Hours / (double)HourlyWorkdayHours : l.Days);

        double pending = 0;
        if (includePending)
        {
            var pend = await q.Where(l => l.Status == "Pending")
                .Select(l => new { l.Days, l.Hours, l.Type }).ToListAsync();
            pending = pend.Sum(l => l.Type == "Hourly" ? l.Hours / (double)HourlyWorkdayHours : l.Days);
        }
        return (Math.Round(used, 2), Math.Round(pending, 2));
    }

    private async Task<(double used, double pending)> UsageOfMonth(int userId, int jy, int jm, bool includePending)
    {
        var (s, e) = MonthRange(jy, jm);
        if (s == default) return (0, 0);
        return await UsageInRange(userId, s, e, includePending);
    }

    private async Task<(double used, double pending)> UsageOfYear(int userId, int jy, bool includePending)
    {
        var (s, e) = YearRange(jy);
        if (s == default) return (0, 0);
        return await UsageInRange(userId, s, e, includePending);
    }

    // ================== موجودی (بالای صفحه) ==================

    [HttpGet("balance/{jy:int}/{jm:int}")]
    public async Task<IActionResult> Balance(int jy, int jm)
    {
        var (s, e) = MonthRange(jy, jm);
        var (used, pending) = await UsageOfMonth(MyUserId, jy, jm, true);

        var missions = await _db.LeaveRequests
            .Where(l => l.RequesterUserId == MyUserId && l.Status == "Approved"
                        && (l.Type == "Mission" || l.Type == "HourlyMission")
                        && l.StartDate >= s && l.StartDate < e)
            .Select(l => l.Type == "Mission" ? l.Days : l.Hours / (double)HourlyWorkdayHours).ToListAsync();

        var count = await _db.LeaveRequests
            .CountAsync(l => l.RequesterUserId == MyUserId && l.StartDate >= s && l.StartDate < e);

        // مصرف سالانه
        var (yUsed, yPending) = await UsageOfYear(MyUserId, jy, true);

        // خلاصه ۱۲ ماه سال
        var months = new List<MonthUsage>();
        double usedByMonth = 0;
        for (int mi = 1; mi <= 12; mi++)
        {
            var (ms, me) = MonthRange(jy, mi);
            var (mu, mp) = await UsageInRange(MyUserId, ms, me, false);
            months.Add(new MonthUsage
            {
                Month = mi,
                MonthName = PersianDate.MonthName(mi),
                Quota = MonthlyQuotaDays,
                Used = mu,
                Pending = mp
            });
            if (mi < jm) usedByMonth += mu; // فقط ماه‌های کامل‌شده
        }

        return Ok(new BalanceInfo
        {
            Year = jy,
            Month = jm,
            MonthName = PersianDate.MonthName(jm),
            Used = used,
            Pending = pending,
            MissionDays = missions.Sum(),
            RequestCount = count,
            YearlyUsed = yUsed,
            YearlyPending = yPending,
            YearlyUsedByMonth = usedByMonth,
            YearlyEarnedToMonth = jm * MonthlyQuotaDays,
            Months = months
        });
    }

    // ================== درخواست‌های من ==================

    [HttpGet("mine")]
    public async Task<IActionResult> Mine([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var list = await _db.LeaveRequests
            .Where(l => l.RequesterUserId == MyUserId)
            .OrderByDescending(l => l.CreatedAt).ThenBy(x => x.Id)
            .ToPageListAsync(pagination, defaultCap: 200);
        return Ok(pagination.Result(await PopulateUserNamesAndToDtoAsync(list)));
    }

    // ================== همه‌ی درخواست‌ها (مدیر) ==================

    [HttpGet]
    public async Task<IActionResult> All([FromQuery] string? status, [FromQuery] int? jy, [FromQuery] int? jm, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!await CanManageAsync()) return Forbid();
        var allowedU = await AllowedUsersAsync();
        var q = _db.LeaveRequests.Where(l => allowedU.Contains(l.RequesterUserId));
        if (!string.IsNullOrWhiteSpace(status) && status != "All")
            q = q.Where(l => l.Status == status);
        if (jy is > 0 && jm is > 0)
        {
            var (s, e) = MonthRange(jy.Value, jm.Value);
            q = q.Where(l => l.StartDate >= s && l.StartDate < e);
        }
        var list = await q.OrderByDescending(l => l.CreatedAt).ThenBy(x => x.Id).ToPageListAsync(pagination, defaultCap: 300);
        return Ok(pagination.Result(await PopulateUserNamesAndToDtoAsync(list)));
    }

    // ================== ثبت درخواست ==================

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LeaveRequestInput input)
    {
        if (input == null) return BadRequest(new { message = "داده‌ای ارسال نشده است." });
        if (input.Type is not ("Daily" or "Hourly" or "HourlyMission" or "Mission"))
            return BadRequest(new { message = "نوع درخواست نامعتبر است." });

        // برای نوع‌های ساعتی، تاریخ پایان همان تاریخ شروع است (اگر ارسال نشده، تنظیمش می‌کنیم)
        if (input.Type is "Hourly" or "HourlyMission")
            input.EndDate = input.StartDate;

        if (input.StartDate.Date > input.EndDate.Date)
            return BadRequest(new { message = "تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد." });

        if (input.Type == "Daily")
        {
            input.Days = (input.EndDate.Date - input.StartDate.Date).TotalDays + 1;
            if (input.Days > 366) return BadRequest(new { message = "بازه‌ی درخواست معتبر نیست." });
            input.Hours = 0;
            input.StartTime = null;
            input.EndTime = null;
        }
        else if (input.Type is "Hourly" or "HourlyMission")
        {
            if (string.IsNullOrWhiteSpace(input.StartTime) || !TimeSpan.TryParse(input.StartTime, out var st))
                return BadRequest(new { message = "ساعت شروع را درست وارد کنید (مثلاً 08:00)." });
            if (string.IsNullOrWhiteSpace(input.EndTime) || !TimeSpan.TryParse(input.EndTime, out var en))
                return BadRequest(new { message = "ساعت پایان را درست وارد کنید (مثلاً 10:30)." });
            if (en <= st)
                return BadRequest(new { message = "ساعت پایان باید بعد از ساعت شروع باشد." });
            var hours = (en - st).TotalHours;
            if (hours > HourlyWorkdayHours)
                return BadRequest(new { message = $"بازه‌ی ساعتی نمی‌تواند بیش از {Fa.Digits(HourlyWorkdayHours)} ساعت باشد." });
            input.Hours = Math.Round(hours, 2);
            input.Days = 0;
            input.EndDate = input.StartDate;
        }
        else
        {
            input.Days = (input.EndDate.Date - input.StartDate.Date).TotalDays + 1;
            input.Hours = 0;
            input.StartTime = null;
            input.EndTime = null;
            if (string.IsNullOrWhiteSpace(input.Destination))
                return BadRequest(new { message = "مقصد ماموریت را وارد کنید." });
        }

        if (input.Type == "HourlyMission" && string.IsNullOrWhiteSpace(input.Destination))
            return BadRequest(new { message = "مقصد ماموریت را وارد کنید." });

        double consume = input.Type == "Hourly" ? input.Hours / (double)HourlyWorkdayHours : input.Days;
        if (input.Type is "Daily" or "Hourly" && consume > 0)
        {
            // بررسی موجودی سالانه
            var (jy, _) = PersianYM(input.StartDate);
            var (yUsed, yPending) = await UsageOfYear(MyUserId, jy, true);
            var yRemaining = YearlyQuotaDays - yUsed - yPending;
            if (consume > yRemaining + 0.0001)
                return BadRequest(new
                {
                    message = $"موجودی مرخصی سالانه شما (سال {Fa.Digits(jy)}) کافی نیست. " +
                              $"(مانده: {FormatDays(yRemaining)} از {FormatDays(YearlyQuotaDays)} روز سالانه)"
                });
        }

        var (reqJy, reqJm) = PersianYM(input.StartDate);
        var serial = await _db.LeaveRequests.CountAsync(l => l.Number.StartsWith($"LR/{reqJy}/")) + 1;
        var reqName = await GetUserFullNameAsync(MyUserId);

        var req = new LeaveRequest
        {
            Number = $"LR/{reqJy}/{serial}",
            Type = input.Type,
            RequesterUserId = MyUserId,
            RequesterName = reqName,
            StartDate = input.StartDate.Date,
            EndDate = input.EndDate.Date,
            StartTime = TimeSpan.TryParse(input.StartTime, out var rst) ? rst : null,
            EndTime = TimeSpan.TryParse(input.EndTime, out var ren) ? ren : null,
            Days = input.Days,
            Hours = input.Hours,
            Destination = input.Destination,
            Reason = input.Reason,
            Status = "Pending",
            CreatedAt = DateTime.Now
        };
        _db.LeaveRequests.Add(req);
        await _db.SaveChangesAsync();

        var typeFa = req.Type switch { "Hourly" => "مرخصی ساعتی", "HourlyMission" => "ماموریت ساعتی", "Mission" => "ماموریت", _ => "مرخصی روزانه" };
        await _notify.SendManyAsync(await HrManagerUserIdsAsync(),
            "درخواست منابع انسانی جدید",
            $"{req.Number} — {typeFa} توسط {req.RequesterName}",
            req.RequesterName, "مرخصی و ماموریت", "/hr-admin");
        await _notify.BroadcastChangedAsync(RtScope);

        return Ok(new { id = req.Id, number = req.Number });
    }

    // ================== تایید / رد ==================

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        if (!await CanManageAsync()) return Forbid();
        var req = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (req.Status != "Pending") return BadRequest(new { message = "این درخواست قبلاً بررسی شده است." });
        if (!await _fa.IsAllowedUserAsync(req.RequesterUserId)) return BadRequest(new { message = UserNotAllowed });

        if (req.Type is "Daily" or "Hourly")
        {
            var (jy, _) = PersianYM(req.StartDate);
            var (yUsed, _) = await UsageOfYear(req.RequesterUserId, jy, false);
            var consume = req.Type == "Hourly" ? req.Hours / (double)HourlyWorkdayHours : req.Days;
            if (consume > YearlyQuotaDays - yUsed + 0.0001)
                return BadRequest(new { message = $"موجودی مرخصی سالانه «{req.RequesterName}» کافی نیست — لطفاً رد کنید." });
        }

        var myFullName = await GetUserFullNameAsync(MyUserId);
        req.Status = "Approved";
        req.ApprovedByUserId = MyUserId;
        req.ApprovedByName = myFullName;
        req.ApprovedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        // بازحساب کسریِ روزهای تحت تأثیر — با این کار درخواست‌های «عقب‌النصر» (روزهای گذشته)
        // بلافاصله روی کسری اعمال می‌شوند
        if (req.Type is "Hourly" or "HourlyMission" or "Daily")
        {
            try
            {
                for (var d = req.StartDate.Date; d <= req.EndDate.Date; d = d.AddDays(1))
                    await _recalc.RecalcDayAsync(req.RequesterUserId, d, DateTime.Now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "بازحساب کسری بعد از تایید درخواست {Number} ناموفق بود", req.Number);
            }
        }

        var typeFa = req.Type switch { "Hourly" => "مرخصی ساعتی", "HourlyMission" => "ماموریت ساعتی", "Mission" => "ماموریت", _ => "مرخصی روزانه" };
        await _notify.SendAsync(req.RequesterUserId,
            $"{typeFa} شما تایید شد",
            $"{req.Number} توسط {myFullName} تایید شد.",
            myFullName, "مرخصی و ماموریت", "/leave");
        await _notify.BroadcastChangedAsync(RtScope);

        return Ok(new { ok = true });
    }

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectInput input)
    {
        if (!await CanManageAsync()) return Forbid();
        var req = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        if (req.Status != "Pending") return BadRequest(new { message = "این درخواست قبلاً بررسی شده است." });

        var myFullName = await GetUserFullNameAsync(MyUserId);
        req.Status = "Rejected";
        req.ApprovedByUserId = MyUserId;
        req.ApprovedByName = myFullName;
        req.ApprovedAt = DateTime.Now;
        req.ApproveNote = input?.Note;
        await _db.SaveChangesAsync();

        var typeFa = req.Type switch { "Hourly" => "مرخصی ساعتی", "HourlyMission" => "ماموریت ساعتی", "Mission" => "ماموریت", _ => "مرخصی روزانه" };
        var body = $"{req.Number} توسط {myFullName} رد شد.";
        if (!string.IsNullOrWhiteSpace(input?.Note)) body += $" «{input.Note}»";
        await _notify.SendAsync(req.RequesterUserId,
            $"{typeFa} شما رد شد", body, MyName, "مرخصی و ماموریت", "/leave");
        await _notify.BroadcastChangedAsync(RtScope);

        return Ok(new { ok = true });
    }

    public class RejectInput { public string? Note { get; set; } }

    // ================== انصراف درخواست ==================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Cancel(int id)
    {
        var req = await _db.LeaveRequests.FirstOrDefaultAsync(l => l.Id == id);
        if (req == null) return NotFound(new { message = "درخواست پیدا نشد." });
        bool isManager = await CanManageAsync();
        if (req.RequesterUserId != MyUserId && !isManager) return Forbid();
        if (req.Status != "Pending") return BadRequest(new { message = "فقط درخواست‌های در انتظار قابل انصراف‌اند." });

        _db.LeaveRequests.Remove(req);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync(RtScope);
        return Ok(new { ok = true });
    }

    // ================== آمار ==================

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        if (!await CanManageAsync()) return Forbid();
        var allowedU = await AllowedUsersAsync();
        var lq = _db.LeaveRequests.Where(l => allowedU.Contains(l.RequesterUserId));
        var pending = await lq.CountAsync(l => l.Status == "Pending");
        var approved = await lq.CountAsync(l => l.Status == "Approved");
        var rejected = await lq.CountAsync(l => l.Status == "Rejected");
        var total = pending + approved + rejected;
        return Ok(new { total, pending, approved, rejected });
    }

    // ================== گزارش ماهانه ==================

    [HttpGet("report/{jy:int}/{jm:int}")]
    public async Task<IActionResult> Report(int jy, int jm, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReportAsync()) return Forbid();
        var (s, e) = MonthRange(jy, jm);
        if (s == default) return BadRequest(new { message = "ماه نامعتبر است." });
        var (ys, ye) = YearRange(jy);

        // ترتیب الزامی: Filter → Aggregate → Order → Count → Page؛ Aggregate هر کاربر با
        // زیرمجموعهٔ همبسته در SQL محاسبه و فقط ردیف‌های صفحه materialize می‌شوند.
        var monthQ = _db.LeaveRequests.AsNoTracking().Where(l => l.StartDate >= s && l.StartDate < e);
        var yearQ = _db.LeaveRequests.AsNoTracking()
            .Where(l => l.StartDate >= ys && l.StartDate < ye && l.Status == "Approved"
                        && (l.Type == "Daily" || l.Type == "Hourly"));

        var allowedU = await AllowedUsersAsync();
        var rowQuery =
            from u in _db.Users.AsNoTracking()
            where u.IsActive && allowedU.Contains(u.Id)
            let daily = monthQ.Where(l => l.RequesterUserId == u.Id && l.Type == "Daily" && l.Status == "Approved")
                .Sum(l => (double?)l.Days) ?? 0
            let hourly = monthQ.Where(l => l.RequesterUserId == u.Id && l.Type == "Hourly" && l.Status == "Approved")
                .Sum(l => (double?)l.Hours) ?? 0
            let missionDays = monthQ.Where(l => l.RequesterUserId == u.Id
                    && (l.Type == "Mission" || l.Type == "HourlyMission") && l.Status == "Approved")
                .Sum(l => (double?)(l.Type == "Mission" ? l.Days : l.Hours / HourlyWorkdayHours)) ?? 0
            let missionCount = monthQ.Count(l => l.RequesterUserId == u.Id
                && (l.Type == "Mission" || l.Type == "HourlyMission") && l.Status == "Approved")
            let pendingCount = monthQ.Count(l => l.RequesterUserId == u.Id && l.Status == "Pending")
            let yearlyUsed = yearQ.Where(l => l.RequesterUserId == u.Id)
                .Sum(l => (double?)(l.Type == "Hourly" ? l.Hours / HourlyWorkdayHours : l.Days)) ?? 0
            orderby (daily + hourly / HourlyWorkdayHours) descending, missionDays ascending, u.Id
            select new { u, daily, hourly, missionDays, missionCount, pendingCount, yearlyUsed };

        var pagination = new Paging.Request(skip, take);
        var page = await rowQuery.ToPageListAsync(pagination);

        // آگریگیت‌های کل گزارش مستقل از صفحه (جمعِ مقادیر گردشدهٔ سطرها — مانند قبل).
        var light = await rowQuery.Select(x => new { x.daily, x.hourly, x.missionCount }).ToListAsync();

        var rows = page.Select(x => new ReportRow
        {
            UserId = x.u.Id,
            Name = string.IsNullOrWhiteSpace(x.u.FirstName) ? x.u.Username : $"{x.u.FirstName} {x.u.LastName}",
            Role = x.u.Role,
            DailyDays = x.daily,
            HourlyHours = x.hourly,
            TotalQuotaDays = Math.Round(x.daily + x.hourly / (double)HourlyWorkdayHours, 2),
            MissionDays = Math.Round(x.missionDays, 2),
            MissionCount = x.missionCount,
            PendingCount = x.pendingCount,
            YearlyUsed = Math.Round(x.yearlyUsed, 2)
        }).ToList();

        var res = new ReportResult
        {
            Year = jy,
            Month = jm,
            MonthName = PersianDate.MonthName(jm),
            Rows = rows,
            TotalUsed = Math.Round(light.Sum(x => Math.Round(x.daily + x.hourly / (double)HourlyWorkdayHours, 2)), 2),
            TotalMissions = light.Sum(x => x.missionCount)
        };
        if (pagination.IsPaged)
            return Ok(new { res.Year, res.Month, res.MonthName, res.Quota, res.TotalUsed, res.TotalMissions, total = pagination.Total ?? res.Rows.Count, rows = res.Rows });
        return Ok(res);
    }

    // ================== گزارش اکسل/CSV ==================
    [HttpGet("report/{jy:int}/{jm:int}/csv")]
    public async Task<IActionResult> ReportCsv(int jy, int jm)
    {
        if (!await CanReportAsync()) return Forbid();
        var res = await Report(jy, jm) as OkObjectResult;
        if (res?.Value is not ReportResult data) return BadRequest();
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("نام,نقش,مرخصی روزانه(روز),مرخصی ساعتی(ساعت),مجموع(روز),مانده ماه,ماموریت(روز),تعداد ماموریت,مصرف سالانه(روز),مانده سالانه,وضعیت");
        foreach (var r in data.Rows)
        {
            sb.AppendJoin(',',
                r.Name, r.Role ?? "", r.DailyDays, r.HourlyHours, r.TotalQuotaDays,
                r.Remaining, r.MissionDays, r.MissionCount, r.YearlyUsed, r.YearlyRemaining, r.StatusFa);
            sb.AppendLine();
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"leave-report-{jy}-{jm}.csv");
    }

    // ================== کمک‌ها ==================

    private async Task<Dictionary<int, string>> GetFullNamesAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();

        var users = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username })
            .ToListAsync();

        return users.ToDictionary(
            u => u.Id,
            u => {
                var fn = $"{u.FirstName} {u.LastName}".Trim();
                return string.IsNullOrWhiteSpace(fn) ? u.Username : fn;
            });
    }

    private async Task<string> GetUserFullNameAsync(int userId)
    {
        var dict = await GetFullNamesAsync(new[] { userId });
        return dict.TryGetValue(userId, out var name) ? name : "کاربر نامشخص";
    }

    private async Task<List<LeaveRequestDto>> PopulateUserNamesAndToDtoAsync(List<LeaveRequest> list)
    {
        var userIds = list.Select(x => x.RequesterUserId)
            .Union(list.Where(x => x.ApprovedByUserId.HasValue).Select(x => x.ApprovedByUserId!.Value));

        var names = await GetFullNamesAsync(userIds);

        return list.Select(l => {
            var dto = ToDto(l);
            if (names.TryGetValue(l.RequesterUserId, out var rName) && !string.IsNullOrWhiteSpace(rName))
                dto.RequesterName = rName;
            if (l.ApprovedByUserId.HasValue && names.TryGetValue(l.ApprovedByUserId.Value, out var aName) && !string.IsNullOrWhiteSpace(aName))
                dto.ApprovedByName = aName;
            return dto;
        }).ToList();
    }

    private static string FormatDays(double d) =>
        d == (long)d ? Fa.Digits((long)d) : Fa.Digits(Math.Round(d, 1).ToString("0.0", CultureInfo.InvariantCulture));

    private static LeaveRequestDto ToDto(LeaveRequest l) => new()
    {
        Id = l.Id,
        Number = l.Number,
        Type = l.Type,
        RequesterUserId = l.RequesterUserId,
        RequesterName = l.RequesterName,
        StartDate = l.StartDate,
        EndDate = l.EndDate,
        StartTime = l.StartTime,
        EndTime = l.EndTime,
        Days = l.Days,
        Hours = l.Hours,
        Destination = l.Destination,
        Reason = l.Reason,
        Status = l.Status,
        ApprovedByName = l.ApprovedByName,
        ApprovedAt = l.ApprovedAt,
        ApproveNote = l.ApproveNote,
        AdminCreated = l.AdminCreated,
        CreatedAt = l.CreatedAt
    };

    // ================== تعطیلات شرکتی (اعمال گروهی برای کل پرسنل) ==================

    public class HolidayInput { public DateTime HolidayDate { get; set; } public string? Name { get; set; } }

    [HttpGet("holidays")]
    public async Task<IActionResult> GetHolidays([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var list = await _db.CompanyHolidays.AsNoTracking().OrderByDescending(h => h.HolidayDate).ThenBy(x => x.Id).ToPageListAsync(pagination);
        return Ok(pagination.Result(list.Select(h => new { h.Id, h.HolidayDate, h.Name, h.CreatedByName, h.CreatedAt, h.IsOfficial }).ToList()));
    }

    [HttpPost("holidays")]
    public async Task<IActionResult> AddHoliday([FromBody] HolidayInput input)
    {
        if (!await CanManageAsync()) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Name)) input.Name = "تعطیلی مجموعه";
        var holiday = new CompanyHoliday
        {
            HolidayDate = input.HolidayDate.Date,
            Name = input.Name.Trim(),
            CreatedByName = MyName,
            CreatedAt = DateTime.Now,
        };
        _db.CompanyHolidays.Add(holiday);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync(RtScope);
        return Ok(new { id = holiday.Id });
    }

    [HttpDelete("holidays/{id:int}")]
    public async Task<IActionResult> DeleteHoliday(int id)
    {
        if (!await CanManageAsync()) return Forbid();
        var holiday = await _db.CompanyHolidays.FirstOrDefaultAsync(h => h.Id == id);
        if (holiday == null) return NotFound(new { message = "تعطیلی پیدا نشد." });
        _db.CompanyHolidays.Remove(holiday);
        await _db.SaveChangesAsync();
        await _notify.BroadcastChangedAsync(RtScope);
        return Ok(new { ok = true });
    }

    // ================== ثبت مرخصی به‌جای کاربر توسط مدیر (تک‌نفر) ==================

    public class OnBehalfInput
    {
        public int UserId { get; set; }
        public string Type { get; set; } = "Hourly";           // Hourly | HourlyMission | Daily
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? StartTime { get; set; }                  // "HH:mm" برای نوع‌های ساعتی
        public string? EndTime { get; set; }
        public string? Destination { get; set; }
        public string? Reason { get; set; }
    }

    [HttpPost("on-behalf")]
    public async Task<IActionResult> CreateOnBehalf([FromBody] OnBehalfInput input)
    {
        if (!await CanManageAsync()) return Forbid();
        if (input.Type is not ("Hourly" or "HourlyMission" or "Daily"))
            return BadRequest(new { message = "نوع درخواست نامعتبر است." });

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == input.UserId);
        if (user == null) return BadRequest(new { message = "کاربر پیدا نشد." });
        if (!await _fa.IsAllowedUserAsync(input.UserId)) return BadRequest(new { message = UserNotAllowed });

        var userName = string.IsNullOrWhiteSpace(user.FirstName) ? user.Username : $"{user.FirstName} {user.LastName}".Trim();

        if (input.Type is "Hourly" or "HourlyMission")
        {
            if (string.IsNullOrWhiteSpace(input.StartTime) || !TimeSpan.TryParse(input.StartTime, out var st))
                return BadRequest(new { message = "ساعت شروع را درست وارد کنید (مثلاً 08:00)." });
            if (string.IsNullOrWhiteSpace(input.EndTime) || !TimeSpan.TryParse(input.EndTime, out var en))
                return BadRequest(new { message = "ساعت پایان را درست وارد کنید (مثلاً 10:30)." });
            if (en <= st) return BadRequest(new { message = "ساعت پایان باید بعد از ساعت شروع باشد." });
            if ((en - st).TotalHours > HourlyWorkdayHours)
                return BadRequest(new { message = $"بازه‌ی ساعتی نمی‌تواند بیش از {Fa.Digits(HourlyWorkdayHours)} ساعت باشد." });
            if (input.Type == "HourlyMission" && string.IsNullOrWhiteSpace(input.Destination))
                return BadRequest(new { message = "مقصد ماموریت را وارد کنید." });
        }
        else
        {
            if (input.EndDate.Date < input.StartDate.Date)
                return BadRequest(new { message = "تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد." });
        }

        var (reqJy, _) = PersianYM(input.StartDate);
        var serial = await _db.LeaveRequests.CountAsync(l => l.Number.StartsWith($"LR/{reqJy}/")) + 1;

        var hours = 0.0;
        if (input.Type is "Hourly" or "HourlyMission")
            hours = Math.Round((TimeSpan.Parse(input.EndTime!) - TimeSpan.Parse(input.StartTime!)).TotalHours, 2);

        var req = new LeaveRequest
        {
            Number = $"LR/{reqJy}/{serial}",
            Type = input.Type,
            RequesterUserId = input.UserId,
            RequesterName = userName,
            StartDate = input.StartDate.Date,
            EndDate = input.Type == "Daily" ? input.EndDate.Date : input.StartDate.Date,
            StartTime = TimeSpan.TryParse(input.StartTime, out var pst) ? pst : null,
            EndTime = TimeSpan.TryParse(input.EndTime, out var pen) ? pen : null,
            Days = input.Type == "Daily" ? (input.EndDate.Date - input.StartDate.Date).TotalDays + 1 : 0,
            Hours = hours,
            Destination = input.Destination,
            Reason = string.IsNullOrWhiteSpace(input.Reason) ? "ثبت شده توسط مدیر" : input.Reason,
            Status = "Approved",
            AdminCreated = true,
            ApprovedByUserId = MyUserId,
            ApprovedByName = MyName,
            ApprovedAt = DateTime.Now,
            ApproveNote = "توسط مدیر به‌جای کاربر ثبت و تایید شد",
            CreatedAt = DateTime.Now,
        };
        _db.LeaveRequests.Add(req);
        await _db.SaveChangesAsync();

        // بازحساب کسری روزهای تحت تأثیر
        try
        {
            for (var d = req.StartDate.Date; d <= req.EndDate.Date; d = d.AddDays(1))
                await _recalc.RecalcDayAsync(req.RequesterUserId, d, DateTime.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "بازحساب کسری بعد از ثبت به‌جای کاربر ناموفق بود");
        }

        var typeFaOnBehalf = req.Type switch { "Hourly" => "مرخصی ساعتی", "HourlyMission" => "ماموریت ساعتی", _ => "مرخصی روزانه" };
        await _notify.SendAsync(req.RequesterUserId,
            "مرخصی/ماموریت توسط مدیر ثبت شد",
            $"یک درخواست {typeFaOnBehalf} به شما ثبت و تایید شد (شماره {req.Number}).",
            MyName, "مرخصی و ماموریت", "/leave");
        await _notify.BroadcastChangedAsync(RtScope);

        return Ok(new { id = req.Id, number = req.Number });
    }
}
