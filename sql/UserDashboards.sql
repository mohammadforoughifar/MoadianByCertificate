/* =====================================================================
   اسکریپت دستی ساخت جداول «داشبورد شخصی» و «گزارش‌ساز»
   ---------------------------------------------------------------------
   چه زمانی لازم است؟
   اگر در صفحهٔ «گزارش‌ساز» هنگام ذخیره خطا دیدید، یا صفحهٔ «داشبورد من»
   خالی بود و در لاگِ شروع API یکی از این پیام‌ها آمده بود:
       [DB] ⚠ جدول‌های داشبورد شخصی (UserDashboards/UserDashWidgets) ...
       [DB] DashboardSchemaV1: ...
   یعنی کاربرِ رشتهٔ اتصال، مجوز ساخت جدول (DDL) نداشته است.

   نحوهٔ اجرا:
   1) SSMS را با یک کاربر دارای مجوز db_owner روی دیتابیس InventoryDb باز کنید.
   2) روی دیتابیسِ درست (همان که در ConnectionStrings:Default است) اجرا کنید.
   3) API را یک بار ری‌استارت کنید.

   اسکریپت idempotent است؛ اجرای چندباره مشکلی ایجاد نمی‌کند.
   ===================================================================== */

/* ---------- داشبورد شخصی ---------- */
IF OBJECT_ID(N'dbo.UserDashboards', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserDashboards](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [UserId] int NOT NULL,
        [Name] nvarchar(120) NOT NULL DEFAULT(N'داشبورد من'),
        [IsDefault] bit NOT NULL DEFAULT(0),
        [SortOrder] int NOT NULL DEFAULT(0),
        [GridColumns] int NOT NULL DEFAULT(12),
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        [UpdatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_UserDashboards_UserId] ON [dbo].[UserDashboards] ([UserId]);
    PRINT 'UserDashboards ساخته شد.';
END
ELSE PRINT 'UserDashboards از قبل وجود دارد.';
GO

IF OBJECT_ID(N'dbo.UserDashWidgets', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserDashWidgets](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [DashboardId] int NOT NULL,
        [WidgetKey] nvarchar(60) NOT NULL,
        [Title] nvarchar(160) NULL,
        [Row] int NOT NULL DEFAULT(0),
        [Col] int NOT NULL DEFAULT(0),
        [W] int NOT NULL DEFAULT(3),
        [H] int NOT NULL DEFAULT(2),
        [SortOrder] int NOT NULL DEFAULT(0),
        [ChartType] int NULL,
        [Range] int NOT NULL DEFAULT(2),
        [ConfigJson] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        CONSTRAINT [FK_UserDashWidgets_UserDashboards] FOREIGN KEY ([DashboardId])
            REFERENCES [dbo].[UserDashboards] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_UserDashWidgets_DashboardId] ON [dbo].[UserDashWidgets] ([DashboardId]);
    PRINT 'UserDashWidgets ساخته شد.';
END
ELSE PRINT 'UserDashWidgets از قبل وجود دارد.';
GO


/* ---------- راستی‌آزمایی ---------- */
SELECT t.name AS TableName, p.rows AS RowCounts
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
WHERE t.name IN ('UserDashboards','UserDashWidgets')
ORDER BY t.name;
GO
