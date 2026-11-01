using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// §۶ نظرسنجی و رأی‌گیری.
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class FaComSchemaV2
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
            Console.WriteLine($"[DB] FaComSchemaV2 خطا: {ex.GetType().Name}: {ex.Message}");
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
            CREATE TABLE IF NOT EXISTS FaComPolls (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Description TEXT NULL,
                Audience INTEGER NOT NULL DEFAULT 0,
                OrgUnitId INTEGER NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CloseAt TEXT NULL,
                CreatedByUserId INTEGER NULL,
                CreatedByName TEXT NULL,
                CreatedAt TEXT NOT NULL
            );");
        Exec(@"
            CREATE TABLE IF NOT EXISTS FaComPollOptions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                PollId INTEGER NOT NULL,
                Text TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );");
        Exec("CREATE INDEX IF NOT EXISTS IX_FaComPollOpt_Poll ON FaComPollOptions (PollId);");
        Exec(@"
            CREATE TABLE IF NOT EXISTS FaComVotes (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                PollId INTEGER NOT NULL,
                OptionId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                VotedAt TEXT NOT NULL
            );");
        Exec("CREATE INDEX IF NOT EXISTS IX_FaComVote_PollUser ON FaComVotes (PollId, UserId);");
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
IF OBJECT_ID(N'FaComPolls', N'U') IS NULL
CREATE TABLE FaComPolls (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Title nvarchar(200) NOT NULL,
    Description nvarchar(1000) NULL,
    Audience int NOT NULL DEFAULT 0,
    OrgUnitId int NULL,
    IsActive bit NOT NULL DEFAULT 1,
    CloseAt datetime2 NULL,
    CreatedByUserId int NULL,
    CreatedByName nvarchar(150) NULL,
    CreatedAt datetime2 NOT NULL
);");
        await Exec(@"
IF OBJECT_ID(N'FaComPollOptions', N'U') IS NULL
CREATE TABLE FaComPollOptions (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    PollId int NOT NULL,
    Text nvarchar(300) NOT NULL,
    SortOrder int NOT NULL DEFAULT 0
);");
        await Exec(@"
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FaComPollOpt_Poll')
CREATE INDEX IX_FaComPollOpt_Poll ON FaComPollOptions (PollId);");
        await Exec(@"
IF OBJECT_ID(N'FaComVotes', N'U') IS NULL
CREATE TABLE FaComVotes (
    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    PollId int NOT NULL,
    OptionId int NOT NULL,
    UserId int NOT NULL,
    VotedAt datetime2 NOT NULL
);");
        await Exec(@"
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FaComVote_PollUser')
CREATE INDEX IX_FaComVote_PollUser ON FaComVotes (PollId, UserId);");
    }
}
