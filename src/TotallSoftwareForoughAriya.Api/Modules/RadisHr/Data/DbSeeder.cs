using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Services;
using RadisHr.Shared.Models;

namespace RadisHr.Api.Data;

/// <summary>
/// داده‌های پایه — پورت مقادیر پیش‌فرض organization.js، hse.js، calendar-settings.js و app.js
/// </summary>
public static class DbSeeder
{
    /// <summary>ساختار سازمانی پیش‌فرض (DEFAULT_ORG)</summary>
    public static readonly (string Unit, string[] Stations)[] DefaultOrg =
    {
        ("اداری",        new[] { "منابع انسانی و امور اداری", "دبیرخانه و خدمات اداری" }),
        ("تولید",        new[] { "آماده‌سازی مواد", "خط تولید", "بسته‌بندی" }),
        ("کنترل کیفیت",  new[] { "آزمایشگاه کنترل کیفیت", "کنترل خط" }),
        ("برنامه ریزی",  new[] { "برنامه‌ریزی تولید" }),
        ("تضمین کفیت",   new[] { "تضمین کیفیت" }),
        ("نگهبان",       new[] { "پست نگهبانی" })
    };

    public static readonly string[] ResponsibilityLevels =
        { "مدیر ارشد", "مدیر واحد", "سرپرست", "مسئول / کارشناس", "اپراتور / کارمند" };

    public static readonly string[] IncidentTypes =
    {
        "برخورد با اجسام", "سقوط از ارتفاع", "لغزش و زمین خوردن", "برق‌گرفتگی",
        "سوختگی", "تماس با مواد شیمیایی", "گیرافتادن در ماشین‌آلات", "سایر"
    };

    public static readonly string[] PpeTypes =
    {
        "کلاه ایمنی", "عینک ایمنی", "گوشی حفاظتی", "ماسک تنفسی", "دستکش ایمنی",
        "کفش ایمنی", "لباس کار", "کمربند ایمنی", "سایر"
    };

    public static readonly string[] ExtinguisherTypes =
        { "پودر و گاز", "CO2", "آب و گاز", "فوم" };

    /// <summary>ده پرسنل نمونهٔ نسخهٔ اصلی — sampleEmployees در app.js</summary>
    public static readonly Employee[] SampleEmployees =
    {
        new() { First = "میثم", Last = "تاباق", Code = "101", Nid = "4591312968", Married = "متاهل", Children = 2, Salary = 450000000m, Hire = "1401/10/01", Unit = "اداری", WorkStation = "منابع انسانی و امور اداری", ResponsibilityLevel = "مدیر واحد", PositionTitle = "مدیر اداری" },
        new() { First = "محمد رضا", Last = "سلطانی قمبوانی", Code = "102", Nid = "4567895968", Married = "متاهل", Children = 1, Salary = 400000000m, Hire = "1401/09/10", Unit = "تولید", WorkStation = "خط تولید", ResponsibilityLevel = "مدیر واحد", PositionTitle = "مدیر تولید" },
        new() { First = "محمد", Last = "قیصری پور", Code = "103", Nid = "6581645967", Married = "مجرد", Children = 0, Salary = 328759102m, Hire = "1402/02/05", Unit = "تولید", WorkStation = "آماده‌سازی مواد", ResponsibilityLevel = "اپراتور / کارمند", PositionTitle = "اپراتور آماده‌سازی" },
        new() { First = "مسلم", Last = "اسماعیلی", Code = "104", Nid = "3211548756", Married = "متاهل", Children = 1, Salary = 460000000m, Hire = "1402/02/06", Unit = "تولید", WorkStation = "خط تولید", ResponsibilityLevel = "سرپرست", PositionTitle = "سرپرست خط" },
        new() { First = "عمران", Last = "مقدسی", Code = "105", Nid = "0078514698", Married = "متاهل", Children = 3, Salary = 320000000m, Hire = "1403/05/25", Unit = "تولید", WorkStation = "بسته‌بندی", ResponsibilityLevel = "اپراتور / کارمند", PositionTitle = "اپراتور بسته‌بندی" },
        new() { First = "محمد", Last = "بیرقی", Code = "106", Nid = "0123625145", Married = "مجرد", Children = 0, Salary = 345000000m, Hire = "1402/03/18", Unit = "کنترل کیفیت", WorkStation = "آزمایشگاه کنترل کیفیت", ResponsibilityLevel = "مسئول / کارشناس", PositionTitle = "کارشناس کنترل کیفیت" },
        new() { First = "محمدرضا", Last = "حبیب خانی", Code = "107", Nid = "0168735592", Married = "متاهل", Children = 2, Salary = 370000000m, Hire = "1402/03/19", Unit = "برنامه ریزی", WorkStation = "برنامه‌ریزی تولید", ResponsibilityLevel = "مسئول / کارشناس", PositionTitle = "کارشناس برنامه‌ریزی" },
        new() { First = "شاهین", Last = "محمدی", Code = "108", Nid = "0213846039", Married = "مجرد", Children = 2, Salary = 395000000m, Hire = "1402/03/20", Unit = "تضمین کفیت", WorkStation = "تضمین کیفیت", ResponsibilityLevel = "مسئول / کارشناس", PositionTitle = "کارشناس تضمین کیفیت" },
        new() { First = "حمید رضا", Last = "عرب عامری", Code = "109", Nid = "0258956486", Married = "متاهل", Children = 3, Salary = 420000000m, Hire = "1402/03/21", Unit = "اداری", WorkStation = "دبیرخانه و خدمات اداری", ResponsibilityLevel = "مسئول / کارشناس", PositionTitle = "کارشناس اداری" },
        new() { First = "محسن", Last = "کمانی", Code = "110", Nid = "3211588756", Married = "متاهل", Children = 0, Salary = 320000000m, Hire = "1402/03/22", Unit = "نگهبان", WorkStation = "پست نگهبانی", ResponsibilityLevel = "اپراتور / کارمند", PositionTitle = "نگهبان" }
    };

    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        // ── پرسنل نمونه (فقط در نخستین اجرا)
        if (!await db.Employees.AnyAsync())
            foreach (var sample in SampleEmployees)
                db.Employees.Add(new Employee
                {
                    First = sample.First, Last = sample.Last, Code = sample.Code, Nid = sample.Nid,
                    Married = sample.Married, Children = sample.Children, Salary = sample.Salary,
                    Hire = sample.Hire, Unit = sample.Unit, WorkStation = sample.WorkStation,
                    ResponsibilityLevel = sample.ResponsibilityLevel, PositionTitle = sample.PositionTitle,
                    ContractType = "دائم", IsActive = true
                });

        // ── الزامات قانونی ۱۴۰۵
        if (!await db.StatutoryRules.AnyAsync())
            db.StatutoryRules.Add(StatutoryRules.CreateDefault(1405));

        // ── ساختار سازمانی
        if (!await db.Departments.AnyAsync())
        {
            var order = 0;
            foreach (var (unit, stations) in DefaultOrg)
            {
                db.Departments.Add(new Department
                {
                    Uid = Guid.NewGuid().ToString("N"),
                    Name = unit,
                    Ordinal = order++,
                    Stations = stations.Select(s => new WorkStation
                    {
                        Uid = Guid.NewGuid().ToString("N"),
                        Name = s
                    }).ToList()
                });
            }
        }

        // ── برنامهٔ کاری پیش‌فرض هر واحد (۰۸:۰۰ تا ۱۶:۰۰)
        if (!await db.UnitSchedules.AnyAsync())
            foreach (var (unit, _) in DefaultOrg)
                db.UnitSchedules.Add(new UnitSchedule { Unit = unit });

        // ── تعاریف HSE
        if (!await db.HseDefinitions.AnyAsync())
        {
            void AddDefs(string group, string[] items)
            {
                for (var i = 0; i < items.Length; i++)
                    db.HseDefinitions.Add(new HseDefinition { Group = group, Title = items[i], Ordinal = i });
            }
            AddDefs("incidentTypes", IncidentTypes);
            AddDefs("ppeTypes", PpeTypes);
            AddDefs("extinguisherTypes", ExtinguisherTypes);
        }

        // ── حساب‌های کاربری
        if (!await db.Users.AnyAsync())
        {
            var initial = config["Seed:InitialPassword"] ?? "Radis@1405";
            foreach (var (key, meta) in RolePages.Map)
            {
                var (hash, salt) = PasswordHasher.Hash(initial);
                db.Users.Add(new AppUser
                {
                    UserKey = key,
                    DisplayName = meta.Name,
                    RoleTitle = meta.Role,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    MustChangePassword = true,
                    IsActive = true
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
