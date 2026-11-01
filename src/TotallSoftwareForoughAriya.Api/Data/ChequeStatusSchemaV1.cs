using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// افزودن ستون‌های Status، BouncedAt و BounceReason به جدول Cheques برای پشتیبانی کامل از ثبت چک برگشتی و چرخه وضعیت چک در سامانه فروشگاهی.
/// </summary>
public static class ChequeStatusSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Cheques ADD COLUMN Status INTEGER NOT NULL DEFAULT 0;");
            }
            catch { /* ستون وجود دارد */ }

            try
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Cheques ADD COLUMN BouncedAt TEXT NULL;");
            }
            catch { /* ستون وجود دارد */ }

            try
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Cheques ADD COLUMN BounceReason TEXT NULL;");
            }
            catch { /* ستون وجود دارد */ }

            // تنظیم داده‌های قبلی: چک‌هایی که IsCleared=true بوده را به Cleared (2) تبدیل می‌کنیم
            try
            {
                await db.Database.ExecuteSqlRawAsync("UPDATE Cheques SET Status = 2 WHERE IsCleared = 1 AND (Status = 0 OR Status IS NULL);");
            }
            catch { }
        }
        else
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.Cheques', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Cheques', N'Status') IS NULL
        ALTER TABLE [dbo].[Cheques] ADD [Status] int NOT NULL DEFAULT 0;

    IF COL_LENGTH(N'dbo.Cheques', N'BouncedAt') IS NULL
        ALTER TABLE [dbo].[Cheques] ADD [BouncedAt] datetime2 NULL;

    IF COL_LENGTH(N'dbo.Cheques', N'BounceReason') IS NULL
        ALTER TABLE [dbo].[Cheques] ADD [BounceReason] nvarchar(200) NULL;

    EXEC(N'UPDATE [dbo].[Cheques] SET [Status] = 2 WHERE [IsCleared] = 1 AND ([Status] = 0 OR [Status] IS NULL);');
END;");
            }
            catch { /* نادیده گرفتن خطا */ }
        }
    }
}
