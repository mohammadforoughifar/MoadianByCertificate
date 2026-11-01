using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ارتقای ماژول «ماشین‌های اداری»: شناسنامهٔ کامل، قرارداد/گارانتی، پایش SNMP،
/// مواد مصرفی، سینی‌ها، شمارندهٔ روزانه و رویدادها.
/// مثل سایر SchemaVxها idempotent است و روی دیتابیس‌های نصب‌شدهٔ قبلی (SQL Server و SQLite)
/// ستون‌ها/جدول‌های جدید را بدون از دست رفتن داده اضافه می‌کند.
/// </summary>
public static class OfficeMachineSchemaV2
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<OfficeMachine>().Property(m => m.PurchasePrice).HasPrecision(18, 2);
        model.Entity<OfficeMachine>().HasIndex(m => new { m.SnmpEnabled, m.LastPollAt });
        model.Entity<OfficeMachineSupply>().HasIndex(s => new { s.MachineId, s.SupplyIndex });
        model.Entity<OfficeMachineTray>().HasIndex(t => new { t.MachineId, t.TrayIndex });
        model.Entity<OfficeMachineCounter>().HasIndex(c => new { c.MachineId, c.Day });
        model.Entity<OfficeMachineEvent>().HasIndex(e => new { e.MachineId, e.IsResolved });
        model.Entity<OfficeMachineEvent>().HasIndex(e => new { e.IsResolved, e.Severity });
    }

    // (نام ستون، نوع SQL Server، نوع SQLite)
    private static readonly (string Name, string SqlServer, string Sqlite)[] MachineColumns =
    {
        ("DeviceType", "nvarchar(30) NOT NULL CONSTRAINT [DF_OfficeMachines_DeviceType] DEFAULT(N'Printer')", "TEXT NOT NULL DEFAULT 'Printer'"),
        ("Brand", "nvarchar(80) NULL", "TEXT NULL"),
        ("AssetTag", "nvarchar(80) NULL", "TEXT NULL"),
        ("IsColor", "bit NOT NULL CONSTRAINT [DF_OfficeMachines_IsColor] DEFAULT(0)", "INTEGER NOT NULL DEFAULT 0"),
        ("Department", "nvarchar(150) NULL", "TEXT NULL"),
        ("ResponsibleUserId", "int NULL", "INTEGER NULL"),
        ("MacAddress", "nvarchar(30) NULL", "TEXT NULL"),
        ("Hostname", "nvarchar(100) NULL", "TEXT NULL"),
        ("FirmwareVersion", "nvarchar(100) NULL", "TEXT NULL"),
        ("SysDescr", "nvarchar(300) NULL", "TEXT NULL"),
        ("PurchaseDate", "datetime2 NULL", "TEXT NULL"),
        ("PurchasePrice", "decimal(18,2) NULL", "TEXT NULL"),
        ("WarrantyUntil", "datetime2 NULL", "TEXT NULL"),
        ("ServiceVendor", "nvarchar(150) NULL", "TEXT NULL"),
        ("ServiceContractUntil", "datetime2 NULL", "TEXT NULL"),
        ("SnmpEnabled", "bit NOT NULL CONSTRAINT [DF_OfficeMachines_SnmpEnabled] DEFAULT(0)", "INTEGER NOT NULL DEFAULT 0"),
        ("SnmpVersion", "nvarchar(5) NOT NULL CONSTRAINT [DF_OfficeMachines_SnmpVersion] DEFAULT(N'v2c')", "TEXT NOT NULL DEFAULT 'v2c'"),
        ("SnmpPort", "int NOT NULL CONSTRAINT [DF_OfficeMachines_SnmpPort] DEFAULT(161)", "INTEGER NOT NULL DEFAULT 161"),
        ("SnmpCommunityCipher", "nvarchar(600) NULL", "TEXT NULL"),
        ("SnmpV3User", "nvarchar(100) NULL", "TEXT NULL"),
        ("SnmpV3AuthProtocol", "nvarchar(10) NOT NULL CONSTRAINT [DF_OfficeMachines_SnmpV3AuthProtocol] DEFAULT(N'SHA1')", "TEXT NOT NULL DEFAULT 'SHA1'"),
        ("SnmpV3AuthCipher", "nvarchar(600) NULL", "TEXT NULL"),
        ("SnmpV3PrivProtocol", "nvarchar(10) NOT NULL CONSTRAINT [DF_OfficeMachines_SnmpV3PrivProtocol] DEFAULT(N'AES')", "TEXT NOT NULL DEFAULT 'AES'"),
        ("SnmpV3PrivCipher", "nvarchar(600) NULL", "TEXT NULL"),
        ("PollIntervalMinutes", "int NOT NULL CONSTRAINT [DF_OfficeMachines_PollIntervalMinutes] DEFAULT(10)", "INTEGER NOT NULL DEFAULT 10"),
        ("AlertsEnabled", "bit NOT NULL CONSTRAINT [DF_OfficeMachines_AlertsEnabled] DEFAULT(1)", "INTEGER NOT NULL DEFAULT 1"),
        ("SupplyLowThreshold", "int NOT NULL CONSTRAINT [DF_OfficeMachines_SupplyLowThreshold] DEFAULT(15)", "INTEGER NOT NULL DEFAULT 15"),
        ("LastPollAt", "datetime2 NULL", "TEXT NULL"),
        ("LastSeenOnlineAt", "datetime2 NULL", "TEXT NULL"),
        ("OnlineState", "nvarchar(20) NOT NULL CONSTRAINT [DF_OfficeMachines_OnlineState] DEFAULT(N'Unknown')", "TEXT NOT NULL DEFAULT 'Unknown'"),
        ("PrinterState", "nvarchar(20) NULL", "TEXT NULL"),
        ("LastPageCount", "bigint NULL", "INTEGER NULL"),
        ("LowestSupplyPercent", "int NULL", "INTEGER NULL"),
        ("LastStatusText", "nvarchar(300) NULL", "TEXT NULL"),
        ("LastErrorCodes", "nvarchar(300) NULL", "TEXT NULL"),
        ("LastPollError", "nvarchar(500) NULL", "TEXT NULL"),
        ("UptimeSeconds", "bigint NULL", "INTEGER NULL"),
        ("UpdatedAt", "datetime2 NULL", "TEXT NULL"),
    };

    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.IsSqlite()) { await EnsureSqliteAsync(db); return; }
            if (db.Database.IsSqlServer()) await EnsureSqlServerAsync(db);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ⚠ ارتقای ماژول ماشین‌های اداری (V2) ناموفق بود: {ex.Message}");
        }
    }

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (name, type, _) in MachineColumns)
            sb.AppendLine($"IF COL_LENGTH(N'dbo.OfficeMachines', N'{name}') IS NULL ALTER TABLE [OfficeMachines] ADD [{name}] {type};");

        // Status: ماشین‌های غیرفعالِ قبلی «رزرو/انبار» در نظر گرفته می‌شوند (داده از بین نمی‌رود).
        sb.AppendLine(@"
IF COL_LENGTH(N'dbo.OfficeMachines', N'Status') IS NULL
BEGIN
    ALTER TABLE [OfficeMachines] ADD [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_OfficeMachines_Status] DEFAULT(N'InService');
    EXEC(N'UPDATE [OfficeMachines] SET [Status] = N''Standby'' WHERE [IsActive] = 0');
END
IF COL_LENGTH(N'dbo.OfficeMachineCosts', N'Category') IS NULL
    ALTER TABLE [OfficeMachineCosts] ADD [Category] nvarchar(30) NOT NULL CONSTRAINT [DF_OfficeMachineCosts_Category] DEFAULT(N'Other');

IF OBJECT_ID(N'dbo.OfficeMachineSupplies', N'U') IS NULL
    CREATE TABLE [OfficeMachineSupplies](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_OfficeMachineSupplies] PRIMARY KEY,
        [MachineId] int NOT NULL, [SupplyIndex] int NOT NULL,
        [Kind] nvarchar(30) NOT NULL CONSTRAINT [DF_OfficeMachineSupplies_Kind] DEFAULT(N'Other'),
        [Color] nvarchar(30) NULL, [Description] nvarchar(200) NULL,
        [Level] int NOT NULL CONSTRAINT [DF_OfficeMachineSupplies_Level] DEFAULT(0),
        [MaxCapacity] int NOT NULL CONSTRAINT [DF_OfficeMachineSupplies_MaxCapacity] DEFAULT(0),
        [Percent] int NULL,
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_OfficeMachineSupplies_UpdatedAt] DEFAULT(GETDATE())
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeMachineSupplies_MachineId_SupplyIndex')
    CREATE INDEX [IX_OfficeMachineSupplies_MachineId_SupplyIndex] ON [OfficeMachineSupplies]([MachineId],[SupplyIndex]);

IF OBJECT_ID(N'dbo.OfficeMachineTrays', N'U') IS NULL
    CREATE TABLE [OfficeMachineTrays](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_OfficeMachineTrays] PRIMARY KEY,
        [MachineId] int NOT NULL, [TrayIndex] int NOT NULL,
        [Name] nvarchar(100) NULL, [MediaName] nvarchar(100) NULL,
        [CurrentLevel] int NOT NULL CONSTRAINT [DF_OfficeMachineTrays_CurrentLevel] DEFAULT(0),
        [MaxCapacity] int NOT NULL CONSTRAINT [DF_OfficeMachineTrays_MaxCapacity] DEFAULT(0),
        [Percent] int NULL,
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_OfficeMachineTrays_UpdatedAt] DEFAULT(GETDATE())
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeMachineTrays_MachineId_TrayIndex')
    CREATE INDEX [IX_OfficeMachineTrays_MachineId_TrayIndex] ON [OfficeMachineTrays]([MachineId],[TrayIndex]);

IF OBJECT_ID(N'dbo.OfficeMachineCounters', N'U') IS NULL
    CREATE TABLE [OfficeMachineCounters](
        [Id] bigint NOT NULL IDENTITY(1,1) CONSTRAINT [PK_OfficeMachineCounters] PRIMARY KEY,
        [MachineId] int NOT NULL, [Day] datetime2 NOT NULL,
        [TotalPages] bigint NOT NULL CONSTRAINT [DF_OfficeMachineCounters_TotalPages] DEFAULT(0),
        [TakenAt] datetime2 NOT NULL CONSTRAINT [DF_OfficeMachineCounters_TakenAt] DEFAULT(GETDATE())
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeMachineCounters_MachineId_Day')
    CREATE INDEX [IX_OfficeMachineCounters_MachineId_Day] ON [OfficeMachineCounters]([MachineId],[Day]);

IF OBJECT_ID(N'dbo.OfficeMachineEvents', N'U') IS NULL
    CREATE TABLE [OfficeMachineEvents](
        [Id] bigint NOT NULL IDENTITY(1,1) CONSTRAINT [PK_OfficeMachineEvents] PRIMARY KEY,
        [MachineId] int NOT NULL,
        [Code] nvarchar(40) NOT NULL CONSTRAINT [DF_OfficeMachineEvents_Code] DEFAULT(N''),
        [Severity] nvarchar(20) NOT NULL CONSTRAINT [DF_OfficeMachineEvents_Severity] DEFAULT(N'Warning'),
        [Message] nvarchar(500) NOT NULL CONSTRAINT [DF_OfficeMachineEvents_Message] DEFAULT(N''),
        [Source] nvarchar(20) NOT NULL CONSTRAINT [DF_OfficeMachineEvents_Source] DEFAULT(N'Poll'),
        [OpenedAt] datetime2 NOT NULL CONSTRAINT [DF_OfficeMachineEvents_OpenedAt] DEFAULT(GETDATE()),
        [LastSeenAt] datetime2 NOT NULL CONSTRAINT [DF_OfficeMachineEvents_LastSeenAt] DEFAULT(GETDATE()),
        [IsResolved] bit NOT NULL CONSTRAINT [DF_OfficeMachineEvents_IsResolved] DEFAULT(0),
        [ResolvedAt] datetime2 NULL, [ResolvedByName] nvarchar(200) NULL,
        [Notified] bit NOT NULL CONSTRAINT [DF_OfficeMachineEvents_Notified] DEFAULT(0)
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeMachineEvents_MachineId_IsResolved')
    CREATE INDEX [IX_OfficeMachineEvents_MachineId_IsResolved] ON [OfficeMachineEvents]([MachineId],[IsResolved]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeMachineEvents_IsResolved_Severity')
    CREATE INDEX [IX_OfficeMachineEvents_IsResolved_Severity] ON [OfficeMachineEvents]([IsResolved],[Severity]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeMachines_SnmpEnabled_LastPollAt')
    CREATE INDEX [IX_OfficeMachines_SnmpEnabled_LastPollAt] ON [OfficeMachines]([SnmpEnabled],[LastPollAt]);
");
        await db.Database.ExecuteSqlRawAsync(sb.ToString());
        Console.WriteLine("[DB] ✔ ماژول ماشین‌های اداری (SNMP، مواد مصرفی، رویدادها) آماده است.");
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        foreach (var (name, _, type) in MachineColumns)
            await TrySqliteAsync(db, $"ALTER TABLE OfficeMachines ADD COLUMN {name} {type};");
        if (await TrySqliteAsync(db, "ALTER TABLE OfficeMachines ADD COLUMN Status TEXT NOT NULL DEFAULT 'InService';"))
            await TrySqliteAsync(db, "UPDATE OfficeMachines SET Status = 'Standby' WHERE IsActive = 0;");
        await TrySqliteAsync(db, "ALTER TABLE OfficeMachineCosts ADD COLUMN Category TEXT NOT NULL DEFAULT 'Other';");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS OfficeMachineSupplies (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, MachineId INTEGER NOT NULL, SupplyIndex INTEGER NOT NULL,
    Kind TEXT NOT NULL DEFAULT 'Other', Color TEXT NULL, Description TEXT NULL,
    Level INTEGER NOT NULL DEFAULT 0, MaxCapacity INTEGER NOT NULL DEFAULT 0, Percent INTEGER NULL,
    UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX IF NOT EXISTS IX_OfficeMachineSupplies_MachineId_SupplyIndex ON OfficeMachineSupplies (MachineId, SupplyIndex);
CREATE TABLE IF NOT EXISTS OfficeMachineTrays (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, MachineId INTEGER NOT NULL, TrayIndex INTEGER NOT NULL,
    Name TEXT NULL, MediaName TEXT NULL, CurrentLevel INTEGER NOT NULL DEFAULT 0, MaxCapacity INTEGER NOT NULL DEFAULT 0,
    Percent INTEGER NULL, UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX IF NOT EXISTS IX_OfficeMachineTrays_MachineId_TrayIndex ON OfficeMachineTrays (MachineId, TrayIndex);
CREATE TABLE IF NOT EXISTS OfficeMachineCounters (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, MachineId INTEGER NOT NULL, Day TEXT NOT NULL,
    TotalPages INTEGER NOT NULL DEFAULT 0, TakenAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX IF NOT EXISTS IX_OfficeMachineCounters_MachineId_Day ON OfficeMachineCounters (MachineId, Day);
CREATE TABLE IF NOT EXISTS OfficeMachineEvents (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, MachineId INTEGER NOT NULL,
    Code TEXT NOT NULL DEFAULT '', Severity TEXT NOT NULL DEFAULT 'Warning', Message TEXT NOT NULL DEFAULT '',
    Source TEXT NOT NULL DEFAULT 'Poll', OpenedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    LastSeenAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, IsResolved INTEGER NOT NULL DEFAULT 0,
    ResolvedAt TEXT NULL, ResolvedByName TEXT NULL, Notified INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS IX_OfficeMachineEvents_MachineId_IsResolved ON OfficeMachineEvents (MachineId, IsResolved);
CREATE INDEX IF NOT EXISTS IX_OfficeMachineEvents_IsResolved_Severity ON OfficeMachineEvents (IsResolved, Severity);
CREATE INDEX IF NOT EXISTS IX_OfficeMachines_SnmpEnabled_LastPollAt ON OfficeMachines (SnmpEnabled, LastPollAt);
");
    }

    private static async Task<bool> TrySqliteAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); return true; }
        catch { return false; /* ستون از قبل وجود دارد یا دیتابیس تازه آن را با مدل ساخته است. */ }
    }
}
