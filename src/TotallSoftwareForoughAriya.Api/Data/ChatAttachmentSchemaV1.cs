using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>نسخهٔ ثابت شمای پیوست چت برای ارتقای SQLite موجود؛ بدون حذف فایل یا پیام.</summary>
public static class ChatAttachmentSchemaV1
{
    public static Task EnsureSqliteAsync(AppDbContext db) => db.Database.IsSqlite()
        ? db.Database.ExecuteSqlRawAsync(SqliteCreateSql)
        : Task.CompletedTask;

    public const string SqliteCreateSql = """
        CREATE TABLE IF NOT EXISTS "ChatAttachments" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_ChatAttachments" PRIMARY KEY,
            "UploadedByUserId" INTEGER NOT NULL,
            "ConversationId" INTEGER NULL,
            "FileName" TEXT NOT NULL,
            "ContentType" TEXT NOT NULL,
            "SizeBytes" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_ChatAttachments_ConversationId" ON "ChatAttachments" ("ConversationId");
        CREATE INDEX IF NOT EXISTS "IX_ChatAttachments_UploadedByUserId" ON "ChatAttachments" ("UploadedByUserId");
        """;
}
