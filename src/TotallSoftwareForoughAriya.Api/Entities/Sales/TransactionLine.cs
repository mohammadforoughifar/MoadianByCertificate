using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

public class TransactionLine : ICompanyOwned
{
    public int Id { get; set; }
    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    public int TransactionId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }

    /// <summary>درصد تخفیف سطر (۰ تا ۱۰۰)</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>مبلغ تخفیف محاسبه‌شده سطر (ریال)</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>آیا نحوهٔ ورود کاربر به ریال بوده یا درصد؟ (عدد > ۱۰۰ به ریال)</summary>
    public bool DiscountIsAmount { get; set; }

    /// <summary>بهای تمام‌شدهٔ قطعیِ واحد برای سطرهایی مثل فاکتور تعمیر؛ برای فروش عادی خالی می‌ماند.</summary>
    public decimal? UnitCostSnapshot { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }
}
