using Inventory.Shared;

namespace Inventory.Shared.Dtos;

/// <summary>خلاصهٔ گزارش اسناد خرید یا فروش در بازهٔ انتخاب‌شده.</summary>
public class OperationsOrderReportDto
{
    public TransactionType Type { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int DocumentCount { get; set; }
    public int LineCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AverageDocumentAmount { get; set; }
    public List<OperationsOrderTrendPoint> Trend { get; set; } = new();
    public List<OperationsPartyReportRow> TopParties { get; set; } = new();
}

/// <summary>یک بازه از روند گزارش خرید/فروش.</summary>
public class OperationsOrderTrendPoint
{
    public DateTime PeriodStart { get; set; }
    public string Label { get; set; } = "";
    public int DocumentCount { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>تجمیع خرید/فروش به تفکیک طرف حساب.</summary>
public class OperationsPartyReportRow
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public int DocumentCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
}

/// <summary>داشبورد یکپارچهٔ عملیات در بازهٔ انتخاب‌شده.</summary>
public class OperationsDashboardDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public int PurchaseCount { get; set; }
    public decimal PurchaseTotal { get; set; }
    public int SaleCount { get; set; }
    public decimal SaleTotal { get; set; }

    /// <summary>درآمد و سود تعمیرات تحویل‌شده؛ برای جلوگیری از نمایش برآوردهای تعمیرات باز.</summary>
    public decimal RepairRevenue { get; set; }
    public decimal RepairCost { get; set; }
    public decimal RepairPartsCost { get; set; }
    public decimal RepairLaborCost { get; set; }
    public decimal RepairProfit { get; set; }
    public int RepairCount { get; set; }
    public int ActiveRepairCount { get; set; }
    public int DeliveredRepairCount { get; set; }
    public int CancelledRepairCount { get; set; }

    /// <summary>وضعیت فعلی پذیرش‌های باز، مستقل از بازهٔ انتخاب‌شده.</summary>
    public int OpenRepairCount { get; set; }
    public int OpenReceivedCount { get; set; }
    public int OpenInProgressCount { get; set; }
    public int OpenReadyCount { get; set; }
    public int OpenAge0To3DaysCount { get; set; }
    public int OpenAge4To7DaysCount { get; set; }
    public int OpenAgeOver7DaysCount { get; set; }

    /// <summary>میانگین زمان واقعی پذیرش تا تحویل، بر حسب روز.</summary>
    public decimal? AverageRepairTurnaroundDays { get; set; }

    /// <summary>رتبه‌بندی معرف‌ها بر اساس مبلغ اسناد فروش در بازهٔ انتخاب‌شده.</summary>
    public List<OperationsReferrerRankDto> TopReferrers { get; set; } = new();
    public OperationsReferrerRankDto? BestReferrer { get; set; }

    public List<OperationsDashboardTrendPoint> Trend { get; set; } = new();
}

/// <summary>مجموعهٔ شاخص‌های یک معرف در گزارش فروش.</summary>
public class OperationsReferrerRankDto
{
    public int ReferrerId { get; set; }
    public string ReferrerName { get; set; } = "";
    public int SaleCount { get; set; }
    public decimal SalesAmount { get; set; }
}

/// <summary>نقطهٔ نمودار خرید/فروش و روند درآمد، هزینه و سود تعمیرات.</summary>
public class OperationsDashboardTrendPoint
{
    public DateTime PeriodStart { get; set; }
    public string Label { get; set; } = "";
    public decimal Purchase { get; set; }
    public decimal Sale { get; set; }
    public decimal RepairRevenue { get; set; }
    public decimal RepairCost { get; set; }
    public decimal RepairProfit { get; set; }
    public decimal RepairPartsCost { get; set; }
    public decimal RepairLaborCost { get; set; }
}
