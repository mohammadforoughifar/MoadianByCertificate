using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ================== اسکیمای «لاگ چاپ» آرشیو اسناد (نسخه ۱) ==================
/// جدول DocumentPrintLogs را در هر دو دیتابیس (Sqlite/SQL Server) به‌صورت
/// خودترمیم و Idempotent می‌سازد؛ دقیقاً با همان سبک DocArchiveSecuritySchemaV1/V2.
/// در DbInitializer بعد از V2 صدا زده می‌شود.
/// </summary>
public static class DocPrintSchemaV1
{
    private const string Table = "DocumentPrintLogs";

    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            var isSqlite = string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.OrdinalIgnoreCase);

            var createSql = isSqlite ? $@"
CREATE TABLE IF NOT EXISTS {Table} (
    Id INTEGER NOT NULL CONSTRAINT PK_{Table} PRIMARY KEY AUTOINCREMENT,
    DocumentId INTEGER NOT NULL,
    VersionId INTEGER NOT NULL,
    AttachmentId INTEGER NOT NULL,
    FileName TEXT NOT NULL,
    ContentType TEXT NULL,
    PrintedByUserId INTEGER NOT NULL,
    PrintedByName TEXT NOT NULL,
    PrintedAt TEXT NOT NULL,
    WatermarkText TEXT NOT NULL,
    PageCount INTEGER NULL,
    FileSizeBytes INTEGER NOT NULL,
    IpAddress TEXT NULL,
    Source TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_{Table}_DocumentId ON {Table} (DocumentId);
CREATE INDEX IF NOT EXISTS IX_{Table}_PrintedAt ON {Table} (PrintedAt);
" : $@"
IF OBJECT_ID(N'[dbo].[{Table}]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[{Table}] (
        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_{Table}] PRIMARY KEY,
        [DocumentId] INT NOT NULL,
        [VersionId] INT NOT NULL,
        [AttachmentId] INT NOT NULL,
        [FileName] NVARCHAR(250) NOT NULL,
        [ContentType] NVARCHAR(150) NULL,
        [PrintedByUserId] INT NOT NULL,
        [PrintedByName] NVARCHAR(150) NOT NULL,
        [PrintedAt] datetime2 NOT NULL,
        [WatermarkText] NVARCHAR(300) NOT NULL,
        [PageCount] INT NULL,
        [FileSizeBytes] BIGINT NOT NULL,
        [IpAddress] NVARCHAR(60) NULL,
        [Source] NVARCHAR(30) NOT NULL
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{Table}_DocumentId' AND object_id = OBJECT_ID(N'[dbo].[{Table}]'))
    CREATE INDEX [IX_{Table}_DocumentId] ON [dbo].[{Table}] ([DocumentId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{Table}_PrintedAt' AND object_id = OBJECT_ID(N'[dbo].[{Table}]'))
    CREATE INDEX [IX_{Table}_PrintedAt] ON [dbo].[{Table}] ([PrintedAt]);
";

            await db.Database.ExecuteSqlRawAsync(createSql);
        }
        catch
        {
            // خودترمیم نباید بوت را بشکند
        }
    }
}
