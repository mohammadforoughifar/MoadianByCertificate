using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.Enums;

namespace MoudianMiddleware.Contracts.ViewModels.Invoices;

/// <summary>ورودی ثبت پیش‌نویس صورتحساب.</summary>
public sealed class InvoiceWriteRequest
{
    /// <summary>شماره‌ی داخلی صورتحساب.</summary>
    public string InvoiceNumber { get; init; } = string.Empty;
    /// <summary>شناسه‌ی خارجی برای idempotency؛ در صورت تکرار همان نتیجه بازگردانده می‌شود.</summary>
    public string? ExternalId { get; init; }
    /// <summary>نوع صورتحساب.</summary>
    public InvoiceType Type { get; init; } = InvoiceType.Original;
    /// <summary>کد الگوی صورتحساب.</summary>
    public int PatternCode { get; init; }
    /// <summary>کد موضوع صورتحساب.</summary>
    public int SubjectCode { get; init; }
    /// <summary>روش تسویه.</summary>
    public SettlementMethod SettlementMethod { get; init; } = SettlementMethod.Cash;
    /// <summary>زمان صدور با منطقه‌ی زمانی مشخص.</summary>
    public DateTimeOffset IssueDate { get; init; }
    /// <summary>شناسه‌ی مشتری اختیاری؛ مشخصات خریدار در صورتحساب snapshot می‌شود.</summary>
    public int? CustomerId { get; init; }
    /// <summary>نام خریدار.</summary>
    public string BuyerName { get; init; } = string.Empty;
    /// <summary>نوع شخص خریدار.</summary>
    public BuyerType BuyerType { get; init; } = BuyerType.NaturalPerson;
    /// <summary>شناسه‌ی ملی خریدار.</summary>
    public string? BuyerNationalId { get; init; }
    /// <summary>شماره‌ی اقتصادی خریدار.</summary>
    public string? BuyerEconomicCode { get; init; }
    /// <summary>کد پستی خریدار.</summary>
    public string? BuyerPostalCode { get; init; }
    /// <summary>نشانی خریدار.</summary>
    public string? BuyerAddress { get; init; }
    /// <summary>کد شعبه‌ی خریدار.</summary>
    public string? BuyerBranchCode { get; init; }
    /// <summary>شناسه‌ی مالیاتی مرجع برای صورتحساب اصلاحی/ابطالی/برگشتی.</summary>
    public string? ReferenceTaxId { get; init; }
    /// <summary>اقلام صورتحساب.</summary>
    public IReadOnlyList<InvoiceItemWriteRequest> Items { get; init; } = Array.Empty<InvoiceItemWriteRequest>();
    /// <summary>ردیف‌های پرداخت.</summary>
    public IReadOnlyList<InvoicePaymentWriteRequest> Payments { get; init; } = Array.Empty<InvoicePaymentWriteRequest>();
}

/// <summary>ورودی یک قلم صورتحساب؛ جمع‌ها در سرور محاسبه می‌شوند.</summary>
public sealed class InvoiceItemWriteRequest
{
    /// <summary>شناسه‌ی کالا/خدمت.</summary>
    public string Sstid { get; init; } = string.Empty;
    /// <summary>شرح کالا/خدمت.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>کد واحد اندازه‌گیری.</summary>
    public string MeasuringUnitCode { get; init; } = string.Empty;
    /// <summary>تعداد/مقدار.</summary>
    public decimal Quantity { get; init; }
    /// <summary>مبلغ واحد به ریال.</summary>
    public decimal UnitPrice { get; init; }
    /// <summary>تخفیف به ریال؛ جمع تخفیف از مبلغ ناخالص بیشتر نمی‌شود.</summary>
    public long DiscountAmount { get; init; }
    /// <summary>نرخ VAT درصدی.</summary>
    public decimal VatRate { get; init; }
    /// <summary>موضوع سایر مالیات/عوارض.</summary>
    public string? OtherTaxSubject { get; init; }
    /// <summary>نرخ سایر مالیات/عوارض.</summary>
    public decimal? OtherTaxRate { get; init; }
    /// <summary>موضوع سایر وجوه قانونی.</summary>
    public string? OtherLegalFeeSubject { get; init; }
    /// <summary>نرخ سایر وجوه قانونی.</summary>
    public decimal? OtherLegalFeeRate { get; init; }
    /// <summary>اجرت ساخت به ریال.</summary>
    public long? ConstructionFee { get; init; }
    /// <summary>سود فروشنده به ریال.</summary>
    public long? SellerProfit { get; init; }
    /// <summary>حق‌العمل به ریال.</summary>
    public long? BrokerFee { get; init; }
    /// <summary>وزن خالص اختیاری.</summary>
    public decimal? NetWeight { get; init; }
    /// <summary>کد HS گمرکی.</summary>
    public string? HsCode { get; init; }
}

/// <summary>ورودی یک پرداخت صورتحساب.</summary>
public sealed class InvoicePaymentWriteRequest
{
    /// <summary>روش پرداخت؛ مقدار عددی pmt تا تأیید جدول رسمی فقط از lookup قابل‌اعتماد تلقی می‌شود.</summary>
    public PaymentMethod Method { get; init; }
    /// <summary>مبلغ پرداخت به ریال.</summary>
    public long Amount { get; init; }
    /// <summary>زمان پرداخت اختیاری.</summary>
    public DateTimeOffset? PaymentDate { get; init; }
    /// <summary>شماره‌ی پیگیری.</summary>
    public string? ReferenceNumber { get; init; }
    /// <summary>شماره‌ی پایانه.</summary>
    public string? TerminalNumber { get; init; }
    /// <summary>شماره‌ی پذیرنده.</summary>
    public string? AcceptorNumber { get; init; }
    /// <summary>شماره‌ی سوییچ.</summary>
    public string? SwitchNumber { get; init; }
    /// <summary>شناسه‌ی پرداخت‌کننده.</summary>
    public string? PayerNationalId { get; init; }
    /// <summary>شماره‌ی کارت ماسک‌شده.</summary>
    public string? MaskedCardNumber { get; init; }
    /// <summary>شناسه‌ی پرداخت بورس.</summary>
    public string? VatPaymentId { get; init; }
    /// <summary>شناسه‌ی قبض بورس.</summary>
    public string? VatBillId { get; init; }
}

/// <summary>فیلتر فهرست صورتحساب‌ها.</summary>
public sealed class InvoiceListQuery : PaginationQuery
{
    /// <summary>فیلتر اختیاری وضعیت.</summary>
    public InvoiceStatus? Status { get; init; }
    /// <summary>فیلتر اختیاری نوع.</summary>
    public InvoiceType? Type { get; init; }
    /// <summary>شروع بازه‌ی صدور.</summary>
    public DateTimeOffset? FromDate { get; init; }
    /// <summary>پایان بازه‌ی صدور.</summary>
    public DateTimeOffset? ToDate { get; init; }
    /// <summary>فیلتر اختیاری مشتری.</summary>
    public int? CustomerId { get; init; }
}

/// <summary>نمایش خلاصه‌ی صورتحساب و وضعیت idempotency.</summary>
public sealed class InvoiceViewModel
{
    /// <summary>شناسه‌ی پایگاه داده.</summary>
    public int Id { get; init; }
    /// <summary>شناسه‌ی مودی.</summary>
    public int TenantId { get; init; }
    /// <summary>شماره‌ی داخلی.</summary>
    public string InvoiceNumber { get; init; } = string.Empty;
    /// <summary>شناسه‌ی خارجی.</summary>
    public string? ExternalId { get; init; }
    /// <summary>شماره‌ی مالیاتی سامانه.</summary>
    public string? TaxId { get; init; }
    /// <summary>شناسه‌ی پیگیری سامانه.</summary>
    public string? Uid { get; init; }
    /// <summary>وضعیت فعلی.</summary>
    public InvoiceStatus Status { get; init; }
    /// <summary>نام خریدار snapshot شده.</summary>
    public string BuyerName { get; init; } = string.Empty;
    /// <summary>زمان صدور.</summary>
    public DateTimeOffset IssueDate { get; init; }
    /// <summary>نوع صورتحساب.</summary>
    public InvoiceType Type { get; init; }
    /// <summary>کد الگو.</summary>
    public int PatternCode { get; init; }
    /// <summary>کد موضوع.</summary>
    public int SubjectCode { get; init; }
    /// <summary>اقلام جهت نمایش و ویرایش پیش‌نویس.</summary>
    public IReadOnlyList<InvoiceItemViewModel> Items { get; init; } = Array.Empty<InvoiceItemViewModel>();
    /// <summary>آخرین کد خطای ثبت‌شده با پیام امن.</summary>
    public string? LastErrorCode { get; init; }
    /// <summary>آخرین پیام خطای سامانه، پس از scrub.</summary>
    public string? LastErrorMessage { get; init; }
    /// <summary>تاریخچه metadata-only ارسال‌ها و استعلام‌ها.</summary>
    public IReadOnlyList<InvoiceSubmissionViewModel> Submissions { get; init; } = Array.Empty<InvoiceSubmissionViewModel>();
    /// <summary>جمع ناخالص قبل از تخفیف.</summary>
    public long TotalBeforeDiscount { get; init; }
    /// <summary>جمع تخفیف.</summary>
    public long TotalDiscount { get; init; }
    /// <summary>جمع پس از تخفیف.</summary>
    public long TotalAfterDiscount { get; init; }
    /// <summary>جمع مالیات بر ارزش افزوده.</summary>
    public long TotalVat { get; init; }
    /// <summary>جمع سایر وجوه.</summary>
    public long TotalOtherTaxes { get; init; }
    /// <summary>مبلغ نهایی.</summary>
    public long TotalAmount { get; init; }
    /// <summary>آیا نتیجه‌ی قبلی به‌صورت idempotent بازگردانده شد؟</summary>
    public bool IsIdempotentReplay { get; init; }
}

/// <summary>یک قلم قابل‌نمایش در جزئیات صورتحساب.</summary>
public sealed class InvoiceItemViewModel
{
    /// <summary>شناسه کالا/خدمت.</summary>
    public string Sstid { get; init; } = string.Empty;
    /// <summary>شرح قلم.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>کد واحد.</summary>
    public string MeasuringUnitCode { get; init; } = string.Empty;
    /// <summary>تعداد.</summary>
    public decimal Quantity { get; init; }
    /// <summary>قیمت واحد ریالی.</summary>
    public decimal UnitPrice { get; init; }
    /// <summary>تخفیف ریالی.</summary>
    public long DiscountAmount { get; init; }
    /// <summary>نرخ مالیات درصدی.</summary>
    public decimal VatRate { get; init; }
}

/// <summary>خلاصه‌ی امن تلاش ارسال/استعلام بدون بدنه‌ی خام SDK.</summary>
public sealed class InvoiceSubmissionViewModel
{
    /// <summary>شماره تلاش.</summary>
    public int AttemptNumber { get; init; }
    /// <summary>نوع اقدام.</summary>
    public SubmissionAction Action { get; init; }
    /// <summary>نتیجه اقدام.</summary>
    public SubmissionStatus Status { get; init; }
    /// <summary>وضعیت گزارش‌شده توسط سامانه.</summary>
    public string? MoadianStatus { get; init; }
    /// <summary>کد خطای امن.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>پیام خطای scrub شده.</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>تعداد هشدارها.</summary>
    public int WarningCount { get; init; }
    /// <summary>زمان آغاز.</summary>
    public DateTimeOffset StartedAt { get; init; }
    /// <summary>زمان پایان.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
    /// <summary>مدت اجرا میلی‌ثانیه.</summary>
    public int? DurationMs { get; init; }
}
