namespace Inventory.Api.Data;

/// <summary>
/// داده نمونه ماژول منابع انسانی (HrMain + HrCore + پرونده پرسنل) برای مشاهده و تست.
/// فقط وقتی اجرا می‌شود که Database:SeedDemoData=true و جدول پرسنل خالی باشد.
/// تاریخ‌ها نسبی به امروز ساخته می‌شوند تا هشدارهای انقضا (قرارداد/سند) همیشه قابل مشاهده باشند.
/// </summary>
public static class HrDemoSeeder
{
    public static void Seed(AppDbContext db)
    {
        var today = DateTime.Today;

        SeedHrMain(db);
        SeedOrgUnits(db);

        if (!db.HrEmployees.Any())
            SeedEmployees(db, today);
        if (!db.HrContracts.Any())
            SeedContracts(db, today);
        if (!db.HrDecrees.Any())
            SeedDecrees(db, today);
        if (!db.HrEmployeeDependents.Any())
            SeedDossier(db, today);
    }

    // ==================== ساختار سازمانی جدید (HrMain) ====================

    private static void SeedHrMain(AppDbContext db)
    {
        if (!db.HrMainCompanies.Any())
            db.HrMainCompanies.Add(new HrMainCompany
            {
                Name = "شرکت فروغ آریا",
                Address = "تهران، خیابان ولیعصر، پلاک ۱",
                Phone = "021-55667788",
                Email = "info@forougharia.example",
                ManagerName = "علی رضایی"
            });
        if (!db.HrMainLocales.Any())
            db.HrMainLocales.Add(new HrMainLocale { Language = "fa", Calendar = "jalali" });
        if (!db.HrMainRules.Any())
            db.HrMainRules.Add(new HrMainRules());
        if (!db.HrMainBranches.Any())
        {
            db.HrMainBranches.AddRange(
                new HrMainBranch
                {
                    Code = "BR-01", Name = "ستاد تهران", Type = HrMainBranchType.Branch,
                    City = "تهران", Address = "خیابان ولیعصر، پلاک ۱",
                    Phone = "021-55667788", ManagerName = "علی رضایی", SortOrder = 1
                },
                new HrMainBranch
                {
                    Code = "BR-02", Name = "شعبه اصفهان", Type = HrMainBranchType.Office,
                    City = "اصفهان", Phone = "031-33445566", SortOrder = 2
                });
        }
        db.SaveChanges();

        if (!db.HrMainOrgNodes.Any())
        {
            var root = new HrMainOrgNode { Code = "N-01", Name = "ستاد", Level = HrMainOrgLevel.Company, SortOrder = 1 };
            db.HrMainOrgNodes.Add(root);
            db.SaveChanges();
            var it = new HrMainOrgNode { Code = "N-IT", Name = "واحد فناوری اطلاعات", Level = HrMainOrgLevel.Unit, ParentId = root.Id, ManagerTitle = "مدیر فناوری اطلاعات", SortOrder = 1 };
            var fin = new HrMainOrgNode { Code = "N-FIN", Name = "واحد مالی", Level = HrMainOrgLevel.Unit, ParentId = root.Id, SortOrder = 2 };
            var sal = new HrMainOrgNode { Code = "N-SAL", Name = "واحد فروش", Level = HrMainOrgLevel.Unit, ParentId = root.Id, SortOrder = 3 };
            db.HrMainOrgNodes.AddRange(it, fin, sal);
            db.SaveChanges();
            var sw = new HrMainOrgNode { Code = "N-SW", Name = "دپارتمان نرم‌افزار", Level = HrMainOrgLevel.Department, ParentId = it.Id, SortOrder = 1 };
            db.HrMainOrgNodes.Add(sw);
            db.SaveChanges();
            db.HrMainOrgNodes.Add(new HrMainOrgNode { Code = "N-BE", Name = "تیم بک‌اند", Level = HrMainOrgLevel.Team, ParentId = sw.Id, SortOrder = 1 });
            db.SaveChanges();
        }

        if (!db.HrMainPositions.Any())
        {
            var root = db.HrMainOrgNodes.First(n => n.Code == "N-01");
            var it = db.HrMainOrgNodes.First(n => n.Code == "N-IT");
            var sw = db.HrMainOrgNodes.First(n => n.Code == "N-SW");
            var fin = db.HrMainOrgNodes.First(n => n.Code == "N-FIN");
            var sal = db.HrMainOrgNodes.First(n => n.Code == "N-SAL");
            db.HrMainPositions.AddRange(
                new HrMainPosition { Code = "P-CEO", Title = "مدیرعامل", OrgNodeId = root.Id, Grade = "ارشد", JobDescription = "راهبری کلی شرکت و نظارت بر واحدها", HeadCount = 1 },
                new HrMainPosition { Code = "P-CTO", Title = "مدیر فناوری اطلاعات", OrgNodeId = it.Id, Grade = "ارشد", JobDescription = "مدیریت واحد فناوری و تیم‌های نرم‌افزار", HeadCount = 1 },
                new HrMainPosition { Code = "P-DEV", Title = "کارشناس نرم‌افزار", OrgNodeId = sw.Id, Grade = "کارشناس", JobDescription = "توسعه و نگهداری سامانه‌ها", Requirements = "کارشناسی نرم‌افزار، ۲ سال سابقه", HeadCount = 5 },
                new HrMainPosition { Code = "P-ACC", Title = "کارشناس مالی", OrgNodeId = fin.Id, Grade = "کارشناس", JobDescription = "امور مالی و حسابداری", HeadCount = 2 },
                new HrMainPosition { Code = "P-SAL", Title = "کارشناس فروش", OrgNodeId = sal.Id, Grade = "کارشناس", JobDescription = "فروش و ارتباط با مشتریان", HeadCount = 3 });
            db.SaveChanges();
        }
    }

    // ==================== ساختار قدیمی (HrOrgUnit) ====================

    private static void SeedOrgUnits(AppDbContext db)
    {
        if (db.HrOrgUnits.Any()) return;
        var co = new HrOrgUnit { Code = "C-01", Name = "شرکت فروغ آریا", Type = HrOrgUnitType.Company, SortOrder = 1 };
        db.HrOrgUnits.Add(co);
        db.SaveChanges();
        db.HrOrgUnits.AddRange(
            new HrOrgUnit { Code = "IT-01", Name = "فناوری اطلاعات", Type = HrOrgUnitType.Department, ParentId = co.Id, Phone = "021-55667788", SortOrder = 1 },
            new HrOrgUnit { Code = "FIN-01", Name = "مالی و حسابداری", Type = HrOrgUnitType.Department, ParentId = co.Id, SortOrder = 2 },
            new HrOrgUnit { Code = "SAL-01", Name = "فروش و بازاریابی", Type = HrOrgUnitType.Department, ParentId = co.Id, SortOrder = 3 },
            new HrOrgUnit { Code = "HR-01", Name = "منابع انسانی", Type = HrOrgUnitType.Department, ParentId = co.Id, SortOrder = 4 });
        db.SaveChanges();
    }

    // ==================== پرسنل ====================

    private static void SeedEmployees(AppDbContext db, DateTime today)
    {
        var co = db.HrOrgUnits.First(u => u.Code == "C-01");
        var it = db.HrOrgUnits.First(u => u.Code == "IT-01");
        var fin = db.HrOrgUnits.First(u => u.Code == "FIN-01");
        var sal = db.HrOrgUnits.First(u => u.Code == "SAL-01");
        var hr = db.HrOrgUnits.First(u => u.Code == "HR-01");
        var nRoot = db.HrMainOrgNodes.First(n => n.Code == "N-01");
        var nIt = db.HrMainOrgNodes.First(n => n.Code == "N-IT");
        var nSw = db.HrMainOrgNodes.First(n => n.Code == "N-SW");
        var nFin = db.HrMainOrgNodes.First(n => n.Code == "N-FIN");
        var nSal = db.HrMainOrgNodes.First(n => n.Code == "N-SAL");
        var pCeo = db.HrMainPositions.First(p => p.Code == "P-CEO");
        var pCto = db.HrMainPositions.First(p => p.Code == "P-CTO");
        var pDev = db.HrMainPositions.First(p => p.Code == "P-DEV");
        var pAcc = db.HrMainPositions.First(p => p.Code == "P-ACC");
        var pSal = db.HrMainPositions.First(p => p.Code == "P-SAL");

        db.HrEmployees.AddRange(
            new HrEmployee
            {
                Code = "1001", FirstName = "علی", LastName = "رضایی", NationalCode = "0012345678",
                BirthDate = today.AddYears(-45).AddDays(-12), Gender = 0, MaritalStatus = 1,
                Mobile = "09121110001", Landline = "02155667701", Email = "rezayi@forougharia.example",
                Address = "تهران، خیابان ولیعصر، کوچه بهار، پلاک ۵",
                HireDate = today.AddYears(-8), OrgUnitId = co.Id, PostTitle = "مدیرعامل",
                HrMainNodeId = nRoot.Id, HrMainPositionId = pCeo.Id,
                EmploymentType = HrEmploymentType.Rasmi, Status = HrEmployeeStatus.Active,
                Workplace = "ساختمان مرکزی — طبقه ۳", Degree = "کارشناسی ارشد", FieldOfStudy = "مدیریت اجرایی",
                EmergencyContactName = "زهرا رضایی", EmergencyContactRelation = "همسر", EmergencyContactPhone = "09121110002",
                BaseSalary = 250_000_000
            },
            new HrEmployee
            {
                Code = "1002", FirstName = "سارا", LastName = "محمدی", NationalCode = "0023456789",
                BirthDate = today.AddYears(-34).AddDays(-40), Gender = 1, MaritalStatus = 1,
                Mobile = "09121110003", Landline = "02155667702", Email = "mohammadi@forougharia.example",
                Address = "تهران، سعادت‌آباد، بلوک ۲",
                HireDate = today.AddYears(-6), OrgUnitId = it.Id, PostTitle = "مدیر فناوری اطلاعات",
                HrMainNodeId = nIt.Id, HrMainPositionId = pCto.Id,
                EmploymentType = HrEmploymentType.TamamVaght, Status = HrEmployeeStatus.Active,
                Workplace = "ساختمان مرکزی — طبقه ۲", Degree = "کارشناسی", FieldOfStudy = "مهندسی نرم‌افزار",
                EmergencyContactName = "محمد محمدی", EmergencyContactRelation = "همسر", EmergencyContactPhone = "09121110004",
                BaseSalary = 180_000_000
            },
            new HrEmployee
            {
                Code = "1003", FirstName = "حسین", LastName = "کریمی", NationalCode = "0034567890",
                BirthDate = today.AddYears(-29).AddDays(-100), Gender = 0, MaritalStatus = 1,
                Mobile = "09121110005", Email = "karimi@forougharia.example",
                Address = "تهران، نارمک، خیابان ۴۶",
                HireDate = today.AddYears(-3), OrgUnitId = it.Id, PostTitle = "کارشناس نرم‌افزار",
                HrMainNodeId = nSw.Id, HrMainPositionId = pDev.Id,
                EmploymentType = HrEmploymentType.Gharardadi, Status = HrEmployeeStatus.Active,
                Workplace = "ساختمان مرکزی — طبقه ۲", Degree = "کارشناسی", FieldOfStudy = "علوم کامپیوتر",
                EmergencyContactName = "الناز کریمی", EmergencyContactRelation = "همسر", EmergencyContactPhone = "09121110006",
                BaseSalary = 95_000_000
            },
            new HrEmployee
            {
                Code = "1004", FirstName = "مریم", LastName = "احمدی", NationalCode = "0045678901",
                BirthDate = today.AddYears(-31).AddDays(-200), Gender = 1, MaritalStatus = 0,
                Mobile = "09121110007", Landline = "02155667703",
                Address = "تهران، یوسف‌آباد، کوچه ۱۲",
                HireDate = today.AddYears(-4), OrgUnitId = fin.Id, PostTitle = "کارشناس مالی",
                HrMainNodeId = nFin.Id, HrMainPositionId = pAcc.Id,
                EmploymentType = HrEmploymentType.Peymani, Status = HrEmployeeStatus.Active,
                Workplace = "ساختمان مرکزی — طبقه ۱", Degree = "کارشناسی", FieldOfStudy = "حسابداری",
                EmergencyContactName = "حسن احمدی", EmergencyContactRelation = "پدر", EmergencyContactPhone = "09121110008",
                BaseSalary = 88_000_000
            },
            new HrEmployee
            {
                Code = "1005", FirstName = "رضا", LastName = "موسوی", NationalCode = "0056789012",
                BirthDate = today.AddYears(-27).AddDays(-60), Gender = 0, MaritalStatus = 0,
                Mobile = "09121110009",
                HireDate = today.AddYears(-1), OrgUnitId = sal.Id, PostTitle = "کارشناس فروش",
                HrMainNodeId = nSal.Id, HrMainPositionId = pSal.Id,
                EmploymentType = HrEmploymentType.Projei, Status = HrEmployeeStatus.Active,
                Workplace = "شعبه اصفهان", Degree = "کارشناسی", FieldOfStudy = "بازاریابی",
                EmergencyContactName = "فاطمه موسوی", EmergencyContactRelation = "مادر", EmergencyContactPhone = "09121110010",
                BaseSalary = 70_000_000
            },
            new HrEmployee
            {
                Code = "1006", FirstName = "نگار", LastName = "صادقی", NationalCode = "0067890123",
                BirthDate = today.AddYears(-26).AddDays(-150), Gender = 1, MaritalStatus = 0,
                Mobile = "09121110011",
                HireDate = today.AddYears(-2), OrgUnitId = hr.Id, PostTitle = "کارشناس منابع انسانی",
                HrMainNodeId = nRoot.Id,
                EmploymentType = HrEmploymentType.PareVaght, Status = HrEmployeeStatus.OnLeave,
                Workplace = "ساختمان مرکزی — طبقه ۱", Degree = "کارشناسی", FieldOfStudy = "مدیریت منابع انسانی",
                BaseSalary = 65_000_000
            },
            new HrEmployee
            {
                Code = "1007", FirstName = "امیر", LastName = "حسینی", NationalCode = "0078901234",
                BirthDate = today.AddYears(-24).AddDays(-20), Gender = 0, MaritalStatus = 0,
                Mobile = "09121110013",
                HireDate = today.AddMonths(-3), OrgUnitId = it.Id, PostTitle = "تکنسین پشتیبانی",
                HrMainNodeId = nSw.Id,
                EmploymentType = HrEmploymentType.Azmayeshi, Status = HrEmployeeStatus.Active,
                Workplace = "ساختمان مرکزی — طبقه ۲", Degree = "کاردانی", FieldOfStudy = "فناوری اطلاعات",
                BaseSalary = 55_000_000
            },
            new HrEmployee
            {
                Code = "1008", FirstName = "لیلا", LastName = "فرهادی", NationalCode = "0089012345",
                BirthDate = today.AddYears(-30).AddDays(-90), Gender = 1, MaritalStatus = 1,
                Mobile = "09121110015",
                HireDate = today.AddYears(-2), OrgUnitId = sal.Id, PostTitle = "کارشناس فروش",
                HrMainNodeId = nSal.Id, HrMainPositionId = pSal.Id,
                EmploymentType = HrEmploymentType.Saati, Status = HrEmployeeStatus.Terminated,
                Workplace = "شعبه اصفهان",
                IsActive = false,
                BaseSalary = 60_000_000
            });
        db.SaveChanges();

        // زنجیره مدیران + مدیران واحدها
        var byCode = db.HrEmployees.ToDictionary(e => e.Code);
        byCode["1002"].ManagerId = byCode["1001"].Id;
        byCode["1003"].ManagerId = byCode["1002"].Id;
        byCode["1004"].ManagerId = byCode["1001"].Id;
        byCode["1005"].ManagerId = byCode["1001"].Id;
        byCode["1006"].ManagerId = byCode["1001"].Id;
        byCode["1007"].ManagerId = byCode["1002"].Id;
        byCode["1008"].ManagerId = byCode["1001"].Id;
        it.ManagerEmployeeId = byCode["1002"].Id;
        fin.ManagerEmployeeId = byCode["1004"].Id;
        sal.ManagerEmployeeId = byCode["1005"].Id;
        hr.ManagerEmployeeId = byCode["1006"].Id;
        db.SaveChanges();
    }

    // ==================== قراردادها ====================

    private static void SeedContracts(AppDbContext db, DateTime today)
    {
        var byCode = db.HrEmployees.ToDictionary(e => e.Code);
        var it = db.HrOrgUnits.First(u => u.Code == "IT-01");
        var fin = db.HrOrgUnits.First(u => u.Code == "FIN-01");
        var sal = db.HrOrgUnits.First(u => u.Code == "SAL-01");
        db.HrContracts.AddRange(
            new HrContract
            {
                EmployeeId = byCode["1001"].Id, ContractNo = "C-1404-001", Type = HrEmploymentType.Rasmi,
                StartDate = today.AddYears(-8), EndDate = null,
                BaseSalary = 250_000_000, JobTitle = "مدیرعامل", IsActive = true
            },
            new HrContract
            {
                EmployeeId = byCode["1002"].Id, ContractNo = "C-1404-002", Type = HrEmploymentType.TamamVaght,
                StartDate = today.AddYears(-6), EndDate = today.AddDays(400),
                BaseSalary = 180_000_000, JobTitle = "مدیر فناوری اطلاعات", OrgUnitId = it.Id, IsActive = true
            },
            new HrContract
            {
                EmployeeId = byCode["1003"].Id, ContractNo = "C-1404-003", Type = HrEmploymentType.Gharardadi,
                StartDate = today.AddYears(-1), EndDate = today.AddDays(20),
                BaseSalary = 95_000_000, JobTitle = "کارشناس نرم‌افزار", OrgUnitId = it.Id, IsActive = true,
                Description = "قرارداد یک‌ساله — نزدیک به پایان"
            },
            new HrContract
            {
                EmployeeId = byCode["1003"].Id, ContractNo = "C-1403-009", Type = HrEmploymentType.Gharardadi,
                StartDate = today.AddYears(-2), EndDate = today.AddYears(-1),
                BaseSalary = 80_000_000, JobTitle = "کارشناس نرم‌افزار", IsActive = false
            },
            new HrContract
            {
                EmployeeId = byCode["1004"].Id, ContractNo = "C-1404-004", Type = HrEmploymentType.Peymani,
                StartDate = today.AddYears(-2), EndDate = today.AddDays(-10),
                BaseSalary = 88_000_000, JobTitle = "کارشناس مالی", OrgUnitId = fin.Id, IsActive = true,
                Description = "منقضی شده — نیاز به تمدید"
            },
            new HrContract
            {
                EmployeeId = byCode["1005"].Id, ContractNo = "C-1404-005", Type = HrEmploymentType.Projei,
                StartDate = today.AddMonths(-6), EndDate = today.AddMonths(6),
                BaseSalary = 70_000_000, JobTitle = "کارشناس فروش", OrgUnitId = sal.Id, IsActive = true
            });
        db.SaveChanges();
    }

    // ==================== احکام ====================

    private static void SeedDecrees(AppDbContext db, DateTime today)
    {
        var byCode = db.HrEmployees.ToDictionary(e => e.Code);
        db.HrDecrees.AddRange(
            new HrDecree
            {
                EmployeeId = byCode["1003"].Id, DecreeNo = "H-101", Type = HrDecreeType.Erteqa,
                EffectiveDate = today.AddDays(5), NewPostTitle = "کارشناس ارشد نرم‌افزار",
                NewBaseSalary = 105_000_000, Description = "ارتقا به کارشناس ارشد",
                IsApplied = false, CreatedByName = "مدیر سیستم"
            },
            new HrDecree
            {
                EmployeeId = byCode["1005"].Id, DecreeNo = "H-102", Type = HrDecreeType.TaghirHoghugh,
                EffectiveDate = today.AddMonths(-1), NewBaseSalary = 70_000_000,
                Description = "افزایش حقوق سالانه",
                IsApplied = true, AppliedAt = today.AddMonths(-1), CreatedByName = "مدیر سیستم"
            });
        db.SaveChanges();
    }

    // ==================== پرونده: تحت‌تکفل، دوره‌ها، مهارت‌ها، زبان‌ها، اسناد ====================

    private static void SeedDossier(AppDbContext db, DateTime today)
    {
        var byCode = db.HrEmployees.ToDictionary(e => e.Code);
        var e2 = byCode["1002"].Id;
        var e3 = byCode["1003"].Id;

        db.HrEmployeeDependents.AddRange(
            new HrEmployeeDependent { EmployeeId = e2, FullName = "محمد محمدی", Relation = "همسر", BirthDate = today.AddYears(-36), NationalCode = "0023456780", IsActive = true },
            new HrEmployeeDependent { EmployeeId = e2, FullName = "دینا محمدی", Relation = "فرزند", BirthDate = today.AddYears(-8), IsActive = true },
            new HrEmployeeDependent { EmployeeId = e3, FullName = "الناز کریمی", Relation = "همسر", BirthDate = today.AddYears(-27), IsActive = true });

        db.HrEmployeeCourses.AddRange(
            new HrEmployeeCourse { EmployeeId = e2, Title = "مدیریت پروژه چابک (اسکرام)", Institute = "جهاد دانشگاهی", Year = 1403, DurationHours = 40, HasCertificate = true },
            new HrEmployeeCourse { EmployeeId = e2, Title = "امنیت شبکه‌های سازمانی", Institute = "مجتمع فنی تهران", Year = 1404, DurationHours = 24, HasCertificate = false });

        db.HrEmployeeSkills.AddRange(
            new HrEmployeeSkill { EmployeeId = e2, Title = "مدیریت تیم", Level = HrSkillLevel.Expert },
            new HrEmployeeSkill { EmployeeId = e2, Title = "C# و دات‌نت", Level = HrSkillLevel.Advanced },
            new HrEmployeeSkill { EmployeeId = e2, Title = "طراحی معماری نرم‌افزار", Level = HrSkillLevel.Advanced },
            new HrEmployeeSkill { EmployeeId = e3, Title = "Blazor", Level = HrSkillLevel.Intermediate });

        db.HrEmployeeLanguages.AddRange(
            new HrEmployeeLanguage { EmployeeId = e2, Language = "انگلیسی", Level = HrSkillLevel.Advanced },
            new HrEmployeeLanguage { EmployeeId = e2, Language = "عربی", Level = HrSkillLevel.Intermediate });

        db.HrEmployeeDocuments.AddRange(
            new HrEmployeeDocument
            {
                EmployeeId = e2, Title = "قرارداد کاری ۱۴۰۴", DocType = HrDocType.Contract,
                IssueDate = today.AddYears(-1), ExpiryDate = today.AddDays(10), Notes = "نزدیک به انقضا"
            },
            new HrEmployeeDocument
            {
                EmployeeId = e2, Title = "کارت ملی", DocType = HrDocType.NationalCard,
                IssueDate = today.AddYears(-5), ExpiryDate = null
            },
            new HrEmployeeDocument
            {
                EmployeeId = e2, Title = "مدرک کارشناسی", DocType = HrDocType.Degree,
                IssueDate = today.AddYears(-12), ExpiryDate = today.AddDays(-30), Notes = "منقضی شده"
            },
            new HrEmployeeDocument
            {
                EmployeeId = e3, Title = "گواهی دوره Blazor", DocType = HrDocType.Other,
                IssueDate = today.AddMonths(-2), ExpiryDate = today.AddDays(200)
            });
        db.SaveChanges();
    }
}
