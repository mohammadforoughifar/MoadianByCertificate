using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;
public class Product : ICompanyOwned
{
    public int Id { get; set; }
    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    [MaxLength(50)] public string Code { get; set; } = "";
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(50)] public string Unit { get; set; } = "عدد";
    [MaxLength(100)] public string? Category { get; set; }
    [MaxLength(100)] public string? Barcode { get; set; }
    public decimal SalePrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal ReorderPoint { get; set; }
    public decimal MaxStock { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsService { get; set; } = false;
    public int? WarehouseId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ==================================================================
    // ماژول انبارداری — فیلدهای تکمیلی فرم چندمرحله‌ای تعریف کالا
    // (همه اختیاری تا دیتابیس‌های موجود بدون مشکل ارتقا پیدا کنند)
    // ==================================================================

    // ---------- اطلاعات اصلی ----------
    /// <summary>نام لاتین / بازرگانی</summary>
    [MaxLength(200)] public string? EnName { get; set; }

    /// <summary>گروه کالا (کلید) — فیلد متنی Category برای سازگاری با نسخه قبل نگه داشته شده است</summary>
    public int? CategoryId { get; set; }

    /// <summary>واحد فرعی (بسته، کارتن، …)</summary>
    [MaxLength(50)] public string? SecondUnit { get; set; }

    /// <summary>ضریب تبدیل واحد فرعی به واحد اصلی</summary>
    public decimal? UnitFactor { get; set; }

    [MaxLength(100)] public string? Brand { get; set; }
    [MaxLength(100)] public string? Model { get; set; }
    [MaxLength(100)] public string? PartNumber { get; set; }

    // ---------- قیمت و مالیات ----------
    /// <summary>قیمت فروش دوم (عمده)</summary>
    public decimal SalePrice2 { get; set; }

    /// <summary>شناسه کالای سازمان امور مالیاتی (۱۳ رقمی)</summary>
    [MaxLength(20)] public string? TaxCode { get; set; }

    /// <summary>کد واحد سنجش مالیاتی</summary>
    [MaxLength(20)] public string? TaxUnitCode { get; set; }

    /// <summary>مشمول مالیات بر ارزش افزوده</summary>
    public bool IsVatIncluded { get; set; }

    /// <summary>نرخ مالیات بر ارزش افزوده (درصد)</summary>
    public decimal VatRate { get; set; }

    /// <summary>نرخ عوارض (درصد)</summary>
    public decimal DutyRate { get; set; }

    /// <summary>سایر مالیات و عوارض (درصد)</summary>
    public decimal OtherTaxRate { get; set; }

    /// <summary>کد گمرکی / تعرفه</summary>
    [MaxLength(30)] public string? CustomsCode { get; set; }

    /// <summary>کشور سازنده</summary>
    [MaxLength(80)] public string? CountryOfOrigin { get; set; }

    // ---------- انبار و موجودی ----------
    /// <summary>روش قیمت‌گذاری اختصاصی کالا (null = ارث‌بری از گروه کالا یا تنظیمات کلی)</summary>
    public ValuationMethod? Valuation { get; set; }

    public decimal MinOrderQty { get; set; }

    /// <summary>محل قفسه / آدرس انبارش</summary>
    [MaxLength(50)] public string? ShelfCode { get; set; }

    public bool TrackBatch { get; set; }
    public bool TrackSerial { get; set; }
    public bool TrackExpiry { get; set; }

    /// <summary>مدت اعتبار (روز)</summary>
    public int? ShelfLifeDays { get; set; }

    public decimal? Weight { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }

    // ---------- سایر ----------
    public string? Note { get; set; }
    [MaxLength(300)] public string? ImageUrl { get; set; }
}
