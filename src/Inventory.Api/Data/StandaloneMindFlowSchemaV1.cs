using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Schema for independent MindFlows and their viewer/editor shares, in the Inventory database.</summary>
public static class StandaloneMindFlowSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS StandaloneMindFlows (
    Id INTEGER NOT NULL CONSTRAINT PK_StandaloneMindFlows PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NOT NULL,
    OwnerUserId INTEGER NOT NULL,
    OwnerName TEXT NOT NULL DEFAULT '',
    Title TEXT NOT NULL,
    MapJson TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_StandaloneMindFlows_CompanyId_OwnerUserId ON StandaloneMindFlows (CompanyId, OwnerUserId);
CREATE TABLE IF NOT EXISTS StandaloneMindFlowMembers (
    Id INTEGER NOT NULL CONSTRAINT PK_StandaloneMindFlowMembers PRIMARY KEY AUTOINCREMENT,
    MindFlowId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    Role TEXT NOT NULL DEFAULT 'Viewer',
    AddedByUserId INTEGER NOT NULL,
    AddedAt TEXT NOT NULL,
    CONSTRAINT FK_StandaloneMindFlowMembers_StandaloneMindFlows_MindFlowId
        FOREIGN KEY (MindFlowId) REFERENCES StandaloneMindFlows (Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_StandaloneMindFlowMembers_MindFlowId_UserId ON StandaloneMindFlowMembers (MindFlowId, UserId);
CREATE INDEX IF NOT EXISTS IX_StandaloneMindFlowMembers_UserId ON StandaloneMindFlowMembers (UserId);");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.StandaloneMindFlows', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StandaloneMindFlows (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StandaloneMindFlows PRIMARY KEY,
        CompanyId int NOT NULL,
        OwnerUserId int NOT NULL,
        OwnerName nvarchar(150) NOT NULL CONSTRAINT DF_StandaloneMindFlows_OwnerName DEFAULT N'',
        Title nvarchar(200) NOT NULL,
        MapJson nvarchar(max) NOT NULL,
        CreatedAt datetime2 NOT NULL,
        UpdatedAt datetime2 NOT NULL
    );
    CREATE INDEX IX_StandaloneMindFlows_CompanyId_OwnerUserId ON dbo.StandaloneMindFlows (CompanyId, OwnerUserId);
END;
IF OBJECT_ID(N'dbo.StandaloneMindFlowMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StandaloneMindFlowMembers (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StandaloneMindFlowMembers PRIMARY KEY,
        MindFlowId int NOT NULL,
        UserId int NOT NULL,
        Role nvarchar(20) NOT NULL CONSTRAINT DF_StandaloneMindFlowMembers_Role DEFAULT N'Viewer',
        AddedByUserId int NOT NULL,
        AddedAt datetime2 NOT NULL,
        CONSTRAINT FK_StandaloneMindFlowMembers_StandaloneMindFlows_MindFlowId
            FOREIGN KEY (MindFlowId) REFERENCES dbo.StandaloneMindFlows(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_StandaloneMindFlowMembers_MindFlowId_UserId UNIQUE (MindFlowId, UserId)
    );
    CREATE INDEX IX_StandaloneMindFlowMembers_UserId ON dbo.StandaloneMindFlowMembers (UserId);
END;");
    }
}
