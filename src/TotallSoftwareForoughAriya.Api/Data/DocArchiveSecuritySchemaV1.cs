using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// تکمیل خودتعمیرکننده اسکیمای آرشیو اسناد برای امکانات امنیتی نسخه بعدی:
///   ۱) جدول لاگ مشاهده/دانلود پیوست‌ها (AppAttachmentAccessLogs)
///   ۲) ستون RequireDownloadConfirm روی Documents (تایید مجدد رمز برای دانلود/مشاهده فایل)
///   ۳) ستون RoleId روی DocFolderPermissions و DocumentPermissions (دسترسی گروهی/نقش‌محور)
///      و بازسازی ایندکس یکتا به‌شکل سه‌ستونی (…, UserId, RoleId).
/// همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند.
/// </summary>
public static class DocArchiveSecuritySchemaV1
{
    /// <summary>اجرای مناسب بر اساس Provider کانکشن فعلی.</summary>
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
            // نباید استارتاپ را متوقف کند؛ در صورت خطا فقط هشدار می‌دهیم.
            Console.WriteLine($"[DB] DocArchiveSecuritySchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
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

        // — ستون‌ها —
        if (!ColumnsOf("Documents").Contains("RequireDownloadConfirm"))
        {
            Exec("ALTER TABLE Documents ADD COLUMN RequireDownloadConfirm INTEGER NOT NULL DEFAULT 0");
            Console.WriteLine("[DB] SQLite: ستون Documents.RequireDownloadConfirm اضافه شد.");
        }

        if (!ColumnsOf("DocFolderPermissions").Contains("RoleId"))
        {
            Exec("ALTER TABLE DocFolderPermissions ADD COLUMN RoleId INTEGER NOT NULL DEFAULT 0");
            Console.WriteLine("[DB] SQLite: ستون DocFolderPermissions.RoleId اضافه شد.");
        }

        if (!ColumnsOf("DocumentPermissions").Contains("RoleId"))
        {
            Exec("ALTER TABLE DocumentPermissions ADD COLUMN RoleId INTEGER NOT NULL DEFAULT 0");
            Console.WriteLine("[DB] SQLite: ستون DocumentPermissions.RoleId اضافه شد.");
        }

        // — جدول لاگ دسترسی پیوست —
        Exec(@"
            CREATE TABLE IF NOT EXISTS AppAttachmentAccessLogs (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                AttachmentId INTEGER NOT NULL,
                Module TEXT NOT NULL,
                RefId INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                Action TEXT NOT NULL,
                UserId INTEGER NOT NULL,
                UserName TEXT NOT NULL,
                Ip TEXT,
                At TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_AppAttachmentAccessLogs_Module_RefId ON AppAttachmentAccessLogs (Module, RefId);
            CREATE INDEX IF NOT EXISTS IX_AppAttachmentAccessLogs_AttachmentId ON AppAttachmentAccessLogs (AttachmentId);
            CREATE INDEX IF NOT EXISTS IX_AppAttachmentAccessLogs_At ON AppAttachmentAccessLogs (At);");

        // — بازسازی ایندکس‌های یکتای دسترسی به‌شکل سه‌ستونی —
        Exec("DROP INDEX IF EXISTS IX_DocFolderPermissions_FolderId_UserId;");
        Exec("CREATE UNIQUE INDEX IF NOT EXISTS IX_DocFolderPermissions_FolderId_UserId_RoleId ON DocFolderPermissions (FolderId, UserId, RoleId);");
        Exec("DROP INDEX IF EXISTS IX_DocumentPermissions_DocumentId_UserId;");
        Exec("CREATE UNIQUE INDEX IF NOT EXISTS IX_DocumentPermissions_DocumentId_UserId_RoleId ON DocumentPermissions (DocumentId, UserId, RoleId);");
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

        // — ستون‌ها —
        await ExecAsync(@"
            IF COL_LENGTH('dbo.Documents', 'RequireDownloadConfirm') IS NULL
                ALTER TABLE [Documents] ADD [RequireDownloadConfirm] bit NOT NULL CONSTRAINT [DF_Documents_RequireDownloadConfirm] DEFAULT(0);
            IF COL_LENGTH('dbo.DocFolderPermissions', 'RoleId') IS NULL
                ALTER TABLE [DocFolderPermissions] ADD [RoleId] int NOT NULL CONSTRAINT [DF_DocFolderPermissions_RoleId] DEFAULT(0);
            IF COL_LENGTH('dbo.DocumentPermissions', 'RoleId') IS NULL
                ALTER TABLE [DocumentPermissions] ADD [RoleId] int NOT NULL CONSTRAINT [DF_DocumentPermissions_RoleId] DEFAULT(0);");

        // — جدول لاگ دسترسی پیوست —
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.AppAttachmentAccessLogs', N'U') IS NULL
            BEGIN
                CREATE TABLE [AppAttachmentAccessLogs] (
                    [Id] int NOT NULL IDENTITY PRIMARY KEY,
                    [AttachmentId] int NOT NULL,
                    [Module] nvarchar(50) NOT NULL,
                    [RefId] int NOT NULL,
                    [FileName] nvarchar(150) NOT NULL,
                    [Action] nvarchar(20) NOT NULL,
                    [UserId] int NOT NULL,
                    [UserName] nvarchar(150) NOT NULL,
                    [Ip] nvarchar(60) NULL,
                    [At] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppAttachmentAccessLogs_Module_RefId' AND object_id = OBJECT_ID(N'dbo.AppAttachmentAccessLogs'))
                CREATE INDEX [IX_AppAttachmentAccessLogs_Module_RefId] ON [AppAttachmentAccessLogs] ([Module], [RefId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppAttachmentAccessLogs_AttachmentId' AND object_id = OBJECT_ID(N'dbo.AppAttachmentAccessLogs'))
                CREATE INDEX [IX_AppAttachmentAccessLogs_AttachmentId] ON [AppAttachmentAccessLogs] ([AttachmentId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppAttachmentAccessLogs_At' AND object_id = OBJECT_ID(N'dbo.AppAttachmentAccessLogs'))
                CREATE INDEX [IX_AppAttachmentAccessLogs_At] ON [AppAttachmentAccessLogs] ([At]);");

        // — بازسازی ایندکس‌های یکتای دسترسی به‌شکل سه‌ستونی —
        await ExecAsync(@"
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocFolderPermissions_FolderId_UserId' AND object_id = OBJECT_ID(N'dbo.DocFolderPermissions'))
                DROP INDEX [IX_DocFolderPermissions_FolderId_UserId] ON [DocFolderPermissions];
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocFolderPermissions_FolderId_UserId_RoleId' AND object_id = OBJECT_ID(N'dbo.DocFolderPermissions'))
                CREATE UNIQUE INDEX [IX_DocFolderPermissions_FolderId_UserId_RoleId] ON [DocFolderPermissions] ([FolderId], [UserId], [RoleId]);
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentPermissions_DocumentId_UserId' AND object_id = OBJECT_ID(N'dbo.DocumentPermissions'))
                DROP INDEX [IX_DocumentPermissions_DocumentId_UserId] ON [DocumentPermissions];
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentPermissions_DocumentId_UserId_RoleId' AND object_id = OBJECT_ID(N'dbo.DocumentPermissions'))
                CREATE UNIQUE INDEX [IX_DocumentPermissions_DocumentId_UserId_RoleId] ON [DocumentPermissions] ([DocumentId], [UserId], [RoleId]);");
    }
}
