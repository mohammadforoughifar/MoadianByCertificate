using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Entities.Common;

namespace MoudianMiddleware.DataLayer.Entities;

/// <summary>
/// سربرگ صورتحساب الکترونیک (Header) همراه با جمع‌های محاسبه‌شده و وضعیت ارسال.
/// </summary>
/// <remarks>
/// <para>
/// مبالغ ریالی از نوع <see cref="long"/> (ریال، بدون اعشار) هستند؛ نرخ‌ها و مقادیر کسری
/// در اقلام به‌صورت <see cref="decimal"/> ذخیره می‌شوند.
/// </para>
/// <para>
/// اطلاعات خریدار به‌صورت snapshot در همین رکورد نگه‌داری می‌شود تا سند ارسال‌شده تغییرناپذیر بماند.
/// </para>
/// </remarks>
public class Invoice : AuditableEntity, ITenantEntity
{
    /// <inheritdoc />
    public int TenantId { get; set; }

    /// <summary>شماره‌ی داخلی صورتحساب (یکتا در هر مودی، قابل نمایش به کاربر).</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>شناسه‌ی فاکتور در سیستم خارجی (ERP/فروش) برای جلوگیری از ثبت تکراری.</summary>
    public string? ExternalId { get; set; }

    /// <summary>نوع صورتحساب (اصلی/اصلاحی/ابطالی/برگشت از فروش).</summary>
    public InvoiceType Type { get; set; } = InvoiceType.Original;

    /// <summary>الگوی صورتحساب (فیلد <c>inp</c>).</summary>
    public int PatternCode { get; set; }

    /// <summary>موضوع صورتحساب (فیلد <c>ins</c>).</summary>
    public int SubjectCode { get; set; }

    /// <summary>روش تسویه (فیلد <c>setm</c>).</summary>
    public SettlementMethod SettlementMethod { get; set; } = SettlementMethod.Cash;

    /// <summary>وضعیت فعلی در چرخه‌ی ارسال.</summary>
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    /// <summary>تاریخ و زمان صدور (UTC) — مبنای فیلد <c>indatim</c>.</summary>
    public DateTimeOffset IssueDate { get; set; }

    /// <summary>شناسه‌ی مشتری ثبت‌شده (اختیاری؛ اطلاعات خریدار در فیلدهای زیر snapshot شده‌اند).</summary>
    public int? CustomerId { get; set; }

    /// <summary>نام خریدار (snapshot).</summary>
    public string BuyerName { get; set; } = string.Empty;

    /// <summary>نوع شخص خریدار (فیلد <c>tob</c>).</summary>
    public BuyerType BuyerType { get; set; } = BuyerType.NaturalPerson;

    /// <summary>کد ملی/شناسه‌ی ملی خریدار (فیلد <c>bid</c>).</summary>
    public string? BuyerNationalId { get; set; }

    /// <summary>شماره‌ی اقتصادی خریدار (فیلد <c>tinb</c>).</summary>
    public string? BuyerEconomicCode { get; set; }

    /// <summary>کد پستی خریدار (فیلد <c>bpc</c>).</summary>
    public string? BuyerPostalCode { get; set; }

    /// <summary>نشانی خریدار (snapshot).</summary>
    public string? BuyerAddress { get; set; }

    /// <summary>کد شعبه‌ی خریدار (فیلد <c>bbc</c>).</summary>
    public string? BuyerBranchCode { get; set; }

    /// <summary>شماره‌ی منحصربه‌فرد مالیاتی (فیلد <c>taxid</c>، ۲۲ کاراکتر)؛ پس از آماده‌سازی صادر می‌شود.</summary>
    public string? TaxId { get; set; }

    /// <summary>سریال صورتحساب (فیلد <c>inno</c>، hex ده‌رقمی).</summary>
    public string? Serial { get; set; }

    /// <summary>شماره‌ی مالیاتی صورتحساب مرجع (فیلد <c>irtaxid</c>) برای اصلاحی/ابطالی/برگشتی.</summary>
    public string? ReferenceTaxId { get; set; }

    /// <summary>شناسه‌ی درخواست (UID) تولیدشده هنگام ارسال به سامانه.</summary>
    public string? Uid { get; set; }

    /// <summary>شماره‌ی پیگیری (ReferenceNumber) دریافتی از سامانه.</summary>
    public string? ReferenceNumber { get; set; }

    /// <summary>مجموع مبلغ قبل از کسر تخفیف (فیلد <c>tprdis</c>).</summary>
    public long TotalBeforeDiscount { get; set; }

    /// <summary>مجموع تخفیفات (فیلد <c>tdis</c>).</summary>
    public long TotalDiscount { get; set; }

    /// <summary>مجموع مبلغ پس از کسر تخفیف (فیلد <c>tadis</c>).</summary>
    public long TotalAfterDiscount { get; set; }

    /// <summary>مجموع مالیات بر ارزش افزوده (فیلد <c>tvam</c>).</summary>
    public long TotalVat { get; set; }

    /// <summary>مجموع سایر مالیات، عوارض و وجوه قانونی (فیلد <c>todam</c>).</summary>
    public long TotalOtherTaxes { get; set; }

    /// <summary>مجموع صورتحساب (فیلد <c>tbill</c>).</summary>
    public long TotalAmount { get; set; }

    /// <summary>مبلغ پرداختی نقدی (فیلد <c>cap</c>).</summary>
    public long CashPayment { get; set; }

    /// <summary>مبلغ پرداختی نسیه (فیلد <c>insp</c>).</summary>
    public long CreditPayment { get; set; }

    /// <summary>مجموع سهم مالیات بر ارزش افزوده از پرداخت (فیلد <c>tvop</c>).</summary>
    public long VatShareFromPayment { get; set; }

    /// <summary>مالیات موضوع ماده ۱۷ (فیلد <c>tax17</c>).</summary>
    public long Tax17 { get; set; }

    /// <summary>زمان ارسال موفق به سامانه.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>زمان تأیید نهایی در کارپوشه.</summary>
    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>کد آخرین خطای دریافتی از سامانه.</summary>
    public string? LastErrorCode { get; set; }

    /// <summary>پیام آخرین خطای دریافتی.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>یادداشت داخلی.</summary>
    public string? Notes { get; set; }

    /// <summary>مشتری مربوطه (اختیاری).</summary>
    public Customer? Customer { get; set; }

    /// <summary>اقلام صورتحساب.</summary>
    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();

    /// <summary>پرداخت‌های صورتحساب.</summary>
    public ICollection<InvoicePayment> Payments { get; set; } = new List<InvoicePayment>();

    /// <summary>تاریخچه‌ی تلاش‌های ارسال/استعلام.</summary>
    public ICollection<InvoiceSubmission> Submissions { get; set; } = new List<InvoiceSubmission>();
}
