using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// خودتعمیرِ اسکیمای «حضور و غیاب فروغ آریا» (FaAtt):
/// FaAttShifts + FaAttShiftAssigns + FaAttDevices + FaAttLogs +
/// FaAttDailies + FaAttMissions + FaAttLeaveTypes + FaAttLeaves.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaAttSchemaV1
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
            Console.WriteLine($"[DB] FaAttSchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
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
            CREATE TABLE IF NOT EXISTS FaAttShifts (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                Type INTEGER NOT NULL DEFAULT 0,
                StartTime TEXT NOT NULL,
                EndTime TEXT NOT NULL,
                LateToleranceMin INTEGER NOT NULL DEFAULT 0,
                EarlyToleranceMin INTEGER NOT NULL DEFAULT 0,
                OvertimeGraceMin INTEGER NOT NULL DEFAULT 10,
                RequiredMinutes INTEGER NOT NULL DEFAULT 480,
                OffDays TEXT NULL,
                Color TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaAttShiftAssigns (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                ShiftId INTEGER NOT NULL,
                FromDate TEXT NOT NULL,
                ToDate TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaAttShiftAssigns_EmployeeId ON FaAttShiftAssigns (EmployeeId);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaAttDevices (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                Type INTEGER NOT NULL DEFAULT 0,
                Location TEXT NULL,
                SerialNo TEXT NULL,
                IpAddress TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                LastSyncAt TEXT NULL,
                CreatedAt TEXT NOT NULL
            );");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaAttLogs (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Timestamp TEXT NOT NULL,
                Type INTEGER NOT NULL DEFAULT 2,
                Source INTEGER NOT NULL DEFAULT 2,
                DeviceId INTEGER NULL,
                Latitude REAL NULL,
                Longitude REAL NULL,
                Note TEXT NULL,
                CreatedByUserId INTEGER NULL,
                CreatedByName TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaAttLogs_EmployeeId ON FaAttLogs (EmployeeId);
            CREATE INDEX IF NOT EXISTS IX_FaAttLogs_Timestamp ON FaAttLogs (Timestamp);");

        // دیتابیس‌های قدیمی‌ترِ FaAtt ستون‌های مکان (GPS) را ندارند — خودتعمیر:
        if (!HasColumn("FaAttLogs", "Latitude"))
            Exec("ALTER TABLE FaAttLogs ADD COLUMN Latitude REAL NULL;");
        if (!HasColumn("FaAttLogs", "Longitude"))
            Exec("ALTER TABLE FaAttLogs ADD COLUMN Longitude REAL NULL;");


        Exec(@"
            CREATE TABLE IF NOT EXISTS FaAttDailies (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Date TEXT NOT NULL,
                ShiftId INTEGER NULL,
                FirstIn TEXT NULL,
                LastOut TEXT NULL,
                WorkMinutes INTEGER NOT NULL DEFAULT 0,
                LateMinutes INTEGER NOT NULL DEFAULT 0,
                EarlyMinutes INTEGER NOT NULL DEFAULT 0,
                OvertimeMinutes INTEGER NOT NULL DEFAULT 0,
                Status INTEGER NOT NULL DEFAULT 4,
                IsIncomplete INTEGER NOT NULL DEFAULT 0,
                Note TEXT NULL,
                CalculatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS UX_FaAttDailies_Employee_Date ON FaAttDailies (EmployeeId, Date);
            CREATE INDEX IF NOT EXISTS IX_FaAttDailies_Date ON FaAttDailies (Date);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaAttMissions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                FromDate TEXT NOT NULL,
                ToDate TEXT NOT NULL,
                Destination TEXT NULL,
                Reason TEXT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                DecidedByUserId INTEGER NULL,
                DecidedByName TEXT NULL,
                DecidedAt TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaAttMissions_EmployeeId ON FaAttMissions (EmployeeId);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaAttLeaveTypes (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                AnnualLimitDays INTEGER NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );");

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaAttLeaves (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                LeaveTypeId INTEGER NOT NULL,
                FromDate TEXT NOT NULL,
                ToDate TEXT NOT NULL,
                HoursPerDay REAL NULL,
                Reason TEXT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                DecidedByUserId INTEGER NULL,
                DecidedByName TEXT NULL,
                DecidedAt TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaAttLeaves_EmployeeId ON FaAttLeaves (EmployeeId);");

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

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.FaAttShifts', N'U') IS NULL
            BEGIN
                CREATE TABLE [FaAttShifts] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [Code] nvarchar(20) NOT NULL,
                    [Name] nvarchar(100) NOT NULL,
                    [Type] int NOT NULL CONSTRAINT [DF_FaAttShifts_Type] DEFAULT(0),
                    [StartTime] time NOT NULL,
                    [EndTime] time NOT NULL,
                    [LateToleranceMin] int NOT NULL CONSTRAINT [DF_FaAttShifts_LateTol] DEFAULT(0),
                    [EarlyToleranceMin] int NOT NULL CONSTRAINT [DF_FaAttShifts_EarlyTol] DEFAULT(0),
                    [OvertimeGraceMin] int NOT NULL CONSTRAINT [DF_FaAttShifts_OtGrace] DEFAULT(10),
                    [RequiredMinutes] int NOT NULL CONSTRAINT [DF_FaAttShifts_Required] DEFAULT(480),
                    [OffDays] nvarchar(20) NULL,
                    [Color] nvarchar(20) NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_FaAttShifts_IsActive] DEFAULT(1),
                    [SortOrder] int NOT NULL CONSTRAINT [DF_FaAttShifts_Sort] DEFAULT(0),
                    [CreatedAt] datetime2 NOT NULL
                );
            END");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.FaAttShiftAssigns', N'U') IS NULL
            BEGIN
                CREATE TABLE [FaAttShiftAssigns] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [EmployeeId] int NOT NULL,
                    [ShiftId] int NOT NULL,
                    [FromDate] datetime2 NOT NULL,
                    [ToDate] datetime2 NULL,
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FaAttShiftAssigns_EmployeeId' AND object_id = OBJECT_ID(N'dbo.FaAttShiftAssigns'))
                CREATE INDEX [IX_FaAttShiftAssigns_EmployeeId] ON [FaAttShiftAssigns] ([EmployeeId]);");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.FaAttDevices', N'U') IS NULL
            BEGIN
                CREATE TABLE [FaAttDevices] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [Code] nvarchar(20) NOT NULL,
                    [Name] nvarchar(100) NOT NULL,
                    [Type] int NOT NULL CONSTRAINT [DF_FaAttDevices_Type] DEFAULT(0),
                    [Location] nvarchar(150) NULL,
                    [SerialNo] nvarchar(50) NULL,
                    [IpAddress] nvarchar(50) NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_FaAttDevices_IsActive] DEFAULT(1),
                    [LastSyncAt] datetime2 NULL,
                    [CreatedAt] datetime2 NOT NULL
                );
            END");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.FaAttLogs', N'U') IS NULL
            BEGIN
                CREATE TABLE [FaAttLogs] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [EmployeeId] int NOT NULL,
                    [Timestamp] datetime2 NOT NULL,
                    [Type] int NOT NULL CONSTRAINT [DF_FaAttLogs_Type] DEFAULT(2),
                    [Source] int NOT NULL CONSTRAINT [DF_FaAttLogs_Source] DEFAULT(2),
                    [DeviceId] int NULL,
                    [Latitude] float NULL,
                    [Longitude] float NULL,
                    [Note] nvarchar(300) NULL,
                    [CreatedByUserId] int NULL,
                    [CreatedByName] nvarchar(150) NULL,
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FaAttLogs_EmployeeId' AND object_id = OBJECT_ID(N'dbo.FaAttLogs'))
                CREATE INDEX [IX_FaAttLogs_EmployeeId] ON [FaAttLogs] ([EmployeeId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FaAttLogs_Timestamp' AND object_id = OBJECT_ID(N'dbo.FaAttLogs'))
                CREATE INDEX [IX_FaAttLogs_Timestamp] ON [FaAttLogs] ([Timestamp]);");

        // دیتابیس‌های قدیمی‌ترِ FaAtt ستون‌های مکان (GPS) را ندارند — خودتعمیر:
        await ExecAsync(@"
IF OBJECT_ID(N'dbo.FaAttLogs', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.FaAttLogs', 'Latitude') IS NULL
    ALTER TABLE [FaAttLogs] ADD [Latitude] float NULL;
IF OBJECT_ID(N'dbo.FaAttLogs', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.FaAttLogs', 'Longitude') IS NULL
    ALTER TABLE [FaAttLogs] ADD [Longitude] float NULL;");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.FaAttDailies', N'U') IS NULL
            BEGIN
                CREATE TABLE [FaAttDailies] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [EmployeeId] int NOT NULL,
                    [Date] datetime2 NOT NULL,
                    [ShiftId] int NULL,
                    [FirstIn] datetime2 NULL,
                    [LastOut] datetime2 NULL,
                    [WorkMinutes] int NOT NULL CONSTRAINT [DF_FaAttDailies_Work] DEFAULT(0),
                    [LateMinutes] int NOT NULL CONSTRAINT [DF_FaAttDailies_Late] DEFAULT(0),
                    [EarlyMinutes] int NOT NULL CONSTRAINT [DF_FaAttDailies_Early] DEFAULT(0),
                    [OvertimeMinutes] int NOT NULL CONSTRAINT [DF_FaAttDailies_Ot] DEFAULT(0),
                    [Status] int NOT NULL CONSTRAINT [DF_FaAttDailies_Status] DEFAULT(4),
                    [IsIncomplete] bit NOT NULL CONSTRAINT [DF_FaAttDailies_Incomplete] DEFAULT(0),
                    [Note] nvarchar(300) NULL,
                    [CalculatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_FaAttDailies_Employee_Date' AND object_id = OBJECT_ID(N'dbo.FaAttDailies'))
                CREATE UNIQUE INDEX [UX_FaAttDailies_Employee_Date] ON [FaAttDailies] ([EmployeeId], [Date]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FaAttDailies_Date' AND object_id = OBJECT_ID(N'dbo.FaAttDailies'))
                CREATE INDEX [IX_FaAttDailies_Date] ON [FaAttDailies] ([Date]);");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.FaAttMissions', N'U') IS NULL
            BEGIN
                CREATE TABLE [FaAttMissions] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [EmployeeId] int NOT NULL,
                    [FromDate] datetime2 NOT NULL,
                    [ToDate] datetime2 NOT NULL,
                    [Destination] nvarchar(150) NULL,
                    [Reason] nvarchar(500) NULL,
                    [Status] int NOT NULL CONSTRAINT [DF_FaAttMissions_Status] DEFAULT(0),
                    [DecidedByUserId] int NULL,
                    [DecidedByName] nvarchar(150) NULL,
                    [DecidedAt] datetime2 NULL,
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FaAttMissions_EmployeeId' AND object_id = OBJECT_ID(N'dbo.FaAttMissions'))
                CREATE INDEX [IX_FaAttMissions_EmployeeId] ON [FaAttMissions] ([EmployeeId]);");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.FaAttLeaveTypes', N'U') IS NULL
            BEGIN
                CREATE TABLE [FaAttLeaveTypes] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [Name] nvarchar(50) NOT NULL,
                    [AnnualLimitDays] int NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_FaAttLeaveTypes_IsActive] DEFAULT(1),
                    [SortOrder] int NOT NULL CONSTRAINT [DF_FaAttLeaveTypes_Sort] DEFAULT(0)
                );
            END");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.FaAttLeaves', N'U') IS NULL
            BEGIN
                CREATE TABLE [FaAttLeaves] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [EmployeeId] int NOT NULL,
                    [LeaveTypeId] int NOT NULL,
                    [FromDate] datetime2 NOT NULL,
                    [ToDate] datetime2 NOT NULL,
                    [HoursPerDay] float NULL,
                    [Reason] nvarchar(500) NULL,
                    [Status] int NOT NULL CONSTRAINT [DF_FaAttLeaves_Status] DEFAULT(0),
                    [DecidedByUserId] int NULL,
                    [DecidedByName] nvarchar(150) NULL,
                    [DecidedAt] datetime2 NULL,
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FaAttLeaves_EmployeeId' AND object_id = OBJECT_ID(N'dbo.FaAttLeaves'))
                CREATE INDEX [IX_FaAttLeaves_EmployeeId] ON [FaAttLeaves] ([EmployeeId]);");
    }
}
