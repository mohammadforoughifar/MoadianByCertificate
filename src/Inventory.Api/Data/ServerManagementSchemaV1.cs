using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// اسکیما و ایندکس‌های مدیریت سرور؛ هم برای SQLite توسعه و هم SQL Server عملیاتی
/// بدون وابستگی به migration قابل اجرا و تکرارپذیر است.
/// </summary>
public static class ServerManagementSchemaV1
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ManagedServer>().HasIndex(s => new { s.CompanyId, s.Status });
        model.Entity<ManagedServer>().HasIndex(s => new { s.CompanyId, s.IloAddress });
        model.Entity<ServerHealthSnapshot>().HasIndex(s => new { s.ManagedServerId, s.CapturedAt });
        model.Entity<ServerOperationLog>().HasIndex(l => new { l.ManagedServerId, l.At });
    }

    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.IsSqlite())
            {
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ManagedServers (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NOT NULL,
    Name TEXT NOT NULL DEFAULT '', Manufacturer TEXT NOT NULL DEFAULT 'HPE', Model TEXT NULL,
    SerialNumber TEXT NULL, AssetTag TEXT NULL,
    IloAddress TEXT NOT NULL DEFAULT '', IloPort INTEGER NOT NULL DEFAULT 443, CredentialId INTEGER NULL, IloVersion TEXT NULL,
    Role TEXT NULL, Environment TEXT NULL, Criticality TEXT NOT NULL DEFAULT 'Normal', Status TEXT NOT NULL DEFAULT 'Active',
    Rack TEXT NULL, RackUnit INTEGER NULL, OperatingSystem TEXT NULL, WarrantyEndDate TEXT NULL, Notes TEXT NULL,
    LastHealthCheckAt TEXT NULL, LastHealthStatus TEXT NULL, LastPowerState TEXT NULL,
    LastHealthMessage TEXT NULL, LastConnectionError TEXT NULL, LastInventoryAt TEXT NULL,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, CreatedByUserId INTEGER NULL,
    UpdatedAt TEXT NULL, UpdatedByUserId INTEGER NULL, UpdatedByName TEXT NULL
);");
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ServerHealthSnapshots (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ManagedServerId INTEGER NOT NULL, CapturedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    OverallHealth TEXT NOT NULL DEFAULT 'Unknown', PowerState TEXT NULL, FirmwareVersion TEXT NULL,
    BiosVersion TEXT NULL, Model TEXT NULL, SerialNumber TEXT NULL, Message TEXT NULL
);");
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ServerOperationLogs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ManagedServerId INTEGER NOT NULL, UserId INTEGER NULL, UserName TEXT NULL,
    Action TEXT NOT NULL DEFAULT '', Result TEXT NOT NULL DEFAULT 'Success', Details TEXT NULL,
    ClientIp TEXT NULL, At TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ManagedServers_CompanyId_Status ON ManagedServers (CompanyId, Status);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ManagedServers_CompanyId_IloAddress ON ManagedServers (CompanyId, IloAddress);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ServerHealthSnapshots_ManagedServerId_CapturedAt ON ServerHealthSnapshots (ManagedServerId, CapturedAt);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ServerOperationLogs_ManagedServerId_At ON ServerOperationLogs (ManagedServerId, At);");
                return;
            }

            if (!db.Database.IsSqlServer()) return;
            await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.ManagedServers', N'U') IS NULL
BEGIN
    CREATE TABLE [ManagedServers](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ManagedServers] PRIMARY KEY,
        [CompanyId] int NOT NULL,
        [Name] nvarchar(150) NOT NULL CONSTRAINT [DF_ManagedServers_Name] DEFAULT(N''),
        [Manufacturer] nvarchar(50) NOT NULL CONSTRAINT [DF_ManagedServers_Manufacturer] DEFAULT(N'HPE'),
        [Model] nvarchar(150) NULL, [SerialNumber] nvarchar(100) NULL, [AssetTag] nvarchar(100) NULL,
        [IloAddress] nvarchar(250) NOT NULL CONSTRAINT [DF_ManagedServers_IloAddress] DEFAULT(N''),
        [IloPort] int NOT NULL CONSTRAINT [DF_ManagedServers_IloPort] DEFAULT(443), [CredentialId] int NULL,
        [IloVersion] nvarchar(80) NULL,
        [Role] nvarchar(100) NULL, [Environment] nvarchar(100) NULL,
        [Criticality] nvarchar(20) NOT NULL CONSTRAINT [DF_ManagedServers_Criticality] DEFAULT(N'Normal'),
        [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_ManagedServers_Status] DEFAULT(N'Active'),
        [Rack] nvarchar(100) NULL, [RackUnit] int NULL, [OperatingSystem] nvarchar(200) NULL,
        [WarrantyEndDate] datetime2 NULL, [Notes] nvarchar(2000) NULL,
        [LastHealthCheckAt] datetime2 NULL, [LastHealthStatus] nvarchar(20) NULL,
        [LastPowerState] nvarchar(30) NULL, [LastHealthMessage] nvarchar(500) NULL,
        [LastConnectionError] nvarchar(1000) NULL, [LastInventoryAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ManagedServers_CreatedAt] DEFAULT(GETDATE()),
        [CreatedByUserId] int NULL, [UpdatedAt] datetime2 NULL, [UpdatedByUserId] int NULL,
        [UpdatedByName] nvarchar(200) NULL
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ManagedServers_CompanyId_Status' AND object_id = OBJECT_ID(N'dbo.ManagedServers'))
    CREATE INDEX [IX_ManagedServers_CompanyId_Status] ON [ManagedServers]([CompanyId], [Status]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ManagedServers_CompanyId_IloAddress' AND object_id = OBJECT_ID(N'dbo.ManagedServers'))
    CREATE INDEX [IX_ManagedServers_CompanyId_IloAddress] ON [ManagedServers]([CompanyId], [IloAddress]);

IF OBJECT_ID(N'dbo.ServerHealthSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE [ServerHealthSnapshots](
        [Id] bigint NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ServerHealthSnapshots] PRIMARY KEY,
        [ManagedServerId] int NOT NULL, [CapturedAt] datetime2 NOT NULL CONSTRAINT [DF_ServerHealthSnapshots_CapturedAt] DEFAULT(GETDATE()),
        [OverallHealth] nvarchar(20) NOT NULL CONSTRAINT [DF_ServerHealthSnapshots_OverallHealth] DEFAULT(N'Unknown'),
        [PowerState] nvarchar(30) NULL, [FirmwareVersion] nvarchar(100) NULL, [BiosVersion] nvarchar(100) NULL,
        [Model] nvarchar(150) NULL, [SerialNumber] nvarchar(100) NULL, [Message] nvarchar(500) NULL
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServerHealthSnapshots_ManagedServerId_CapturedAt' AND object_id = OBJECT_ID(N'dbo.ServerHealthSnapshots'))
    CREATE INDEX [IX_ServerHealthSnapshots_ManagedServerId_CapturedAt] ON [ServerHealthSnapshots]([ManagedServerId], [CapturedAt]);

IF OBJECT_ID(N'dbo.ServerOperationLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [ServerOperationLogs](
        [Id] bigint NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ServerOperationLogs] PRIMARY KEY,
        [ManagedServerId] int NOT NULL, [UserId] int NULL, [UserName] nvarchar(200) NULL,
        [Action] nvarchar(40) NOT NULL CONSTRAINT [DF_ServerOperationLogs_Action] DEFAULT(N''),
        [Result] nvarchar(20) NOT NULL CONSTRAINT [DF_ServerOperationLogs_Result] DEFAULT(N'Success'),
        [Details] nvarchar(1000) NULL, [ClientIp] nvarchar(64) NULL,
        [At] datetime2 NOT NULL CONSTRAINT [DF_ServerOperationLogs_At] DEFAULT(GETDATE())
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServerOperationLogs_ManagedServerId_At' AND object_id = OBJECT_ID(N'dbo.ServerOperationLogs'))
    CREATE INDEX [IX_ServerOperationLogs_ManagedServerId_At] ON [ServerOperationLogs]([ManagedServerId], [At]);
");
            Console.WriteLine("[DB] ✔ جدول‌های مدیریت سرورها (ManagedServers) آماده‌اند.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ⚠ ساخت جدول‌های مدیریت سرورها ناموفق بود: {ex.Message}");
        }
    }
}
