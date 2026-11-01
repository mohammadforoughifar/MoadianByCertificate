using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// فاز چهارم مدیریت سرور: تاریخچهٔ ساخت‌یافتهٔ قطعات سخت‌افزاری و رخدادهای Redfish/iLO.
/// پاسخ خام Redfish ذخیره نمی‌شود و ارتقا برای SQLite و SQL Server تکرارپذیر است.
/// </summary>
public static class ServerHardwareDetailsSchemaV4
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ServerHardwareComponentSnapshot>().HasIndex(x => new { x.ManagedServerId, x.CapturedAt });
        model.Entity<ServerHardwareComponentSnapshot>().HasIndex(x => new { x.ManagedServerId, x.ComponentType, x.ComponentKey, x.CapturedAt });
        model.Entity<ServerHardwareEvent>().HasIndex(x => new { x.ManagedServerId, x.EventKey }).IsUnique();
        model.Entity<ServerHardwareEvent>().HasIndex(x => new { x.ManagedServerId, x.OccurredAt });
    }

    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.IsSqlite())
            {
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN LastHardwareDetailsAt TEXT NULL;");
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ServerHardwareComponentSnapshots (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ManagedServerId INTEGER NOT NULL, CapturedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ComponentType TEXT NOT NULL DEFAULT '', ComponentKey TEXT NOT NULL DEFAULT '', Name TEXT NOT NULL DEFAULT '',
    Health TEXT NOT NULL DEFAULT 'Unknown', State TEXT NULL, Reading TEXT NULL, Model TEXT NULL,
    SerialNumber TEXT NULL, Message TEXT NULL
);
CREATE TABLE IF NOT EXISTS ServerHardwareEvents (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ManagedServerId INTEGER NOT NULL, EventKey TEXT NOT NULL DEFAULT '', Severity TEXT NOT NULL DEFAULT 'Unknown',
    Message TEXT NOT NULL DEFAULT '', Origin TEXT NULL, OccurredAt TEXT NULL,
    FirstSeenAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, LastSeenAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ServerHardwareComponentSnapshots_ManagedServerId_CapturedAt ON ServerHardwareComponentSnapshots (ManagedServerId, CapturedAt);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ServerHardwareComponentSnapshots_ManagedServerId_ComponentType_ComponentKey_CapturedAt ON ServerHardwareComponentSnapshots (ManagedServerId, ComponentType, ComponentKey, CapturedAt);");
                await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_ServerHardwareEvents_ManagedServerId_EventKey ON ServerHardwareEvents (ManagedServerId, EventKey);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ServerHardwareEvents_ManagedServerId_OccurredAt ON ServerHardwareEvents (ManagedServerId, OccurredAt);");
                return;
            }

            if (!db.Database.IsSqlServer()) return;
            await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.ManagedServers', N'LastHardwareDetailsAt') IS NULL
    ALTER TABLE [ManagedServers] ADD [LastHardwareDetailsAt] datetime2 NULL;
IF OBJECT_ID(N'dbo.ServerHardwareComponentSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE [ServerHardwareComponentSnapshots](
        [Id] bigint NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ServerHardwareComponentSnapshots] PRIMARY KEY,
        [ManagedServerId] int NOT NULL, [CapturedAt] datetime2 NOT NULL CONSTRAINT [DF_ServerHardwareComponentSnapshots_CapturedAt] DEFAULT(GETDATE()),
        [ComponentType] nvarchar(40) NOT NULL CONSTRAINT [DF_ServerHardwareComponentSnapshots_ComponentType] DEFAULT(N''),
        [ComponentKey] nvarchar(200) NOT NULL CONSTRAINT [DF_ServerHardwareComponentSnapshots_ComponentKey] DEFAULT(N''),
        [Name] nvarchar(200) NOT NULL CONSTRAINT [DF_ServerHardwareComponentSnapshots_Name] DEFAULT(N''),
        [Health] nvarchar(20) NOT NULL CONSTRAINT [DF_ServerHardwareComponentSnapshots_Health] DEFAULT(N'Unknown'),
        [State] nvarchar(40) NULL, [Reading] nvarchar(100) NULL, [Model] nvarchar(150) NULL,
        [SerialNumber] nvarchar(100) NULL, [Message] nvarchar(500) NULL
    );
END
IF OBJECT_ID(N'dbo.ServerHardwareEvents', N'U') IS NULL
BEGIN
    CREATE TABLE [ServerHardwareEvents](
        [Id] bigint NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ServerHardwareEvents] PRIMARY KEY,
        [ManagedServerId] int NOT NULL, [EventKey] nvarchar(200) NOT NULL CONSTRAINT [DF_ServerHardwareEvents_EventKey] DEFAULT(N''),
        [Severity] nvarchar(30) NOT NULL CONSTRAINT [DF_ServerHardwareEvents_Severity] DEFAULT(N'Unknown'),
        [Message] nvarchar(2000) NOT NULL CONSTRAINT [DF_ServerHardwareEvents_Message] DEFAULT(N''), [Origin] nvarchar(500) NULL,
        [OccurredAt] datetime2 NULL, [FirstSeenAt] datetime2 NOT NULL CONSTRAINT [DF_ServerHardwareEvents_FirstSeenAt] DEFAULT(GETDATE()),
        [LastSeenAt] datetime2 NOT NULL CONSTRAINT [DF_ServerHardwareEvents_LastSeenAt] DEFAULT(GETDATE())
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServerHardwareComponentSnapshots_ManagedServerId_CapturedAt' AND object_id = OBJECT_ID(N'dbo.ServerHardwareComponentSnapshots'))
    CREATE INDEX [IX_ServerHardwareComponentSnapshots_ManagedServerId_CapturedAt] ON [ServerHardwareComponentSnapshots]([ManagedServerId], [CapturedAt]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServerHardwareComponentSnapshots_ManagedServerId_ComponentType_ComponentKey_CapturedAt' AND object_id = OBJECT_ID(N'dbo.ServerHardwareComponentSnapshots'))
    CREATE INDEX [IX_ServerHardwareComponentSnapshots_ManagedServerId_ComponentType_ComponentKey_CapturedAt] ON [ServerHardwareComponentSnapshots]([ManagedServerId], [ComponentType], [ComponentKey], [CapturedAt]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServerHardwareEvents_ManagedServerId_EventKey' AND object_id = OBJECT_ID(N'dbo.ServerHardwareEvents'))
    CREATE UNIQUE INDEX [IX_ServerHardwareEvents_ManagedServerId_EventKey] ON [ServerHardwareEvents]([ManagedServerId], [EventKey]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServerHardwareEvents_ManagedServerId_OccurredAt' AND object_id = OBJECT_ID(N'dbo.ServerHardwareEvents'))
    CREATE INDEX [IX_ServerHardwareEvents_ManagedServerId_OccurredAt] ON [ServerHardwareEvents]([ManagedServerId], [OccurredAt]);
");
            Console.WriteLine("[DB] ✔ تاریخچهٔ قطعات سخت‌افزاری و رخدادهای سرور آماده‌اند.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ⚠ ارتقای جزئیات سخت‌افزاری سرور ناموفق بود: {ex.Message}");
        }
    }

    private static async Task TrySqliteAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch { /* ستون از قبل وجود دارد یا دیتابیس تازه با مدل EF ساخته شده است. */ }
    }
}
