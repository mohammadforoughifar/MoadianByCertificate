using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.ReportStudio;

// =====================================================================
//  کاتالوگ اسکیما — «فهرست سفید» جدول‌ها، ستون‌ها و جوین‌های مجاز
//
//  این کلاس قلب امنیت گزارش‌ساز است: کلاینت هیچ‌وقت SQL یا نام واقعی
//  جدول/ستون نمی‌فرستد، فقط کلیدهای همین کاتالوگ را می‌فرستد. هر کلیدی
//  که اینجا نباشد، در اعتبارسنجی رد می‌شود.
//
//  افزودن جدول جدید = یک ورودی در Tables + (در صورت نیاز) چند Join
//  + یک case در RsQuerySource برای ساخت IQueryable آن.
// =====================================================================
public static class RsSchemaCatalog
{
    // ---------- کمکی‌های ساخت ستون ----------
    private static RsFieldDto F(string key, string title, RsFieldType type,
        bool group = true, bool agg = false, string? unit = null, string? hint = null,
        Dictionary<string, string>? enumMap = null) => new()
    {
        Key = key,
        Title = title,
        Type = type,
        Groupable = group,
        Aggregatable = agg,
        Unit = unit,
        Hint = hint,
        EnumMap = enumMap
    };

    private static RsFieldDto Text(string k, string t, string? hint = null) =>
        F(k, t, RsFieldType.Text, hint: hint);

    private static RsFieldDto Num(string k, string t, string? unit = null) =>
        F(k, t, RsFieldType.Number, group: false, agg: true, unit: unit);

    private static RsFieldDto Money(string k, string t) =>
        F(k, t, RsFieldType.Money, group: false, agg: true, unit: "ریال");

    private static RsFieldDto Date(string k, string t) =>
        F(k, t, RsFieldType.Date, agg: true);

    private static RsFieldDto Bool(string k, string t, string? hint = null) =>
        F(k, t, RsFieldType.Bool, hint: hint);

    private static RsFieldDto Enum(string k, string t, Dictionary<string, string> map) =>
        F(k, t, RsFieldType.Enum, enumMap: map);

    // ---------- نگاشت‌های Enum ----------
    private static Dictionary<string, string> LetterTypeMap => new()
    {
        ["1"] = "داخلی", ["2"] = "صادره", ["3"] = "وارده"
    };

    private static Dictionary<string, string> OutStatusMap => new()
    {
        ["0"] = "پیش‌نویس", ["1"] = "در گردش تایید", ["2"] = "تایید شده", ["3"] = "صادر شده"
    };

    private static Dictionary<string, string> LevelMap => new()
    {
        ["0"] = "عادی", ["1"] = "محرمانه", ["2"] = "خیلی محرمانه", ["3"] = "سرّی"
    };

    private static Dictionary<string, string> UrgencyMap => new()
    {
        ["0"] = "عادی", ["1"] = "فوری", ["2"] = "خیلی فوری", ["3"] = "آنی"
    };

    private static Dictionary<string, string> TaeedMap => new()
    {
        ["0"] = "بدون اقدام", ["1"] = "تایید", ["2"] = "رد"
    };

    private static Dictionary<string, string> InvoiceKindMap => new()
    {
        ["0"] = "فروش", ["1"] = "خرید", ["2"] = "برگشت از فروش", ["3"] = "برگشت از خرید"
    };

    private static Dictionary<string, string> InvoiceStatusMap => new()
    {
        ["0"] = "پیش‌نویس", ["1"] = "قطعی", ["2"] = "باطل"
    };

    private static Dictionary<string, string> WoStatusMap => new()
    {
        ["Open"] = "باز", ["Closed"] = "بسته", ["Cancelled"] = "لغوشده"
    };

    private static Dictionary<string, string> WoPriorityMap => new()
    {
        ["0"] = "کم", ["1"] = "عادی", ["2"] = "بالا", ["3"] = "فوری"
    };

    private static Dictionary<string, string> WoRecurrenceMap => new()
    {
        ["0"] = "بدون تکرار", ["1"] = "روزانه", ["2"] = "هفتگی", ["3"] = "ماهانه"
    };

    private static Dictionary<string, string> WoOutcomeMap => new()
    {
        ["open"] = "باز",
        ["late"] = "معوق",
        ["ontime"] = "انجام به‌موقع",
        ["latedone"] = "انجام با تأخیر",
        ["closednodone"] = "بسته بدون انجام"
    };

    private static Dictionary<string, string> WoDoneMap => new()
    {
        ["pending"] = "بدون پاسخ", ["done"] = "انجام شد", ["notdone"] = "انجام نشد"
    };

    private static Dictionary<string, string> WoDecisionMap => new()
    {
        ["none"] = "بدون تصمیم", ["Approved"] = "تایید", ["Rejected"] = "رد"
    };

    private static Dictionary<string, string> WoActionMap => new()
    {
        ["Created"] = "ثبت", ["Edited"] = "ویرایش", ["Seen"] = "رویت", ["Reply"] = "پاسخ",
        ["Approved"] = "تایید", ["Rejected"] = "رد", ["Extended"] = "تمدید", ["Closed"] = "بستن",
        ["Deleted"] = "حذف", ["Chained"] = "ارجاع زنجیره‌ای", ["Commented"] = "گفتگو"
    };

    // =====================================================================
    //  جدول‌ها
    // =====================================================================
    public static readonly IReadOnlyList<RsTableDto> Tables = new List<RsTableDto>
    {
        // ---------------- اتوماسیون اداری ----------------
        new()
        {
            Key = "letter",
            Title = "نامه‌ها (هر سه نوع)",
            Category = "اتوماسیون اداری",
            Icon = "bi-envelope-paper",
            Module = "InnerLetters",
            Description = "هر سطر یک نامه است — داخلی، صادره یا وارده. نقطهٔ شروع بیشتر گزارش‌های مکاتبات.",
            Fields = new()
            {
                Num("letter.id", "شناسه"),
                Enum("letter.type", "نوع نامه", LetterTypeMap),
                Text("letter.no", "شماره نامه"),
                Text("letter.title", "موضوع"),
                Date("letter.date", "تاریخ ثبت"),
                Date("letter.date_closed", "تاریخ صدور / تاریخ نامه"),
                Text("letter.creator", "ثبت‌کننده"),
                Text("letter.status", "وضعیت"),
                Enum("letter.conf", "محرمانگی", LevelMap),
                Enum("letter.urg", "فوریت", UrgencyMap),
                Text("letter.party", "طرف مقابل", "گیرندهٔ صادره یا فرستندهٔ وارده"),
                Text("letter.method", "روش ارسال"),
                Bool("letter.starred", "نشان‌دار"),
                Bool("letter.archived", "بایگانی‌شده"),
                Num("letter.erja_count", "تعداد ارجاع", "مورد"),
                Num("letter.days_open", "روزهای باز", "روز"),
                F("letter.count", "تعداد نامه", RsFieldType.Number,
                  group: false, agg: true, unit: "نامه", hint: "برای شمارش نامه‌ها از این سنجه استفاده کنید")
            }
        },
        new()
        {
            Key = "erja",
            Title = "ارجاع‌ها",
            Category = "اتوماسیون اداری",
            Icon = "bi-arrow-left-right",
            Module = "InnerLetters",
            Description = "هر سطر یک ارجاع نامه به یک کاربر است. برای تحلیل گردش کار و پاسخ‌گویی.",
            Fields = new()
            {
                Num("erja.id", "شناسه"),
                Text("erja.sender", "فرستنده"),
                Text("erja.receiver", "گیرنده"),
                Date("erja.date", "تاریخ ارجاع"),
                Text("erja.type", "نوع ارجاع", "گیرنده / ارجاع / هامش"),
                Enum("erja.taeed", "وضعیت تایید", TaeedMap),
                Bool("erja.is_read", "خوانده‌شده"),
                Bool("erja.answered", "پاسخ داده‌شده"),
                Bool("erja.overdue", "معوق", "مهلت گذشته و بی‌پاسخ"),
                Date("erja.deadline", "مهلت پاسخ"),
                Text("erja.note", "متن ارجاع"),
                F("erja.count", "تعداد ارجاع", RsFieldType.Number,
                  group: false, agg: true, unit: "مورد")
            }
        },

        new()
        {
            Key = "outgoing",
            Title = "نامه‌های صادره",
            Category = "اتوماسیون اداری",
            Icon = "bi-envelope-arrow-up",
            Module = "OutgoingLetters",
            Description = "جزئیات کامل نامه‌های صادره: گیرنده، دبیرخانه، روش ارسال و رهگیری.",
            Fields = new()
            {
                Num("outgoing.id", "شناسه"),
                Text("outgoing.no", "شماره نامه"),
                Text("outgoing.sadere_no", "شماره صادره"),
                Num("outgoing.number", "شماره ترتیبی"),
                Text("outgoing.title", "موضوع"),
                Date("outgoing.date_sabt", "تاریخ ثبت"),
                Date("outgoing.date_sadere", "تاریخ صدور"),
                Text("outgoing.creator", "ثبت‌کننده"),
                Enum("outgoing.status", "وضعیت", OutStatusMap),
                Text("outgoing.receiver_org", "سازمان گیرنده"),
                Text("outgoing.receiver_name", "نام گیرنده"),
                Text("outgoing.receiver_title", "سمت گیرنده"),
                Text("outgoing.method", "روش ارسال"),
                Text("outgoing.tracking", "کد رهگیری"),
                Text("outgoing.deliverer", "تحویل‌گیرنده"),
                Text("outgoing.dest_reg_no", "شماره ثبت مقصد"),
                Text("outgoing.dest_email", "ایمیل مقصد"),
                Text("outgoing.ext_ref", "شماره عطف خارجی"),
                Text("outgoing.copy_to", "رونوشت"),
                Text("outgoing.conf", "محرمانگی"),
                Text("outgoing.urg", "فوریت"),
                Bool("outgoing.dabirkhane", "ثبت دبیرخانه"),
                Date("outgoing.date_dabirkhane", "تاریخ دبیرخانه"),
                Bool("outgoing.starred", "نشان‌دار"),
                Num("outgoing.days_to_issue", "روز تا صدور", "روز"),
                F("outgoing.count", "تعداد نامه صادره", RsFieldType.Number,
                  group: false, agg: true, unit: "نامه")
            }
        },
        new()
        {
            Key = "incoming",
            Title = "نامه‌های وارده",
            Category = "اتوماسیون اداری",
            Icon = "bi-envelope-arrow-down",
            Module = "IncomingLetters",
            Description = "جزئیات کامل نامه‌های وارده: فرستنده، شماره ثبت و وضعیت بایگانی.",
            Fields = new()
            {
                Num("incoming.id", "شناسه"),
                Text("incoming.no", "شماره نامه"),
                Num("incoming.number_sabt", "شماره ثبت"),
                Text("incoming.number_varede", "شماره نامهٔ فرستنده"),
                Text("incoming.title", "موضوع"),
                Date("incoming.date", "تاریخ نامه"),
                Date("incoming.date_ersal", "تاریخ دریافت"),
                Text("incoming.creator", "ثبت‌کننده"),
                Text("incoming.sender", "فرستنده"),
                Text("incoming.method", "نحوهٔ ارسال"),
                Text("incoming.delivery_name", "تحویل‌دهنده"),
                Enum("incoming.conf", "محرمانگی", LevelMap),
                Enum("incoming.urg", "فوریت", UrgencyMap),
                Bool("incoming.archived", "بایگانی‌شده"),
                Bool("incoming.starred", "نشان‌دار"),
                Text("incoming.description", "توضیحات"),
                Num("incoming.days_open", "روزهای باز", "روز"),
                F("incoming.count", "تعداد نامه وارده", RsFieldType.Number,
                  group: false, agg: true, unit: "نامه")
            }
        },

        // ---------------- فروش و خرید ----------------
        new()
        {
            Key = "invoice",
            Title = "فاکتورها",
            Category = "فروش و خرید",
            Icon = "bi-receipt",
            Module = "FacInvoices",
            Description = "سرجمع هر فاکتور فروش/خرید.",
            Fields = new()
            {
                Num("invoice.id", "شناسه"),
                Num("invoice.number", "شماره فاکتور"),
                Enum("invoice.kind", "نوع", InvoiceKindMap),
                Enum("invoice.status", "وضعیت", InvoiceStatusMap),
                Date("invoice.date", "تاریخ"),
                Date("invoice.due", "سررسید"),
                Text("invoice.party", "طرف حساب"),
                Text("invoice.warehouse", "انبار"),
                Money("invoice.gross", "مبلغ ناخالص"),
                Money("invoice.discount", "تخفیف"),
                Money("invoice.vat", "مالیات"),
                Money("invoice.net", "مبلغ خالص"),
                F("invoice.count", "تعداد فاکتور", RsFieldType.Number,
                  group: false, agg: true, unit: "فاکتور")
            }
        },
        new()
        {
            Key = "invoice_line",
            Title = "اقلام فاکتور",
            Category = "فروش و خرید",
            Icon = "bi-list-ul",
            Module = "FacInvoices",
            Description = "هر سطر یک قلم کالا در فاکتور. برای تحلیل فروش بر اساس کالا.",
            Fields = new()
            {
                Text("invoice_line.product", "کالا"),
                Text("invoice_line.product_code", "کد کالا"),
                Text("invoice_line.category", "دستهٔ کالا"),
                Text("invoice_line.unit", "واحد"),
                Num("invoice_line.qty", "مقدار"),
                Money("invoice_line.price", "فی"),
                Money("invoice_line.discount", "تخفیف"),
                Money("invoice_line.total", "مبلغ کل"),
                F("invoice_line.count", "تعداد قلم", RsFieldType.Number,
                  group: false, agg: true, unit: "قلم")
            }
        },

        // ---------------- پایه ----------------
        new()
        {
            Key = "party",
            Title = "طرف حساب‌ها",
            Category = "اطلاعات پایه",
            Icon = "bi-people",
            Module = "Parties",
            Description = "مشتریان و تأمین‌کنندگان.",
            Fields = new()
            {
                Num("party.id", "شناسه"),
                Text("party.name", "نام"),
                Text("party.mobile", "موبایل"),
                Text("party.phone", "تلفن"),
                Bool("party.is_active", "فعال"),
                F("party.count", "تعداد طرف حساب", RsFieldType.Number,
                  group: false, agg: true, unit: "نفر")
            }
        },
        new()
        {
            Key = "product",
            Title = "کالاها",
            Category = "اطلاعات پایه",
            Icon = "bi-box-seam",
            Module = "Products",
            Description = "فهرست کالاها و خدمات.",
            Fields = new()
            {
                Num("product.id", "شناسه"),
                Text("product.name", "نام کالا"),
                Text("product.code", "کد"),
                Text("product.category", "دسته"),
                Text("product.unit", "واحد"),
                Money("product.sale_price", "قیمت فروش"),
                Money("product.purchase_price", "قیمت خرید"),
                Bool("product.is_active", "فعال"),
                F("product.count", "تعداد کالا", RsFieldType.Number,
                  group: false, agg: true, unit: "قلم")
            }
        },

        // ---------------------------------------------------------------------
        //  انبار و موجودی
        // ---------------------------------------------------------------------
        new()
        {
            Key = "stock",
            Title = "موجودی انبار",
            Category = "انبار و موجودی",
            Icon = "bi-boxes",
            Module = "Stock",
            Description = "موجودیِ کالا به تفکیک انبار — برای گزارشِ کالاهای موجود، ارزشِ ریالیِ موجودی و کالاهای رو به اتمام.",
            Fields = new()
            {
                Num("stock.id", "شناسه"),
                Text("stock.product", "نام کالا"),
                Text("stock.code", "کد کالا"),
                Text("stock.warehouse", "انبار"),
                F("stock.quantity", "مقدار موجود", RsFieldType.Number,
                  group: false, agg: true, unit: "عدد"),
                Money("stock.avg_cost", "میانگین قیمت تمام‌شده"),
                Money("stock.value", "ارزش موجودی")
            }
        },

        // ---------------------------------------------------------------------
        //  آرشیو اسناد و مدارک
        // ---------------------------------------------------------------------
        new()
        {
            Key = "document",
            Title = "مدارک آرشیو",
            Category = "آرشیو اسناد",
            Icon = "bi-archive",
            Module = "DocArchive",
            Description = "فهرست مدارکِ آرشیو به همراه پوشه و تاریخ انقضا — برای گزارشِ مدارکِ منقضی‌شده و در آستانه انقضا.",
            Fields = new()
            {
                Num("document.id", "شناسه"),
                Text("document.code", "کد مدرک"),
                Text("document.title", "عنوان مدرک"),
                Text("document.folder", "پوشه"),
                Text("document.customer_code", "کد مشتری"),
                Date("document.expire_date", "تاریخ انقضا"),
                Bool("document.is_public", "عمومی"),
                Bool("document.require_confirm", "محرمانه"),
                F("document.count", "تعداد مدارک", RsFieldType.Number,
                  group: false, agg: true, unit: "فقره")
            }
        },

        // ---------------------------------------------------------------------
        //  تعمیرات
        // ---------------------------------------------------------------------
        new()
        {
            Key = "repair",
            Title = "دستورهای تعمیر",
            Category = "تعمیرات",
            Icon = "bi-wrench",
            Module = "Repairs",
            Description = "دستگاه‌های پذیرش‌شده برای تعمیر، وضعیت و مبلغ اعلامی.",
            Fields = new()
            {
                Num("repair.id", "شناسه"),
                Text("repair.number", "شماره پذیرش"),
                Text("repair.party", "مشتری"),
                Text("repair.device_type", "نوع دستگاه"),
                Text("repair.device_model", "مدل دستگاه"),
                Text("repair.technician", "تکنسین"),
                Text("repair.status", "وضعیت"),
                Date("repair.received_at", "تاریخ پذیرش"),
                Date("repair.delivered_at", "تاریخ تحویل"),
                Money("repair.quoted_price", "مبلغ اعلامی"),
                F("repair.count", "تعداد دستورها", RsFieldType.Number,
                  group: false, agg: true, unit: "مورد")
            }
        },

        // ---------------------------------------------------------------------
        //  پروژه‌ها
        // ---------------------------------------------------------------------
        new()
        {
            Key = "project",
            Title = "پروژه‌ها",
            Category = "پروژه‌ها",
            Icon = "bi-kanban",
            Module = "Projects",
            Description = "پروژه‌های ثبت‌شده با کد، نام، تحویل‌گیرنده و تاریخ‌های خروج/ورود.",
            Fields = new()
            {
                Num("project.id", "شناسه"),
                Text("project.code", "کد پروژه"),
                Text("project.name", "نام پروژه"),
                Text("project.receiver", "تحویل‌گیرنده"),
                Text("project.serial", "شماره سریال"),
                Date("project.exit_date", "تاریخ خروج"),
                Date("project.entry_date", "تاریخ ورود"),
                F("project.count", "تعداد پروژه‌ها", RsFieldType.Number,
                  group: false, agg: true, unit: "پروژه")
            }
        },

        // ---------------------------------------------------------------------
        //  مالی و خزانه‌داری
        // ---------------------------------------------------------------------
        new()
        {
            Key = "transaction",
            Title = "تراکنش‌های مالی",
            Category = "مالی و خزانه",
            Icon = "bi-cash-coin",
            Module = "Transactions",
            Description = "دریافت‌ها و پرداخت‌ها به همراه مبلغ، روش پرداخت و مبلغ تسویه‌شده.",
            Fields = new()
            {
                Num("transaction.id", "شناسه"),
                Text("transaction.number", "شماره"),
                Text("transaction.type", "نوع تراکنش"),
                Date("transaction.date", "تاریخ"),
                Text("transaction.party", "طرف حساب"),
                Text("transaction.payment_method", "روش پرداخت"),
                Money("transaction.amount", "مبلغ"),
                Money("transaction.settled_amount", "مبلغ تسویه‌شده"),
                Date("transaction.due_date", "تاریخ سررسید"),
                F("transaction.count", "تعداد تراکنش‌ها", RsFieldType.Number,
                  group: false, agg: true, unit: "فقره")
            }
        },
        new()
        {
            Key = "expense",
            Title = "هزینه‌ها",
            Category = "مالی و خزانه",
            Icon = "bi-receipt",
            Module = "Expenses",
            Description = "هزینه‌های ثبت‌شده به تفکیک دسته‌بندی و گیرنده.",
            Fields = new()
            {
                Num("expense.id", "شناسه"),
                Text("expense.number", "شماره"),
                Text("expense.category", "دسته‌بندی هزینه"),
                Text("expense.payee", "گیرنده"),
                Text("expense.pay_type", "نوع پرداخت"),
                Date("expense.date", "تاریخ"),
                Money("expense.amount", "مبلغ"),
                F("expense.count", "تعداد هزینه‌ها", RsFieldType.Number,
                  group: false, agg: true, unit: "فقره")
            }
        },

        // --------------------------------------------------------------------
        //  دستور کار
        //  دستورهای تعمیر (repair) ماژول دیگری است و اینجا تکرار نمی‌شود.
        // --------------------------------------------------------------------
        new()
        {
            Key = "workorder",
            Title = "دستورهای کار",
            Category = "دستور کار",
            Icon = "bi-list-task",
            Module = "WorkOrders",
            Description = "دستورهای کار باز و بسته. هر کاربر دستورهایی را می‌بیند که خودش داده یا به او محول شده؛ مدیر سیستم همه را می‌بیند.",
            Fields = new()
            {
                Num("workorder.id", "شناسه"),
                Text("workorder.number", "شماره"),
                Text("workorder.title", "عنوان"),
                Text("workorder.description", "شرح"),
                Text("workorder.owner", "دستوردهنده"),
                Enum("workorder.status", "وضعیت", WoStatusMap),
                Enum("workorder.priority", "اولویت", WoPriorityMap),
                Enum("workorder.recurrence", "تکرار", WoRecurrenceMap),
                Enum("workorder.outcome", "نتیجه", WoOutcomeMap),
                Date("workorder.due_at", "مهلت"),
                Date("workorder.created_at", "تاریخ ثبت"),
                Date("workorder.closed_at", "تاریخ بستن"),
                Text("workorder.close_note", "یادداشت بستن"),
                Num("workorder.extension_count", "تعداد تمدید"),
                Text("workorder.tags", "برچسب‌ها"),
                Text("workorder.source", "مبدأ"),
                Num("workorder.source_id", "شناسه مبدأ"),
                Text("workorder.parent_no", "شماره دستور والد"),
                Text("workorder.assignees", "گیرندگان"),
                Num("workorder.assignee_count", "تعداد گیرنده"),
                Num("workorder.checklist_done", "آیتم انجام‌شده"),
                Num("workorder.checklist_total", "کل چک‌لیست"),
                Num("workorder.progress", "درصد پیشرفت"),
                Num("workorder.comment_count", "تعداد گفتگو"),
                Num("workorder.attachment_count", "تعداد پیوست"),
                Num("workorder.days_left", "روز مانده تا مهلت"),
                Bool("workorder.overdue", "معوق"),
                F("workorder.count", "تعداد دستور کار", RsFieldType.Number,
                  group: false, agg: true, unit: "مورد")
            }
        },
        new()
        {
            Key = "wo_assignee",
            Title = "گیرندگان دستور کار",
            Category = "دستور کار",
            Icon = "bi-people",
            Module = "WorkOrders",
            Description = "هر سطر یک گیرندهٔ دستور کار است: رویت، پاسخ، انجام و تصمیم دستوردهنده.",
            Fields = new()
            {
                Num("wo_assignee.id", "شناسه"),
                Num("wo_assignee.order_id", "شناسه دستور"),
                Text("wo_assignee.order_no", "شماره دستور"),
                Text("wo_assignee.order_title", "عنوان دستور"),
                Enum("wo_assignee.order_status", "وضعیت دستور", WoStatusMap),
                Date("wo_assignee.order_due", "مهلت دستور"),
                Text("wo_assignee.owner", "دستوردهنده"),
                Text("wo_assignee.user", "گیرنده"),
                Date("wo_assignee.seen_at", "تاریخ رویت"),
                Date("wo_assignee.replied_at", "تاریخ پاسخ"),
                Enum("wo_assignee.result", "نتیجه پاسخ", WoDoneMap),
                Text("wo_assignee.reply", "متن پاسخ"),
                Enum("wo_assignee.decision", "تصمیم دستوردهنده", WoDecisionMap),
                Text("wo_assignee.decision_note", "یادداشت تصمیم"),
                Bool("wo_assignee.late", "پاسخ دیر یا مهلت گذشته"),
                F("wo_assignee.count", "تعداد گیرنده", RsFieldType.Number,
                  group: false, agg: true, unit: "نفر")
            }
        },
        new()
        {
            Key = "wo_checklist",
            Title = "چک‌لیست دستور کار",
            Category = "دستور کار",
            Icon = "bi-check2-square",
            Module = "WorkOrders",
            Description = "آیتم‌های چک‌لیست هر دستور کار و اینکه چه کسی تیک زده است.",
            Fields = new()
            {
                Num("wo_checklist.id", "شناسه"),
                Num("wo_checklist.order_id", "شناسه دستور"),
                Text("wo_checklist.order_no", "شماره دستور"),
                Text("wo_checklist.order_title", "عنوان دستور"),
                Text("wo_checklist.text", "آیتم"),
                Num("wo_checklist.sort", "ترتیب"),
                Bool("wo_checklist.is_done", "انجام شده"),
                Text("wo_checklist.done_by", "انجام‌دهنده"),
                Date("wo_checklist.done_at", "تاریخ انجام"),
                F("wo_checklist.count", "تعداد آیتم", RsFieldType.Number,
                  group: false, agg: true, unit: "آیتم")
            }
        },
        new()
        {
            Key = "wo_comment",
            Title = "گفتگوی دستور کار",
            Category = "دستور کار",
            Icon = "bi-chat-left-text",
            Module = "WorkOrders",
            Description = "کامنت‌های داخل دستور کار، شامل پاسخ‌ها.",
            Fields = new()
            {
                Num("wo_comment.id", "شناسه"),
                Num("wo_comment.order_id", "شناسه دستور"),
                Text("wo_comment.order_no", "شماره دستور"),
                Text("wo_comment.order_title", "عنوان دستور"),
                Text("wo_comment.author", "نویسنده"),
                Text("wo_comment.text", "متن"),
                Date("wo_comment.created_at", "تاریخ"),
                Bool("wo_comment.is_reply", "پاسخ به کامنت دیگر"),
                Bool("wo_comment.is_deleted", "حذف‌شده"),
                F("wo_comment.count", "تعداد گفتگو", RsFieldType.Number,
                  group: false, agg: true, unit: "مورد")
            }
        },
        new()
        {
            Key = "wo_log",
            Title = "تاریخچه دستور کار",
            Category = "دستور کار",
            Icon = "bi-clock-history",
            Module = "WorkOrders",
            Description = "رویدادهای دستور کار: ثبت، رویت، پاسخ، تمدید، بستن و حذف.",
            Fields = new()
            {
                Num("wo_log.id", "شناسه"),
                Num("wo_log.order_id", "شناسه دستور"),
                Text("wo_log.order_no", "شماره دستور"),
                Text("wo_log.order_title", "عنوان دستور"),
                Text("wo_log.actor", "انجام‌دهنده"),
                Enum("wo_log.action", "رویداد", WoActionMap),
                Text("wo_log.text", "شرح رویداد"),
                Date("wo_log.created_at", "تاریخ"),
                F("wo_log.count", "تعداد رویداد", RsFieldType.Number,
                  group: false, agg: true, unit: "مورد")
            }
        },
        new()
        {
            Key = "wo_attachment",
            Title = "پیوست‌های دستور کار",
            Category = "دستور کار",
            Icon = "bi-paperclip",
            Module = "WorkOrders",
            Description = "فهرست فایل‌های پیوست دستور کار (بدون محتوای فایل).",
            Fields = new()
            {
                Num("wo_attachment.id", "شناسه"),
                Num("wo_attachment.order_id", "شناسه دستور"),
                Text("wo_attachment.order_no", "شماره دستور"),
                Text("wo_attachment.order_title", "عنوان دستور"),
                Text("wo_attachment.file_name", "نام فایل"),
                Text("wo_attachment.content_type", "نوع فایل"),
                Text("wo_attachment.uploader", "بارگذار"),
                Date("wo_attachment.uploaded_at", "تاریخ بارگذاری"),
                F("wo_attachment.count", "تعداد پیوست", RsFieldType.Number,
                  group: false, agg: true, unit: "فایل")
            }
        }
    };

    // =====================================================================
    //  جوین‌های مجاز
    //
    //  فقط این مسیرها قابل استفاده‌اند. کاربر نمی‌تواند دو جدول بی‌ربط را
    //  به هم بچسباند و ضرب دکارتی بسازد.
    // =====================================================================
    public static readonly IReadOnlyList<RsJoinPathDto> Joins = new List<RsJoinPathDto>
    {
        new()
        {
            Key = "letter->erja",
            FromTable = "letter",
            ToTable = "erja",
            Title = "نامه ← ارجاع‌های آن",
            Description = "هر نامه می‌تواند چند ارجاع داشته باشد؛ سطرهای نامه به تعداد ارجاع‌ها تکرار می‌شوند.",
            Multiplies = true
        },
        new()
        {
            Key = "outgoing->erja",
            FromTable = "outgoing",
            ToTable = "erja",
            Title = "نامه صادره ← ارجاع‌های آن",
            Description = "گردش تایید و ارجاع نامه‌های صادره.",
            Multiplies = true
        },
        new()
        {
            Key = "incoming->erja",
            FromTable = "incoming",
            ToTable = "erja",
            Title = "نامه وارده ← ارجاع‌های آن",
            Description = "ارجاع نامه‌های وارده به کاربران.",
            Multiplies = true
        },
        new()
        {
            Key = "invoice->invoice_line",
            FromTable = "invoice",
            ToTable = "invoice_line",
            Title = "فاکتور ← اقلام آن",
            Description = "هر فاکتور چند قلم دارد؛ سطرها تکرار می‌شوند.",
            Multiplies = true
        },
        new()
        {
            Key = "invoice->party",
            FromTable = "invoice",
            ToTable = "party",
            Title = "فاکتور ← طرف حساب",
            Description = "اطلاعات کامل مشتری/تأمین‌کنندهٔ فاکتور.",
            Multiplies = false
        },
        new()
        {
            Key = "invoice_line->product",
            FromTable = "invoice_line",
            ToTable = "product",
            Title = "قلم فاکتور ← کالا",
            Description = "مشخصات کامل کالای هر قلم.",
            Multiplies = false
        },
        new()
        {
            Key = "workorder->wo_assignee",
            FromTable = "workorder",
            ToTable = "wo_assignee",
            Title = "دستور کار ← گیرندگان",
            Description = "هر دستور می‌تواند چند گیرنده داشته باشد؛ سطرها تکرار می‌شوند.",
            Multiplies = true
        },
        new()
        {
            Key = "workorder->wo_checklist",
            FromTable = "workorder",
            ToTable = "wo_checklist",
            Title = "دستور کار ← چک‌لیست",
            Description = "آیتم‌های چک‌لیست همان دستور. سطرها به تعداد آیتم‌ها تکرار می‌شوند.",
            Multiplies = true
        },
        new()
        {
            Key = "workorder->wo_comment",
            FromTable = "workorder",
            ToTable = "wo_comment",
            Title = "دستور کار ← گفتگو",
            Description = "کامنت‌های داخل دستور کار. سطرها تکرار می‌شوند.",
            Multiplies = true
        },
        new()
        {
            Key = "workorder->wo_log",
            FromTable = "workorder",
            ToTable = "wo_log",
            Title = "دستور کار ← تاریخچه",
            Description = "رویدادهای تاریخچهٔ همان دستور. سطرها تکرار می‌شوند.",
            Multiplies = true
        },
        new()
        {
            Key = "workorder->wo_attachment",
            FromTable = "workorder",
            ToTable = "wo_attachment",
            Title = "دستور کار ← پیوست‌ها",
            Description = "فایل‌های پیوست همان دستور. سطرها تکرار می‌شوند.",
            Multiplies = true
        }
    };

    // =====================================================================
    //  جست‌وجوها
    // =====================================================================

    public static RsTableDto? FindTable(string? key) =>
        key is null ? null : Tables.FirstOrDefault(t =>
            t.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static RsFieldDto? FindField(string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return null;
        var dot = fieldKey.IndexOf('.');
        if (dot <= 0) return null;
        var table = FindTable(fieldKey[..dot]);
        return table?.Fields.FirstOrDefault(f =>
            f.Key.Equals(fieldKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>جدولی که این ستون به آن تعلق دارد.</summary>
    public static string? TableOfField(string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return null;
        var dot = fieldKey.IndexOf('.');
        return dot <= 0 ? null : fieldKey[..dot];
    }

    public static RsJoinPathDto? FindJoin(string? key) =>
        key is null ? null : Joins.FirstOrDefault(j =>
            j.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>جوین‌هایی که از یک جدولِ حاضر در گزارش به جدول تازه می‌روند.</summary>
    public static IEnumerable<RsJoinPathDto> JoinsFrom(IEnumerable<string> presentTables)
    {
        var set = new HashSet<string>(presentTables, StringComparer.OrdinalIgnoreCase);
        return Joins.Where(j => set.Contains(j.FromTable) && !set.Contains(j.ToTable));
    }
}
