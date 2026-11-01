using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// =====================================================================
// اسکیمای گاوصندوق رمزهای تجهیزات (ItCredentials + ItCredentialLogs)
// بدون نیاز به migration — idempotent روی SQL Server و SQLite.
// =====================================================================
public static class ItVaultSchemaV1
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ItCredential>().HasIndex(c => new { c.CompanyId, c.Category });
        model.Entity<ItCredentialLog>().HasIndex(l => new { l.CredentialId, l.At });
        model.Entity<ItCredentialLog>().HasIndex(l => new { l.CompanyId, l.At });
    }

    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.IsSqlite())
            {
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ItCredentials (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NOT NULL,
    Category TEXT NOT NULL DEFAULT 'Server',
    Title TEXT NOT NULL DEFAULT '',
    Host TEXT NULL, Port INTEGER NULL, AccessMethod TEXT NULL, Url TEXT NULL, Username TEXT NULL,
    PasswordCipher TEXT NULL, ExtraSecretCipher TEXT NULL, KeyId TEXT NULL,
    Location TEXT NULL, Notes TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    PasswordChangedAt TEXT NULL,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CreatedByUserId INTEGER NULL, UpdatedAt TEXT NULL, UpdatedByUserId INTEGER NULL, UpdatedByName TEXT NULL
);");
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ItCredentialLogs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CredentialId INTEGER NOT NULL, CompanyId INTEGER NOT NULL, UserId INTEGER NOT NULL,
    UserName TEXT NULL, Action TEXT NOT NULL DEFAULT '', ClientIp TEXT NULL, Details TEXT NULL,
    At TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ItCredentials_CompanyId_Category ON ItCredentials (CompanyId, Category);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ItCredentialLogs_CredentialId_At ON ItCredentialLogs (CredentialId, At);");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ItCredentialLogs_CompanyId_At ON ItCredentialLogs (CompanyId, At);");
                return;
            }

            if (!db.Database.IsSqlServer()) return;
            await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.ItCredentials', N'U') IS NULL
BEGIN
    CREATE TABLE [ItCredentials](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ItCredentials] PRIMARY KEY,
        [CompanyId] int NOT NULL,
        [Category] nvarchar(30) NOT NULL CONSTRAINT [DF_ItCredentials_Category] DEFAULT(N'Server'),
        [Title] nvarchar(200) NOT NULL CONSTRAINT [DF_ItCredentials_Title] DEFAULT(N''),
        [Host] nvarchar(200) NULL,
        [Port] int NULL,
        [AccessMethod] nvarchar(30) NULL,
        [Url] nvarchar(500) NULL,
        [Username] nvarchar(200) NULL,
        [PasswordCipher] nvarchar(max) NULL,
        [ExtraSecretCipher] nvarchar(max) NULL,
        [KeyId] nvarchar(32) NULL,
        [Location] nvarchar(250) NULL,
        [Notes] nvarchar(2000) NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_ItCredentials_IsActive] DEFAULT(1),
        [PasswordChangedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ItCredentials_CreatedAt] DEFAULT(GETDATE()),
        [CreatedByUserId] int NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedByUserId] int NULL,
        [UpdatedByName] nvarchar(200) NULL
    );
    CREATE INDEX [IX_ItCredentials_CompanyId_Category] ON [ItCredentials]([CompanyId], [Category]);
END

IF OBJECT_ID(N'dbo.ItCredentialLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [ItCredentialLogs](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ItCredentialLogs] PRIMARY KEY,
        [CredentialId] int NOT NULL,
        [CompanyId] int NOT NULL,
        [UserId] int NOT NULL,
        [UserName] nvarchar(200) NULL,
        [Action] nvarchar(30) NOT NULL CONSTRAINT [DF_ItCredentialLogs_Action] DEFAULT(N''),
        [ClientIp] nvarchar(64) NULL,
        [Details] nvarchar(500) NULL,
        [At] datetime2 NOT NULL CONSTRAINT [DF_ItCredentialLogs_At] DEFAULT(GETDATE())
    );
    CREATE INDEX [IX_ItCredentialLogs_CredentialId_At] ON [ItCredentialLogs]([CredentialId], [At]);
    CREATE INDEX [IX_ItCredentialLogs_CompanyId_At] ON [ItCredentialLogs]([CompanyId], [At]);
END
");
            Console.WriteLine("[DB] ✔ جدول‌های گاوصندوق رمزهای تجهیزات (ItCredentials) آماده‌اند.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ⚠ ساخت جدول‌های گاوصندوق رمزها ناموفق بود: {ex.Message}");
        }
    }
}
