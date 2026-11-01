namespace Inventory.Api.Data;

/// <summary>
/// داده پایه ماژول حضور و غیاب (انواع مرخصی + شیفت‌های پیش‌فرض + دستگاه نمونه).
/// برخلاف دیتای دمو، همیشه و بدون شرط پرچم اجرا می‌شود (اگر جدول خالی باشد).
/// </summary>
public static class FaAttMasterSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (!db.FaAttLeaveTypes.Any())
        {
            db.FaAttLeaveTypes.AddRange(
                new FaAttLeaveType { Name = "استحقاقی", AnnualLimitDays = 26, SortOrder = 1 },
                new FaAttLeaveType { Name = "استعلاجی", AnnualLimitDays = null, SortOrder = 2 },
                new FaAttLeaveType { Name = "بدون حقوق", AnnualLimitDays = null, SortOrder = 3 },
                new FaAttLeaveType { Name = "ساعتی", AnnualLimitDays = null, SortOrder = 4 },
                new FaAttLeaveType { Name = "تشویقی", AnnualLimitDays = 3, SortOrder = 5 });
            db.SaveChanges();
        }

        // §۷: انواع مرخصی زایمان/پدرانه/ازدواج/فوت (upsert با نام — برای دیتابیس‌هایی که سیدر قبلی را دارند)
        var wantTypes = new (string Name, int? Limit, int Order)[]
        {
            ("زایمان", 270, 6),   // ۹ ماه
            ("پدرانه", 14, 7),
            ("ازدواج", 3, 8),
            ("فوت", 3, 9),
        };
        var haveTypes = db.FaAttLeaveTypes.Select(t => t.Name).ToList();
        var typesChanged = false;
        foreach (var (tName, tLimit, tOrder) in wantTypes)
            if (!haveTypes.Contains(tName))
            {
                db.FaAttLeaveTypes.Add(new FaAttLeaveType { Name = tName, AnnualLimitDays = tLimit, SortOrder = tOrder });
                typesChanged = true;
            }
        if (typesChanged) db.SaveChanges();

        if (!db.FaAttShifts.Any())
        {
            db.FaAttShifts.AddRange(
                new FaAttShift
                {
                    Code = "M", Name = "شیفت صبح (ثابت)", Type = FaAttShiftType.Fixed,
                    StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(17, 0, 0),
                    LateToleranceMin = 10, EarlyToleranceMin = 5, OvertimeGraceMin = 10,
                    RequiredMinutes = 480, OffDays = "5", Color = "#2563eb", SortOrder = 1
                },
                new FaAttShift
                {
                    Code = "E", Name = "شیفت عصر (ثابت)", Type = FaAttShiftType.Fixed,
                    StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(22, 0, 0),
                    LateToleranceMin = 10, EarlyToleranceMin = 5, OvertimeGraceMin = 10,
                    RequiredMinutes = 480, OffDays = "5", Color = "#f59e0b", SortOrder = 2
                },
                new FaAttShift
                {
                    Code = "N", Name = "شب‌کاری", Type = FaAttShiftType.Night,
                    StartTime = new TimeSpan(22, 0, 0), EndTime = new TimeSpan(6, 0, 0),
                    LateToleranceMin = 10, EarlyToleranceMin = 10, OvertimeGraceMin = 15,
                    RequiredMinutes = 480, OffDays = "5", Color = "#7c3aed", SortOrder = 3
                },
                new FaAttShift
                {
                    Code = "F", Name = "شناور", Type = FaAttShiftType.Flexible,
                    StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(17, 0, 0),
                    LateToleranceMin = 0, EarlyToleranceMin = 0, OvertimeGraceMin = 10,
                    RequiredMinutes = 480, OffDays = "5", Color = "#0d9488", SortOrder = 4
                });
            db.SaveChanges();
        }

        if (!db.FaAttDevices.Any())
        {
            db.FaAttDevices.Add(new FaAttDevice
            {
                Code = "DEV-01", Name = "دستگاه ورودی اصلی", Type = FaAttDeviceType.Fingerprint,
                Location = "ورودی ساختمان مرکزی", IsActive = true
            });
            db.SaveChanges();
        }
    }
}
