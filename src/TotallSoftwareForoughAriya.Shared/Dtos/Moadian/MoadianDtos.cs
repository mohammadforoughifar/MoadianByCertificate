namespace Inventory.Shared.Dtos;

// =====================================================================
// قراردادها و مدل‌های مشترک ماژول سامانه مودیان (نسخهٔ گواهی امضا — SDK 2.0.29)
// منطبق با TaxCollectData.Library 2.0.29:
//   • امضای دیجیتال با گواهی امضای الکترونیکی (PKCS#8 + X.509 / PFX / توکن PKCS#11)
//   • ارسال صورتحساب (اصلی، اصلاحی، ابطالی، برگشت از فروش — الگوهای ۱ تا ۷)
//   • استعلام وضعیت پردازش ارسال (ReferenceNumber / UID / بازهٔ زمانی)
//   • استعلام وضعیت به‌روز کارپوشه و ماده ۶ (InquiryInvoiceStatus)
//   • ثبت و پیگیری پرداخت صورتحساب در سامانه مودیان (RegisterPaymentRequest)
// =====================================================================

public enum MdnEnvironment
{
    Production = 1,
    Sandbox = 2
}

public enum MdnSignatureMode
{
    Pkcs8Certificate = 1,
    Pkcs11Token = 2
}

/// <summary>نوع شخص مودی / خریدار طبق فیلد رسمی tob در سامانه مودیان.</summary>
public enum MdnTaxpayerType
{
    Natural = 1,
    Legal = 2,
    CivicPartnership = 3,
    ForeignNational = 4
}

/// <summary>نوع صورتحساب (inty).</summary>
public enum MdnInvoiceType
{
    Type1 = 1,
    Type2 = 2
}

/// <summary>الگوی صورتحساب (inp).</summary>
public enum MdnInvoicePattern
{
    Sale = 1,
    CurrencySale = 2,
    GoldJewelry = 3,
    Contracting = 4,
    UtilityBill = 5,
    AirTicket = 6,
    Export = 7
}

/// <summary>موضوع صورتحساب (ins).</summary>
public enum MdnInvoiceSubject
{
    Original = 1,
    Corrective = 2,
    Cancellation = 3,
    SalesReturn = 4
}

/// <summary>روش تسویه صورتحساب (setm).</summary>
public enum MdnSettlementMethod
{
    Cash = 1,
    Credit = 2,
    CashAndCredit = 3
}

/// <summary>وضعیت پردازش بسته در زیرسامانهٔ جمع‌آوری.</summary>
public enum MdnSubmissionStatus
{
    Draft = 0,
    Sent = 1,
    InProgress = 2,
    Success = 3,
    Failed = 4,
    Timeout = 5,
    NotFound = 6
}

/// <summary>وضعیت به‌روز صورتحساب در کارپوشهٔ مودیان (خروجی InquiryInvoiceStatus).</summary>
public enum MdnFolderStatus
{
    Unknown = 0,
    Pending = 1,
    Approved = 2,
    SystemApproved = 3,
    Rejected = 4,
    Cancelled = 5,
    NotFound = 6
}

/// <summary>وضعیت تسویه و پرداخت صورتحساب.</summary>
public enum MdnPaymentStatus
{
    Unpaid = 0,
    PartiallyPaid = 1,
    Paid = 2,
    NotApplicable = 3
}

/// <summary>روش پرداخت هم‌تراز با PaymentMethod در SDK 2.0.29 و کد pmt سامانه مودیان.</summary>
public enum MdnPaymentMethod
{
    Cheque = 1,
    Barter = 2,
    Cash = 3,
    Pos = 4,
    Internet = 5,
    CardToCard = 6,
    BankTransfer = 7,
    Other = 8
}

/// <summary>وضعیت ثبت پرداخت در سامانه مودیان (RegisterPaymentRequest).</summary>
public enum MdnPaymentSyncState
{
    IncludedInInvoice = 0,
    LocalDraft = 1,
    Registered = 2,
    InProgress = 3,
    Failed = 4
}

// ==================== پروفایل مودی، اتصال و گواهی امضا ====================

public class MdnProfileDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public string TradeName { get; set; } = "";
    public MdnTaxpayerType TaxpayerType { get; set; } = MdnTaxpayerType.Legal;
    public string EconomicCode { get; set; } = "";
    public string? NationalId { get; set; }
    public string? PostalCode { get; set; }
    public string? BranchCode { get; set; }
    public string? Address { get; set; }

    public string MemoryId { get; set; } = "";
    public MdnEnvironment Environment { get; set; } = MdnEnvironment.Production;
    public string ProductionBaseUrl { get; set; } = "https://tp.tax.gov.ir/requestsmanager";
    public string SandboxBaseUrl { get; set; } = "https://rc.tax.gov.ir/requestsmanager";
    public MdnSignatureMode SignatureMode { get; set; } = MdnSignatureMode.Pkcs8Certificate;

    public bool HasPrivateKey { get; set; }
    public bool HasCertificate { get; set; }
    public string? CertificateSubject { get; set; }
    public string? CertificateIssuer { get; set; }
    public string? CertificateSerial { get; set; }
    public string? CertificateThumbprint { get; set; }
    public DateTime? CertificateNotBefore { get; set; }
    public DateTime? CertificateNotAfter { get; set; }
    public int? CertificateDaysRemaining { get; set; }
    public bool IsCertificateExpired { get; set; }

    public string? Pkcs11LibraryPath { get; set; }
    public string? Pkcs11CertificateAlias { get; set; }
    public bool HasPkcs11Pin { get; set; }

    public long NextSerial { get; set; } = 1;
    public decimal DefaultVatRate { get; set; } = 10;
    public bool IsActive { get; set; } = true;

    public DateTime? LastConnectionTestAt { get; set; }
    public string? LastConnectionStatus { get; set; }
    public string? LastFiscalStatus { get; set; }
    public bool IsReadyForSigning =>
        !string.IsNullOrWhiteSpace(MemoryId) &&
        !string.IsNullOrWhiteSpace(EconomicCode) &&
        (SignatureMode == MdnSignatureMode.Pkcs8Certificate
            ? HasPrivateKey && HasCertificate && !IsCertificateExpired
            : !string.IsNullOrWhiteSpace(Pkcs11LibraryPath) && !string.IsNullOrWhiteSpace(Pkcs11CertificateAlias) && HasPkcs11Pin);
}

public class MdnSaveProfileRequest
{
    public string TradeName { get; set; } = "";
    public MdnTaxpayerType TaxpayerType { get; set; } = MdnTaxpayerType.Legal;
    public string EconomicCode { get; set; } = "";
    public string? NationalId { get; set; }
    public string? PostalCode { get; set; }
    public string? BranchCode { get; set; }
    public string? Address { get; set; }

    public string MemoryId { get; set; } = "";
    public MdnEnvironment Environment { get; set; } = MdnEnvironment.Production;
    public string ProductionBaseUrl { get; set; } = "https://tp.tax.gov.ir/requestsmanager";
    public string SandboxBaseUrl { get; set; } = "https://rc.tax.gov.ir/requestsmanager";
    public MdnSignatureMode SignatureMode { get; set; } = MdnSignatureMode.Pkcs8Certificate;

    public long NextSerial { get; set; } = 1;
    public decimal DefaultVatRate { get; set; } = 10;
    public bool IsActive { get; set; } = true;
}

public class MdnUploadCertificateRequest
{
    public MdnSignatureMode SignatureMode { get; set; } = MdnSignatureMode.Pkcs8Certificate;

    /// <summary>محتوای PEM یا Base64 کلید خصوصی (PKCS#8 یا RSA PRIVATE KEY).</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>رمز کلید خصوصی در صورت رمزگذاری بودن فایل کلید خصوصی (اختیاری).</summary>
    public string? PrivateKeyPassword { get; set; }

    /// <summary>گواهی امضای الکترونیکی به صورت متن PEM یا Base64 فایل CRT/CER/DER/P7B.</summary>
    public string? CertificateContent { get; set; }

    /// <summary>آپلود یکجای فایل PFX / P12 به صورت Base64 (جایگزین کلید + گواهی جداگانه).</summary>
    public string? PfxBase64 { get; set; }
    public string? PfxPassword { get; set; }

    /// <summary>تنظیمات توکن سخت‌افزاری PKCS#11.</summary>
    public string? Pkcs11LibraryPath { get; set; }
    public string? Pkcs11CertificateAlias { get; set; }
    public string? Pkcs11Pin { get; set; }
}

public class MdnPkcs11DiscoverRequest
{
    /// <summary>مسیر دلخواه فایل DLL/SO؛ اگر خالی باشد تمام مسیرهای استاندارد سیستم اسکن می‌شوند.</summary>
    public string? LibraryPath { get; set; }

    /// <summary>رمز پین توکن (اختیاری؛ برای خواندن گواهی‌های خصوصی و تأیید کلید خصوصی روی توکن).</summary>
    public string? Pin { get; set; }
}

public class MdnPkcs11DriverDto
{
    public string LibraryPath { get; set; } = "";
    public string VendorName { get; set; } = "";
    public bool LoadSucceeded { get; set; }
    public int ConnectedTokensCount { get; set; }
    public string? DiagnosticMessage { get; set; }
}

public class MdnPkcs11CertificateItemDto
{
    public string LibraryPath { get; set; } = "";
    public string VendorName { get; set; } = "";
    public string SlotDescription { get; set; } = "";
    public string TokenLabel { get; set; } = "";
    public string TokenModel { get; set; } = "";
    public string TokenManufacturer { get; set; } = "";
    public string TokenSerial { get; set; } = "";
    public string CertificateAlias { get; set; } = "";
    public string Subject { get; set; } = "";
    public string CommonName { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Thumbprint { get; set; } = "";
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    public bool IsExpired { get; set; }
    public bool IsCertificateAuthority { get; set; }
    public bool HasPrivateKey { get; set; }
}

public class MdnPkcs11DiscoverResultDto
{
    public bool Is64BitProcess { get; set; }
    public string Summary { get; set; } = "";
    public List<MdnPkcs11DriverDto> Drivers { get; set; } = new();
    public List<MdnPkcs11CertificateItemDto> Certificates { get; set; } = new();
}

public class MdnDiagnosticStepDto
{
    public string Title { get; set; } = "";
    public bool Passed { get; set; }
    public string Detail { get; set; } = "";
}

public class MdnConnectionDiagnosticsDto
{
    public bool Success { get; set; }
    public string Summary { get; set; } = "";
    public DateTime CheckedAt { get; set; } = DateTime.Now;
    public string ActiveEndpoint { get; set; } = "";
    public DateTime? ServerTimeUtc { get; set; }
    public double? ClockDriftSeconds { get; set; }
    public string? ServerPublicKeyId { get; set; }
    public string? FiscalStatus { get; set; }
    public string? FiscalTradeName { get; set; }
    public string? FiscalEconomicCode { get; set; }
    public string? FiscalNationalId { get; set; }
    public string? FiscalSaleThreshold { get; set; }
    public string? TaxpayerStatus { get; set; }
    public string? TaxpayerType { get; set; }
    public List<MdnDiagnosticStepDto> Steps { get; set; } = new();
}

public class MdnTaxpayerLookupDto
{
    public string EconomicCode { get; set; } = "";
    public string? NameTrade { get; set; }
    public string? TaxpayerStatus { get; set; }
    public string? TaxpayerType { get; set; }
    public string? NationalId { get; set; }
    public string? PostalCode { get; set; }
    public string? Address { get; set; }
}

public class MdnArticle6CheckRequest
{
    public string EconomicCode { get; set; } = "";
    public long VatValue { get; set; }
    /// <summary>دوره مالیاتی به صورت سال و فصل شمسی، مثال: 14051</summary>
    public int Period { get; set; }
}

public class MdnArticle6CheckResultDto
{
    public string EconomicCode { get; set; } = "";
    public long VatValue { get; set; }
    public int Period { get; set; }
    public bool Article6RemainStatus { get; set; }
    public string Message { get; set; } = "";
}

// ==================== اطلاعات پایه (خریدار، کالا/خدمت، واحد سنجش) ====================

public class MdnBuyerDto
{
    public int Id { get; set; }
    public int? PartyId { get; set; }
    public string Name { get; set; } = "";
    public MdnTaxpayerType BuyerType { get; set; } = MdnTaxpayerType.Legal;
    public string? EconomicCode { get; set; }
    public string? NationalId { get; set; }
    public string? PostalCode { get; set; }
    public string? BranchCode { get; set; }
    public string? PassportNumber { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MdnSaveBuyerRequest
{
    public int Id { get; set; }
    public int? PartyId { get; set; }
    public string Name { get; set; } = "";
    public MdnTaxpayerType BuyerType { get; set; } = MdnTaxpayerType.Legal;
    public string? EconomicCode { get; set; }
    public string? NationalId { get; set; }
    public string? PostalCode { get; set; }
    public string? BranchCode { get; set; }
    public string? PassportNumber { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MdnCatalogItemDto
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public string? InternalCode { get; set; }
    public string Name { get; set; } = "";
    /// <summary>شناسهٔ ۱۳ رقمی کالا/خدمت سازمان امور مالیاتی (sstid).</summary>
    public string Sstid { get; set; } = "";
    public string UnitCode { get; set; } = "164";
    public string UnitName { get; set; } = "عدد";
    public decimal VatRate { get; set; } = 10;
    public long DefaultUnitPrice { get; set; }
    public string? HsCode { get; set; }
    public bool IsService { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MdnSaveCatalogItemRequest
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public string? InternalCode { get; set; }
    public string Name { get; set; } = "";
    public string Sstid { get; set; } = "";
    public string UnitCode { get; set; } = "164";
    public decimal VatRate { get; set; } = 10;
    public long DefaultUnitPrice { get; set; }
    public string? HsCode { get; set; }
    public bool IsService { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MdnMeasureUnitDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class MdnSaveMeasureUnitRequest
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class MdnSyncResultDto
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public string Message { get; set; } = "";
}

// ==================== صورتحساب الکترونیکی، اقلام، پرداخت‌ها و لاگ‌ها ====================

public class MdnInvoiceFilterRequest
{
    public string? Search { get; set; }
    public MdnSubmissionStatus? SubmissionStatus { get; set; }
    public MdnFolderStatus? FolderStatus { get; set; }
    public MdnPaymentStatus? PaymentStatus { get; set; }
    public MdnInvoiceSubject? Subject { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class MdnInvoiceListResultDto
{
    public List<MdnInvoiceSummaryDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class MdnInvoiceSummaryDto
{
    public int Id { get; set; }
    public string InternalNumber { get; set; } = "";
    public long SerialDecimal { get; set; }
    public string? SerialHex { get; set; }
    public string? TaxId { get; set; }
    public string? ReferenceTaxId { get; set; }
    public int? ParentInvoiceId { get; set; }
    public string? Uid { get; set; }
    public string? ReferenceNumber { get; set; }

    public DateTime IssueDate { get; set; }
    public MdnInvoiceType InvoiceType { get; set; }
    public MdnInvoicePattern Pattern { get; set; }
    public MdnInvoiceSubject Subject { get; set; }
    public MdnSettlementMethod SettlementMethod { get; set; }

    public int? BuyerId { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerEconomicCode { get; set; }
    public string? BuyerNationalId { get; set; }

    public long TotalBeforeDiscount { get; set; }
    public long TotalDiscount { get; set; }
    public long TotalAfterDiscount { get; set; }
    public long TotalVat { get; set; }
    public long TotalOtherTaxes { get; set; }
    public long TotalAmount { get; set; }
    public long CashAmount { get; set; }
    public long CreditAmount { get; set; }
    public long PaidAmount { get; set; }
    public long RemainingAmount => Math.Max(0, TotalAmount - PaidAmount);

    public MdnSubmissionStatus SubmissionStatus { get; set; }
    public MdnFolderStatus FolderStatus { get; set; }
    public string? FolderStatusRaw { get; set; }
    public string? Article6Status { get; set; }
    public MdnPaymentStatus PaymentStatus { get; set; }

    public DateTime? SentAt { get; set; }
    public DateTime? LastInquiryAt { get; set; }
    public DateTime? LastFolderInquiryAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }

    public int? SourceFacInvoiceId { get; set; }
    public int? SourceFacInvoiceNumber { get; set; }
}

public class MdnInvoiceLineDto
{
    public int Id { get; set; }
    public int RowNumber { get; set; }
    public int? CatalogItemId { get; set; }
    public string Sstid { get; set; } = "";
    public string Description { get; set; } = "";
    public string UnitCode { get; set; } = "164";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public long PriceBeforeDiscount { get; set; }
    public long DiscountAmount { get; set; }
    public long PriceAfterDiscount { get; set; }
    public decimal VatRate { get; set; } = 10;
    public long VatAmount { get; set; }
    public string? OtherTaxSubject { get; set; }
    public decimal? OtherTaxRate { get; set; }
    public long OtherTaxAmount { get; set; }
    public string? OtherLegalFeeSubject { get; set; }
    public decimal? OtherLegalFeeRate { get; set; }
    public long OtherLegalFeeAmount { get; set; }
    public long? ConstructionFee { get; set; }
    public long? SellerProfit { get; set; }
    public long? BrokerFee { get; set; }
    public string? CurrencyCode { get; set; }
    public long? ExchangeRate { get; set; }
    public decimal? NetWeight { get; set; }
    public string? HsCode { get; set; }
    public string? ContractRegistrationNumber { get; set; }
    public long TotalLineAmount { get; set; }
}

public class MdnInvoicePaymentDto
{
    public int Id { get; set; }
    public MdnPaymentMethod PaymentMethod { get; set; } = MdnPaymentMethod.Pos;
    public long PaidAmount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public string? SwitchNumber { get; set; }
    public string? AcceptorNumber { get; set; }
    public string? TerminalNumber { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? MaskedCardNumber { get; set; }
    public string? PayerNationalId { get; set; }
    public string? Note { get; set; }

    public MdnPaymentSyncState SyncState { get; set; }
    public DateTime? RegisteredAt { get; set; }
    public string? LastError { get; set; }
}

public class MdnSubmissionLogDto
{
    public int Id { get; set; }
    public string ActionType { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsSuccess { get; set; }
    public string StatusCode { get; set; } = "";
    public string? Uid { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? TaxId { get; set; }
    public string Summary { get; set; } = "";
    public string? ErrorDetailsJson { get; set; }
    public string? CreatedByUsername { get; set; }
}

public class MdnInvoiceDetailDto : MdnInvoiceSummaryDto
{
    public DateTime? CreateDate { get; set; }
    public MdnTaxpayerType BuyerType { get; set; } = MdnTaxpayerType.Legal;
    public string? BuyerPostalCode { get; set; }
    public string? BuyerBranchCode { get; set; }
    public string? BuyerPassportNumber { get; set; }
    public string? ContractRegistrationNumber { get; set; }
    public string? BillId { get; set; }
    public string? CustomsLicenseNumber { get; set; }
    public string? CustomsCode { get; set; }
    public string? CustomsDeclarationNumber { get; set; }
    public DateTime? CustomsDeclarationDate { get; set; }
    public long? Tax17 { get; set; }
    public string? SellerEconomicCode { get; set; }
    public string? SellerTradeName { get; set; }
    public string? SellerPostalCode { get; set; }
    public string? SellerBranchCode { get; set; }
    public string? MemoryId { get; set; }
    public string? Description { get; set; }

    public List<MdnInvoiceLineDto> Lines { get; set; } = new();
    public List<MdnInvoicePaymentDto> Payments { get; set; } = new();
    public List<MdnSubmissionLogDto> Logs { get; set; } = new();
    public List<MdnInvoiceSummaryDto> RelatedInvoices { get; set; } = new();
}

public class MdnSaveInvoiceRequest
{
    public int Id { get; set; }
    public DateTime IssueDate { get; set; } = DateTime.Now;
    public MdnInvoiceType InvoiceType { get; set; } = MdnInvoiceType.Type1;
    public MdnInvoicePattern Pattern { get; set; } = MdnInvoicePattern.Sale;
    public MdnInvoiceSubject Subject { get; set; } = MdnInvoiceSubject.Original;
    public MdnSettlementMethod SettlementMethod { get; set; } = MdnSettlementMethod.Cash;

    public int? ParentInvoiceId { get; set; }
    public string? ReferenceTaxId { get; set; }

    public int? BuyerId { get; set; }
    public string? BuyerName { get; set; }
    public MdnTaxpayerType BuyerType { get; set; } = MdnTaxpayerType.Legal;
    public string? BuyerEconomicCode { get; set; }
    public string? BuyerNationalId { get; set; }
    public string? BuyerPostalCode { get; set; }
    public string? BuyerBranchCode { get; set; }
    public string? BuyerPassportNumber { get; set; }

    public string? ContractRegistrationNumber { get; set; }
    public string? BillId { get; set; }
    public string? CustomsLicenseNumber { get; set; }
    public string? CustomsCode { get; set; }
    public string? CustomsDeclarationNumber { get; set; }
    public DateTime? CustomsDeclarationDate { get; set; }
    public long? Tax17 { get; set; }
    public long? CashAmount { get; set; }
    public long? CreditAmount { get; set; }
    public string? Description { get; set; }

    public List<MdnInvoiceLineDto> Lines { get; set; } = new();
    public List<MdnInvoicePaymentDto> InitialPayments { get; set; } = new();
}

public class MdnCreateDerivativeRequest
{
    public MdnInvoiceSubject Subject { get; set; } = MdnInvoiceSubject.Corrective;
    public DateTime IssueDate { get; set; } = DateTime.Now;
    public string? Description { get; set; }
}

public class MdnRegisterPaymentRequest
{
    public MdnPaymentMethod PaymentMethod { get; set; } = MdnPaymentMethod.Pos;
    public long PaidAmount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public string TerminalNumber { get; set; } = "";
    public string ReferenceNumber { get; set; } = "";
    public string? SwitchNumber { get; set; }
    public string? AcceptorNumber { get; set; }
    public string? MaskedCardNumber { get; set; }
    public string? PayerNationalId { get; set; }
    public string? Note { get; set; }
    /// <summary>آیا بلافاصله با RegisterPaymentRequestAsync به سامانه مودیان ارسال شود؟</summary>
    public bool SendToMoadianNow { get; set; } = true;
}

public class MdnBulkIdsRequest
{
    public List<int> InvoiceIds { get; set; } = new();
}

public class MdnTimeRangeInquiryRequest
{
    public DateTime StartDate { get; set; } = DateTime.Now.AddDays(-7);
    public DateTime? EndDate { get; set; } = DateTime.Now;
    public string? Status { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class MdnTimeRangeInquiryItemDto
{
    public string ReferenceNumber { get; set; } = "";
    public string Uid { get; set; } = "";
    public string Status { get; set; } = "";
    public string PacketType { get; set; } = "";
    public string? FiscalId { get; set; }
    public int? MatchedInvoiceId { get; set; }
    public string? MatchedTaxId { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class MdnActionItemOutcomeDto
{
    public int InvoiceId { get; set; }
    public string InternalNumber { get; set; } = "";
    public string? TaxId { get; set; }
    public bool Success { get; set; }
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}

public class MdnBatchActionResultDto
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public string Message { get; set; } = "";
    public List<MdnActionItemOutcomeDto> Items { get; set; } = new();
}

public class MdnInvoiceJsonPreviewDto
{
    public int InvoiceId { get; set; }
    public string PotentialTaxId { get; set; } = "";
    public string PotentialSerialHex { get; set; } = "";
    public string JsonPayload { get; set; } = "";
    public List<string> ValidationWarnings { get; set; } = new();
}

public class MdnErpFacInvoicePickDto
{
    public int Id { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyTaxId { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalVat { get; set; }
    public int LineCount { get; set; }
    public bool AlreadyImported { get; set; }
}

// ==================== داشبورد مدیریتی مودیان ====================

public class MdnDashboardDto
{
    public MdnProfileDto Profile { get; set; } = new();

    public int TotalInvoices { get; set; }
    public int DraftCount { get; set; }
    public int InProgressCount { get; set; }
    public int SubmissionSuccessCount { get; set; }
    public int SubmissionFailedCount { get; set; }

    public int FolderApprovedCount { get; set; }
    public int FolderPendingCount { get; set; }
    public int FolderRejectedCount { get; set; }
    public int CancelledCount { get; set; }

    public long ApprovedTotalAmount { get; set; }
    public long ApprovedTotalVat { get; set; }
    public long TotalRegisteredPayments { get; set; }
    public long TotalUnpaidCreditAmount { get; set; }

    public List<MdnInvoiceSummaryDto> RecentInvoices { get; set; } = new();
    public List<MdnSubmissionLogDto> RecentLogs { get; set; } = new();
}

// ==================== برچسب‌ها و رنگ‌های نمایشی فارسی ====================

public static class MdnUiLabels
{
    public static string EnvironmentTitle(MdnEnvironment v) => v switch
    {
        MdnEnvironment.Production => "عملیاتی (tp.tax.gov.ir)",
        MdnEnvironment.Sandbox => "آزمایشی / سندباکس (rc.tax.gov.ir)",
        _ => "نامشخص"
    };

    public static string SignatureModeTitle(MdnSignatureMode v) => v switch
    {
        MdnSignatureMode.Pkcs8Certificate => "کلید خصوصی + گواهی امضای الکترونیکی (PKCS#8 / X.509)",
        MdnSignatureMode.Pkcs11Token => "توکن سخت‌افزاری امضای الکترونیکی (PKCS#11)",
        _ => "نامشخص"
    };

    public static string TaxpayerTypeTitle(MdnTaxpayerType v) => v switch
    {
        MdnTaxpayerType.Natural => "حقیقی",
        MdnTaxpayerType.Legal => "حقوقی",
        MdnTaxpayerType.CivicPartnership => "مشارکت مدنی",
        MdnTaxpayerType.ForeignNational => "اتباع غیرایرانی",
        _ => "نامشخص"
    };

    public static string InvoiceTypeTitle(MdnInvoiceType v) => v switch
    {
        MdnInvoiceType.Type1 => "نوع اول (با اطلاعات خریدار)",
        MdnInvoiceType.Type2 => "نوع دوم (مصرف‌کننده نهایی)",
        _ => "نامشخص"
    };

    public static string PatternTitle(MdnInvoicePattern v) => v switch
    {
        MdnInvoicePattern.Sale => "الگوی ۱ — فروش",
        MdnInvoicePattern.CurrencySale => "الگوی ۲ — فروش ارزی",
        MdnInvoicePattern.GoldJewelry => "الگوی ۳ — طلا، جواهر و پلاتین",
        MdnInvoicePattern.Contracting => "الگوی ۴ — پیمانکاری",
        MdnInvoicePattern.UtilityBill => "الگوی ۵ — قبوض خدماتی",
        MdnInvoicePattern.AirTicket => "الگوی ۶ — بلیط هواپیما",
        MdnInvoicePattern.Export => "الگوی ۷ — صادرات",
        _ => "سایر"
    };

    public static string SubjectTitle(MdnInvoiceSubject v) => v switch
    {
        MdnInvoiceSubject.Original => "اصلی",
        MdnInvoiceSubject.Corrective => "اصلاحی",
        MdnInvoiceSubject.Cancellation => "ابطالی",
        MdnInvoiceSubject.SalesReturn => "برگشت از فروش",
        _ => "نامشخص"
    };

    public static string SettlementTitle(MdnSettlementMethod v) => v switch
    {
        MdnSettlementMethod.Cash => "نقدی",
        MdnSettlementMethod.Credit => "نسیه",
        MdnSettlementMethod.CashAndCredit => "نقدی / نسیه",
        _ => "نامشخص"
    };

    public static string SubmissionTitle(MdnSubmissionStatus v) => v switch
    {
        MdnSubmissionStatus.Draft => "پیش‌نویس",
        MdnSubmissionStatus.Sent => "ارسال‌شده",
        MdnSubmissionStatus.InProgress => "در حال بررسی (IN_PROGRESS)",
        MdnSubmissionStatus.Success => "ثبت موفق در درگاه",
        MdnSubmissionStatus.Failed => "رد شده (دارای خطا)",
        MdnSubmissionStatus.Timeout => "منقضی (TIMEOUT)",
        MdnSubmissionStatus.NotFound => "یافت نشد (NOT_FOUND)",
        _ => "نامشخص"
    };

    public static string SubmissionBadgeClass(MdnSubmissionStatus v) => v switch
    {
        MdnSubmissionStatus.Draft => "badge bg-secondary-subtle text-secondary border",
        MdnSubmissionStatus.Sent or MdnSubmissionStatus.InProgress => "badge bg-warning-subtle text-warning-emphasis border border-warning-subtle",
        MdnSubmissionStatus.Success => "badge bg-success-subtle text-success border border-success-subtle",
        MdnSubmissionStatus.Failed => "badge bg-danger-subtle text-danger border border-danger-subtle",
        _ => "badge bg-dark-subtle text-dark border"
    };

    public static string FolderTitle(MdnFolderStatus v) => v switch
    {
        MdnFolderStatus.Unknown => "استعلام‌نشده",
        MdnFolderStatus.Pending => "در انتظار واکنش خریدار",
        MdnFolderStatus.Approved => "تأیید شده در کارپوشه",
        MdnFolderStatus.SystemApproved => "تأیید سیستمی کارپوشه",
        MdnFolderStatus.Rejected => "رد شده در کارپوشه",
        MdnFolderStatus.Cancelled => "ابطال شده",
        MdnFolderStatus.NotFound => "ناموجود در کارپوشه",
        _ => "نامشخص"
    };

    public static string FolderBadgeClass(MdnFolderStatus v) => v switch
    {
        MdnFolderStatus.Approved or MdnFolderStatus.SystemApproved => "badge bg-success text-white",
        MdnFolderStatus.Pending => "badge bg-info-subtle text-info-emphasis border border-info-subtle",
        MdnFolderStatus.Rejected => "badge bg-danger text-white",
        MdnFolderStatus.Cancelled => "badge bg-dark text-white",
        _ => "badge bg-light text-muted border"
    };

    public static string PaymentStatusTitle(MdnPaymentStatus v) => v switch
    {
        MdnPaymentStatus.Unpaid => "پرداخت‌نشده",
        MdnPaymentStatus.PartiallyPaid => "تسویه جزئی",
        MdnPaymentStatus.Paid => "تسویه کامل",
        MdnPaymentStatus.NotApplicable => "غیرمشمول",
        _ => "نامشخص"
    };

    public static string PaymentStatusBadgeClass(MdnPaymentStatus v) => v switch
    {
        MdnPaymentStatus.Paid => "badge bg-success-subtle text-success border border-success-subtle",
        MdnPaymentStatus.PartiallyPaid => "badge bg-warning-subtle text-warning-emphasis border border-warning-subtle",
        MdnPaymentStatus.Unpaid => "badge bg-danger-subtle text-danger border border-danger-subtle",
        _ => "badge bg-secondary-subtle text-secondary border"
    };

    public static string PaymentMethodTitle(MdnPaymentMethod v) => v switch
    {
        MdnPaymentMethod.Cheque => "چک (CHEQUE)",
        MdnPaymentMethod.Barter => "تهاتر (BARTER)",
        MdnPaymentMethod.Cash => "وجه نقد (CASH)",
        MdnPaymentMethod.Pos => "دستگاه کارتخوان (POS)",
        MdnPaymentMethod.Internet => "درگاه اینترنتی (INTERNET)",
        MdnPaymentMethod.CardToCard => "کارت به کارت (CARD)",
        MdnPaymentMethod.BankTransfer => "انتقال بانکی (TRANSFER)",
        MdnPaymentMethod.Other => "سایر (OTHER)",
        _ => "سایر"
    };

    public static string PaymentSyncTitle(MdnPaymentSyncState v) => v switch
    {
        MdnPaymentSyncState.IncludedInInvoice => "ارسال‌شده در متن صورتحساب",
        MdnPaymentSyncState.LocalDraft => "ثبت محلی (آماده ارسال)",
        MdnPaymentSyncState.Registered => "ثبت‌شده در سامانه مودیان",
        MdnPaymentSyncState.InProgress => "در حال بررسی در سامانه",
        MdnPaymentSyncState.Failed => "خطا در ثبت سامانه مودیان",
        _ => "نامشخص"
    };

    public static string PaymentSyncBadgeClass(MdnPaymentSyncState v) => v switch
    {
        MdnPaymentSyncState.Registered or MdnPaymentSyncState.IncludedInInvoice => "badge bg-success-subtle text-success border border-success-subtle",
        MdnPaymentSyncState.InProgress or MdnPaymentSyncState.LocalDraft => "badge bg-warning-subtle text-warning-emphasis border border-warning-subtle",
        MdnPaymentSyncState.Failed => "badge bg-danger-subtle text-danger border border-danger-subtle",
        _ => "badge bg-light text-muted border"
    };
}
