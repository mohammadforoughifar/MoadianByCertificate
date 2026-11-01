using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// افزودن ستون TrsAccountId به جدول Transactions و Cheques جهت نگهداری بانک انتخابی در اسناد خرید و فروش.
/// </summary>
public static class TransactionBankSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Transactions ADD COLUMN TrsAccountId INTEGER NULL;");
            }
            catch { /* ستون وجود دارد */ }

            try
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Cheques ADD COLUMN TrsAccountId INTEGER NULL;");
            }
            catch { /* ستون وجود دارد */ }

            try
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Expenses ADD COLUMN TrsAccountId INTEGER NULL;");
            }
            catch { /* ستون وجود دارد */ }

            try
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Expenses ADD COLUMN TrsAccountName TEXT NULL;");
            }
            catch { /* ستون وجود دارد */ }
        }
        else
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Transactions', N'TrsAccountId') IS NULL
BEGIN
    ALTER TABLE [dbo].[Transactions] ADD [TrsAccountId] int NULL;
END;

IF OBJECT_ID(N'dbo.Cheques', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Cheques', N'TrsAccountId') IS NULL
BEGIN
    ALTER TABLE [dbo].[Cheques] ADD [TrsAccountId] int NULL;
END;

IF OBJECT_ID(N'dbo.Expenses', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Expenses', N'TrsAccountId') IS NULL
BEGIN
    ALTER TABLE [dbo].[Expenses] ADD [TrsAccountId] int NULL;
END;

IF OBJECT_ID(N'dbo.Expenses', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Expenses', N'TrsAccountName') IS NULL
BEGIN
    ALTER TABLE [dbo].[Expenses] ADD [TrsAccountName] nvarchar(200) NULL;
END;
");
            }
            catch
            {
                // در صورت وجود خطا در دسترسی، استارتاپ متوقف نشود
            }
        }
    }
}
