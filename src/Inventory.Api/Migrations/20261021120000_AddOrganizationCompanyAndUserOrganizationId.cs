using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261021120000_AddOrganizationCompanyAndUserOrganizationId")]
    public partial class AddOrganizationCompanyAndUserOrganizationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: supports old installs already repaired at startup as well as clean databases.
            migrationBuilder.Sql(OrganizationSchemaV1.SqlServerOrganizations);
            migrationBuilder.Sql(OrganizationSchemaV1.SqlServerColumns);
            migrationBuilder.Sql(OrganizationSchemaV1.SqlServerRelations);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemUsers_Organizations_OrganizationId",
                table: "SystemUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_SystemCompanies_CompanyId",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_SystemUsers_OrganizationId",
                table: "SystemUsers");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_CompanyId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "SystemUsers");
        }
    }
}
