using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Migrations
{
    /// <summary>
    /// تخصیص سند دریافت/پرداخت به چند فاکتور و پشتیبانی از تسویهٔ مرحله‌ای.
    /// ستون InvoiceId قبلی برای سازگاری نگه داشته می‌شود و اطلاعات موجود به جدول جدید منتقل می‌گردد.
    /// </summary>
    public partial class TreasuryInvoiceAllocations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.TrsInvoiceAllocations', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TrsInvoiceAllocations](
        [Id] int NOT NULL IDENTITY(1,1),
        [TrsVoucherId] int NOT NULL,
        [InvoiceId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_TrsInvoiceAllocations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TrsInvoiceAllocations_TrsVouchers_TrsVoucherId]
            FOREIGN KEY ([TrsVoucherId]) REFERENCES [dbo].[TrsVouchers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TrsInvoiceAllocations_FacInvoices_InvoiceId]
            FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[FacInvoices] ([Id]) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID(N'dbo.TrsInvoiceAllocations', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsInvoiceAllocations_InvoiceId'
                   AND [object_id] = OBJECT_ID(N'dbo.TrsInvoiceAllocations'))
    CREATE INDEX [IX_TrsInvoiceAllocations_InvoiceId] ON [dbo].[TrsInvoiceAllocations] ([InvoiceId]);

IF OBJECT_ID(N'dbo.TrsInvoiceAllocations', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TrsInvoiceAllocations_TrsVoucherId_InvoiceId'
                   AND [object_id] = OBJECT_ID(N'dbo.TrsInvoiceAllocations'))
    CREATE UNIQUE INDEX [IX_TrsInvoiceAllocations_TrsVoucherId_InvoiceId]
        ON [dbo].[TrsInvoiceAllocations] ([TrsVoucherId], [InvoiceId]);

-- انتقال خودکار پیوندهای تک‌فاکتوری قدیمی به جدول تخصیص جدید؛ مبلغ قبلی همان مبلغ تسویه ثبت‌شده است.
IF OBJECT_ID(N'dbo.TrsInvoiceAllocations', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.TrsVouchers', N'U') IS NOT NULL
BEGIN
    INSERT INTO [dbo].[TrsInvoiceAllocations] ([TrsVoucherId], [InvoiceId], [Amount])
    SELECT v.[Id], v.[InvoiceId], v.[TotalAmount]
    FROM [dbo].[TrsVouchers] AS v
    WHERE v.[InvoiceId] IS NOT NULL
      AND v.[TotalAmount] > 0
      AND EXISTS (SELECT 1 FROM [dbo].[FacInvoices] AS i WHERE i.[Id] = v.[InvoiceId])
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[TrsInvoiceAllocations] AS a
          WHERE a.[TrsVoucherId] = v.[Id] AND a.[InvoiceId] = v.[InvoiceId]
      );
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // عمداً برگشت‌ناپذیر است: حذف جدول می‌تواند تخصیص چندفاکتوری ثبت‌شده را از بین ببرد.
        }
    }
}
