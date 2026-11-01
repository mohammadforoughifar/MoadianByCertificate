using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>قسط سند فروش اقساطی (دفترچه اقساط)</summary>
public class InstallmentLine : ICompanyOwned
{
    public int Id { get; set; }
    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    public int TransactionId { get; set; }

    /// <summary>شماره قسط (۱، ۲، ...)</summary>
    public int No { get; set; }

    public decimal Amount { get; set; }

    /// <summary>تاریخ سررسید قسط</summary>
    public DateTime DueDate { get; set; }

    /// <summary>پرداخت شده؟</summary>
    public bool IsPaid { get; set; }

    /// <summary>تاریخ پرداخت</summary>
    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}