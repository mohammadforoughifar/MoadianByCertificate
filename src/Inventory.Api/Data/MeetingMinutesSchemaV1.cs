using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای صورتجلسه — نسخه ۱ (ماژول «فرم‌های متفرقه»)
//  • مثل سایر SchemaV1ها — idempotent و ایمن برای هر دو پرووایدر
//    (SQL Server و SQLite). جدول‌ها:
//   • MeetingMinutes              — صورتجلسه‌ها
//   • MeetingMinutesParticipants  — حاضرین/غایبین + امضای الکترونیکی
//   • MeetingMinutesItems         — بندها (شرح/مسئول/پیگیری/تصمیمات)
// ============================================================
public static class MeetingMinutesSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);
    }

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.MeetingMinutes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MeetingMinutes
    (
        Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MeetingMinutes PRIMARY KEY,
        Title           nvarchar(300) NOT NULL,
        MeetingDate     datetime2 NOT NULL,
        DateRegistered  datetime2 NOT NULL,
        Status          nvarchar(20) NOT NULL,
        ClosedAt        datetime2 NULL,
        CreatedByUserId int NOT NULL,
        CreatedByName   nvarchar(150) NOT NULL,
        IsDeleted       bit NOT NULL CONSTRAINT DF_MeetingMinutes_IsDeleted DEFAULT(0),
        DeletedAt       datetime2 NULL,
        DeletedByUserId int NULL
    );
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.MeetingMinutesParticipants', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MeetingMinutesParticipants
    (
        Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MeetingMinutesParticipants PRIMARY KEY,
        MinutesId     int NOT NULL CONSTRAINT FK_MeetingMinutesParticipants_Minutes FOREIGN KEY REFERENCES dbo.MeetingMinutes(Id) ON DELETE CASCADE,
        UserId        int NOT NULL,
        Name          nvarchar(150) NOT NULL,
        Kind          nvarchar(20) NOT NULL,
        SignatureData nvarchar(max) NULL,
        SignedAt      datetime2 NULL,
        Notified      bit NOT NULL CONSTRAINT DF_MeetingMinutesParticipants_Notified DEFAULT(0)
    );
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.MeetingMinutesItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MeetingMinutesItems
    (
        Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MeetingMinutesItems PRIMARY KEY,
        MinutesId           int NOT NULL CONSTRAINT FK_MeetingMinutesItems_Minutes FOREIGN KEY REFERENCES dbo.MeetingMinutes(Id) ON DELETE CASCADE,
        RowNo               int NOT NULL,
        Description         nvarchar(max) NOT NULL,
        DueDate             datetime2 NULL,
        ResponsibleUserId   int NULL,
        ResponsibleName     nvarchar(150) NULL,
        FollowUpDate        datetime2 NULL,
        FollowUpUserId      int NULL,
        FollowUpName        nvarchar(150) NULL,
        ItemStatus          nvarchar(20) NOT NULL,
        RespApproved        bit NOT NULL CONSTRAINT DF_MeetingMinutesItems_RespApproved DEFAULT(0),
        RespApprovedAt      datetime2 NULL,
        RespNote            nvarchar(500) NULL,
        FollowUpDecision    nvarchar(20) NULL,
        FollowUpDecidedAt   datetime2 NULL,
        FollowUpNote        nvarchar(500) NULL
    );
END;");

        await SafeAsync(db, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MeetingMinutes_Participants' AND object_id = OBJECT_ID(N'dbo.MeetingMinutesParticipants'))
    CREATE INDEX IX_MeetingMinutes_Participants ON dbo.MeetingMinutesParticipants(MinutesId);");
        await SafeAsync(db, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MeetingMinutes_Items' AND object_id = OBJECT_ID(N'dbo.MeetingMinutesItems'))
    CREATE INDEX IX_MeetingMinutes_Items ON dbo.MeetingMinutesItems(MinutesId);");
        await SafeAsync(db, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MeetingMinutes_Status' AND object_id = OBJECT_ID(N'dbo.MeetingMinutes'))
    CREATE INDEX IX_MeetingMinutes_Status ON dbo.MeetingMinutes(Status);");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS MeetingMinutes
(
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Title           TEXT NOT NULL,
    MeetingDate     TEXT NOT NULL,
    DateRegistered  TEXT NOT NULL,
    Status          TEXT NOT NULL,
    ClosedAt        TEXT NULL,
    CreatedByUserId INTEGER NOT NULL,
    CreatedByName   TEXT NOT NULL,
    IsDeleted       INTEGER NOT NULL DEFAULT 0,
    DeletedAt       TEXT NULL,
    DeletedByUserId INTEGER NULL
);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS MeetingMinutesParticipants
(
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    MinutesId     INTEGER NOT NULL REFERENCES MeetingMinutes(Id) ON DELETE CASCADE,
    UserId        INTEGER NOT NULL,
    Name          TEXT NOT NULL,
    Kind          TEXT NOT NULL,
    SignatureData TEXT NULL,
    SignedAt      TEXT NULL,
    Notified      INTEGER NOT NULL DEFAULT 0
);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS MeetingMinutesItems
(
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    MinutesId           INTEGER NOT NULL REFERENCES MeetingMinutes(Id) ON DELETE CASCADE,
    RowNo               INTEGER NOT NULL,
    Description         TEXT NOT NULL,
    DueDate             TEXT NULL,
    ResponsibleUserId   INTEGER NULL,
    ResponsibleName     TEXT NULL,
    FollowUpDate        TEXT NULL,
    FollowUpUserId      INTEGER NULL,
    FollowUpName        TEXT NULL,
    ItemStatus          TEXT NOT NULL,
    RespApproved        INTEGER NOT NULL DEFAULT 0,
    RespApprovedAt      TEXT NULL,
    RespNote            TEXT NULL,
    FollowUpDecision    TEXT NULL,
    FollowUpDecidedAt   TEXT NULL,
    FollowUpNote        TEXT NULL
);");

        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_MeetingMinutes_Participants ON MeetingMinutesParticipants(MinutesId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_MeetingMinutes_Items ON MeetingMinutesItems(MinutesId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_MeetingMinutes_Status ON MeetingMinutes(Status);");
    }

    /// <summary>اجرای امن — خطای «تکراری» راه‌اندازی برنامه را متوقف نکند.</summary>
    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] MeetingMinutesSchemaV1: {m}");
        }
    }
}
