using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// =====================================================================
// اسکیمای جداول هوش مصنوعی فروغ آریا — SQL Server و SQLite
// =====================================================================
public static class AiSchemaV1
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<AiConversation>().HasIndex(c => new { c.UserId, c.LastMessageAtUtc });
        model.Entity<AiMessage>().HasIndex(m => m.ConversationId);
        model.Entity<AiKnowledgeDoc>().HasIndex(d => d.DocKey).IsUnique();
        model.Entity<AiPendingAction>().HasIndex(a => new { a.UserId, a.Status });
        model.Entity<AiReminder>().HasIndex(r => new { r.UserId, r.IsSent });
        model.Entity<AiReportSchedule>().HasIndex(x => new { x.IsActive, x.NextRunAt });
        model.Entity<AiDocEmbedding>().HasIndex(x => new { x.DocType, x.DocId }).IsUnique();
        model.Entity<AiAlertRule>().HasIndex(x => x.IsActive);
        model.Entity<AiFeedback>().HasIndex(x => new { x.MessageId, x.UserId }).IsUnique();
        model.Entity<AiAuditLog>().HasIndex(x => x.CreatedAt);
    }

    public static async Task EnsureAsync(AppDbContext db)
    {
        var sqlite = db.Database.IsSqlite();
        string T(string cols) => cols
            .Replace("IDKEY", sqlite ? "INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT" : "INT NOT NULL IDENTITY PRIMARY KEY")
            .Replace("DT", sqlite ? "TEXT" : "datetime2")
            .Replace("BIGTEXT", sqlite ? "TEXT" : "NVARCHAR(MAX)")
            .Replace("BOOL", sqlite ? "INTEGER NOT NULL" : "BIT NOT NULL");

        var conv = T("Id IDKEY, UserId INT NOT NULL, Channel NVARCHAR(20) NOT NULL, Title NVARCHAR(200) NOT NULL, CreatedAtUtc DT NOT NULL, LastMessageAtUtc DT NOT NULL, IsArchived BOOL");
        var msg = T("Id IDKEY, ConversationId INT NOT NULL, Role NVARCHAR(20) NOT NULL, Content BIGTEXT NOT NULL, ToolsUsed NVARCHAR(500) NULL, UsedFallback BOOL, CreatedAtUtc DT NOT NULL");
        var doc = T("Id IDKEY, Category NVARCHAR(100) NOT NULL, Title NVARCHAR(200) NOT NULL, Content BIGTEXT NOT NULL, Link NVARCHAR(300) NULL, DocKey NVARCHAR(100) NOT NULL, EmbeddingJson BIGTEXT NULL, IsActive BOOL, UpdatedAtUtc DT NOT NULL");

        var act = T("Id IDKEY, UserId INT NOT NULL, Action NVARCHAR(40) NOT NULL, ArgsJson BIGTEXT NOT NULL, Summary BIGTEXT NOT NULL, Status INT NOT NULL, CreatedAtUtc DT NOT NULL, ExpiresAtUtc DT NOT NULL, DecidedAtUtc DT NULL, ResultText BIGTEXT NULL");
        var rem = T("Id IDKEY, UserId INT NOT NULL, Text NVARCHAR(500) NOT NULL, RemindAt DT NOT NULL, Recurrence INT NOT NULL, IsSent BOOL, Attempts INT NOT NULL, CreatedAt DT NOT NULL, SentAt DT NULL");
        var aud = T("Id IDKEY, UserId INT NOT NULL, ConversationId INT NOT NULL, Channel NVARCHAR(20) NOT NULL, UserMessage NVARCHAR(500) NOT NULL, ToolsUsed NVARCHAR(500) NULL, Success BOOL, UsedFallback BOOL, Error NVARCHAR(300) NULL, DurationMs INT NOT NULL, CreatedAt DT NOT NULL");
        var fb = T("Id IDKEY, MessageId INT NOT NULL, ConversationId INT NOT NULL, UserId INT NOT NULL, Rating INT NOT NULL, Reason NVARCHAR(30) NULL, Comment NVARCHAR(500) NULL, CreatedAt DT NOT NULL, UpdatedAt DT NULL");
        var alr = T("Id IDKEY, UserId INT NOT NULL, Title NVARCHAR(300) NOT NULL, QueryJson BIGTEXT NOT NULL, Agg NVARCHAR(10) NOT NULL, AggField NVARCHAR(100) NULL, Op NVARCHAR(10) NOT NULL, Value REAL NOT NULL, IsActive BOOL, LastState BOOL, LastCheckedAt DT NULL, LastFiredAt DT NULL, FailCount INT NOT NULL, CreatedAt DT NOT NULL");
        var emb = T("Id IDKEY, DocType NVARCHAR(20) NOT NULL, DocId INT NOT NULL, TextHash NVARCHAR(16) NOT NULL, VectorJson BIGTEXT NULL, UpdatedAt DT NOT NULL");
        var sch = T("Id IDKEY, UserId INT NOT NULL, Title NVARCHAR(300) NOT NULL, Kind NVARCHAR(20) NOT NULL, SpecJson BIGTEXT NOT NULL, ScheduleType NVARCHAR(20) NOT NULL, Day INT NOT NULL, Time NVARCHAR(5) NOT NULL, WantExcel BOOL, IsActive BOOL, NextRunAt DT NOT NULL, LastRunAt DT NULL, FailCount INT NOT NULL, CreatedAt DT NOT NULL");

        foreach (var (table, cols) in new[] { ("AiConversations", conv), ("AiMessages", msg), ("AiKnowledgeDocs", doc), ("AiPendingActions", act), ("AiReminders", rem), ("AiReportSchedules", sch), ("AiDocEmbeddings", emb), ("AiAlertRules", alr), ("AiFeedbacks", fb), ("AiAuditLogs", aud) })
        {
            var sql = sqlite
                ? $"CREATE TABLE IF NOT EXISTS {table} ({cols})"
                : $"IF OBJECT_ID(N'dbo.{table}',N'U') IS NULL CREATE TABLE {table} ({cols})";
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        // مهاجرت خودکار: در نسخه اول، ستون‌های بولی روی SQL Server اشتباهاً INT ساخته شدند؛
        // ولی EF برای bool نوع BIT می‌خواهد و هنگام خواندن خطای Int32→Boolean می‌داد.
        // مقادیر فقط ۰/۱ هستند پس تبدیل امن است و هیچ داده‌ای از دست نمی‌رود.
        if (!sqlite)
        {
            await AlterColumnToBitAsync(db, "AiConversations", "IsArchived");
            await AlterColumnToBitAsync(db, "AiMessages", "UsedFallback");
            await AlterColumnToBitAsync(db, "AiKnowledgeDocs", "IsActive");
        }

        // ستون‌های جدید برای دیتابیس‌های قدیمی (امن برای اجرای چندباره)
        await AddColumnIfMissingAsync(db, "AiKnowledgeDocs", "EmbeddingJson", sqlite ? "TEXT NULL" : "NVARCHAR(MAX) NULL");
        await AddColumnIfMissingAsync(db, "AiMessages", "UsedFallback", sqlite ? "INTEGER NOT NULL DEFAULT 0" : "BIT NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(db, "AiMessages", "ToolsUsed", "NVARCHAR(500) NULL");

        foreach (var (table, cols, unique) in new[]
                 {
                     ("AiConversations", "UserId, LastMessageAtUtc", false),
                     ("AiMessages", "ConversationId", false),
                     ("AiKnowledgeDocs", "DocKey", true),
                     ("AiPendingActions", "UserId, Status", false),
                     ("AiReminders", "UserId, IsSent", false),
                     ("AiReportSchedules", "IsActive, NextRunAt", false),
                     ("AiDocEmbeddings", "DocType, DocId", true),
                     ("AiAlertRules", "IsActive", false),
                     ("AiFeedbacks", "MessageId, UserId", true),
                     ("AiAuditLogs", "CreatedAt", false),
                 })
        {
            var name = "IX_" + table + "_" + cols.Replace(", ", "_");
            var sql = $"CREATE {(unique ? "UNIQUE " : "")}INDEX {(sqlite ? "IF NOT EXISTS " : "")}[{name}] ON [{table}] ({cols});";
            if (!sqlite) sql = $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'{name}' AND object_id=OBJECT_ID(N'dbo.{table}')) " + sql;
            await db.Database.ExecuteSqlRawAsync(sql);
        }
    }

    /// <summary>تبدیل ستون INT به BIT — فقط اگر هنوز BIT نشده باشد (امن برای اجرای چندباره).</summary>
    private static async Task AlterColumnToBitAsync(AppDbContext db, string table, string column)
    {
        await db.Database.ExecuteSqlRawAsync($"""
            IF EXISTS (SELECT 1 FROM sys.columns c
                       INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                       WHERE c.object_id = OBJECT_ID(N'dbo.{table}') AND c.name = N'{column}'
                         AND t.name IN (N'int', N'tinyint', N'smallint'))
                ALTER TABLE {table} ALTER COLUMN {column} BIT NOT NULL
            """);
    }

    private static async Task AddColumnIfMissingAsync(AppDbContext db, string table, string column, string type)
    {
        var sqlite = db.Database.IsSqlite();
        if (sqlite)
        {
            var cols = await db.Database.SqlQueryRaw<string>($"SELECT name FROM pragma_table_info('{table}')").ToListAsync();
            if (cols.Any(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase))) return;
            // SQLite نوع INT NOT NULL بدون DEFAULT را روی جدول پر قبول نمی‌کند؛ پس DEFAULT اضافه می‌کنیم
            var t = type.Replace("INT NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0");
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {t}");
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(
                $"IF COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL ALTER TABLE {table} ADD {column} {type}");
        }
    }
}
