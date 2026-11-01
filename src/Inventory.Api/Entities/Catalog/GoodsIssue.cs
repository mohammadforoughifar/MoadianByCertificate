using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// حواله تحویل کالا — سند سبک «فقط مقدار» (بدون هیچ مبلغ/ریالی).
/// <para>
/// برای ثبت تحویل کالا به مشتری در جایی که قیمت وارد نمی‌شود: یک مشتری + چند سطر
/// کالا با مقدار. برخلاف رسید/حواله انبار (<see cref="Transaction"/>) این سند
/// هیچ اثری روی موجودی، بهای تمام‌شده و حسابداری ندارد — صرفاً سابقهٔ تحویل است.
/// </para>
/// </summary>
public class GoodsIssue : ICompanyOwned
{
    public int Id { get; set; }

    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    /// <summary>شماره حواله — الگوی <c>HI-1405-0001</c> (سال شمسی + شمارهٔ متوالی)</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    public DateTime Date { get; set; }

    /// <summary>مشتری (طرف حساب با نوع Customer)</summary>
    public int PartyId { get; set; }

    /// <summary>نام مشتری در لحظهٔ ثبت — snapshot تا تغییر نام در طرف حساب، سابقه را عوض نکند</summary>
    [MaxLength(200)]
    public string PartyName { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<GoodsIssueLine> Lines { get; set; } = new();

    /// <summary>جمع مقدار همه سطرها — فقط برای نمایش در فهرست</summary>
    public decimal TotalQuantity => Lines.Sum(l => l.Quantity);
}

/// <summary>سطر حواله: یک کالا با مقدار — بدون قیمت.</summary>
public class GoodsIssueLine : ICompanyOwned
{
    public int Id { get; set; }
    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    public int IssueId { get; set; }

    public int ProductId { get; set; }

    /// <summary>نام کالا در لحظهٔ ثبت — snapshot</summary>
    [MaxLength(200)]
    public string ProductName { get; set; } = "";

    /// <summary>مقدار — بدون هیچ واحد پولی</summary>
    public decimal Quantity { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }
}
