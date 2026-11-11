using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public static class MoadianCertSchemaV2
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.GetDbConnection() is SqliteConnection sqliteConn)
                await EnsureSqliteAsync(sqliteConn.ConnectionString);
            else if (db.Database.GetDbConnection() is SqlConnection sqlServerConn)
                await EnsureSqlServerAsync(sqlServerConn.ConnectionString);

            await SeedMeasureUnitsAsync(db);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] MoadianCertSchemaV2 هشدار/خطا: {ex.Message}");
            throw;
        }
    }

    internal const string SqlServerDropLegacyTables = @"
-- حذف کلیدهای خارجی احتمالی روی جداول قدیمی مودیان پیش از DROP TABLE
DECLARE @dropFkSql nvarchar(max) = N'';
SELECT @dropFkSql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id))
    + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';'
FROM sys.foreign_keys
WHERE OBJECT_NAME(referenced_object_id) IN (
    N'InvoicePayments', N'InvoiceItems', N'Invoices', N'ConnectInfos',
    N'GoodsOrServices', N'UnitOfMeasurements', N'FiscalYears', N'Customers', N'CPInfos',
    N'MoadianLogs', N'MoadianInvoiceLines', N'MoadianInvoices',
    N'MoadianFiscalPeriods', N'MoadianCpcList', N'MoadianSettings', N'FiscalPrinterSettings'
);
IF LEN(@dropFkSql) > 0 EXEC sp_executesql @dropFkSql;

DROP TABLE IF EXISTS [dbo].[InvoicePayments];
DROP TABLE IF EXISTS [dbo].[InvoiceItems];
DROP TABLE IF EXISTS [dbo].[Invoices];
DROP TABLE IF EXISTS [dbo].[ConnectInfos];
DROP TABLE IF EXISTS [dbo].[GoodsOrServices];
DROP TABLE IF EXISTS [dbo].[UnitOfMeasurements];
DROP TABLE IF EXISTS [dbo].[FiscalYears];
DROP TABLE IF EXISTS [dbo].[Customers];
DROP TABLE IF EXISTS [dbo].[CPInfos];

DROP TABLE IF EXISTS [dbo].[MoadianLogs];
DROP TABLE IF EXISTS [dbo].[MoadianInvoiceLines];
DROP TABLE IF EXISTS [dbo].[MoadianInvoices];
DROP TABLE IF EXISTS [dbo].[MoadianFiscalPeriods];
DROP TABLE IF EXISTS [dbo].[MoadianCpcList];
DROP TABLE IF EXISTS [dbo].[MoadianSettings];
DROP TABLE IF EXISTS [dbo].[FiscalPrinterSettings];
";

    internal const string SqlServerCreateTables = @"
IF OBJECT_ID(N'dbo.MdnMeasureUnits', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnMeasureUnits] (
        [Id] int NOT NULL IDENTITY PRIMARY KEY,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_MdnMeasureUnits_IsActive] DEFAULT(1)
    );
    CREATE UNIQUE INDEX [IX_MdnMeasureUnits_Code] ON [dbo].[MdnMeasureUnits] ([Code]);
END;

IF OBJECT_ID(N'dbo.MdnTaxpayerProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnTaxpayerProfiles] (
        [Id] int NOT NULL IDENTITY PRIMARY KEY,
        [CompanyId] int NULL,
        [TradeName] nvarchar(200) NOT NULL,
        [TaxpayerType] int NOT NULL,
        [EconomicCode] nvarchar(20) NOT NULL,
        [NationalId] nvarchar(20) NULL,
        [PostalCode] nvarchar(20) NULL,
        [BranchCode] nvarchar(10) NULL,
        [Address] nvarchar(400) NULL,
        [MemoryId] nvarchar(12) NOT NULL,
        [Environment] int NOT NULL,
        [ProductionBaseUrl] nvarchar(300) NOT NULL,
        [SandboxBaseUrl] nvarchar(300) NOT NULL,
        [SignatureMode] int NOT NULL,
        [EncryptedPrivateKeyPem] nvarchar(max) NULL,
        [EncryptedCertificateBase64] nvarchar(max) NULL,
        [CertificateSubject] nvarchar(400) NULL,
        [CertificateIssuer] nvarchar(400) NULL,
        [CertificateSerial] nvarchar(120) NULL,
        [CertificateThumbprint] nvarchar(120) NULL,
        [CertificateNotBefore] datetime2 NULL,
        [CertificateNotAfter] datetime2 NULL,
        [Pkcs11LibraryPath] nvarchar(400) NULL,
        [Pkcs11CertificateAlias] nvarchar(200) NULL,
        [EncryptedPkcs11Pin] nvarchar(max) NULL,
        [NextSerial] bigint NOT NULL CONSTRAINT [DF_MdnTaxpayerProfiles_NextSerial] DEFAULT(1),
        [DefaultVatRate] decimal(18,4) NOT NULL CONSTRAINT [DF_MdnTaxpayerProfiles_Vat] DEFAULT(10),
        [Article9SendDeadlineDays] int NOT NULL CONSTRAINT [DF_MdnTaxpayerProfiles_Article9Deadline] DEFAULT(12),
        [IsActive] bit NOT NULL CONSTRAINT [DF_MdnTaxpayerProfiles_IsActive] DEFAULT(1),
        [LastConnectionTestAt] datetime2 NULL,
        [LastConnectionStatus] nvarchar(300) NULL,
        [LastFiscalStatus] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL
    );
    CREATE INDEX [IX_MdnTaxpayerProfiles_CompanyId] ON [dbo].[MdnTaxpayerProfiles] ([CompanyId]);
END;

IF OBJECT_ID(N'dbo.MdnBuyers', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnBuyers] (
        [Id] int NOT NULL IDENTITY PRIMARY KEY,
        [CompanyId] int NULL,
        [PartyId] int NULL,
        [Name] nvarchar(200) NOT NULL,
        [BuyerType] int NOT NULL,
        [EconomicCode] nvarchar(20) NULL,
        [NationalId] nvarchar(20) NULL,
        [PostalCode] nvarchar(20) NULL,
        [BranchCode] nvarchar(10) NULL,
        [PassportNumber] nvarchar(30) NULL,
        [Phone] nvarchar(50) NULL,
        [Address] nvarchar(400) NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_MdnBuyers_IsActive] DEFAULT(1),
        [CreatedAt] datetime2 NOT NULL
    );
    CREATE INDEX [IX_MdnBuyers_CompanyId_EconomicCode] ON [dbo].[MdnBuyers] ([CompanyId], [EconomicCode]);
    CREATE INDEX [IX_MdnBuyers_CompanyId_NationalId] ON [dbo].[MdnBuyers] ([CompanyId], [NationalId]);
END;

IF OBJECT_ID(N'dbo.MdnCatalogItems', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnCatalogItems] (
        [Id] int NOT NULL IDENTITY PRIMARY KEY,
        [CompanyId] int NULL,
        [ProductId] int NULL,
        [InternalCode] nvarchar(60) NULL,
        [Name] nvarchar(250) NOT NULL,
        [Sstid] nvarchar(20) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [VatRate] decimal(18,4) NOT NULL,
        [DefaultUnitPrice] bigint NOT NULL,
        [HsCode] nvarchar(30) NULL,
        [IsService] bit NOT NULL CONSTRAINT [DF_MdnCatalogItems_IsService] DEFAULT(0),
        [IsActive] bit NOT NULL CONSTRAINT [DF_MdnCatalogItems_IsActive] DEFAULT(1),
        [CreatedAt] datetime2 NOT NULL
    );
    CREATE INDEX [IX_MdnCatalogItems_CompanyId_Sstid] ON [dbo].[MdnCatalogItems] ([CompanyId], [Sstid]);
END;

IF OBJECT_ID(N'dbo.MdnInvoices', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnInvoices] (
        [Id] int NOT NULL IDENTITY PRIMARY KEY,
        [CompanyId] int NULL,
        [InternalNumber] nvarchar(40) NOT NULL,
        [SerialDecimal] bigint NOT NULL,
        [SerialHex] nvarchar(16) NULL,
        [TaxId] nvarchar(30) NULL,
        [ReferenceTaxId] nvarchar(30) NULL,
        [ParentInvoiceId] int NULL,
        [Uid] nvarchar(64) NULL,
        [ReferenceNumber] nvarchar(80) NULL,
        [MemoryId] nvarchar(12) NULL,
        [IssueDate] datetime2 NOT NULL,
        [CreateDate] datetime2 NOT NULL,
        [RegistrationDate] datetime2 NULL,
        [IsArticle9] bit NOT NULL CONSTRAINT [DF_MdnInvoices_IsArticle9] DEFAULT(0),
        [SendRule] int NULL,
        [InvoiceType] int NOT NULL,
        [Pattern] int NOT NULL,
        [Subject] int NOT NULL,
        [SettlementMethod] int NOT NULL,
        [SellerEconomicCode] nvarchar(20) NULL,
        [SellerTradeName] nvarchar(200) NULL,
        [SellerPostalCode] nvarchar(20) NULL,
        [SellerBranchCode] nvarchar(10) NULL,
        [BuyerId] int NULL,
        [BuyerName] nvarchar(200) NULL,
        [BuyerType] int NOT NULL,
        [BuyerEconomicCode] nvarchar(20) NULL,
        [BuyerNationalId] nvarchar(20) NULL,
        [BuyerPostalCode] nvarchar(20) NULL,
        [BuyerBranchCode] nvarchar(10) NULL,
        [BuyerPassportNumber] nvarchar(30) NULL,
        [ContractRegistrationNumber] nvarchar(30) NULL,
        [BillId] nvarchar(30) NULL,
        [CustomsLicenseNumber] nvarchar(30) NULL,
        [CustomsCode] nvarchar(20) NULL,
        [CustomsDeclarationNumber] nvarchar(30) NULL,
        [CustomsDeclarationDate] datetime2 NULL,
        [Tax17] bigint NULL,
        [Note1] nvarchar(30) NULL,
        [Note2] nvarchar(30) NULL,
        [TotalBeforeDiscount] bigint NOT NULL,
        [TotalDiscount] bigint NOT NULL,
        [TotalAfterDiscount] bigint NOT NULL,
        [TotalVat] bigint NOT NULL,
        [TotalOtherTaxes] bigint NOT NULL,
        [TotalAmount] bigint NOT NULL,
        [CashAmount] bigint NOT NULL,
        [CreditAmount] bigint NOT NULL,
        [PaidAmount] bigint NOT NULL,
        [SubmissionStatus] int NOT NULL,
        [FolderStatus] int NOT NULL,
        [FolderStatusRaw] nvarchar(60) NULL,
        [Article6Status] nvarchar(60) NULL,
        [PaymentStatus] int NOT NULL,
        [SentAt] datetime2 NULL,
        [LastInquiryAt] datetime2 NULL,
        [LastFolderInquiryAt] datetime2 NULL,
        [LastErrorCode] nvarchar(80) NULL,
        [LastErrorMessage] nvarchar(2000) NULL,
        [Description] nvarchar(600) NULL,
        [SourceFacInvoiceId] int NULL,
        [SourceFacInvoiceNumber] int NULL,
        [CreatedByUserId] int NOT NULL,
        [UpdatedAt] datetime2 NOT NULL
    );
    CREATE INDEX [IX_MdnInvoices_CompanyId_InternalNumber] ON [dbo].[MdnInvoices] ([CompanyId], [InternalNumber]);
    CREATE INDEX [IX_MdnInvoices_TaxId] ON [dbo].[MdnInvoices] ([TaxId]);
    CREATE INDEX [IX_MdnInvoices_ReferenceNumber] ON [dbo].[MdnInvoices] ([ReferenceNumber]);
    CREATE INDEX [IX_MdnInvoices_CompanyId_SubmissionStatus] ON [dbo].[MdnInvoices] ([CompanyId], [SubmissionStatus]);
    CREATE INDEX [IX_MdnInvoices_CompanyId_FolderStatus] ON [dbo].[MdnInvoices] ([CompanyId], [FolderStatus]);
    CREATE INDEX [IX_MdnInvoices_CompanyId_IsArticle9] ON [dbo].[MdnInvoices] ([CompanyId], [IsArticle9]);
END;

IF OBJECT_ID(N'dbo.MdnInvoiceLines', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnInvoiceLines] (
        [Id] int NOT NULL IDENTITY PRIMARY KEY,
        [CompanyId] int NULL,
        [InvoiceId] int NOT NULL,
        [RowNumber] int NOT NULL,
        [CatalogItemId] int NULL,
        [Sstid] nvarchar(20) NOT NULL,
        [Description] nvarchar(400) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [Quantity] decimal(18,4) NOT NULL,
        [UnitPrice] decimal(18,4) NOT NULL,
        [PriceBeforeDiscount] bigint NOT NULL,
        [DiscountAmount] bigint NOT NULL,
        [PriceAfterDiscount] bigint NOT NULL,
        [VatRate] decimal(18,4) NOT NULL,
        [VatAmount] bigint NOT NULL,
        [OtherTaxSubject] nvarchar(255) NULL,
        [OtherTaxRate] decimal(18,4) NULL,
        [OtherTaxAmount] bigint NOT NULL,
        [OtherLegalFeeSubject] nvarchar(255) NULL,
        [OtherLegalFeeRate] decimal(18,4) NULL,
        [OtherLegalFeeAmount] bigint NOT NULL,
        [ConstructionFee] bigint NULL,
        [SellerProfit] bigint NULL,
        [BrokerFee] bigint NULL,
        [CurrencyCode] nvarchar(10) NULL,
        [ExchangeRate] bigint NULL,
        [NetWeight] decimal(18,4) NULL,
        [HsCode] nvarchar(30) NULL,
        [ContractRegistrationNumber] nvarchar(30) NULL,
        [TotalLineAmount] bigint NOT NULL,
        CONSTRAINT [FK_MdnInvoiceLines_MdnInvoices_InvoiceId]
            FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[MdnInvoices] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_MdnInvoiceLines_InvoiceId] ON [dbo].[MdnInvoiceLines] ([InvoiceId]);
END;

IF OBJECT_ID(N'dbo.MdnInvoicePayments', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnInvoicePayments] (
        [Id] int NOT NULL IDENTITY PRIMARY KEY,
        [CompanyId] int NULL,
        [InvoiceId] int NOT NULL,
        [PaymentMethod] int NOT NULL,
        [PaidAmount] bigint NOT NULL,
        [PaymentDate] datetime2 NOT NULL,
        [SwitchNumber] nvarchar(30) NULL,
        [AcceptorNumber] nvarchar(30) NULL,
        [TerminalNumber] nvarchar(40) NULL,
        [ReferenceNumber] nvarchar(80) NULL,
        [MaskedCardNumber] nvarchar(30) NULL,
        [PayerNationalId] nvarchar(20) NULL,
        [Note] nvarchar(400) NULL,
        [SyncState] int NOT NULL,
        [RegisteredAt] datetime2 NULL,
        [LastError] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [FK_MdnInvoicePayments_MdnInvoices_InvoiceId]
            FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[MdnInvoices] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_MdnInvoicePayments_InvoiceId] ON [dbo].[MdnInvoicePayments] ([InvoiceId]);
END;

IF OBJECT_ID(N'dbo.MdnSubmissionLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnSubmissionLogs] (
        [Id] int NOT NULL IDENTITY PRIMARY KEY,
        [CompanyId] int NULL,
        [InvoiceId] int NULL,
        [ActionType] nvarchar(50) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [IsSuccess] bit NOT NULL,
        [StatusCode] nvarchar(60) NOT NULL,
        [Uid] nvarchar(64) NULL,
        [ReferenceNumber] nvarchar(80) NULL,
        [TaxId] nvarchar(30) NULL,
        [Summary] nvarchar(600) NOT NULL,
        [ErrorDetailsJson] nvarchar(max) NULL,
        [CreatedByUsername] nvarchar(100) NULL,
        CONSTRAINT [FK_MdnSubmissionLogs_MdnInvoices_InvoiceId]
            FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[MdnInvoices] ([Id]) ON DELETE SET NULL
    );
    CREATE INDEX [IX_MdnSubmissionLogs_InvoiceId] ON [dbo].[MdnSubmissionLogs] ([InvoiceId]);
END;
";

    internal const string SqlServerUpgradeArticle9Columns = @"
IF OBJECT_ID(N'dbo.MdnTaxpayerProfiles', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.MdnTaxpayerProfiles', N'Article9SendDeadlineDays') IS NULL
    ALTER TABLE [dbo].[MdnTaxpayerProfiles] ADD [Article9SendDeadlineDays] int NOT NULL CONSTRAINT [DF_MdnTaxpayerProfiles_Article9Deadline] DEFAULT(12);

IF OBJECT_ID(N'dbo.MdnInvoices', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.MdnInvoices', N'RegistrationDate') IS NULL
    ALTER TABLE [dbo].[MdnInvoices] ADD [RegistrationDate] datetime2 NULL;

IF OBJECT_ID(N'dbo.MdnInvoices', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.MdnInvoices', N'IsArticle9') IS NULL
    ALTER TABLE [dbo].[MdnInvoices] ADD [IsArticle9] bit NOT NULL CONSTRAINT [DF_MdnInvoices_IsArticle9] DEFAULT(0);

IF OBJECT_ID(N'dbo.MdnInvoices', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.MdnInvoices', N'SendRule') IS NULL
    ALTER TABLE [dbo].[MdnInvoices] ADD [SendRule] int NULL;

IF OBJECT_ID(N'dbo.MdnInvoices', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.MdnInvoices', N'Note1') IS NULL
    ALTER TABLE [dbo].[MdnInvoices] ADD [Note1] nvarchar(30) NULL;

IF OBJECT_ID(N'dbo.MdnInvoices', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.MdnInvoices', N'Note2') IS NULL
    ALTER TABLE [dbo].[MdnInvoices] ADD [Note2] nvarchar(30) NULL;
";

    internal const string SqlServerUpgradeArticle9Indexes = @"
IF OBJECT_ID(N'dbo.MdnInvoices', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.MdnInvoices', N'IsArticle9') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MdnInvoices') AND name = N'IX_MdnInvoices_CompanyId_IsArticle9')
    CREATE INDEX [IX_MdnInvoices_CompanyId_IsArticle9] ON [dbo].[MdnInvoices] ([CompanyId], [IsArticle9]);
";

    private static async Task EnsureSqlServerAsync(string connectionString)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        using (var dropCmd = conn.CreateCommand())
        {
            dropCmd.CommandText = SqlServerDropLegacyTables;
            await dropCmd.ExecuteNonQueryAsync();
        }

        using (var createCmd = conn.CreateCommand())
        {
            createCmd.CommandText = SqlServerCreateTables;
            await createCmd.ExecuteNonQueryAsync();
        }

        using (var alterCmd = conn.CreateCommand())
        {
            alterCmd.CommandText = SqlServerUpgradeArticle9Columns;
            await alterCmd.ExecuteNonQueryAsync();
        }

        using (var idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = SqlServerUpgradeArticle9Indexes;
            await idxCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureSqliteAsync(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        async Task ExecAsync(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        await ExecAsync(@"
DROP TABLE IF EXISTS InvoicePayments;
DROP TABLE IF EXISTS InvoiceItems;
DROP TABLE IF EXISTS Invoices;
DROP TABLE IF EXISTS ConnectInfos;
DROP TABLE IF EXISTS GoodsOrServices;
DROP TABLE IF EXISTS UnitOfMeasurements;
DROP TABLE IF EXISTS FiscalYears;
DROP TABLE IF EXISTS Customers;
DROP TABLE IF EXISTS CPInfos;
DROP TABLE IF EXISTS MoadianLogs;
DROP TABLE IF EXISTS MoadianInvoiceLines;
DROP TABLE IF EXISTS MoadianInvoices;
DROP TABLE IF EXISTS MoadianFiscalPeriods;
DROP TABLE IF EXISTS MoadianCpcList;
DROP TABLE IF EXISTS MoadianSettings;
DROP TABLE IF EXISTS FiscalPrinterSettings;

CREATE TABLE IF NOT EXISTS MdnMeasureUnits (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_MdnMeasureUnits_Code ON MdnMeasureUnits (Code);

CREATE TABLE IF NOT EXISTS MdnTaxpayerProfiles (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    TradeName TEXT NOT NULL,
    TaxpayerType INTEGER NOT NULL,
    EconomicCode TEXT NOT NULL,
    NationalId TEXT NULL,
    PostalCode TEXT NULL,
    BranchCode TEXT NULL,
    Address TEXT NULL,
    MemoryId TEXT NOT NULL,
    Environment INTEGER NOT NULL,
    ProductionBaseUrl TEXT NOT NULL,
    SandboxBaseUrl TEXT NOT NULL,
    SignatureMode INTEGER NOT NULL,
    EncryptedPrivateKeyPem TEXT NULL,
    EncryptedCertificateBase64 TEXT NULL,
    CertificateSubject TEXT NULL,
    CertificateIssuer TEXT NULL,
    CertificateSerial TEXT NULL,
    CertificateThumbprint TEXT NULL,
    CertificateNotBefore TEXT NULL,
    CertificateNotAfter TEXT NULL,
    Pkcs11LibraryPath TEXT NULL,
    Pkcs11CertificateAlias TEXT NULL,
    EncryptedPkcs11Pin TEXT NULL,
    NextSerial INTEGER NOT NULL DEFAULT 1,
    DefaultVatRate TEXT NOT NULL DEFAULT '10',
    Article9SendDeadlineDays INTEGER NOT NULL DEFAULT 12,
    IsActive INTEGER NOT NULL DEFAULT 1,
    LastConnectionTestAt TEXT NULL,
    LastConnectionStatus TEXT NULL,
    LastFiscalStatus TEXT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MdnBuyers (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    PartyId INTEGER NULL,
    Name TEXT NOT NULL,
    BuyerType INTEGER NOT NULL,
    EconomicCode TEXT NULL,
    NationalId TEXT NULL,
    PostalCode TEXT NULL,
    BranchCode TEXT NULL,
    PassportNumber TEXT NULL,
    Phone TEXT NULL,
    Address TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MdnCatalogItems (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    ProductId INTEGER NULL,
    InternalCode TEXT NULL,
    Name TEXT NOT NULL,
    Sstid TEXT NOT NULL,
    UnitCode TEXT NOT NULL,
    VatRate TEXT NOT NULL,
    DefaultUnitPrice INTEGER NOT NULL,
    HsCode TEXT NULL,
    IsService INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MdnInvoices (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    InternalNumber TEXT NOT NULL,
    SerialDecimal INTEGER NOT NULL,
    SerialHex TEXT NULL,
    TaxId TEXT NULL,
    ReferenceTaxId TEXT NULL,
    ParentInvoiceId INTEGER NULL,
    Uid TEXT NULL,
    ReferenceNumber TEXT NULL,
    MemoryId TEXT NULL,
    IssueDate TEXT NOT NULL,
    CreateDate TEXT NOT NULL,
    RegistrationDate TEXT NULL,
    IsArticle9 INTEGER NOT NULL DEFAULT 0,
    SendRule INTEGER NULL,
    InvoiceType INTEGER NOT NULL,
    Pattern INTEGER NOT NULL,
    Subject INTEGER NOT NULL,
    SettlementMethod INTEGER NOT NULL,
    SellerEconomicCode TEXT NULL,
    SellerTradeName TEXT NULL,
    SellerPostalCode TEXT NULL,
    SellerBranchCode TEXT NULL,
    BuyerId INTEGER NULL,
    BuyerName TEXT NULL,
    BuyerType INTEGER NOT NULL,
    BuyerEconomicCode TEXT NULL,
    BuyerNationalId TEXT NULL,
    BuyerPostalCode TEXT NULL,
    BuyerBranchCode TEXT NULL,
    BuyerPassportNumber TEXT NULL,
    ContractRegistrationNumber TEXT NULL,
    BillId TEXT NULL,
    CustomsLicenseNumber TEXT NULL,
    CustomsCode TEXT NULL,
    CustomsDeclarationNumber TEXT NULL,
    CustomsDeclarationDate TEXT NULL,
    Tax17 INTEGER NULL,
    Note1 TEXT NULL,
    Note2 TEXT NULL,
    TotalBeforeDiscount INTEGER NOT NULL,
    TotalDiscount INTEGER NOT NULL,
    TotalAfterDiscount INTEGER NOT NULL,
    TotalVat INTEGER NOT NULL,
    TotalOtherTaxes INTEGER NOT NULL,
    TotalAmount INTEGER NOT NULL,
    CashAmount INTEGER NOT NULL,
    CreditAmount INTEGER NOT NULL,
    PaidAmount INTEGER NOT NULL,
    SubmissionStatus INTEGER NOT NULL,
    FolderStatus INTEGER NOT NULL,
    FolderStatusRaw TEXT NULL,
    Article6Status TEXT NULL,
    PaymentStatus INTEGER NOT NULL,
    SentAt TEXT NULL,
    LastInquiryAt TEXT NULL,
    LastFolderInquiryAt TEXT NULL,
    LastErrorCode TEXT NULL,
    LastErrorMessage TEXT NULL,
    Description TEXT NULL,
    SourceFacInvoiceId INTEGER NULL,
    SourceFacInvoiceNumber INTEGER NULL,
    CreatedByUserId INTEGER NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MdnInvoiceLines (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    InvoiceId INTEGER NOT NULL REFERENCES MdnInvoices(Id) ON DELETE CASCADE,
    RowNumber INTEGER NOT NULL,
    CatalogItemId INTEGER NULL,
    Sstid TEXT NOT NULL,
    Description TEXT NOT NULL,
    UnitCode TEXT NOT NULL,
    Quantity TEXT NOT NULL,
    UnitPrice TEXT NOT NULL,
    PriceBeforeDiscount INTEGER NOT NULL,
    DiscountAmount INTEGER NOT NULL,
    PriceAfterDiscount INTEGER NOT NULL,
    VatRate TEXT NOT NULL,
    VatAmount INTEGER NOT NULL,
    OtherTaxSubject TEXT NULL,
    OtherTaxRate TEXT NULL,
    OtherTaxAmount INTEGER NOT NULL,
    OtherLegalFeeSubject TEXT NULL,
    OtherLegalFeeRate TEXT NULL,
    OtherLegalFeeAmount INTEGER NOT NULL,
    ConstructionFee INTEGER NULL,
    SellerProfit INTEGER NULL,
    BrokerFee INTEGER NULL,
    CurrencyCode TEXT NULL,
    ExchangeRate INTEGER NULL,
    NetWeight TEXT NULL,
    HsCode TEXT NULL,
    ContractRegistrationNumber TEXT NULL,
    TotalLineAmount INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS MdnInvoicePayments (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    InvoiceId INTEGER NOT NULL REFERENCES MdnInvoices(Id) ON DELETE CASCADE,
    PaymentMethod INTEGER NOT NULL,
    PaidAmount INTEGER NOT NULL,
    PaymentDate TEXT NOT NULL,
    SwitchNumber TEXT NULL,
    AcceptorNumber TEXT NULL,
    TerminalNumber TEXT NULL,
    ReferenceNumber TEXT NULL,
    MaskedCardNumber TEXT NULL,
    PayerNationalId TEXT NULL,
    Note TEXT NULL,
    SyncState INTEGER NOT NULL,
    RegisteredAt TEXT NULL,
    LastError TEXT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MdnSubmissionLogs (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    InvoiceId INTEGER NULL REFERENCES MdnInvoices(Id) ON DELETE SET NULL,
    ActionType TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    IsSuccess INTEGER NOT NULL,
    StatusCode TEXT NOT NULL,
    Uid TEXT NULL,
    ReferenceNumber TEXT NULL,
    TaxId TEXT NULL,
    Summary TEXT NOT NULL,
    ErrorDetailsJson TEXT NULL,
    CreatedByUsername TEXT NULL
);
");

        var sqliteUpgrades = new (string Table, string Column, string Ddl)[]
        {
            ("MdnTaxpayerProfiles", "Article9SendDeadlineDays", "INTEGER NOT NULL DEFAULT 12"),
            ("MdnInvoices", "RegistrationDate", "TEXT NULL"),
            ("MdnInvoices", "IsArticle9", "INTEGER NOT NULL DEFAULT 0"),
            ("MdnInvoices", "SendRule", "INTEGER NULL"),
            ("MdnInvoices", "Note1", "TEXT NULL"),
            ("MdnInvoices", "Note2", "TEXT NULL")
        };

        foreach (var (table, column, ddl) in sqliteUpgrades)
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
            if (!exists)
                await ExecAsync($"ALTER TABLE {table} ADD COLUMN {column} {ddl};");
        }

        await ExecAsync("CREATE INDEX IF NOT EXISTS IX_MdnInvoices_CompanyId_IsArticle9 ON MdnInvoices (CompanyId, IsArticle9);");
    }

    private static async Task SeedMeasureUnitsAsync(AppDbContext db)
    {
        if (await db.MdnMeasureUnits.AnyAsync()) return;

        var defaults = new (string Code, string Name)[]
        {
            ("164",  "عدد / فقره"),
            ("1644", "کیلوگرم"),
            ("1627", "متر"),
            ("1628", "متر مربع"),
            ("1647", "متر مکعب"),
            ("1618", "لیتر"),
            ("165",  "بسته"),
            ("169",  "کارتن"),
            ("168",  "دستگاه"),
            ("1615", "تن"),
            ("1624", "گرم"),
            ("161",  "ساعت"),
            ("1654", "نفر-ساعت"),
            ("1645", "روز"),
            ("1653", "ماه"),
            ("1635", "جفت"),
            ("1611", "حلقه / رول"),
            ("1639", "برگ"),
            ("1620", "جلد"),
            ("1676", "سرویس / خدمت")
        };

        foreach (var (code, name) in defaults)
            db.MdnMeasureUnits.Add(new MdnMeasureUnit { Code = code, Name = name, IsActive = true });

        await db.SaveChangesAsync();
    }
}
