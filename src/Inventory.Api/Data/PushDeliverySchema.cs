using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Data;

public class PushDelivery
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    public int UserId { get; set; }
    [MaxLength(64)] public string Tag { get; set; } = "";
    [MaxLength(120)] public string Title { get; set; } = "";
    [MaxLength(300)] public string Body { get; set; } = "";
    [MaxLength(300)] public string Link { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    [MaxLength(32)] public string? LeaseToken { get; set; }
    [MaxLength(100)] public string? ErrorCode { get; set; }
}

public static class PushDeliverySchema
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PushDelivery>().HasIndex(j => new { j.Status, j.NextAttemptAtUtc });
        model.Entity<PushDelivery>().HasIndex(j => new { j.SubscriptionId, j.Tag }).IsUnique();
        model.Entity<PushSubscription>().HasIndex(s => s.Endpoint).IsUnique();
    }
    public static async Task EnsureAsync(AppDbContext db)
    {
        var sqlite = db.Database.IsSqlite();
        var subscriptionColumns = "Id IDKEY, UserId INT NOT NULL, Endpoint NVARCHAR(500) NOT NULL, P256DH NVARCHAR(200) NOT NULL, Auth NVARCHAR(100) NOT NULL, UserAgent NVARCHAR(250) NULL, CreatedAt DT NOT NULL, LastSeenAt DT NOT NULL"
            .Replace("IDKEY", sqlite ? "INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT" : "INT NOT NULL IDENTITY PRIMARY KEY").Replace("DT", sqlite ? "TEXT" : "datetime2");
        await db.Database.ExecuteSqlRawAsync(sqlite ? $"CREATE TABLE IF NOT EXISTS PushSubscriptions ({subscriptionColumns})" : $"IF OBJECT_ID(N'dbo.PushSubscriptions',N'U') IS NULL CREATE TABLE PushSubscriptions ({subscriptionColumns})");
        var columns = "Id IDKEY, SubscriptionId INT NOT NULL, UserId INT NOT NULL, Tag NVARCHAR(64) NOT NULL, Title NVARCHAR(120) NOT NULL, Body NVARCHAR(300) NOT NULL, Link NVARCHAR(300) NOT NULL, Status NVARCHAR(20) NOT NULL, Attempts INT NOT NULL, CreatedAtUtc DT NOT NULL, ExpiresAtUtc DT NOT NULL, NextAttemptAtUtc DT NOT NULL, LeaseUntilUtc DT NULL, LeaseToken NVARCHAR(32) NULL, ErrorCode NVARCHAR(100) NULL";
        columns = columns.Replace("IDKEY", sqlite ? "INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT" : "INT NOT NULL IDENTITY PRIMARY KEY").Replace("DT", sqlite ? "TEXT" : "datetime2");
        await db.Database.ExecuteSqlRawAsync(sqlite ? $"CREATE TABLE IF NOT EXISTS PushDeliveries ({columns})" : $"IF OBJECT_ID(N'dbo.PushDeliveries',N'U') IS NULL CREATE TABLE PushDeliveries ({columns})");
        // Endpoint is a browser capability, never shared by two accounts. Retain newest legacy registration.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM PushSubscriptions WHERE Id IN (SELECT Id FROM (SELECT Id, ROW_NUMBER() OVER (PARTITION BY Endpoint ORDER BY LastSeenAt DESC, Id DESC) AS rn FROM PushSubscriptions) d WHERE rn > 1)");
        foreach (var (table, cols, unique) in new[] { ("PushDeliveries", "Status, NextAttemptAtUtc", false), ("PushDeliveries", "SubscriptionId, Tag", true), ("PushSubscriptions", "Endpoint", true) })
        {
            var name = "IX_" + table + "_" + cols.Replace(", ", "_");
            var sql = $"CREATE {(unique ? "UNIQUE " : "")}INDEX {(sqlite ? "IF NOT EXISTS " : "")}[{name}] ON [{table}] ({cols});";
            if (!sqlite) sql = $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'{name}' AND object_id=OBJECT_ID(N'dbo.{table}')) " + sql;
            await db.Database.ExecuteSqlRawAsync(sql);
        }
    }
}
