using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «ویزیتوری» — نسخه ۱
//  • EnsureCreated (SQLite) و Migrate (SQL Server) جدول‌های جدید را به
//    دیتابیس‌های قدیمی اضافه نمی‌کنند؛ مثل GoodsIssueSchemaV1 اینجا
//    جدول‌ها را به‌صورت ایمن و idempotent می‌سازیم.
//  • VisOrders / VisOrderLines / VisOrderHistories / VisCustomerLinks
//  • عمداً بدون FK سخت به Parties/Products/Users: حذف آن‌ها نباید سابقه را بشکند.
//  • هیچ جدول موجودی تغییر نمی‌کند.
// ============================================================
public static class SalesVisitSchemaV1
{
    public const string TableOrders = "VisOrders";
    public const string TableLines = "VisOrderLines";
    public const string TableHistory = "VisOrderHistories";
    public const string TableLinks = "VisCustomerLinks";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);

        // ستون سقف تخفیف ویزیتور روی جدول موجود «Referrers» — افزودنی و بی‌خطر
        await EnsureReferrerDiscountColumnAsync(db);

        // ستون‌های تاریخ تحویل و حالت تخفیف روی جدول‌های موجود — افزودنی و بی‌خطر
        await EnsureDeliveryDateColumnsAsync(db);
    }

    /// <summary>
    /// افزودن ستون‌های «تاریخ تحویل» به <c>VisOrders</c> و «حالت تخفیف» به <c>VisOrderLines</c>
    /// برای دیتابیس‌هایی که پیش از این تغییر ساخته شده‌اند (idempotent).
    /// <para>
    /// چرا دو تاریخ؟ تاریخ درخواستی ویزیتور باید دست‌نخورده بماند تا اگر مدیر تاریخ را
    /// عوض کرد، هر دو برای پیگیری در سابقه باشد.
    /// </para>
    /// </summary>
    private static async Task EnsureDeliveryDateColumnsAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            if (!await SqliteHasColumnAsync(db, TableOrders, "DeliveryDate"))
                await SafeAsync(db, $"ALTER TABLE {TableOrders} ADD COLUMN DeliveryDate TEXT NULL;");
            if (!await SqliteHasColumnAsync(db, TableOrders, "FinalDeliveryDate"))
                await SafeAsync(db, $"ALTER TABLE {TableOrders} ADD COLUMN FinalDeliveryDate TEXT NULL;");
            if (!await SqliteHasColumnAsync(db, TableOrders, "FinalDateSetAt"))
                await SafeAsync(db, $"ALTER TABLE {TableOrders} ADD COLUMN FinalDateSetAt TEXT NULL;");
            if (!await SqliteHasColumnAsync(db, TableOrders, "FinalDateSetBy"))
                await SafeAsync(db, $"ALTER TABLE {TableOrders} ADD COLUMN FinalDateSetBy TEXT NULL;");
            if (!await SqliteHasColumnAsync(db, TableLines, "DiscountIsAmount"))
                await SafeAsync(db, $"ALTER TABLE {TableLines} ADD COLUMN DiscountIsAmount INTEGER NOT NULL DEFAULT 0;");
            return;
        }

        await SafeAsync(db, $@"
IF COL_LENGTH('dbo.{TableOrders}','DeliveryDate') IS NULL
    ALTER TABLE [dbo].[{TableOrders}] ADD [DeliveryDate] datetime2 NULL;
IF COL_LENGTH('dbo.{TableOrders}','FinalDeliveryDate') IS NULL
    ALTER TABLE [dbo].[{TableOrders}] ADD [FinalDeliveryDate] datetime2 NULL;
IF COL_LENGTH('dbo.{TableOrders}','FinalDateSetAt') IS NULL
    ALTER TABLE [dbo].[{TableOrders}] ADD [FinalDateSetAt] datetime2 NULL;
IF COL_LENGTH('dbo.{TableOrders}','FinalDateSetBy') IS NULL
    ALTER TABLE [dbo].[{TableOrders}] ADD [FinalDateSetBy] nvarchar(150) NULL;
IF COL_LENGTH('dbo.{TableLines}','DiscountIsAmount') IS NULL
    ALTER TABLE [dbo].[{TableLines}] ADD [DiscountIsAmount] bit NOT NULL DEFAULT(0);");
    }

    /// <summary>
    /// افزودن ستون <c>Referrers.MaxDiscountPercent</c> به دیتابیس‌های موجود (idempotent).
    /// چون فقط ستون nullable/با پیش‌فرض اضافه می‌شود، به دادهٔ فعلی کاری ندارد.
    /// </summary>
    private static async Task EnsureReferrerDiscountColumnAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            if (await SqliteHasColumnAsync(db, "Referrers", "MaxDiscountPercent")) return;
            await SafeAsync(db, "ALTER TABLE Referrers ADD COLUMN MaxDiscountPercent TEXT NOT NULL DEFAULT '0';");
            return;
        }

        await SafeAsync(db, @"
IF COL_LENGTH('dbo.Referrers','MaxDiscountPercent') IS NULL
    ALTER TABLE [dbo].[Referrers] ADD [MaxDiscountPercent] decimal(18,2) NOT NULL DEFAULT(0);");
    }

    private static async Task<bool> SqliteHasColumnAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch
        {
            return true;   // اگر نتوانستیم بسنجیم، دست نمی‌زنیم
        }
    }

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.VisOrders', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VisOrders](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CompanyId] int NULL,
        [Number] nvarchar(30) NOT NULL,
        [Date] datetime2 NOT NULL,
        [ReferrerId] int NOT NULL,
        [ReferrerName] nvarchar(150) NOT NULL,
        [PartyId] int NOT NULL,
        [PartyName] nvarchar(200) NOT NULL,
        [WarehouseId] int NOT NULL,
        [WarehouseName] nvarchar(150) NOT NULL,
        [Status] int NOT NULL,
        [PaymentMethod] int NOT NULL,
        [TotalGross] decimal(18,2) NOT NULL DEFAULT(0),
        [TotalDiscount] decimal(18,2) NOT NULL DEFAULT(0),
        [TotalNet] decimal(18,2) NOT NULL DEFAULT(0),
        [Note] nvarchar(500) NULL,
        [DiscountOverLimit] bit NOT NULL DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        [CreatedByUserId] int NOT NULL DEFAULT(0),
        [CreatedByName] nvarchar(150) NOT NULL DEFAULT(N''),
        [SubmittedAt] datetime2 NULL,
        [DecidedAt] datetime2 NULL,
        [DecidedBy] nvarchar(150) NULL,
        [DecisionNote] nvarchar(500) NULL,
        [ReturnCount] int NOT NULL DEFAULT(0),
        [ExpiresAt] datetime2 NULL,
        [InvoiceId] int NULL,
        [InvoiceNumber] nvarchar(30) NULL,
        [DeliveryDate] datetime2 NULL,
        [FinalDeliveryDate] datetime2 NULL,
        [FinalDateSetAt] datetime2 NULL,
        [FinalDateSetBy] nvarchar(150) NULL
    );
    CREATE UNIQUE INDEX [IX_VisOrders_CompanyId_Number] ON [dbo].[VisOrders] ([CompanyId], [Number]);
    CREATE INDEX [IX_VisOrders_Status] ON [dbo].[VisOrders] ([Status]);
    CREATE INDEX [IX_VisOrders_ReferrerId_Date] ON [dbo].[VisOrders] ([ReferrerId], [Date]);
    CREATE INDEX [IX_VisOrders_PartyId] ON [dbo].[VisOrders] ([PartyId]);
    CREATE INDEX [IX_VisOrders_InvoiceId] ON [dbo].[VisOrders] ([InvoiceId]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.VisOrderLines', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VisOrderLines](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CompanyId] int NULL,
        [OrderId] int NOT NULL,
        [RowNo] int NOT NULL DEFAULT(0),
        [ProductId] int NOT NULL,
        [ProductCode] nvarchar(50) NOT NULL DEFAULT(N''),
        [ProductName] nvarchar(200) NOT NULL DEFAULT(N''),
        [Unit] nvarchar(50) NULL,
        [Quantity] decimal(18,2) NOT NULL DEFAULT(0),
        [UnitPrice] decimal(18,2) NOT NULL DEFAULT(0),
        [DiscountPercent] decimal(18,2) NOT NULL DEFAULT(0),
        [DiscountAmount] decimal(18,2) NOT NULL DEFAULT(0),
        [DiscountIsAmount] bit NOT NULL DEFAULT(0),
        [LineTotal] decimal(18,2) NOT NULL DEFAULT(0),
        [Note] nvarchar(300) NULL
    );
    CREATE INDEX [IX_VisOrderLines_OrderId] ON [dbo].[VisOrderLines] ([OrderId]);
    CREATE INDEX [IX_VisOrderLines_ProductId] ON [dbo].[VisOrderLines] ([ProductId]);
    ALTER TABLE [dbo].[VisOrderLines] ADD CONSTRAINT [FK_VisOrderLines_VisOrders_OrderId]
        FOREIGN KEY ([OrderId]) REFERENCES [dbo].[VisOrders] ([Id]) ON DELETE CASCADE;
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.VisOrderHistories', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VisOrderHistories](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CompanyId] int NULL,
        [OrderId] int NOT NULL,
        [FromStatus] int NULL,
        [ToStatus] int NOT NULL,
        [UserId] int NOT NULL DEFAULT(0),
        [UserName] nvarchar(150) NOT NULL DEFAULT(N''),
        [At] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        [Comment] nvarchar(500) NULL
    );
    CREATE INDEX [IX_VisOrderHistories_OrderId] ON [dbo].[VisOrderHistories] ([OrderId]);
    ALTER TABLE [dbo].[VisOrderHistories] ADD CONSTRAINT [FK_VisOrderHistories_VisOrders_OrderId]
        FOREIGN KEY ([OrderId]) REFERENCES [dbo].[VisOrders] ([Id]) ON DELETE CASCADE;
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.VisCustomerLinks', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VisCustomerLinks](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CompanyId] int NULL,
        [ReferrerId] int NOT NULL,
        [PartyId] int NOT NULL,
        [CreditLimit] decimal(18,2) NULL,
        [MaxDiscountPercent] decimal(18,2) NULL,
        [VisitCycleDays] int NULL,
        [LastOrderAt] datetime2 NULL,
        [IsActive] bit NOT NULL DEFAULT(1),
        [CreatedAt] datetime2 NOT NULL DEFAULT(SYSDATETIME())
    );
    CREATE UNIQUE INDEX [IX_VisCustomerLinks_ReferrerId_PartyId] ON [dbo].[VisCustomerLinks] ([ReferrerId], [PartyId]);
    CREATE INDEX [IX_VisCustomerLinks_PartyId] ON [dbo].[VisCustomerLinks] ([PartyId]);
END");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        // EnsureCreated روی دیتابیس تازه، جدول را با ستون‌های درست می‌سازد؛
        // این خودتعمیر فقط برای دیتابیس‌های قدیمی است. پس اول وجودِ جدول را می‌سنجیم.
        var hasTable = false;
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='VisOrders';";
            hasTable = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch { /* جدول نیست — EnsureCreated آن را ساخته است */ }

        if (hasTable) return;

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS VisOrders (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    Number TEXT NOT NULL,
    Date TEXT NOT NULL,
    ReferrerId INTEGER NOT NULL,
    ReferrerName TEXT NOT NULL,
    PartyId INTEGER NOT NULL,
    PartyName TEXT NOT NULL,
    WarehouseId INTEGER NOT NULL,
    WarehouseName TEXT NOT NULL,
    Status INTEGER NOT NULL,
    PaymentMethod INTEGER NOT NULL,
    TotalGross TEXT NOT NULL,
    TotalDiscount TEXT NOT NULL,
    TotalNet TEXT NOT NULL,
    Note TEXT NULL,
    DiscountOverLimit INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    CreatedByUserId INTEGER NOT NULL DEFAULT 0,
    CreatedByName TEXT NOT NULL DEFAULT '',
    SubmittedAt TEXT NULL,
    DecidedAt TEXT NULL,
    DecidedBy TEXT NULL,
    DecisionNote TEXT NULL,
    ReturnCount INTEGER NOT NULL DEFAULT 0,
    ExpiresAt TEXT NULL,
    InvoiceId INTEGER NULL,
    InvoiceNumber TEXT NULL,
    DeliveryDate TEXT NULL,
    FinalDeliveryDate TEXT NULL,
    FinalDateSetAt TEXT NULL,
    FinalDateSetBy TEXT NULL
);");
        await SafeAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_VisOrders_CompanyId_Number ON VisOrders (CompanyId, Number);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_VisOrders_Status ON VisOrders (Status);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_VisOrders_ReferrerId_Date ON VisOrders (ReferrerId, Date);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_VisOrders_PartyId ON VisOrders (PartyId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS VisOrderLines (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    OrderId INTEGER NOT NULL,
    RowNo INTEGER NOT NULL DEFAULT 0,
    ProductId INTEGER NOT NULL,
    ProductCode TEXT NOT NULL DEFAULT '',
    ProductName TEXT NOT NULL DEFAULT '',
    Unit TEXT NULL,
    Quantity TEXT NOT NULL,
    UnitPrice TEXT NOT NULL,
    DiscountPercent TEXT NOT NULL,
    DiscountAmount TEXT NOT NULL,
    DiscountIsAmount INTEGER NOT NULL DEFAULT 0,
    LineTotal TEXT NOT NULL,
    Note TEXT NULL,
    FOREIGN KEY (OrderId) REFERENCES VisOrders (Id) ON DELETE CASCADE
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_VisOrderLines_OrderId ON VisOrderLines (OrderId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_VisOrderLines_ProductId ON VisOrderLines (ProductId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS VisOrderHistories (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    OrderId INTEGER NOT NULL,
    FromStatus INTEGER NULL,
    ToStatus INTEGER NOT NULL,
    UserId INTEGER NOT NULL DEFAULT 0,
    UserName TEXT NOT NULL DEFAULT '',
    At TEXT NOT NULL,
    Comment TEXT NULL,
    FOREIGN KEY (OrderId) REFERENCES VisOrders (Id) ON DELETE CASCADE
);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_VisOrderHistories_OrderId ON VisOrderHistories (OrderId);");

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS VisCustomerLinks (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    ReferrerId INTEGER NOT NULL,
    PartyId INTEGER NOT NULL,
    CreditLimit TEXT NULL,
    MaxDiscountPercent TEXT NULL,
    VisitCycleDays INTEGER NULL,
    LastOrderAt TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL
);");
        await SafeAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_VisCustomerLinks_ReferrerId_PartyId ON VisCustomerLinks (ReferrerId, PartyId);");
        await SafeAsync(db, "CREATE INDEX IF NOT EXISTS IX_VisCustomerLinks_PartyId ON VisCustomerLinks (PartyId);");
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] SalesVisitSchemaV1: {m}");
        }
    }
}
