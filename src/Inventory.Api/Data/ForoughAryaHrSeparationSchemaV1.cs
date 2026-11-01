using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ================== تفکیک کامل «منابع انسانی فروغ آریا» از «منابع انسانی اصلی» — نسخه ۱ ==================
/// <para>این دو ماژول مجزا هستند و نباید هیچ جدول مشترکی داشته باشند. پیش از این نسخه چهار نقطهٔ مشترک وجود داشت:</para>
/// <list type="table">
///   <item><b>HrEmployees</b> (مال منابع انسانی اصلی) ← حقوق/ارزیابی/دورهٔ آزمایشیِ فروغ آریا از آن می‌خواندند.
///         اکنون فروغ آریا جدول اختصاصی <c>FaHrPersonnel</c> دارد (کپی یک‌باره با همان شناسه‌ها تا ارجاع‌های قبلی سالم بمانند).</item>
///   <item><b>CompanyHolidays</b> (مال فروغ آریا) ← منابع انسانی اصلی (HrMain و FaAtt) هم از آن می‌خواند و می‌نوشت.
///         اکنون منابع انسانی اصلی جدول اختصاصی <c>HrMainHolidays</c> دارد.</item>
///   <item><b>HrMinWages</b> (مال فروغ آریا) ← حقوق منابع انسانی اصلی (FaPay) از آن می‌خواند.
///         اکنون FaPay جدول اختصاصی <c>FaPayMinWages</c> دارد.</item>
///   <item><b>HrPerf*</b> (مال فروغ آریا) ← نیازسنجی آموزش (FaLms) و پاداش حقوق (FaPay) منابع انسانی اصلی از آن می‌خواندند.
///         اکنون FaLms از ارزیابی عملکرد خودِ منابع انسانی اصلی (HrAppraisals) استفاده می‌کند؛ ارجاع‌های قدیمیِ
///         نیازها به دوره/شاخص فروغ آریا (که دیگر معنا ندارند) یک‌بار پاک می‌شوند (نمرهٔ ثبت‌شده حفظ می‌شود).</item>
/// </list>
/// <para>همهٔ دستورها idempotent هستند؛ مراحل کپی فقط یک بار اجرا می‌شوند (جدول FaHrSchemaMarks).</para>
/// </summary>
public static class ForoughAryaHrSeparationSchemaV1
{
    public const string StepPersonnel = "personnel-from-hremployees-v1";
    public const string StepHolidays = "holidays-to-hrmain-v1";
    public const string StepMinWages = "minwages-to-fapay-v1";
    public const string StepLmsNeeds = "falms-needs-perf-unlink-v1";

    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn is SqliteConnection sqlite)
                await EnsureSqliteAsync(sqlite.ConnectionString);
            else if (conn is SqlConnection sqlServer)
                await EnsureSqlServerAsync(sqlServer.ConnectionString);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ForoughAryaHrSeparationSchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ============================== SQL Server ==============================

    private static async Task EnsureSqlServerAsync(string cs)
    {
        await using var raw = new SqlConnection(cs);
        await raw.OpenAsync();

        async Task<bool> Exec(string label, string sql)
        {
            try
            {
                await using var c = raw.CreateCommand();
                c.CommandText = sql;
                c.CommandTimeout = 300;
                await c.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex) { Console.WriteLine($"[DB] FaHrSeparation: {label} → {ex.Message}"); return false; }
        }
        async Task<bool> Scalar(string sql)
        {
            await using var c = raw.CreateCommand();
            c.CommandText = sql;
            var v = await c.ExecuteScalarAsync();
            return v != null && v != DBNull.Value && Convert.ToInt32(v) != 0;
        }
        Task<bool> TableExists(string t) => Scalar($"SELECT CASE WHEN OBJECT_ID(N'dbo.{t}', N'U') IS NULL THEN 0 ELSE 1 END");
        Task<bool> ColExists(string t, string c) => Scalar($"SELECT CASE WHEN COL_LENGTH(N'dbo.{t}', N'{c}') IS NULL THEN 0 ELSE 1 END");
        Task<bool> Done(string step) => Scalar($"SELECT COUNT(1) FROM dbo.FaHrSchemaMarks WHERE Step = N'{step}'");
        Task<bool> Mark(string step) => Exec("mark " + step,
            $"IF NOT EXISTS (SELECT 1 FROM dbo.FaHrSchemaMarks WHERE Step = N'{step}') INSERT INTO dbo.FaHrSchemaMarks (Step, DoneAt) VALUES (N'{step}', SYSDATETIME());");

        // ---------- جدول‌ها ----------
        await Exec("FaHrSchemaMarks", @"
IF OBJECT_ID(N'dbo.FaHrSchemaMarks', N'U') IS NULL
CREATE TABLE dbo.FaHrSchemaMarks (
    Step nvarchar(80) NOT NULL CONSTRAINT PK_FaHrSchemaMarks PRIMARY KEY,
    DoneAt datetime2 NOT NULL
);");
        await Exec("FaHrPersonnel", @"
IF OBJECT_ID(N'dbo.FaHrPersonnel', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FaHrPersonnel (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FaHrPersonnel PRIMARY KEY,
        Code nvarchar(20) NOT NULL,
        FirstName nvarchar(80) NOT NULL,
        LastName nvarchar(80) NOT NULL,
        NationalCode nvarchar(10) NOT NULL,
        BirthDate datetime2 NULL,
        Gender int NOT NULL CONSTRAINT DF_FaHrPersonnel_Gender DEFAULT 0,
        MaritalStatus int NOT NULL CONSTRAINT DF_FaHrPersonnel_Marital DEFAULT 0,
        Mobile nvarchar(20) NULL,
        Email nvarchar(150) NULL,
        Address nvarchar(300) NULL,
        HireDate datetime2 NOT NULL,
        UnitTitle nvarchar(150) NULL,
        PostTitle nvarchar(150) NULL,
        ManagerId int NULL,
        EmploymentType int NOT NULL CONSTRAINT DF_FaHrPersonnel_EmpType DEFAULT 1,
        Status int NOT NULL CONSTRAINT DF_FaHrPersonnel_Status DEFAULT 0,
        BaseSalary decimal(18,2) NOT NULL CONSTRAINT DF_FaHrPersonnel_Base DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_FaHrPersonnel_Active DEFAULT 1,
        Sheba nvarchar(29) NULL,
        BankName nvarchar(60) NULL,
        InsuranceNo nvarchar(12) NULL,
        CreatedAt datetime2 NOT NULL,
        UpdatedAt datetime2 NULL
    );
    CREATE INDEX IX_FaHrPersonnel_Code ON dbo.FaHrPersonnel (Code);
END");
        await Exec("HrMainHolidays", @"
IF OBJECT_ID(N'dbo.HrMainHolidays', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrMainHolidays (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrMainHolidays PRIMARY KEY,
        HolidayDate datetime2 NOT NULL,
        Name nvarchar(100) NOT NULL,
        IsOfficial bit NOT NULL CONSTRAINT DF_HrMainHolidays_IsOfficial DEFAULT 0,
        CreatedByName nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL
    );
    CREATE INDEX IX_HrMainHolidays_HolidayDate ON dbo.HrMainHolidays (HolidayDate);
END");
        await Exec("FaPayMinWages", @"
IF OBJECT_ID(N'dbo.FaPayMinWages', N'U') IS NULL
CREATE TABLE dbo.FaPayMinWages (
    Year int NOT NULL CONSTRAINT PK_FaPayMinWages PRIMARY KEY,
    MonthlyWage decimal(18,2) NOT NULL,
    DailyWage decimal(18,2) NOT NULL,
    EffectiveDate datetime2 NOT NULL,
    Note nvarchar(300) NULL,
    UpdatedAt datetime2 NOT NULL
);");

        // ---------- ۱) پرسنل: HrEmployees → FaHrPersonnel (با همان شناسه‌ها) ----------
        if (!await Done(StepPersonnel))
        {
            var ok = true;
            if (await TableExists("HrEmployees"))
            {
                async Task<string> Opt(string col) => await ColExists("HrEmployees", col) ? "e." + col : "NULL";
                var unit = await TableExists("HrOrgUnits")
                    ? "(SELECT TOP 1 u.Name FROM dbo.HrOrgUnits u WHERE u.Id = e.OrgUnitId)"
                    : "NULL";
                var sql = $@"
SET IDENTITY_INSERT dbo.FaHrPersonnel ON;
INSERT INTO dbo.FaHrPersonnel (Id, Code, FirstName, LastName, NationalCode, BirthDate, Gender, MaritalStatus,
    Mobile, Email, Address, HireDate, UnitTitle, PostTitle, ManagerId, EmploymentType, Status, BaseSalary,
    IsActive, Sheba, BankName, InsuranceNo, CreatedAt, UpdatedAt)
SELECT e.Id, LEFT(ISNULL(e.Code, N''), 20), LEFT(ISNULL(e.FirstName, N''), 80), LEFT(ISNULL(e.LastName, N''), 80),
    LEFT(ISNULL(e.NationalCode, N''), 10), e.BirthDate, e.Gender, e.MaritalStatus,
    e.Mobile, e.Email, e.Address, e.HireDate, LEFT({unit}, 150), e.PostTitle, e.ManagerId, e.EmploymentType, e.Status,
    e.BaseSalary, e.IsActive, {await Opt("Sheba")}, {await Opt("BankName")}, {await Opt("InsuranceNo")}, e.CreatedAt, e.UpdatedAt
FROM dbo.HrEmployees e
WHERE NOT EXISTS (SELECT 1 FROM dbo.FaHrPersonnel f WHERE f.Id = e.Id);
SET IDENTITY_INSERT dbo.FaHrPersonnel OFF;";
                ok = await Exec("copy personnel", sql);
            }
            if (ok)
            {
                await Mark(StepPersonnel);
                Console.WriteLine("[DB] ✔ پرسنل «منابع انسانی فروغ آریا» به جدول اختصاصی FaHrPersonnel منتقل شد.");
            }
        }

        // ---------- ۲) تعطیلات: CompanyHolidays → HrMainHolidays ----------
        if (!await Done(StepHolidays))
        {
            var ok = true;
            if (await TableExists("CompanyHolidays"))
            {
                var isOff = await ColExists("CompanyHolidays", "IsOfficial") ? "h.IsOfficial" : "0";
                var by = await ColExists("CompanyHolidays", "CreatedByName") ? "h.CreatedByName" : "NULL";
                ok = await Exec("copy holidays", $@"
INSERT INTO dbo.HrMainHolidays (HolidayDate, Name, IsOfficial, CreatedByName, CreatedAt)
SELECT h.HolidayDate, LEFT(ISNULL(h.Name, N''), 100), {isOff}, {by}, h.CreatedAt
FROM dbo.CompanyHolidays h
WHERE NOT EXISTS (SELECT 1 FROM dbo.HrMainHolidays m WHERE m.HolidayDate = h.HolidayDate);");
            }
            if (ok) await Mark(StepHolidays);
        }

        // ---------- ۳) حداقل دستمزد: HrMinWages → FaPayMinWages ----------
        if (!await Done(StepMinWages))
        {
            var ok = true;
            if (await TableExists("HrMinWages"))
                ok = await Exec("copy min wages", @"
INSERT INTO dbo.FaPayMinWages (Year, MonthlyWage, DailyWage, EffectiveDate, Note, UpdatedAt)
SELECT w.Year, w.MonthlyWage, w.DailyWage, w.EffectiveDate, w.Note, w.UpdatedAt
FROM dbo.HrMinWages w
WHERE NOT EXISTS (SELECT 1 FROM dbo.FaPayMinWages f WHERE f.Year = w.Year);");
            if (ok) await Mark(StepMinWages);
        }

        // ---------- ۴) نیازسنجی آموزش: قطع ارجاع به ارزیابی فروغ آریا ----------
        if (!await Done(StepLmsNeeds))
        {
            var ok = true;
            if (await ColExists("FaLmsNeeds", "PerfPeriodId") && await ColExists("FaLmsNeeds", "PerfKpiId"))
                ok = await Exec("unlink lms needs", "UPDATE dbo.FaLmsNeeds SET PerfPeriodId = NULL, PerfKpiId = NULL WHERE PerfPeriodId IS NOT NULL OR PerfKpiId IS NOT NULL;");
            if (ok) await Mark(StepLmsNeeds);
        }
    }

    // ============================== SQLite ==============================

    private static async Task EnsureSqliteAsync(string cs)
    {
        await using var raw = new SqliteConnection(cs);
        await raw.OpenAsync();

        async Task<bool> Exec(string label, string sql)
        {
            try
            {
                await using var c = raw.CreateCommand();
                c.CommandText = sql;
                await c.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex) { Console.WriteLine($"[DB] FaHrSeparation (SQLite): {label} → {ex.Message}"); return false; }
        }
        async Task<long> Scalar(string sql)
        {
            await using var c = raw.CreateCommand();
            c.CommandText = sql;
            var v = await c.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? 0 : Convert.ToInt64(v);
        }
        async Task<bool> TableExists(string t) => await Scalar($"SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='{t}'") > 0;
        async Task<bool> ColExists(string t, string col) => await Scalar($"SELECT COUNT(1) FROM pragma_table_info('{t}') WHERE name='{col}'") > 0;
        async Task<bool> Done(string step) => await Scalar($"SELECT COUNT(1) FROM FaHrSchemaMarks WHERE Step = '{step}'") > 0;
        Task<bool> Mark(string step) => Exec("mark " + step,
            $"INSERT OR IGNORE INTO FaHrSchemaMarks (Step, DoneAt) VALUES ('{step}', datetime('now','localtime'));");

        await Exec("FaHrSchemaMarks", @"
CREATE TABLE IF NOT EXISTS FaHrSchemaMarks (
    Step TEXT NOT NULL PRIMARY KEY,
    DoneAt TEXT NOT NULL
);");
        await Exec("FaHrPersonnel", @"
CREATE TABLE IF NOT EXISTS FaHrPersonnel (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    FirstName TEXT NOT NULL,
    LastName TEXT NOT NULL,
    NationalCode TEXT NOT NULL,
    BirthDate TEXT NULL,
    Gender INTEGER NOT NULL DEFAULT 0,
    MaritalStatus INTEGER NOT NULL DEFAULT 0,
    Mobile TEXT NULL,
    Email TEXT NULL,
    Address TEXT NULL,
    HireDate TEXT NOT NULL,
    UnitTitle TEXT NULL,
    PostTitle TEXT NULL,
    ManagerId INTEGER NULL,
    EmploymentType INTEGER NOT NULL DEFAULT 1,
    Status INTEGER NOT NULL DEFAULT 0,
    BaseSalary TEXT NOT NULL DEFAULT '0',
    IsActive INTEGER NOT NULL DEFAULT 1,
    Sheba TEXT NULL,
    BankName TEXT NULL,
    InsuranceNo TEXT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NULL
);");
        await Exec("IX_FaHrPersonnel_Code", "CREATE INDEX IF NOT EXISTS IX_FaHrPersonnel_Code ON FaHrPersonnel (Code);");
        await Exec("HrMainHolidays", @"
CREATE TABLE IF NOT EXISTS HrMainHolidays (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    HolidayDate TEXT NOT NULL,
    Name TEXT NOT NULL,
    IsOfficial INTEGER NOT NULL DEFAULT 0,
    CreatedByName TEXT NULL,
    CreatedAt TEXT NOT NULL
);");
        await Exec("IX_HrMainHolidays_HolidayDate", "CREATE INDEX IF NOT EXISTS IX_HrMainHolidays_HolidayDate ON HrMainHolidays (HolidayDate);");
        await Exec("FaPayMinWages", @"
CREATE TABLE IF NOT EXISTS FaPayMinWages (
    Year INTEGER NOT NULL PRIMARY KEY,
    MonthlyWage TEXT NOT NULL,
    DailyWage TEXT NOT NULL,
    EffectiveDate TEXT NOT NULL,
    Note TEXT NULL,
    UpdatedAt TEXT NOT NULL
);");

        if (!await Done(StepPersonnel))
        {
            var ok = true;
            if (await TableExists("HrEmployees"))
            {
                async Task<string> Opt(string col) => await ColExists("HrEmployees", col) ? "e." + col : "NULL";
                var unit = await TableExists("HrOrgUnits")
                    ? "(SELECT u.Name FROM HrOrgUnits u WHERE u.Id = e.OrgUnitId LIMIT 1)"
                    : "NULL";
                ok = await Exec("copy personnel", $@"
INSERT OR IGNORE INTO FaHrPersonnel (Id, Code, FirstName, LastName, NationalCode, BirthDate, Gender, MaritalStatus,
    Mobile, Email, Address, HireDate, UnitTitle, PostTitle, ManagerId, EmploymentType, Status, BaseSalary,
    IsActive, Sheba, BankName, InsuranceNo, CreatedAt, UpdatedAt)
SELECT e.Id, IFNULL(e.Code, ''), IFNULL(e.FirstName, ''), IFNULL(e.LastName, ''), IFNULL(e.NationalCode, ''),
    e.BirthDate, e.Gender, e.MaritalStatus, e.Mobile, e.Email, e.Address, e.HireDate, {unit}, e.PostTitle,
    e.ManagerId, e.EmploymentType, e.Status, e.BaseSalary, e.IsActive,
    {await Opt("Sheba")}, {await Opt("BankName")}, {await Opt("InsuranceNo")}, e.CreatedAt, e.UpdatedAt
FROM HrEmployees e;");
            }
            if (ok)
            {
                await Mark(StepPersonnel);
                Console.WriteLine("[DB] ✔ پرسنل «منابع انسانی فروغ آریا» به جدول اختصاصی FaHrPersonnel منتقل شد.");
            }
        }

        if (!await Done(StepHolidays))
        {
            var ok = true;
            if (await TableExists("CompanyHolidays"))
            {
                var isOff = await ColExists("CompanyHolidays", "IsOfficial") ? "h.IsOfficial" : "0";
                var by = await ColExists("CompanyHolidays", "CreatedByName") ? "h.CreatedByName" : "NULL";
                ok = await Exec("copy holidays", $@"
INSERT INTO HrMainHolidays (HolidayDate, Name, IsOfficial, CreatedByName, CreatedAt)
SELECT h.HolidayDate, IFNULL(h.Name, ''), {isOff}, {by}, h.CreatedAt
FROM CompanyHolidays h
WHERE NOT EXISTS (SELECT 1 FROM HrMainHolidays m WHERE m.HolidayDate = h.HolidayDate);");
            }
            if (ok) await Mark(StepHolidays);
        }

        if (!await Done(StepMinWages))
        {
            var ok = true;
            if (await TableExists("HrMinWages"))
                ok = await Exec("copy min wages", @"
INSERT OR IGNORE INTO FaPayMinWages (Year, MonthlyWage, DailyWage, EffectiveDate, Note, UpdatedAt)
SELECT w.Year, w.MonthlyWage, w.DailyWage, w.EffectiveDate, w.Note, w.UpdatedAt FROM HrMinWages w;");
            if (ok) await Mark(StepMinWages);
        }

        if (!await Done(StepLmsNeeds))
        {
            var ok = true;
            if (await ColExists("FaLmsNeeds", "PerfPeriodId") && await ColExists("FaLmsNeeds", "PerfKpiId"))
                ok = await Exec("unlink lms needs", "UPDATE FaLmsNeeds SET PerfPeriodId = NULL, PerfKpiId = NULL WHERE PerfPeriodId IS NOT NULL OR PerfKpiId IS NOT NULL;");
            if (ok) await Mark(StepLmsNeeds);
        }
    }
}
