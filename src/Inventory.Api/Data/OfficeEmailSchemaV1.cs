using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای اتوماسیون اداری + ایمیل سازمانی در زمان اجرا
//  • EnsureCreated (SQLite) و Migrate (SQL Server) ساختارهای جدیدتر را
//    به دیتابیس‌های قدیمی اضافه نمی‌کنند؛ مثل DocArchiveSecuritySchemaV1
//    اینجا ستون‌ها/جدول‌های تازه را دستی می‌سازیم.
//  • ستون‌های OutgoingLetters: IsNeshan (نشان صادره) و DestEmail (ایمیل مقصد دبیرخانه)
//  • جداول ایمیل: Oto_TBL_Email / Oto_TBL_Sent / Oto_TBL_Inbox /
//    Oto_TBl_EmailFolder / Oto_TBL_EmailAttachments (نام دقیق دیتابیس Otomasion)
// ============================================================
public static class OfficeEmailSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        // ---------- ستون‌های جدید نامه صادره ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.OutgoingLetters', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.OutgoingLetters', N'IsNeshan') IS NULL
    ALTER TABLE dbo.OutgoingLetters ADD IsNeshan bit NOT NULL DEFAULT(0);
IF OBJECT_ID(N'dbo.OutgoingLetters', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.OutgoingLetters', N'DestEmail') IS NULL
    ALTER TABLE dbo.OutgoingLetters ADD DestEmail nvarchar(250) NULL;
IF OBJECT_ID(N'dbo.OutgoingLetters', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.OutgoingLetters', N'DelivererName') IS NULL
    ALTER TABLE dbo.OutgoingLetters ADD DelivererName nvarchar(250) NULL;
IF OBJECT_ID(N'dbo.OutgoingLetters', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.OutgoingLetters', N'TrackingCode') IS NULL
    ALTER TABLE dbo.OutgoingLetters ADD TrackingCode nvarchar(100) NULL;");

        // ---------- حساب‌های ایمیل ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Oto_TBL_Email', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Oto_TBL_Email](
        [Email_Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Email_Address] nvarchar(250) NOT NULL,
        [Password] nvarchar(255) NOT NULL,
        [SMTP] nvarchar(150) NOT NULL,
        [IMAP] nvarchar(150) NOT NULL,
        [User_Id] int NOT NULL,
        [Activation_Code] nvarchar(100) NULL,
        [IsActive] bit NOT NULL DEFAULT(1),
        [EmailType] nvarchar(30) NOT NULL DEFAULT(N'Gmail'),
        [Semat_Id] int NULL,
        [IsDabirkhane] bit NOT NULL DEFAULT(0),
        [SmtpPort] int NOT NULL DEFAULT(587),
        [SmtpSsl] bit NOT NULL DEFAULT(1),
        [ImapPort] int NOT NULL DEFAULT(993),
        [ImapSsl] bit NOT NULL DEFAULT(1),
        [Display_Name] nvarchar(200) NULL,
        [Last_Sync] datetime2 NULL
    );
    CREATE INDEX [IX_Oto_TBL_Email_User_Id] ON [dbo].[Oto_TBL_Email] ([User_Id]);
    CREATE INDEX [IX_Oto_TBL_Email_IsDabirkhane] ON [dbo].[Oto_TBL_Email] ([IsDabirkhane]);
END
ELSE IF COL_LENGTH(N'dbo.Oto_TBL_Email', N'SmtpPort') IS NULL
BEGIN
    ALTER TABLE dbo.Oto_TBL_Email ADD SmtpPort int NOT NULL DEFAULT(587);
    ALTER TABLE dbo.Oto_TBL_Email ADD SmtpSsl bit NOT NULL DEFAULT(1);
    ALTER TABLE dbo.Oto_TBL_Email ADD ImapPort int NOT NULL DEFAULT(993);
    ALTER TABLE dbo.Oto_TBL_Email ADD ImapSsl bit NOT NULL DEFAULT(1);
    ALTER TABLE dbo.Oto_TBL_Email ADD Display_Name nvarchar(200) NULL;
    ALTER TABLE dbo.Oto_TBL_Email ADD Last_Sync datetime2 NULL;
END");

        // پیشرفتِ همگام‌سازیِ افزایشی: آخرین شناسه‌ی IMAP که برای هر صندوق خوانده شده.
        // با این دو ستون، بار اول همه‌ی پیام‌ها گرفته می‌شود و از بار دوم فقط پیام‌های جدید.
        await SafeAsync(db, @"
IF COL_LENGTH(N'dbo.Oto_TBL_Email', N'Last_Inbox_Uid') IS NULL
BEGIN
    ALTER TABLE dbo.Oto_TBL_Email ADD Last_Inbox_Uid bigint NOT NULL DEFAULT(0);
END
IF COL_LENGTH(N'dbo.Oto_TBL_Email', N'Last_Sent_Uid') IS NULL
BEGIN
    ALTER TABLE dbo.Oto_TBL_Email ADD Last_Sent_Uid bigint NOT NULL DEFAULT(0);
END
IF COL_LENGTH(N'dbo.Oto_TBL_Email', N'Last_Junk_Uid') IS NULL
BEGIN
    ALTER TABLE dbo.Oto_TBL_Email ADD Last_Junk_Uid bigint NOT NULL DEFAULT(0);
END");

        // ---------- نشانِ هرزنامه (اسپم) روی ایمیل‌های دریافتی ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Oto_TBL_Inbox', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Oto_TBL_Inbox', N'Is_Spam') IS NULL
BEGIN
    ALTER TABLE dbo.Oto_TBL_Inbox ADD Is_Spam bit NOT NULL DEFAULT(0);
    CREATE INDEX [IX_Oto_TBL_Inbox_IsSpam] ON [dbo].[Oto_TBL_Inbox] ([Is_Spam]);
END");

        // ---------- ایمیل‌های ارسالی ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Oto_TBL_Sent', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Oto_TBL_Sent](
        [Sent_Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Email_Id] int NOT NULL,
        [UId] nvarchar(300) NOT NULL DEFAULT(N''),
        [To_Display] nvarchar(1000) NOT NULL DEFAULT(N''),
        [Subject] nvarchar(500) NOT NULL DEFAULT(N''),
        [Date] datetime2 NOT NULL,
        [Is_Neshan] bit NOT NULL DEFAULT(0),
        [Is_Attachment] bit NOT NULL DEFAULT(0),
        [Body] nvarchar(max) NOT NULL DEFAULT(N''),
        [IsInFolder] int NOT NULL DEFAULT(0),
        [LetterSourceId] int NULL
    );
    CREATE INDEX [IX_Oto_TBL_Sent_Email_UId] ON [dbo].[Oto_TBL_Sent] ([Email_Id], [UId]);
END
ELSE IF COL_LENGTH(N'dbo.Oto_TBL_Sent', N'LetterSourceId') IS NULL
    ALTER TABLE dbo.Oto_TBL_Sent ADD LetterSourceId int NULL;");

        // ---------- ایمیل‌های دریافتی ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Oto_TBL_Inbox', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Oto_TBL_Inbox](
        [Inbox_Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Email_Id] int NOT NULL,
        [UId] nvarchar(300) NOT NULL DEFAULT(N''),
        [Subject] nvarchar(500) NOT NULL DEFAULT(N''),
        [Date] datetime2 NOT NULL,
        [Is_Read] bit NOT NULL DEFAULT(0),
        [Is_Neshan] bit NOT NULL DEFAULT(0),
        [Body] nvarchar(max) NOT NULL DEFAULT(N''),
        [Is_Attachment] bit NOT NULL DEFAULT(0),
        [From_Address] nvarchar(300) NOT NULL DEFAULT(N''),
        [IsInFolder] int NOT NULL DEFAULT(0),
        [Is_Spam] bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX [IX_Oto_TBL_Inbox_Email_UId] ON [dbo].[Oto_TBL_Inbox] ([Email_Id], [UId]);
    CREATE INDEX [IX_Oto_TBL_Inbox_IsRead] ON [dbo].[Oto_TBL_Inbox] ([Is_Read]);
    CREATE INDEX [IX_Oto_TBL_Inbox_IsSpam] ON [dbo].[Oto_TBL_Inbox] ([Is_Spam]);
END");

        // ---------- پوشه‌های بایگانی ایمیل ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Oto_TBl_EmailFolder', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Oto_TBl_EmailFolder](
        [EmailFolderId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Title] nvarchar(200) NOT NULL DEFAULT(N''),
        [EmailId] int NOT NULL DEFAULT(0),
        [ParentId] int NOT NULL DEFAULT(0),
        [UserId] int NOT NULL,
        [SematId] int NULL,
        [IsFolder] bit NOT NULL DEFAULT(1),
        [TypeEmail] int NOT NULL DEFAULT(0),
        [Uid] int NOT NULL DEFAULT(0),
        [EmailSetId] int NULL,
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX [IX_Oto_TBl_EmailFolder_User_Parent] ON [dbo].[Oto_TBl_EmailFolder] ([UserId], [ParentId]);
    CREATE INDEX [IX_Oto_TBl_EmailFolder_IsFolder] ON [dbo].[Oto_TBl_EmailFolder] ([IsFolder]);
END
ELSE IF COL_LENGTH(N'dbo.Oto_TBl_EmailFolder', N'IsDelete') IS NULL
    ALTER TABLE dbo.Oto_TBl_EmailFolder ADD IsDelete bit NOT NULL DEFAULT(0);");

        // ---------- پیوست‌های ایمیل ----------
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Oto_TBL_EmailAttachments', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Oto_TBL_EmailAttachments](
        [Attachment_Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Email_id] int NOT NULL,
        [UId] nvarchar(300) NOT NULL DEFAULT(N''),
        [Type] nvarchar(20) NOT NULL DEFAULT(N'Sent'),
        [Attachment_Real_Name] nvarchar(255) NOT NULL DEFAULT(N''),
        [Attachment_Saved_Name] nvarchar(255) NOT NULL DEFAULT(N''),
        [Attachment_FilePath] nvarchar(300) NULL
    );
    CREATE INDEX [IX_Oto_TBL_EmailAttachments_Type_Email] ON [dbo].[Oto_TBL_EmailAttachments] ([Type], [Email_id]);
END
ELSE IF COL_LENGTH(N'dbo.Oto_TBL_EmailAttachments', N'Attachment_FilePath') IS NULL
    ALTER TABLE dbo.Oto_TBL_EmailAttachments ADD Attachment_FilePath nvarchar(300) NULL;");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, @"ALTER TABLE OutgoingLetters ADD COLUMN IsNeshan INTEGER NOT NULL DEFAULT 0;");
        await SafeAsync(db, @"ALTER TABLE OutgoingLetters ADD COLUMN DelivererName TEXT NULL;");
        await SafeAsync(db, @"ALTER TABLE OutgoingLetters ADD COLUMN TrackingCode TEXT NULL;");
        await SafeAsync(db, @"ALTER TABLE OutgoingLetters ADD COLUMN DestEmail TEXT NULL;");

        await SafeAsync(db, @"CREATE TABLE IF NOT EXISTS Oto_TBL_Email (
    Email_Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Email_Address TEXT NOT NULL,
    Password TEXT NOT NULL,
    SMTP TEXT NOT NULL,
    IMAP TEXT NOT NULL,
    User_Id INTEGER NOT NULL,
    Activation_Code TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    EmailType TEXT NOT NULL DEFAULT 'Gmail',
    Semat_Id INTEGER NULL,
    IsDabirkhane INTEGER NOT NULL DEFAULT 0,
    SmtpPort INTEGER NOT NULL DEFAULT 587,
    SmtpSsl INTEGER NOT NULL DEFAULT 1,
    ImapPort INTEGER NOT NULL DEFAULT 993,
    ImapSsl INTEGER NOT NULL DEFAULT 1,
    Display_Name TEXT NULL,
    Last_Sync TEXT NULL,
    Last_Inbox_Uid INTEGER NOT NULL DEFAULT 0,
    Last_Sent_Uid INTEGER NOT NULL DEFAULT 0,
    Last_Junk_Uid INTEGER NOT NULL DEFAULT 0);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_Oto_TBL_Email_User_Id ON Oto_TBL_Email (User_Id);");

        // دیتابیس‌های SQLiteِ قدیمی‌تر این ستون‌ها را ندارند — خطای «ستون تکراری» پذیرفته می‌شود
        await SafeAsync(db, @"ALTER TABLE Oto_TBL_Email ADD COLUMN Last_Inbox_Uid INTEGER NOT NULL DEFAULT 0;");
        await SafeAsync(db, @"ALTER TABLE Oto_TBL_Email ADD COLUMN Last_Sent_Uid INTEGER NOT NULL DEFAULT 0;");
        await SafeAsync(db, @"ALTER TABLE Oto_TBL_Email ADD COLUMN Last_Junk_Uid INTEGER NOT NULL DEFAULT 0;");

        await SafeAsync(db, @"CREATE TABLE IF NOT EXISTS Oto_TBL_Sent (
    Sent_Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Email_Id INTEGER NOT NULL,
    UId TEXT NOT NULL DEFAULT '',
    To_Display TEXT NOT NULL DEFAULT '',
    Subject TEXT NOT NULL DEFAULT '',
    Date TEXT NOT NULL,
    Is_Neshan INTEGER NOT NULL DEFAULT 0,
    Is_Attachment INTEGER NOT NULL DEFAULT 0,
    Body TEXT NOT NULL DEFAULT '',
    IsInFolder INTEGER NOT NULL DEFAULT 0,
    LetterSourceId INTEGER NULL);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_Oto_TBL_Sent_Email_UId ON Oto_TBL_Sent (Email_Id, UId);");

        await SafeAsync(db, @"CREATE TABLE IF NOT EXISTS Oto_TBL_Inbox (
    Inbox_Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Email_Id INTEGER NOT NULL,
    UId TEXT NOT NULL DEFAULT '',
    Subject TEXT NOT NULL DEFAULT '',
    Date TEXT NOT NULL,
    Is_Read INTEGER NOT NULL DEFAULT 0,
    Is_Neshan INTEGER NOT NULL DEFAULT 0,
    Body TEXT NOT NULL DEFAULT '',
    Is_Attachment INTEGER NOT NULL DEFAULT 0,
    From_Address TEXT NOT NULL DEFAULT '',
    IsInFolder INTEGER NOT NULL DEFAULT 0,
    Is_Spam INTEGER NOT NULL DEFAULT 0);");
        await SafeAsync(db, @"CREATE INDEX IF NOT EXISTS IX_Oto_TBL_Inbox_Email_UId ON Oto_TBL_Inbox (Email_Id, UId);");
        await SafeAsync(db, @"ALTER TABLE Oto_TBL_Inbox ADD COLUMN Is_Spam INTEGER NOT NULL DEFAULT 0;");

        await SafeAsync(db, @"CREATE TABLE IF NOT EXISTS Oto_TBl_EmailFolder (
    EmailFolderId INTEGER PRIMARY KEY AUTOINCREMENT,
    Title TEXT NOT NULL DEFAULT '',
    EmailId INTEGER NOT NULL DEFAULT 0,
    ParentId INTEGER NOT NULL DEFAULT 0,
    UserId INTEGER NOT NULL,
    SematId INTEGER NULL,
    IsFolder INTEGER NOT NULL DEFAULT 1,
    TypeEmail INTEGER NOT NULL DEFAULT 0,
    Uid INTEGER NOT NULL DEFAULT 0,
    EmailSetId INTEGER NULL,
    IsDelete INTEGER NOT NULL DEFAULT 0);");

        await SafeAsync(db, @"CREATE TABLE IF NOT EXISTS Oto_TBL_EmailAttachments (
    Attachment_Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Email_id INTEGER NOT NULL,
    UId TEXT NOT NULL DEFAULT '',
    Type TEXT NOT NULL DEFAULT 'Sent',
    Attachment_Real_Name TEXT NOT NULL DEFAULT '',
    Attachment_Saved_Name TEXT NOT NULL DEFAULT '',
    Attachment_FilePath TEXT NULL);");
    }

    /// <summary>اجرای امن — خطای «ستون/جدول از قبل هست» را می‌پذیریم</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var msg = ex.Message ?? "";
            if (msg.Contains("already") || msg.Contains("duplicate") || msg.Contains("تکراری")) return;
            Console.WriteLine($"[DB] هشدار OfficeEmailSchemaV1: {ex.Message}");
        }
    }
}
