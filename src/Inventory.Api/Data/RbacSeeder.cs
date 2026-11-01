using Inventory.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public static class RbacSeeder
{
    // اکشن‌های استاندارد CRUD
    private static readonly string[] CrudActions = { "Create", "Read", "Update", "Delete", "Export" };

    // ماژول‌ها و اکشن‌هایشان — منبع واحد حقیقت برای پرمیشن‌ها
    // ماژول‌های جدید (داشبوردها/گزارش‌ها) به‌صورت خودکار به دیتابیس‌های موجود هم اضافه می‌شوند.
    private static readonly Dictionary<string, string[]> ModuleActions = new()
    {
        ["Products"] = CrudActions,
        ["Stock"] = CrudActions,
        // فروش: + دسترسی «مشاهده سود» — قابل مدیریت به ازای هر نقش
        ["Orders"] = new[] { "Create", "Read", "Update", "Delete", "Export", "ViewProfit" },
        // حواله تحویل کالا — سند «فقط مقدار» (بدون قیمت)
        ["GoodsIssues"] = CrudActions,
        ["Repairs"] = CrudActions,
        ["CctvCameras"] = CrudActions,
        ["CctvNvrs"] = CrudActions,
        // گاوصندوق رمزهای تجهیزات — Reveal: نمایش/کپی رمز (با تأیید دوبارهٔ رمز ورود) | ViewLog: لاگ دسترسی‌ها
        ["ItVault"] = new[] { "View", "Create", "Update", "Delete", "Reveal", "ViewLog" },
        // مدیریت آی‌پی (IPAM) — Scan: اسکن رنج و پینگ آی‌پی | Update: ویرایش رنج و رزرو آی‌پی
        ["Ipam"] = new[] { "View", "Create", "Update", "Delete", "Scan" },
        // سرورهای HPE/iLO — Collect فقط خواندنی است (Health/Inventory از Redfish) و عملیات پاور ندارد.
        ["Servers"] = new[] { "View", "Create", "Update", "Delete", "Collect" },
        // طرف حساب — ماژول قدیمی «Parties» هر دو نوع را پوشش می‌دهد و برای سازگاری باقی می‌ماند.
        // برای تفکیک دسترسی «مشتری» از «تأمین‌کننده» این دو ماژول مستقل هم تعریف می‌شوند:
        //   Customers  = مشتریان (ویزیتور فقط این را می‌گیرد ⇒ تأمین‌کننده‌ها را هرگز نمی‌بیند)
        //   Suppliers  = تأمین‌کنندگان
        // کنترلر طرف حساب مجوز را بر اساس نوع سطر از همین دو ماژول می‌سنجد.
        ["Parties"] = CrudActions,
        ["Customers"] = CrudActions,
        ["Suppliers"] = CrudActions,
        ["Expenses"] = CrudActions,
        ["Reports"] = CrudActions,
        ["SystemUsers"] = CrudActions,
        ["SystemCompanies"] = CrudActions,   // کمپانی‌ها
        ["SystemDepartments"] = CrudActions, // واحدها (دپارتمان‌ها)
        // ================== درخواست خدمت آی‌تی ==================
        // Create: ثبت درخواست | ViewCompany: دیدن سیستم‌های کل شرکت | ViewDepartment: دیدن سیستم‌های واحد خود
        // Expert: کارشناس آی‌تی (دریافت ارجاع) | Manage: مدیر آی‌تی (ارجاع و تایید)
        ["ItRequests"] = new[] { "Create", "ViewCompany", "ViewDepartment", "Expert", "Manage" },
        // ================== دستور کار ==================
        // View: مشاهده ماژول (مبنای نمایش در منو) | Create: ساخت دستور کار (حداقل برای خود)
        // AssignOthers: دستور کار به دیگران | Delete: حذف — فقط با مجوز مستقل، به نقش‌های عادی خودکار اعطا نمی‌شود
        ["WorkOrders"] = new[] { "View", "Create", "AssignOthers", "Delete" },
        // ================== پروژه کاری (داخل دستور کار) ==================
        // View: مشاهده و منو | Create: ساخت پروژه
        // Update: ویرایش/تمدید/بستن/مدیریت اعضا برای غیرمالک (مالک همیشه مجاز است) | Delete: حذف
        //
        // اکشن‌های بخشی (هر «قسمت» پروژه جداگانه): دسترسی به تب‌های صفحهٔ پروژه را
        // کنترل می‌کنند تا مدیر بتواند مثلاً فقط «خلاصه» و «دستورها» را بدهد و
        // «نگار ذهنی» یا «اعضا» را ندهد:
        //   Summary  → تب خلاصه (اطلاعات، تاریخ‌ها، وضعیت)
        //   Orders   → تب دستورهای کار پروژه
        //   MindFlow → تب نگار ذهنی همان پروژه
        //   Members  → تب اعضا (فهرست اعضای پروژه)
        //   Activity → تب فعالیت‌ها (تاریخچهٔ تغییرات)
        ["WorkProjects"] = new[] { "View", "Create", "Update", "Delete", "Summary", "Orders", "MindFlow", "Members", "Activity" },
        // ================== نگار ذهنی (نقشهٔ ذهنی) ==================
        // نگار ذهنی دو جا دیده می‌شود: تب داخل پروژهٔ کاری (با مجوز بخشی WorkProjects.MindFlow)
        // و صفحهٔ مستقل «نگار ذهنی» (نگاشت‌های مستقل). این ماژول دومی را کنترل می‌کند:
        //   View    → دیدن صفحهٔ نگار ذهنی، فهرست نگاشت‌های مجاز و نمایش در منو
        //   Create  → ساخت نگاشت مستقل تازه
        //   Update  → ویرایش محتوای نگاشت (گره/رابط/جابه‌جایی) — همچنان نیازمند نقش ویرایشگر
        //   Delete  → حذف نگاشت (فقط سازنده) — بدون این مجوز، دکمهٔ حذف کار نمی‌کند
        //   Members → مدیریت دسترسی و اعضای نگاشت (فقط سازنده)
        ["MindFlow"] = new[] { "View", "Create", "Update", "Delete", "Members" },
        // ================== میز کار توسعه (مدیریت تیم نرم‌افزار) ==================
        // View: منو و مشاهده | Create: تسک/مشکل/changelog | Update: ویرایش و جابجایی وضعیت
        // Delete: حذف | Manage: تنظیم وضعیت‌ها/ماژول‌ها/اسپرینت | Assign: تخصیص به دیگران
        ["DevTeam"] = new[] { "View", "Create", "Update", "Delete", "Manage", "Assign" },
        // Manage: عملیات امنیتی/زیرساختی تنظیمات (مانند کلیدهای Push) که نباید صرفاً با Update عمومی مجاز شوند.
        ["Settings"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Manage" },
        // تنظیمات شخصی کاربر: مشاهده صفحه، تغییر رمز و مدیریت گروه‌های شخصی گیرندگان نامه
        ["PersonalSettings"] = new[] { "Read", "ChangePassword", "ManageLetterGroups" },
        ["Warehouses"] = CrudActions,
        // ================== ماژول انبارداری ==================
        // InvDocs: اسناد رسید و حواله — Confirm: قطعی‌سازی | Cancel: ابطال
        ["InvDocs"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm", "Cancel" },
        // InvDocTypes: مدیریت انواع رسید/حواله و ماهیت آن‌ها
        ["InvDocTypes"] = new[] { "Create", "Read", "Update", "Delete", "Export", "ViewCode" },
        // ================== ماژول حسابداری ==================
        // AccAccounts: کدینگ حساب‌ها، سال مالی و قواعد سند خودکار
        ["AccAccounts"] = CrudActions,
        // AccVouchers: اسناد حسابداری — Confirm: قطعی‌سازی | Cancel: ابطال
        ["AccVouchers"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm", "Cancel" },
        // AccDimensions: ابعاد تحلیلی (مراکز هزینه / شعبه) و مقادیر آن‌ها
        ["AccDimensions"] = CrudActions,
        // FixedAssets: دارایی ثابت — Confirm: ارسال استهلاک به حسابداری
        ["FixedAssets"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm" },
        // Budgets: بودجه و کنترل بودجه
        ["Budgets"] = CrudActions,
        // ================== ماژول فاکتور ==================
        // FacInvoices: فاکتور خرید/فروش — Confirm: قطعی‌سازی | Cancel: ابطال
        ["FacInvoices"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm", "Cancel" },
        // ================== ماژول خزانه‌داری ==================
        // TrsAccounts: صندوق، بانک، کارتخوان، تنخواه و تنظیمات خزانه
        ["TrsAccounts"] = CrudActions,
        // TrsVouchers: اسناد دریافت/پرداخت/انتقال — Confirm: قطعی‌سازی | Cancel: ابطال
        ["TrsVouchers"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm", "Cancel" },
        // TrsCheques: چک‌ها — Confirm: اجرای عملیات چک (واگذاری، وصول، برگشت)
        ["TrsCheques"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm" },
        // ================== سامانه مودیان مبتنی بر گواهی امضا (SDK 2.0.29) ==================
        // View: مشاهده داشبورد و صورتحساب‌ها | Certificate: پرونده مودی و گواهی امضا | MasterData: خریداران/کالاها/واحدها
        // InvoiceCreate/Update/Delete/Send/Inquiry: چرخه صورتحساب | FolderStatus: استعلام کارپوشه و ماده ۶ | PaymentRegister: ثبت پرداخت | Diagnose: عیب‌یابی
        ["Moadian"] = new[] { "View", "Certificate", "MasterData", "InvoiceCreate", "InvoiceUpdate", "InvoiceDelete", "InvoiceSend", "InvoiceInquiry", "FolderStatus", "PaymentRegister", "Diagnose" },
        // ShopGuide: راهنمای کاربری «سامانه فروشگاهی» — فقط مشاهده (بدون دسترسی به اسناد)
        ["ShopGuide"] = new[] { "View" },
        // ================== ماژول انبارگردانی و بارکد ==================
        // StkSessions: دوره‌های انبارگردانی — Apply: صدور اسناد اصلاح | Cancel: لغو دوره
        ["StkSessions"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Apply", "Cancel" },
        // StkBarcodes: بارکدهای کالا، تولید گروهی و چاپ برچسب
        ["StkBarcodes"] = CrudActions,
        // ================== بخش معرف ==================
        ["Referrers"] = CrudActions,                                     // مدیریت معرف‌ها
        ["ReferrerWallets"] = new[] { "Read", "Update", "Export" },      // کیف پول معرف‌ها
        ["ReferrerPanel"] = new[] { "MyDashboard", "MyProducts", "MyWallet", "MyCard" }, // پنل معرف (هر بخش جدا)
        // ================== ماژول ویزیتوری (سفارش میدانی) ==================
        // View: مشاهدهٔ منو و فهرست | Create: ثبت سفارش | Update: ویرایش پیش‌نویس/بازگردانی از «برگشتی»
        // Submit: ارسال برای تأیید | Cancel: ابطال سفارش خودش
        // Approve: تأیید/برگشت/رد مدیر فروش | SetDiscountCap: تعیین سقف تخفیف ویزیتور
        // AssignCustomer: تخصیص مشتری به ویزیتور | FinanceView: صف مالی و ساخت پیش‌نویس فاکتور | Export
        ["SalesVisit"] = new[] { "View", "Create", "Update", "Submit", "Cancel", "Approve", "SetDiscountCap", "AssignCustomer", "FinanceView", "Export" },
        // ================== منابع انسانی: مرخصی و ماموریت ==================
        // Request: ثبت درخواست شخصی | Approve: تایید/رد درخواست دیگران | Report: گزارش ماهانه
        ["LeaveRequests"] = new[] { "Request", "Approve", "Report" },
        // ================== حضور و غیاب ==================
        // SelfCheckin: زدن ورود/خروج برای خود
        // ViewAll: مشاهده لاگ همه پرسنل | ManageShifts: مدیریت شیفت‌ها و اصلاح رکوردها | Report: گزارش ماهانه
        ["Attendance"] = new[] { "SelfCheckin", "ViewAll", "ManageShifts", "Report" },
        // ================== سامانه کامل RADIS-HR V019 ==================
        // HrTalent و جذب/استعداد فعلاً زیرمجموعهٔ HrCore هستند؛ بنابراین permission مستقل HrTalent تعریف نمی‌شود.
        // Access: ورود به کل ماژول؛ مجوز روی تمام کنترلرهای واردشده نیز در Host اعمال می‌شود.
        ["RadisHr"] = new[] { "Access", "Dashboard", "Employees", "EmployeeEntry", "Attendance", "Payroll", "StatutoryRules", "OrgStructure", "OrgSettings", "Hse", "Finance", "Accounting", "ProductionDaily", "Notices" /* دسترسی تفکیکی هر لینک بن‌سازه */ },
        // ================== دسترسی به ازای هر داشبورد ==================
        ["Dashboards"] = new[] { "Financial", "Management", "Hardware" },
        // ================== دسترسی به ازای هر گزارش ==================
        ["ReportPages"] = new[] { "Kardex", "Reorder" },
        // ================== مدیریت پروژه‌ها ==================
        // ViewFactor: رویت ستون‌های شماره/نوع فاکتور در لیست و جزئیات و اکسل (حساس مالی)
        ["Projects"] = new[] { "Create", "Read", "Update", "UpdateExitDate", "Delete", "Export", "ViewFactor" }, // ورود و خروج پروژه‌ها؛ UpdateExitDate برای ثبت مستقل تاریخ خروج
        ["ReportWorks"] = CrudActions,                         // گزارش‌های کار
        ["Karfarmas"] = CrudActions,                           // کارفرماها
        ["TypeFactors"] = CrudActions,                         // انواع فاکتور
        ["ProjectAttach"] = new[] { "Create", "Read", "Delete" }, // پیوست‌های پروژه
        ["ProjectCartable"] = new[] { "Read", "Manager", "Expert" }, // کارتابل پروژه — Read=مشاهده، Manager=تایید/رد مدیر، Expert=اتمام کارشناسی
        // ================== اتوماسیون اداری — نامه داخلی ==================
        // Create: ثبت و ارسال نامه (و مدیریت پیش‌نویس‌ها/گروه‌های گیرندگان)
        // Read: کارتابل، مشاهده نامه و پیوست‌ها | Erja: ارجاع نامه به دیگران
        // Delete: حذف نامه/گروه (مدیرانه)
        ["InnerLetters"] = new[] { "Create", "Read", "Erja", "Delete", "ViewAll" },
        // ================== فرم‌های متفرقه — صورتجلسه ==================
        // View: مشاهده ماژول (مبنای نمایش در منو) | Create: ثبت صورتجلسه
        // Update: ویرایش، بندها و تصمیمات | Sign: امضای الکترونیکی
        // ToInnerLetter / ToOutgoingLetter / SendEmail: تبدیل به نامه داخلی/صادره/ایمیل
        // (این سه مورد در کنترلر، مجوز خودِ آن ماژول‌ها را هم چک می‌کنند)
        // Print: قالب چاپی با سربرگ | Delete: حذف صورتجلسه
        ["MeetingMinutes"] = new[] { "View", "Create", "Update", "Delete", "Sign", "ToInnerLetter", "ToOutgoingLetter", "SendEmail", "Print" },
        // ================== آرشیو اسناد و مدارک ==================
        ["DocArchive"] = new[] { "Read", "Create", "Delete", "Manage", "Export" },
        // ================== هسته پرسنلی (کارگزینی) ==================
        // Read: مشاهده پرونده/چارت/قرارداد/احکام | Create: ثبت جدید | Update: ویرایش + اجرای حکم
        // Delete: حذف/غیرفعال‌سازی | Manage: داشبورد مدیریتی و گزارش‌ها
        ["HrCore"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Dashboard", "Contracts", "ContractTemplates", "Decrees", "Recruitment" /* دسترسی تفکیکی هر لینک */ },
        // حقوق و دستمزد: Read=مشاهده فیش و گزارش‌ها | Manage=تعریف آیتم، محاسبه، قفل دوره
        ["HrPay"] = new[] { "Read", "Manage" },
        // ================== منابع انسانی اصلی — مدیریت پایه سازمانی ==================
        // Read/Create/Update/Delete: دسترسی عمومی به کل ماژول (پیش‌فرض نقش‌های قدیمی)
        // Manage: داشبورد مدیریتی و گزارش‌ها
        // دسترسی‌های تفکیکی زیر در HrMainController واقعاً چک می‌شوند (نه فقط تزئینی) و
        // اجازه می‌دهند بدون مجوز عمومی، فقط بخش مشخصی مدیریت شود:
        //   Overview  → GET overview
        //   Company   → پروفایل شرکت + لوگو
        //   Org       → ساختار سازمانی (گره‌ها)
        //   Positions → پست‌های سازمانی
        //   Branches  → شعب و دفاتر
        //   Calendar  → زبان/تقویم + تعطیلات رسمی
        //   Rules     → قوانین پیش‌فرض مرخصی/تأخیر/اضافه‌کاری (عمداً از Update عمومی جداست
        //               چون این پارامترها محاسبات همه پرسنل را تحت‌تأثیر قرار می‌دهند)
        ["HrMain"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Overview", "Company", "Org", "Positions", "Branches", "Calendar", "Rules" },
        // ================== حضور و غیاب فروغ آریا (ماژول جدید و مستقل) ==================
        // Read: مشاهده شیفت/وضعیت روزانه/گزارش‌ها | Create: وب‌کلاک و ثبت تردد
        // Update: ویرایش + محاسبه مجدد + تأیید ماموریت/مرخصی | Delete: حذف | Manage: داشبورد مدیریتی
        ["FaAtt"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Clock", "Daily", "Shifts", "LeaveTypes", "Missions", "Approvals", "Leaves", "MyLeaves", "Balances", "Devices", "Reports" /* دسترسی تفکیکی هر لینک */ },
        // ================== حقوق و دستمزد فروغ آریا (ماژول جدید و مستقل) ==================
        // Read: مشاهده فیش خود و گزارش‌ها | Manage: تنظیمات، محاسبه، نهایی‌سازی و پرداخت
        ["FaPay"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Dashboard", "Runs", "My", "Adjustments", "Items", "Settings", "Reports" /* دسترسی تفکیکی هر لینک */ },
        // ================== آموزش و توسعه فروغ آریا (ماژول جدید و مستقل) ==================
        // Read: مشاهده دوره‌ها | Create: ثبت‌نام و ثبت نیاز | Manage: مدیریت کامل LMS
        ["FaLms"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Dashboard", "Courses", "Calendar", "Enrollments", "Needs", "Budgets", "Certificates", "My", "Reports" /* دسترسی تفکیکی هر لینک */ },
        // ================== ارتباطات داخلی فروغ آریا (ماژول جدید و مستقل) ==================
        // Read: مشاهده اطلاعیه‌ها و تیکت خود | Create: ثبت تیکت | Manage: مدیریت اطلاعیه‌ها و پاسخ تیکت‌ها
        ["FaCom"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Dashboard", "Announcements", "Tickets", "My" /* دسترسی تفکیکی هر لینک */ },
        // ================== صفحات شخصی هر کاربر ==================
        // View: نمایش آیتم در منوی «داشبوردها» و ورود به صفحهٔ کارتابل من / بایگانی شخصی
        ["MyCartable"] = new[] { "View" },
        ["MyArchive"] = new[] { "View" },
        // ================== داشبورد شخصی (طراحی‌شده توسط خود کاربر) ==================
        // View: دیدن «داشبورد من» و ویجت‌ها | Design: ساخت/ویرایش/حذف داشبورد و چیدمان ویجت‌ها
        // دادهٔ هر ویجت جداگانه با مجوز مشاهدهٔ ماژول خودش کنترل می‌شود.
        ["MyDashboards"] = new[] { "View", "Design" },
        // ================== گزارش‌ساز حرفه‌ای ==================
        // View: دیدن و اجرای گزارش‌های خود/اشتراکی | Design: ساخت و ویرایش گزارش
        // Share: اشتراک‌گذاری گزارش با کاربران و نقش‌ها
        // نکته: دسترسی به «دادهٔ» هر جدول جداگانه با RBAC همان ماژول کنترل می‌شود،
        // پس اشتراک‌گذاری هرگز مجوز داده را دور نمی‌زند.
        ["ReportStudio"] = new[] { "View", "Design", "Share" },
        // ================== پیام‌رسان سازمانی ==================
        // View: مشاهده گفتگوها و پیام‌ها | Send: ارسال پیام | Manage: مدیریت (حذف پیام/گروه)
        ["Chat"] = new[] { "View", "Send", "Manage" },
        // ================== اتوماسیون اداری — نامه صادره (فاز دوم + امضا + دبیرخانه) ==================
        // همان دسترسی‌های داخلی اما برای ماژول صادره + Sign (امضا کننده بودن)
        // Dabirkhane: دبیرخانه نامه صادره — ثبت شماره مقصد و روش ارسال نامه‌های امضا شده
        ["OutgoingLetters"] = new[] { "Create", "Read", "Erja", "Delete", "Sign", "Dabirkhane", "ViewAll" },
        // ================== اتوماسیون اداری — نامه وارده ==================
        // ViewAll: استثنای «دیدن نامه‌های دیگران» (پیش‌فرض هر کس فقط نامه‌های خودش را می‌بیند)
        ["IncomingLetters"] = new[] { "Create", "Read", "Erja", "Delete", "Dabirkhane", "ViewAll" },
        // ================== ایمیل سازمانی (پست الکترونیک) ==================
        // Read: صندوق ایمیل و حساب‌های من | Create: ارسال ایمیل و افزودن حساب
        // Update: ویرایش حساب/نشان/خوانده‌شده | Delete: حذف حساب/پیام
        ["Email"] = new[] { "Create", "Read", "Update", "Delete", "Personal" },
        // ================== اتوماسیون اداری — ساختار شماره نامه (تنظیمات مدیر سیستم) ==================
        // Read: مشاهده ساختار شماره سه نوع نامه (داخلی/صادره/وارده) در تنظیمات
        // Update: تغییر ترتیب/اجزای ساختار شماره اندیکاتور
        ["LetterStructures"] = new[] { "Read", "Update" },
        // ================== هوش مصنوعی فروغ آریا ==================
        // Use: گفتگو با دستیار + هوش نامه‌ها + منشی کارمندی | Manage: مدیریت (آینده)
        ["AiAssistant"] = new[] { "Use", "Manage" }
    };

    public static async Task SeedAsync(AppDbContext db)
    {
        var firstSeed = !await db.Roles.AnyAsync();

        if (firstSeed)
        {
            // Create Roles
            var adminRole = new Role { Name = "Admin", Description = "دسترسی کامل به تمام بخش‌ها", IsActive = true };
            var operatorRole = new Role { Name = "Operator", Description = "دسترسی به عملیات روزمره", IsActive = true };
            var accountantRole = new Role { Name = "Accountant", Description = "دسترسی به مالی و گزارشات", IsActive = true };

            await db.Roles.AddRangeAsync(adminRole, operatorRole, accountantRole);
            await db.SaveChangesAsync();
        }

        // ================== همگام‌سازی پرمیشن‌ها (حتی برای دیتابیس‌های موجود) ==================
        // هر پرمیشنی که در ModuleActions هست ولی در دیتابیس نیست، اضافه می‌شود
        var existing = await db.Permissions
            .Select(p => new { p.Module, p.Action })
            .ToListAsync();
        var existingSet = existing.Select(p => $"{p.Module}:{p.Action}").ToHashSet();

        var newPermissions = new List<Permission>();
        foreach (var (module, actions) in ModuleActions)
        {
            foreach (var action in actions)
            {
                if (!existingSet.Contains($"{module}:{action}"))
                {
                    newPermissions.Add(new Permission
                    {
                        Module = module,
                        Action = action,
                        Description = $"{action} {module}"
                    });
                }
            }
        }

        if (newPermissions.Count > 0)
        {
            await db.Permissions.AddRangeAsync(newPermissions);
            await db.SaveChangesAsync();

            // پرمیشن‌های جدید به‌صورت خودکار به نقش Admin داده می‌شوند
            var admin = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (admin != null)
            {
                foreach (var perm in newPermissions)
                    db.RolePermissions.Add(new RolePermission { RoleId = admin.Id, PermissionId = perm.Id });
                await db.SaveChangesAsync();
            }

            Console.WriteLine($"[RBAC] {newPermissions.Count} پرمیشن جدید اضافه شد (داشبوردها/گزارش‌ها/معرف).");
        }

        // ================== مهاجرت مودیان به نسخهٔ گواهی امضا (SDK 2.0.29) ==================
        var moadianMigrationMap = new Dictionary<string, string[]>
        {
            ["Configure"]      = new[] { "Certificate" },
            ["Company"]        = new[] { "Certificate" },
            ["FiscalYear"]     = new[] { "Certificate" },
            ["Connection"]     = new[] { "Certificate" },
            ["Goods"]          = new[] { "MasterData" },
            ["Units"]          = new[] { "MasterData" },
            ["Customers"]      = new[] { "MasterData" },
            ["Create"]         = new[] { "InvoiceCreate" },
            ["Update"]         = new[] { "InvoiceUpdate" },
            ["Delete"]         = new[] { "InvoiceDelete" },
            ["Send"]           = new[] { "InvoiceSend", "InvoiceInquiry", "FolderStatus", "PaymentRegister" },
            ["InvoicePreview"] = new[] { "View" },
            ["InvoiceStatus"]  = new[] { "InvoiceInquiry", "FolderStatus", "PaymentRegister" }
        };
        var oldMoadianPerms = await db.Permissions.Where(p => p.Module == "Moadian" && moadianMigrationMap.Keys.Contains(p.Action)).ToListAsync();
        if (oldMoadianPerms.Count > 0)
        {
            var allMoadianPerms = await db.Permissions.Where(p => p.Module == "Moadian").ToListAsync();
            var permByAction = allMoadianPerms.ToDictionary(p => p.Action, p => p);
            var rolePerms = await db.RolePermissions.Include(rp => rp.Permission).Where(rp => rp.Permission.Module == "Moadian").ToListAsync();
            var rolePermSet = rolePerms.Select(rp => (rp.RoleId, rp.Permission.Action)).ToHashSet();
            var toAdd = new List<RolePermission>();
            foreach (var rp in rolePerms)
            {
                if (!moadianMigrationMap.TryGetValue(rp.Permission.Action, out var newActions)) continue;
                foreach (var newAction in newActions)
                {
                    if (!permByAction.TryGetValue(newAction, out var newPerm)) continue;
                    if (rolePermSet.Contains((rp.RoleId, newAction))) continue;
                    toAdd.Add(new RolePermission { RoleId = rp.RoleId, PermissionId = newPerm.Id });
                    rolePermSet.Add((rp.RoleId, newAction));
                }
            }
            if (toAdd.Count > 0)
            {
                await db.RolePermissions.AddRangeAsync(toAdd);
                await db.SaveChangesAsync();
            }

            var obsoleteIds = oldMoadianPerms.Select(p => p.Id).ToHashSet();
            var obsoleteRolePerms = await db.RolePermissions.Where(rp => obsoleteIds.Contains(rp.PermissionId)).ToListAsync();
            if (obsoleteRolePerms.Count > 0) db.RolePermissions.RemoveRange(obsoleteRolePerms);
            db.Permissions.RemoveRange(oldMoadianPerms);
            await db.SaveChangesAsync();
            Console.WriteLine($"[RBAC] مهاجرت مودیان به SDK 2.0.29: {oldMoadianPerms.Count} پرمیشن منسوخ پاک‌سازی شد.");
        }

        // ================== مهاجرت «پروژه‌های کاری / نگار ذهنی» به مجوزهای بخشی ==================
        // مجوزهای بخشی تازه (Summary/Orders/MindFlow/Members/Activity و کل ماژول MindFlow)
        // «الزامی» هستند؛ اگر برای نقش‌های موجود اعطا نشوند، کاربرانی که امروز به این
        // بخش‌ها دسترسی دارند بعد از به‌روزرسانی دسترسی‌شان را از دست می‌دهند.
        // قاعدهٔ ارتقا: هر نقشی که «مشاهدهٔ پروژه‌های کاری» دارد، مجوزهای بخشی پروژه و
        // همهٔ مجوزهای نگار ذهنی را هم می‌گیرد؛ پس رفتار فعلی حفظ می‌شود و مدیر از این
        // پس می‌تواند تک‌تک بخش‌ها را بردارد.
        {
            var projViewPerm = await db.Permissions
                .Where(p => p.Module == "WorkProjects" && p.Action == "View")
                .Select(p => p.Id).FirstOrDefaultAsync();
            if (projViewPerm > 0)
            {
                var sectionActions = new[] { "Summary", "Orders", "MindFlow", "Members", "Activity" };
                var sectionPermIds = await db.Permissions
                    .Where(p => p.Module == "WorkProjects" && sectionActions.Contains(p.Action))
                    .Select(p => p.Id).ToListAsync();
                var mindFlowPermIds = await db.Permissions
                    .Where(p => p.Module == "MindFlow")
                    .Select(p => p.Id).ToListAsync();

                var rolesWithView = await db.RolePermissions
                    .Where(rp => rp.PermissionId == projViewPerm)
                    .Select(rp => rp.RoleId).Distinct().ToListAsync();

                var grantPermIds = sectionPermIds.Concat(mindFlowPermIds).Distinct().ToList();
                if (rolesWithView.Count > 0 && grantPermIds.Count > 0)
                {
                    // نام متغیر عمداً «existingGrants» است: در همین متد یک «existing» سطح‌متد
                    // برای همگام‌سازی مجوزها وجود دارد و اعلان دوباره در بلوک تودرتو
                    // خطای CS0136 می‌داد.
                    var existingGrants = await db.RolePermissions
                        .Where(rp => rolesWithView.Contains(rp.RoleId) && grantPermIds.Contains(rp.PermissionId))
                        .Select(rp => new { rp.RoleId, rp.PermissionId })
                        .ToListAsync();
                    var have = existingGrants.Select(x => (x.RoleId, x.PermissionId)).ToHashSet();

                    var toGrant = new List<RolePermission>();
                    foreach (var roleId in rolesWithView)
                        foreach (var permId in grantPermIds)
                            if (!have.Contains((roleId, permId)))
                                toGrant.Add(new RolePermission { RoleId = roleId, PermissionId = permId });

                    if (toGrant.Count > 0)
                    {
                        await db.RolePermissions.AddRangeAsync(toGrant);
                        await db.SaveChangesAsync();
                        Console.WriteLine($"[RBAC] {toGrant.Count} دسترسی بخشی «پروژه‌های کاری / نگار ذهنی» به {rolesWithView.Count} نقش اعطا شد (حفظ دسترسی فعلی).");
                    }
                }
            }
        }

        // ================== نقش «معرف» (Referrer) — برای کاربران لاگین معرف ==================
        var referrerRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Referrer");
        if (referrerRole == null)
        {
            referrerRole = new Role { Name = "Referrer", Description = "معرف — دسترسی به پنل شخصی (کیف پول، محصولات، کارت)", IsActive = true };
            db.Roles.Add(referrerRole);
            await db.SaveChangesAsync();

            // پیش‌فرض: همه‌ی بخش‌های پنل معرف
            var panelPerms = await db.Permissions.Where(p => p.Module == "ReferrerPanel").ToListAsync();
            foreach (var perm in panelPerms)
                db.RolePermissions.Add(new RolePermission { RoleId = referrerRole.Id, PermissionId = perm.Id });
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] نقش «Referrer» با دسترسی‌های پنل معرف ساخته شد.");
        }

        // ================== نقش‌های منابع انسانی ==================
        // نقش «مدیر منابع انسانی» — تایید/رد همه‌ی درخواست‌ها + گزارش ماهانه‌ی همه‌ی نیروها (پنل مدیریت)
        var hrManagerRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "HrManager");
        var hrManagerCreated = false;
        if (hrManagerRole == null)
        {
            hrManagerCreated = true;
            hrManagerRole = new Role { Name = "HrManager", Description = "مدیر منابع انسانی — تایید/رد درخواست‌های مرخصی و ماموریت همه‌ی نیروها + گزارش ماهانه", IsActive = true };
            db.Roles.Add(hrManagerRole);
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] نقش «HrManager» ساخته شد.");
        }
        var hrPerms = await db.Permissions
            .Where(p => hrManagerCreated && p.Module == "LeaveRequests" && (p.Action == "Request" || p.Action == "Approve" || p.Action == "Report"))
            .ToListAsync();
        var hrHas = await db.RolePermissions.Where(rp => rp.RoleId == hrManagerRole.Id)
            .Select(rp => rp.PermissionId).ToListAsync();
        foreach (var perm in hrPerms.Where(p => !hrHas.Contains(p.Id)))
            db.RolePermissions.Add(new RolePermission { RoleId = hrManagerRole.Id, PermissionId = perm.Id });
        await db.SaveChangesAsync();

        // نقش «کارمند» — فقط کارتابل شخصی (ثبت درخواست مرخصی/ماموریت؛ فقط اطلاعات خودش)
        var employeeRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Employee");
        if (employeeRole == null)
        {
            employeeRole = new Role { Name = "Employee", Description = "کارمند — کارتابل شخصی منابع انسانی (ثبت و پیگیری درخواست‌های خود)", IsActive = true };
            db.Roles.Add(employeeRole);
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] نقش «Employee» ساخته شد.");
        }
        var empHas = await db.RolePermissions.Where(rp => rp.RoleId == employeeRole.Id)
            .Select(rp => rp.PermissionId).ToListAsync();
        var empPerms = await db.Permissions
            .Where(p => p.Module == "LeaveRequests" && p.Action == "Request")
            .ToListAsync();
        foreach (var perm in empPerms.Where(p => !empHas.Contains(p.Id)))
            db.RolePermissions.Add(new RolePermission { RoleId = employeeRole.Id, PermissionId = perm.Id });
        await db.SaveChangesAsync();

        // ================== دسترسی‌های حضور و غیاب ==================
        // کارمند: ثبت ورود/خروج شخصی
        var selfCheckPerm = await db.Permissions.FirstOrDefaultAsync(p => p.Module == "Attendance" && p.Action == "SelfCheckin");
        if (selfCheckPerm != null)
        {
            var hasSelf = await db.RolePermissions.Where(rp => rp.PermissionId == selfCheckPerm.Id && rp.RoleId == employeeRole.Id).AnyAsync();
            if (!hasSelf) { db.RolePermissions.Add(new RolePermission { RoleId = employeeRole.Id, PermissionId = selfCheckPerm.Id }); await db.SaveChangesAsync(); }
        }
        // مدیر منابع انسانی: تمام دسترسی‌های حضور و غیاب
        var hrAttPerms = await db.Permissions.Where(p => p.Module == "Attendance").ToListAsync();
        var hrAttHas = await db.RolePermissions.Where(rp => rp.RoleId == hrManagerRole.Id).Select(rp => rp.PermissionId).ToListAsync();
        foreach (var perm in hrAttPerms.Where(p => !hrAttHas.Contains(p.Id)))
            db.RolePermissions.Add(new RolePermission { RoleId = hrManagerRole.Id, PermissionId = perm.Id });
        await db.SaveChangesAsync();

        // مدیر منابع انسانی: مدیریت پایه سازمانی (HrMain) + کارگزینی (HrCore) + حقوق (HrPay)
        var hrMainModules = new[] { "HrMain", "HrCore", "HrPay", "FaAtt", "FaPay", "FaLms", "FaCom" };
        var hrMainPerms = await db.Permissions.Where(p => hrManagerCreated && hrMainModules.Contains(p.Module)).ToListAsync();
        var hrMainHas = await db.RolePermissions.Where(rp => rp.RoleId == hrManagerRole.Id).Select(rp => rp.PermissionId).ToListAsync();
        foreach (var perm in hrMainPerms.Where(p => !hrMainHas.Contains(p.Id)))
            db.RolePermissions.Add(new RolePermission { RoleId = hrManagerRole.Id, PermissionId = perm.Id });
        await db.SaveChangesAsync();

        // دسترسی‌های منابع انسانی فقط از RolePermissions انتخاب‌شده توسط مدیر اعمال می‌شوند.
        // برای نقش‌های Operator/Accountant/Referrer هیچ دسترسی HR خودکار اضافه نمی‌شود.

        // پیام‌رسان، صفحه‌های شخصی و گزارش‌ساز نباید به‌صورت پنهان به همهٔ نقش‌ها
        // تزریق شوند. هر نقش باید آن‌ها را صریحاً از «نقش‌ها و دسترسی‌ها» دریافت کند.
        // این پاک‌سازی یک‌باره فقط نقش‌هایی را هدف می‌گیرد که واقعاً «فقط مودیان»
        // بوده‌اند و مجوزهای اضافه‌شان محصول seed قدیمی بوده است.
        await RemoveFormerImplicitPermissionsFromMoadianOnlyRolesAsync(db);

// ================== نقش‌های ماژول ویزیتوری ==================
        // SalesVisitor: ویزیتور میدانی — ثبت و ارسال سفارش، مشاهدهٔ سفارش‌های خودش
        var salesVisitorRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "SalesVisitor");
        if (salesVisitorRole == null)
        {
            salesVisitorRole = new Role
            {
                Name = "SalesVisitor",
                Description = "ویزیتور — ثبت سفارش میدانی، پیگیری سفارش‌های خودش و پورسانت (فقط دادهٔ خودش)",
                IsActive = true
            };
            db.Roles.Add(salesVisitorRole);
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] نقش «SalesVisitor» ساخته شد.");
        }
        var visitorIsNew = !await db.RolePermissions.AnyAsync(rp => rp.RoleId == salesVisitorRole.Id);
        {
            var wanted = new[] { "View", "Create", "Update", "Submit", "Cancel" };
            var perms = await db.Permissions
                .Where(p => p.Module == "SalesVisit" && wanted.Contains(p.Action))
                .ToListAsync();
            var has = await db.RolePermissions.Where(rp => rp.RoleId == salesVisitorRole.Id)
                .Select(rp => rp.PermissionId).ToListAsync();
            foreach (var perm in perms.Where(p => !has.Contains(p.Id)))
                db.RolePermissions.Add(new RolePermission { RoleId = salesVisitorRole.Id, PermissionId = perm.Id });
            await db.SaveChangesAsync();
        }
        {
            // ویزیتور «تعریف مشتری» می‌کند — فقط مشتری، هرگز تأمین‌کننده.
            // فقط یک‌بار (نقش تازه) یا اگر مدیر بعداً آن را برداشته باشد، دوباره اضافه نمی‌شود؛
            // پس این بلوک با یک رخداد در AuditLog علامت می‌خورد.
            const string flag = "VisitorCustomerAccessV1";
            var applied = await db.AuditLogs.AsNoTracking()
                .AnyAsync(l => l.Module == "RbacSeeder" && l.Action == flag);
            if (visitorIsNew || !applied)
            {
                var wanted = new[] { "Create", "Update" };
                var perms = await db.Permissions
                    .Where(p => p.Module == "Customers" && wanted.Contains(p.Action))
                    .ToListAsync();
                var has = await db.RolePermissions.Where(rp => rp.RoleId == salesVisitorRole.Id)
                    .Select(rp => rp.PermissionId).ToListAsync();
                foreach (var perm in perms.Where(p => !has.Contains(p.Id)))
                    db.RolePermissions.Add(new RolePermission { RoleId = salesVisitorRole.Id, PermissionId = perm.Id });
                await db.SaveChangesAsync();

                // نقش‌های ویزیتوری که قبلاً «Parties» کامل داشتند تا این نسخه، دسترسی مشتری/تأمین‌کننده‌شان
                // نگه داشته می‌شود (رفتار قبلی حفظ می‌شود) — تغییر مسئولیت مدیر نیست.
                var legacyParties = await db.RolePermissions
                    .Where(rp => rp.RoleId == salesVisitorRole.Id
                                 && db.Permissions.Any(p => p.Id == rp.PermissionId && p.Module == "Parties"))
                    .Select(rp => rp.PermissionId).ToListAsync();
                if (legacyParties.Count > 0)
                {
                    var split = await db.Permissions
                        .Where(p => p.Module == "Suppliers" || (p.Module == "Customers" && !wanted.Contains(p.Action)))
                        .ToListAsync();
                    var hasNow = await db.RolePermissions.Where(rp => rp.RoleId == salesVisitorRole.Id)
                        .Select(rp => rp.PermissionId).ToListAsync();
                    foreach (var perm in split.Where(p => !hasNow.Contains(p.Id)))
                        db.RolePermissions.Add(new RolePermission { RoleId = salesVisitorRole.Id, PermissionId = perm.Id });
                }

                db.AuditLogs.Add(new AuditLog
                {
                    Module = "RbacSeeder",
                    Action = flag,
                    Summary = "اعطای مجوز «مشتریان» (بدون تأمین‌کنندگان) به نقش SalesVisitor",
                    Username = "system"
                });
                await db.SaveChangesAsync();
            }
        }

        // SalesManager: مدیر فروش — تأیید/برگشت سفارش‌ها، تخصیص مشتری، سقف تخفیف و صف مالی
        var salesManagerRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "SalesManager");
        if (salesManagerRole == null)
        {
            salesManagerRole = new Role
            {
                Name = "SalesManager",
                Description = "مدیر فروش — تأیید سفارش ویزیتورها، تخصیص مشتری و سقف تخفیف، صف مالی (بدون قطعی‌سازی حسابداری)",
                IsActive = true
            };
            db.Roles.Add(salesManagerRole);
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] نقش «SalesManager» ساخته شد.");
        }
        {
            var wanted = new[] { "View", "Approve", "SetDiscountCap", "AssignCustomer", "FinanceView", "Export" };
            // (مجوزهای مشتری/تأمین‌کننده مدیر فروش پایین‌تر، در بلوک SalesManagerAccess، اضافه می‌شود)
            var perms = await db.Permissions
                .Where(p => p.Module == "SalesVisit" && wanted.Contains(p.Action))
                .ToListAsync();
            var has = await db.RolePermissions.Where(rp => rp.RoleId == salesManagerRole.Id)
                .Select(rp => rp.PermissionId).ToListAsync();
            foreach (var perm in perms.Where(p => !has.Contains(p.Id)))
                db.RolePermissions.Add(new RolePermission { RoleId = salesManagerRole.Id, PermissionId = perm.Id });
            await db.SaveChangesAsync();
        }

// SalesManager: مجوز «مشتریان» (دیدن/تخصیص همهٔ مشتریان) — بدون تأمین‌کنندگان
        {
            const string flag = "ManagerCustomerAccessV1";
            if (!await db.AuditLogs.AsNoTracking().AnyAsync(l => l.Module == "RbacSeeder" && l.Action == flag))
            {
                var perms = await db.Permissions
                    .Where(p => p.Module == "Customers" && (p.Action == "Read" || p.Action == "Update"))
                    .ToListAsync();
                var has = await db.RolePermissions.Where(rp => rp.RoleId == salesManagerRole.Id)
                    .Select(rp => rp.PermissionId).ToListAsync();
                foreach (var perm in perms.Where(p => !has.Contains(p.Id)))
                    db.RolePermissions.Add(new RolePermission { RoleId = salesManagerRole.Id, PermissionId = perm.Id });
                db.AuditLogs.Add(new AuditLog
                {
                    Module = "RbacSeeder",
                    Action = flag,
                    Summary = "اعطای مجوز «مشتریان» به نقش SalesManager",
                    Username = "system"
                });
                await db.SaveChangesAsync();
            }
        }

        // ================== نقش تیم نرم‌افزار (DevDeveloper) — میز کار توسعه ==================
        // View/Create/Update/Assign برای کار روزمره؛ Manage/Delete معمولاً فقط Admin
        var devDevRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "DevDeveloper");
        if (devDevRole == null)
        {
            devDevRole = new Role
            {
                Name = "DevDeveloper",
                Description = "تیم نرم‌افزار — میز کار توسعه (تسک، مشکل، تایمر، changelog)",
                IsActive = true
            };
            db.Roles.Add(devDevRole);
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] نقش «DevDeveloper» ساخته شد.");
        }
        {
            var wanted = new[] { "View", "Create", "Update", "Assign" };
            var devPerms = await db.Permissions
                .Where(p => p.Module == "DevTeam" && wanted.Contains(p.Action))
                .ToListAsync();
            var has = await db.RolePermissions.Where(rp => rp.RoleId == devDevRole.Id)
                .Select(rp => rp.PermissionId).ToListAsync();
            foreach (var perm in devPerms.Where(p => !has.Contains(p.Id)))
                db.RolePermissions.Add(new RolePermission { RoleId = devDevRole.Id, PermissionId = perm.Id });
            await db.SaveChangesAsync();
        }

        // اطمینان: Admin همهٔ پرمیشن‌های DevTeam را دارد (حتی اگر قبلاً seed شده)
        {
            var admin = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (admin != null)
            {
                var allDt = await db.Permissions.Where(p => p.Module == "DevTeam").ToListAsync();
                var has = await db.RolePermissions.Where(rp => rp.RoleId == admin.Id)
                    .Select(rp => rp.PermissionId).ToListAsync();
                var added = 0;
                foreach (var perm in allDt.Where(p => !has.Contains(p.Id)))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = admin.Id, PermissionId = perm.Id });
                    added++;
                }
                if (added > 0)
                {
                    await db.SaveChangesAsync();
                    Console.WriteLine($"[RBAC] {added} پرمیشن DevTeam به Admin اضافه شد.");
                }
            }
        }

        if (firstSeed)
        {
            var allPermissions = await db.Permissions.ToListAsync();
            var adminRole = await db.Roles.FirstAsync(r => r.Name == "Admin");
            var operatorRole = await db.Roles.FirstAsync(r => r.Name == "Operator");
            var accountantRole = await db.Roles.FirstAsync(r => r.Name == "Accountant");

            // Admin: همه‌ی پرمیشن‌ها (پرمیشن‌های جدید بالاتر داده شده — تکراری نشود)
            var adminHas = await db.RolePermissions.Where(rp => rp.RoleId == adminRole.Id)
                .Select(rp => rp.PermissionId).ToListAsync();
            foreach (var perm in allPermissions.Where(p => !adminHas.Contains(p.Id)))
                db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = perm.Id });

            // Operator: عملیات روزمره + داشبوردها و گزارش‌ها
            // جلوگیری از کلید تکراری RolePermission در صورت هم‌پوشانی مجوزهای نقش‌های پایه.
            var operatorHas = await db.RolePermissions.Where(rp => rp.RoleId == operatorRole.Id)
                .Select(rp => rp.PermissionId).ToListAsync();
            var operatorPerms = allPermissions
                .Where(p => !operatorHas.Contains(p.Id)
                            && ((p.Action is "Read" or "Create" or "Update" && p.Module is not "SystemUsers" and not "Settings")
                                || p.Module is "Dashboards" or "ReportPages"))
                .ToList();
            foreach (var perm in operatorPerms)
                db.RolePermissions.Add(new RolePermission { RoleId = operatorRole.Id, PermissionId = perm.Id });

            // Accountant: مالی و گزارشات + داشبورد مالی
            var accountantHas = await db.RolePermissions.Where(rp => rp.RoleId == accountantRole.Id)
                .Select(rp => rp.PermissionId).ToListAsync();
            var accountantPerms = allPermissions
                .Where(p => !accountantHas.Contains(p.Id)
                            && (p.Module is "Reports" or "Expenses" or "Parties" or "Orders" or "ReportPages"
                                || (p.Module == "Dashboards" && p.Action == "Financial")))
                .ToList();
            foreach (var perm in accountantPerms)
                db.RolePermissions.Add(new RolePermission { RoleId = accountantRole.Id, PermissionId = perm.Id });

            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] Roles and Permissions seeded successfully.");
        }
    }

    /// <summary>
    /// نسخه‌های قبل در هر راه‌اندازی، Chat.View/Send و چند صفحهٔ شخصی را به همهٔ
    /// نقش‌های فعال اضافه می‌کردند. چون آن داده‌ها منشأ جداگانه‌ای ندارند، فقط نقش
    /// «صرفاً مودیان» (نقشی که هیچ مجوزی جز مودیان و آن تزریق‌های قدیمی ندارد) با
    /// اطمینان قابل پاک‌سازی است. رخدادِ migration در لاگ ثبت می‌شود تا انتخاب‌های
    /// صریح بعدی مدیر دوباره حذف نشوند.
    /// </summary>
    private static async Task RemoveFormerImplicitPermissionsFromMoadianOnlyRolesAsync(AppDbContext db)
    {
        const string migrationAction = "MoadianImplicitAccessCleanupV1";
        if (await db.AuditLogs.AsNoTracking().AnyAsync(l => l.Module == "RbacSeeder" && l.Action == migrationAction))
            return;

        var assignments = await (
            from rp in db.RolePermissions
            join role in db.Roles on rp.RoleId equals role.Id
            join permission in db.Permissions on rp.PermissionId equals permission.Id
            where role.IsActive
            select new { Assignment = rp, role.Id, permission.Module, permission.Action }
        ).ToListAsync();

        var formerlyImplicit = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Chat.View",
            "Chat.Send",
            "MyCartable.View",
            "MyArchive.View",
            "MyDashboards.View",
            "MyDashboards.Design",
            "ReportStudio.View"
        };

        var toRemove = new List<RolePermission>();
        foreach (var roleAssignments in assignments.GroupBy(x => x.Id))
        {
            var permissions = roleAssignments.ToList();
            var hasMoadian = permissions.Any(p => p.Module.Equals("Moadian", StringComparison.OrdinalIgnoreCase));
            var isMoadianOnly = hasMoadian && permissions.All(p =>
                p.Module.Equals("Moadian", StringComparison.OrdinalIgnoreCase)
                || formerlyImplicit.Contains($"{p.Module}.{p.Action}"));
            if (!isMoadianOnly) continue;

            toRemove.AddRange(permissions
                .Where(p => formerlyImplicit.Contains($"{p.Module}.{p.Action}"))
                .Select(p => p.Assignment));
        }

        if (toRemove.Count > 0)
            db.RolePermissions.RemoveRange(toRemove);

        db.AuditLogs.Add(new AuditLog
        {
            At = DateTime.Now,
            Module = "RbacSeeder",
            Action = migrationAction,
            HttpMethod = "SYSTEM",
            Path = "startup/rbac",
            Summary = $"پاک‌سازی {toRemove.Count} مجوز تزریقی از نقش‌های صرفاً مودیان",
            StatusCode = 200
        });
        await db.SaveChangesAsync();

        Console.WriteLine($"[RBAC] پاک‌سازی یک‌بارهٔ {toRemove.Count} مجوز تزریقی از نقش‌های صرفاً مودیان انجام شد.");
    }
}
