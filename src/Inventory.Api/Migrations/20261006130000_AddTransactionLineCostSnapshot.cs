using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>هزینهٔ واحدِ قطعی را برای محاسبهٔ درست سودِ فاکتورهای تعمیرات روی سطر فروش نگه می‌دارد.</summary>
    public partial class AddTransactionLineCostSnapshot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.TransactionLines', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.TransactionLines', N'UnitCostSnapshot') IS NULL
    ALTER TABLE [dbo].[TransactionLines] ADD [UnitCostSnapshot] decimal(18,2) NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // عمداً ستون حذف نمی‌شود؛ حذف آن، snapshot هزینهٔ واقعیِ فاکتورهای تعمیر را از بین می‌برد.
        }
    }
}
