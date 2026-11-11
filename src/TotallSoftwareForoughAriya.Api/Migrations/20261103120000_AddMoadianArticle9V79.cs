using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261103120000_AddMoadianArticle9V79")]
    public partial class AddMoadianArticle9V79 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MoadianCertSchemaV2.SqlServerUpgradeArticle9Columns);
            migrationBuilder.Sql(MoadianCertSchemaV2.SqlServerUpgradeArticle9Indexes);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.MdnInvoices', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MdnInvoices') AND name = N'IX_MdnInvoices_CompanyId_IsArticle9')
    DROP INDEX [IX_MdnInvoices_CompanyId_IsArticle9] ON [dbo].[MdnInvoices];

IF OBJECT_ID(N'dbo.MdnInvoices', N'U') IS NOT NULL
BEGIN
    DECLARE @dfIsArt9 sysname;
    SELECT @dfIsArt9 = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.MdnInvoices') AND c.name = N'IsArticle9';
    IF @dfIsArt9 IS NOT NULL
        EXEC(N'ALTER TABLE [dbo].[MdnInvoices] DROP CONSTRAINT ' + QUOTENAME(@dfIsArt9));

    IF COL_LENGTH(N'dbo.MdnInvoices', N'Note2') IS NOT NULL ALTER TABLE [dbo].[MdnInvoices] DROP COLUMN [Note2];
    IF COL_LENGTH(N'dbo.MdnInvoices', N'Note1') IS NOT NULL ALTER TABLE [dbo].[MdnInvoices] DROP COLUMN [Note1];
    IF COL_LENGTH(N'dbo.MdnInvoices', N'SendRule') IS NOT NULL ALTER TABLE [dbo].[MdnInvoices] DROP COLUMN [SendRule];
    IF COL_LENGTH(N'dbo.MdnInvoices', N'IsArticle9') IS NOT NULL ALTER TABLE [dbo].[MdnInvoices] DROP COLUMN [IsArticle9];
    IF COL_LENGTH(N'dbo.MdnInvoices', N'RegistrationDate') IS NOT NULL ALTER TABLE [dbo].[MdnInvoices] DROP COLUMN [RegistrationDate];
END;

IF OBJECT_ID(N'dbo.MdnTaxpayerProfiles', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.MdnTaxpayerProfiles', N'Article9SendDeadlineDays') IS NOT NULL
BEGIN
    DECLARE @dfDeadline sysname;
    SELECT @dfDeadline = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.MdnTaxpayerProfiles') AND c.name = N'Article9SendDeadlineDays';
    IF @dfDeadline IS NOT NULL
        EXEC(N'ALTER TABLE [dbo].[MdnTaxpayerProfiles] DROP CONSTRAINT ' + QUOTENAME(@dfDeadline));

    ALTER TABLE [dbo].[MdnTaxpayerProfiles] DROP COLUMN [Article9SendDeadlineDays];
END;
");
        }
    }
}
