namespace Inventory.Shared.Dtos;

/// <summary>سطر حواله تحویل کالا — فقط مقدار، بدون هیچ مبلغی.</summary>
public class GoodsIssueLineDto
{
    public int Id { get; set; }

    /// <summary>شناسه کالا از فهرست کالاها — اختیاری و معمولاً صفر (کالا به صورت متن آزاد تایپ می شود).</summary>
    public int ProductId { get; set; }

    /// <summary>نام کالا — متن آزاد که کاربر تایپ می کند (بدون انتخاب از فهرست کالاها).</summary>
    public string ProductName { get; set; } = "";

    /// <summary>مقدار — واحد ندارد و ریالی نیست</summary>
    public decimal Quantity { get; set; }
    public string? Note { get; set; }
}

/// <summary>حواله تحویل کالا — سند سبک «فقط مقدار» برای یک مشتری.</summary>
public class GoodsIssueDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? Description { get; set; }
    public List<GoodsIssueLineDto> Lines { get; set; } = new();

    /// <summary>جمع مقدار سطرها</summary>
    public decimal TotalQuantity => Lines.Sum(l => l.Quantity);

    public int LineCount => Lines.Count;
}

/// <summary>درخواست ثبت/ویرایش حواله.</summary>
public class GoodsIssueCommand
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int PartyId { get; set; }
    public string? Description { get; set; }
    public List<GoodsIssueLineDto> Lines { get; set; } = new();
}
