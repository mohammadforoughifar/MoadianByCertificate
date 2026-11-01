using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>گروه / دسته‌بندی کالا (درختی — با پشتیبانی از گروه والد)</summary>
public class ProductCategory : ICompanyOwned
{
    public int Id { get; set; }

    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    [MaxLength(100)]
    public string Name { get; set; } = "";

    /// <summary>شناسه گروه والد (null = گروه ریشه)</summary>
    public int? ParentId { get; set; }

    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ---------- ماژول انبارداری ----------
    /// <summary>کد گروه (اختیاری)</summary>
    [MaxLength(30)] public string? Code { get; set; }

    /// <summary>ترتیب نمایش بین گروه‌های هم‌سطح</summary>
    public int SortOrder { get; set; }

    /// <summary>روش قیمت‌گذاری گروه (null = ارث‌بری از والد یا تنظیمات کلی)</summary>
    public ValuationMethod? Valuation { get; set; }

    /// <summary>مشمول مالیات بر ارزش افزوده برای کالاهای این گروه</summary>
    [NotMapped]
    public bool? IsVatIncluded { get; set; }

    /// <summary>نرخ مالیات بر ارزش افزوده گروه کالا (درصد)</summary>
    [NotMapped]
    public decimal? VatRate { get; set; }
}