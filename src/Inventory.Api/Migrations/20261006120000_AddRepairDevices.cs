using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// چنددستگاهی‌کردن یک پذیرش. ستون‌های قدیمی روی RepairOrders برای سازگاری
    /// گزارش‌ها/نسخه‌های قبلی حفظ می‌شوند و اطلاعاتشان به کارت اول منتقل می‌شود.
    /// </summary>
    public partial class AddRepairDevices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.RepairOrders', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.RepairDevices', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RepairDevices](
        [Id] int NOT NULL IDENTITY(1,1),
        [RepairOrderId] int NOT NULL,
        [DeviceType] nvarchar(100) NOT NULL,
        [DeviceModel] nvarchar(200) NULL,
        [SerialNumber] nvarchar(100) NULL,
        [ProblemDescription] nvarchar(1000) NULL,
        [Accessories] nvarchar(500) NULL,
        [QuotedPrice] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_RepairDevices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RepairDevices_RepairOrders_RepairOrderId]
            FOREIGN KEY ([RepairOrderId]) REFERENCES [dbo].[RepairOrders] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.RepairDevices', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE [name] = N'IX_RepairDevices_RepairOrderId'
                  AND [object_id] = OBJECT_ID(N'dbo.RepairDevices'))
    CREATE INDEX [IX_RepairDevices_RepairOrderId] ON [dbo].[RepairDevices] ([RepairOrderId]);

IF OBJECT_ID(N'dbo.RepairOrders', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.RepairDevices', N'U') IS NOT NULL
BEGIN
    INSERT INTO [dbo].[RepairDevices]
        ([RepairOrderId], [DeviceType], [DeviceModel], [SerialNumber], [ProblemDescription], [Accessories], [QuotedPrice])
    SELECT r.[Id], r.[DeviceType], r.[DeviceModel], r.[SerialNumber],
           r.[ProblemDescription], r.[Accessories], r.[QuotedPrice]
    FROM [dbo].[RepairOrders] AS r
    WHERE NOT EXISTS (
        SELECT 1 FROM [dbo].[RepairDevices] AS d WHERE d.[RepairOrderId] = r.[Id]
    );
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // برگشت عمدی انجام نمی‌شود: ممکن است چند دستگاه ثبت شده باشد و حذف جدول اطلاعات را از بین ببرد.
        }
    }
}
