using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// تکمیل خودتعمیرکننده اسکیمای آرشیو اسناد — موج دوم امکانات:
///   ۱) ستون «واترمارک پیش‌نمایش» روی Documents (per-document، توسط مدیر مدرک)
///   ۲) جدول درخواست دسترسی (DocAccessRequests)
///   ۳) جدول تنظیمات شماره‌گذار خودکار کد مدرک (DocCodeSettings — تک‌ردیف Id=1)
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class DocArchiveSecuritySchemaV2
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
            Console.WriteLine($"[DB] DocArchiveSecuritySchemaV2 خطا: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ================== SQLite ==================

    private static async Task EnsureSqliteAsync(string connectionString)
    {
        using var raw = new SqliteConnection(connectionString);
        raw.Open();

        List<string> ColumnsOf(string table)
        {
            var cols = new List<string>();
            using var c = raw.CreateCommand();
            c.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
            using var rd = c.ExecuteReader();
            while (rd.Read()) cols.Add(rd.GetString(0));
            return cols;
        }

        void Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        if (!ColumnsOf("Documents").Contains("WatermarkPreview"))
        {
            Exec("ALTER TABLE Documents ADD COLUMN WatermarkPreview INTEGER NOT NULL DEFAULT 0");
            Console.WriteLine("[DB] SQLite: ستون Documents.WatermarkPreview اضافه شد.");
        }

        Exec(@"
            CREATE TABLE IF NOT EXISTS DocAccessRequests (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                DocumentId INTEGER NOT NULL,
                RequesterUserId INTEGER NOT NULL,
                RequesterName TEXT NOT NULL,
                RequestedLevel INTEGER NOT NULL,
                Note TEXT,
                Status INTEGER NOT NULL DEFAULT 0,
                HandledByUserId INTEGER,
                HandledByName TEXT,
                HandlerNote TEXT,
                CreatedAt TEXT NOT NULL,
                HandledAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_DocAccessRequests_DocumentId_Status ON DocAccessRequests (DocumentId, Status);
            CREATE INDEX IF NOT EXISTS IX_DocAccessRequests_RequesterUserId_Status ON DocAccessRequests (RequesterUserId, Status);

            CREATE TABLE IF NOT EXISTS DocCodeSettings (
                Id INTEGER NOT NULL PRIMARY KEY,
                Enabled INTEGER NOT NULL DEFAULT 0,
                Prefix TEXT NOT NULL DEFAULT 'DOC-',
                Padding INTEGER NOT NULL DEFAULT 5,
                NextNumber INTEGER NOT NULL DEFAULT 1,
                BackfillRanAt TEXT,
                BackfillRanByName TEXT,
                BackfillAssignedCount INTEGER NOT NULL DEFAULT 0
            );
            INSERT OR IGNORE INTO DocCodeSettings (Id, Enabled, Prefix, Padding, NextNumber, BackfillAssignedCount)
                VALUES (1, 0, 'DOC-', 5, 1, 0);");
        await Task.CompletedTask;
    }

    // ================== SQL Server ==================

    private static async Task EnsureSqlServerAsync(string connectionString)
    {
        using var raw = new SqlConnection(connectionString);
        await raw.OpenAsync();

        async Task ExecAsync(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            await c.ExecuteNonQueryAsync();
        }

        await ExecAsync(@"
            IF COL_LENGTH('dbo.Documents', 'WatermarkPreview') IS NULL
                ALTER TABLE [Documents] ADD [WatermarkPreview] bit NOT NULL CONSTRAINT [DF_Documents_WatermarkPreview] DEFAULT(0);");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.DocAccessRequests', N'U') IS NULL
            BEGIN
                CREATE TABLE [DocAccessRequests] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [DocumentId] int NOT NULL,
                    [RequesterUserId] int NOT NULL,
                    [RequesterName] nvarchar(150) NOT NULL,
                    [RequestedLevel] int NOT NULL,
                    [Note] nvarchar(500) NULL,
                    [Status] int NOT NULL CONSTRAINT [DF_DocAccessRequests_Status] DEFAULT(0),
                    [HandledByUserId] int NULL,
                    [HandledByName] nvarchar(150) NULL,
                    [HandlerNote] nvarchar(500) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [HandledAt] datetime2 NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocAccessRequests_DocumentId_Status' AND object_id = OBJECT_ID(N'dbo.DocAccessRequests'))
                CREATE INDEX [IX_DocAccessRequests_DocumentId_Status] ON [DocAccessRequests] ([DocumentId], [Status]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocAccessRequests_RequesterUserId_Status' AND object_id = OBJECT_ID(N'dbo.DocAccessRequests'))
                CREATE INDEX [IX_DocAccessRequests_RequesterUserId_Status] ON [DocAccessRequests] ([RequesterUserId], [Status]);");

        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.DocCodeSettings', N'U') IS NULL
            BEGIN
                CREATE TABLE [DocCodeSettings] (
                    [Id] int NOT NULL PRIMARY KEY,
                    [Enabled] bit NOT NULL CONSTRAINT [DF_DocCodeSettings_Enabled] DEFAULT(0),
                    [Prefix] nvarchar(10) NOT NULL CONSTRAINT [DF_DocCodeSettings_Prefix] DEFAULT(N'DOC-'),
                    [Padding] int NOT NULL CONSTRAINT [DF_DocCodeSettings_Padding] DEFAULT(5),
                    [NextNumber] int NOT NULL CONSTRAINT [DF_DocCodeSettings_NextNumber] DEFAULT(1),
                    [BackfillRanAt] datetime2 NULL,
                    [BackfillRanByName] nvarchar(150) NULL,
                    [BackfillAssignedCount] int NOT NULL CONSTRAINT [DF_DocCodeSettings_BackfillAssignedCount] DEFAULT(0)
                );
            END
            IF NOT EXISTS (SELECT 1 FROM [DocCodeSettings] WHERE [Id] = 1)
                INSERT INTO [DocCodeSettings] ([Id], [Enabled], [Prefix], [Padding], [NextNumber], [BackfillAssignedCount])
                VALUES (1, 0, N'DOC-', 5, 1, 0);");
    }
}
