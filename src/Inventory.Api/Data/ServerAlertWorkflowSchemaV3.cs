using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// فاز سوم مدیریت سرور: مسئول هشدار، بازهٔ تعمیرات، جلوگیری از اعلان تکراری و اتصال دستی دستور کار.
/// همهٔ تغییرات برای SQLite و SQL Server تکرارپذیر هستند و روی نصب‌های قبلی اعمال می‌شوند.
/// </summary>
public static class ServerAlertWorkflowSchemaV3
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ManagedServer>().HasIndex(s => new { s.ResponsibleUserId, s.AlertNotificationsEnabled });
        model.Entity<ManagedServer>().HasIndex(s => s.MaintenanceUntil);
        model.Entity<ServerAlert>().HasIndex(a => new { a.ManagedServerId, a.LastNotifiedAt });
        model.Entity<ServerAlert>().HasIndex(a => a.WorkOrderId);
    }

    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.IsSqlite())
            {
                // SQLite از ADD COLUMN IF NOT EXISTS پشتیبانی نمی‌کند.
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN ResponsibleUserId INTEGER NULL;");
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN AlertNotificationsEnabled INTEGER NOT NULL DEFAULT 0;");
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN MaintenanceUntil TEXT NULL;");
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN MaintenanceNote TEXT NULL;");
                await TrySqliteAsync(db, "ALTER TABLE ServerAlerts ADD COLUMN LastNotifiedAt TEXT NULL;");
                await TrySqliteAsync(db, "ALTER TABLE ServerAlerts ADD COLUMN NotificationCount INTEGER NOT NULL DEFAULT 0;");
                await TrySqliteAsync(db, "ALTER TABLE ServerAlerts ADD COLUMN WorkOrderId INTEGER NULL;");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ManagedServers_ResponsibleUserId_AlertNotificationsEnabled ON ManagedServers (ResponsibleUserId, AlertNotificationsEnabled);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ManagedServers_MaintenanceUntil ON ManagedServers (MaintenanceUntil);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ServerAlerts_ManagedServerId_LastNotifiedAt ON ServerAlerts (ManagedServerId, LastNotifiedAt);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ServerAlerts_WorkOrderId ON ServerAlerts (WorkOrderId);");
                return;
            }

            if (!db.Database.IsSqlServer()) return;
            await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.ManagedServers', N'ResponsibleUserId') IS NULL
    ALTER TABLE [ManagedServers] ADD [ResponsibleUserId] int NULL;
IF COL_LENGTH(N'dbo.ManagedServers', N'AlertNotificationsEnabled') IS NULL
    ALTER TABLE [ManagedServers] ADD [AlertNotificationsEnabled] bit NOT NULL CONSTRAINT [DF_ManagedServers_AlertNotificationsEnabled] DEFAULT(0);
IF COL_LENGTH(N'dbo.ManagedServers', N'MaintenanceUntil') IS NULL
    ALTER TABLE [ManagedServers] ADD [MaintenanceUntil] datetime2 NULL;
IF COL_LENGTH(N'dbo.ManagedServers', N'MaintenanceNote') IS NULL
    ALTER TABLE [ManagedServers] ADD [MaintenanceNote] nvarchar(500) NULL;
IF COL_LENGTH(N'dbo.ServerAlerts', N'LastNotifiedAt') IS NULL
    ALTER TABLE [ServerAlerts] ADD [LastNotifiedAt] datetime2 NULL;
IF COL_LENGTH(N'dbo.ServerAlerts', N'NotificationCount') IS NULL
    ALTER TABLE [ServerAlerts] ADD [NotificationCount] int NOT NULL CONSTRAINT [DF_ServerAlerts_NotificationCount] DEFAULT(0);
IF COL_LENGTH(N'dbo.ServerAlerts', N'WorkOrderId') IS NULL
    ALTER TABLE [ServerAlerts] ADD [WorkOrderId] int NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ManagedServers_ResponsibleUserId_AlertNotificationsEnabled' AND object_id = OBJECT_ID(N'dbo.ManagedServers'))
    CREATE INDEX [IX_ManagedServers_ResponsibleUserId_AlertNotificationsEnabled] ON [ManagedServers]([ResponsibleUserId], [AlertNotificationsEnabled]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ManagedServers_MaintenanceUntil' AND object_id = OBJECT_ID(N'dbo.ManagedServers'))
    CREATE INDEX [IX_ManagedServers_MaintenanceUntil] ON [ManagedServers]([MaintenanceUntil]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServerAlerts_ManagedServerId_LastNotifiedAt' AND object_id = OBJECT_ID(N'dbo.ServerAlerts'))
    CREATE INDEX [IX_ServerAlerts_ManagedServerId_LastNotifiedAt] ON [ServerAlerts]([ManagedServerId], [LastNotifiedAt]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServerAlerts_WorkOrderId' AND object_id = OBJECT_ID(N'dbo.ServerAlerts'))
    CREATE INDEX [IX_ServerAlerts_WorkOrderId] ON [ServerAlerts]([WorkOrderId]);
");
            Console.WriteLine("[DB] ✔ مسئول هشدار، بازهٔ تعمیرات و کنترل اعلان‌های سرور آماده‌اند.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ⚠ ارتقای گردش‌کار هشدارهای سرور ناموفق بود: {ex.Message}");
        }
    }

    private static async Task TrySqliteAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch { /* ستون از قبل وجود دارد یا مدل EF آن را در دیتابیس تازه ساخته است. */ }
    }
}
