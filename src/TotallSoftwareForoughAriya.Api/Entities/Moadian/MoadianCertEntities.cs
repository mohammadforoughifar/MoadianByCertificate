using System.ComponentModel.DataAnnotations;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Data;

// =====================================================================
// موجودیت‌های ماژول جدید سامانه مودیان مبتنی بر گواهی امضای الکترونیکی
// نسخهٔ SDK: TaxCollectData.Library 2.0.29
// =====================================================================

/// <summary>
/// پروفایل مودی، تنظیمات حافظهٔ مالیاتی و گواهی امضای الکترونیکی به تفکیک هر شرکت سیستم.
/// کلید خصوصی، گواهی و رمز توکن فقط به صورت رمزنگاری‌شدهٔ AES-256-GCM (با کلید اصلی خارج از دیتابیس) ذخیره می‌شوند.
/// </summary>
public class MdnTaxpayerProfile : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }

    [MaxLength(200)] public string TradeName { get; set; } = "";
    public MdnTaxpayerType TaxpayerType { get; set; } = MdnTaxpayerType.Legal;
    [MaxLength(20)] public string EconomicCode { get; set; } = "";
    [MaxLength(20)] public string? NationalId { get; set; }
    [MaxLength(20)] public string? PostalCode { get; set; }
    [MaxLength(10)] public string? BranchCode { get; set; }
    [MaxLength(400)] public string? Address { get; set; }

    /// <summary>شناسه یکتای ۶ کاراکتری حافظه مالیاتی (مثال: A11226).</summary>
    [MaxLength(12)] public string MemoryId { get; set; } = "";

    public MdnEnvironment Environment { get; set; } = MdnEnvironment.Production;
    [MaxLength(300)] public string ProductionBaseUrl { get; set; } = "https://tp.tax.gov.ir/requestsmanager";
    [MaxLength(300)] public string SandboxBaseUrl { get; set; } = "https://rc.tax.gov.ir/requestsmanager";
    public MdnSignatureMode SignatureMode { get; set; } = MdnSignatureMode.Pkcs8Certificate;

    // ---------- مواد رمزنگاری‌شده امضا (AES-256-GCM با AAD شرکت) ----------
    public string? EncryptedPrivateKeyPem { get; set; }
    public string? EncryptedCertificateBase64 { get; set; }

    // فرادادهٔ غیرحساس گواهی X.509 برای نمایش و هشدار انقضا
    [MaxLength(400)] public string? CertificateSubject { get; set; }
    [MaxLength(400)] public string? CertificateIssuer { get; set; }
    [MaxLength(120)] public string? CertificateSerial { get; set; }
    [MaxLength(120)] public string? CertificateThumbprint { get; set; }
    public DateTime? CertificateNotBefore { get; set; }
    public DateTime? CertificateNotAfter { get; set; }

    // ---------- توکن سخت‌افزاری PKCS#11 ----------
    [MaxLength(400)] public string? Pkcs11LibraryPath { get; set; }
    [MaxLength(200)] public string? Pkcs11CertificateAlias { get; set; }
    public string? EncryptedPkcs11Pin { get; set; }

    /// <summary>شمارندهٔ سریال ده‌دهی برای تولید TaxId و inno هگزادسیمال.</summary>
    public long NextSerial { get; set; } = 1;
    public decimal DefaultVatRate { get; set; } = 10;

    /// <summary>مهلت مجاز ارسال عادی صورتحساب (به روز) طبق اعلام سازمان امور مالیاتی؛ مازاد بر آن مشمول موضوع ماده ۹ (insr=1) است.</summary>
    public int Article9SendDeadlineDays { get; set; } = MdnArticle9Rules.DefaultSendDeadlineDays;

    public bool IsActive { get; set; } = true;

    public DateTime? LastConnectionTestAt { get; set; }
    [MaxLength(300)] public string? LastConnectionStatus { get; set; }
    [MaxLength(120)] public string? LastFiscalStatus { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>واحدهای سنجش رسمی سازمان امور مالیاتی (مشترک در کل سامانه).</summary>
public class MdnMeasureUnit
{
    public int Id { get; set; }
    [MaxLength(20)] public string Code { get; set; } = "";
    [MaxLength(150)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

/// <summary>خریداران صورتحساب الکترونیکی (با قابلیت همگام‌سازی با Parties سیستم).</summary>
public class MdnBuyer : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int? PartyId { get; set; }

    [MaxLength(200)] public string Name { get; set; } = "";
    public MdnTaxpayerType BuyerType { get; set; } = MdnTaxpayerType.Legal;
    [MaxLength(20)] public string? EconomicCode { get; set; }
    [MaxLength(20)] public string? NationalId { get; set; }
    [MaxLength(20)] public string? PostalCode { get; set; }
    [MaxLength(10)] public string? BranchCode { get; set; }
    [MaxLength(30)] public string? PassportNumber { get; set; }
    [MaxLength(50)] public string? Phone { get; set; }
    [MaxLength(400)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>کالاها و خدمات مشمول مودیان به همراه شناسهٔ ۱۳ رقمی (sstid) و قابلیت همگام‌سازی با Products سیستم.</summary>
public class MdnCatalogItem : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int? ProductId { get; set; }

    [MaxLength(60)] public string? InternalCode { get; set; }
    [MaxLength(250)] public string Name { get; set; } = "";
    [MaxLength(20)] public string Sstid { get; set; } = "";
    [MaxLength(20)] public string UnitCode { get; set; } = "164";
    public decimal VatRate { get; set; } = 10;
    public long DefaultUnitPrice { get; set; }
    [MaxLength(30)] public string? HsCode { get; set; }
    public bool IsService { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>صورتحساب الکترونیکی سامانه مودیان (پشتیبانی کامل از چرخهٔ ارسال، کارپوشه و پرداخت SDK 2.0.29).</summary>
public class MdnInvoice : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }

    [MaxLength(40)] public string InternalNumber { get; set; } = "";
    public long SerialDecimal { get; set; }
    [MaxLength(16)] public string? SerialHex { get; set; }

    /// <summary>شماره منحصر به فرد مالیاتی ۲۲ کاراکتری (taxid).</summary>
    [MaxLength(30)] public string? TaxId { get; set; }

    /// <summary>شماره مالیاتی صورتحساب مرجع برای اصلاحی/ابطالی/برگشت از فروش (irtaxid).</summary>
    [MaxLength(30)] public string? ReferenceTaxId { get; set; }
    public int? ParentInvoiceId { get; set; }

    [MaxLength(64)] public string? Uid { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(12)] public string? MemoryId { get; set; }

    public DateTime IssueDate { get; set; } = DateTime.Now;
    public DateTime CreateDate { get; set; } = DateTime.Now;

    /// <summary>تاریخ و زمان ثبت صورتحساب (indati2m در دستورالعمل V7.9 — اجباری برای صورتحساب‌های موضوع ماده ۹).</summary>
    public DateTime? RegistrationDate { get; set; }

    /// <summary>آیا صورتحساب مشمول قاعدهٔ موضوع ماده ۹ قانون پایانه‌های فروشگاهی (ارسال خارج از مهلت مجاز — insr=1) است؟</summary>
    public bool IsArticle9 { get; set; }

    /// <summary>مقدار فیلد قاعده ارسال صورتحساب (header.insr در جدول ۸۶ دستورالعمل V7.9؛ ۱ برای ماده ۹، تهی برای عادی).</summary>
    public int? SendRule { get; set; }

    public MdnInvoiceType InvoiceType { get; set; } = MdnInvoiceType.Type1;
    public MdnInvoicePattern Pattern { get; set; } = MdnInvoicePattern.Sale;
    public MdnInvoiceSubject Subject { get; set; } = MdnInvoiceSubject.Original;
    public MdnSettlementMethod SettlementMethod { get; set; } = MdnSettlementMethod.Cash;

    // ---------- اسنپ‌شات فروشنده ----------
    [MaxLength(20)] public string? SellerEconomicCode { get; set; }
    [MaxLength(200)] public string? SellerTradeName { get; set; }
    [MaxLength(20)] public string? SellerPostalCode { get; set; }
    [MaxLength(10)] public string? SellerBranchCode { get; set; }

    // ---------- اطلاعات و اسنپ‌شات خریدار ----------
    public int? BuyerId { get; set; }
    [MaxLength(200)] public string? BuyerName { get; set; }
    public MdnTaxpayerType BuyerType { get; set; } = MdnTaxpayerType.Legal;
    [MaxLength(20)] public string? BuyerEconomicCode { get; set; }
    [MaxLength(20)] public string? BuyerNationalId { get; set; }
    [MaxLength(20)] public string? BuyerPostalCode { get; set; }
    [MaxLength(10)] public string? BuyerBranchCode { get; set; }
    [MaxLength(30)] public string? BuyerPassportNumber { get; set; }

    // ---------- فیلدهای تکمیلی الگوها ----------
    [MaxLength(30)] public string? ContractRegistrationNumber { get; set; }
    [MaxLength(30)] public string? BillId { get; set; }
    [MaxLength(30)] public string? CustomsLicenseNumber { get; set; }
    [MaxLength(20)] public string? CustomsCode { get; set; }
    [MaxLength(30)] public string? CustomsDeclarationNumber { get; set; }
    public DateTime? CustomsDeclarationDate { get; set; }
    public long? Tax17 { get; set; }

    /// <summary>یادداشت ۱ و ۲ سرآمد (nti1 و nti2 در جداول ۸۷ و ۸۸ دستورالعمل RC_IITP.IS_V7.9 — حداکثر ۳۰ کاراکتر).</summary>
    [MaxLength(30)] public string? Note1 { get; set; }
    [MaxLength(30)] public string? Note2 { get; set; }

    // ---------- مبالغ ریالی رسمی ----------
    public long TotalBeforeDiscount { get; set; }
    public long TotalDiscount { get; set; }
    public long TotalAfterDiscount { get; set; }
    public long TotalVat { get; set; }
    public long TotalOtherTaxes { get; set; }
    public long TotalAmount { get; set; }
    public long CashAmount { get; set; }
    public long CreditAmount { get; set; }
    public long PaidAmount { get; set; }

    // ---------- وضعیت‌های سه‌گانه (ارسال + کارپوشه + پرداخت) ----------
    public MdnSubmissionStatus SubmissionStatus { get; set; } = MdnSubmissionStatus.Draft;
    public MdnFolderStatus FolderStatus { get; set; } = MdnFolderStatus.Unknown;
    [MaxLength(60)] public string? FolderStatusRaw { get; set; }
    [MaxLength(60)] public string? Article6Status { get; set; }
    public MdnPaymentStatus PaymentStatus { get; set; } = MdnPaymentStatus.Unpaid;

    public DateTime? SentAt { get; set; }
    public DateTime? LastInquiryAt { get; set; }
    public DateTime? LastFolderInquiryAt { get; set; }
    [MaxLength(80)] public string? LastErrorCode { get; set; }
    [MaxLength(2000)] public string? LastErrorMessage { get; set; }
    [MaxLength(600)] public string? Description { get; set; }

    // ---------- اتصال اختیاری به فاکتور فروش سیستم ----------
    public int? SourceFacInvoiceId { get; set; }
    public int? SourceFacInvoiceNumber { get; set; }

    public int CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<MdnInvoiceLine> Lines { get; set; } = new();
    public List<MdnInvoicePayment> Payments { get; set; } = new();
    public List<MdnSubmissionLog> Logs { get; set; } = new();
}

/// <summary>قلم کالا/خدمت صورتحساب الکترونیکی مودیان (BodyItemDto).</summary>
public class MdnInvoiceLine : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int InvoiceId { get; set; }
    public MdnInvoice? Invoice { get; set; }

    public int RowNumber { get; set; }
    public int? CatalogItemId { get; set; }

    [MaxLength(20)] public string Sstid { get; set; } = "";
    [MaxLength(400)] public string Description { get; set; } = "";
    [MaxLength(20)] public string UnitCode { get; set; } = "164";

    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public long PriceBeforeDiscount { get; set; }
    public long DiscountAmount { get; set; }
    public long PriceAfterDiscount { get; set; }

    public decimal VatRate { get; set; } = 10;
    public long VatAmount { get; set; }

    [MaxLength(255)] public string? OtherTaxSubject { get; set; }
    public decimal? OtherTaxRate { get; set; }
    public long OtherTaxAmount { get; set; }

    [MaxLength(255)] public string? OtherLegalFeeSubject { get; set; }
    public decimal? OtherLegalFeeRate { get; set; }
    public long OtherLegalFeeAmount { get; set; }

    public long? ConstructionFee { get; set; }
    public long? SellerProfit { get; set; }
    public long? BrokerFee { get; set; }

    [MaxLength(10)] public string? CurrencyCode { get; set; }
    public long? ExchangeRate { get; set; }
    public decimal? NetWeight { get; set; }
    [MaxLength(30)] public string? HsCode { get; set; }
    [MaxLength(30)] public string? ContractRegistrationNumber { get; set; }

    public long TotalLineAmount { get; set; }
}

/// <summary>تراکنش‌های پرداخت صورتحساب (همراه صورتحساب یا ثبت پسینی با RegisterPaymentRequestAsync).</summary>
public class MdnInvoicePayment : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int InvoiceId { get; set; }
    public MdnInvoice? Invoice { get; set; }

    public MdnPaymentMethod PaymentMethod { get; set; } = MdnPaymentMethod.Pos;
    public long PaidAmount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;

    [MaxLength(30)] public string? SwitchNumber { get; set; }
    [MaxLength(30)] public string? AcceptorNumber { get; set; }
    [MaxLength(40)] public string? TerminalNumber { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(30)] public string? MaskedCardNumber { get; set; }
    [MaxLength(20)] public string? PayerNationalId { get; set; }
    [MaxLength(400)] public string? Note { get; set; }

    public MdnPaymentSyncState SyncState { get; set; } = MdnPaymentSyncState.LocalDraft;
    public DateTime? RegisteredAt { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>ردیابی و لاگ ممیزی همهٔ ارتباطات با سامانه مودیان (ارسال، استعلام درگاه، استعلام کارپوشه، ثبت پرداخت).</summary>
public class MdnSubmissionLog : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int? InvoiceId { get; set; }
    public MdnInvoice? Invoice { get; set; }

    [MaxLength(50)] public string ActionType { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsSuccess { get; set; }
    [MaxLength(60)] public string StatusCode { get; set; } = "";
    [MaxLength(64)] public string? Uid { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(30)] public string? TaxId { get; set; }
    [MaxLength(600)] public string Summary { get; set; } = "";
    public string? ErrorDetailsJson { get; set; }
    [MaxLength(100)] public string? CreatedByUsername { get; set; }
}
