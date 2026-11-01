using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای امضاکنندگان نامه صادره — نسخه ۱
//  افزودن ستون‌های IsRead / DateRead به جدول OutgoingLetterSigners
//  تا «خوانده‌شدن» از «امضا کردن» جدا شود.
// ============================================================
public static class OutgoingSignerReadSchemaV1
{
    private const string Table = "OutgoingLetterSigners";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.{Table}', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.{Table}', N'IsRead') IS NULL
    ALTER TABLE dbo.{Table} ADD IsRead bit NOT NULL CONSTRAINT DF_{Table}_IsRead DEFAULT 0;
IF OBJECT_ID(N'dbo.{Table}', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.{Table}', N'DateRead') IS NULL
    ALTER TABLE dbo.{Table} ADD DateRead datetime2 NULL;");
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, $"ALTER TABLE {Table} ADD COLUMN IsRead INTEGER NOT NULL DEFAULT 0;");
        await SafeAsync(db, $"ALTER TABLE {Table} ADD COLUMN DateRead TEXT NULL;");
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] OutgoingSignerReadSchemaV1: {m}");
        }
    }
}
