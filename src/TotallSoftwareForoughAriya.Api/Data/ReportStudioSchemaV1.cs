using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  اسکیمای گزارش‌ساز حرفه‌ای — نسخه ۱ (خودترمیم و idempotent)
//
//  RsReports            : تعریف گزارش‌ها
//  RsReportUserShares   : اشتراک با کاربرِ مشخص
//  RsReportRoleShares   : اشتراک با نقش
//
//  مثل بقیهٔ اسکیماهای این پروژه، هم روی SQL Server و هم SQLite
//  ساخته می‌شود تا دیتابیس‌های موجود بدون مایگریشن به‌روز شوند.
// ============================================================
public static class ReportStudioSchemaV1
{
    private static int _done;
    private static int _running;

    public static string? LastError { get; private set; }

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? SqliteAsync(db) : SqlServerAsync(db);

    public static async Task EnsureOnceAsync(AppDbContext db)
    {
        if (Volatile.Read(ref _done) == 1 && LastError is null) return;
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            LastError = null;
            await EnsureAsync(db);
            if (LastError is null) Volatile.Write(ref _done, 1);
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    public static async Task<bool> TablesExistAsync(AppDbContext db)
    {
        try
        {
            await db.RsReports.AsNoTracking().Select(x => x.Id).Take(1).ToListAsync();
            return true;
        }
        catch { return false; }
    }

    // ==================== SQL Server ====================
    private static async Task SqlServerAsync(AppDbContext db)
    {
        await Safe(db, @"
IF OBJECT_ID(N'dbo.RsReports', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RsReports](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_RsReports] PRIMARY KEY,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(500) NULL,
        [Icon] nvarchar(60) NOT NULL DEFAULT(N'bi-file-earmark-bar-graph'),
        [Folder] nvarchar(100) NULL,
        [OwnerUserId] int NOT NULL,
        [Visibility] int NOT NULL DEFAULT(0),
        [QueryJson] nvarchar(max) NOT NULL,
        [ModulesCsv] nvarchar(500) NOT NULL DEFAULT(N''),
        [IsDelete] bit NOT NULL DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL DEFAULT(GETDATE()),
        [UpdatedAt] datetime2 NOT NULL DEFAULT(GETDATE())
    );
    CREATE INDEX [IX_RsReports_Owner] ON [dbo].[RsReports] ([OwnerUserId]);
    CREATE INDEX [IX_RsReports_Visibility] ON [dbo].[RsReports] ([Visibility]);
END");

        await Safe(db, @"
IF OBJECT_ID(N'dbo.RsReportUserShares', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RsReportUserShares](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_RsReportUserShares] PRIMARY KEY,
        [ReportId] int NOT NULL,
        [UserId] int NOT NULL,
        [Access] int NOT NULL DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL DEFAULT(GETDATE()),
        CONSTRAINT [FK_RsReportUserShares_RsReports] FOREIGN KEY ([ReportId])
            REFERENCES [dbo].[RsReports] ([Id]) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX [UX_RsReportUserShares] ON [dbo].[RsReportUserShares] ([ReportId], [UserId]);
END");

        await Safe(db, @"
IF OBJECT_ID(N'dbo.RsReportRoleShares', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RsReportRoleShares](
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_RsReportRoleShares] PRIMARY KEY,
        [ReportId] int NOT NULL,
        [RoleId] int NOT NULL,
        [Access] int NOT NULL DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL DEFAULT(GETDATE()),
        CONSTRAINT [FK_RsReportRoleShares_RsReports] FOREIGN KEY ([ReportId])
            REFERENCES [dbo].[RsReports] ([Id]) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX [UX_RsReportRoleShares] ON [dbo].[RsReportRoleShares] ([ReportId], [RoleId]);
END");
    }

    // ==================== SQLite ====================
    private static async Task SqliteAsync(AppDbContext db)
    {
        await Safe(db, @"
CREATE TABLE IF NOT EXISTS RsReports (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Description TEXT NULL,
    Icon TEXT NOT NULL DEFAULT 'bi-file-earmark-bar-graph',
    Folder TEXT NULL,
    OwnerUserId INTEGER NOT NULL,
    Visibility INTEGER NOT NULL DEFAULT 0,
    QueryJson TEXT NOT NULL,
    ModulesCsv TEXT NOT NULL DEFAULT '',
    IsDelete INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);");
        await Safe(db, "CREATE INDEX IF NOT EXISTS IX_RsReports_Owner ON RsReports (OwnerUserId);");

        await Safe(db, @"
CREATE TABLE IF NOT EXISTS RsReportUserShares (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ReportId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    Access INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    CONSTRAINT FK_RsReportUserShares_RsReports FOREIGN KEY (ReportId)
        REFERENCES RsReports (Id) ON DELETE CASCADE
);");
        await Safe(db, "CREATE UNIQUE INDEX IF NOT EXISTS UX_RsReportUserShares ON RsReportUserShares (ReportId, UserId);");

        await Safe(db, @"
CREATE TABLE IF NOT EXISTS RsReportRoleShares (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ReportId INTEGER NOT NULL,
    RoleId INTEGER NOT NULL,
    Access INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    CONSTRAINT FK_RsReportRoleShares_RsReports FOREIGN KEY (ReportId)
        REFERENCES RsReports (Id) ON DELETE CASCADE
);");
        await Safe(db, "CREATE UNIQUE INDEX IF NOT EXISTS UX_RsReportRoleShares ON RsReportRoleShares (ReportId, RoleId);");
    }

    private static async Task Safe(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("already exists", StringComparison.OrdinalIgnoreCase)) return;
            LastError = m;
            Console.WriteLine($"[DB] ReportStudioSchemaV1: {m}");
        }
    }
}
