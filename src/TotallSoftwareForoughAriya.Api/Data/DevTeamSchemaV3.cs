using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  میز کار توسعه — نسخه ۳: ساب‌تسک + وابستگی
//  • DtTasks.ParentTaskId / SortOrder
//  • جدول DtTaskDependencies
// ============================================================
public static class DevTeamSchemaV3
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTasks', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.DtTasks', N'ParentTaskId') IS NULL
    ALTER TABLE dbo.DtTasks ADD ParentTaskId int NULL;
IF OBJECT_ID(N'dbo.DtTasks', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.DtTasks', N'SortOrder') IS NULL
    ALTER TABLE dbo.DtTasks ADD SortOrder int NOT NULL CONSTRAINT DF_DtTasks_SortOrder DEFAULT(0);");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTasks', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DtTasks_ParentTaskId' AND object_id = OBJECT_ID(N'dbo.DtTasks'))
    CREATE INDEX IX_DtTasks_ParentTaskId ON dbo.DtTasks(ParentTaskId);");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.DtTaskDependencies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DtTaskDependencies (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DtTaskDependencies PRIMARY KEY,
        TaskId int NOT NULL,
        DependsOnTaskId int NOT NULL,
        Kind nvarchar(20) NOT NULL,
        CreatedByUserId int NOT NULL,
        CreatedByName nvarchar(150) NOT NULL,
        CreatedAt datetime2 NOT NULL
    );
    CREATE UNIQUE INDEX IX_DtTaskDependencies_Pair ON dbo.DtTaskDependencies(TaskId, DependsOnTaskId);
    CREATE INDEX IX_DtTaskDependencies_DependsOn ON dbo.DtTaskDependencies(DependsOnTaskId);
END;");
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, "ALTER TABLE DtTasks ADD COLUMN ParentTaskId INTEGER NULL;");
        await SafeAsync(db, "ALTER TABLE DtTasks ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTasks_ParentTaskId ON DtTasks(ParentTaskId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS DtTaskDependencies (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER NOT NULL,
    DependsOnTaskId INTEGER NOT NULL,
    Kind TEXT NOT NULL,
    CreatedByUserId INTEGER NOT NULL,
    CreatedByName TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_DtTaskDependencies_Pair ON DtTaskDependencies(TaskId, DependsOnTaskId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_DtTaskDependencies_DependsOn ON DtTaskDependencies(DependsOnTaskId);");
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
                Console.WriteLine($"[DB] DevTeamSchemaV3: {m}");
        }
    }
}
