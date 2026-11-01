using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ================== اسکیمای «PWA آفلاین + همکاری» آرشیو اسناد ==================
/// جایگزین دو فایل مایگریشن دستی که attribute [Migration] نداشتند و به همین دلیل
/// هرگز توسط EF اجرا نمی‌شدند (DocArchivePwaOffline و DocCollabScannerLeak).
/// نبودِ این اسکیمای روی دیتابیس‌های قدیمی خودش را با خطای
/// «Invalid column name 'BiometricVerified'» و «Invalid object name 'DocComments'»
/// نشان می‌داد.
///
/// آنچه پوشش می‌دهد:
///  - جدول‌ها: DocFavorites، DocComments، DocCommentMentions، DocLeakTraces
///  - ستون‌ها: DocumentApprovers.SignatureDataUrl / BiometricVerified / DeviceInfo
///
/// خودترمیم و Idempotent؛ دقیقاً با همان سبک DocPrintSchemaV1. در DbInitializer
/// در هر دو مسیر Sqlite و SQL Server صدا زده می‌شود.
/// </summary>
public static class DocArchivePwaSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        var isSqlite = string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.OrdinalIgnoreCase);

        if (isSqlite) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    /// <summary>اجرای یک قطعه SQL؛ خطا (مثلاً ستون تکراری در دیتابیس قدیمی) بوت را نمی‌شکند.</summary>
    private static async Task Run(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch { /* خودترمیم نباید بوت را بشکند */ }
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await Run(db, @"
CREATE TABLE IF NOT EXISTS DocFavorites (
    Id INTEGER NOT NULL CONSTRAINT PK_DocFavorites PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    DocumentId INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_DocFavorites_UserId_DocumentId ON DocFavorites (UserId, DocumentId);
CREATE INDEX IF NOT EXISTS IX_DocFavorites_UserId ON DocFavorites (UserId);
");
        await Run(db, @"
CREATE TABLE IF NOT EXISTS DocComments (
    Id INTEGER NOT NULL CONSTRAINT PK_DocComments PRIMARY KEY AUTOINCREMENT,
    DocumentId INTEGER NOT NULL,
    VersionId INTEGER NULL,
    ParentId INTEGER NULL,
    AuthorUserId INTEGER NOT NULL,
    AuthorName TEXT NOT NULL,
    Body TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    EditedAt TEXT NULL,
    IsResolved INTEGER NOT NULL DEFAULT 0,
    ResolvedByUserId INTEGER NULL,
    ResolvedAt TEXT NULL,
    IsDeleted INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_DocComments_DocumentId ON DocComments (DocumentId, CreatedAt);
");
        await Run(db, @"
CREATE TABLE IF NOT EXISTS DocCommentMentions (
    Id INTEGER NOT NULL CONSTRAINT PK_DocCommentMentions PRIMARY KEY AUTOINCREMENT,
    CommentId INTEGER NOT NULL REFERENCES DocComments (Id) ON DELETE CASCADE,
    MentionedUserId INTEGER NOT NULL,
    MentionedUserName TEXT NOT NULL
);
");
        await Run(db, @"
CREATE TABLE IF NOT EXISTS DocLeakTraces (
    Id INTEGER NOT NULL CONSTRAINT PK_DocLeakTraces PRIMARY KEY AUTOINCREMENT,
    DocumentId INTEGER NOT NULL,
    VersionId INTEGER NULL,
    UserId INTEGER NOT NULL,
    UserName TEXT NOT NULL,
    IpAddress TEXT NULL,
    VisibleWatermark TEXT NOT NULL,
    InvisiblePayload TEXT NOT NULL,
    FileHash TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL,
    Action TEXT NOT NULL DEFAULT 'Download'
);
CREATE INDEX IF NOT EXISTS IX_DocLeakTraces_DocumentId ON DocLeakTraces (DocumentId, CreatedAt);
");
        // ستون‌های امضا روی گردشکارهای مدرک — هر کدام جدا تا ستون موجود، بقیه را بلوکه نکند
        await Run(db, "ALTER TABLE DocumentApprovers ADD COLUMN SignatureDataUrl TEXT NULL;");
        await Run(db, "ALTER TABLE DocumentApprovers ADD COLUMN BiometricVerified INTEGER NOT NULL DEFAULT 0;");
        await Run(db, "ALTER TABLE DocumentApprovers ADD COLUMN DeviceInfo TEXT NULL;");
        // ستون‌های «حل‌شده» روی نظرات — برای دیتابیسی که جدول را ناقص ساخته باشد
        await Run(db, "ALTER TABLE DocComments ADD COLUMN IsResolved INTEGER NOT NULL DEFAULT 0;");
        await Run(db, "ALTER TABLE DocComments ADD COLUMN ResolvedByUserId INTEGER NULL;");
        await Run(db, "ALTER TABLE DocComments ADD COLUMN ResolvedAt TEXT NULL;");
        await Run(db, "ALTER TABLE DocComments ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;");
    }

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await Run(db, @"
IF OBJECT_ID(N'dbo.DocFavorites', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocFavorites(
        Id INT IDENTITY(1,1) CONSTRAINT PK_DocFavorites PRIMARY KEY,
        UserId INT NOT NULL,
        DocumentId INT NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME()
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocFavorites_UserId_DocumentId' AND object_id = OBJECT_ID(N'dbo.DocFavorites'))
    CREATE UNIQUE INDEX IX_DocFavorites_UserId_DocumentId ON dbo.DocFavorites(UserId, DocumentId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocFavorites_UserId' AND object_id = OBJECT_ID(N'dbo.DocFavorites'))
    CREATE INDEX IX_DocFavorites_UserId ON dbo.DocFavorites(UserId);
");
        await Run(db, @"
IF OBJECT_ID(N'dbo.DocComments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocComments(
        Id INT IDENTITY(1,1) CONSTRAINT PK_DocComments PRIMARY KEY,
        DocumentId INT NOT NULL,
        VersionId INT NULL,
        ParentId INT NULL,
        AuthorUserId INT NOT NULL,
        AuthorName NVARCHAR(150) NOT NULL,
        Body NVARCHAR(2000) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME(),
        EditedAt datetime2 NULL,
        IsResolved BIT NOT NULL DEFAULT 0,
        ResolvedByUserId INT NULL,
        ResolvedAt datetime2 NULL,
        IsDeleted BIT NOT NULL DEFAULT 0
    );
END
IF COL_LENGTH(N'dbo.DocComments', N'IsResolved') IS NULL
    ALTER TABLE dbo.DocComments ADD IsResolved BIT NOT NULL CONSTRAINT DF_DocComments_IsResolved DEFAULT 0;
IF COL_LENGTH(N'dbo.DocComments', N'ResolvedByUserId') IS NULL
    ALTER TABLE dbo.DocComments ADD ResolvedByUserId INT NULL;
IF COL_LENGTH(N'dbo.DocComments', N'ResolvedAt') IS NULL
    ALTER TABLE dbo.DocComments ADD ResolvedAt datetime2 NULL;
IF COL_LENGTH(N'dbo.DocComments', N'IsDeleted') IS NULL
    ALTER TABLE dbo.DocComments ADD IsDeleted BIT NOT NULL CONSTRAINT DF_DocComments_IsDeleted DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocComments_DocumentId' AND object_id = OBJECT_ID(N'dbo.DocComments'))
    CREATE INDEX IX_DocComments_DocumentId ON dbo.DocComments(DocumentId, CreatedAt);
");
        await Run(db, @"
IF OBJECT_ID(N'dbo.DocCommentMentions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocCommentMentions(
        Id INT IDENTITY(1,1) CONSTRAINT PK_DocCommentMentions PRIMARY KEY,
        CommentId INT NOT NULL CONSTRAINT FK_DocCommentMentions_DocComments FOREIGN KEY REFERENCES dbo.DocComments(Id) ON DELETE CASCADE,
        MentionedUserId INT NOT NULL,
        MentionedUserName NVARCHAR(150) NOT NULL
    );
END
");
        await Run(db, @"
IF OBJECT_ID(N'dbo.DocLeakTraces', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocLeakTraces(
        Id INT IDENTITY(1,1) CONSTRAINT PK_DocLeakTraces PRIMARY KEY,
        DocumentId INT NOT NULL,
        VersionId INT NULL,
        UserId INT NOT NULL,
        UserName NVARCHAR(150) NOT NULL,
        IpAddress NVARCHAR(60) NULL,
        VisibleWatermark NVARCHAR(300) NOT NULL,
        InvisiblePayload NVARCHAR(300) NOT NULL,
        FileHash NVARCHAR(64) NOT NULL DEFAULT '',
        CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME(),
        [Action] NVARCHAR(30) NOT NULL DEFAULT 'Download'
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocLeakTraces_DocumentId' AND object_id = OBJECT_ID(N'dbo.DocLeakTraces'))
    CREATE INDEX IX_DocLeakTraces_DocumentId ON dbo.DocLeakTraces(DocumentId, CreatedAt);
");
        // ستون‌های امضا روی گردشکارهای مدرک — همان سه ستونی که خطای Invalid column name می‌دادند
        await Run(db, @"
IF OBJECT_ID(N'dbo.DocumentApprovers', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.DocumentApprovers', N'SignatureDataUrl') IS NULL
    ALTER TABLE dbo.DocumentApprovers ADD SignatureDataUrl NVARCHAR(max) NULL;
IF OBJECT_ID(N'dbo.DocumentApprovers', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.DocumentApprovers', N'BiometricVerified') IS NULL
    ALTER TABLE dbo.DocumentApprovers ADD BiometricVerified BIT NOT NULL CONSTRAINT DF_DocumentApprovers_BiometricVerified DEFAULT 0;
IF OBJECT_ID(N'dbo.DocumentApprovers', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.DocumentApprovers', N'DeviceInfo') IS NULL
    ALTER TABLE dbo.DocumentApprovers ADD DeviceInfo NVARCHAR(300) NULL;
");
    }
}
