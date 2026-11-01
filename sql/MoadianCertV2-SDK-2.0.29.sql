/* ============================================================================
   اسکریپت دستی ارتقای دیتابیس سامانه مودیان به نسخه گواهی امضا (SDK 2.0.29)
   ----------------------------------------------------------------------------
   توجه: برنامه به صورت خودکار در هنگام راه‌اندازی (از طریق EF Migration و
   MoadianCertSchemaV2.EnsureAsync) این عملیات را انجام می‌دهد.
   این فایل برای اجرا در SSMS در محیط‌هایی است که کاربرِ ConnectionString مجوز
   DDL (DROP/CREATE TABLE) ندارد.
   این اسکریپت کاملاً Idempotent است و اجرای چندباره آن بی‌خطر است.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 50001, N'لطفاً دیتابیس اصلی نرم‌افزار را انتخاب کنید (نه دیتابیس‌های سیستمی SQL Server).', 1;

BEGIN TRANSACTION;

-- ۱) حذف محدودیت‌های کلید خارجی جداول منسوخ مودیان (0.0.27 / 0.0.34)
DECLARE @dropFkSql nvarchar(max) = N'';
SELECT @dropFkSql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id))
    + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' + CHAR(10)
FROM sys.foreign_keys
WHERE OBJECT_NAME(parent_object_id) IN (
    N'InvoiceStatusHistory', N'InvoicePayment', N'InvoiceBody', N'InvoiceHeader',
    N'MoadianGood', N'MoadianCustomer', N'MeasuringUnit', N'MoadianFiscalYear', N'MoadianCompany',
    N'MoadianLogs', N'MoadianInvoiceLines', N'MoadianInvoices',
    N'MoadianFiscalPeriods', N'MoadianCpcList', N'MoadianSettings', N'FiscalPrinterSettings'
)
OR OBJECT_NAME(referenced_object_id) IN (
    N'InvoiceStatusHistory', N'InvoicePayment', N'InvoiceBody', N'InvoiceHeader',
    N'MoadianGood', N'MoadianCustomer', N'MeasuringUnit', N'MoadianFiscalYear', N'MoadianCompany',
    N'MoadianLogs', N'MoadianInvoiceLines', N'MoadianInvoices',
    N'MoadianFiscalPeriods', N'MoadianCpcList', N'MoadianSettings', N'FiscalPrinterSettings'
);
IF LEN(@dropFkSql) > 0 EXEC sp_executesql @dropFkSql;

-- ۲) حذف کامل جداول قدیمی سامانه مودیان
DROP TABLE IF EXISTS [dbo].[InvoiceStatusHistory];
DROP TABLE IF EXISTS [dbo].[InvoicePayment];
DROP TABLE IF EXISTS [dbo].[InvoiceBody];
DROP TABLE IF EXISTS [dbo].[InvoiceHeader];
DROP TABLE IF EXISTS [dbo].[MoadianGood];
DROP TABLE IF EXISTS [dbo].[MoadianCustomer];
DROP TABLE IF EXISTS [dbo].[MeasuringUnit];
DROP TABLE IF EXISTS [dbo].[MoadianFiscalYear];
DROP TABLE IF EXISTS [dbo].[MoadianCompany];
DROP TABLE IF EXISTS [dbo].[MoadianLogs];
DROP TABLE IF EXISTS [dbo].[MoadianInvoiceLines];
DROP TABLE IF EXISTS [dbo].[MoadianInvoices];
DROP TABLE IF EXISTS [dbo].[MoadianFiscalPeriods];
DROP TABLE IF EXISTS [dbo].[MoadianCpcList];
DROP TABLE IF EXISTS [dbo].[MoadianSettings];
DROP TABLE IF EXISTS [dbo].[FiscalPrinterSettings];

-- ۳) ساخت جداول جدید Mdn* (مبتنی بر گواهی امضای الکترونیکی و SDK 2.0.29)
IF OBJECT_ID(N'dbo.MdnMeasureUnits', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnMeasureUnits] (
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MdnMeasureUnits] PRIMARY KEY,
        [Code] nvarchar(16) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_MdnMeasureUnits_IsActive] DEFAULT(1)
    );
    CREATE UNIQUE INDEX [IX_MdnMeasureUnits_Code] ON [dbo].[MdnMeasureUnits] ([Code]);
END;

IF OBJECT_ID(N'dbo.MdnTaxpayerProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnTaxpayerProfiles] (
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MdnTaxpayerProfiles] PRIMARY KEY,
        [CompanyId] int NULL,
        [TradeName] nvarchar(200) NOT NULL CONSTRAINT [DF_MdnProfile_TradeName] DEFAULT(N''),
        [TaxpayerType] int NOT NULL CONSTRAINT [DF_MdnProfile_Type] DEFAULT(2),
        [EconomicCode] nvarchar(20) NOT NULL CONSTRAINT [DF_MdnProfile_Eco] DEFAULT(N''),
        [NationalId] nvarchar(20) NULL,
        [PostalCode] nvarchar(12) NULL,
        [BranchCode] nvarchar(10) NULL,
        [Address] nvarchar(500) NULL,
        [MemoryId] nvarchar(12) NOT NULL CONSTRAINT [DF_MdnProfile_Memory] DEFAULT(N''),
        [Environment] int NOT NULL CONSTRAINT [DF_MdnProfile_Env] DEFAULT(0),
        [ProductionBaseUrl] nvarchar(300) NOT NULL CONSTRAINT [DF_MdnProfile_ProdUrl] DEFAULT(N'https://tp.tax.gov.ir/requestsmanager'),
        [SandboxBaseUrl] nvarchar(300) NOT NULL CONSTRAINT [DF_MdnProfile_SandUrl] DEFAULT(N'https://rc.tax.gov.ir/requestsmanager'),
        [SignatureMode] int NOT NULL CONSTRAINT [DF_MdnProfile_SigMode] DEFAULT(0),
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
        [NextSerial] bigint NOT NULL CONSTRAINT [DF_MdnProfile_Serial] DEFAULT(1),
        [DefaultVatRate] decimal(6,2) NOT NULL CONSTRAINT [DF_MdnProfile_Vat] DEFAULT(10),
        [IsActive] bit NOT NULL CONSTRAINT [DF_MdnProfile_Active] DEFAULT(1),
        [LastConnectionTestAt] datetime2 NULL,
        [LastConnectionStatus] nvarchar(300) NULL,
        [LastFiscalStatus] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_MdnProfile_Created] DEFAULT(SYSDATETIME()),
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_MdnProfile_Updated] DEFAULT(SYSDATETIME())
    );
    CREATE UNIQUE INDEX [IX_MdnTaxpayerProfiles_CompanyId] ON [dbo].[MdnTaxpayerProfiles] ([CompanyId]) WHERE [CompanyId] IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.MdnBuyers', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnBuyers] (
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MdnBuyers] PRIMARY KEY,
        [CompanyId] int NULL,
        [PartyId] int NULL,
        [Name] nvarchar(200) NOT NULL,
        [BuyerType] int NOT NULL CONSTRAINT [DF_MdnBuyers_Type] DEFAULT(2),
        [EconomicCode] nvarchar(20) NULL,
        [NationalId] nvarchar(20) NULL,
        [PostalCode] nvarchar(12) NULL,
        [BranchCode] nvarchar(10) NULL,
        [PassportNumber] nvarchar(24) NULL,
        [Phone] nvarchar(30) NULL,
        [Address] nvarchar(500) NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_MdnBuyers_Active] DEFAULT(1),
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_MdnBuyers_Created] DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_MdnBuyers_CompanyId_EconomicCode] ON [dbo].[MdnBuyers] ([CompanyId], [EconomicCode]);
    CREATE INDEX [IX_MdnBuyers_CompanyId_PartyId] ON [dbo].[MdnBuyers] ([CompanyId], [PartyId]);
END;

IF OBJECT_ID(N'dbo.MdnCatalogItems', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnCatalogItems] (
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MdnCatalogItems] PRIMARY KEY,
        [CompanyId] int NULL,
        [ProductId] int NULL,
        [InternalCode] nvarchar(60) NULL,
        [Name] nvarchar(250) NOT NULL,
        [Sstid] nvarchar(20) NOT NULL,
        [UnitCode] nvarchar(16) NOT NULL CONSTRAINT [DF_MdnCat_Unit] DEFAULT(N'164'),
        [VatRate] decimal(6,2) NOT NULL CONSTRAINT [DF_MdnCat_Vat] DEFAULT(10),
        [DefaultUnitPrice] bigint NOT NULL CONSTRAINT [DF_MdnCat_Price] DEFAULT(0),
        [HsCode] nvarchar(30) NULL,
        [IsService] bit NOT NULL CONSTRAINT [DF_MdnCat_Svc] DEFAULT(0),
        [IsActive] bit NOT NULL CONSTRAINT [DF_MdnCat_Active] DEFAULT(1),
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_MdnCat_Created] DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_MdnCatalogItems_CompanyId_Sstid] ON [dbo].[MdnCatalogItems] ([CompanyId], [Sstid]);
    CREATE INDEX [IX_MdnCatalogItems_CompanyId_ProductId] ON [dbo].[MdnCatalogItems] ([CompanyId], [ProductId]);
END;

IF OBJECT_ID(N'dbo.MdnInvoices', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnInvoices] (
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MdnInvoices] PRIMARY KEY,
        [CompanyId] int NULL,
        [InternalNumber] nvarchar(40) NOT NULL,
        [SerialDecimal] bigint NOT NULL CONSTRAINT [DF_MdnInv_SerialDec] DEFAULT(0),
        [SerialHex] nvarchar(16) NULL,
        [TaxId] nvarchar(30) NULL,
        [ReferenceTaxId] nvarchar(30) NULL,
        [ParentInvoiceId] int NULL,
        [Uid] nvarchar(64) NULL,
        [ReferenceNumber] nvarchar(80) NULL,
        [MemoryId] nvarchar(12) NULL,
        [IssueDate] datetime2 NOT NULL,
        [CreateDate] datetime2 NOT NULL,
        [InvoiceType] int NOT NULL CONSTRAINT [DF_MdnInv_Type] DEFAULT(1),
        [Pattern] int NOT NULL CONSTRAINT [DF_MdnInv_Pattern] DEFAULT(1),
        [Subject] int NOT NULL CONSTRAINT [DF_MdnInv_Subject] DEFAULT(1),
        [SettlementMethod] int NOT NULL CONSTRAINT [DF_MdnInv_Setm] DEFAULT(1),
        [SellerEconomicCode] nvarchar(20) NULL,
        [SellerTradeName] nvarchar(200) NULL,
        [SellerPostalCode] nvarchar(12) NULL,
        [SellerBranchCode] nvarchar(10) NULL,
        [BuyerId] int NULL,
        [BuyerName] nvarchar(200) NULL,
        [BuyerType] int NOT NULL CONSTRAINT [DF_MdnInv_BuyerType] DEFAULT(2),
        [BuyerEconomicCode] nvarchar(20) NULL,
        [BuyerNationalId] nvarchar(20) NULL,
        [BuyerPostalCode] nvarchar(12) NULL,
        [BuyerBranchCode] nvarchar(10) NULL,
        [BuyerPassportNumber] nvarchar(24) NULL,
        [ContractRegistrationNumber] nvarchar(30) NULL,
        [BillId] nvarchar(30) NULL,
        [CustomsLicenseNumber] nvarchar(30) NULL,
        [CustomsCode] nvarchar(20) NULL,
        [CustomsDeclarationNumber] nvarchar(30) NULL,
        [CustomsDeclarationDate] datetime2 NULL,
        [Tax17] bigint NULL,
        [TotalBeforeDiscount] bigint NOT NULL CONSTRAINT [DF_MdnInv_Tprdis] DEFAULT(0),
        [TotalDiscount] bigint NOT NULL CONSTRAINT [DF_MdnInv_Tdis] DEFAULT(0),
        [TotalAfterDiscount] bigint NOT NULL CONSTRAINT [DF_MdnInv_Tadis] DEFAULT(0),
        [TotalVat] bigint NOT NULL CONSTRAINT [DF_MdnInv_Tvam] DEFAULT(0),
        [TotalOtherTaxes] bigint NOT NULL CONSTRAINT [DF_MdnInv_Todam] DEFAULT(0),
        [TotalAmount] bigint NOT NULL CONSTRAINT [DF_MdnInv_Tbill] DEFAULT(0),
        [CashAmount] bigint NOT NULL CONSTRAINT [DF_MdnInv_Cap] DEFAULT(0),
        [CreditAmount] bigint NOT NULL CONSTRAINT [DF_MdnInv_Insp] DEFAULT(0),
        [PaidAmount] bigint NOT NULL CONSTRAINT [DF_MdnInv_Paid] DEFAULT(0),
        [SubmissionStatus] int NOT NULL CONSTRAINT [DF_MdnInv_SubStatus] DEFAULT(0),
        [FolderStatus] int NOT NULL CONSTRAINT [DF_MdnInv_FolderStatus] DEFAULT(0),
        [FolderStatusRaw] nvarchar(60) NULL,
        [Article6Status] nvarchar(60) NULL,
        [PaymentStatus] int NOT NULL CONSTRAINT [DF_MdnInv_PayStatus] DEFAULT(0),
        [SentAt] datetime2 NULL,
        [LastInquiryAt] datetime2 NULL,
        [LastFolderInquiryAt] datetime2 NULL,
        [LastErrorCode] nvarchar(80) NULL,
        [LastErrorMessage] nvarchar(2000) NULL,
        [SourceFacInvoiceId] int NULL,
        [SourceFacInvoiceNumber] int NULL,
        [Description] nvarchar(600) NULL,
        [CreatedByUserId] int NOT NULL CONSTRAINT [DF_MdnInv_CreatedBy] DEFAULT(0),
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_MdnInv_Updated] DEFAULT(SYSDATETIME()),
        CONSTRAINT [FK_MdnInvoices_Parent] FOREIGN KEY ([ParentInvoiceId]) REFERENCES [dbo].[MdnInvoices] ([Id]),
        CONSTRAINT [FK_MdnInvoices_Buyer] FOREIGN KEY ([BuyerId]) REFERENCES [dbo].[MdnBuyers] ([Id]) ON DELETE SET NULL
    );
    CREATE INDEX [IX_MdnInvoices_CompanyId_IssueDate] ON [dbo].[MdnInvoices] ([CompanyId], [IssueDate]);
    CREATE INDEX [IX_MdnInvoices_CompanyId_TaxId] ON [dbo].[MdnInvoices] ([CompanyId], [TaxId]);
    CREATE INDEX [IX_MdnInvoices_CompanyId_ReferenceNumber] ON [dbo].[MdnInvoices] ([CompanyId], [ReferenceNumber]);
    CREATE INDEX [IX_MdnInvoices_CompanyId_SubmissionStatus] ON [dbo].[MdnInvoices] ([CompanyId], [SubmissionStatus]);
    CREATE INDEX [IX_MdnInvoices_CompanyId_FolderStatus] ON [dbo].[MdnInvoices] ([CompanyId], [FolderStatus]);
END;

IF OBJECT_ID(N'dbo.MdnInvoiceLines', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnInvoiceLines] (
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MdnInvoiceLines] PRIMARY KEY,
        [CompanyId] int NULL,
        [InvoiceId] int NOT NULL,
        [RowNumber] int NOT NULL CONSTRAINT [DF_MdnLine_Row] DEFAULT(1),
        [CatalogItemId] int NULL,
        [Sstid] nvarchar(20) NOT NULL,
        [Description] nvarchar(400) NOT NULL,
        [UnitCode] nvarchar(16) NOT NULL CONSTRAINT [DF_MdnLine_Unit] DEFAULT(N'164'),
        [Quantity] decimal(18,4) NOT NULL CONSTRAINT [DF_MdnLine_Qty] DEFAULT(1),
        [UnitPrice] decimal(18,4) NOT NULL CONSTRAINT [DF_MdnLine_Price] DEFAULT(0),
        [PriceBeforeDiscount] bigint NOT NULL CONSTRAINT [DF_MdnLine_Prdis] DEFAULT(0),
        [DiscountAmount] bigint NOT NULL CONSTRAINT [DF_MdnLine_Dis] DEFAULT(0),
        [PriceAfterDiscount] bigint NOT NULL CONSTRAINT [DF_MdnLine_Adis] DEFAULT(0),
        [VatRate] decimal(6,2) NOT NULL CONSTRAINT [DF_MdnLine_Vra] DEFAULT(10),
        [VatAmount] bigint NOT NULL CONSTRAINT [DF_MdnLine_Vam] DEFAULT(0),
        [OtherTaxSubject] nvarchar(255) NULL,
        [OtherTaxRate] decimal(6,2) NULL,
        [OtherTaxAmount] bigint NOT NULL CONSTRAINT [DF_MdnLine_Odam] DEFAULT(0),
        [OtherLegalFeeSubject] nvarchar(255) NULL,
        [OtherLegalFeeRate] decimal(6,2) NULL,
        [OtherLegalFeeAmount] bigint NOT NULL CONSTRAINT [DF_MdnLine_Olam] DEFAULT(0),
        [ConstructionFee] bigint NULL,
        [SellerProfit] bigint NULL,
        [BrokerFee] bigint NULL,
        [CurrencyCode] nvarchar(10) NULL,
        [ExchangeRate] bigint NULL,
        [NetWeight] decimal(18,4) NULL,
        [HsCode] nvarchar(30) NULL,
        [ContractRegistrationNumber] nvarchar(30) NULL,
        [TotalLineAmount] bigint NOT NULL CONSTRAINT [DF_MdnLine_Tsstam] DEFAULT(0),
        CONSTRAINT [FK_MdnInvoiceLines_Invoice] FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[MdnInvoices] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_MdnInvoiceLines_InvoiceId] ON [dbo].[MdnInvoiceLines] ([InvoiceId]);
END;

IF OBJECT_ID(N'dbo.MdnInvoicePayments', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnInvoicePayments] (
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MdnInvoicePayments] PRIMARY KEY,
        [CompanyId] int NULL,
        [InvoiceId] int NOT NULL,
        [PaymentMethod] int NOT NULL CONSTRAINT [DF_MdnPay_Method] DEFAULT(3),
        [PaidAmount] bigint NOT NULL CONSTRAINT [DF_MdnPay_Amount] DEFAULT(0),
        [PaymentDate] datetime2 NOT NULL,
        [SwitchNumber] nvarchar(20) NULL,
        [AcceptorNumber] nvarchar(20) NULL,
        [TerminalNumber] nvarchar(20) NULL,
        [ReferenceNumber] nvarchar(50) NULL,
        [MaskedCardNumber] nvarchar(24) NULL,
        [PayerNationalId] nvarchar(20) NULL,
        [Note] nvarchar(400) NULL,
        [SyncState] int NOT NULL CONSTRAINT [DF_MdnPay_State] DEFAULT(0),
        [RegisteredAt] datetime2 NULL,
        [LastError] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_MdnPay_Created] DEFAULT(SYSDATETIME()),
        CONSTRAINT [FK_MdnInvoicePayments_Invoice] FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[MdnInvoices] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_MdnInvoicePayments_InvoiceId] ON [dbo].[MdnInvoicePayments] ([InvoiceId]);
END;

IF OBJECT_ID(N'dbo.MdnSubmissionLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MdnSubmissionLogs] (
        [Id] int NOT NULL IDENTITY(1,1) CONSTRAINT [PK_MdnSubmissionLogs] PRIMARY KEY,
        [CompanyId] int NULL,
        [InvoiceId] int NULL,
        [ActionType] nvarchar(40) NOT NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_MdnLog_Created] DEFAULT(SYSDATETIME()),
        [IsSuccess] bit NOT NULL CONSTRAINT [DF_MdnLog_Success] DEFAULT(0),
        [StatusCode] nvarchar(60) NOT NULL CONSTRAINT [DF_MdnLog_Code] DEFAULT(N''),
        [Uid] nvarchar(64) NULL,
        [ReferenceNumber] nvarchar(80) NULL,
        [TaxId] nvarchar(30) NULL,
        [Summary] nvarchar(600) NOT NULL CONSTRAINT [DF_MdnLog_Summary] DEFAULT(N''),
        [ErrorDetailsJson] nvarchar(max) NULL,
        [CreatedByUsername] nvarchar(100) NULL,
        CONSTRAINT [FK_MdnSubmissionLogs_Invoice] FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[MdnInvoices] ([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_MdnSubmissionLogs_CompanyId_CreatedAt] ON [dbo].[MdnSubmissionLogs] ([CompanyId], [CreatedAt]);
    CREATE INDEX [IX_MdnSubmissionLogs_InvoiceId] ON [dbo].[MdnSubmissionLogs] ([InvoiceId]);
END;

COMMIT TRANSACTION;
PRINT N'ارتقای جداول سامانه مودیان (SDK 2.0.29) با موفقیت انجام شد.';
GO
