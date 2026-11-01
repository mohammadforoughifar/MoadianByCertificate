using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// خودتعمیرِ اسکیمای «منابع انسانی اصلی — مدیریت پایه سازمانی» (HrMain):
/// HrMainCompanies + HrMainBranches + HrMainOrgNodes + HrMainPositions + HrMainLocales + HrMainRules
/// به‌علاوه‌ی دو ستون پیوند پرسنل به ساختار جدید (HrEmployees.HrMainNodeId / HrMainPositionId).
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند؛
/// مثل OrganizationSchemaV1 این اسکیما به‌صورت Ensure ساخته می‌شود نه EF Migration،
/// تا دیتابیس‌های موجود هم بدون مشکل ارتقا یابند.
/// </summary>
public static class HrMainSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.GetDbConnection() is SqliteConnection sqlConn)
                await EnsureSqliteAsync(sqlConn.ConnectionString);
            else if (db.Database.GetDbConnection() is SqlConnection sqlServerConn)
                await EnsureSqlServerAsync(sqlServerConn.ConnectionString);
        }
        catch (Exception ex)
        {
            // نباید استارتاپ را متوقف کند؛ در صورت خطا فقط هشدار می‌دهیم.
            Console.WriteLine($"[DB] HrMainSchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ================== SQLite ==================

    private static async Task EnsureSqliteAsync(string connectionString)
    {
        using var raw = new SqliteConnection(connectionString);
        raw.Open();

        void Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        bool HasColumn(string table, string column)
        {
            using var c = raw.CreateCommand();
            c.CommandText = $"PRAGMA table_info({table})";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrMainCompanies (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                LogoPath TEXT NULL,
                Address TEXT NULL,
                EconomicCode TEXT NULL,
                RegistrationNo TEXT NULL,
                NationalId TEXT NULL,
                Phone TEXT NULL,
                Email TEXT NULL,
                Website TEXT NULL,
                ManagerName TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NULL
            );");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrMainBranches (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                Type INTEGER NOT NULL DEFAULT 0,
                City TEXT NULL,
                Address TEXT NULL,
                Phone TEXT NULL,
                ManagerName TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrMainOrgNodes (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ParentId INTEGER NULL,
                Level INTEGER NOT NULL DEFAULT 1,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                BranchId INTEGER NULL,
                ManagerTitle TEXT NULL,
                Phone TEXT NULL,
                Description TEXT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_HrMainOrgNodes_ParentId ON HrMainOrgNodes (ParentId);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrMainPositions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Code TEXT NULL,
                Title TEXT NOT NULL,
                OrgNodeId INTEGER NULL,
                Grade TEXT NULL,
                JobDescription TEXT NULL,
                Requirements TEXT NULL,
                HeadCount INTEGER NOT NULL DEFAULT 1,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_HrMainPositions_OrgNodeId ON HrMainPositions (OrgNodeId);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrMainLocales (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Language TEXT NOT NULL DEFAULT 'fa',
                Calendar TEXT NOT NULL DEFAULT 'jalali',
                UpdatedAt TEXT NOT NULL
            );");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrMainRules (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                AnnualLeaveDays INTEGER NOT NULL DEFAULT 26,
                MaxConsecutiveLeaveDays INTEGER NOT NULL DEFAULT 9,
                MaxCarryOverDays INTEGER NOT NULL DEFAULT 9,
                UnpaidLeaveAllowed INTEGER NOT NULL DEFAULT 1,
                LateGraceMinutes INTEGER NOT NULL DEFAULT 10,
                MonthlyAllowedLateMinutes INTEGER NOT NULL DEFAULT 60,
                MaxLateWithoutLeaveMinutes INTEGER NOT NULL DEFAULT 120,
                LateDeductionFactor NUMERIC NOT NULL DEFAULT 1.0,
                AbsenceDailyDeductionFactor NUMERIC NOT NULL DEFAULT 1.0,
                UnexcusedAbsenceWarningAfter INTEGER NOT NULL DEFAULT 3,
                OvertimeFactor NUMERIC NOT NULL DEFAULT 1.4,
                MaxMonthlyOvertimeHours INTEGER NOT NULL DEFAULT 60,
                OvertimeNeedsApproval INTEGER NOT NULL DEFAULT 1,
                ContractAlertDays INTEGER NOT NULL DEFAULT 30,
                DefaultWorkStartTime TEXT NOT NULL DEFAULT '08:00:00',
                DefaultWorkEndTime TEXT NOT NULL DEFAULT '17:00:00',
                DefaultWeeklyOffDays TEXT NULL DEFAULT '5',
                UpdatedAt TEXT NOT NULL
            );");

            if (!HasColumn("HrMainRules", "ContractAlertDays"))
                Exec("ALTER TABLE HrMainRules ADD COLUMN ContractAlertDays INTEGER NOT NULL DEFAULT 30;");
            if (!HasColumn("HrMainRules", "DefaultWorkStartTime"))
                Exec("ALTER TABLE HrMainRules ADD COLUMN DefaultWorkStartTime TEXT NOT NULL DEFAULT '08:00:00';");
            if (!HasColumn("HrMainRules", "DefaultWorkEndTime"))
                Exec("ALTER TABLE HrMainRules ADD COLUMN DefaultWorkEndTime TEXT NOT NULL DEFAULT '17:00:00';");
            if (!HasColumn("HrMainRules", "DefaultWeeklyOffDays"))
                Exec("ALTER TABLE HrMainRules ADD COLUMN DefaultWeeklyOffDays TEXT NULL DEFAULT '5';");

        Exec(@"
            CREATE TABLE IF NOT EXISTS HrMainChangeLogs (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                At TEXT NOT NULL,
                Entity INTEGER NOT NULL,
                Action INTEGER NOT NULL,
                EntityId INTEGER NOT NULL,
                EntityName TEXT NOT NULL,
                FieldName TEXT NULL,
                OldValue TEXT NULL,
                NewValue TEXT NULL,
                ByUserId INTEGER NULL,
                ByUsername TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS IX_HrMainChangeLogs_At ON HrMainChangeLogs (At);
            CREATE INDEX IF NOT EXISTS IX_HrMainChangeLogs_Entity ON HrMainChangeLogs (Entity, EntityId);");

        // پیوند پرسنل به ساختار سازمانی جدید (دیتابیس‌های موجود این ستون‌ها را ندارند)
        if (!HasColumn("HrEmployees", "HrMainNodeId"))
            Exec("ALTER TABLE HrEmployees ADD COLUMN HrMainNodeId INTEGER NULL;");
        if (!HasColumn("HrEmployees", "HrMainPositionId"))
            Exec("ALTER TABLE HrEmployees ADD COLUMN HrMainPositionId INTEGER NULL;");

        await Task.CompletedTask;
    }

    // ================== SQL Server ==================

    private static async Task EnsureSqlServerAsync(string connectionString)
    {
        using var raw = new SqlConnection(connectionString);
        await raw.OpenAsync();

        async Task ExecAsync(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            await c.ExecuteNonQueryAsync();
        }

        // ---------- HrMainCompanies ----------
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrMainCompanies', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrMainCompanies] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [Name] nvarchar(200) NOT NULL,
                    [LogoPath] nvarchar(300) NULL,
                    [Address] nvarchar(400) NULL,
                    [EconomicCode] nvarchar(30) NULL,
                    [RegistrationNo] nvarchar(30) NULL,
                    [NationalId] nvarchar(30) NULL,
                    [Phone] nvarchar(50) NULL,
                    [Email] nvarchar(150) NULL,
                    [Website] nvarchar(150) NULL,
                    [ManagerName] nvarchar(150) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NULL
                );
            END");

        // ---------- HrMainBranches ----------
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrMainBranches', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrMainBranches] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [Code] nvarchar(20) NOT NULL,
                    [Name] nvarchar(150) NOT NULL,
                    [Type] int NOT NULL CONSTRAINT [DF_HrMainBranches_Type] DEFAULT(0),
                    [City] nvarchar(80) NULL,
                    [Address] nvarchar(400) NULL,
                    [Phone] nvarchar(50) NULL,
                    [ManagerName] nvarchar(150) NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_HrMainBranches_IsActive] DEFAULT(1),
                    [SortOrder] int NOT NULL CONSTRAINT [DF_HrMainBranches_SortOrder] DEFAULT(0),
                    [CreatedAt] datetime2 NOT NULL
                );
            END");

        // ---------- HrMainOrgNodes ----------
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrMainOrgNodes', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrMainOrgNodes] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [ParentId] int NULL,
                    [Level] int NOT NULL CONSTRAINT [DF_HrMainOrgNodes_Level] DEFAULT(1),
                    [Code] nvarchar(20) NOT NULL,
                    [Name] nvarchar(150) NOT NULL,
                    [BranchId] int NULL,
                    [ManagerTitle] nvarchar(150) NULL,
                    [Phone] nvarchar(50) NULL,
                    [Description] nvarchar(500) NULL,
                    [SortOrder] int NOT NULL CONSTRAINT [DF_HrMainOrgNodes_SortOrder] DEFAULT(0),
                    [IsActive] bit NOT NULL CONSTRAINT [DF_HrMainOrgNodes_IsActive] DEFAULT(1),
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HrMainOrgNodes_ParentId' AND object_id = OBJECT_ID(N'dbo.HrMainOrgNodes'))
                CREATE INDEX [IX_HrMainOrgNodes_ParentId] ON [HrMainOrgNodes] ([ParentId]);");

        // ---------- HrMainPositions ----------
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrMainPositions', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrMainPositions] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [Code] nvarchar(20) NULL,
                    [Title] nvarchar(150) NOT NULL,
                    [OrgNodeId] int NULL,
                    [Grade] nvarchar(50) NULL,
                    [JobDescription] nvarchar(max) NULL,
                    [Requirements] nvarchar(max) NULL,
                    [HeadCount] int NOT NULL CONSTRAINT [DF_HrMainPositions_HeadCount] DEFAULT(1),
                    [IsActive] bit NOT NULL CONSTRAINT [DF_HrMainPositions_IsActive] DEFAULT(1),
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HrMainPositions_OrgNodeId' AND object_id = OBJECT_ID(N'dbo.HrMainPositions'))
                CREATE INDEX [IX_HrMainPositions_OrgNodeId] ON [HrMainPositions] ([OrgNodeId]);");

        // ---------- HrMainLocales ----------
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrMainLocales', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrMainLocales] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [Language] nvarchar(10) NOT NULL CONSTRAINT [DF_HrMainLocales_Language] DEFAULT(N'fa'),
                    [Calendar] nvarchar(10) NOT NULL CONSTRAINT [DF_HrMainLocales_Calendar] DEFAULT(N'jalali'),
                    [UpdatedAt] datetime2 NOT NULL
                );
            END");

        // ---------- HrMainRules ----------
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrMainRules', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrMainRules] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [AnnualLeaveDays] int NOT NULL CONSTRAINT [DF_HrMainRules_AnnualLeaveDays] DEFAULT(26),
                    [MaxConsecutiveLeaveDays] int NOT NULL CONSTRAINT [DF_HrMainRules_MaxConsecutiveLeaveDays] DEFAULT(9),
                    [MaxCarryOverDays] int NOT NULL CONSTRAINT [DF_HrMainRules_MaxCarryOverDays] DEFAULT(9),
                    [UnpaidLeaveAllowed] bit NOT NULL CONSTRAINT [DF_HrMainRules_UnpaidLeaveAllowed] DEFAULT(1),
                    [LateGraceMinutes] int NOT NULL CONSTRAINT [DF_HrMainRules_LateGraceMinutes] DEFAULT(10),
                    [MonthlyAllowedLateMinutes] int NOT NULL CONSTRAINT [DF_HrMainRules_MonthlyAllowedLateMinutes] DEFAULT(60),
                    [MaxLateWithoutLeaveMinutes] int NOT NULL CONSTRAINT [DF_HrMainRules_MaxLateWithoutLeaveMinutes] DEFAULT(120),
                    [LateDeductionFactor] decimal(18,2) NOT NULL CONSTRAINT [DF_HrMainRules_LateDeductionFactor] DEFAULT(1.0),
                    [AbsenceDailyDeductionFactor] decimal(18,2) NOT NULL CONSTRAINT [DF_HrMainRules_AbsenceDailyDeductionFactor] DEFAULT(1.0),
                    [UnexcusedAbsenceWarningAfter] int NOT NULL CONSTRAINT [DF_HrMainRules_UnexcusedAbsenceWarningAfter] DEFAULT(3),
                    [OvertimeFactor] decimal(18,2) NOT NULL CONSTRAINT [DF_HrMainRules_OvertimeFactor] DEFAULT(1.4),
                    [MaxMonthlyOvertimeHours] int NOT NULL CONSTRAINT [DF_HrMainRules_MaxMonthlyOvertimeHours] DEFAULT(60),
                    [OvertimeNeedsApproval] bit NOT NULL CONSTRAINT [DF_HrMainRules_OvertimeNeedsApproval] DEFAULT(1),
                    [ContractAlertDays] int NOT NULL CONSTRAINT [DF_HrMainRules_ContractAlertDays] DEFAULT(30),
                    [DefaultWorkStartTime] time NOT NULL CONSTRAINT [DF_HrMainRules_DefaultWorkStartTime] DEFAULT('08:00:00'),
                    [DefaultWorkEndTime] time NOT NULL CONSTRAINT [DF_HrMainRules_DefaultWorkEndTime] DEFAULT('17:00:00'),
                    [DefaultWeeklyOffDays] nvarchar(20) NULL CONSTRAINT [DF_HrMainRules_DefaultWeeklyOffDays] DEFAULT(N'5'),
                    [UpdatedAt] datetime2 NOT NULL
                );
            END");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrMainRules', N'U') IS NOT NULL
            AND COL_LENGTH(N'dbo.HrMainRules', N'ContractAlertDays') IS NULL
                ALTER TABLE dbo.HrMainRules ADD [ContractAlertDays] int NOT NULL CONSTRAINT [DF_HrMainRules_ContractAlertDays] DEFAULT(30);");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrMainRules', N'U') IS NOT NULL
            AND COL_LENGTH(N'dbo.HrMainRules', N'DefaultWorkStartTime') IS NULL
                ALTER TABLE dbo.HrMainRules ADD [DefaultWorkStartTime] time NOT NULL CONSTRAINT [DF_HrMainRules_DefaultWorkStartTime] DEFAULT('08:00:00');
            IF OBJECT_ID(N'dbo.HrMainRules', N'U') IS NOT NULL
            AND COL_LENGTH(N'dbo.HrMainRules', N'DefaultWorkEndTime') IS NULL
                ALTER TABLE dbo.HrMainRules ADD [DefaultWorkEndTime] time NOT NULL CONSTRAINT [DF_HrMainRules_DefaultWorkEndTime] DEFAULT('17:00:00');
            IF OBJECT_ID(N'dbo.HrMainRules', N'U') IS NOT NULL
            AND COL_LENGTH(N'dbo.HrMainRules', N'DefaultWeeklyOffDays') IS NULL
                ALTER TABLE dbo.HrMainRules ADD [DefaultWeeklyOffDays] nvarchar(20) NULL CONSTRAINT [DF_HrMainRules_DefaultWeeklyOffDays] DEFAULT(N'5');");

        // ---------- HrMainChangeLogs ----------
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrMainChangeLogs', N'U') IS NULL
            BEGIN
                CREATE TABLE [HrMainChangeLogs] (
                    [Id] bigint NOT NULL IDENTITY PRIMARY KEY,
                    [At] datetime2 NOT NULL,
                    [Entity] int NOT NULL,
                    [Action] int NOT NULL,
                    [EntityId] int NOT NULL,
                    [EntityName] nvarchar(150) NOT NULL,
                    [FieldName] nvarchar(60) NULL,
                    [OldValue] nvarchar(300) NULL,
                    [NewValue] nvarchar(300) NULL,
                    [ByUserId] int NULL,
                    [ByUsername] nvarchar(100) NOT NULL CONSTRAINT [DF_HrMainChangeLogs_ByUsername] DEFAULT('')
                );
                CREATE INDEX [IX_HrMainChangeLogs_At] ON [HrMainChangeLogs] ([At]);
                CREATE INDEX [IX_HrMainChangeLogs_Entity] ON [HrMainChangeLogs] ([Entity], [EntityId]);
            END");

        // پیوند پرسنل به ساختار سازمانی جدید (دیتابیس‌های موجود این ستون‌ها را ندارند)
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.HrEmployees', N'U') IS NOT NULL
            AND COL_LENGTH(N'dbo.HrEmployees', N'HrMainNodeId') IS NULL
                ALTER TABLE dbo.HrEmployees ADD HrMainNodeId int NULL;
            IF OBJECT_ID(N'dbo.HrEmployees', N'U') IS NOT NULL
            AND COL_LENGTH(N'dbo.HrEmployees', N'HrMainPositionId') IS NULL
                ALTER TABLE dbo.HrEmployees ADD HrMainPositionId int NULL;");
    }
}
