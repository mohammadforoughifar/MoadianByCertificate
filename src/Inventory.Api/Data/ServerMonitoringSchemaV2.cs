using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ارتقای فاز دوم مدیریت سرور: پایش زمان‌بندی‌شده و هشدارهای قابل تأیید.
/// V1 فقط شناسنامه و دریافت دستی Redfish را ایجاد می‌کرد؛ این لایه روی دیتابیس‌های
/// نصب‌شدهٔ قبلی نیز ستون‌ها و جدول جدید را به‌صورت idempotent اضافه می‌کند.
/// </summary>
public static class ServerMonitoringSchemaV2
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ManagedServer>().HasIndex(s => new { s.MonitoringEnabled, s.LastAutomaticPollAt });
        model.Entity<ServerAlert>().HasIndex(a => new { a.ManagedServerId, a.Status });
        model.Entity<ServerAlert>().HasIndex(a => new { a.Status, a.Severity, a.LastSeenAt });
    }

    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.IsSqlite())
            {
                // SQLite دستور IF NOT EXISTS برای ADD COLUMN ندارد؛ خطای ستون موجود عمداً نادیده گرفته می‌شود.
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN MonitoringEnabled INTEGER NOT NULL DEFAULT 0;");
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN MonitoringIntervalMinutes INTEGER NOT NULL DEFAULT 15;");
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN LastAutomaticPollAt TEXT NULL;");
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ServerAlerts (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ManagedServerId INTEGER NOT NULL, Category TEXT NOT NULL DEFAULT '', Severity TEXT NOT NULL DEFAULT 'Warning',
    Message TEXT NOT NULL DEFAULT '', Status TEXT NOT NULL DEFAULT 'Open',
    OpenedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, LastSeenAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    AcknowledgedAt TEXT NULL, AcknowledgedByUserId INTEGER NULL, AcknowledgedByName TEXT NULL, ResolvedAt TEXT NULL
);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ManagedServers_MonitoringEnabled_LastAutomaticPollAt ON ManagedServers (MonitoringEnabled, LastAutomaticPollAt);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ServerAlerts_ManagedServerId_Status ON ServerAlerts (ManagedServerId, Status);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ServerAlerts_Status_Severity_LastSeenAt ON ServerAlerts (Status, Severity, LastSeenAt);");
                return;
            }

            if (!db.Database.IsSqlServer()) return;
            await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.ManagedServers', N'MonitoringEnabled') IS NULL
    ALTER TABLE [ManagedServers] ADD [MonitoringEnabled] bit NOT NULL CONSTRAINT [DF_ManagedServers_MonitoringEnabled] DEFAULT(0);
IF COL_LENGTH(N'dbo.ManagedServers', N'MonitoringIntervalMinutes') IS NULL
    ALTER TABLE [ManagedServers] ADD [MonitoringIntervalMinutes] int NOT NULL CONSTRAINT [DF_ManagedServers_MonitoringIntervalMinutes] DEFAULT(15);
IF COL_LENGTH(N'dbo.ManagedServers', N'LastAutomaticPollAt') IS NULL
    ALTER TABLE [ManagedServers] ADD [LastAutomaticPollAt] datetime2 NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ManagedServers_MonitoringEnabled_LastAutomaticPollAt' AND object_id = OBJECT_ID(N'dbo.ManagedServers'))
    CREATE INDEX [IX_ManagedServers_MonitoringEnabled_LastAutomaticPollAt] ON [ManagedServers]([MonitoringEnabled], [LastAutomaticPollAt]);

IF OBJECT_ID(N'dbo.ServerAlerts', N'U') IS NULL
BEGIN
    CREATE TABLE [ServerAlerts](
        [Id] bigint NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ServerAlerts] PRIMARY KEY,
        [ManagedServerId] int NOT NULL, [Category] nvarchar(40) NOT NULL CONSTRAINT [DF_ServerAlerts_Category] DEFAULT(N''),
        [Severity] nvarchar(20) NOT NULL CONSTRAINT [DF_ServerAlerts_Severity] DEFAULT(N'Warning'),
        [Message] nvarchar(1000) NOT NULL CONSTRAINT [DF_ServerAlerts_Message] DEFAULT(N''),
        [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_ServerAlerts_Status] DEFAULT(N'Open'),
        [OpenedAt] datetime2 NOT NULL CONSTRAINT [DF_ServerAlerts_OpenedAt] DEFAULT(GETDATE()),
        [LastSeenAt] datetime2 NOT NULL CONSTRAINT [DF_ServerAlerts_LastSeenAt] DEFAULT(GETDATE()),
        [AcknowledgedAt] datetime2 NULL, [AcknowledgedByUserId] int NULL,
        [AcknowledgedByName] nvarchar(200) NULL, [ResolvedAt] datetime2 NULL
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServerAlerts_ManagedServerId_Status' AND object_id = OBJECT_ID(N'dbo.ServerAlerts'))
    CREATE INDEX [IX_ServerAlerts_ManagedServerId_Status] ON [ServerAlerts]([ManagedServerId], [Status]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServerAlerts_Status_Severity_LastSeenAt' AND object_id = OBJECT_ID(N'dbo.ServerAlerts'))
    CREATE INDEX [IX_ServerAlerts_Status_Severity_LastSeenAt] ON [ServerAlerts]([Status], [Severity], [LastSeenAt]);
");
            Console.WriteLine("[DB] ✔ ستون‌های پایش و جدول هشدارهای سرور (ServerAlerts) آماده‌اند.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ⚠ ارتقای پایش سرورها ناموفق بود: {ex.Message}");
        }
    }

    private static async Task TrySqliteAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch { /* ستون از قبل وجود دارد یا دیتابیس تازه آن را با مدل ساخته است. */ }
    }
}
