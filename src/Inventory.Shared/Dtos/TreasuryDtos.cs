namespace Inventory.Shared.Dtos;

// =====================================================================
// DTO های ماژول خزانه‌داری
//   ۱) صندوق / بانک        ۲) سند دریافت و پرداخت
//   ۳) چک و عملیات چک      ۴) قواعد حسابداری خزانه
//   ۵) گزارش‌ها (گردش خزانه، دفتر چک، داشبورد)
// همه‌ی نام‌ها با پیشوند Trs تا با DTOهای انبار (Inv)، حسابداری (Acc)
// و فاکتور (Fac) تداخل نکنند.
// =====================================================================

// ============================ ۱) صندوق و بانک ============================

/// <summary>یک صندوق، حساب بانکی، کارتخوان یا تنخواه‌گردان</summary>
public class TrsAccount
{
    public int Id { get; set; }

    /// <summary>کد یکتا — مثلاً C-01 یا BNK-MELLAT</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public TreasuryAccountKind Kind { get; set; } = TreasuryAccountKind.Cash;

    /// <summary>حساب معین/تفصیلی متناظر در کدینگ حسابداری</summary>
    public int? AccountId { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }

    // ---------- مشخصات بانکی ----------
    public string? BankName { get; set; }
    public string? BranchName { get; set; }
    public string? BranchCode { get; set; }
    public string? AccountNumber { get; set; }
    public string? Iban { get; set; }
    public string? CardNumber { get; set; }

    /// <summary>مانده ابتدای دوره</summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>حساب پیش‌فرض دریافت/پرداخت نقدی</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public int SortOrder { get; set; }

    // ---------- محاسباتی (پر شده توسط سرور) ----------
    /// <summary>جمع دریافت‌های قطعی‌شده</summary>
    public decimal TotalIn { get; set; }

    /// <summary>جمع پرداخت‌های قطعی‌شده</summary>
    public decimal TotalOut { get; set; }

    /// <summary>مانده جاری = مانده اول دوره + دریافت − پرداخت</summary>
    public decimal Balance { get; set; }

    /// <summary>تعداد گردش</summary>
    public int MoveCount { get; set; }

    public string KindTitle => Kind switch
    {
        TreasuryAccountKind.Cash => "صندوق",
        TreasuryAccountKind.Bank => "بانک",
        TreasuryAccountKind.Pos => "کارتخوان",
        _ => "تنخواه‌گردان"
    };

    /// <summary>عنوان کامل برای نمایش در فهرست‌ها</summary>
    public string DisplayName => Kind == TreasuryAccountKind.Bank && !string.IsNullOrWhiteSpace(BankName)
        ? $"{Name} — {BankName}"
        : Name;
}

// ============================ ۲) سند خزانه ============================

/// <summary>یک سطر (ابزار) سند دریافت یا پرداخت</summary>
public class TrsVoucherLine
{
    public int Id { get; set; }
    public int RowNo { get; set; }

    /// <summary>ابزار: نقد، کارتخوان، حواله، چک، تخفیف</summary>
    public PayMethod Method { get; set; } = PayMethod.Cash;

    /// <summary>صندوق/بانک این سطر — برای نقد، کارت و حواله الزامی است</summary>
    public int? TrsAccountId { get; set; }
    public string? TrsAccountName { get; set; }

    public decimal Amount { get; set; }

    /// <summary>شماره پیگیری / شماره حواله / شماره ارجاع کارتخوان</summary>
    public string? RefNumber { get; set; }

    public string? Description { get; set; }

    // ---------- مشخصات چک (وقتی Method = Cheque) ----------
    /// <summary>چک ساخته‌شده یا انتخاب‌شده برای این سطر</summary>
    public int? ChequeId { get; set; }

    /// <summary>شماره چک</summary>
    public string? ChequeNumber { get; set; }

    /// <summary>شناسه صیاد (۱۶ رقمی)</summary>
    public string? SayadId { get; set; }

    public string? ChequeBankName { get; set; }
    public string? ChequeBranchName { get; set; }

    /// <summary>شماره حساب صادرکننده چک</summary>
    public string? ChequeAccountNumber { get; set; }

    public DateTime? IssueDate { get; set; }
    public DateTime? DueDate { get; set; }

    /// <summary>وضعیت چک — فقط خواندنی، از رکورد چک پر می‌شود</summary>
    public ChequeStatus? ChequeStatus { get; set; }

    public string MethodTitle => Method switch
    {
        PayMethod.Cash => "نقد",
        PayMethod.Card => "کارتخوان",
        PayMethod.Transfer => "حواله بانکی",
        PayMethod.Cheque => "چک",
        _ => "تخفیف و کسورات"
    };

    /// <summary>آیا این ابزار به صندوق/بانک نیاز دارد؟</summary>
    public bool NeedsAccount => Method is PayMethod.Cash or PayMethod.Card or PayMethod.Transfer;
}

/// <summary>سند دریافت / پرداخت / انتقال بین حساب‌ها</summary>
public class TrsVoucher
{
    public int Id { get; set; }

    /// <summary>شماره سند — در هر نوع جداگانه شماره‌گذاری می‌شود</summary>
    public int Number { get; set; }

    public TreasuryKind Kind { get; set; } = TreasuryKind.Receipt;

    public DateTime Date { get; set; } = DateTime.Now;

    /// <summary>طرف حساب (مشتری یا تأمین‌کننده) — در انتقال خالی است</summary>
    public int? PartyId { get; set; }
    public string? PartyName { get; set; }

    /// <summary>بابت — شرح سند</summary>
    public string? Description { get; set; }

    /// <summary>شماره عطف / ارجاع خارجی</summary>
    public string? RefNumber { get; set; }

    public TreasuryStatus Status { get; set; } = TreasuryStatus.Draft;

    // ---------- انتقال بین حساب‌ها (Kind = Transfer) ----------
    public int? FromAccountId { get; set; }
    public string? FromAccountName { get; set; }
    public int? ToAccountId { get; set; }
    public string? ToAccountName { get; set; }

    /// <summary>کارمزد انتقال (بر عهده ما — به حساب هزینه می‌رود)</summary>
    public decimal FeeAmount { get; set; }

    /// <summary>فاکتور مرتبط قدیمی؛ برای سازگاری با سندهای تک‌فاکتوری قبلی نگه داشته شده است</summary>
    public int? InvoiceId { get; set; }
    public int? InvoiceNumber { get; set; }

    /// <summary>تخصیص به یک یا چند فاکتور؛ مبلغ هر ردیف می‌تواند جزئی باشد</summary>
    public List<TrsInvoiceAllocationDto> InvoiceAllocations { get; set; } = new();

    public List<TrsVoucherLine> Lines { get; set; } = new();

    // ---------- مبالغ ----------
    /// <summary>جمع مبلغ سطرها</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>جمع بخش نقدی (نقد + کارت + حواله)</summary>
    public decimal CashAmount { get; set; }

    /// <summary>جمع بخش چک</summary>
    public decimal ChequeAmount { get; set; }

    /// <summary>جمع تخفیف و کسورات</summary>
    public decimal DiscountAmount { get; set; }

    // ---------- سند حسابداری ----------
    public int? VoucherId { get; set; }
    public int? VoucherNumber { get; set; }

    // ---------- ردیابی ----------
    public int LineCount { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    // ---------- عنوان‌های نمایشی ----------
    public string KindTitle => Kind switch
    {
        TreasuryKind.Receipt => "سند دریافت",
        TreasuryKind.Payment => "سند پرداخت",
        _ => "انتقال بین حساب‌ها"
    };

    public string StatusTitle => Status switch
    {
        TreasuryStatus.Draft => "پیش‌نویس",
        TreasuryStatus.Confirmed => "قطعی",
        _ => "ابطال شده"
    };

    /// <summary>آیا پول وارد خزانه می‌شود؟</summary>
    public bool IsIncoming => Kind == TreasuryKind.Receipt;
}

/// <summary>تخصیص یک سند خزانه به یک فاکتور</summary>
public class TrsInvoiceAllocationDto
{
    public int InvoiceId { get; set; }
    public int InvoiceNumber { get; set; }
    public InvoiceKind InvoiceKind { get; set; }
    public DateTime InvoiceDate { get; set; }
    public decimal InvoiceTotal { get; set; }
    /// <summary>ماندهٔ فاکتور با حذف اثر این سند؛ برای نمایش مانده با اثر تخصیص</summary>
    public decimal RemainingBeforeSettlement { get; set; }
    /// <summary>مبلغ تسویهٔ مؤثر؛ پس از برگشت چک ممکن است از مبلغ تخصیص اولیه کمتر باشد</summary>
    public decimal EffectiveAmount { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>یک فاکتور قطعی با مانده‌ی باز، قابل انتخاب برای تسویه</summary>
public class TrsSettlementInvoice
{
    public int InvoiceId { get; set; }
    public int InvoiceNumber { get; set; }
    public InvoiceKind InvoiceKind { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal InvoiceTotal { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal RemainingAmount { get; set; }

    public string KindTitle => InvoiceKind switch
    {
        InvoiceKind.Purchase => "فاکتور خرید",
        InvoiceKind.Sale => "فاکتور فروش",
        InvoiceKind.PurchaseReturn => "برگشت از خرید",
        _ => "برگشت از فروش"
    };
}

// ============================ ۳) چک ============================

/// <summary>یک برگ چک دریافتی یا پرداختی</summary>
public class TrsCheque
{
    public int Id { get; set; }

    public ChequeKind Kind { get; set; } = ChequeKind.Received;

    /// <summary>شماره چک</summary>
    public string Number { get; set; } = "";

    /// <summary>شناسه صیاد (۱۶ رقمی)</summary>
    public string? SayadId { get; set; }

    public decimal Amount { get; set; }

    public DateTime IssueDate { get; set; } = DateTime.Now;
    public DateTime DueDate { get; set; } = DateTime.Now;

    // ---------- بانک ----------
    public string? BankName { get; set; }
    public string? BranchName { get; set; }

    /// <summary>شماره حساب صادرکننده (چک دریافتی) یا حساب ما (چک صادره)</summary>
    public string? AccountNumber { get; set; }

    /// <summary>نام صاحب چک (در چک دریافتی، اگر با طرف حساب فرق دارد)</summary>
    public string? OwnerName { get; set; }

    /// <summary>طرف حساب: پرداخت‌کننده (دریافتی) یا دریافت‌کننده (صادره)</summary>
    public int? PartyId { get; set; }
    public string? PartyName { get; set; }

    /// <summary>
    /// چک دریافتی: بانکی که چک به آن واگذار شده.
    /// چک صادره: حساب بانکی ما که چک از آن کشیده شده (الزامی).
    /// </summary>
    public int? TrsAccountId { get; set; }
    public string? TrsAccountName { get; set; }

    public ChequeStatus Status { get; set; } = ChequeStatus.InHand;

    /// <summary>تاریخ آخرین تغییر وضعیت (واگذاری، وصول، برگشت)</summary>
    public DateTime? StatusDate { get; set; }

    public string? Description { get; set; }

    // ---------- منشأ ----------
    /// <summary>سند خزانه‌ای که این چک در آن ثبت شده است</summary>
    public int? TrsVoucherId { get; set; }
    public int? TrsVoucherNumber { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>تاریخچه عملیات انجام‌شده روی چک</summary>
    public List<TrsChequeAction> Actions { get; set; } = new();

    // ---------- محاسباتی ----------
    public string KindTitle => Kind == ChequeKind.Received ? "چک دریافتی" : "چک پرداختی";

    public string StatusTitle => Status switch
    {
        ChequeStatus.InHand => Kind == ChequeKind.Received ? "نزد صندوق" : "تحویل شده",
        ChequeStatus.InCollection => "در جریان وصول",
        ChequeStatus.Cleared => Kind == ChequeKind.Received ? "وصول شده" : "پاس شده",
        ChequeStatus.Bounced => "برگشتی",
        ChequeStatus.Endorsed => "خرج شده",
        _ => "ابطال شده"
    };

    /// <summary>وضعیت باز است؟ (هنوز تعیین تکلیف نشده)</summary>
    public bool IsOpen => Status is ChequeStatus.InHand or ChequeStatus.InCollection;

    /// <summary>روز تا سررسید — منفی یعنی سررسید گذشته است</summary>
    public int DaysToDue => (int)(DueDate.Date - DateTime.Now.Date).TotalDays;

    /// <summary>سررسید گذشته و هنوز وصول نشده</summary>
    public bool IsOverdue => IsOpen && DaysToDue < 0;

    /// <summary>سررسید در هفت روز آینده</summary>
    public bool IsDueSoon => IsOpen && DaysToDue >= 0 && DaysToDue <= 7;
}

/// <summary>یک عملیات (تغییر وضعیت) روی چک</summary>
public class TrsChequeAction
{
    public int Id { get; set; }
    public int ChequeId { get; set; }

    /// <summary>وضعیت جدیدی که چک به آن منتقل شد</summary>
    public ChequeStatus Status { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;

    /// <summary>صندوق/بانک درگیر در عملیات (بانک واگذاری یا بانک وصول)</summary>
    public int? TrsAccountId { get; set; }
    public string? TrsAccountName { get; set; }

    public string? Description { get; set; }

    /// <summary>سند حسابداری صادرشده برای این عملیات</summary>
    public int? VoucherId { get; set; }
    public int? VoucherNumber { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public string StatusTitle => Status switch
    {
        ChequeStatus.InHand => "ثبت اولیه",
        ChequeStatus.InCollection => "واگذاری به بانک",
        ChequeStatus.Cleared => "وصول",
        ChequeStatus.Bounced => "برگشت",
        ChequeStatus.Endorsed => "خرج چک",
        _ => "ابطال"
    };
}

/// <summary>دستور اجرای یک عملیات روی چک</summary>
public class TrsChequeCommand
{
    public int ChequeId { get; set; }

    /// <summary>وضعیت مقصد</summary>
    public ChequeStatus Status { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;

    /// <summary>بانک واگذاری یا بانک وصول (بسته به عملیات)</summary>
    public int? TrsAccountId { get; set; }

    public string? Description { get; set; }
}

// ============================ ۴) قواعد خزانه ============================

/// <summary>
/// پیکربندی حسابداری خزانه برای دریافت و پرداخت.
/// یک رکورد برای «دریافت» و یک رکورد برای «پرداخت».
/// </summary>
public class TrsRule
{
    public int Id { get; set; }

    public TreasuryKind Kind { get; set; }

    /// <summary>حساب طرف حساب: دریافتنی (دریافت) یا پرداختنی (پرداخت)</summary>
    public int? PartyAccountId { get; set; }
    public string? PartyAccountName { get; set; }

    /// <summary>اسناد دریافتنی (چک دریافتی) یا اسناد پرداختنی (چک صادره)</summary>
    public int? ChequeAccountId { get; set; }
    public string? ChequeAccountName { get; set; }

    /// <summary>اسناد در جریان وصول — فقط برای چک‌های دریافتی</summary>
    public int? CollectionAccountId { get; set; }
    public string? CollectionAccountName { get; set; }

    /// <summary>تخفیفات نقدی اعطایی (دریافت) یا دریافتی (پرداخت)</summary>
    public int? DiscountAccountId { get; set; }
    public string? DiscountAccountName { get; set; }

    /// <summary>کارمزد بانکی — برای انتقال و عملیات چک</summary>
    public int? FeeAccountId { get; set; }
    public string? FeeAccountName { get; set; }

    /// <summary>با قطعی شدن سند خزانه، سند حسابداری خودکار صادر شود</summary>
    public bool AutoVoucher { get; set; } = true;

    public bool IsActive { get; set; }
    public string? Description { get; set; }

    public string KindTitle => Kind switch
    {
        TreasuryKind.Receipt => "دریافت",
        TreasuryKind.Payment => "پرداخت",
        _ => "انتقال"
    };
}

// ============================ ۵) گزارش‌ها ============================

/// <summary>یک سطر گردش صندوق/بانک</summary>
public class TrsFlowRow
{
    public DateTime Date { get; set; }

    public int TrsVoucherId { get; set; }
    public int Number { get; set; }
    public TreasuryKind Kind { get; set; }

    /// <summary>ابزار (نقد، چک، حواله، …) یا «انتقال»</summary>
    public string Method { get; set; } = "";

    public string? PartyName { get; set; }
    public string? Description { get; set; }
    public string? RefNumber { get; set; }

    /// <summary>وارده</summary>
    public decimal In { get; set; }

    /// <summary>صادره</summary>
    public decimal Out { get; set; }

    /// <summary>مانده تجمعی</summary>
    public decimal Balance { get; set; }
}

/// <summary>خروجی گزارش گردش صندوق/بانک</summary>
public class TrsFlowResult
{
    public int TrsAccountId { get; set; }
    public string AccountName { get; set; } = "";
    public string AccountKindTitle { get; set; } = "";

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>مانده ابتدای بازه</summary>
    public decimal OpeningBalance { get; set; }

    public List<TrsFlowRow> Rows { get; set; } = new();

    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }

    /// <summary>مانده پایان بازه</summary>
    public decimal ClosingBalance { get; set; }
}

/// <summary>خلاصه خزانه برای داشبورد</summary>
public class TrsDashboard
{
    /// <summary>مانده هر صندوق/بانک</summary>
    public List<TrsAccount> Accounts { get; set; } = new();

    /// <summary>جمع مانده همه حساب‌ها</summary>
    public decimal TotalBalance { get; set; }

    public decimal TotalCashBalance { get; set; }
    public decimal TotalBankBalance { get; set; }

    // ---------- گردش دوره ----------
    public decimal PeriodIn { get; set; }
    public decimal PeriodOut { get; set; }
    public int ReceiptCount { get; set; }
    public int PaymentCount { get; set; }
    public int DraftCount { get; set; }

    // ---------- چک‌ها ----------
    /// <summary>جمع مبلغ چک‌های دریافتی باز</summary>
    public decimal ReceivedChequeTotal { get; set; }

    /// <summary>جمع مبلغ چک‌های پرداختی باز</summary>
    public decimal IssuedChequeTotal { get; set; }

    public int ReceivedChequeCount { get; set; }
    public int IssuedChequeCount { get; set; }

    /// <summary>تعداد چک‌های سررسید گذشته و وصول‌نشده</summary>
    public int OverdueChequeCount { get; set; }
    public decimal OverdueChequeTotal { get; set; }

    /// <summary>چک‌های نزدیک به سررسید (۳۰ روز آینده) — برای جدول یادآوری</summary>
    public List<TrsCheque> UpcomingCheques { get; set; } = new();
}
