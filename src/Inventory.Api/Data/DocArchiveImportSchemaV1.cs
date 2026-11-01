using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ساخت خودتعمیر جدول‌های ورود انبوه، برای SQL Server و SQLite.
/// مثل بقیهٔ کلاس‌های Schema از DbInitializer صدا زده می‌شود.
///
/// نکته دربارهٔ نسخه‌های قدیمی: اگر قبلاً جدول‌های DocImportJobs و DocImportItems
/// ساخته شده باشند، دست‌نخورده می‌مانند (حذف خودکار داده خطرناک است). این کلاس فقط
/// جدول‌های جدید را می‌سازد.
/// </summary>
public static class DocArchiveImportSchemaV1
{
    public static async Task EnsureAsync(DbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    private static async Task EnsureSqliteAsync(DbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        await ExecAsync(db, """
            CREATE TABLE IF NOT EXISTS DocImportBatches (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedAt TEXT NOT NULL,
                FinishedAt TEXT NULL,
                CreatedByUserId INTEGER NOT NULL,
                CreatedByName TEXT NOT NULL,
                TotalFiles INTEGER NOT NULL DEFAULT 0,
                ImportedCount INTEGER NOT NULL DEFAULT 0,
                FailedCount INTEGER NOT NULL DEFAULT 0,
                SkippedCount INTEGER NOT NULL DEFAULT 0,
                CreatedFolderCount INTEGER NOT NULL DEFAULT 0,
                RootFolderId INTEGER NULL,
                CreateMissingFolders INTEGER NOT NULL DEFAULT 1
            )
            """);

        await ExecAsync(db, """
            CREATE TABLE IF NOT EXISTS DocImportResults (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BatchId INTEGER NOT NULL,
                RelativePath TEXT NOT NULL,
                FileName TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL,
                ErrorCode TEXT NULL,
                MessageFa TEXT NULL,
                ParsedTitle TEXT NULL,
                ParsedCode TEXT NULL,
                DocumentId INTEGER NULL,
                ProcessedAtUtc TEXT NOT NULL
            )
            """);

        await ExecAsync(db,
            "CREATE UNIQUE INDEX IF NOT EXISTS UX_DocImportResults_Batch_Path ON DocImportResults(BatchId, RelativePath)");
        await ExecAsync(db,
            "CREATE INDEX IF NOT EXISTS IX_DocImportResults_Batch_Status ON DocImportResults(BatchId, Status)");
    }

    private static async Task EnsureSqlServerAsync(DbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'DocImportBatches', N'U') IS NULL
            CREATE TABLE [DocImportBatches](
                [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DocImportBatches] PRIMARY KEY,
                [StartedAt] datetime2 NOT NULL,
                [FinishedAt] datetime2 NULL,
                [CreatedByUserId] int NOT NULL,
                [CreatedByName] nvarchar(100) NOT NULL,
                [TotalFiles] int NOT NULL CONSTRAINT [DF_DocImportBatches_Total] DEFAULT(0),
                [ImportedCount] int NOT NULL CONSTRAINT [DF_DocImportBatches_Imported] DEFAULT(0),
                [FailedCount] int NOT NULL CONSTRAINT [DF_DocImportBatches_Failed] DEFAULT(0),
                [SkippedCount] int NOT NULL CONSTRAINT [DF_DocImportBatches_Skipped] DEFAULT(0),
                [CreatedFolderCount] int NOT NULL CONSTRAINT [DF_DocImportBatches_Folders] DEFAULT(0),
                [RootFolderId] int NULL,
                [CreateMissingFolders] bit NOT NULL CONSTRAINT [DF_DocImportBatches_Missing] DEFAULT(1)
            )
            """);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'DocImportResults', N'U') IS NULL
            CREATE TABLE [DocImportResults](
                [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DocImportResults] PRIMARY KEY,
                [BatchId] int NOT NULL,
                [RelativePath] nvarchar(600) NOT NULL,
                [FileName] nvarchar(255) NOT NULL,
                [SizeBytes] bigint NOT NULL CONSTRAINT [DF_DocImportResults_Size] DEFAULT(0),
                [Status] nvarchar(20) NOT NULL,
                [ErrorCode] nvarchar(50) NULL,
                [MessageFa] nvarchar(500) NULL,
                [ParsedTitle] nvarchar(255) NULL,
                [ParsedCode] nvarchar(80) NULL,
                [DocumentId] int NULL,
                [ProcessedAtUtc] datetime2 NOT NULL
            )
            """);

        await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DocImportResults_Batch_Path')
            CREATE UNIQUE INDEX [UX_DocImportResults_Batch_Path] ON [DocImportResults]([BatchId], [RelativePath])
            """);

        await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DocImportResults_Batch_Status')
            CREATE INDEX [IX_DocImportResults_Batch_Status] ON [DocImportResults]([BatchId], [Status])
            """);
    }

    private static async Task ExecAsync(DbContext db, string sql)
        => await db.Database.ExecuteSqlRawAsync(sql);
}
