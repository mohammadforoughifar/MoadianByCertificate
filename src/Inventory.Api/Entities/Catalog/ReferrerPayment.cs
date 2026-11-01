using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>سند پرداخت پورسانت به معرف.</summary>
public class ReferrerPayment : ICompanyOwned
{
    public int Id { get; set; }
    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    public int ReferrerId { get; set; }

    [MaxLength(30)]
    public string Number { get; set; } = "";

    public decimal Amount { get; set; }
    public DateTime Date { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}