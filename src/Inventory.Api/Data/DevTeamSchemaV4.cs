using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  میز کار توسعه — نسخه ۴ (فاز ۳):
//  • تایمر زنده: DtTasks.TimerStartedAt / TimerStartedByUserId
//  • ترتیب کانبان ریشه با SortOrder (از قبل موجود؛ ایندکس ترکیبی)
// ============================================================
public static class DevTeamSchemaV4
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTasks', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.DtTasks', N'TimerStartedAt') IS NULL
    ALTER TABLE dbo.DtTasks ADD TimerStartedAt datetime2 NULL;
IF OBJECT_ID(N'dbo.DtTasks', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.DtTasks', N'TimerStartedByUserId') IS NULL
    ALTER TABLE dbo.DtTasks ADD TimerStartedByUserId int NULL;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTasks', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DtTasks_Status_Sort' AND object_id = OBJECT_ID(N'dbo.DtTasks'))
    CREATE INDEX IX_DtTasks_Status_Sort ON dbo.DtTasks(StatusId, SortOrder);");
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, "ALTER TABLE DtTasks ADD COLUMN TimerStartedAt TEXT NULL;");
        await SafeAsync(db, "ALTER TABLE DtTasks ADD COLUMN TimerStartedByUserId INTEGER NULL;");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTasks_Status_Sort ON DtTasks(StatusId, SortOrder);");
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] DevTeamSchemaV4: {m}");
        }
    }
}
