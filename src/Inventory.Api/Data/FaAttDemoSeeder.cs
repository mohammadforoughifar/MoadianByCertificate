using System.Globalization;

namespace Inventory.Api.Data;

/// <summary>
/// داده نمونه حضور و مرخصی فروغ آریا (§۷) — فقط در حالت دمو (Database:SeedDemoData=true).
/// زنجیره مدیران (ManagerId)، مانده اول سال، چند درخواست نمونه و یک مأموریت.
/// </summary>
public static class FaAttDemoSeeder
{
    public static void Seed(AppDbContext db)
    {
        var emps = db.HrEmployees.ToDictionary(e => e.Code, e => e);
        if (emps.Count == 0) return;

        // ---------- زنجیره مدیران ----------
        void SetMgr(string empCode, string mgrCode)
        {
            if (emps.TryGetValue(empCode, out var e) && emps.TryGetValue(mgrCode, out var m))
                e.ManagerId = m.Id;
        }
        SetMgr("1002", "1001");
        SetMgr("1003", "1002");
        SetMgr("1004", "1001");
        SetMgr("1005", "1001");
        SetMgr("1006", "1001");
        db.SaveChanges();

        // ---------- مانده اول سال جاری برای انواع دارای استحقاق ----------
        var jy = new PersianCalendar().GetYear(DateTime.Today);
        var types = db.FaAttLeaveTypes.Where(t => t.IsActive).ToList();
        var rules = db.HrMainRules.FirstOrDefault();
        foreach (var e in db.HrEmployees.Where(x => x.IsActive).ToList())
        {
            foreach (var t in types)
            {
                if (db.FaAttLeaveBalances.Any(b => b.EmployeeId == e.Id && b.Year == jy && b.LeaveTypeId == t.Id))
                    continue;
                double entitled = t.Name == "استحقاقی"
                    ? (rules?.AnnualLeaveDays ?? t.AnnualLimitDays ?? 26)
                    : (t.AnnualLimitDays ?? 0);
                db.FaAttLeaveBalances.Add(new FaAttLeaveBalance
                {
                    EmployeeId = e.Id, Year = jy, LeaveTypeId = t.Id, EntitledDays = entitled
                });
            }
        }
        db.SaveChanges();

        // ---------- درخواست‌های نمونه ----------
        if (!db.FaAttLeaves.Any() && emps.TryGetValue("1003", out var dev) && emps.TryGetValue("1004", out var acc))
        {
            var est = types.FirstOrDefault(t => t.Name == "استحقاقی");
            if (est != null)
            {
                // یک درخواست در انتظار نزد مدیر
                db.FaAttLeaves.Add(new FaAttLeave
                {
                    EmployeeId = dev.Id, LeaveTypeId = est.Id,
                    FromDate = DateTime.Today.AddDays(7), ToDate = DateTime.Today.AddDays(8),
                    Reason = "کار شخصی", Status = FaAttRequestStatus.Pending, WorkflowStep = 0,
                    CreatedAt = DateTime.Now
                });
                // یک مرخصی تأییدشده گذشته + مصرف مانده
                var from = DateTime.Today.AddDays(-20);
                db.FaAttLeaves.Add(new FaAttLeave
                {
                    EmployeeId = acc.Id, LeaveTypeId = est.Id,
                    FromDate = from, ToDate = from.AddDays(1),
                    Reason = "سفر", Status = FaAttRequestStatus.Approved, WorkflowStep = 2,
                    ManagerDecidedByName = "مدیرعامل", ManagerDecidedAt = DateTime.Now.AddDays(-21),
                    DecidedByName = "کارشناس منابع انسانی", DecidedAt = DateTime.Now.AddDays(-21),
                    CreatedAt = DateTime.Now.AddDays(-22)
                });
                var bal = db.FaAttLeaveBalances.FirstOrDefault(b =>
                    b.EmployeeId == acc.Id && b.Year == jy && b.LeaveTypeId == est.Id);
                if (bal != null) bal.UsedDays += 2;
            }
        }

        // ---------- یک مأموریت نمونه ----------
        if (!db.FaAttMissions.Any() && emps.TryGetValue("1005", out var sal))
        {
            db.FaAttMissions.Add(new FaAttMission
            {
                EmployeeId = sal.Id,
                FromDate = DateTime.Today.AddDays(3), ToDate = DateTime.Today.AddDays(4),
                Destination = "اصفهان", Reason = "بازدید مشتری",
                Status = FaAttRequestStatus.Pending, CreatedAt = DateTime.Now
            });
        }
        db.SaveChanges();
    }
}
