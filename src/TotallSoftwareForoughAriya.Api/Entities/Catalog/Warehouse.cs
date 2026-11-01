using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Inventory.Shared;
namespace Inventory.Api.Data;

public class Warehouse : ICompanyOwned
{
    public int Id { get; set; }

    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    [MaxLength(150)]
    public string Name { get; set; } = "";

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    // ---------- ماژول انبارداری ----------
    /// <summary>کد انبار (اختیاری، یکتا)</summary>
    [MaxLength(30)] public string? Code { get; set; }

    /// <summary>ماهیت انبار (اصلی، فرعی، امانی، ضایعات، …)</summary>
    public WarehouseKind Kind { get; set; } = WarehouseKind.Main;

    /// <summary>نام انباردار</summary>
    [MaxLength(120)] public string? KeeperName { get; set; }

    /// <summary>انبار پیش‌فرض سیستم</summary>
    public bool IsDefault { get; set; }

    /// <summary>اجازه منفی شدن موجودی در این انبار</summary>
    public bool AllowNegative { get; set; }

    /// <summary>روش قیمت‌گذاری پیش‌فرض انبار (اختیاری — اولویت پس از گروه کالا)</summary>
    [NotMapped]
    public ValuationMethod? Valuation { get; set; }
}