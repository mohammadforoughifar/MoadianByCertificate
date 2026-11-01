using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «کانال شرکت‌های راه‌دور» (درخواست خدمات IT بین‌شرکتی) — نسخه ۱
//  • EnsureCreated (SQLite) و Migrate (SQL Server) جدول/ستون‌های تازه را به دیتابیس‌های
//    قدیمی اضافه نمی‌کنند؛ اینجا به‌صورت امن و idempotent انجام می‌شود.
//  سمت سرور مرکزی:
//      ItClientCompanies      — شرکت‌های مشتری + هش کلید API
//  سمت نصبِ شعبه:
//      ItRemoteConnections    — تنظیم اتصال به سرور مرکزی (تک‌رکوردی)
//      ItRemoteRequests       — آینهٔ محلی درخواست‌های ارسالی
//      ItRemoteUserKeys       — کلید پایدار هر کاربر برای «درخواست‌های من»
//  ستون‌های تازه روی ItRequests:
//      SourceCompanyId, ExternalRequesterKey, ExternalId, RequesterPhone,
//      RequesterEmail, TrackToken
// ============================================================
public static class ItRemoteRequestSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static Task EnsureSqlServerAsync(AppDbContext db)
    {
        Run(db, @"
IF OBJECT_ID(N'dbo.ItClientCompanies', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItClientCompanies](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Phone] nvarchar(50) NULL,
        [ApiKeyHash] nvarchar(100) NOT NULL CONSTRAINT [DF_ItClientCompanies_ApiKeyHash] DEFAULT(''),
        [ApiKeyHint] nvarchar(10) NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_ItClientCompanies_IsActive] DEFAULT(1),
        [HourlyLimit] int NOT NULL CONSTRAINT [DF_ItClientCompanies_HourlyLimit] DEFAULT(60),
        [LastSeenAt] datetime2 NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ItClientCompanies_CreatedAt] DEFAULT(GETDATE()),
        [KeyRotatedAt] datetime2 NULL
    );
    CREATE UNIQUE INDEX [IX_ItClientCompanies_Code] ON [dbo].[ItClientCompanies] ([Code]);
END", "جدول ItClientCompanies");

        // ستون‌های تازه روی ItRequests
        Run(db, "IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'SourceCompanyId') IS NULL ALTER TABLE dbo.ItRequests ADD SourceCompanyId int NULL;", "ستون SourceCompanyId");
        Run(db, "IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'ExternalRequesterKey') IS NULL ALTER TABLE dbo.ItRequests ADD ExternalRequesterKey nvarchar(40) NULL;", "ستون ExternalRequesterKey");
        Run(db, "IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'ExternalId') IS NULL ALTER TABLE dbo.ItRequests ADD ExternalId nvarchar(40) NULL;", "ستون ExternalId");
        Run(db, "IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'RequesterPhone') IS NULL ALTER TABLE dbo.ItRequests ADD RequesterPhone nvarchar(30) NULL;", "ستون RequesterPhone");
        Run(db, "IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'RequesterEmail') IS NULL ALTER TABLE dbo.ItRequests ADD RequesterEmail nvarchar(120) NULL;", "ستون RequesterEmail");
        Run(db, "IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'TrackToken') IS NULL ALTER TABLE dbo.ItRequests ADD TrackToken nvarchar(40) NULL;", "ستون TrackToken");

        // ایندکس یکتای idempotency (NULL‌ها در SQL Server یکتا محسوب نمی‌شوند؛ فیلتر هم می‌گذاریم)
        Run(db, @"
IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItRequests_ExternalId' AND object_id = OBJECT_ID(N'dbo.ItRequests'))
    CREATE UNIQUE INDEX [IX_ItRequests_ExternalId] ON [dbo].[ItRequests] ([SourceCompanyId], [ExternalId])
    WHERE [ExternalId] IS NOT NULL;", "ایندکس یکتای ExternalId");

        Run(db, @"
IF OBJECT_ID(N'dbo.ItRemoteConnections', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItRemoteConnections](
        [Id] int NOT NULL CONSTRAINT [PK_ItRemoteConnections] PRIMARY KEY,
        [ServerUrl] nvarchar(300) NULL,
        [CompanyCode] nvarchar(50) NULL,
        [CompanyName] nvarchar(200) NULL,
        [ApiKey] nvarchar(100) NULL,
        [Enabled] bit NOT NULL CONSTRAINT [DF_ItRemoteConnections_Enabled] DEFAULT(1),
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_ItRemoteConnections_UpdatedAt] DEFAULT(GETDATE())
    );
    INSERT INTO [dbo].[ItRemoteConnections] ([Id], [Enabled]) VALUES (1, 1);
END", "جدول ItRemoteConnections");

        Run(db, @"
IF OBJECT_ID(N'dbo.ItRemoteRequests', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItRemoteRequests](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [RemoteNumber] nvarchar(30) NULL,
        [TrackToken] nvarchar(40) NULL,
        [ExternalId] nvarchar(40) NOT NULL,
        [RequesterKey] nvarchar(40) NOT NULL,
        [LocalUserId] int NOT NULL,
        [RequesterName] nvarchar(150) NOT NULL,
        [SystemLabel] nvarchar(250) NULL,
        [RequestType] nvarchar(30) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [FinalResponse] nvarchar(4000) NULL,
        [ManagerApprovedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [SyncedAt] datetime2 NULL,
        [LastError] nvarchar(500) NULL
    );
    CREATE INDEX [IX_ItRemoteRequests_LocalUserId] ON [dbo].[ItRemoteRequests] ([LocalUserId]);
    CREATE INDEX [IX_ItRemoteRequests_RemoteNumber] ON [dbo].[ItRemoteRequests] ([RemoteNumber]);
END", "جدول ItRemoteRequests");

        Run(db, @"
IF OBJECT_ID(N'dbo.ItRemoteUserKeys', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItRemoteUserKeys](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [UserId] int NOT NULL,
        [Key] nvarchar(40) NOT NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ItRemoteUserKeys_CreatedAt] DEFAULT(GETDATE())
    );
    CREATE UNIQUE INDEX [IX_ItRemoteUserKeys_UserId] ON [dbo].[ItRemoteUserKeys] ([UserId]);
END", "جدول ItRemoteUserKeys");

        return Task.CompletedTask;
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        async Task Exec(string sql)
        {
            try { using var c = conn.CreateCommand(); c.CommandText = sql; await c.ExecuteNonQueryAsync(); }
            catch (Exception ex) { Console.WriteLine($"[DB] SQLite: {ex.Message}"); }
        }

        await Exec(@"
CREATE TABLE IF NOT EXISTS ItClientCompanies (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL,
    Phone TEXT NULL,
    ApiKeyHash TEXT NOT NULL DEFAULT '',
    ApiKeyHint TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    HourlyLimit INTEGER NOT NULL DEFAULT 60,
    LastSeenAt TEXT NULL,
    Note TEXT NULL,
    CreatedAt TEXT NOT NULL,
    KeyRotatedAt TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ItClientCompanies_Code ON ItClientCompanies (Code);");

        // ستون‌های تازه روی ItRequests
        foreach (var col in new[]
        {
            ("SourceCompanyId", "INTEGER"), ("ExternalRequesterKey", "TEXT"), ("ExternalId", "TEXT"),
            ("RequesterPhone", "TEXT"), ("RequesterEmail", "TEXT"), ("TrackToken", "TEXT")
        })
            await AddCol(conn, "ItRequests", col.Item1, col.Item2);

        await Exec("CREATE UNIQUE INDEX IF NOT EXISTS IX_ItRequests_ExternalId ON ItRequests (SourceCompanyId, ExternalId);");

        await Exec(@"
CREATE TABLE IF NOT EXISTS ItRemoteConnections (
    Id INTEGER NOT NULL PRIMARY KEY,
    ServerUrl TEXT NULL,
    CompanyCode TEXT NULL,
    CompanyName TEXT NULL,
    ApiKey TEXT NULL,
    Enabled INTEGER NOT NULL DEFAULT 1,
    UpdatedAt TEXT NOT NULL
);
INSERT OR IGNORE INTO ItRemoteConnections (Id, Enabled, UpdatedAt) VALUES (1, 1, '');");

        await Exec(@"
CREATE TABLE IF NOT EXISTS ItRemoteRequests (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    RemoteNumber TEXT NULL,
    TrackToken TEXT NULL,
    ExternalId TEXT NOT NULL,
    RequesterKey TEXT NOT NULL,
    LocalUserId INTEGER NOT NULL,
    RequesterName TEXT NOT NULL,
    SystemLabel TEXT NULL,
    RequestType TEXT NOT NULL DEFAULT 'Hardware',
    Title TEXT NOT NULL,
    Description TEXT NOT NULL DEFAULT '',
    Status TEXT NOT NULL DEFAULT 'New',
    FinalResponse TEXT NULL,
    ManagerApprovedAt TEXT NULL,
    CompletedAt TEXT NULL,
    CreatedAt TEXT NOT NULL,
    SyncedAt TEXT NULL,
    LastError TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_ItRemoteRequests_LocalUserId ON ItRemoteRequests (LocalUserId);
CREATE INDEX IF NOT EXISTS IX_ItRemoteRequests_RemoteNumber ON ItRemoteRequests (RemoteNumber);");

        await Exec(@"
CREATE TABLE IF NOT EXISTS ItRemoteUserKeys (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    Key TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ItRemoteUserKeys_UserId ON ItRemoteUserKeys (UserId);");
    }

    private static async Task AddCol(System.Data.Common.DbConnection conn, string table, string column, string type)
    {
        try
        {
            var cols = new List<string>();
            using (var c = conn.CreateCommand())
            {
                c.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
                using var rd = await c.ExecuteReaderAsync();
                while (await rd.ReadAsync()) cols.Add(rd.GetString(0));
            }
            if (cols.Count == 0 || cols.Contains(column)) return;
            using (var c = conn.CreateCommand())
            {
                c.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
                await c.ExecuteNonQueryAsync();
            }
            Console.WriteLine($"[DB] SQLite: ستون {column} به {table} اضافه شد.");
        }
        catch (Exception ex) { Console.WriteLine($"[DB] SQLite: ستون {column} → {ex.Message}"); }
    }

    private static void Run(AppDbContext db, string sql, string label)
    {
        try { db.Database.ExecuteSqlRaw(sql); }
        catch (Exception ex) { Console.WriteLine($"[DB] ItRemoteRequestSchemaV1: {label} → {ex.Message}"); }
    }
}
