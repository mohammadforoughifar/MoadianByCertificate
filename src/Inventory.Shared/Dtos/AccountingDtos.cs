namespace Inventory.Shared.Dtos;

// =====================================================================
// DTO های ماژول حسابداری
//   ۱) سال مالی            ۲) کدینگ حساب‌ها (درختی: کل ← معین ← تفصیلی)
//   ۳) سند حسابداری        ۴) دفاتر (روزنامه، کل، معین)
//   ۵) تراز آزمایشی        ۶) قواعد سند خودکار انبار
// همه‌ی نام‌ها با پیشوند Acc تا با DTOهای دیگر تداخل نکنند.
// =====================================================================

// ============================ ۱) سال مالی ============================

/// <summary>سال (دوره) مالی</summary>
public class AccFiscalYear
{
    public int Id { get; set; }

    /// <summary>عنوان دوره — مثلاً «سال مالی ۱۴۰۴»</summary>
    public string Title { get; set; } = "";

    /// <summary>کد دوره (دو رقم آخر سال یا شماره دلخواه)</summary>
    public string? Code { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>دوره جاری سیستم — اسناد جدید در این دوره ثبت می‌شوند</summary>
    public bool IsCurrent { get; set; }

    /// <summary>بسته شده — امکان ثبت یا ویرایش سند وجود ندارد</summary>
    public bool IsClosed { get; set; }

    public string? Description { get; set; }

    // ---------- محاسباتی ----------
    public int VoucherCount { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
}

// ============================ ۲) حساب ============================

/// <summary>یک گره از درخت کدینگ حساب‌ها (گروه / کل / معین / تفصیلی)</summary>
public class AccAccount
{
    public int Id { get; set; }

    /// <summary>کد حساب — یکتا. مثال: 1، 11، 1101، 110101</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>نام لاتین (اختیاری — برای گزارش‌های دوزبانه)</summary>
    public string? EnName { get; set; }

    public int? ParentId { get; set; }
    public string? ParentName { get; set; }

    /// <summary>سطح حساب: گروه / کل / معین / تفصیلی</summary>
    public AccountLevel Level { get; set; } = AccountLevel.Group;

    /// <summary>نوع حساب: دارایی، بدهی، سرمایه، درآمد، هزینه</summary>
    public AccountType Type { get; set; } = AccountType.Asset;

    /// <summary>ماهیت حساب: بدهکار، بستانکار، دوطرفه</summary>
    public AccountNature Nature { get; set; } = AccountNature.Debit;

    /// <summary>حساب دائم (ترازنامه‌ای) است؟ حساب‌های موقت در پایان دوره بسته می‌شوند</summary>
    public bool IsPermanent { get; set; } = true;

    /// <summary>فقط روی حساب‌های «قابل ثبت» می‌توان سند زد (معمولاً برگ‌های درخت)</summary>
    public bool IsPostable { get; set; }

    /// <summary>الزام ثبت طرف حساب روی سطر سند</summary>
    public bool RequiresParty { get; set; }

    /// <summary>حساب سیستمی — قابل حذف نیست (مثلاً موجودی کالا، خرید، فروش)</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Description { get; set; }

    // ---------- محاسباتی ----------
    public int Depth { get; set; }
    public string FullPath { get; set; } = "";
    public int ChildCount { get; set; }

    /// <summary>گردش و مانده در دوره‌ی جاری (با احتساب زیرمجموعه‌ها)</summary>
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance => Debit - Credit;

    /// <summary>نمایش مانده به‌همراه تشخیص (بد/بس)</summary>
    public string BalanceSide => Balance > 0 ? "بد" : Balance < 0 ? "بس" : "—";

    public List<AccAccount> Children { get; set; } = new();

    public string LevelTitle => Level switch
    {
        AccountLevel.Group => "گروه",
        AccountLevel.General => "کل",
        AccountLevel.Subsidiary => "معین",
        _ => "تفصیلی"
    };

    public string TypeTitle => Type switch
    {
        AccountType.Asset => "دارایی",
        AccountType.Liability => "بدهی",
        AccountType.Equity => "سرمایه",
        AccountType.Income => "درآمد",
        _ => "هزینه"
    };

    public string NatureTitle => Nature switch
    {
        AccountNature.Debit => "بدهکار",
        AccountNature.Credit => "بستانکار",
        _ => "دوطرفه"
    };
}

/// <summary>جابه‌جایی حساب در درخت</summary>
public class AccAccountMove
{
    public int Id { get; set; }
    public int? NewParentId { get; set; }
    public int SortOrder { get; set; }
}

// ============================ ۳) سند حسابداری ============================

/// <summary>یک سطر (آرتیکل) سند حسابداری</summary>
public class AccVoucherLine
{
    public int Id { get; set; }
    public int RowNo { get; set; }

    public int AccountId { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";

    /// <summary>طرف حساب (مشتری/تأمین‌کننده) — اختیاری</summary>
    public int? PartyId { get; set; }
    public string? PartyName { get; set; }

    /// <summary>مرکز هزینه / پروژه — اختیاری</summary>
    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }

    /// <summary>بُعد تحلیلی (مرکز هزینه/شعبه) — اختیاری</summary>
    public int? DimensionValueId { get; set; }
    public string? DimensionValueName { get; set; }

    public string? Description { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    /// <summary>شماره پیگیری/چک/فاکتور مرتبط با این آرتیکل</summary>
    public string? RefNumber { get; set; }
}

/// <summary>سند حسابداری</summary>
public class AccVoucher
{
    public int Id { get; set; }

    /// <summary>شماره سند در دوره مالی</summary>
    public int Number { get; set; }

    /// <summary>شماره عطف/پیگیری (اختیاری)</summary>
    public string? RefNumber { get; set; }

    public int FiscalYearId { get; set; }
    public string? FiscalYearTitle { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;
    public string? Description { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Draft;

    /// <summary>منشأ سند: دستی، سند انبار، فاکتور، افتتاحیه، اختتامیه</summary>
    public VoucherSource Source { get; set; } = VoucherSource.Manual;

    /// <summary>شناسه سند مبدأ (مثلاً InvDoc.Id) برای اسناد خودکار</summary>
    public int? SourceId { get; set; }

    /// <summary>عنوان خوانا از سند مبدأ — «رسید خرید RC-1404-0007»</summary>
    public string? SourceTitle { get; set; }

    public List<AccVoucherLine> Lines { get; set; } = new();

    // ---------- محاسباتی ----------
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal Difference => TotalDebit - TotalCredit;
    public bool IsBalanced => Difference == 0;
    public int LineCount { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public string StatusTitle => Status switch
    {
        VoucherStatus.Draft => "پیش‌نویس",
        VoucherStatus.Confirmed => "قطعی",
        _ => "ابطال شده"
    };

    public string SourceTitleFa => Source switch
    {
        VoucherSource.Manual => "دستی",
        VoucherSource.InventoryDoc => "سند انبار",
        VoucherSource.Invoice => "فاکتور",
        VoucherSource.Opening => "افتتاحیه",
        _ => "اختتامیه"
    };
}

// ============================ ۴) دفاتر ============================

/// <summary>یک سطر دفتر (روزنامه / کل / معین)</summary>
public class AccLedgerRow
{
    public DateTime Date { get; set; }
    public int VoucherId { get; set; }
    public int VoucherNumber { get; set; }
    public int RowNo { get; set; }

    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string? PartyName { get; set; }
    public string? DimensionValueName { get; set; }
    public string? Description { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    /// <summary>مانده تجمعی</summary>
    public decimal Balance { get; set; }

    public string BalanceSide => Balance > 0 ? "بد" : Balance < 0 ? "بس" : "—";
}

/// <summary>خروجی کامل گزارش دفتر یک حساب</summary>
public class AccLedgerResult
{
    public int AccountId { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string LevelTitle { get; set; } = "";

    /// <summary>مانده ابتدای بازه</summary>
    public decimal OpeningBalance { get; set; }

    public List<AccLedgerRow> Rows { get; set; } = new();

    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal ClosingBalance { get; set; }
}

// ============================ ۵) تراز آزمایشی ============================

/// <summary>یک سطر تراز آزمایشی (۴ / ۶ / ۸ ستونی)</summary>
public class AccTrialBalanceRow
{
    public int AccountId { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public AccountLevel Level { get; set; }
    public int Depth { get; set; }

    /// <summary>مانده ابتدای دوره</summary>
    public decimal OpeningDebit { get; set; }
    public decimal OpeningCredit { get; set; }

    /// <summary>گردش طی دوره</summary>
    public decimal PeriodDebit { get; set; }
    public decimal PeriodCredit { get; set; }

    /// <summary>گردش تجمعی (ابتدای دوره + طی دوره)</summary>
    public decimal TotalDebit => OpeningDebit + PeriodDebit;
    public decimal TotalCredit => OpeningCredit + PeriodCredit;

    /// <summary>مانده پایان دوره</summary>
    public decimal ClosingDebit { get; set; }
    public decimal ClosingCredit { get; set; }
}

/// <summary>خروجی گزارش تراز آزمایشی</summary>
public class AccTrialBalanceResult
{
    public AccountLevel Level { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public List<AccTrialBalanceRow> Rows { get; set; } = new();

    public decimal SumOpeningDebit { get; set; }
    public decimal SumOpeningCredit { get; set; }
    public decimal SumPeriodDebit { get; set; }
    public decimal SumPeriodCredit { get; set; }
    public decimal SumClosingDebit { get; set; }
    public decimal SumClosingCredit { get; set; }

    /// <summary>آیا تراز است؟ (جمع بدهکار = جمع بستانکار)</summary>
    public bool IsBalanced => SumClosingDebit == SumClosingCredit && SumPeriodDebit == SumPeriodCredit;
}

// ============================ ۶) قواعد سند خودکار ============================

/// <summary>
/// نگاشت «نوع سند انبار» به حساب‌ها؛ با قطعی شدن سند انبار،
/// سند حسابداری متناظر به‌صورت خودکار صادر می‌شود.
/// </summary>
public class AccInvRule
{
    public int Id { get; set; }

    public int DocTypeId { get; set; }
    public string DocTypeName { get; set; } = "";
    public StockNature Nature { get; set; }

    /// <summary>حساب موجودی کالا (معمولاً بدهکار در رسید، بستانکار در حواله)</summary>
    public int? InventoryAccountId { get; set; }
    public string? InventoryAccountName { get; set; }

    /// <summary>حساب طرف مقابل (خرید، فروش، بهای تمام‌شده، ضایعات، …)</summary>
    public int? CounterAccountId { get; set; }
    public string? CounterAccountName { get; set; }

    /// <summary>مبنای مبلغ سند: بهای تمام‌شده یا مبلغ سطرهای سند</summary>
    public bool UseCostValue { get; set; } = true;

    /// <summary>صدور خودکار فعال است؟</summary>
    public bool IsActive { get; set; } = true;

    public string? Description { get; set; }
}

// ============================ خلاصه داشبورد ============================

/// <summary>خلاصه وضعیت مالی برای داشبورد ERP</summary>
public class AccDashboard
{
    public string? FiscalYearTitle { get; set; }
    public int VoucherCount { get; set; }
    public int DraftCount { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }

    public decimal AssetTotal { get; set; }
    public decimal LiabilityTotal { get; set; }
    public decimal EquityTotal { get; set; }
    public decimal IncomeTotal { get; set; }
    public decimal ExpenseTotal { get; set; }

    /// <summary>سود (زیان) دوره = درآمد − هزینه</summary>
    public decimal Profit => IncomeTotal - ExpenseTotal;

    public bool IsBalanced => TotalDebit == TotalCredit;
}

// ============================ ۶) ابعاد تحلیلی ============================

/// <summary>بُعد تحلیلی — مرکز هزینه / شعبه</summary>
public class AccDimension
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Description { get; set; }
    public int ValueCount { get; set; }
}

/// <summary>مقدار یک بُعد (نشان می‌دهد در کدام مرکز/شعبه)</summary>
public class AccDimensionValue
{
    public int Id { get; set; }
    public int DimensionId { get; set; }
    public string? DimensionName { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public string? ParentName { get; set; }
    public string CodeTree { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Description { get; set; }
}

// ============================ ۷) دارایی ثابت ============================

/// <summary>گروه دارایی ثابت</summary>
public class FixedAssetCategory
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int DefaultUsefulLifeMonths { get; set; } = 60;
    public DepreciationMethod DefaultMethod { get; set; } = DepreciationMethod.StraightLine;
    public decimal DefaultResidualPercent { get; set; } = 0;
    public int? DepreciationAccAccountId { get; set; }
    public int? ExpenseAccAccountId { get; set; }
    public int? AccumulatedAccAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Description { get; set; }
    public int AssetCount { get; set; }
}

/// <summary>دارایی ثابت</summary>
public class FixedAsset
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? EnName { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? Location { get; set; }
    public string? Vendor { get; set; }
    public string? SerialNo { get; set; }
    public DateTime PurchaseDate { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SalvageValue { get; set; }
    public int UsefulLifeMonths { get; set; } = 60;
    public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;
    public FixedAssetStatus Status { get; set; } = FixedAssetStatus.Active;
    public int? DimensionValueId { get; set; }
    public string? DimensionValueName { get; set; }
    public int? AssetAccAccountId { get; set; }
    public int? ExpenseAccAccountId { get; set; }
    public int? AccumulatedAccAccountId { get; set; }
    public decimal AccumulatedDepreciation { get; set; }
    public decimal MonthlyDepreciation => UsefulLifeMonths <= 0 ? 0 : Math.Round((PurchasePrice - SalvageValue) / UsefulLifeMonths, 2);
    public decimal BookValue => PurchasePrice - AccumulatedDepreciation;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>اجرای استهلاک ماهانه</summary>
public class FixedAssetDepreciationRun
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime RunDate { get; set; }
    public decimal TotalAmount { get; set; }
    public int? VoucherId { get; set; }
    public bool IsPosted { get; set; }
    public string? CreatedBy { get; set; }
    public string? Notes { get; set; }
    public List<FixedAssetDepreciationLine> Lines { get; set; } = new();
}

/// <summary>سطر استهلاک</summary>
public class FixedAssetDepreciationLine
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public int AssetId { get; set; }
    public string? AssetCode { get; set; }
    public string? AssetName { get; set; }
    public decimal Amount { get; set; }
    public decimal AccumulatedAfter { get; set; }
    public decimal BookValueAfter { get; set; }
}

/// <summary>درخواست اجرای استهلاک</summary>
public class FixedAssetDepreciationRequest
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string? Notes { get; set; }
}

// ============================ ۸) بودجه و کنترل بودجه ============================

/// <summary>بودجه</summary>
public class Budget
{
    public int Id { get; set; }
    public int FiscalYearId { get; set; }
    public string? FiscalYearTitle { get; set; }
    public int? DimensionValueId { get; set; }
    public string? DimensionValueName { get; set; }
    public string Name { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public decimal CommittedAmount { get; set; }
    public decimal ActualAmount { get; set; }
    public decimal Available => TotalAmount - CommittedAmount - ActualAmount;
    public decimal UsedPercent => TotalAmount <= 0 ? 0 : Math.Round((CommittedAmount + ActualAmount) * 100m / TotalAmount, 1);
    public bool IsMaster { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public List<BudgetItem> Items { get; set; } = new();
}

/// <summary>قلم بودجه</summary>
public class BudgetItem
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public int AccAccountId { get; set; }
    public string? AccAccountCode { get; set; }
    public string? AccAccountName { get; set; }
    public decimal PlannedAmount { get; set; }
    public decimal CommittedAmount { get; set; }
    public decimal ActualAmount { get; set; }
    public decimal Available => PlannedAmount - CommittedAmount - ActualAmount;
}

/// <summary>رویداد بودجه (تعهد/مصرف)</summary>
public class BudgetTransaction
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public string? BudgetName { get; set; }
    public int BudgetItemId { get; set; }
    public BudgetTransactionType Type { get; set; }
    public int? VoucherId { get; set; }
    public int? SourceId { get; set; }
    public string? SourceTitle { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }
}

/// <summary>داشبورد بودجه</summary>
public class BudgetDashboard
{
    public int BudgetCount { get; set; }
    public decimal TotalPlanned { get; set; }
    public decimal TotalCommitted { get; set; }
    public decimal TotalActual { get; set; }

    /// <summary>بودجه‌های در آستانه‌ی اتمام (بالای ۸۰٪ مصرف)</summary>
    public List<Budget> NearLimit { get; set; } = new();

    /// <summary>بودجه‌های تجاوز کرده</summary>
    public List<Budget> OverLimit { get; set; } = new();
}
