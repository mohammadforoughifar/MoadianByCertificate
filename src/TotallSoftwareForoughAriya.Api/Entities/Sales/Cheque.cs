using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>چک دریافتی بابت سند فروش</summary>
public class Cheque : ICompanyOwned
{
    public int Id { get; set; }
    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    public int TransactionId { get; set; }

    /// <summary>شماره چک / صیاد</summary>
    [MaxLength(50)]
    public string Number { get; set; } = "";

    /// <summary>نام بانک</summary>
    [MaxLength(100)]
    public string? BankName { get; set; }

    /// <summary>حساب بانکی متناظر در سامانه خزانه (برای چک‌های صادره یا واریزی)</summary>
    public int? TrsAccountId { get; set; }
    public TrsAccount? TrsAccount { get; set; }

    /// <summary>شماره حساب / شعبه</summary>
    [MaxLength(100)]
    public string? AccountInfo { get; set; }

    /// <summary>صاحب چک</summary>
    [MaxLength(150)]
    public string? OwnerName { get; set; }

    public decimal Amount { get; set; }

    /// <summary>تاریخ سررسید چک</summary>
    public DateTime DueDate { get; set; }

    /// <summary>پاس شده؟</summary>
    public bool IsCleared { get; set; }

    /// <summary>تاریخ پاس شدن</summary>
    public DateTime? ClearedAt { get; set; }

    /// <summary>وضعیت چک: در جریان / وصول‌شده / برگشتی</summary>
    public ChequeStatus Status { get; set; } = ChequeStatus.InHand;

    /// <summary>تاریخ برگشت چک</summary>
    public DateTime? BouncedAt { get; set; }

    /// <summary>علت برگشت چک</summary>
    [MaxLength(200)]
    public string? BounceReason { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}