using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261102120000_AddUsersOrganizationId")]
    public partial class AddUsersOrganizationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: adds dbo.Users.OrganizationId on databases created before OrganizationSchemaV1
            // included dbo.Users alongside dbo.SystemUsers. Separate batches avoid SQL Server compiling
            // CREATE INDEX before ALTER TABLE executes.
            migrationBuilder.Sql(OrganizationSchemaV1.SqlServerUsersOrganizationColumn);
            migrationBuilder.Sql(OrganizationSchemaV1.SqlServerUsersOrganizationIndex);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'IX_Users_OrganizationId')
    DROP INDEX [IX_Users_OrganizationId] ON [dbo].[Users];
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'OrganizationId') IS NOT NULL
    ALTER TABLE [dbo].[Users] DROP COLUMN [OrganizationId];
");
        }
    }
}
