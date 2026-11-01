using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// شمای ثابت نسخهٔ اول چت برای SQLite. این نسخه را پس از انتشار تغییر ندهید؛
/// تغییرهای بعدی باید ارتقای جداگانه داشته باشند. EnsureCreated دیتابیس موجود را ارتقا نمی‌دهد.
/// فقط جدول‌ها و ایندکس‌های مفقود ساخته می‌شوند؛ هیچ کاربر یا پیامی حذف نمی‌شود.
/// </summary>
public static class ChatSchemaV1
{
    public static Task EnsureSqliteAsync(AppDbContext db) => db.Database.IsSqlite()
        ? db.Database.ExecuteSqlRawAsync(SqliteCreateSql)
        : Task.CompletedTask;

    public const string SqliteCreateSql = """
        CREATE TABLE IF NOT EXISTS "ChatConversations" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ChatConversations" PRIMARY KEY AUTOINCREMENT,
            "Title" TEXT NOT NULL,
            "Type" INTEGER NOT NULL DEFAULT 1,
            "Description" TEXT NULL,
            "AvatarUrl" TEXT NULL,
            "CreatedByUserId" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            "LastMessageAt" TEXT NOT NULL,
            "LastMessageSnippet" TEXT NULL,
            "LastMessageSenderId" INTEGER NULL,
            "LastMessageSenderName" TEXT NULL,
            "PinnedMessageId" INTEGER NULL,
            "IsArchived" INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS "ChatMembers" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ChatMembers" PRIMARY KEY AUTOINCREMENT,
            "ConversationId" INTEGER NOT NULL,
            "UserId" INTEGER NOT NULL,
            "UserName" TEXT NOT NULL,
            "UserDisplayName" TEXT NOT NULL,
            "UserAvatarUrl" TEXT NULL,
            "Role" INTEGER NOT NULL DEFAULT 3,
            "JoinedAt" TEXT NOT NULL,
            "LastReadMessageId" INTEGER NOT NULL DEFAULT 0,
            "UnreadCount" INTEGER NOT NULL DEFAULT 0,
            "IsMuted" INTEGER NOT NULL DEFAULT 0,
            "IsPinned" INTEGER NOT NULL DEFAULT 0,
            "IsArchived" INTEGER NOT NULL DEFAULT 0,
            CONSTRAINT "FK_ChatMembers_ChatConversations_ConversationId"
                FOREIGN KEY ("ConversationId") REFERENCES "ChatConversations" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS "ChatMessages" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ChatMessages" PRIMARY KEY AUTOINCREMENT,
            "ConversationId" INTEGER NOT NULL,
            "SenderUserId" INTEGER NOT NULL,
            "SenderName" TEXT NOT NULL,
            "SenderAvatarUrl" TEXT NULL,
            "Text" TEXT NULL,
            "MessageType" INTEGER NOT NULL DEFAULT 1,
            "FileUrl" TEXT NULL,
            "FileName" TEXT NULL,
            "FileSizeBytes" INTEGER NULL,
            "FileContentType" TEXT NULL,
            "ReplyToMessageId" INTEGER NULL,
            "ReplyToSenderName" TEXT NULL,
            "ReplyToSnippet" TEXT NULL,
            "ForwardFromMessageId" INTEGER NULL,
            "ForwardFromSenderName" TEXT NULL,
            "ErpModule" TEXT NULL,
            "ErpEntityId" TEXT NULL,
            "ErpEntityTitle" TEXT NULL,
            "ErpEntitySummary" TEXT NULL,
            "IsEdited" INTEGER NOT NULL DEFAULT 0,
            "EditedAt" TEXT NULL,
            "IsDeleted" INTEGER NOT NULL DEFAULT 0,
            "DeletedAt" TEXT NULL,
            "IsPinned" INTEGER NOT NULL DEFAULT 0,
            "ReactionsJson" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            CONSTRAINT "FK_ChatMessages_ChatConversations_ConversationId"
                FOREIGN KEY ("ConversationId") REFERENCES "ChatConversations" ("Id") ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS "IX_ChatConversations_LastMessageAt" ON "ChatConversations" ("LastMessageAt");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ChatMembers_ConversationId_UserId" ON "ChatMembers" ("ConversationId", "UserId");
        CREATE INDEX IF NOT EXISTS "IX_ChatMembers_UserId" ON "ChatMembers" ("UserId");
        CREATE INDEX IF NOT EXISTS "IX_ChatMessages_ConversationId_CreatedAt" ON "ChatMessages" ("ConversationId", "CreatedAt");
        CREATE INDEX IF NOT EXISTS "IX_ChatMessages_SenderUserId" ON "ChatMessages" ("SenderUserId");
        """;
}
