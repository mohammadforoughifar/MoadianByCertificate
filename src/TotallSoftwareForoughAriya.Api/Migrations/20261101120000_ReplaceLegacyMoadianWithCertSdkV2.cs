using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261101120000_ReplaceLegacyMoadianWithCertSdkV2")]
    public partial class ReplaceLegacyMoadianWithCertSdkV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MoadianCertSchemaV2.SqlServerDropLegacyTables);
            migrationBuilder.Sql(MoadianCertSchemaV2.SqlServerCreateTables);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS [dbo].[MdnSubmissionLogs];
DROP TABLE IF EXISTS [dbo].[MdnInvoicePayments];
DROP TABLE IF EXISTS [dbo].[MdnInvoiceLines];
DROP TABLE IF EXISTS [dbo].[MdnInvoices];
DROP TABLE IF EXISTS [dbo].[MdnCatalogItems];
DROP TABLE IF EXISTS [dbo].[MdnBuyers];
DROP TABLE IF EXISTS [dbo].[MdnTaxpayerProfiles];
DROP TABLE IF EXISTS [dbo].[MdnMeasureUnits];
");
        }
    }
}
