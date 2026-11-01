-- Run in SSMS against the SAME application database after backup and stopping the app.
-- Does not choose a server/database, delete records, assign companies, or stamp EF migration history.
-- The updated application's idempotent migration can safely run after this repair.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 50001, N'Select the application database, not a system database.', 1;
IF OBJECT_ID(N'dbo.SystemUsers', N'U') IS NULL OR OBJECT_ID(N'dbo.SystemCompanies', N'U') IS NULL
    THROW 50002, N'Core application tables not found. Check the selected database.', 1;
SELECT DB_NAME() AS TargetDatabase;
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC(N'
            IF OBJECT_ID(N''dbo.Organizations'', N''U'') IS NULL
            BEGIN
                CREATE TABLE [Organizations] (
                    [OrganizationId] int NOT NULL IDENTITY PRIMARY KEY,
                    [NameUnit] nvarchar(200) NOT NULL,
                    [NameUniq] nvarchar(100) NOT NULL,
                    [IsDefault] bit NOT NULL CONSTRAINT [DF_Organizations_IsDefault] DEFAULT(0),
                    [IsActive] bit NOT NULL CONSTRAINT [DF_Organizations_IsActive] DEFAULT(1),
                    [IsDelete] bit NOT NULL CONSTRAINT [DF_Organizations_IsDelete] DEFAULT(0),
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N''IX_Organizations_NameUniq'' AND object_id = OBJECT_ID(N''dbo.Organizations''))
                CREATE UNIQUE INDEX [IX_Organizations_NameUniq] ON [Organizations] ([NameUniq]);');
    EXEC(N'
IF OBJECT_ID(N''dbo.Organizations'', N''U'') IS NOT NULL
   AND COL_LENGTH(N''dbo.Organizations'', N''CompanyId'') IS NULL
    ALTER TABLE [dbo].[Organizations] ADD [CompanyId] int NULL;
IF OBJECT_ID(N''dbo.SystemUsers'', N''U'') IS NOT NULL
   AND COL_LENGTH(N''dbo.SystemUsers'', N''OrganizationId'') IS NULL
    ALTER TABLE [dbo].[SystemUsers] ADD [OrganizationId] int NULL;
');
    EXEC(N'
IF OBJECT_ID(N''dbo.Organizations'', N''U'') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.Organizations'') AND name = N''IX_Organizations_CompanyId'')
        CREATE INDEX [IX_Organizations_CompanyId] ON [dbo].[Organizations] ([CompanyId]);
    IF OBJECT_ID(N''dbo.SystemCompanies'', N''U'') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N''dbo.Organizations'') AND name = N''FK_Organizations_SystemCompanies_CompanyId'')
        ALTER TABLE [dbo].[Organizations] WITH CHECK
            ADD CONSTRAINT [FK_Organizations_SystemCompanies_CompanyId]
            FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[SystemCompanies] ([Id]) ON DELETE NO ACTION;
END;
IF OBJECT_ID(N''dbo.SystemUsers'', N''U'') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.SystemUsers'') AND name = N''IX_SystemUsers_OrganizationId'')
        CREATE INDEX [IX_SystemUsers_OrganizationId] ON [dbo].[SystemUsers] ([OrganizationId]);
    IF OBJECT_ID(N''dbo.Organizations'', N''U'') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N''dbo.SystemUsers'') AND name = N''FK_SystemUsers_Organizations_OrganizationId'')
        ALTER TABLE [dbo].[SystemUsers] WITH CHECK
            ADD CONSTRAINT [FK_SystemUsers_Organizations_OrganizationId]
            FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([OrganizationId]) ON DELETE NO ACTION;
END;
');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
SELECT DB_NAME() AS DatabaseName,
       COL_LENGTH(N'dbo.Organizations', N'CompanyId') AS CompanyIdColumnLength,
       COL_LENGTH(N'dbo.SystemUsers', N'OrganizationId') AS OrganizationIdColumnLength;
