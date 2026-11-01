using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>سند انبار (خرید / فروش / اصلاح موجودی)</summary>
public class Transaction : ICompanyOwned
{
    public int Id { get; set; }

    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    [MaxLength(30)]
    public string Number { get; set; } = "";

    public TransactionType Type { get; set; }
    public DateTime Date { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public int WarehouseId { get; set; }
    public int? PartyId { get; set; }

    /// <summary>معرف (بازاریاب) سند فروش</summary>
    public int? ReferrerId { get; set; }

    public decimal Amount { get; set; }

    // ---------- پرداخت (فقط فروش) ----------

    /// <summary>روش پرداخت: نقدی / نسیه / چک / اقساطی</summary>
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    /// <summary>نوع دریافت نقدی (نقد/کارت‌خوان/کارت به کارت) — برای نقدی یا بخش نقدی پرداخت ترکیبی</summary>
    public CashType? CashType { get; set; }

    /// <summary>پیش‌دریافت نقدی در پرداخت ترکیبی (نسیه/چک/اقساط + مقداری نقد)</summary>
    public decimal CashAmount { get; set; }

    /// <summary>حساب بانکی / صندوق انتخابی برای تسویه</summary>
    public int? TrsAccountId { get; set; }
    public TrsAccount? TrsAccount { get; set; }

    /// <summary>تاریخ سررسید — برای نسیه</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>مبلغ تسویه‌شده تاکنون (نسیه/اقساط)</summary>
    public decimal SettledAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<TransactionLine> Lines { get; set; } = new();
    public List<Cheque> Cheques { get; set; } = new();
    public List<InstallmentLine> Installments { get; set; } = new();
}