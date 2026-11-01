using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  میز کار توسعه — نسخه ۲
//  • ستون‌های WorkOrderId / WorkOrderNumber روی DtTasks
// ============================================================
public static class DevTeamSchemaV2
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTasks', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.DtTasks', N'WorkOrderId') IS NULL
    ALTER TABLE dbo.DtTasks ADD WorkOrderId int NULL;
IF OBJECT_ID(N'dbo.DtTasks', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.DtTasks', N'WorkOrderNumber') IS NULL
    ALTER TABLE dbo.DtTasks ADD WorkOrderNumber nvarchar(30) NULL;");
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTasks', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DtTasks_WorkOrderId' AND object_id = OBJECT_ID(N'dbo.DtTasks'))
    CREATE INDEX IX_DtTasks_WorkOrderId ON dbo.DtTasks(WorkOrderId);");
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        // SQLite: ADD COLUMN فقط اگر نباشد — خطا را نادیده می‌گیریم
        await SafeAsync(db, "ALTER TABLE DtTasks ADD COLUMN WorkOrderId INTEGER NULL;");
        await SafeAsync(db, "ALTER TABLE DtTasks ADD COLUMN WorkOrderNumber TEXT NULL;");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTasks_WorkOrderId ON DtTasks(WorkOrderId);");
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
                Console.WriteLine($"[DB] DevTeamSchemaV2: {m}");
        }
    }
}
