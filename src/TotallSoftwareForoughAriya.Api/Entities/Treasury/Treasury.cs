using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

// =====================================================================
// موجودیت‌های ماژول خزانه‌داری
//   TrsAccount      — صندوق / بانک / کارتخوان / تنخواه
//   TrsVoucher      — سند دریافت، پرداخت یا انتقال
//   TrsVoucherLine  — سطر (ابزار) سند خزانه
//   TrsCheque       — برگ چک دریافتی یا پرداختی
//   TrsChequeAction — تاریخچه عملیات چک
//   TrsRule         — پیکربندی حساب‌های سند خودکار خزانه
// =====================================================================

/// <summary>صندوق، حساب بانکی، کارتخوان یا تنخواه‌گردان</summary>
public class TrsAccount
{
    public int Id { get; set; }

    [MaxLength(40)] public string Code { get; set; } = "";
    [MaxLength(200)] public string Name { get; set; } = "";

    public TreasuryAccountKind Kind { get; set; } = TreasuryAccountKind.Cash;

    /// <summary>حساب متناظر در کدینگ حسابداری</summary>
    public int? AccountId { get; set; }
    public AccAccount? Account { get; set; }

    [MaxLength(120)] public string? BankName { get; set; }
    [MaxLength(120)] public string? BranchName { get; set; }
    [MaxLength(30)] public string? BranchCode { get; set; }
    [MaxLength(40)] public string? AccountNumber { get; set; }
    [MaxLength(34)] public string? Iban { get; set; }
    [MaxLength(20)] public string? CardNumber { get; set; }

    public decimal OpeningBalance { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    [MaxLength(500)] public string? Description { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>سند دریافت / پرداخت / انتقال بین حساب‌ها</summary>
public class TrsVoucher
{
    public int Id { get; set; }

    /// <summary>شماره سند در هر نوع (دریافت/پرداخت/انتقال) به‌صورت مستقل</summary>
    public int Number { get; set; }

    public TreasuryKind Kind { get; set; } = TreasuryKind.Receipt;

    public DateTime Date { get; set; } = DateTime.Now;

    public int? PartyId { get; set; }
    public Party? Party { get; set; }

    [MaxLength(600)] public string? Description { get; set; }
    [MaxLength(60)] public string? RefNumber { get; set; }

    public TreasuryStatus Status { get; set; } = TreasuryStatus.Draft;

    // ---------- انتقال بین حساب‌ها ----------
    public int? FromAccountId { get; set; }
    public TrsAccount? FromAccount { get; set; }

    public int? ToAccountId { get; set; }
    public TrsAccount? ToAccount { get; set; }

    /// <summary>کارمزد انتقال</summary>
    public decimal FeeAmount { get; set; }

    /// <summary>فاکتور تسویه‌شده با این سند (اختیاری)</summary>
    public int? InvoiceId { get; set; }

    // ---------- مبالغ ----------
    public decimal TotalAmount { get; set; }
    public decimal CashAmount { get; set; }
    public decimal ChequeAmount { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>سند حسابداری صادرشده</summary>
    public int? VoucherId { get; set; }

    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(120)] public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public List<TrsVoucherLine> Lines { get; set; } = new();

    /// <summary>تخصیص مبلغ سند به یک یا چند فاکتور؛ برای تسویه کامل یا جزئی</summary>
    public List<TrsInvoiceAllocation> InvoiceAllocations { get; set; } = new();
}

/// <summary>مبلغ تخصیص‌یافته از یک سند دریافت/پرداخت به یک فاکتور</summary>
public class TrsInvoiceAllocation
{
    public int Id { get; set; }

    public int TrsVoucherId { get; set; }
    public TrsVoucher? TrsVoucher { get; set; }

    public int InvoiceId { get; set; }
    public FacInvoice? Invoice { get; set; }

    /// <summary>مبلغی از بدهی فاکتور که با این سند تسویه می‌شود</summary>
    public decimal Amount { get; set; }
}

/// <summary>سطر (ابزار) سند خزانه</summary>
public class TrsVoucherLine
{
    public int Id { get; set; }

    public int TrsVoucherId { get; set; }
    public TrsVoucher? TrsVoucher { get; set; }

    public int RowNo { get; set; }

    public PayMethod Method { get; set; } = PayMethod.Cash;

    /// <summary>صندوق/بانک این سطر (برای نقد، کارت و حواله)</summary>
    public int? TrsAccountId { get; set; }
    public TrsAccount? TrsAccount { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(60)] public string? RefNumber { get; set; }
    [MaxLength(500)] public string? Description { get; set; }

    /// <summary>چک متناظر (وقتی Method = Cheque)</summary>
    public int? ChequeId { get; set; }
    public TrsCheque? Cheque { get; set; }
}

/// <summary>برگ چک دریافتی یا پرداختی</summary>
public class TrsCheque
{
    public int Id { get; set; }

    public ChequeKind Kind { get; set; } = ChequeKind.Received;

    [MaxLength(40)] public string Number { get; set; } = "";

    /// <summary>شناسه صیاد (۱۶ رقمی)</summary>
    [MaxLength(20)] public string? SayadId { get; set; }

    public decimal Amount { get; set; }

    public DateTime IssueDate { get; set; } = DateTime.Now;
    public DateTime DueDate { get; set; } = DateTime.Now;

    [MaxLength(120)] public string? BankName { get; set; }
    [MaxLength(120)] public string? BranchName { get; set; }
    [MaxLength(40)] public string? AccountNumber { get; set; }
    [MaxLength(200)] public string? OwnerName { get; set; }

    public int? PartyId { get; set; }
    public Party? Party { get; set; }

    /// <summary>بانک واگذاری (دریافتی) یا حساب صادرکننده (پرداختی)</summary>
    public int? TrsAccountId { get; set; }
    public TrsAccount? TrsAccount { get; set; }

    public ChequeStatus Status { get; set; } = ChequeStatus.InHand;
    public DateTime? StatusDate { get; set; }

    [MaxLength(500)] public string? Description { get; set; }

    /// <summary>سند خزانه‌ای که چک در آن ثبت شده است</summary>
    public int? TrsVoucherId { get; set; }

    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<TrsChequeAction> Actions { get; set; } = new();
}

/// <summary>تاریخچه یک تغییر وضعیت روی چک</summary>
public class TrsChequeAction
{
    public int Id { get; set; }

    public int ChequeId { get; set; }
    public TrsCheque? Cheque { get; set; }

    public ChequeStatus Status { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;

    public int? TrsAccountId { get; set; }

    [MaxLength(500)] public string? Description { get; set; }

    /// <summary>سند حسابداری این عملیات</summary>
    public int? VoucherId { get; set; }

    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>پیکربندی حساب‌های سند خودکار خزانه (یک رکورد برای دریافت، یکی برای پرداخت)</summary>
public class TrsRule
{
    public int Id { get; set; }

    public TreasuryKind Kind { get; set; }

    /// <summary>حساب‌های دریافتنی / پرداختنی تجاری</summary>
    public int? PartyAccountId { get; set; }

    /// <summary>اسناد دریافتنی / اسناد پرداختنی</summary>
    public int? ChequeAccountId { get; set; }

    /// <summary>اسناد در جریان وصول</summary>
    public int? CollectionAccountId { get; set; }

    /// <summary>تخفیفات نقدی</summary>
    public int? DiscountAccountId { get; set; }

    /// <summary>کارمزد بانکی</summary>
    public int? FeeAccountId { get; set; }

    public bool AutoVoucher { get; set; } = true;
    public bool IsActive { get; set; }

    [MaxLength(500)] public string? Description { get; set; }
}
