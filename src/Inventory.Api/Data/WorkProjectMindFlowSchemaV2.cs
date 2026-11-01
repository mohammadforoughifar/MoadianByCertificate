using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>جدول دعوت‌های مستقیم به MindFlow؛ به اعضای دعوت‌شده فقط دسترسی مشاهده می‌دهد.</summary>
public static class WorkProjectMindFlowSchemaV2
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS WorkProjectMindFlowMembers (
    Id INTEGER NOT NULL CONSTRAINT PK_WorkProjectMindFlowMembers PRIMARY KEY AUTOINCREMENT,
    ProjectId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    AddedByUserId INTEGER NOT NULL,
    AddedAt TEXT NOT NULL,
    CONSTRAINT FK_WorkProjectMindFlowMembers_WorkProjects_ProjectId
        FOREIGN KEY (ProjectId) REFERENCES WorkProjects (Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkProjectMindFlowMembers_ProjectId_UserId
    ON WorkProjectMindFlowMembers (ProjectId, UserId);");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.WorkProjectMindFlowMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkProjectMindFlowMembers (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkProjectMindFlowMembers PRIMARY KEY,
        ProjectId int NOT NULL,
        UserId int NOT NULL,
        AddedByUserId int NOT NULL,
        AddedAt datetime2 NOT NULL,
        CONSTRAINT FK_WorkProjectMindFlowMembers_WorkProjects_ProjectId
            FOREIGN KEY (ProjectId) REFERENCES dbo.WorkProjects(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_WorkProjectMindFlowMembers_ProjectId_UserId UNIQUE (ProjectId, UserId)
    );
END;");
    }
}
