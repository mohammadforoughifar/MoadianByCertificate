using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// بازحساب کسری یک روز برای یک کاربر — بعد از تایید/تغییر وضعیت درخواست مرخصی/ماموریت.
/// با این کار درخواست‌های «عقب‌النصر» (برای روزهای گذشته) هم بلافاصله روی کسری اعمال می‌شوند.
/// قوانین محاسبه دقیقاً مطابق AttendanceController است:
///   کارکرد = مجموع بازه‌های بسته (+ بازه‌ی باز تا پایان شیفت برای روز جاری)
///   غیبت پوشش‌شده = بازه‌های [خروج، ورود بعدی] یا [خروج آخر، پایان شیفت] که کاملاً داخل
///                  مرخصی/ماموریت ساعتی تاییدشده باشند
///   کسری = max(0, موظفی − کارکرد − غیبت‌پوشش‌شده)
/// </summary>
public class AttendanceRecalcService
{
    private readonly AppDbContext _db;
    public AttendanceRecalcService(AppDbContext db) => _db = db;

    public static int ScheduledMinutes(ShiftGroup? sg)
    {
        if (sg == null) return 8 * 60;
        var m = (int)(sg.EndTime - sg.StartTime).TotalMinutes;
        return m > 0 ? m : 8 * 60;
    }

    /// <summary>
    /// بخش «دیررس بودن» یعنی بازه‌ی [شروع شیفت، اولین ورود] — اگر مرخصی/ماموریت ساعتی تاییدشده
    /// کل این بازه را پوشش دهد، دقیقه‌های آن به‌عنوان غیبت پوشش‌شده حساب می‌شود (کسری نمی‌خورد).
    /// </summary>
    public static int LateCoveredMinutes(ShiftGroup? sg, DateTime workDate, DateTime? firstEnter, List<LeaveRequest> covers)
    {
        if (sg == null || !firstEnter.HasValue) return 0;
        var schedStart = workDate.Add(sg.StartTime);
        if (firstEnter.Value <= schedStart) return 0;
        var gs = schedStart.TimeOfDay;
        var ge = firstEnter.Value.TimeOfDay;
        return covers.Any(l => l.StartTime <= gs && l.EndTime >= ge)
            ? Math.Max(0, (int)(firstEnter.Value - schedStart).TotalMinutes)
            : 0;
    }

    /// <summary>بازحساب یک روز مشخص (اگر رکوردی نیست و مرخصی ساعتی تاییدشده‌ای هم ندارد، کاری نمی‌کند).</summary>
    public async Task RecalcDayAsync(int userId, DateTime date, DateTime asOf)
    {
        date = date.Date;
        var user = await _db.Users.Include(u => u.ShiftGroup).AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var sg = user?.ShiftGroup;
        var userName = user == null ? "" : (string.IsNullOrWhiteSpace(user.FirstName) ? user.Username : $"{user.FirstName} {user.LastName}".Trim());

        // قاعده‌ی کاری روز: تقویم کاری > تعطیل رسمی/شرکتی > شیفت > پیش‌فرض
        var cal = await _db.WorkCalendarDays.AsNoTracking().FirstOrDefaultAsync(d => d.Date.Date == date);
        var hol = await _db.CompanyHolidays.AsNoTracking().FirstOrDefaultAsync(h => h.HolidayDate.Date == date);
        var settings = await _db.WorkCalendarSettings.AsNoTracking().FirstOrDefaultAsync();
        var rule = WorkRules.Resolve(date, sg, cal, hol, settings);

        var hourlies = await _db.LeaveRequests
            .Where(l => l.RequesterUserId == userId && l.Status == "Approved"
                        && (l.Type == "Hourly" || l.Type == "HourlyMission")
                        && l.StartDate == date
                        && l.StartTime != null && l.EndTime != null)
            .ToListAsync();
        var hasDaily = await _db.LeaveRequests
            .AnyAsync(l => l.RequesterUserId == userId && l.Status == "Approved" && l.Type == "Daily"
                           && l.StartDate.Date <= date && l.EndDate.Date >= date);

        var rec = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.UserId == userId && a.WorkDate == date);

        if (rec == null)
        {
            // روزی بدون رکورد: فقط اگر مرخصی تاییدشده دارد، رکورد جایگزین بسازیم
            if (hourlies.Count == 0 && !hasDaily) return;
            rec = new AttendanceRecord
            {
                WorkDate = date,
                UserId = userId,
                UserName = userName,
                ShiftGroupId = sg?.Id,
                FinalStatus = rule.Source == "Holiday" ? "Holiday" : "LeaveDay",
                HasApprovedLeave = true,
                CreatedAt = DateTime.Now,
            };
            _db.AttendanceRecords.Add(rec);
        }
        if (!rec.ShiftGroupId.HasValue) rec.ShiftGroupId = sg?.Id;

        var segs = await _db.AttendanceSegments
            .Where(s => s.UserId == userId && s.WorkDate == date)
            .OrderBy(s => s.Seq)
            .ToListAsync();

        // ---------- ارزیابی پوشش هر بازه‌ی غیبت بسته ----------
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            if (s.ExitAt == null) continue;
            var gapEnd = (i + 1 < segs.Count && segs[i + 1].EnterAt.HasValue)
                ? segs[i + 1].EnterAt!.Value
                : date.Add(rule.End); // روز تمام شده → تا پایان روز
            var gs = s.ExitAt.Value.TimeOfDay;
            var ge = gapEnd.TimeOfDay;
            var cover = hourlies.FirstOrDefault(l => l.StartTime <= gs && l.EndTime >= ge);
            s.ExitCovered = cover != null;
            s.LinkedLeaveRequestId = cover?.Id;
            s.LinkedLeaveNumber = cover?.Number;
        }

        // ---------- تجمیع بر اساس قاعده‌ی کاری ----------
        AttendanceMath.Recompute(rec, segs, rule, hourlies, hasDaily, asOf);
        if (rule.Source == "Holiday")
        {
            rec.FinalStatus = "Holiday";
            rec.HasApprovedLeave = true;
        }
        rec.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}

