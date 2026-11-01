using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ستون‌های «تبدیل ویس به متن» روی جدول پیام‌های چت (ChatMessages).
///
/// چرا به‌صورت خودتعمیر و نه مایگریشن جدید؟ چون همان‌طور که در DbInitializer توضیح داده شده،
/// روی دیتابیس‌های موجود، مایگریشن squash شده «stamp و رد» می‌شود و جدول/ستون جدید هرگز
/// ساخته نمی‌شود. این کلاس همان الگوی Ensure* بقیهٔ ماژول‌ها را دارد: idempotent و بی‌خطر.
/// </summary>
public static class ChatTranscriptSchemaV1
{
    /// <summary>
    /// SQL Server: شرط COL_LENGTH باعث می‌شود اجرای مجددِ برنامه دستور ALTER نامعتبر نزند.
    /// نسخهٔ قبلی خطای «ستون از قبل وجود دارد» را catch می‌کرد، اما EF پیش از catch آن را با
    /// سطح fail در لاگ ثبت می‌کرد و تشخیص خطاهای واقعی را دشوار می‌ساخت.
    /// </summary>
    public static Task EnsureSqlServerAsync(AppDbContext db) =>
        db.Database.IsSqlite()
            ? Task.CompletedTask
            : db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.ChatMessages', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.ChatMessages', N'Transcript') IS NULL
    ALTER TABLE [dbo].[ChatMessages] ADD [Transcript] nvarchar(max) NULL;
IF OBJECT_ID(N'dbo.ChatMessages', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.ChatMessages', N'TranscribedAt') IS NULL
    ALTER TABLE [dbo].[ChatMessages] ADD [TranscribedAt] datetime2 NULL;");

    /// <summary>SQLite معادل idempotent — فقط ستون‌های واقعاً جاافتاده را اضافه می‌کند.</summary>
    public static async Task EnsureSqliteAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite()) return;
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone) await connection.OpenAsync();
        try
        {
            await using var table = connection.CreateCommand();
            table.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ChatMessages';";
            if (Convert.ToInt64(await table.ExecuteScalarAsync() ?? 0L) == 0) return;

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var info = connection.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info('ChatMessages');";
                await using var reader = await info.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }

            foreach (var (name, type) in new[] { ("Transcript", "TEXT"), ("TranscribedAt", "TEXT") })
            {
                if (columns.Contains(name)) continue;
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE ChatMessages ADD COLUMN {name} {type} NULL;";
                await alter.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
    }
}
