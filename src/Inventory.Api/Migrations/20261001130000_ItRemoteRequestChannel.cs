using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// کانال شرکت‌های راه‌دور: نسخه‌ی نرم‌افزارِ نصب‌شده در شرکتِ مشتری، درخواست‌های کاربرانش را
    /// (که در سرور مرکزی «کاربر» نیستند) با کلید API و کلید پایدارِ کاربر ارسال می‌کند.
    /// دستورها idempotent هستند تا روی دیتابیس‌های قدیمی هم امن باشند.
    /// </summary>
    public partial class ItRemoteRequestChannel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- ستون‌های تازه روی ItRequests ----------
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'SourceCompanyId') IS NULL
    ALTER TABLE dbo.ItRequests ADD SourceCompanyId int NULL;");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'ExternalRequesterKey') IS NULL
    ALTER TABLE dbo.ItRequests ADD ExternalRequesterKey nvarchar(40) NULL;");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'ExternalId') IS NULL
    ALTER TABLE dbo.ItRequests ADD ExternalId nvarchar(40) NULL;");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'RequesterPhone') IS NULL
    ALTER TABLE dbo.ItRequests ADD RequesterPhone nvarchar(30) NULL;");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'RequesterEmail') IS NULL
    ALTER TABLE dbo.ItRequests ADD RequesterEmail nvarchar(120) NULL;");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ItRequests', N'TrackToken') IS NULL
    ALTER TABLE dbo.ItRequests ADD TrackToken nvarchar(40) NULL;");

            // ---------- شرکت‌های مشتری (سمت سرور مرکزی) ----------
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItClientCompanies', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItClientCompanies](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Phone] nvarchar(50) NULL,
        [ApiKeyHash] nvarchar(100) NOT NULL CONSTRAINT [DF_ItClientCompanies_ApiKeyHash] DEFAULT(''),
        [ApiKeyHint] nvarchar(10) NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_ItClientCompanies_IsActive] DEFAULT(1),
        [HourlyLimit] int NOT NULL CONSTRAINT [DF_ItClientCompanies_HourlyLimit] DEFAULT(60),
        [LastSeenAt] datetime2 NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ItClientCompanies_CreatedAt] DEFAULT(GETDATE()),
        [KeyRotatedAt] datetime2 NULL
    );
    CREATE UNIQUE INDEX [IX_ItClientCompanies_Code] ON [dbo].[ItClientCompanies] ([Code]);
END");

            // ---------- ایندکس یکتای idempotency ----------
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItRequests', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ItRequests_ExternalId' AND object_id = OBJECT_ID(N'dbo.ItRequests'))
    CREATE UNIQUE INDEX [IX_ItRequests_ExternalId] ON [dbo].[ItRequests] ([SourceCompanyId], [ExternalId])
    WHERE [ExternalId] IS NOT NULL;");

            // ---------- تنظیم اتصال (سمت شرکت) ----------
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItRemoteConnections', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItRemoteConnections](
        [Id] int NOT NULL CONSTRAINT [PK_ItRemoteConnections] PRIMARY KEY,
        [ServerUrl] nvarchar(300) NULL,
        [CompanyCode] nvarchar(50) NULL,
        [CompanyName] nvarchar(200) NULL,
        [ApiKey] nvarchar(100) NULL,
        [Enabled] bit NOT NULL CONSTRAINT [DF_ItRemoteConnections_Enabled] DEFAULT(1),
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_ItRemoteConnections_UpdatedAt] DEFAULT(GETDATE())
    );
    INSERT INTO [dbo].[ItRemoteConnections] ([Id], [Enabled]) VALUES (1, 1);
END");

            // ---------- آینهٔ محلی درخواست‌های ارسالی ----------
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItRemoteRequests', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItRemoteRequests](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [RemoteNumber] nvarchar(30) NULL,
        [TrackToken] nvarchar(40) NULL,
        [ExternalId] nvarchar(40) NOT NULL,
        [RequesterKey] nvarchar(40) NOT NULL,
        [LocalUserId] int NOT NULL,
        [RequesterName] nvarchar(150) NOT NULL,
        [SystemLabel] nvarchar(250) NULL,
        [RequestType] nvarchar(30) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [FinalResponse] nvarchar(4000) NULL,
        [ManagerApprovedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [SyncedAt] datetime2 NULL,
        [LastError] nvarchar(500) NULL
    );
    CREATE INDEX [IX_ItRemoteRequests_LocalUserId] ON [dbo].[ItRemoteRequests] ([LocalUserId]);
    CREATE INDEX [IX_ItRemoteRequests_RemoteNumber] ON [dbo].[ItRemoteRequests] ([RemoteNumber]);
END");

            // ---------- کلید پایدار هر کاربر ----------
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.ItRemoteUserKeys', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ItRemoteUserKeys](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [UserId] int NOT NULL,
        [Key] nvarchar(40) NOT NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ItRemoteUserKeys_CreatedAt] DEFAULT(GETDATE())
    );
    CREATE UNIQUE INDEX [IX_ItRemoteUserKeys_UserId] ON [dbo].[ItRemoteUserKeys] ([UserId]);
END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // حذفِ جدول/ستون روی دیتابیسِ در حال استفاده، تاریخچهٔ درخواست‌های مشتری‌ها را نابود می‌کند
            // و با «ساختار فعلی» برنامه ناسازگار است؛ Down خالی است (Irreversible).
        }
    }
}
