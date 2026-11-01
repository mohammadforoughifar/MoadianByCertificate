using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ماشین‌های اداری — V3: پایش چاپگرهای USB/محلی از طریق ایجنت سیستم‌ها.
///   • جدول OfficeAgentPrinters (چاپگرهای گزارش‌شده توسط ایجنت‌ها)
///   • ستون OfficeMachines.AgentPrinterId (لینک دستگاه کابلی به چاپگر ایجنت)
/// idempotent؛ روی SQL Server و SQLite بدون از دست رفتن داده اجرا می‌شود.
/// </summary>
public static class OfficeMachineSchemaV3
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<OfficeAgentPrinter>().HasIndex(p => new { p.AgentId, p.PrinterName }).IsUnique();
        model.Entity<OfficeAgentPrinter>().HasIndex(p => p.SystemInfoId);
        model.Entity<OfficeMachine>().HasIndex(m => m.AgentPrinterId);
        model.Entity<OfficeMachine>().HasIndex(m => m.CompanyId);
        model.Entity<OfficeMachine>().HasIndex(m => m.AssignedUserId);
    }

    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.IsSqlite()) { await EnsureSqliteAsync(db); return; }
            if (db.Database.IsSqlServer()) await EnsureSqlServerAsync(db);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ⚠ ارتقای ماژول ماشین‌های اداری (V3 — پایش با ایجنت) ناموفق بود: {ex.Message}");
        }
    }

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.OfficeMachines', N'AgentPrinterId') IS NULL
    ALTER TABLE [OfficeMachines] ADD [AgentPrinterId] int NULL;
IF COL_LENGTH(N'dbo.OfficeMachines', N'CompanyId') IS NULL
    ALTER TABLE [OfficeMachines] ADD [CompanyId] int NULL;
IF COL_LENGTH(N'dbo.OfficeMachines', N'AssignedUserId') IS NULL
    ALTER TABLE [OfficeMachines] ADD [AssignedUserId] int NULL;

IF OBJECT_ID(N'dbo.OfficeAgentPrinters', N'U') IS NULL
    CREATE TABLE [OfficeAgentPrinters](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_OfficeAgentPrinters] PRIMARY KEY,
        [AgentId] nvarchar(100) NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_AgentId] DEFAULT(N''),
        [SystemInfoId] int NULL,
        [HostName] nvarchar(100) NULL,
        [PrinterName] nvarchar(250) NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_PrinterName] DEFAULT(N''),
        [DriverName] nvarchar(250) NULL,
        [PortName] nvarchar(150) NULL,
        [Kind] nvarchar(20) NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_Kind] DEFAULT(N'Other'),
        [PortIp] nvarchar(50) NULL,
        [IsDefault] bit NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_IsDefault] DEFAULT(0),
        [IsShared] bit NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_IsShared] DEFAULT(0),
        [Location] nvarchar(250) NULL,
        [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_Status] DEFAULT(N'Unknown'),
        [ErrorCodes] nvarchar(300) NULL,
        [JobsCount] int NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_JobsCount] DEFAULT(0),
        [JobsInError] int NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_JobsInError] DEFAULT(0),
        [OldestJobMinutes] int NULL,
        [AgentCumulativePages] bigint NULL,
        [PagesLogged] bigint NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_PagesLogged] DEFAULT(0),
        [IsPresent] bit NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_IsPresent] DEFAULT(1),
        [IsIgnored] bit NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_IsIgnored] DEFAULT(0),
        [FirstSeenAt] datetime2 NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_FirstSeenAt] DEFAULT(GETDATE()),
        [LastSeenAt] datetime2 NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_LastSeenAt] DEFAULT(GETDATE()),
        [LastReportAt] datetime2 NOT NULL CONSTRAINT [DF_OfficeAgentPrinters_LastReportAt] DEFAULT(GETDATE())
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeAgentPrinters_AgentId_PrinterName')
    CREATE UNIQUE INDEX [IX_OfficeAgentPrinters_AgentId_PrinterName] ON [OfficeAgentPrinters]([AgentId],[PrinterName]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeAgentPrinters_SystemInfoId')
    CREATE INDEX [IX_OfficeAgentPrinters_SystemInfoId] ON [OfficeAgentPrinters]([SystemInfoId]);
");
        // ایندکس روی ستون تازه‌ساخته باید در batch جداگانه باشد (SQL Server ستون را در همان batch نمی‌شناسد)
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeMachines_CompanyId')
    CREATE INDEX [IX_OfficeMachines_CompanyId] ON [OfficeMachines]([CompanyId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeMachines_AssignedUserId')
    CREATE INDEX [IX_OfficeMachines_AssignedUserId] ON [OfficeMachines]([AssignedUserId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfficeMachines_AgentPrinterId')
    CREATE INDEX [IX_OfficeMachines_AgentPrinterId] ON [OfficeMachines]([AgentPrinterId]);
");
        Console.WriteLine("[DB] ✔ پایش چاپگرهای USB با ایجنت (ماشین‌های اداری V3) آماده است.");
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        foreach (var col in new[] { "AgentPrinterId", "CompanyId", "AssignedUserId" })
        {
            try { await db.Database.ExecuteSqlRawAsync($"ALTER TABLE OfficeMachines ADD COLUMN {col} INTEGER NULL;"); }
            catch { /* ستون از قبل وجود دارد */ }
        }
        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS OfficeAgentPrinters (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AgentId TEXT NOT NULL DEFAULT '', SystemInfoId INTEGER NULL, HostName TEXT NULL,
    PrinterName TEXT NOT NULL DEFAULT '', DriverName TEXT NULL, PortName TEXT NULL,
    Kind TEXT NOT NULL DEFAULT 'Other', PortIp TEXT NULL,
    IsDefault INTEGER NOT NULL DEFAULT 0, IsShared INTEGER NOT NULL DEFAULT 0, Location TEXT NULL,
    Status TEXT NOT NULL DEFAULT 'Unknown', ErrorCodes TEXT NULL,
    JobsCount INTEGER NOT NULL DEFAULT 0, JobsInError INTEGER NOT NULL DEFAULT 0, OldestJobMinutes INTEGER NULL,
    AgentCumulativePages INTEGER NULL, PagesLogged INTEGER NOT NULL DEFAULT 0,
    IsPresent INTEGER NOT NULL DEFAULT 1, IsIgnored INTEGER NOT NULL DEFAULT 0,
    FirstSeenAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, LastSeenAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    LastReportAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE UNIQUE INDEX IF NOT EXISTS IX_OfficeAgentPrinters_AgentId_PrinterName ON OfficeAgentPrinters (AgentId, PrinterName);
CREATE INDEX IF NOT EXISTS IX_OfficeAgentPrinters_SystemInfoId ON OfficeAgentPrinters (SystemInfoId);
CREATE INDEX IF NOT EXISTS IX_OfficeMachines_AgentPrinterId ON OfficeMachines (AgentPrinterId);
CREATE INDEX IF NOT EXISTS IX_OfficeMachines_CompanyId ON OfficeMachines (CompanyId);
CREATE INDEX IF NOT EXISTS IX_OfficeMachines_AssignedUserId ON OfficeMachines (AssignedUserId);
");
    }
}
