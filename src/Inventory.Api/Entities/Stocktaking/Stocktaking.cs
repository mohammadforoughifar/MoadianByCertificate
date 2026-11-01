using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

// =====================================================================
// موجودیت‌های ماژول انبارگردانی و بارکد
//   BcdBarcode  — بارکدهای کالا (چند بارکد برای هر کالا)
//   StkSession  — دوره انبارگردانی
//   StkLine     — قلم لیست شمارش
//   StkScan     — تاریخچه تک‌تک اسکن‌ها (رد پای شمارش)
// =====================================================================

/// <summary>یک بارکد متصل به کالا</summary>
public class BcdBarcode
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>محتوای بارکد — در کل سیستم یکتا</summary>
    [MaxLength(60)] public string Code { get; set; } = "";

    public BarcodeType Type { get; set; } = BarcodeType.Code128;

    /// <summary>واحد این بارکد (عدد، بسته، کارتن)</summary>
    [MaxLength(50)] public string? Unit { get; set; }

    /// <summary>چند واحد اصلی در این بسته است</summary>
    public decimal PackQty { get; set; } = 1;

    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;

    [MaxLength(300)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>یک دوره انبارگردانی</summary>
public class StkSession
{
    public int Id { get; set; }

    public int Number { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;

    public StocktakeScope Scope { get; set; } = StocktakeScope.InStock;

    public int? CategoryId { get; set; }

    public StocktakeStatus Status { get; set; } = StocktakeStatus.Draft;

    /// <summary>اقلام شمارش‌نشده هنگام اعمال، صفر فرض شوند</summary>
    public bool TreatUncountedAsZero { get; set; }

    [MaxLength(600)] public string? Description { get; set; }

    /// <summary>سند رسید اضافی انبار</summary>
    public int? SurplusDocId { get; set; }

    /// <summary>سند حواله کسری انبار</summary>
    public int? ShortageDocId { get; set; }

    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(120)] public string? AppliedBy { get; set; }
    public DateTime? AppliedAt { get; set; }

    public List<StkLine> Lines { get; set; } = new();
}

/// <summary>یک قلم از لیست شمارش</summary>
public class StkLine
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public StkSession? Session { get; set; }

    public int RowNo { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>موجودی سیستم در لحظه‌ی قفل شدن دوره</summary>
    public decimal SystemQty { get; set; }

    public decimal CountedQty { get; set; }

    public bool IsCounted { get; set; }

    /// <summary>بهای واحد در لحظه اسنپ‌شات</summary>
    public decimal UnitCost { get; set; }

    [MaxLength(300)] public string? Note { get; set; }

    [MaxLength(120)] public string? CountedBy { get; set; }
    public DateTime? CountedAt { get; set; }

    public int ScanCount { get; set; }
}

/// <summary>رد پای یک اسکن یا ورود دستی در حین شمارش</summary>
public class StkScan
{
    public long Id { get; set; }

    public int SessionId { get; set; }
    public StkSession? Session { get; set; }

    public int LineId { get; set; }
    public int ProductId { get; set; }

    [MaxLength(60)] public string? Barcode { get; set; }

    /// <summary>مقداری که در این اسکن ثبت شد</summary>
    public decimal Quantity { get; set; }

    /// <summary>true = افزوده شد | false = جایگزین شد</summary>
    public bool Accumulated { get; set; }

    [MaxLength(120)] public string? ScannedBy { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.Now;
}
