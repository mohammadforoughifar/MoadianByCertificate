using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ساخت جدول دستگاه‌های پذیرش و انتقال داده‌های قدیمیِ تک‌دستگاهی.
/// SQLite از EnsureCreated استفاده می‌کند و SQL Server هم برای دیتابیس‌های قدیمی
/// به خودتعمیر نیاز دارد؛ بنابراین عملیات به‌صورت ایمن و تکرارپذیر انجام می‌شود.
/// </summary>
public static class RepairDeviceSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
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

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS RepairDevices (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    RepairOrderId INTEGER NOT NULL,
    DeviceType TEXT NOT NULL,
    DeviceModel TEXT NULL,
    SerialNumber TEXT NULL,
    ProblemDescription TEXT NULL,
    Accessories TEXT NULL,
    QuotedPrice TEXT NOT NULL,
    FOREIGN KEY (RepairOrderId) REFERENCES RepairOrders (Id) ON DELETE CASCADE
);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_RepairDevices_RepairOrderId ON RepairDevices (RepairOrderId);");
        await db.Database.ExecuteSqlRawAsync(@"
INSERT INTO RepairDevices
    (RepairOrderId, DeviceType, DeviceModel, SerialNumber, ProblemDescription, Accessories, QuotedPrice)
SELECT r.Id, r.DeviceType, r.DeviceModel, r.SerialNumber, r.ProblemDescription, r.Accessories, r.QuotedPrice
FROM RepairOrders AS r
WHERE NOT EXISTS (
    SELECT 1 FROM RepairDevices AS d WHERE d.RepairOrderId = r.Id
);");
    }
}
