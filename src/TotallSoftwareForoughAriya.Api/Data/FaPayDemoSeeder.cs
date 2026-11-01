using System.Globalization;

namespace Inventory.Api.Data;

/// <summary>
/// داده نمونه حقوق و دستمزد فروغ آریا (FaPay §۸) — فقط در حالت دمو (Database:SeedDemoData=true).
/// مبالغ پیش‌فرض مزایای ثابت + چند ثبت ماهانه نمونه برای ماه جاری.
/// </summary>
public static class FaPayDemoSeeder
{
    public static void Seed(AppDbContext db)
    {
        var types = db.FaPayItemTypes.ToDictionary(t => t.Code, t => t);
        if (types.Count == 0) return;

        // ---------- مبالغ پیش‌فرض مزایای ثابت (فقط اگر صفر باشند) ----------
        void SetDefault(string code, double amount)
        {
            if (types.TryGetValue(code, out var t) && t.DefaultAmount == 0)
                t.DefaultAmount = amount;
        }
        SetDefault("HOUSING", 9_000_000);
        SetDefault("FOOD", 5_000_000);
        SetDefault("TRANS", 3_000_000);
        db.SaveChanges();

        // ---------- ثبت‌های ماه جاری ----------
        var pc = new PersianCalendar();
        var jy = pc.GetYear(DateTime.Today);
        var jm = pc.GetMonth(DateTime.Today);
        var emps = db.HrEmployees.Where(e => e.IsActive).OrderBy(e => e.Code).Take(6).ToList();
        if (emps.Count == 0) return;

        void Add(int empIdx, string typeCode, double amount, string? note)
        {
            if (empIdx >= emps.Count || !types.TryGetValue(typeCode, out var t)) return;
            var e = emps[empIdx];
            if (db.FaPayAdjustments.Any(a => a.EmployeeId == e.Id && a.Year == jy && a.Month == jm && a.ItemTypeId == t.Id))
                return;
            db.FaPayAdjustments.Add(new FaPayAdjustment
            {
                EmployeeId = e.Id, Year = jy, Month = jm, ItemTypeId = t.Id,
                Amount = amount, Note = note, CreatedByUserId = 1, CreatedByName = "demo", CreatedAt = DateTime.Now
            });
        }
        Add(0, "KARANEH", 15_000_000, "کارانه ماه جاری");
        Add(1, "KARANEH", 12_000_000, "کارانه ماه جاری");
        Add(2, "PADASH", 5_000_000, "پاداش پروژه");
        Add(3, "MISSION", 2_500_000, "حق مأموریت");
        Add(4, "ADVANCE", 10_000_000, "مساعده — کسر از حقوق");
        db.SaveChanges();
    }
}
