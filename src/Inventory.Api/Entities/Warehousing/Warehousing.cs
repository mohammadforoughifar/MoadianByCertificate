using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Inventory.Shared;

namespace Inventory.Api.Data;

// =====================================================================
// موجودیت‌های ماژول انبارداری
//  • ویژگی کالا (تعریف / گزینه / مقدار)
//  • نوع رسید و حواله (با ماهیت افزایشی، کاهشی، خنثی)
//  • سند انبار و سطرهای آن
//  • دفتر (لجر) کاردکس + مانده انبارداری
// =====================================================================

// ============================ ویژگی کالا ============================

/// <summary>تعریف یک ویژگی کالا (رنگ، سایز، ولتاژ، …)</summary>
public class ProductAttributeDef
{
    public int Id { get; set; }

    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(50)] public string? Code { get; set; }

    /// <summary>نوع مقدار (متنی، عددی، بله/خیر، تاریخ، فهرستی)</summary>
    public AttrValueType ValueType { get; set; } = AttrValueType.Text;

    /// <summary>واحد نمایش مقدار</summary>
    [MaxLength(50)] public string? Unit { get; set; }

    /// <summary>گروه کالای مرتبط (null = عمومی برای همه گروه‌ها)</summary>
    public int? CategoryId { get; set; }

    public bool IsRequired { get; set; }
    public bool ShowInList { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>گزینه‌ی مجاز یک ویژگی فهرستی</summary>
public class ProductAttributeOption
{
    public int Id { get; set; }
    public int AttributeId { get; set; }
    [MaxLength(150)] public string Title { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>مقدار یک ویژگی برای یک کالا</summary>
public class ProductAttributeValue
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int AttributeId { get; set; }

    /// <summary>الزام ویژگی در سطح این کالا (دارد / ندارد)</summary>
    [NotMapped]
    public bool IsRequired { get; set; }

    public int? OptionId { get; set; }
    [MaxLength(500)] public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public bool? BoolValue { get; set; }
    public DateTime? DateValue { get; set; }
}

// ============================ نوع رسید و حواله ============================

/// <summary>نوع رسید/حواله — ماهیت آن اثر سند روی موجودی را تعیین می‌کند</summary>
public class InvDocType
{
    public int Id { get; set; }

    [MaxLength(30)] public string Code { get; set; } = "";
    [MaxLength(120)] public string Name { get; set; } = "";

    /// <summary>ماهیت: افزایشی (رسید) / کاهشی (حواله) / خنثی</summary>
    public StockNature Nature { get; set; } = StockNature.Increase;

    /// <summary>سند انتقال بین دو انبار (ماهیت خنثی)</summary>
    public bool IsTransfer { get; set; }

    public bool RequiresParty { get; set; }
    public bool RequiresPrice { get; set; }

    [MaxLength(10)] public string? NumberPrefix { get; set; }
    [MaxLength(30)] public string? Color { get; set; }
    [MaxLength(50)] public string? Icon { get; set; }

    /// <summary>نوع سیستمی — قابل حذف نیست</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

// ============================ سند انبار ============================

/// <summary>سند رسید یا حواله انبار</summary>
public class InvDoc
{
    public int Id { get; set; }

    [MaxLength(30)] public string Number { get; set; } = "";

    public int DocTypeId { get; set; }
    public int WarehouseId { get; set; }

    /// <summary>انبار مقصد (اسناد انتقال)</summary>
    public int? CounterWarehouseId { get; set; }

    public int? PartyId { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;

    [MaxLength(50)] public string? RefNumber { get; set; }
    public string? Description { get; set; }

    public InvDocStatus Status { get; set; } = InvDocStatus.Draft;

    public decimal TotalQuantity { get; set; }
    public decimal TotalValue { get; set; }

    [MaxLength(80)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(80)] public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public List<InvDocLine> Lines { get; set; } = new();
}

/// <summary>سطر سند انبار</summary>
public class InvDocLine
{
    public int Id { get; set; }
    public int DocId { get; set; }
    public int RowNo { get; set; }

    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    [NotMapped] public bool IsVatIncluded { get; set; }
    [NotMapped] public decimal VatRate { get; set; }
    [NotMapped] public decimal VatAmount { get; set; }

    [MaxLength(50)] public string? BatchNo { get; set; }
    [MaxLength(80)] public string? SerialNo { get; set; }
    public DateTime? ExpiryDate { get; set; }
    [MaxLength(300)] public string? Description { get; set; }

    /// <summary>بهای تمام‌شده محاسبه‌شده هنگام قطعی‌سازی (اسناد کاهشی)</summary>
    public decimal? OutCost { get; set; }

    public InvDoc? Doc { get; set; }
}

// ============================ دفتر کاردکس ============================

/// <summary>
/// یک ردیف دفتر انبار (کاردکس) — پس از قطعی‌سازی اسناد ساخته می‌شود و
/// مبنای گزارش کاردکس، مانده مقداری و ریالی و لایه‌های FIFO/LIFO است.
/// </summary>
public class InvLedgerEntry
{
    public long Id { get; set; }

    public int ProductId { get; set; }
    public int WarehouseId { get; set; }

    public DateTime Date { get; set; }

    public int DocId { get; set; }
    public int DocLineId { get; set; }
    public int DocTypeId { get; set; }
    public StockNature Nature { get; set; }

    [MaxLength(30)] public string Number { get; set; } = "";
    [MaxLength(300)] public string? Description { get; set; }

    public decimal QtyIn { get; set; }
    public decimal QtyOut { get; set; }

    /// <summary>بهای واحد این گردش</summary>
    public decimal UnitCost { get; set; }

    public decimal ValueIn { get; set; }
    public decimal ValueOut { get; set; }

    /// <summary>مانده مقداری پس از این گردش</summary>
    public decimal BalanceQty { get; set; }

    /// <summary>مانده ریالی پس از این گردش</summary>
    public decimal BalanceValue { get; set; }

    /// <summary>باقی‌مانده‌ی این لایه (فقط ردیف‌های ورودی — برای FIFO/LIFO)</summary>
    public decimal RemainingQty { get; set; }

    /// <summary>ترتیب گردش در یک تاریخ</summary>
    public int Seq { get; set; }
}

/// <summary>مانده مقداری و ریالی هر کالا در هر انبار — حاصل اسناد انبارداری</summary>
public class InvStock
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Value { get; set; }
    public decimal AvgCost { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
