using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  میز کار توسعه — نسخه ۵ (مسیر C):
//  • DtWorkflowStatuses.WipLimit  (null/0 = بدون سقف)
//  • جدول DtTaskChecklistItems
// ============================================================
public static class DevTeamSchemaV5
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtWorkflowStatuses', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.DtWorkflowStatuses', N'WipLimit') IS NULL
    ALTER TABLE dbo.DtWorkflowStatuses ADD WipLimit int NULL;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTaskChecklistItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtTaskChecklistItems (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtTaskChecklistItems PRIMARY KEY,
        TaskId int NOT NULL,
        Title nvarchar(300) NOT NULL,
        IsDone bit NOT NULL CONSTRAINT DF_DtTaskChecklist_IsDone DEFAULT(0),
        SortOrder int NOT NULL CONSTRAINT DF_DtTaskChecklist_Sort DEFAULT(0),
        CreatedByUserId int NOT NULL,
        CreatedByName nvarchar(150) NOT NULL,
        CreatedAt datetime2 NOT NULL,
        DoneAt datetime2 NULL,
        DoneByUserId int NULL,
        DoneByName nvarchar(150) NULL
    );
    CREATE INDEX IX_DtTaskChecklistItems_TaskId ON dbo.DtTaskChecklistItems(TaskId);
END;");
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, "ALTER TABLE DtWorkflowStatuses ADD COLUMN WipLimit INTEGER NULL;");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtTaskChecklistItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER NOT NULL,
    Title TEXT NOT NULL,
    IsDone INTEGER NOT NULL DEFAULT 0,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    CreatedByUserId INTEGER NOT NULL,
    CreatedByName TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    DoneAt TEXT NULL,
    DoneByUserId INTEGER NULL,
    DoneByName TEXT NULL
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTaskChecklistItems_TaskId ON DtTaskChecklistItems(TaskId);");
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
                Console.WriteLine($"[DB] DevTeamSchemaV5: {m}");
        }
    }
}
