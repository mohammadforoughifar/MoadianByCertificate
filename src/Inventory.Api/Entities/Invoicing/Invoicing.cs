using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

// =====================================================================
// موجودیت‌های ماژول فاکتور
//   FacInvoice ← FacInvoiceLine        FacRule (پیکربندی هر نوع فاکتور)
// =====================================================================

/// <summary>فاکتور خرید / فروش / برگشتی</summary>
public class FacInvoice
{
    public int Id { get; set; }

    /// <summary>شماره فاکتور — در هر نوع فاکتور جداگانه شمارش می‌شود</summary>
    public int Number { get; set; }

    [MaxLength(60)]
    public string? RefNumber { get; set; }

    public InvoiceKind Kind { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;
    public DateTime? DueDate { get; set; }

    public int? PartyId { get; set; }
    public Party? Party { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public SettlementType Settlement { get; set; } = SettlementType.Credit;

    [MaxLength(600)]
    public string? Description { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    // ---------- مبالغ ----------
    public decimal TotalGross { get; set; }
    public decimal TotalLineDiscount { get; set; }
    public decimal InvoiceDiscount { get; set; }
    public decimal TotalTaxable { get; set; }
    public decimal TotalVat { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal TotalNet { get; set; }

    // ---------- اسناد خودکار ----------
    /// <summary>سند انبار متناظر (رسید/حواله)</summary>
    public int? InvDocId { get; set; }

    /// <summary>سند حسابداری متناظر</summary>
    public int? VoucherId { get; set; }

    // ---------- ردیابی ----------
    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(120)] public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public List<FacInvoiceLine> Lines { get; set; } = new();
}

/// <summary>یک قلم از فاکتور</summary>
public class FacInvoiceLine
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public FacInvoice? Invoice { get; set; }

    public int RowNo { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>کد مالیاتی کالا در زمان صدور فاکتور</summary>
    [MaxLength(40)]
    public string? TaxCode { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }

    /// <summary>مبلغ پس از تخفیف (مأخذ مالیات) — برای گزارش‌گیری ذخیره می‌شود</summary>
    public decimal Taxable { get; set; }

    /// <summary>جمع سطر = مأخذ + مالیات</summary>
    public decimal Total { get; set; }

    [MaxLength(400)]
    public string? Description { get; set; }
}

/// <summary>پیکربندی هر نوع فاکتور برای صدور خودکار سند انبار و سند حسابداری</summary>
public class FacRule
{
    public int Id { get; set; }

    public InvoiceKind Kind { get; set; }

    /// <summary>نوع سند انبار (رسید یا حواله) که با قطعی شدن فاکتور صادر می‌شود</summary>
    public int? DocTypeId { get; set; }
    public InvDocType? DocType { get; set; }

    public int? PartyAccountId { get; set; }
    public int? MainAccountId { get; set; }
    public int? VatAccountId { get; set; }
    public int? CashAccountId { get; set; }
    public int? ShippingAccountId { get; set; }

    public bool AutoInvDoc { get; set; } = true;
    public bool AutoVoucher { get; set; } = true;
    public bool IsActive { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }
}
