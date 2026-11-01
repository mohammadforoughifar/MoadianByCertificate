using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>
/// سفارش ویزیتور (بازاریاب میدانی) — پیش‌سند فروش.
/// <para>
/// این سند <b>هیچ اثری</b> روی موجودی، حساب طرف‌حساب و پورسانت ندارد. فقط پس از
/// تأیید مدیر به پیش‌نویس فاکتور فروش تبدیل می‌شود و اثر واقعی با قطعی‌سازی همان
/// فاکتور (سند انبار + سند حسابداری) اعمال می‌گردد. چرخهٔ وضعیت در
/// <see cref="VisOrderStatus"/> تعریف شده است.
/// </para>
/// </summary>
public class VisOrder : ICompanyOwned
{
    public int Id { get; set; }

    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }

    /// <summary>شماره سفارش — الگوی <c>VS-1405-0001</c> (سال شمسی + شمارهٔ متوالی)</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    public DateTime Date { get; set; }

    // ---------- تاریخ تحویل (دو تاریخ مستقل) ----------

    /// <summary>
    /// تاریخ درخواستیِ دریافت محصول — همان چیزی که ویزیتور در فرم انتخاب می‌کند.
    /// <para>
    /// این مقدار <b>هرگز</b> با تصمیم مدیر بازنویسی نمی‌شود؛ تاریخ مدیر در
    /// <see cref="FinalDeliveryDate"/> نگه‌داری می‌شود تا هر دو برای پیگیری باقی بمانند.
    /// </para>
    /// </summary>
    public DateTime? DeliveryDate { get; set; }

    /// <summary>تاریخ نهایی تحویل — مدیر فروش هنگام تأیید تعیین یا اصلاح می‌کند.</summary>
    public DateTime? FinalDeliveryDate { get; set; }

    /// <summary>چه زمانی تاریخ نهایی تعیین شد</summary>
    public DateTime? FinalDateSetAt { get; set; }

    /// <summary>چه کسی تاریخ نهایی را تعیین/اصلاح کرد</summary>
    [MaxLength(150)]
    public string? FinalDateSetBy { get; set; }

    /// <summary>تاریخ نهایی تغییریافته نسبت به تاریخ ویزیتور است؟ (برای نمایش تفاوت در گزارش‌ها)</summary>
    public bool FinalDateChanged => FinalDeliveryDate.HasValue && DeliveryDate.HasValue
                                    && FinalDeliveryDate.Value.Date != DeliveryDate.Value.Date;

    /// <summary>تاریخ مؤثر تحویل: اگر مدیر تعیین کرده باشد همان، وگرنه تاریخ ویزیتور.</summary>
    public DateTime? EffectiveDeliveryDate => FinalDeliveryDate ?? DeliveryDate;

    // ---------- ویزیتور ----------

    /// <summary>ویزیتور ثبت‌کننده (معرف). مالکیت داده و پورسانت بر همین مبناست.</summary>
    public int ReferrerId { get; set; }

    /// <summary>نام ویزیتور در لحظهٔ ثبت — snapshot</summary>
    [MaxLength(150)]
    public string ReferrerName { get; set; } = "";

    // ---------- مشتری و انبار ----------

    public int PartyId { get; set; }

    /// <summary>نام مشتری در لحظهٔ ثبت — snapshot تا تغییر نام، سابقه را عوض نکند</summary>
    [MaxLength(200)]
    public string PartyName { get; set; } = "";

    public int WarehouseId { get; set; }

    [MaxLength(150)]
    public string WarehouseName { get; set; } = "";

    // ---------- وضعیت و مبالغ ----------

    public VisOrderStatus Status { get; set; } = VisOrderStatus.Draft;

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    /// <summary>جمع سطرها قبل از تخفیف</summary>
    public decimal TotalGross { get; set; }

    /// <summary>جمع تخفیف سطرها</summary>
    public decimal TotalDiscount { get; set; }

    /// <summary>مبلغ خالص قابل پرداخت (بعد از تخفیف)</summary>
    public decimal TotalNet { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>آیا تخفیف ثبت‌شده از سقف مجاز ویزیتور عبور کرده است؟</summary>
    public bool DiscountOverLimit { get; set; }

    // ---------- گردش و تصمیم ----------

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>نام تصمیم‌گیرنده (مدیر فروش)</summary>
    [MaxLength(150)]
    public string? DecidedBy { get; set; }

    /// <summary>دلیل تصمیم (برای برگشت/رد اجباری است)</summary>
    [MaxLength(500)]
    public string? DecisionNote { get; set; }

    /// <summary>چند بار برای اصلاح برگشت خورده است</summary>
    public int ReturnCount { get; set; }

    /// <summary>اعتبار سفارش ارسال‌شده؛ پس از آن خودکار منقضی می‌شود (اختیاری).</summary>
    public DateTime? ExpiresAt { get; set; }

    // ---------- اتصال به گام مالی ----------

    /// <summary>شناسهٔ فاکتور (پیش‌نویس/قطعی) که از این سفارش ساخته شده است.</summary>
    public int? InvoiceId { get; set; }

    /// <summary>شمارهٔ فاکتور — snapshot برای نمایش سریع در فهرست.</summary>
    [MaxLength(30)]
    public string? InvoiceNumber { get; set; }

    public List<VisOrderLine> Lines { get; set; } = new();
    public List<VisOrderHistory> History { get; set; } = new();

    /// <summary>جمع مقدار سطرها — نمایشی</summary>
    public decimal TotalQuantity => Lines.Sum(l => l.Quantity);
}

/// <summary>سطر سفارش ویزیتور: کالا + مقدار + قیمت/تخفیف پیشنهادی.</summary>
public class VisOrderLine : ICompanyOwned
{
    public int Id { get; set; }

    public int? CompanyId { get; set; }

    public int OrderId { get; set; }

    public int RowNo { get; set; }

    public int ProductId { get; set; }

    /// <summary>کد و نام کالا در لحظهٔ ثبت — snapshot (حذف/تغییر نام کالا سابقه را نمی‌شکند)</summary>
    [MaxLength(50)]
    public string ProductCode { get; set; } = "";

    [MaxLength(200)]
    public string ProductName { get; set; } = "";

    [MaxLength(50)]
    public string? Unit { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }

    /// <summary>مبلغ تخفیف محاسبه‌شده (ریال) — برای عدم بازمحاسبه در گزارش‌های بعدی</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// نحوهٔ ورود تخفیف: <c>false</c> = درصدی (۰ تا ۹۹)، <c>true</c> = مبلغ ریالی.
    /// <para>
    /// قاعدهٔ فرم: عدد تا ۹۹ درصد و از ۱۰۰ به بالا مبلغ ریالی است. این پرچم ثبت می‌کند
    /// کاربر کدام حالت را وارد کرده تا در ویرایش، همان حالت به او نشان داده شود.
    /// </para>
    /// </summary>
    public bool DiscountIsAmount { get; set; }

    /// <summary>مبلغ سطر پس از تخفیف</summary>
    public decimal LineTotal { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }
}

/// <summary>
/// گردش سفارش ویزیتور — «چه کسی، کِی، چه تغییری، چرا».
/// عمداً بدون FK سخت به کاربر تا حذف کاربر سابقه را نشکند.
/// </summary>
public class VisOrderHistory : ICompanyOwned
{
    public int Id { get; set; }

    public int? CompanyId { get; set; }

    public int OrderId { get; set; }

    public VisOrderStatus? FromStatus { get; set; }

    public VisOrderStatus ToStatus { get; set; }

    public int UserId { get; set; }

    [MaxLength(150)]
    public string UserName { get; set; } = "";

    public DateTime At { get; set; } = DateTime.Now;

    [MaxLength(500)]
    public string? Comment { get; set; }
}

/// <summary>
/// اتصال ویزیتور ↔ مشتری (پروندهٔ مشتری).
/// <para>
/// اگر برای مشتری رکوردی اینجا نباشد، از <see cref="Party.ReferrerId"/> موجود
/// ارث می‌برد؛ بنابراین داده‌های فعلی بدون مهاجرت کار می‌کنند.
/// </para>
/// </summary>
public class VisCustomerLink : ICompanyOwned
{
    public int Id { get; set; }

    public int? CompanyId { get; set; }

    public int ReferrerId { get; set; }

    public int PartyId { get; set; }

    /// <summary>سقف اعتبار اختصاصی این ویزیتور برای این مشتری (اختیاری)</summary>
    public decimal? CreditLimit { get; set; }

    /// <summary>سقف درصد تخفیف مجاز روی این مشتری (اختیاری — بالاتر از سقف ویزیتور ملاک است)</summary>
    public decimal? MaxDiscountPercent { get; set; }

    /// <summary>دورهٔ سرکشی (روز) — برای یادآور کارتابل ویزیتور</summary>
    public int? VisitCycleDays { get; set; }

    /// <summary>آخرین سرکشی/سفارش ثبت‌شده برای این مشتری</summary>
    public DateTime? LastOrderAt { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
