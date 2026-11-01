namespace Inventory.Shared.Dtos;

// =====================================================================
// DTO های ماژول انبارگردانی و بارکد
//   ۱) بارکد کالا و اسکن       ۲) دوره انبارگردانی و اقلام آن
//   ۳) ثبت شمارش (اسکن)        ۴) گزارش مغایرت و برچسب
// نام‌ها با پیشوند Stk (انبارگردانی) و Bcd (بارکد).
// =====================================================================

// ============================ ۱) بارکد ============================

/// <summary>یک بارکد متصل به کالا — هر کالا می‌تواند چند بارکد داشته باشد</summary>
public class BcdBarcode
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }

    /// <summary>محتوای بارکد — در کل سیستم یکتاست</summary>
    public string Code { get; set; } = "";

    public BarcodeType Type { get; set; } = BarcodeType.Code128;

    /// <summary>واحدی که این بارکد نماینده آن است (عدد، بسته، کارتن)</summary>
    public string? Unit { get; set; }

    /// <summary>چند واحد اصلی در این بسته است — اسکن یک کارتن یعنی مثلاً ۱۲ عدد</summary>
    public decimal PackQty { get; set; } = 1;

    /// <summary>بارکد اصلی کالا (روی برچسب چاپ می‌شود)</summary>
    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public string TypeTitle => Type switch
    {
        BarcodeType.Code128 => "Code 128",
        BarcodeType.Ean13 => "EAN-13",
        BarcodeType.Ean8 => "EAN-8",
        _ => "کد داخلی"
    };
}

/// <summary>نتیجه اسکن یک بارکد</summary>
public class BcdScanResult
{
    public bool Found { get; set; }

    /// <summary>بارکدی که اسکن شد</summary>
    public string Code { get; set; } = "";

    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>ضریب بسته — مقدار پیش‌فرضی که با یک اسکن باید اضافه شود</summary>
    public decimal PackQty { get; set; } = 1;

    /// <summary>موجودی فعلی کالا در انبار درخواستی</summary>
    public decimal CurrentStock { get; set; }

    public decimal SalePrice { get; set; }
    public decimal PurchasePrice { get; set; }

    /// <summary>پیام خطا وقتی بارکد پیدا نشد</summary>
    public string? Message { get; set; }
}

/// <summary>یک برچسب آماده چاپ</summary>
public class BcdLabel
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? Barcode { get; set; }
    public BarcodeType Type { get; set; } = BarcodeType.Code128;
    public string? Unit { get; set; }
    public decimal SalePrice { get; set; }

    /// <summary>تعداد تکرار این برچسب در صفحه چاپ</summary>
    public int Copies { get; set; } = 1;
}

// ============================ ۲) دوره انبارگردانی ============================

/// <summary>یک قلم از لیست شمارش</summary>
public class StkLine
{
    public int Id { get; set; }
    public int RowNo { get; set; }

    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? Barcode { get; set; }
    public string? ShelfCode { get; set; }

    /// <summary>موجودی سیستم در لحظه‌ی شروع شمارش (قفل‌شده)</summary>
    public decimal SystemQty { get; set; }

    /// <summary>مقدار شمارش‌شده</summary>
    public decimal CountedQty { get; set; }

    /// <summary>آیا این قلم شمارش شده است؟ (تفکیک «صفر شمرده شد» از «شمرده نشد»)</summary>
    public bool IsCounted { get; set; }

    /// <summary>بهای واحد در لحظه اسنپ‌شات — برای ارزش‌گذاری مغایرت</summary>
    public decimal UnitCost { get; set; }

    public string? Note { get; set; }

    public string? CountedBy { get; set; }
    public DateTime? CountedAt { get; set; }

    /// <summary>تعداد دفعات اسکن این قلم</summary>
    public int ScanCount { get; set; }

    // ---------- محاسباتی ----------
    /// <summary>مغایرت = شمارش − سیستم. مثبت یعنی اضافی انبار</summary>
    public decimal Diff => CountedQty - SystemQty;

    /// <summary>ارزش ریالی مغایرت</summary>
    public decimal DiffValue => Diff * UnitCost;

    public bool HasDiff => IsCounted && Diff != 0;

    /// <summary>اضافی انبار — باید رسید صادر شود</summary>
    public bool IsSurplus => IsCounted && Diff > 0;

    /// <summary>کسری انبار — باید حواله صادر شود</summary>
    public bool IsShortage => IsCounted && Diff < 0;
}

/// <summary>یک دوره (سشن) انبارگردانی</summary>
public class StkSession
{
    public int Id { get; set; }

    public int Number { get; set; }
    public string Title { get; set; } = "";

    public int WarehouseId { get; set; }
    public string? WarehouseName { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;

    public StocktakeScope Scope { get; set; } = StocktakeScope.InStock;

    /// <summary>گروه کالا — وقتی Scope = ByCategory</summary>
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }

    public StocktakeStatus Status { get; set; } = StocktakeStatus.Draft;

    /// <summary>اقلامی که شمارش نشده‌اند، هنگام اعمال، صفر در نظر گرفته شوند</summary>
    public bool TreatUncountedAsZero { get; set; }

    public string? Description { get; set; }

    public List<StkLine> Lines { get; set; } = new();

    // ---------- اسناد اصلاح ----------
    /// <summary>سند رسید اضافی انبار</summary>
    public int? SurplusDocId { get; set; }
    public string? SurplusDocNumber { get; set; }

    /// <summary>سند حواله کسری انبار</summary>
    public int? ShortageDocId { get; set; }
    public string? ShortageDocNumber { get; set; }

    // ---------- آمار (پر شده توسط سرور) ----------
    public int TotalLines { get; set; }
    public int CountedLines { get; set; }
    public int DiffLines { get; set; }

    public decimal SurplusQty { get; set; }
    public decimal ShortageQty { get; set; }
    public decimal SurplusValue { get; set; }

    /// <summary>ارزش کسری — همیشه مثبت گزارش می‌شود</summary>
    public decimal ShortageValue { get; set; }

    /// <summary>خالص اثر ریالی = اضافی − کسری</summary>
    public decimal NetValue { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? AppliedBy { get; set; }
    public DateTime? AppliedAt { get; set; }

    // ---------- عنوان‌های نمایشی ----------
    public string StatusTitle => Status switch
    {
        StocktakeStatus.Draft => "پیش‌نویس",
        StocktakeStatus.Counting => "در حال شمارش",
        StocktakeStatus.Review => "بررسی مغایرت",
        StocktakeStatus.Applied => "اعمال شده",
        _ => "لغو شده"
    };

    public string ScopeTitle => Scope switch
    {
        StocktakeScope.InStock => "کالاهای دارای موجودی",
        StocktakeScope.AllProducts => "همه کالاهای فعال",
        StocktakeScope.ByCategory => "یک گروه کالا",
        _ => "فقط کالاهای اسکن‌شده"
    };

    /// <summary>درصد پیشرفت شمارش</summary>
    public int Progress => TotalLines == 0 ? 0 : (int)Math.Round(CountedLines * 100.0 / TotalLines);

    public bool IsEditable => Status is StocktakeStatus.Draft or StocktakeStatus.Counting;
}

// ============================ ۳) ثبت شمارش ============================

/// <summary>دستور ثبت یک شمارش (از اسکنر یا ورود دستی)</summary>
public class StkCountCommand
{
    public int SessionId { get; set; }

    /// <summary>یکی از این دو باید پر باشد</summary>
    public int? ProductId { get; set; }
    public string? Barcode { get; set; }

    /// <summary>مقدار</summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>true = به مقدار قبلی اضافه شود (حالت اسکنر) | false = جایگزین شود (ورود دستی)</summary>
    public bool Accumulate { get; set; } = true;

    public string? Note { get; set; }
}

/// <summary>پاسخ سرور به یک شمارش — برای بازخورد سریع در صفحه اسکنر</summary>
public class StkCountResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }

    /// <summary>سطر به‌روزشده</summary>
    public StkLine? Line { get; set; }

    /// <summary>آمار به‌روز شده‌ی دوره</summary>
    public int TotalLines { get; set; }
    public int CountedLines { get; set; }
    public int DiffLines { get; set; }

    /// <summary>کالای اسکن‌شده در لیست شمارش نبود و اضافه شد</summary>
    public bool LineAdded { get; set; }
}

// ============================ ۴) گزارش ============================

/// <summary>یک سطر گزارش مغایرت انبارگردانی</summary>
public class StkDiffRow
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? ShelfCode { get; set; }

    public decimal SystemQty { get; set; }
    public decimal CountedQty { get; set; }
    public decimal Diff { get; set; }
    public decimal UnitCost { get; set; }
    public decimal DiffValue { get; set; }

    public bool IsCounted { get; set; }
    public string? Note { get; set; }
}

/// <summary>خروجی گزارش مغایرت</summary>
public class StkDiffResult
{
    public int SessionId { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string WarehouseName { get; set; } = "";
    public DateTime Date { get; set; }
    public StocktakeStatus Status { get; set; }

    public List<StkDiffRow> Rows { get; set; } = new();

    public int TotalLines { get; set; }
    public int CountedLines { get; set; }
    public int UncountedLines { get; set; }

    public decimal SurplusQty { get; set; }
    public decimal ShortageQty { get; set; }
    public decimal SurplusValue { get; set; }
    public decimal ShortageValue { get; set; }
    public decimal NetValue { get; set; }

    /// <summary>دقت انبار: درصد اقلامی که مغایرت نداشته‌اند</summary>
    public decimal Accuracy { get; set; }
}
