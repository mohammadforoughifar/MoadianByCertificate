using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// فاز پنجم مدیریت سرور: تأیید گواهی TLS هر iLO با اثر انگشت SHA-256 (برای گواهی‌های خودامضای پیش‌فرض iLO).
/// ارتقا برای SQLite و SQL Server تکرارپذیر است و دادهٔ موجود را تغییر نمی‌دهد.
/// </summary>
public static class ServerIloCertSchemaV5
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.IsSqlite())
            {
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN IloCertThumbprint TEXT NULL;");
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN IloCertSubject TEXT NULL;");
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN IloCertNotAfter TEXT NULL;");
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN IloCertTrustedAt TEXT NULL;");
                await TrySqliteAsync(db, "ALTER TABLE ManagedServers ADD COLUMN IloCertTrustedByName TEXT NULL;");
                return;
            }

            if (!db.Database.IsSqlServer()) return;
            await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.ManagedServers', N'IloCertThumbprint') IS NULL
    ALTER TABLE [ManagedServers] ADD [IloCertThumbprint] nvarchar(64) NULL;
IF COL_LENGTH(N'dbo.ManagedServers', N'IloCertSubject') IS NULL
    ALTER TABLE [ManagedServers] ADD [IloCertSubject] nvarchar(400) NULL;
IF COL_LENGTH(N'dbo.ManagedServers', N'IloCertNotAfter') IS NULL
    ALTER TABLE [ManagedServers] ADD [IloCertNotAfter] datetime2 NULL;
IF COL_LENGTH(N'dbo.ManagedServers', N'IloCertTrustedAt') IS NULL
    ALTER TABLE [ManagedServers] ADD [IloCertTrustedAt] datetime2 NULL;
IF COL_LENGTH(N'dbo.ManagedServers', N'IloCertTrustedByName') IS NULL
    ALTER TABLE [ManagedServers] ADD [IloCertTrustedByName] nvarchar(200) NULL;
");
            Console.WriteLine("[DB] ✔ ستون‌های تأیید گواهی iLO آماده‌اند.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ⚠ ارتقای ستون‌های گواهی iLO ناموفق بود: {ex.Message}");
        }
    }

    private static async Task TrySqliteAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch { /* ستون از قبل وجود دارد یا دیتابیس تازه با مدل EF ساخته شده است. */ }
    }
}
