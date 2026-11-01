using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>افزودن ستون اختیاری نگهداری MindFlow هر پروژه؛ idempotent برای SQL Server و SQLite.</summary>
public static class WorkProjectMindFlowSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE WorkProjects ADD COLUMN MindFlowJson TEXT NULL;"); }
            catch (Exception ex) when ((ex.Message ?? "").Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
            return;
        }

        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.WorkProjects', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.WorkProjects', N'MindFlowJson') IS NULL
    ALTER TABLE dbo.WorkProjects ADD MindFlowJson nvarchar(max) NULL;");
    }
}
