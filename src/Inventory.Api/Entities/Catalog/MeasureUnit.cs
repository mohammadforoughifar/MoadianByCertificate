using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>واحد شمارش کالا (عدد، کیلوگرم، لیتر، ...)</summary>
public class MeasureUnit : ICompanyOwned
{
    public int Id { get; set; }

    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    [MaxLength(50)]
    public string Name { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}