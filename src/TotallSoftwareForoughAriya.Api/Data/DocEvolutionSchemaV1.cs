using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Data;

/// <summary>Additive, idempotent upgrade. Failure propagates so startup never silently serves a partial security schema.</summary>
public static class DocEvolutionSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        var sqlite = db.Database.IsSqlite();
        var tables = new Dictionary<string, string>
        {
            ["DocTemporaryGrants"] = "Id IDENTITYKEY, DocumentId INT NOT NULL, UserId INT NOT NULL, CanDownload BOOL NOT NULL, ExpiresAtUtc DATE NOT NULL, RevokedAtUtc DATE NULL, GrantedByUserId INT NOT NULL, CreatedAtUtc DATE NOT NULL",
            ["DocRenewalPolicies"] = "DocumentId INT NOT NULL PRIMARY KEY, Enabled BOOL NOT NULL, AssigneeUserId INT NOT NULL, LeadDays INT NOT NULL, OwnerUserId INT NOT NULL",
            ["DocRenewalRuns"] = "Id IDENTITYKEY, DocumentId INT NOT NULL, ExpiryDate DATE NOT NULL, WorkOrderId INT NOT NULL, CreatedAtUtc DATE NOT NULL",
            ["DocIndexJobs"] = "AttachmentId INT NOT NULL PRIMARY KEY, Status NVARCHAR(20) NOT NULL, Generation INT NOT NULL, Attempts INT NOT NULL, NextAttemptAtUtc DATE NOT NULL, LeaseUntilUtc DATE NULL, LeaseToken NVARCHAR(32) NULL, Error NVARCHAR(500) NULL, UpdatedAtUtc DATE NOT NULL"
        };
        foreach (var (name, columns) in tables)
        {
            var ddl = columns.Replace("IDENTITYKEY", sqlite ? "INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT" : "INT NOT NULL IDENTITY PRIMARY KEY")
                .Replace("BOOL", sqlite ? "INTEGER" : "BIT").Replace("DATE", sqlite ? "TEXT" : "datetime2");
            await db.Database.ExecuteSqlRawAsync(sqlite ? $"CREATE TABLE IF NOT EXISTS [{name}] ({ddl});" : $"IF OBJECT_ID(N'dbo.{name}',N'U') IS NULL CREATE TABLE [{name}] ({ddl});");
        }
        foreach (var (table, cols, unique) in new[] {
            ("DocTemporaryGrants", "DocumentId, UserId", true), ("DocTemporaryGrants", "UserId, ExpiresAtUtc", false),
            ("DocRenewalRuns", "DocumentId, ExpiryDate", true), ("DocIndexJobs", "Status, NextAttemptAtUtc", false) })
        {
            var name = "IX_" + table + "_" + cols.Replace(", ", "_");
            var sql = $"CREATE {(unique ? "UNIQUE " : "")}INDEX {(sqlite ? "IF NOT EXISTS " : "")}[{name}] ON [{table}] ({cols});";
            if (!sqlite) sql = $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'{name}' AND object_id=OBJECT_ID(N'dbo.{table}')) " + sql;
            await db.Database.ExecuteSqlRawAsync(sql);
        }
    }
}
