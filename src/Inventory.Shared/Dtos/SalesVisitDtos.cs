using Inventory.Shared;

namespace Inventory.Shared.Dtos;

// =====================================================================
//  DTOهای ماژول ویزیتوری — مشترک بین API و کلاینت
// =====================================================================

/// <summary>سطر سفارش ویزیتور.</summary>
public class VisOrderLineDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>درصد تخفیف (در حالت ریالی، معادل محاسبه‌شده برای نمایش/سقف)</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>مبلغ تخفیف (ریال)</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>تخفیف به‌صورت مبلغ ریالی وارد شده است؟ (false = درصدی)</summary>
    public bool DiscountIsAmount { get; set; }

    public decimal LineTotal { get; set; }
    public string? Note { get; set; }

    /// <summary>نمایش تخفیف: ریالی یا درصدی — یک منبع برای همهٔ صفحه‌ها.</summary>
    public string DiscountText => DiscountIsAmount
        ? Fa.Money(DiscountAmount) + " ریال"
        : Fa.Digits(DiscountPercent) + "٪";
}

/// <summary>یک ردیف گردش سفارش (چه کسی، کِی، چرا).</summary>
public class VisOrderHistoryDto
{
    public int Id { get; set; }
    public VisOrderStatus? FromStatus { get; set; }
    public VisOrderStatus ToStatus { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public DateTime At { get; set; }
    public string? Comment { get; set; }
}

/// <summary>سفارش ویزیتور — سرسفارش + سطرها + گردش.</summary>
public class VisOrderDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";

    /// <summary>تاریخ ثبت سفارش</summary>
    public DateTime Date { get; set; }

    /// <summary>تاریخ درخواستیِ دریافت محصول (انتخاب ویزیتور) — هرگز بازنویسی نمی‌شود</summary>
    public DateTime? DeliveryDate { get; set; }

    /// <summary>تاریخ نهایی تحویل (تعیین مدیر فروش هنگام تأیید)</summary>
    public DateTime? FinalDeliveryDate { get; set; }

    public DateTime? FinalDateSetAt { get; set; }
    public string? FinalDateSetBy { get; set; }

    /// <summary>آیا مدیر تاریخ تحویل را نسبت به تاریخ ویزیتور تغییر داده است؟</summary>
    public bool FinalDateChanged => FinalDeliveryDate.HasValue && DeliveryDate.HasValue
                                    && FinalDeliveryDate.Value.Date != DeliveryDate.Value.Date;

    /// <summary>تاریخ مؤثر تحویل (نهایی مدیر، وگرنه تاریخ ویزیتور)</summary>
    public DateTime? EffectiveDeliveryDate => FinalDeliveryDate ?? DeliveryDate;

    public int ReferrerId { get; set; }
    public string ReferrerName { get; set; } = "";

    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyMobile { get; set; }

    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";

    public VisOrderStatus Status { get; set; }
    public PaymentMethod PaymentMethod { get; set; }

    public decimal TotalGross { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalNet { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>تعداد بار برگشت برای اصلاح — برای دیدن موارد پرمشکل در صف تأیید.</summary>
    public int ReturnCount { get; set; }

    /// <summary>آیا تخفیف از سقف مجاز ویزیتور عبور کرده است؟ (نیازمند تأیید ویژه)</summary>
    public bool DiscountOverLimit { get; set; }

    public int? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceStatus { get; set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// شناسهٔ «فروش فروشگاه» ثبت‌شده برای این سفارش (سند فروش در ماژول فروش).
    /// پس از تأیید مالی، سند فروش واقعی ساخته می‌شود و شناسه/شمارهٔ آن در
    /// همین دو فیلد نگه‌داری می‌شود.
    /// </summary>
    public int? SaleId => InvoiceId;

    /// <summary>شمارهٔ فروش ثبت‌شده در فهرست فروش فروشگاه.</summary>
    public string? SaleNumber => InvoiceNumber;

    /// <summary>عنوان فارسی روش پرداخت — همان روشی که ویزیتور انتخاب کرده است.</summary>
    public string PaymentMethodTitle => PaymentMethod switch
    {
        PaymentMethod.Cash => "نقدی",
        PaymentMethod.Credit => "نسیه",
        PaymentMethod.Cheque => "چک",
        PaymentMethod.Installment => "اقساطی",
        _ => "—"
    };

    public List<VisOrderLineDto> Lines { get; set; } = new();
    public List<VisOrderHistoryDto> History { get; set; } = new();

    /// <summary>
    /// اقلامی که موجودی انبار سفارش برایشان کافی نیست (فقط در صف تأیید و کارتابل مالی پر می‌شود).
    /// در ثبت فروش، موجودی باید تأمین شده باشد؛ همین فهرست پیش از زدن دکمه به کاربر نشان داده می‌شود.
    /// </summary>
    public List<VisStockWarning> StockWarnings { get; set; } = new();

    public int LineCount => Lines.Count;
    public decimal TotalQuantity => Lines.Sum(l => l.Quantity);
}

/// <summary>ثبت/ویرایش پیش‌نویس سفارش.</summary>
public class VisOrderCommand
{
    public int Id { get; set; }
    public DateTime Date { get; set; }

    /// <summary>تاریخ دریافت محصول (شمسی در فرم، میلادی در API). پیش‌فرض: امروز.</summary>
    public DateTime? DeliveryDate { get; set; }
    public int PartyId { get; set; }
    public int WarehouseId { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? Note { get; set; }
    public List<VisOrderLineDto> Lines { get; set; } = new();

    /// <summary>پس از ذخیره، بلافاصله برای تأیید ارسال شود.</summary>
    public bool SubmitAfterSave { get; set; }
}

/// <summary>تصمیم مدیر روی سفارش (تأیید/برگشت/رد/ابطال).</summary>
public class VisOrderDecision
{
    public int Id { get; set; }
    public string? Comment { get; set; }

    /// <summary>
    /// تاریخ نهاییِ تحویل که مدیر در تأیید تعیین می‌کند.
    /// خالی = پذیرش تاریخ ویزیتور (همان مقدار در تاریخ نهایی ثبت می‌شود).
    /// </summary>
    public DateTime? FinalDeliveryDate { get; set; }
}

/// <summary>
/// فرمان «تأیید مالی» سفارش ویزیتور.
/// مالی در این گام روش پرداخت را نهایی می‌کند: نقدی → سند حسابداری صندوق/بانک،
/// چک → ثبت چک‌ها، اقساط → ساخت دفترچهٔ اقساط؛ سپس «فروش فروشگاه» ثبت می‌شود.
/// </summary>
public class VisFinanceCommand
{
    public int Id { get; set; }

    /// <summary>روش پرداخت نهایی — پیش‌فرض همان انتخاب ویزیتور است.</summary>
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    /// <summary>نوع دریافت نقدی (وجه نقد / کارت‌خوان / کارت به کارت)</summary>
    public CashType? CashType { get; set; }

    /// <summary>حساب خزانه (صندوق/بانک) برای تسویه</summary>
    public int? TrsAccountId { get; set; }

    /// <summary>پیش‌دریافت نقدی در پرداخت ترکیبی (نسیه/چک/اقساط)</summary>
    public decimal CashAmount { get; set; }

    /// <summary>سررسید نسیه</summary>
    public DateTime? DueDate { get; set; }

    public List<ChequeDto> Cheques { get; set; } = new();
    public List<InstallmentDto> Installments { get; set; } = new();

    /// <summary>یادداشت مالی — در گردش سفارش ثبت می‌شود.</summary>
    public string? Comment { get; set; }
}

/// <summary>هویت و توان‌های کاربر جاری در ماژول ویزیتوری (سمت سرور محاسبه می‌شود).</summary>
public class VisActorInfo
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public int? ReferrerId { get; set; }
    public string ReferrerName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool CanCreate { get; set; }
    public bool CanApprove { get; set; }
    public bool CanFinance { get; set; }
    public decimal MaxDiscountPercent { get; set; }

    /// <summary>شرکت مؤثر کاربر — مشتری، انبار و کالا بر اساس همین شرکت دیده می‌شوند.</summary>
    public int? CompanyId { get; set; }

    /// <summary>نام شرکت مؤثر (برای نمایش در فرم سفارش).</summary>
    public string CompanyName { get; set; } = "";

    /// <summary>تعداد انبارهای فعالِ شرکتِ کاربر (۰ = شرکت انبار ندارد).</summary>
    public int WarehouseCount { get; set; }

    /// <summary>وضعیت دیدن کالا برای همین کاربر (برای پیام راهنمای فرم).</summary>
    public int ProductCount { get; set; }

    /// <summary>انبار پیش‌فرض برای فرم سفارش (اولین انبار فعالِ همان شرکت).</summary>
    public int DefaultWarehouseId { get; set; }
}

/// <summary>مشتری سبک برای کمبوی فرم سفارش.</summary>
public class VisCustomerLite
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Address { get; set; }

    /// <summary>مانده حساب (بدهکار) — برای نمایش اعتبار در فرم سفارش.</summary>
    public decimal Balance { get; set; }

    /// <summary>نام ویزیتورِ مسئول مشتری — برای شفافیت در فهرست شرکت.</summary>
    public string? ReferrerName { get; set; }

    /// <summary>این مشتری در پروندهٔ همین کاربر است (اول فهرست می‌آید).</summary>
    public bool OwnedByMe { get; set; }

    /// <summary>شناسهٔ ویزیتور مسئول — استفادهٔ داخلی سرور.</summary>
    public int? OwnerRefId { get; set; }
}

/// <summary>کالای سبک برای جست‌وجوی سطرهای سفارش.</summary>
public class VisProductLite
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public decimal SalePrice { get; set; }
    public decimal Stock { get; set; }
    public bool IsService { get; set; }
}

/// <summary>انبار سبک.</summary>
public class VisWarehouseLite
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>هشدار کسری موجودی یک سطر.</summary>
public class VisStockWarning
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Required { get; set; }
    public decimal Available { get; set; }
    public decimal Shortage => Required - Available;
}

/// <summary>ردیف صف تأیید مدیر — همراه هشدارها.</summary>
public class VisApprovalItem
{
    public VisOrderDto Order { get; set; } = new();
    public List<VisStockWarning> StockWarnings { get; set; } = new();

    /// <summary>مشتری از سقف اعتبار عبور کرده است (اگر سقف تعریف شده باشد).</summary>
    public bool CreditOverLimit { get; set; }
    public decimal CustomerBalance { get; set; }
    public decimal? CreditLimit { get; set; }
}

/// <summary>آمار کارتابلی/داشبوردی ماژول.</summary>
public class VisQueueStats
{
    public int Draft { get; set; }
    public int Submitted { get; set; }
    public int Approved { get; set; }
    public int Returned { get; set; }
    public int Invoiced { get; set; }
    public int Rejected { get; set; }
    public int Canceled { get; set; }

    public decimal ApprovedAmount { get; set; }
    public decimal InvoicedAmount { get; set; }

    /// <summary>پورسانت تخمینی سفارش‌های تأییدشده (فقط برای نمایش به ویزیتور).</summary>
    public decimal CommissionEstimate { get; set; }
}

// =====================================================================
//  کارتابل یکپارچه — قرارداد مشترک
// =====================================================================

/// <summary>اولویت آیتم کارتابل.</summary>
public enum CartablePriority
{
    Normal = 0,
    High = 1,
    Urgent = 2
}

/// <summary>
/// یک آیتم کارتابل — مستقل از ماژول سازنده.
/// <para>
/// <see cref="Actionable"/> تفاوت «منتظر اقدام من» و «اطلاع» را مشخص می‌کند؛
/// کلاینت بر همین مبنا آیتم‌ها را در تب‌های جدا نشان می‌دهد.
/// </para>
/// </summary>
public class CartableItemDto
{
    /// <summary>کلید ماژول سازنده: sales-visit، itrequests، projects …</summary>
    public string Source { get; set; } = "";

    /// <summary>نوع رخداد درون ماژول: submitted، returned، finance-pending …</summary>
    public string Kind { get; set; } = "";

    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string? Link { get; set; }
    public string Icon { get; set; } = "bi-lightning";
    public string Color { get; set; } = "#2563eb";
    public CartablePriority Priority { get; set; }
    public DateTime? DueAt { get; set; }

    /// <summary>آیا منتظر اقدام کاربر است؟ (اگر نه: فقط اطلاع)</summary>
    public bool Actionable { get; set; } = true;

    /// <summary>برای تجمیع: «۴ سفارش منتظر تأیید» به‌جای ۴ آیتم هم‌شکل.</summary>
    public int? BadgeCount { get; set; }
}

/// <summary>پاسخ واحد کارتابل کاربر جاری.</summary>
public class CartableResponseDto
{
    public List<CartableItemDto> Items { get; set; } = new();
    public int ActionableCount => Items.Count(i => i.Actionable);
    public int InfoCount => Items.Count(i => !i.Actionable);
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}
