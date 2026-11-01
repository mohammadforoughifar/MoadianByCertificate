using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Idempotent upgrade for existing SQLite and SQL Server databases. Fail visibly on errors.</summary>
// DDL identifiers and types below are compile-time allowlisted constants, never user input.
#pragma warning disable EF1002
public static class WorkOrderSchemaV2
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        var columns = new[]
        {
            ("RecurrenceSeriesId", "TEXT NULL", "nvarchar(32) NULL"),
            ("RecurrenceScheduledAt", "TEXT NULL", "datetime2 NULL"),
            ("DeletedAt", "TEXT NULL", "datetime2 NULL"),
            ("DeletedByUserId", "INTEGER NULL", "int NULL")
        };
        foreach (var (name, sqlite, sqlServer) in columns)
        {
            if (db.Database.IsSqlite())
            {
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('WorkOrders') WHERE name='{name}'";
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0)
                    await db.Database.ExecuteSqlRawAsync($"ALTER TABLE WorkOrders ADD COLUMN {name} {sqlite};");
            }
            else
                await db.Database.ExecuteSqlRawAsync($"IF COL_LENGTH(N'dbo.WorkOrders', N'{name}') IS NULL ALTER TABLE dbo.WorkOrders ADD [{name}] {sqlServer};");
        }
        const string index = "IX_WorkOrders_RecurrenceSeriesId_RecurrenceScheduledAt";
        var sql = db.Database.IsSqlite()
            ? $"CREATE UNIQUE INDEX IF NOT EXISTS {index} ON WorkOrders (RecurrenceSeriesId, RecurrenceScheduledAt) WHERE RecurrenceSeriesId IS NOT NULL AND RecurrenceScheduledAt IS NOT NULL;"
            : $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{index}' AND object_id = OBJECT_ID(N'dbo.WorkOrders')) CREATE UNIQUE INDEX [{index}] ON dbo.WorkOrders (RecurrenceSeriesId, RecurrenceScheduledAt) WHERE RecurrenceSeriesId IS NOT NULL AND RecurrenceScheduledAt IS NOT NULL;";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
