using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// خودتعمیرِ اسکیمای «ثبت‌کنندهٔ طرف حساب» — نسخه ۱.
/// <para>
/// ستون‌های <c>RegisteredBy*</c> بعد از ساخت اولیهٔ جدول <c>Parties</c> اضافه شده‌اند؛
/// مایگریشن EF برای دیتابیس‌های موجود اجرا نمی‌شود، پس اینجا idempotent اضافه می‌شوند.
/// همهٔ ستون‌ها nullable هستند و به دادهٔ فعلی کاری ندارند.
/// </para>
/// </summary>
public static class PartyRegistrationSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            if (!await HasColumnAsync(db, "Parties", "RegisteredByUserId"))
                await SafeAsync(db, "ALTER TABLE Parties ADD COLUMN RegisteredByUserId INTEGER NULL;");
            if (!await HasColumnAsync(db, "Parties", "RegisteredByName"))
                await SafeAsync(db, "ALTER TABLE Parties ADD COLUMN RegisteredByName TEXT NULL;");
            if (!await HasColumnAsync(db, "Parties", "RegisteredByReferrerId"))
                await SafeAsync(db, "ALTER TABLE Parties ADD COLUMN RegisteredByReferrerId INTEGER NULL;");
            if (!await HasColumnAsync(db, "Parties", "RegisteredAt"))
                await SafeAsync(db, "ALTER TABLE Parties ADD COLUMN RegisteredAt TEXT NULL;");
            return;
        }

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Parties', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Parties','RegisteredByUserId') IS NULL
        ALTER TABLE [dbo].[Parties] ADD [RegisteredByUserId] int NULL;
    IF COL_LENGTH('dbo.Parties','RegisteredByName') IS NULL
        ALTER TABLE [dbo].[Parties] ADD [RegisteredByName] nvarchar(150) NULL;
    IF COL_LENGTH('dbo.Parties','RegisteredByReferrerId') IS NULL
        ALTER TABLE [dbo].[Parties] ADD [RegisteredByReferrerId] int NULL;
    IF COL_LENGTH('dbo.Parties','RegisteredAt') IS NULL
        ALTER TABLE [dbo].[Parties] ADD [RegisteredAt] datetime2 NULL;
END");
    }

    private static async Task<bool> HasColumnAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch
        {
            return true;   // اگر نسنجیدیم، دست نمی‌زنیم
        }
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex) { Console.WriteLine($"[DB] PartyRegistration: {ex.Message}"); }
    }
}
