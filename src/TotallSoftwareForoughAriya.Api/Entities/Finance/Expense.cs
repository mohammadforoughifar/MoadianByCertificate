using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;
public class Expense : ICompanyOwned
{
    public int Id { get; set; }
    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    [MaxLength(30)] public string Number { get; set; } = "";
    public int CategoryId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public CashType PayType { get; set; } = CashType.Cash;

    /// <summary>بانک / حساب خزانهٔ انتخابی برای نحوهٔ پرداخت</summary>
    public int? TrsAccountId { get; set; }
    [MaxLength(200)] public string? TrsAccountName { get; set; }
    [MaxLength(150)] public string? Payee { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}