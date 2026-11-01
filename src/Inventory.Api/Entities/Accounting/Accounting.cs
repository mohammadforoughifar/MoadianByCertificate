using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

// =====================================================================
// موجودیت‌های ماژول حسابداری
//  • سال مالی
//  • درخت کدینگ حساب‌ها (گروه ← کل ← معین ← تفصیلی)
//  • سند حسابداری و آرتیکل‌های آن
//  • قاعده‌ی صدور خودکار سند از روی اسناد انبار
// =====================================================================

/// <summary>سال (دوره) مالی</summary>
public class AccFiscalYear
{
    public int Id { get; set; }

    [MaxLength(120)] public string Title { get; set; } = "";
    [MaxLength(20)] public string? Code { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>دوره جاری — اسناد جدید اینجا ثبت می‌شوند</summary>
    public bool IsCurrent { get; set; }

    /// <summary>بسته شده — ثبت/ویرایش سند ممنوع</summary>
    public bool IsClosed { get; set; }

    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>گره درخت کدینگ حساب‌ها</summary>
public class AccAccount
{
    public int Id { get; set; }

    [MaxLength(30)] public string Code { get; set; } = "";
    [MaxLength(180)] public string Name { get; set; } = "";
    [MaxLength(180)] public string? EnName { get; set; }

    public int? ParentId { get; set; }
    public AccAccount? Parent { get; set; }

    public AccountLevel Level { get; set; } = AccountLevel.Group;
    public AccountType Type { get; set; } = AccountType.Asset;
    public AccountNature Nature { get; set; } = AccountNature.Debit;

    /// <summary>حساب دائم (ترازنامه‌ای)؟ حساب‌های موقت در اختتامیه بسته می‌شوند</summary>
    public bool IsPermanent { get; set; } = true;

    /// <summary>قابل ثبت سند (معمولاً برگ درخت)</summary>
    public bool IsPostable { get; set; }

    /// <summary>ثبت طرف حساب روی آرتیکل الزامی است</summary>
    public bool RequiresParty { get; set; }

    /// <summary>حساب سیستمی — قابل حذف نیست</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>سند حسابداری</summary>
public class AccVoucher
{
    public int Id { get; set; }

    /// <summary>شماره سند در دوره مالی</summary>
    public int Number { get; set; }

    [MaxLength(60)] public string? RefNumber { get; set; }

    public int FiscalYearId { get; set; }
    public AccFiscalYear? FiscalYear { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;
    [MaxLength(600)] public string? Description { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Draft;
    public VoucherSource Source { get; set; } = VoucherSource.Manual;

    /// <summary>شناسه سند مبدأ (مثلاً InvDoc.Id)</summary>
    public int? SourceId { get; set; }
    [MaxLength(200)] public string? SourceTitle { get; set; }

    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }

    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(120)] public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public List<AccVoucherLine> Lines { get; set; } = new();
}

/// <summary>آرتیکل (سطر) سند حسابداری</summary>
public class AccVoucherLine
{
    public int Id { get; set; }

    public int VoucherId { get; set; }
    public AccVoucher? Voucher { get; set; }

    public int RowNo { get; set; }

    public int AccountId { get; set; }
    public AccAccount? Account { get; set; }

    public int? PartyId { get; set; }
    public int? ProjectId { get; set; }

    /// <summary>بُعد تحلیلی (مرکز هزینه/شعبه) — برای گزارش‌های تحلیلی</summary>
    public int? DimensionValueId { get; set; }
    public AccDimensionValue? DimensionValue { get; set; }

    [MaxLength(600)] public string? Description { get; set; }
    [MaxLength(60)] public string? RefNumber { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

/// <summary>قاعده‌ی صدور خودکار سند حسابداری از روی نوع سند انبار</summary>
public class AccInvRule
{
    public int Id { get; set; }

    public int DocTypeId { get; set; }
    public InvDocType? DocType { get; set; }

    /// <summary>حساب موجودی کالا</summary>
    public int? InventoryAccountId { get; set; }

    /// <summary>حساب طرف مقابل (خرید، فروش، بهای تمام‌شده، ضایعات، …)</summary>
    public int? CounterAccountId { get; set; }

    /// <summary>مبنای مبلغ: بهای تمام‌شده (true) یا مبلغ سطرهای سند (false)</summary>
    public bool UseCostValue { get; set; } = true;

    public bool IsActive { get; set; } = true;
    [MaxLength(500)] public string? Description { get; set; }
}

// =====================================================================
// ابعاد تحلیلی (مراکز هزینه، پروژه، شعبه) — ماژول حسابداری تحلیلی
// =====================================================================

/// <summary>بُعد تحلیلی — مثلاً «مرکز هزینه»، «پروژه»، «شعبه»</summary>
public class AccDimension
{
    public int Id { get; set; }

    [MaxLength(30)] public string Code { get; set; } = "";
    [MaxLength(180)] public string Name { get; set; } = "";

    /// <summary>بُعد سیستمی (پروژه/شعبه) — پیش‌فرض ساخته می‌شود</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<AccDimensionValue> Values { get; set; } = new();
}

/// <summary>مقدار یک بُعد — مثلاً «هزینه جاری»، «پروژه پل ۱۲»، «شعبه تهران»</summary>
public class AccDimensionValue
{
    public int Id { get; set; }

    public int DimensionId { get; set; }
    public AccDimension? Dimension { get; set; }

    [MaxLength(40)] public string Code { get; set; } = "";
    [MaxLength(180)] public string Name { get; set; } = "";

    public int? ParentId { get; set; }
    public AccDimensionValue? Parent { get; set; }

    /// <summary>مسیر کامل کد — مثال: 10101 (مرکز ← واحد ← زیرواحد)</summary>
    [MaxLength(120)] public string CodeTree { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
}

// =====================================================================
// دارایی ثابت و استهلاک
// =====================================================================

/// <summary>گروه دارایی ثابت — مثلاً «تجهیزات اداری»، «خودرو»، «ماشین‌آلات»</summary>
public class FixedAssetCategory
{
    public int Id { get; set; }

    [MaxLength(30)] public string Code { get; set; } = "";
    [MaxLength(180)] public string Name { get; set; } = "";

    /// <summary>عمر مفید پیش‌فرض (ماه)</summary>
    public int DefaultUsefulLifeMonths { get; set; } = 60;

    /// <summary>روش استهلاک پیش‌فرض</summary>
    public DepreciationMethod DefaultMethod { get; set; } = DepreciationMethod.StraightLine;

    /// <summary>درصد باقیمانده (ارزش اسقاط) — 0 تا 20</summary>
    public decimal DefaultResidualPercent { get; set; } = 0;

    /// <summary>حساب حسابداری استهلاک این گروه</summary>
    public int? DepreciationAccAccountId { get; set; }

    /// <summary>حساب هزینه استهلاک این گروه</summary>
    public int? ExpenseAccAccountId { get; set; }

    /// <summary>حساب جمع استهلاک (ذخیره) این گروه</summary>
    public int? AccumulatedAccAccountId { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
}

/// <summary>دارایی ثابت</summary>
public class FixedAsset
{
    public int Id { get; set; }

    [MaxLength(40)] public string Code { get; set; } = "";
    [MaxLength(180)] public string Name { get; set; } = "";
    [MaxLength(180)] public string? EnName { get; set; }

    public int CategoryId { get; set; }
    public FixedAssetCategory? Category { get; set; }

    /// <summary>محل استقرار / نگهداری</summary>
    [MaxLength(200)] public string? Location { get; set; }

    /// <summary>تأمین‌کننده / فروشنده</summary>
    [MaxLength(180)] public string? Vendor { get; set; }

    /// <summary>شماره سریال / پلاک / پلاک خودرو</summary>
    [MaxLength(120)] public string? SerialNo { get; set; }

    public DateTime PurchaseDate { get; set; }
    public decimal PurchasePrice { get; set; }

    /// <summary>ارزش اسقاط (در پایان عمر مفید)</summary>
    public decimal SalvageValue { get; set; }

    /// <summary>عمر مفید به ماه</summary>
    public int UsefulLifeMonths { get; set; } = 60;

    public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;

    public FixedAssetStatus Status { get; set; } = FixedAssetStatus.Active;

    /// <summary>بُعد تحلیلی (مرکز هزینه/پروژه/شعبه) — برای شارژ هزینه استهلاک</summary>
    public int? DimensionValueId { get; set; }
    public AccDimensionValue? DimensionValue { get; set; }

    /// <summary>حساب ملکیت (دارایی) این دارایی — برای سند استهلاک</summary>
    public int? AssetAccAccountId { get; set; }

    /// <summary>حساب هزینه استهلاک (برای سند استهلاک)</summary>
    public int? ExpenseAccAccountId { get; set; }

    /// <summary>حساب جمع استهلاک/ذخیره (برای سند استهلاک)</summary>
    public int? AccumulatedAccAccountId { get; set; }

    /// <summary>جمع استهلاک انباشته تا لحظه‌ی آخرین اجرا</summary>
    public decimal AccumulatedDepreciation { get; set; }

    public bool IsActive { get; set; } = true;
    [MaxLength(500)] public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>اجرای استهلاک (ماهانه)</summary>
public class FixedAssetDepreciationRun
{
    public int Id { get; set; }

    /// <summary>سال مالی / سال شمسی اجرا</summary>
    public int Year { get; set; }

    /// <summary>ماه شمسی اجرا (۱ تا ۱۲)</summary>
    public int Month { get; set; }

    public DateTime RunDate { get; set; } = DateTime.Now;

    /// <summary>جمع استهلاک دوره</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>سند حسابداری صادرشده برای این اجرا</summary>
    public int? VoucherId { get; set; }

    /// <summary>استهلاک به حسابداری منتقل شد؟</summary>
    public bool IsPosted { get; set; }

    [MaxLength(120)] public string? CreatedBy { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }

    public List<FixedAssetDepreciationLine> Lines { get; set; } = new();
}

/// <summary>سطر استهلاک یک دارایی در یک اجرا</summary>
public class FixedAssetDepreciationLine
{
    public int Id { get; set; }

    public int RunId { get; set; }
    public FixedAssetDepreciationRun? Run { get; set; }

    public int AssetId { get; set; }
    public FixedAsset? Asset { get; set; }

    /// <summary>مبلغ استهلاک این دوره</summary>
    public decimal Amount { get; set; }

    /// <summary>استهلاک انباشته پس از این دوره</summary>
    public decimal AccumulatedAfter { get; set; }

    /// <summary>ارزش دفتری پس از این دوره</summary>
    public decimal BookValueAfter { get; set; }
}

// =====================================================================
// بودجه و کنترل بودجه
// =====================================================================

/// <summary>بودجه — یک برنامه‌ی سالانه برای یک بُعد تحلیلی (مرکز هزینه/پروژه)</summary>
public class Budget
{
    public int Id { get; set; }

    public int FiscalYearId { get; set; }
    public AccFiscalYear? FiscalYear { get; set; }

    /// <summary>بُعد تحلیلی بودجه — معمولاً مرکز هزینه یا پروژه</summary>
    public int? DimensionValueId { get; set; }
    public AccDimensionValue? DimensionValue { get; set; }

    [MaxLength(180)] public string Name { get; set; } = "";

    /// <summary>مبلغ کل بودجه (تجمیعی روی اقلام)</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>بودجه مرجع/اصلی برای تخصیص‌های بعدی</summary>
    public bool IsMaster { get; set; }

    public bool IsActive { get; set; } = true;
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<BudgetItem> Items { get; set; } = new();
}

/// <summary>قلم بودجه — مبلغ برنامه‌ریزی‌شده روی یک حساب</summary>
public class BudgetItem
{
    public int Id { get; set; }

    public int BudgetId { get; set; }
    public Budget? Budget { get; set; }

    public int AccAccountId { get; set; }
    public AccAccount? AccAccount { get; set; }

    public decimal PlannedAmount { get; set; }

    /// <summary>مبلغ تعهدشده (سفارش/قرارداد) — از اسناد بودجه</summary>
    public decimal CommittedAmount { get; set; }

    /// <summary>مبلغ مصرف‌شده (سند قطعی حسابداری) — از اسناد بودجه</summary>
    public decimal ActualAmount { get; set; }
}

/// <summary>رویداد بودجه — تعهد یا مصرف که از سند/سفارش ثبت می‌شود</summary>
public class BudgetTransaction
{
    public int Id { get; set; }

    public int BudgetId { get; set; }
    public Budget? Budget { get; set; }

    public int BudgetItemId { get; set; }
    public BudgetItem? BudgetItem { get; set; }

    public BudgetTransactionType Type { get; set; } = BudgetTransactionType.Actual;

    /// <summary>سند حسابداری مرتبط</summary>
    public int? VoucherId { get; set; }

    /// <summary>سند/سفارش مبدأ (مثلاً InvDoc.Id)</summary>
    public int? SourceId { get; set; }
    [MaxLength(200)] public string? SourceTitle { get; set; }

    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    [MaxLength(500)] public string? Description { get; set; }
}
