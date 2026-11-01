using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>ردیف کار/قطعه مصرفی روی دستگاه تعمیری</summary>
public class RepairItem : ICompanyOwned
{
    public int Id { get; set; }
    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    public int RepairOrderId { get; set; }

    /// <summary>شرح کار انجام‌شده یا قطعه مصرفی</summary>
    [MaxLength(300)]
    public string Description { get; set; } = "";

    /// <summary>کالای مصرفی از انبار (null = فقط اجرت/کار)</summary>
    public int? ProductId { get; set; }

    public decimal Quantity { get; set; } = 1;

    /// <summary>هزینه داخلی (بهای قطعه برای مجموعه / هزینه کار)</summary>
    public decimal Cost { get; set; }

    /// <summary>مبلغ دریافتی از مشتری بابت این ردیف</summary>
    public decimal Price { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}