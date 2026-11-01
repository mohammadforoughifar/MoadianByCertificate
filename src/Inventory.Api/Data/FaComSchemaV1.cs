using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۱۵ ارتباطات داخلی — اسکیمای FaCom:
/// FaComAnnouncements + FaComTickets + FaComReplies.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaComSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.GetDbConnection() is SqliteConnection sqlConn)
                await EnsureSqliteAsync(sqlConn.ConnectionString);
            else if (db.Database.GetDbConnection() is SqlConnection sqlServerConn)
                await EnsureSqlServerAsync(sqlServerConn.ConnectionString);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] FaComSchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ================== SQLite ==================

    private static async Task EnsureSqliteAsync(string connectionString)
    {
        using var raw = new SqliteConnection(connectionString);
        raw.Open();

        void Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        Exec(@"
            CREATE TABLE IF NOT EXISTS FaComAnnouncements (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Body TEXT NOT NULL,
                Audience INTEGER NOT NULL DEFAULT 0,
                OrgUnitId INTEGER NULL,
                PublishFrom TEXT NULL,
                PublishTo TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedByUserId INTEGER NULL,
                CreatedByName TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS FaComTickets (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                EmployeeId INTEGER NOT NULL,
                Subject TEXT NOT NULL,
                Body TEXT NOT NULL,
                Category INTEGER NOT NULL DEFAULT 0,
                Priority INTEGER NOT NULL DEFAULT 1,
                Status INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                ClosedAt TEXT NULL,
                ClosedByName TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaComTicket_Emp ON FaComTickets (EmployeeId);
            CREATE TABLE IF NOT EXISTS FaComReplies (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                TicketId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                UserName TEXT NOT NULL,
                Body TEXT NOT NULL,
                IsHrReply INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_FaComReply_Ticket ON FaComReplies (TicketId);");

        await Task.CompletedTask;
    }

    // ================== SQL Server ==================

    private static async Task EnsureSqlServerAsync(string connectionString)
    {
        using var raw = new SqlConnection(connectionString);
        await raw.OpenAsync();

        async Task Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            await c.ExecuteNonQueryAsync();
        }

        await Exec(@"
IF OBJECT_ID(N'FaComAnnouncements', N'U') IS NULL
CREATE TABLE FaComAnnouncements (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Title nvarchar(200) NOT NULL,
    Body nvarchar(4000) NOT NULL,
    Audience int NOT NULL DEFAULT 0,
    OrgUnitId int NULL,
    PublishFrom datetime2 NULL,
    PublishTo datetime2 NULL,
    IsActive bit NOT NULL DEFAULT 1,
    CreatedByUserId int NULL,
    CreatedByName nvarchar(150) NULL,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF OBJECT_ID(N'FaComTickets', N'U') IS NULL
CREATE TABLE FaComTickets (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    EmployeeId int NOT NULL,
    Subject nvarchar(200) NOT NULL,
    Body nvarchar(2000) NOT NULL,
    Category int NOT NULL DEFAULT 0,
    Priority int NOT NULL DEFAULT 1,
    Status int NOT NULL DEFAULT 0,
    CreatedAt datetime2 NOT NULL,
    ClosedAt datetime2 NULL,
    ClosedByName nvarchar(150) NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaComTicket_Emp' AND object_id = OBJECT_ID(N'FaComTickets'))
CREATE INDEX IX_FaComTicket_Emp ON FaComTickets (EmployeeId);");
        await Exec(@"
IF OBJECT_ID(N'FaComReplies', N'U') IS NULL
CREATE TABLE FaComReplies (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    TicketId int NOT NULL,
    UserId int NULL,
    UserName nvarchar(150) NOT NULL,
    Body nvarchar(2000) NOT NULL,
    IsHrReply bit NOT NULL DEFAULT 0,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FaComReply_Ticket' AND object_id = OBJECT_ID(N'FaComReplies'))
CREATE INDEX IX_FaComReply_Ticket ON FaComReplies (TicketId);");
    }
}
