// لایه داده حکمرانی‌شده هوش مصنوعی — کاتالوگ موجودیت‌ها (§۱۵)
// تنها راه دسترسی AI به داده خام، همین فیلدهای تعریف‌شده است؛ هیچ SQL خامی ساخته نمی‌شود.
using Inventory.Api.Data;

namespace Inventory.Api.Services.Ai;

/// <summary>تعریف یک فیلد قابل‌مشاهده/فیلتر در اکسپلورر داده</summary>
public sealed class AiFieldDef
{
    /// <summary>نام فیلد؛ ناوبری تک‌سطحی با نقطه مجاز است (مثل Party.Name)</summary>
    public string Name { get; init; } = "";
    /// <summary>عنوان فارسی برای جدول چت و اکسل</summary>
    public string Fa { get; init; } = "";
    /// <summary>نوع: text|number|money|date|datetime|bool|enum|duration|time</summary>
    public string Kind { get; init; } = "text";
    /// <summary>نگاشت مقدار به فارسی (برای enum و int کددار)؛ کلید = نام/عدد مقدار</summary>
    public Dictionary<string, string>? MapFa { get; init; }
    /// <summary>فیلد حساس: فقط با مجوز SensitiveModule/Read دیده می‌شود</summary>
    public bool Sensitive { get; init; }
    public string SensitiveModule { get; init; } = "FaPay";
    /// <summary>حداکثر طول متن در خروجی (۰ = بدون برش)</summary>
    public int Truncate { get; init; }
}

/// <summary>تعریف یک موجودیت قابل کاوش</summary>
public sealed class AiEntityDef
{
    /// <summary>کلید موجودیت در explore_data</summary>
    public string Name { get; init; } = "";
    public string Fa { get; init; } = "";
    /// <summary>راهنمای فارسی برای مدل: این موجودیت کی به کار می‌آید</summary>
    public string Hint { get; init; } = "";
    /// <summary>ماژول RBAC (دسترسی Read)؛ خالی = باز برای همه کاربران مجاز AI</summary>
    public string Module { get; init; } = "";
    /// <summary>اکشن RBAC روی ماژول (پیش‌فرض Read؛ مثلاً View برای صورتجلسه)</summary>
    public string ModuleAction { get; init; } = "Read";
    public Type ClrType { get; init; } = null!;
    /// <summary>محدوده سطر: all | letters | employee | user | referral</summary>
    public string Scope { get; init; } = "all";
    /// <summary>ماژولی که مجوز Manage آن، محدوده سطر را دور می‌زند</summary>
    public string ScopeManageModule { get; init; } = "";
    public string ScopeManageAction { get; init; } = "Manage";
    /// <summary>فیلترهای همیشه‌اعمال (Field, Op, Value)</summary>
    public List<(string Field, string Op, string Value)> BaseFilters { get; init; } = new();
    public List<AiFieldDef> Fields { get; init; } = new();
    public string DefaultOrder { get; init; } = "Id";
    public AiFieldDef? FindField(string name)
        => Fields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public static class AiDataCatalog
{
    private static AiFieldDef F(string name, string fa, string kind = "text",
        Dictionary<string, string>? map = null, bool sensitive = false,
        string sensitiveModule = "FaPay", int truncate = 0)
        => new() { Name = name, Fa = fa, Kind = kind, MapFa = map, Sensitive = sensitive, SensitiveModule = sensitiveModule, Truncate = truncate };

    public static readonly List<AiEntityDef> Entities = new()
    {
        new AiEntityDef
        {
            Name = "party", Fa = "طرف‌حساب‌ها", Module = "Parties", ClrType = typeof(Party),
            Hint = "مشتریان و تأمین‌کنندگان؛ برای «لیست مشتریان»، «تلفن فلان مشتری»",
            Fields = new()
            {
                F("Id", "کد"), F("Name", "نام"),
                F("Type", "نوع", "enum", new() { ["Customer"] = "مشتری", ["Supplier"] = "تأمین‌کننده" }),
                F("Phone", "تلفن"), F("Mobile", "موبایل"), F("Address", "آدرس", truncate: 80),
                F("IsActive", "فعال؟", "bool"),
            },
        },
        new AiEntityDef
        {
            Name = "product", Fa = "کالاها", Module = "Products", ClrType = typeof(Product),
            Hint = "کارت کالا: قیمت فروش/خرید، واحد، دسته؛ برای «قیمت فلان کالا»، «لیست کالاهای دسته X»",
            Fields = new()
            {
                F("Id", "کد"), F("Code", "کد کالا"), F("Name", "نام کالا"), F("Unit", "واحد"),
                F("Category", "دسته"), F("Brand", "برند"), F("Model", "مدل"),
                F("SalePrice", "قیمت فروش", "money"), F("PurchasePrice", "قیمت خرید", "money"),
                F("ReorderPoint", "نقطه سفارش", "number"),
                F("IsActive", "فعال؟", "bool"), F("IsService", "خدمت؟", "bool"),
                F("CreatedAt", "تاریخ ثبت", "date"),
            },
        },
        new AiEntityDef
        {
            Name = "employee", Fa = "پرسنل", Module = "HrCore", ClrType = typeof(HrEmployee),
            Hint = "کارکنان: مشخصات، سمت، نوع استخدام؛ حقوق و شبا فقط با مجوز حقوق دیده می‌شود",
            Fields = new()
            {
                F("Id", "کد"), F("Code", "کد پرسنلی"), F("FirstName", "نام"), F("LastName", "نام خانوادگی"),
                F("NationalCode", "کد ملی"), F("BirthDate", "تاریخ تولد", "date"),
                F("Gender", "جنسیت", "enum", new() { ["0"] = "مرد", ["1"] = "زن" }),
                F("MaritalStatus", "تأهل", "enum", new() { ["0"] = "مجرد", ["1"] = "متاهل" }),
                F("Mobile", "موبایل"), F("HireDate", "تاریخ استخدام", "date"), F("PostTitle", "سمت"),
                F("EmploymentType", "نوع استخدام", "enum", new()
                {
                    ["Rasmi"] = "رسمی", ["Gharardadi"] = "قراردادی", ["Peymani"] = "پیمانی",
                    ["Saati"] = "ساعتی", ["Mashaverei"] = "مشاوره‌ای", ["TamamVaght"] = "تمام‌وقت",
                    ["PareVaght"] = "پاره‌وقت", ["Projei"] = "پروژه‌ای", ["Azmayeshi"] = "آزمایشی",
                }),
                F("Status", "وضعیت", "enum", new()
                {
                    ["Active"] = "فعال", ["OnLeave"] = "در مرخصی", ["Suspended"] = "معلق",
                    ["Terminated"] = "خاتمه‌یافته", ["Retired"] = "بازنشسته",
                }),
                F("BaseSalary", "حقوق پایه", "money", sensitive: true),
                F("Sheba", "شبا", sensitive: true), F("InsuranceNo", "شماره بیمه", sensitive: true),
                F("IsActive", "فعال؟", "bool"), F("CreatedAt", "تاریخ ثبت", "date"),
            },
        },
        new AiEntityDef
        {
            Name = "contract", Fa = "قراردادهای پرسنلی", Module = "HrCore", ClrType = typeof(HrContract),
            Hint = "قراردادهای کارکنان: شماره، نوع، بازه زمانی؛ برای «قراردادهای رو به اتمام»",
            Fields = new()
            {
                F("Id", "کد"), F("EmployeeId", "کد پرسنل"), F("ContractNo", "شماره قرارداد"),
                F("Type", "نوع", "enum", new()
                {
                    ["Rasmi"] = "رسمی", ["Gharardadi"] = "قراردادی", ["Peymani"] = "پیمانی",
                    ["Saati"] = "ساعتی", ["Mashaverei"] = "مشاوره‌ای", ["TamamVaght"] = "تمام‌وقت",
                    ["PareVaght"] = "پاره‌وقت", ["Projei"] = "پروژه‌ای", ["Azmayeshi"] = "آزمایشی",
                }),
                F("StartDate", "شروع", "date"), F("EndDate", "پایان", "date"),
                F("BaseSalary", "حقوق پایه", "money", sensitive: true),
                F("JobTitle", "عنوان شغلی"), F("IsActive", "فعال؟", "bool"), F("CreatedAt", "تاریخ ثبت", "date"),
            },
        },
        new AiEntityDef
        {
            Name = "leave", Fa = "مرخصی‌ها", Module = "FaAtt", ClrType = typeof(FaAttLeave),
            Scope = "employee", ScopeManageModule = "FaAtt",
            Hint = "درخواست‌های مرخصی؛ هر کس فقط مرخصی‌های خودش را می‌بیند مگر مدیر",
            Fields = new()
            {
                F("Id", "کد"), F("EmployeeId", "کد پرسنل"),
                F("FromDate", "از تاریخ", "date"), F("ToDate", "تا تاریخ", "date"),
                F("HoursPerDay", "ساعت در روز", "number"), F("Reason", "علت", truncate: 120),
                F("Status", "وضعیت", "enum", new()
                {
                    ["Pending"] = "در انتظار", ["Approved"] = "تأییدشده", ["Rejected"] = "ردشده",
                }),
                F("WorkflowStep", "مرحله", "enum", new()
                {
                    ["0"] = "نزد مدیر", ["1"] = "نزد HR", ["2"] = "تمام‌شده",
                }),
                F("DecidedByName", "تصمیم‌گیرنده"), F("DecidedAt", "زمان تصمیم", "datetime"),
                F("CreatedAt", "تاریخ ثبت", "datetime"),
            },
        },
        new AiEntityDef
        {
            Name = "attendance", Fa = "حضور و غیاب", Module = "FaAtt", ClrType = typeof(AttendanceRecord),
            Scope = "user", ScopeManageModule = "FaAtt",
            Hint = "رکوردهای روزانه ورود/خروج، تأخیر و اضافه‌کار؛ هر کس فقط رکوردهای خودش را می‌بیند مگر مدیر",
            Fields = new()
            {
                F("Id", "کد"), F("WorkDate", "تاریخ", "date"), F("UserId", "کد کاربر"), F("UserName", "کاربر"),
                F("EnterAt", "ورود", "datetime"), F("ExitAt", "خروج", "datetime"),
                F("EnterStatus", "وضعیت ورود"),
                F("LateMinutes", "تأخیر (دقیقه)", "number"), F("WorkMinutes", "کارکرد (دقیقه)", "number"),
                F("OvertimeMinutes", "اضافه‌کار (دقیقه)", "number"), F("DeficitMinutes", "کسری (دقیقه)", "number"),
                F("PendingOvertimeMinutes", "مازادِ در انتظار تصمیم مدیر (دقیقه)", "number"),
                F("UnauthorizedMinutes", "تردد غیرمجاز (دقیقه)", "number"),
                F("FinalStatus", "وضعیت نهایی"), F("Note", "یادداشت", truncate: 100),
            },
        },
        new AiEntityDef
        {
            Name = "invoice", Fa = "فاکتورها", Module = "FacInvoices", ClrType = typeof(FacInvoice),
            Hint = "فاکتورهای خرید/فروش و برگشتی؛ برای «فاکتورهای امروز»، «فروش فلان مشتری»",
            Fields = new()
            {
                F("Id", "کد"), F("Number", "شماره"),
                F("Kind", "نوع", "enum", new()
                {
                    ["Purchase"] = "خرید", ["Sale"] = "فروش",
                    ["PurchaseReturn"] = "برگشت از خرید", ["SaleReturn"] = "برگشت از فروش",
                }),
                F("Date", "تاریخ", "date"), F("DueDate", "سررسید", "date"),
                F("Party.Name", "طرف حساب"),
                F("Settlement", "تسویه", "enum", new() { ["Credit"] = "نسیه", ["Cash"] = "نقدی" }),
                F("Status", "وضعیت", "enum", new()
                {
                    ["Draft"] = "پیش‌نویس", ["Confirmed"] = "قطعی", ["Cancelled"] = "ابطال‌شده",
                }),
                F("TotalNet", "مبلغ خالص", "money"), F("TotalTaxable", "مشمول مالیات", "money"),
                F("TotalVat", "مالیات", "money"),
                F("CreatedBy", "ثبت‌کننده"), F("CreatedAt", "تاریخ ثبت", "datetime"),
                F("ConfirmedAt", "تاریخ قطعی", "datetime"),
            },
        },
        new AiEntityDef
        {
            Name = "cheque", Fa = "چک‌ها", Module = "TrsCheques", ClrType = typeof(TrsCheque),
            Hint = "چک‌های دریافتی و صادره؛ برای «چک‌های برگشتی»، «چک‌های سررسید این هفته»",
            Fields = new()
            {
                F("Id", "کد"), F("Number", "شماره چک"),
                F("Kind", "نوع", "enum", new() { ["Received"] = "دریافتی", ["Issued"] = "صادره" }),
                F("Amount", "مبلغ", "money"),
                F("IssueDate", "تاریخ صدور", "date"), F("DueDate", "سررسید", "date"),
                F("BankName", "بانک"), F("OwnerName", "صاحب چک"), F("Party.Name", "طرف حساب"),
                F("Status", "وضعیت", "enum", new()
                {
                    ["InHand"] = "نزد ما", ["InCollection"] = "در جریان وصول", ["Cleared"] = "وصول/پاس‌شده",
                    ["Bounced"] = "برگشتی", ["Endorsed"] = "خرج‌شده", ["Cancelled"] = "ابطال‌شده",
                }),
                F("StatusDate", "تاریخ وضعیت", "date"),
                F("CreatedBy", "ثبت‌کننده"), F("CreatedAt", "تاریخ ثبت", "datetime"),
            },
        },
        new AiEntityDef
        {
            Name = "trs_voucher", Fa = "اسناد خزانه", Module = "TrsVouchers", ClrType = typeof(TrsVoucher),
            Hint = "دریافت‌ها و پرداخت‌های خزانه؛ برای «پرداخت‌های امروز»، «دریافت‌های این ماه»",
            Fields = new()
            {
                F("Id", "کد"), F("Number", "شماره"),
                F("Kind", "نوع", "enum", new()
                {
                    ["Receipt"] = "دریافت", ["Payment"] = "پرداخت", ["Transfer"] = "انتقال",
                }),
                F("Date", "تاریخ", "date"), F("Party.Name", "طرف حساب"),
                F("Description", "شرح", truncate: 100),
                F("Status", "وضعیت", "enum", new()
                {
                    ["Draft"] = "پیش‌نویس", ["Confirmed"] = "قطعی", ["Cancelled"] = "ابطال‌شده",
                }),
                F("TotalAmount", "مبلغ کل", "money"),
                F("CreatedBy", "ثبت‌کننده"), F("CreatedAt", "تاریخ ثبت", "datetime"),
                F("ConfirmedAt", "تاریخ قطعی", "datetime"),
            },
        },
        new AiEntityDef
        {
            Name = "acc_voucher", Fa = "اسناد حسابداری", Module = "AccVouchers", ClrType = typeof(AccVoucher),
            Hint = "اسناد حسابداری (روزنامه)؛ برای «اسناد امروز»، «اسناد دستی این ماه»",
            Fields = new()
            {
                F("Id", "کد"), F("Number", "شماره"), F("Date", "تاریخ", "date"),
                F("Description", "شرح", truncate: 100),
                F("Status", "وضعیت", "enum", new()
                {
                    ["Draft"] = "پیش‌نویس", ["Confirmed"] = "قطعی", ["Cancelled"] = "باطل",
                }),
                F("Source", "منبع", "enum", new()
                {
                    ["Manual"] = "دستی", ["InventoryDoc"] = "خودکار انبار", ["Invoice"] = "خودکار فاکتور",
                    ["Opening"] = "افتتاحیه", ["Closing"] = "اختتامیه", ["Payroll"] = "خودکار حقوق",
                }),
                F("TotalDebit", "جمع بدهکار", "money"), F("TotalCredit", "جمع بستانکار", "money"),
                F("CreatedBy", "ثبت‌کننده"), F("CreatedAt", "تاریخ ثبت", "datetime"),
                F("ConfirmedAt", "تاریخ قطعی", "datetime"),
            },
        },
        new AiEntityDef
        {
            Name = "letter", Fa = "نامه‌ها", Module = "InnerLetters", ClrType = typeof(InnerLetter),
            Scope = "letters", ScopeManageModule = "InnerLetters", ScopeManageAction = "ViewAll",
            Hint = "نامه‌های داخلی (فقط مشخصات؛ متن کامل با ابزار letter_detail)؛ هر کس نامه‌های خودش را می‌بیند",
            BaseFilters = new() { ("IsDelete", "eq", "false") },
            Fields = new()
            {
                F("Id", "کد"), F("LetterNumber", "شماره نامه"), F("Title", "موضوع", truncate: 120),
                F("DateSabt", "تاریخ ثبت", "datetime"),
                F("Mahramanegi", "محرمانگی"), F("Foriat", "فوریت"),
                F("Creator.FirstName", "نام ثبت‌کننده"), F("Creator.LastName", "نام خانوادگی ثبت‌کننده"),
            },
        },
        new AiEntityDef
        {
            Name = "project", Fa = "پروژه‌ها", Module = "Projects", ClrType = typeof(ProjectEntryExit),
            Hint = "پروژه‌ها: مشخصات، تاریخ‌های ورود/خروج، وضعیت گردش‌کار، جمع ساعات",
            BaseFilters = new() { ("IsDelete", "eq", "false") },
            Fields = new()
            {
                F("Id", "کد"), F("CodeProject", "کد پروژه"), F("ProjectName", "نام پروژه", truncate: 100),
                F("SerialNumber", "شماره سریال"), F("ProjectReceiver", "تحویل‌گیرنده"),
                F("EntryDate", "تاریخ ورود", "date"), F("ExitDate", "تاریخ خروج", "date"),
                F("FlowStatus", "وضعیت", "enum", new()
                {
                    ["0"] = "در انتظار تایید مدیر", ["1"] = "در انتظار کارشناسی",
                    ["2"] = "رد شده", ["3"] = "نهایی",
                }),
                F("TotalSpentTime", "جمع ساعات", "duration"), F("CreatedAt", "تاریخ ثبت", "datetime"),
            },
        },
        new AiEntityDef
        {
            Name = "report_work", Fa = "گزارش‌کارها", Module = "ReportWorks", ClrType = typeof(ReportWork),
            Hint = "گزارش‌کارهای ثبت‌شده روی پروژه‌ها؛ برای «کارهای امروز فلان پروژه»",
            BaseFilters = new() { ("IsDelete", "eq", "false") },
            Fields = new()
            {
                F("Id", "کد"), F("CodeProject", "کد پروژه"), F("ProjectId", "شناسه پروژه"),
                F("ReportDate", "تاریخ", "date"), F("UserId", "کد کاربر"),
                F("User.FirstName", "نام ثبت‌کننده"), F("User.LastName", "نام خانوادگی ثبت‌کننده"),
                F("WorkDescription", "شرح کار", truncate: 150),
                F("StartTime", "شروع", "time"), F("EndTime", "پایان", "time"),
                F("SpentTime", "مدت", "duration"), F("CreatedAt", "تاریخ ثبت", "datetime"),
            },
        },
        new AiEntityDef
        {
            Name = "ticket", Fa = "تیکت‌های HR", Module = "FaCom", ClrType = typeof(FaComTicket),
            Scope = "employee", ScopeManageModule = "FaCom",
            Hint = "تیکت‌های کارمندان به منابع انسانی؛ هر کس فقط تیکت‌های خودش را می‌بیند مگر مدیر",
            Fields = new()
            {
                F("Id", "کد"), F("Subject", "موضوع", truncate: 100), F("Body", "متن", truncate: 150),
                F("Category", "دسته", "enum", new()
                {
                    ["Other"] = "سایر", ["Leave"] = "مرخصی", ["Payroll"] = "حقوق",
                    ["Insurance"] = "بیمه", ["Contract"] = "قرارداد", ["Training"] = "آموزش",
                }),
                F("Priority", "اولویت", "enum", new()
                {
                    ["0"] = "کم", ["1"] = "متوسط", ["2"] = "زیاد",
                }),
                F("Status", "وضعیت", "enum", new()
                {
                    ["New"] = "جدید", ["InProgress"] = "در حال بررسی",
                    ["Answered"] = "پاسخ داده‌شده", ["Closed"] = "بسته",
                }),
                F("CreatedAt", "تاریخ ثبت", "datetime"),
                F("ClosedAt", "تاریخ بستن", "datetime"), F("ClosedByName", "بسته‌شده توسط"),
            },
        },
        new AiEntityDef
        {
            Name = "user", Fa = "کاربران", Module = "", ClrType = typeof(User),
            Hint = "کاربران سیستم (فقط مشخصات پایه؛ بدون رمز و شماره تماس)",
            Fields = new()
            {
                F("Id", "کد"), F("Username", "نام کاربری"),
                F("FirstName", "نام"), F("LastName", "نام خانوادگی"),
                F("Role", "نقش"), F("IsActive", "فعال؟", "bool"), F("CreatedAt", "تاریخ ثبت", "date"),
            },
        },
        // ---------- موج دوم (§۱۵) ----------
        new AiEntityDef
        {
            Name = "warehouse", Fa = "انبارها", Module = "Warehouses", ClrType = typeof(Warehouse),
            Hint = "مشخصات انبارها؛ برای «لیست انبارها»، «انباردار فلان انبار» (موجودی با stock_status)",
            Fields = new()
            {
                F("Id", "کد"), F("Code", "کد انبار"), F("Name", "نام انبار"),
                F("Kind", "نوع", "enum", new()
                {
                    ["Main"] = "اصلی", ["Sub"] = "فرعی", ["Consignment"] = "امانی",
                }),
                F("KeeperName", "انباردار"), F("Phone", "تلفن"), F("Address", "آدرس", truncate: 80),
                F("IsDefault", "پیش‌فرض؟", "bool"), F("IsActive", "فعال؟", "bool"),
            },
        },
        new AiEntityDef
        {
            Name = "invoice_line", Fa = "سطرهای فاکتور", Module = "FacInvoices", ClrType = typeof(FacInvoiceLine),
            Hint = "اقلام داخل فاکتورها: کالا، مقدار، فی و مبلغ؛ برای «فروش فلان کالا»، «اقلام فاکتور شماره X»",
            Fields = new()
            {
                F("Id", "کد"), F("InvoiceId", "کد فاکتور"), F("Invoice.Number", "شماره فاکتور", "number"),
                F("RowNo", "ردیف", "number"), F("ProductId", "کد کالا"),
                F("Product.Code", "کد کالا (حرفی)"), F("Product.Name", "کالا"),
                F("Quantity", "مقدار", "number"), F("UnitPrice", "فی", "money"),
                F("Discount", "تخفیف", "money"), F("Taxable", "مشمول مالیات", "money"),
                F("VatAmount", "مالیات", "money"), F("Total", "مبلغ", "money"),
                F("Description", "شرح", truncate: 100),
            },
        },
        new AiEntityDef
        {
            Name = "voucher_line", Fa = "سطرهای سند حسابداری", Module = "AccVouchers", ClrType = typeof(AccVoucherLine),
            Hint = "آرتیکل‌های اسناد: حساب، بدهکار/بستانکار؛ برای «گردش حساب X»، «سطرهای سند شماره Y»",
            Fields = new()
            {
                F("Id", "کد"), F("VoucherId", "کد سند"),
                F("Voucher.Number", "شماره سند", "number"), F("Voucher.Date", "تاریخ سند", "date"),
                F("RowNo", "ردیف", "number"), F("AccountId", "کد حساب"),
                F("Account.Code", "کد حساب (حرفی)"), F("Account.Name", "حساب"),
                F("PartyId", "کد طرف", "number"), F("Description", "شرح", truncate: 100),
                F("Debit", "بدهکار", "money"), F("Credit", "بستانکار", "money"),
            },
        },
        new AiEntityDef
        {
            Name = "mission", Fa = "مأموریت‌ها", Module = "FaAtt", ClrType = typeof(FaAttMission),
            Scope = "employee", ScopeManageModule = "FaAtt",
            Hint = "مأموریت‌های پرسنل؛ هر کس فقط مأموریت‌های خودش را می‌بیند مگر مدیر",
            Fields = new()
            {
                F("Id", "کد"), F("EmployeeId", "کد پرسنل"),
                F("FromDate", "از تاریخ", "date"), F("ToDate", "تا تاریخ", "date"),
                F("Destination", "مقصد"), F("Reason", "علت", truncate: 120),
                F("Status", "وضعیت", "enum", new()
                {
                    ["Pending"] = "در انتظار", ["Approved"] = "تأییدشده", ["Rejected"] = "ردشده",
                }),
                F("DecidedByName", "تصمیم‌گیرنده"), F("DecidedAt", "زمان تصمیم", "datetime"),
                F("CreatedAt", "تاریخ ثبت", "datetime"),
            },
        },
        new AiEntityDef
        {
            Name = "loan", Fa = "وام‌ها", Module = "FaPay", ClrType = typeof(FaPayLoan),
            Scope = "employee", ScopeManageModule = "FaPay",
            Hint = "وام و مساعده پرسنل؛ هر کس فقط وام‌های خودش را می‌بیند مگر مدیر حقوق",
            Fields = new()
            {
                F("Id", "کد"), F("EmployeeId", "کد پرسنل"), F("Title", "عنوان"),
                F("TotalAmount", "مبلغ کل", "money"), F("InstallmentCount", "تعداد قسط", "number"),
                F("InstallmentAmount", "مبلغ قسط", "money"),
                F("StartYear", "سال شروع", "number"), F("StartMonth", "ماه شروع", "number"),
                F("Status", "وضعیت", "enum", new()
                {
                    ["Active"] = "فعال", ["Paid"] = "تسویه‌شده", ["Cancelled"] = "لغوشده",
                }),
                F("Note", "یادداشت", truncate: 100),
            },
        },
        new AiEntityDef
        {
            Name = "leave_balance", Fa = "مانده مرخصی", Module = "FaAtt", ClrType = typeof(FaAttLeaveBalance),
            Scope = "employee", ScopeManageModule = "FaAtt",
            Hint = "استحقاق و مصرف مرخصی سالانه به تفکیک نوع؛ هر کس فقط مال خودش مگر مدیر",
            Fields = new()
            {
                F("Id", "کد"), F("EmployeeId", "کد پرسنل"), F("Year", "سال", "number"),
                F("LeaveTypeId", "کد نوع مرخصی", "number"),
                F("EntitledDays", "استحقاق (روز)", "number"), F("UsedDays", "استفاده‌شده", "number"),
                F("CarriedDays", "انتقالی", "number"), F("CashedDays", "بازخریدشده", "number"),
                F("CashAmount", "مبلغ بازخرید", "money"),
            },
        },
        new AiEntityDef
        {
            Name = "acc_account", Fa = "کدینگ حساب‌ها", Module = "AccAccounts", ClrType = typeof(AccAccount),
            Hint = "سرفصل‌های حسابداری: کد، نام، سطح و نوع؛ برای «کد حساب X»، «حساب‌های معین»",
            Fields = new()
            {
                F("Id", "کد"), F("Code", "کد حساب"), F("Name", "نام حساب"),
                F("Level", "سطح", "enum", new()
                {
                    ["Group"] = "گروه", ["General"] = "کل", ["Subsidiary"] = "معین", ["Detail"] = "تفصیلی",
                }),
                F("Type", "نوع", "enum", new()
                {
                    ["Asset"] = "دارایی", ["Liability"] = "بدهی", ["Equity"] = "حقوق مالکانه",
                    ["Income"] = "درآمد", ["Expense"] = "هزینه",
                }),
                F("Nature", "ماهیت", "enum", new()
                {
                    ["Debit"] = "بدهکار", ["Credit"] = "بستانکار",
                }),
                F("IsPostable", "قابل ثبت؟", "bool"), F("RequiresParty", "نیاز به طرف؟", "bool"),
                F("IsActive", "فعال؟", "bool"),
            },
        },
        new AiEntityDef
        {
            Name = "trs_account", Fa = "صندوق و بانک‌ها", Module = "TrsAccounts", ClrType = typeof(TrsAccount),
            Hint = "حساب‌های خزانه: صندوق، بانک، کارتخوان؛ برای «لیست بانک‌ها»، «صندوق پیش‌فرض»",
            Fields = new()
            {
                F("Id", "کد"), F("Code", "کد"), F("Name", "نام حساب"),
                F("Kind", "نوع", "enum", new()
                {
                    ["Cash"] = "صندوق", ["Bank"] = "بانک", ["Pos"] = "کارتخوان",
                }),
                F("BankName", "بانک"), F("AccountNumber", "شماره حساب"),
                F("OpeningBalance", "موجودی اول", "money"),
                F("IsDefault", "پیش‌فرض؟", "bool"), F("IsActive", "فعال؟", "bool"),
                F("Description", "شرح", truncate: 80),
            },
        },
        new AiEntityDef
        {
            Name = "referral", Fa = "ارجاع‌ها", Module = "InnerLetters", ClrType = typeof(Erja),
            Scope = "referral", ScopeManageModule = "InnerLetters", ScopeManageAction = "ViewAll",
            Hint = "ارجاع‌های نامه‌ها؛ هر کس ارجاع‌های فرستاده/دریافت‌کرده خودش را می‌بیند",
            DefaultOrder = "ErjaId",
            Fields = new()
            {
                F("ErjaId", "کد ارجاع"), F("SourceId", "کد نامه", "number"),
                F("SenderUserId", "کد فرستنده", "number"), F("ReciverUserId", "کد گیرنده", "number"),
                F("Date", "تاریخ", "datetime"), F("Type", "نوع"),
                F("IsRead", "خوانده‌شده؟", "bool"), F("MatnErja", "متن ارجاع", truncate: 150),
                F("Answer", "پاسخ", truncate: 150), F("MohlatPasokh", "مهلت پاسخ", "date"),
                F("IsBayegani", "بایگانی؟", "bool"),
            },
        },
        new AiEntityDef
        {
            Name = "meeting_minutes", Fa = "صورتجلسات", Module = "MeetingMinutes", ModuleAction = "View",
            ClrType = typeof(MeetingMinutes),
            Hint = "صورتجلسات: عنوان، تاریخ جلسه، وضعیت؛ برای «صورتجلسات ماه گذشته»، «صورتجلسه درباره بودجه»",
            BaseFilters = new() { ("IsDeleted", "eq", "false") },
            Fields = new()
            {
                F("Id", "کد"), F("Title", "موضوع", truncate: 120),
                F("MeetingDate", "تاریخ جلسه", "date"),
                F("Status", "وضعیت", "enum", new() { ["InReview"] = "در حال بررسی", ["Closed"] = "اتمام نهایی" }),
                F("DateRegistered", "تاریخ ثبت", "datetime"),
                F("CreatedByName", "ثبت‌کننده"),
                F("ClosedAt", "تاریخ اتمام", "datetime"),
            },
        },
        new AiEntityDef
        {
            Name = "minutes_item", Fa = "بندهای صورتجلسه", Module = "MeetingMinutes", ModuleAction = "View",
            ClrType = typeof(MeetingMinutesItem),
            Hint = "بندهای صورتجلسات با مسئول و مهلت؛ برای «اقدام‌های باز»، «بندهای با سررسید این هفته»",
            BaseFilters = new() { ("Minutes.IsDeleted", "eq", "false") },
            Fields = new()
            {
                F("Id", "کد"), F("MinutesId", "کد صورتجلسه", "number"),
                F("Minutes.Title", "موضوع صورتجلسه", truncate: 100),
                F("RowNo", "ردیف", "number"),
                F("Description", "شرح بند", truncate: 150),
                F("ItemStatus", "وضعیت", "enum", new() { ["InProgress"] = "در جریان", ["Done"] = "انجام شد", ["Rejected"] = "رد شده" }),
                F("ResponsibleName", "مسئول"),
                F("DueDate", "مهلت", "date"),
                F("FollowUpName", "مسئول پیگیری"),
                F("FollowUpDate", "تاریخ پیگیری", "date"),
                F("FollowUpDecision", "تصمیم پیگیری", "enum", new() { ["Approved"] = "تأیید", ["Rejected"] = "رد" }),
            },
        },
    };

    public static AiEntityDef? Find(string name)
        => Entities.FirstOrDefault(e => e.Name.Equals(name?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));
}
