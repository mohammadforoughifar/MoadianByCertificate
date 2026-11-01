namespace Inventory.Shared.Dtos;

// =====================================================================
// DTO های ماژول انبارداری
//   ۱) گروه کالا (درختی)   ۲) ویژگی‌های کالا   ۳) کالا (فرم چندمرحله‌ای)
//   ۴) انبارها             ۵) نوع رسید/حواله   ۶) اسناد رسید و حواله
//   ۷) کاردکس و موجودی
// همه‌ی نام‌ها با پیشوند Inv تا با DTOهای قدیمی (Product، Warehouse و…) تداخل نکنند.
// =====================================================================

// ============================ ۱) گروه کالا ============================

/// <summary>گروه کالا — گره درخت گروه‌بندی</summary>
public class InvCategory
{
    public int Id { get; set; }

    /// <summary>کد گروه (اختیاری، یکتا)</summary>
    public string? Code { get; set; }

    /// <summary>نام گروه</summary>
    public string Name { get; set; } = "";

    /// <summary>شناسه گروه والد (null = ریشه)</summary>
    public int? ParentId { get; set; }

    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>ترتیب نمایش بین هم‌سطح‌ها</summary>
    public int SortOrder { get; set; }

    /// <summary>روش قیمت‌گذاری این گروه (null = ارث‌بری از والد یا تنظیمات کلی)</summary>
    public ValuationMethod? Valuation { get; set; }

    /// <summary>مشمول مالیات بر ارزش افزوده برای کالاهای این گروه</summary>
    public bool? IsVatIncluded { get; set; }

    /// <summary>نرخ مالیات بر ارزش افزوده گروه کالا (درصد)</summary>
    public decimal? VatRate { get; set; }

    /// <summary>روش موثر (پس از ارث‌بری) — پر شده توسط سرور</summary>
    public ValuationMethod EffectiveValuation { get; set; }

    /// <summary>آیا روش قیمت‌گذاری از والد به ارث رسیده است؟</summary>
    public bool ValuationInherited { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد کالای مستقیم این گروه</summary>
    public int ProductCount { get; set; }

    /// <summary>تعداد کالا با احتساب زیرگروه‌ها</summary>
    public int TotalProductCount { get; set; }

    /// <summary>عمق در درخت</summary>
    public int Depth { get; set; }

    /// <summary>مسیر کامل: والد ← فرزند</summary>
    public string FullPath { get; set; } = "";

    /// <summary>کد کامل سلسله‌مراتبی گروه کالا (ترکیب کدهای والد تا این سطح)</summary>
    public string HierarchyCode { get; set; } = "";

    /// <summary>زیرگروه‌ها (در خروجی درختی پر می‌شود)</summary>
    public List<InvCategory> Children { get; set; } = new();
}

/// <summary>جابه‌جایی گروه در درخت (کشیدن و رها کردن)</summary>
public class InvCategoryMove
{
    public int Id { get; set; }
    public int? NewParentId { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>درخواست اعمال ارزش افزوده به یک گروه کالا و کالاهای آن</summary>
public class ApplyCategoryVatRequest
{
    public int CategoryId { get; set; }
    public bool IsVatIncluded { get; set; } = true;
    public decimal VatRate { get; set; } = 10m;
}

public class ApplyCategoryVatResponse
{
    public int UpdatedCount { get; set; }
}

// ============================ ۲) ویژگی کالا ============================

/// <summary>تعریف یک ویژگی کالا (رنگ، سایز، ولتاژ، …)</summary>
public class InvAttribute
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }

    /// <summary>نوع مقدار</summary>
    public AttrValueType ValueType { get; set; } = AttrValueType.Text;

    /// <summary>واحد نمایش مقدار (مثلاً: میلی‌متر، وات)</summary>
    public string? Unit { get; set; }

    /// <summary>گروه کالای مرتبط (null = برای همه گروه‌ها)</summary>
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>در فهرست کالاها به‌عنوان ستون/فیلتر نمایش داده شود</summary>
    public bool ShowInList { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    /// <summary>گزینه‌های مجاز (فقط برای نوع «انتخاب از فهرست»)</summary>
    public List<InvAttributeOption> Options { get; set; } = new();

    /// <summary>تعداد کالاهایی که این ویژگی برایشان مقداردهی شده</summary>
    public int UsageCount { get; set; }
}

/// <summary>گزینه‌ی مجاز یک ویژگی از نوع فهرستی</summary>
public class InvAttributeOption
{
    public int Id { get; set; }
    public int AttributeId { get; set; }
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>مقدار یک ویژگی برای یک کالا</summary>
public class InvProductAttrValue
{
    public int Id { get; set; }
    public int AttributeId { get; set; }
    public string AttributeName { get; set; } = "";
    public AttrValueType ValueType { get; set; }
    public string? Unit { get; set; }
    public bool IsRequired { get; set; }

    /// <summary>گزینه انتخاب‌شده (نوع فهرستی)</summary>
    public int? OptionId { get; set; }

    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public bool? BoolValue { get; set; }
    public DateTime? DateValue { get; set; }

    /// <summary>گزینه‌های قابل انتخاب (پر شده توسط سرور برای رندر فرم)</summary>
    public List<InvAttributeOption> Options { get; set; } = new();

    /// <summary>نمایش خوانا از مقدار</summary>
    public string Display { get; set; } = "";
}

// ============================ ۳) کالا ============================

/// <summary>کالا — همه‌ی اطلاعات فرم چندمرحله‌ای تعریف کالا</summary>
public class InvProduct
{
    // ---------- تب ۱: اطلاعات اصلی ----------
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>نام لاتین / بازرگانی</summary>
    public string? EnName { get; set; }

    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryPath { get; set; }

    /// <summary>پیشوند کد گروه در کد کالا (تولید شده از سلسله‌مراتب)</summary>
    public string? CategoryCodePrefix { get; set; }

    /// <summary>بخش اختصاصی کد کالا (گام / سریال کالا)</summary>
    public string? ProductCodeSuffix { get; set; }

    public string Unit { get; set; } = "عدد";

    /// <summary>واحد فرعی (بسته، کارتن، …)</summary>
    public string? SecondUnit { get; set; }

    /// <summary>ضریب تبدیل واحد فرعی به واحد اصلی</summary>
    public decimal? UnitFactor { get; set; }

    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? PartNumber { get; set; }
    public string? Barcode { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsService { get; set; }

    // ---------- تب ۲: قیمت و مالیات ----------
    public decimal PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }

    /// <summary>قیمت فروش دوم (عمده)</summary>
    public decimal SalePrice2 { get; set; }

    /// <summary>کد (شناسه) کالای سازمان امور مالیاتی — ۱۳ رقمی</summary>
    public string? TaxCode { get; set; }

    /// <summary>کد واحد سنجش مالیاتی (مطابق فهرست سامانه مودیان)</summary>
    public string? TaxUnitCode { get; set; }

    /// <summary>مشمول مالیات بر ارزش افزوده</summary>
    public bool IsVatIncluded { get; set; } = true;

    /// <summary>نرخ مالیات بر ارزش افزوده (درصد)</summary>
    public decimal VatRate { get; set; } = 10;

    /// <summary>نرخ عوارض (درصد)</summary>
    public decimal DutyRate { get; set; }

    /// <summary>سایر مالیات/عوارض (درصد)</summary>
    public decimal OtherTaxRate { get; set; }

    /// <summary>کد گمرکی / تعرفه</summary>
    public string? CustomsCode { get; set; }

    /// <summary>کشور سازنده</summary>
    public string? CountryOfOrigin { get; set; }

    // ---------- تب ۳: انبار و موجودی ----------
    /// <summary>انبار پیش‌فرض کالا (null = همه انبارها)</summary>
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }

    /// <summary>روش قیمت‌گذاری اختصاصی کالا (null = ارث‌بری از گروه کالا / تنظیمات)</summary>
    public ValuationMethod? Valuation { get; set; }

    /// <summary>روش موثر پس از ارث‌بری — پر شده توسط سرور</summary>
    public ValuationMethod EffectiveValuation { get; set; }

    /// <summary>منبع روش موثر (کالا / گروه کالا / تنظیمات کلی)</summary>
    public string ValuationSource { get; set; } = "";

    public decimal ReorderPoint { get; set; }
    public decimal MaxStock { get; set; }
    public decimal MinOrderQty { get; set; }

    /// <summary>محل قفسه / آدرس انبارش</summary>
    public string? ShelfCode { get; set; }

    public bool TrackBatch { get; set; }
    public bool TrackSerial { get; set; }
    public bool TrackExpiry { get; set; }

    /// <summary>مدت اعتبار (روز)</summary>
    public int? ShelfLifeDays { get; set; }

    public decimal? Weight { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }

    // ---------- تب ۴: ویژگی‌ها ----------
    public List<InvProductAttrValue> Attributes { get; set; } = new();

    // ---------- تب ۵: توضیحات ----------
    public string? Description { get; set; }
    public string? Note { get; set; }
    public string? ImageUrl { get; set; }

    // ---------- محاسباتی ----------
    public DateTime CreatedAt { get; set; }
    public decimal TotalStock { get; set; }
    public decimal AvgCost { get; set; }
    public decimal StockValue { get; set; }
    public bool BelowReorder => ReorderPoint > 0 && TotalStock <= ReorderPoint;
}

// ============================ ۴) انبار ============================

/// <summary>انبار</summary>
public class InvWarehouse
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = "";
    public WarehouseKind Kind { get; set; } = WarehouseKind.Main;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? KeeperName { get; set; }

    /// <summary>انبار پیش‌فرض سیستم</summary>
    public bool IsDefault { get; set; }

    /// <summary>اجازه منفی شدن موجودی این انبار</summary>
    public bool AllowNegative { get; set; }

    /// <summary>روش قیمت‌گذاری پیش‌فرض انبار (اختیاری — اولویت پس از گروه کالا)</summary>
    public ValuationMethod? Valuation { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }

    // ---------- محاسباتی ----------
    /// <summary>تعداد اقلام دارای موجودی</summary>
    public int ItemCount { get; set; }

    public decimal TotalQuantity { get; set; }
    public decimal TotalValue { get; set; }
    public int DocCount { get; set; }
}

// ============================ ۵) نوع رسید و حواله ============================

/// <summary>نوع رسید/حواله — ماهیت آن تعیین می‌کند سند روی موجودی چه اثری دارد</summary>
public class InvDocType
{
    public int Id { get; set; }

    /// <summary>کد نوع سند (یکتا)</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>ماهیت: افزایشی / کاهشی / خنثی</summary>
    public StockNature Nature { get; set; } = StockNature.Increase;

    /// <summary>سند انتقال بین دو انبار (فقط برای ماهیت خنثی معنا دارد)</summary>
    public bool IsTransfer { get; set; }

    /// <summary>طرف حساب اجباری است</summary>
    public bool RequiresParty { get; set; }

    /// <summary>مبلغ واحد اجباری است</summary>
    public bool RequiresPrice { get; set; }

    /// <summary>پیش‌شماره سند (مثلاً RC برای رسید)</summary>
    public string? NumberPrefix { get; set; }

    /// <summary>رنگ نشان (badge) در فهرست‌ها</summary>
    public string? Color { get; set; }

    /// <summary>آیکن Bootstrap Icons</summary>
    public string? Icon { get; set; }

    /// <summary>نوع سیستمی — قابل حذف نیست</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Description { get; set; }

    /// <summary>تعداد اسناد ثبت‌شده با این نوع</summary>
    public int DocCount { get; set; }

    public string NatureTitle => Nature switch
    {
        StockNature.Increase => "افزایشی (رسید)",
        StockNature.Decrease => "کاهشی (حواله)",
        _ => IsTransfer ? "خنثی (انتقال بین انبار)" : "خنثی (بدون اثر)"
    };
}

// ============================ ۶) سند انبار ============================

/// <summary>سطر سند رسید/حواله</summary>
public class InvDocLine
{
    public int Id { get; set; }
    public int RowNo { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? UnitPriceText { get; set; }
    public decimal Discount { get; set; }

    /// <summary>ورودی تخفیف (تا ۱۰۰ = درصد، بیشتر از ۱۰۰ = ریال)</summary>
    public decimal DiscountInput { get; set; }

    /// <summary>مشمول مالیات بر ارزش افزوده</summary>
    public bool IsVatIncluded { get; set; }

    /// <summary>نرخ مالیات بر ارزش افزوده (درصد)</summary>
    public decimal VatRate { get; set; }

    /// <summary>مبلغ مالیات بر ارزش افزوده سطر</summary>
    public decimal VatAmount => IsVatIncluded && VatRate > 0
        ? Math.Round((Quantity * UnitPrice - Discount) * (VatRate / 100m), 0)
        : 0;

    /// <summary>مبلغ سطر = تعداد × فی − تخفیف + ارزش افزوده</summary>
    public decimal Amount => (Quantity * UnitPrice - Discount) + VatAmount;

    public string? BatchNo { get; set; }
    public string? SerialNo { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Description { get; set; }

    /// <summary>موجودی فعلی کالا در انبار سند (پر شده توسط سرور)</summary>
    public decimal CurrentStock { get; set; }

    /// <summary>بهای تمام‌شده خروج (برای اسناد کاهشیِ قطعی‌شده)</summary>
    public decimal? OutCost { get; set; }
}

/// <summary>سند رسید یا حواله انبار</summary>
public class InvDoc
{
    public int Id { get; set; }
    public string Number { get; set; } = "";

    public int DocTypeId { get; set; }
    public string DocTypeName { get; set; } = "";
    public string? DocTypeColor { get; set; }
    public string? DocTypeIcon { get; set; }
    public StockNature Nature { get; set; }
    public bool IsTransfer { get; set; }

    public int WarehouseId { get; set; }
    public string? WarehouseName { get; set; }

    /// <summary>انبار مقصد (اسناد انتقال)</summary>
    public int? CounterWarehouseId { get; set; }
    public string? CounterWarehouseName { get; set; }

    public int? PartyId { get; set; }
    public string? PartyName { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;

    /// <summary>شماره/عطف سند مرجع (فاکتور، بارنامه، …)</summary>
    public string? RefNumber { get; set; }

    public string? Description { get; set; }
    public InvDocStatus Status { get; set; } = InvDocStatus.Draft;

    public decimal TotalQuantity { get; set; }
    public decimal TotalValue { get; set; }
    public int LineCount { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public List<InvDocLine> Lines { get; set; } = new();

    public string StatusTitle => Status switch
    {
        InvDocStatus.Draft => "پیش‌نویس",
        InvDocStatus.Confirmed => "قطعی",
        _ => "ابطال شده"
    };
}

// ============================ ۷) کاردکس و موجودی ============================

/// <summary>یک سطر کاردکس ریالی/مقداری کالا</summary>
public class InvKardexRow
{
    public DateTime Date { get; set; }
    public int DocId { get; set; }
    public string Number { get; set; } = "";
    public string DocTypeName { get; set; } = "";
    public StockNature Nature { get; set; }
    public string? WarehouseName { get; set; }
    public string? PartyName { get; set; }
    public string? Description { get; set; }

    public decimal InQty { get; set; }
    public decimal InPrice { get; set; }
    public decimal InValue { get; set; }

    public decimal OutQty { get; set; }
    public decimal OutPrice { get; set; }
    public decimal OutValue { get; set; }

    public decimal BalanceQty { get; set; }
    public decimal BalanceValue { get; set; }
    public decimal BalanceAvg => BalanceQty != 0 ? BalanceValue / BalanceQty : 0;
}

/// <summary>خروجی کامل گزارش کاردکس</summary>
public class InvKardexResult
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? WarehouseName { get; set; }

    public ValuationMethod Method { get; set; }
    public string MethodTitle { get; set; } = "";

    /// <summary>مانده ابتدای بازه</summary>
    public decimal OpeningQty { get; set; }
    public decimal OpeningValue { get; set; }

    public List<InvKardexRow> Rows { get; set; } = new();

    public decimal TotalInQty { get; set; }
    public decimal TotalInValue { get; set; }
    public decimal TotalOutQty { get; set; }
    public decimal TotalOutValue { get; set; }
    public decimal ClosingQty { get; set; }
    public decimal ClosingValue { get; set; }
}

/// <summary>یک سطر موجودی انبار (مقداری و ریالی)</summary>
public class InvStockRow
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? CategoryName { get; set; }
    public int WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Quantity { get; set; }
    public decimal AvgCost { get; set; }
    public decimal Value { get; set; }
    public decimal ReorderPoint { get; set; }
    public bool BelowReorder => ReorderPoint > 0 && Quantity <= ReorderPoint;
}

// ============================ ۸) تنظیمات جامع انبارداری و کدینگ ============================

/// <summary>تنظیمات انبارداری، ساختار کدینگ کالا و ارزش افزوده</summary>
public class WarehousingSettingsDto
{
    /// <summary>روش قیمت‌گذاری خروج انبار: Average | FIFO | LIFO</summary>
    public string CostingMethod { get; set; } = "Average";

    /// <summary>اجازه منفی شدن موجودی در انبار</summary>
    public bool AllowNegativeStock { get; set; } = false;

    /// <summary>نرخ پیش‌فرض مالیات بر ارزش افزوده (درصد)</summary>
    public decimal DefaultVatRate { get; set; } = 10m;

    /// <summary>نرخ پیش‌فرض عوارض قانونی (درصد)</summary>
    public decimal DefaultDutyRate { get; set; } = 0m;

    /// <summary>طول کد هر سطح از گروه‌های کالا (تعداد ارقام، پیش‌فرض: ۲)</summary>
    public int CategoryCodeLength { get; set; } = 2;

    /// <summary>طول کد اختصاصی کالا / گام کالا (تعداد ارقام/کاراکترها، پیش‌فرض: ۴)</summary>
    public int ProductCodeLength { get; set; } = 4;

    /// <summary>تولید خودکار کد کالا بر اساس سلسله‌مراتب گروه کالا</summary>
    public bool AutoCodeFromCategory { get; set; } = true;

    /// <summary>جداکننده اجزای کد کالا (خالی = بدون جداکننده مانند 01020001، یا - یا /)</summary>
    public string? CodeDelimiter { get; set; } = "";
}

