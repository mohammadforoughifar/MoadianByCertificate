using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>ساخت جدول دسترسی چندشرکتی بدون نیاز به migration دستی در استقرارهای قدیمی.</summary>
public static class CompanyAccessSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS UserCompanyAccesses (UserId INTEGER NOT NULL, CompanyId INTEGER NOT NULL, IsDefault INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, PRIMARY KEY (UserId, CompanyId));");
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE WorkOrders ADD COLUMN CompanyId INTEGER NULL;"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE MeetingMinutes ADD COLUMN CompanyId INTEGER NULL;"); } catch { }
            return;
        }
        if (!db.Database.IsSqlServer()) return;
        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.UserCompanyAccesses', N'U') IS NULL
BEGIN
    CREATE TABLE [UserCompanyAccesses](
        [UserId] int NOT NULL,
        [CompanyId] int NOT NULL,
        [IsDefault] bit NOT NULL CONSTRAINT [DF_UserCompanyAccesses_IsDefault] DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_UserCompanyAccesses_CreatedAt] DEFAULT(GETDATE()),
        CONSTRAINT [PK_UserCompanyAccesses] PRIMARY KEY ([UserId], [CompanyId])
    );
    CREATE INDEX [IX_UserCompanyAccesses_CompanyId] ON [UserCompanyAccesses]([CompanyId]);
END

IF OBJECT_ID(N'dbo.WorkOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkOrders', N'CompanyId') IS NULL ALTER TABLE [WorkOrders] ADD [CompanyId] int NULL;
IF OBJECT_ID(N'dbo.MeetingMinutes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MeetingMinutes', N'CompanyId') IS NULL ALTER TABLE [MeetingMinutes] ADD [CompanyId] int NULL;
");
    }
}
