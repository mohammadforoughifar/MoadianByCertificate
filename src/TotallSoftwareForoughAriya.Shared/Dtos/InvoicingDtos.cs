namespace Inventory.Shared.Dtos;

// =====================================================================
// DTO های ماژول فاکتور (خرید / فروش / برگشتی‌ها)
//   ۱) فاکتور و اقلام آن        ۲) قواعد اتصال به انبار و حسابداری
//   ۳) گزارش فروش و خرید
// همه‌ی نام‌ها با پیشوند Fac تا با DTOهای انبار (Inv) و حسابداری (Acc) تداخل نکنند.
// =====================================================================

// ============================ ۱) اقلام فاکتور ============================

/// <summary>یک سطر (قلم) فاکتور</summary>
public class FacInvoiceLine
{
    public int Id { get; set; }
    public int RowNo { get; set; }

    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>کد مالیاتی کالا (اسنپ‌شات در زمان صدور — برای سامانه مودیان)</summary>
    public string? TaxCode { get; set; }

    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }

    /// <summary>درصد تخفیف سطر (اگر پر شود، مبلغ تخفیف از روی آن محاسبه می‌شود)</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>مبلغ تخفیف سطر</summary>
    public decimal Discount { get; set; }

    /// <summary>نرخ مالیات بر ارزش افزوده (درصد)</summary>
    public decimal VatRate { get; set; }

    /// <summary>مبلغ مالیات و عوارض سطر</summary>
    public decimal VatAmount { get; set; }

    public string? Description { get; set; }

    /// <summary>موجودی فعلی کالا در انبار فاکتور (پر شده توسط سرور)</summary>
    public decimal CurrentStock { get; set; }

    // ---------- محاسباتی ----------
    /// <summary>مبلغ ناخالص = تعداد × فی</summary>
    public decimal Gross => Quantity * UnitPrice;

    /// <summary>مبلغ پس از تخفیف (مأخذ مالیات)</summary>
    public decimal Taxable => Gross - Discount;

    /// <summary>جمع سطر = مبلغ پس از تخفیف + مالیات</summary>
    public decimal Total => Taxable + VatAmount;
}

// ============================ ۲) فاکتور ============================

/// <summary>فاکتور خرید / فروش / برگشت از خرید / برگشت از فروش</summary>
public class FacInvoice
{
    public int Id { get; set; }

    /// <summary>شماره فاکتور — در هر نوع فاکتور جداگانه شماره‌گذاری می‌شود</summary>
    public int Number { get; set; }

    /// <summary>شماره عطف / شماره فاکتور طرف مقابل</summary>
    public string? RefNumber { get; set; }

    public InvoiceKind Kind { get; set; } = InvoiceKind.Sale;

    public DateTime Date { get; set; } = DateTime.Now;

    /// <summary>سررسید (برای فاکتور نسیه)</summary>
    public DateTime? DueDate { get; set; }

    public int? PartyId { get; set; }
    public string? PartyName { get; set; }

    public int WarehouseId { get; set; }
    public string? WarehouseName { get; set; }

    /// <summary>نحوه تسویه: نقد یا نسیه</summary>
    public SettlementType Settlement { get; set; } = SettlementType.Credit;

    public string? Description { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public List<FacInvoiceLine> Lines { get; set; } = new();

    // ---------- مبالغ (سمت سرور محاسبه و ذخیره می‌شود) ----------
    /// <summary>جمع ناخالص اقلام</summary>
    public decimal TotalGross { get; set; }

    /// <summary>جمع تخفیف اقلام</summary>
    public decimal TotalLineDiscount { get; set; }

    /// <summary>تخفیف کلی روی فاکتور (سرجمع)</summary>
    public decimal InvoiceDiscount { get; set; }

    /// <summary>مأخذ مالیات = ناخالص − تخفیف‌ها</summary>
    public decimal TotalTaxable { get; set; }

    /// <summary>جمع مالیات و عوارض</summary>
    public decimal TotalVat { get; set; }

    /// <summary>هزینه حمل و سایر هزینه‌های افزوده به فاکتور</summary>
    public decimal ShippingCost { get; set; }

    /// <summary>مبلغ قابل پرداخت</summary>
    public decimal TotalNet { get; set; }

    /// <summary>جمع تخصیص‌های اسناد دریافت/پرداخت قطعی‌شده به این فاکتور</summary>
    public decimal SettledAmount { get; set; }

    /// <summary>ماندهٔ باز فاکتور؛ برای فاکتور نقدی صفر است</summary>
    public decimal RemainingAmount { get; set; }

    // ---------- اسناد خودکار ----------
    /// <summary>سند انبار صادرشده (رسید یا حواله)</summary>
    public int? InvDocId { get; set; }
    public string? InvDocNumber { get; set; }

    /// <summary>سند حسابداری صادرشده</summary>
    public int? VoucherId { get; set; }
    public int? VoucherNumber { get; set; }

    // ---------- ردیابی ----------
    public int LineCount { get; set; }
    public decimal TotalQuantity { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    // ---------- عنوان‌های نمایشی ----------
    public string KindTitle => Kind switch
    {
        InvoiceKind.Purchase => "فاکتور خرید",
        InvoiceKind.Sale => "فاکتور فروش",
        InvoiceKind.PurchaseReturn => "برگشت از خرید",
        _ => "برگشت از فروش"
    };

    public string StatusTitle => Status switch
    {
        InvoiceStatus.Draft => "پیش‌نویس",
        InvoiceStatus.Confirmed => "قطعی",
        _ => "ابطال شده"
    };

    public string SettlementTitle => Settlement == SettlementType.Cash ? "نقدی" : "نسیه";

    /// <summary>آیا این فاکتور کالا را وارد انبار می‌کند؟ (خرید و برگشت از فروش)</summary>
    public bool IsIncoming => Kind is InvoiceKind.Purchase or InvoiceKind.SaleReturn;

    /// <summary>آیا این فاکتور سمت فروش است؟ (فروش و برگشت از فروش)</summary>
    public bool IsSaleSide => Kind is InvoiceKind.Sale or InvoiceKind.SaleReturn;
}

// ============================ ۳) قواعد فاکتور ============================

/// <summary>
/// پیکربندی هر نوع فاکتور: نوع سند انبارِ متناظر و حساب‌هایی که
/// سند حسابداری خودکار روی آن‌ها زده می‌شود.
/// </summary>
public class FacRule
{
    public int Id { get; set; }

    public InvoiceKind Kind { get; set; }

    /// <summary>نوع سند انبار برای صدور خودکار رسید/حواله</summary>
    public int? DocTypeId { get; set; }
    public string? DocTypeName { get; set; }

    /// <summary>حساب طرف حساب: دریافتنی (فروش) یا پرداختنی (خرید)</summary>
    public int? PartyAccountId { get; set; }
    public string? PartyAccountName { get; set; }

    /// <summary>حساب اصلی: فروش کالا (فروش) یا خرید کالا (خرید)</summary>
    public int? MainAccountId { get; set; }
    public string? MainAccountName { get; set; }

    /// <summary>حساب مالیات بر ارزش افزوده</summary>
    public int? VatAccountId { get; set; }
    public string? VatAccountName { get; set; }

    /// <summary>حساب صندوق/بانک برای فاکتورهای نقدی</summary>
    public int? CashAccountId { get; set; }
    public string? CashAccountName { get; set; }

    /// <summary>حساب هزینه حمل</summary>
    public int? ShippingAccountId { get; set; }
    public string? ShippingAccountName { get; set; }

    /// <summary>با قطعی شدن فاکتور، سند انبار خودکار صادر و قطعی شود</summary>
    public bool AutoInvDoc { get; set; } = true;

    /// <summary>با قطعی شدن فاکتور، سند حسابداری خودکار صادر و قطعی شود</summary>
    public bool AutoVoucher { get; set; } = true;

    public bool IsActive { get; set; }
    public string? Description { get; set; }

    public string KindTitle => Kind switch
    {
        InvoiceKind.Purchase => "فاکتور خرید",
        InvoiceKind.Sale => "فاکتور فروش",
        InvoiceKind.PurchaseReturn => "برگشت از خرید",
        _ => "برگشت از فروش"
    };
}

// ============================ ۴) گزارش‌ها ============================

/// <summary>یک سطر گزارش فروش/خرید (تجمیع بر اساس دوره، طرف حساب یا کالا)</summary>
public class FacSummaryRow
{
    public string Title { get; set; } = "";
    public int Count { get; set; }
    public decimal Quantity { get; set; }
    public decimal Taxable { get; set; }
    public decimal Vat { get; set; }
    public decimal Net { get; set; }
}

/// <summary>خروجی گزارش فروش و خرید</summary>
public class FacSummaryResult
{
    public InvoiceKind Kind { get; set; }
    public string GroupBy { get; set; } = "month";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public List<FacSummaryRow> Rows { get; set; } = new();

    public int TotalCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalTaxable { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalNet { get; set; }
}

/// <summary>خلاصه فروش و خرید برای داشبورد</summary>
public class FacDashboard
{
    public decimal SaleTotal { get; set; }
    public decimal PurchaseTotal { get; set; }
    public decimal SaleReturnTotal { get; set; }
    public decimal PurchaseReturnTotal { get; set; }

    public int SaleCount { get; set; }
    public int PurchaseCount { get; set; }
    public int DraftCount { get; set; }

    public decimal VatPayable { get; set; }
    public decimal Receivable { get; set; }
    public decimal Payable { get; set; }

    /// <summary>سود ناخالص تقریبی = فروش خالص − بهای تمام‌شده اقلام فروخته‌شده</summary>
    public decimal GrossProfit { get; set; }

    public List<FacSummaryRow> TopProducts { get; set; } = new();
    public List<FacSummaryRow> TopParties { get; set; } = new();
}
