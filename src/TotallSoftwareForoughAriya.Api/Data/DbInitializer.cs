using System.Globalization;
using Inventory.Api.Services;
using Inventory.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>ساخت خودکار دیتابیس و بارگذاری داده اولیه در زمان راه‌اندازی.</summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var provider = config["Database:Provider"] ?? "SqlServer";
        // داده نمونه (دمو) فقط وقتی ساخته می‌شود که صریحاً فعال شده باشد — پیش‌فرض: دیتابیس تمیز
        var seedDemo = string.Equals(config["Database:SeedDemoData"], "true", StringComparison.OrdinalIgnoreCase);

        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    // حالت توسعه/تست: بدون مایگریشن
                    db.Database.EnsureCreated();
                    // Required model columns must exist before any service queries organizations/users.
                    await OrganizationSchemaV1.EnsureAsync(db);
                    // ⚠️ باید پیش از هر اسکریپت/کوئری دیگری باشد: فیلتر خودکار «شرکت» در EF به ستون
                    // CompanyId ارجاع می‌دهد؛ اگر دیتابیس قدیمی این ستون را نداشته باشد، اولین کوئری
                    // (مثلاً در TransactionLineCostSchemaV1) با خطای «no such column: CompanyId» می‌شکند.
                    await SalesCompanySchemaV1.EnsureAsync(db);
                    // دیتابیس‌های قدیمی با EnsureCreated جدول چت دریافت نمی‌کنند.
                    await ChatSchemaV1.EnsureSqliteAsync(db);
                    await ChatAttachmentSchemaV1.EnsureSqliteAsync(db);
                    // ستون‌های تبدیل ویس به متن (رونویسی پیام صوتی)
                    await ChatTranscriptSchemaV1.EnsureSqliteAsync(db);
                    // EnsureCreated ستون‌های جدید را به دیتابیسِ موجود اضافه نمی‌کند؛ اینجا خودتعمیر می‌کنیم
                    EnsureSqliteWorkCalendarSchema(db);
                    EnsureSqliteUserSignatureColumn(db);
                    // امکانات امنیتی آرشیو: لاگ دانلود/مشاهده، تایید رمز برای دانلود، دسترسی گروهی
                    await DocArchiveSecuritySchemaV1.EnsureAsync(db);
                    // موج دوم: واترمارک پیش‌نمایش، درخواست دسترسی، شماره‌گذار خودکار کد مدرک
                    await DocArchiveSecuritySchemaV2.EnsureAsync(db);
                    // موج سوم: لاگ چاپ فایل‌های مدرک (چاپ‌کننده + تاریخ روی خروجی)
                    await DocPrintSchemaV1.EnsureAsync(db);
                    // ورود انبوه آرشیو: جدول نوبت‌ها و نتایج + ایندکس پیوست‌ها
                    await DocArchiveImportSchemaV1.EnsureAsync(db);
                    // PWA آفلاین + همکاری آرشیو: جدول ستاره/نظر/منشن/ردیابی نشتی + ستون‌های امضا
                    await DocArchivePwaSchemaV1.EnsureAsync(db);
                    // کارت‌های دستگاه تعمیرات + انتقال خودکار پذیرش‌های تک‌دستگاهی قدیمی
                    await RepairDeviceSchemaV1.EnsureAsync(db);
                    // ستون‌های تخفیف سطر فاکتور فروش/خرید (باید پیش از کوئری‌های TransactionLineCostSchemaV1 باشد)
                    await TransactionLineDiscountSchemaV1.EnsureAsync(db);
                    // ذخیرهٔ هزینهٔ قطعی روی سطر فاکتور و اصلاح فاکتورهای تعمیراتیِ قبلی
                    await TransactionLineCostSchemaV1.EnsureAsync(db);
                    // پشتیبانی از انتخاب بانک در اسناد خرید و فروش عملیات
                    await TransactionBankSchemaV1.EnsureAsync(db);
                    // وضعیت، تاریخ و علت برگشت چک‌های فروشگاهی
                    await ChequeStatusSchemaV1.EnsureAsync(db);
                    // ستون‌های نشان صادره + جداول ایمیل سازمانی (Oto_*)
                    await OfficeEmailSchemaV1.EnsureAsync(db);
                    // دستور کار: ستون اولویت + جدول یادآور مهلت
                    await WorkOrderSchemaV1.EnsureAsync(db);
                    // حواله تحویل کالا (بدون قیمت) — جدول‌های سند «فقط مقدار»
                    await GoodsIssueSchemaV1.EnsureAsync(db);
                    // مازادِ کارکرد: «در انتظار تصمیم مدیر» — ستون‌های تصمیم و پیش‌فرض رفتار
                    await AttendanceExtraWorkSchemaV1.EnsureAsync(db);
                    // کانال شرکت‌های راه‌دور (درخواست خدمات IT بین‌شرکتی)
                    await ItRemoteRequestSchemaV1.EnsureAsync(db);
                }
                else
                {
                    MigrateSqlServer(db);
                    // Also repairs old databases whose migration history was previously stamped/skipped.
                    await OrganizationSchemaV1.EnsureAsync(db);
                    // ⚠️ همان دلیل بالا: قبل از هر کوئری/اسکریپت دیگری ستون CompanyId باید وجود داشته باشد،
                    // وگرنه SQL Server خطای «Invalid column name 'CompanyId'» می‌دهد.
                    await SalesCompanySchemaV1.EnsureAsync(db);
                    EnsureSystemUserPhoneColumn(db);
                    EnsureUserSignatureColumn(db);
                    // ستون‌های تبدیل ویس به متن (رونویسی پیام صوتی) — مایگریشن squash شده این‌ها را نمی‌سازد
                    await ChatTranscriptSchemaV1.EnsureSqlServerAsync(db);
                    // جداول/ستون‌های تقویم کاری و حضور — دیتابیس‌های قدیمی SQL Server این‌ها را ندارند:
                    // مایگریشن squash شده روی دیتابیسِ موجود «stamp و رد» می‌شود و جدول‌های جدید هرگز ساخته
                    // نمی‌شوند؛ نتیجه: صفحهٔ تقویم کاری بعد از انتشار خالی می‌ماند (معادل SQLite این خودتعمیر موجود است).
                    EnsureSqlServerWorkCalendarSchema(db);
                    // امکانات امنیتی آرشیو: لاگ دانلود/مشاهده، تایید رمز برای دانلود، دسترسی گروهی
                    await DocArchiveSecuritySchemaV1.EnsureAsync(db);
                    // موج دوم: واترمارک پیش‌نمایش، درخواست دسترسی، شماره‌گذار خودکار کد مدرک
                    await DocArchiveSecuritySchemaV2.EnsureAsync(db);
                    // موج سوم: لاگ چاپ فایل‌های مدرک (چاپ‌کننده + تاریخ روی خروجی)
                    await DocPrintSchemaV1.EnsureAsync(db);
                    // ورود انبوه آرشیو: جدول نوبت‌ها و نتایج + ایندکس پیوست‌ها
                    await DocArchiveImportSchemaV1.EnsureAsync(db);
                    // PWA آفلاین + همکاری آرشیو: جدول ستاره/نظر/منشن/ردیابی نشتی + ستون‌های امضا
                    await DocArchivePwaSchemaV1.EnsureAsync(db);
                    // کارت‌های دستگاه تعمیرات + انتقال خودکار پذیرش‌های تک‌دستگاهی قدیمی
                    await RepairDeviceSchemaV1.EnsureAsync(db);
                    // ستون‌های تخفیف سطر فاکتور فروش/خرید (باید پیش از کوئری‌های TransactionLineCostSchemaV1 باشد)
                    await TransactionLineDiscountSchemaV1.EnsureAsync(db);
                    // ذخیرهٔ هزینهٔ قطعی روی سطر فاکتور و اصلاح فاکتورهای تعمیراتیِ قبلی
                    await TransactionLineCostSchemaV1.EnsureAsync(db);
                    // پشتیبانی از انتخاب بانک در اسناد خرید و فروش عملیات
                    await TransactionBankSchemaV1.EnsureAsync(db);
                    // وضعیت، تاریخ و علت برگشت چک‌های فروشگاهی
                    await ChequeStatusSchemaV1.EnsureAsync(db);
                    // ستون‌های نشان صادره + جداول ایمیل سازمانی (Oto_*)
                    await OfficeEmailSchemaV1.EnsureAsync(db);
                    // دستور کار: ستون اولویت + جدول یادآور مهلت
                    await WorkOrderSchemaV1.EnsureAsync(db);
                    // حواله تحویل کالا (بدون قیمت) — جدول‌های سند «فقط مقدار»
                    await GoodsIssueSchemaV1.EnsureAsync(db);
                    // مازادِ کارکرد: «در انتظار تصمیم مدیر» — ستون‌های تصمیم و پیش‌فرض رفتار
                    await AttendanceExtraWorkSchemaV1.EnsureAsync(db);
                    // کانال شرکت‌های راه‌دور (درخواست خدمات IT بین‌شرکتی)
                    await ItRemoteRequestSchemaV1.EnsureAsync(db);
                }

                // ماژول ویزیتوری: جدول‌های سفارش میدانی (VisOrders/…). برای هر دو موتور، idempotent.
                await SalesVisitSchemaV1.EnsureAsync(db);

                // طرف حساب: ستون‌های «ثبت‌کننده» (چه ویزیتوری این مشتری را ثبت کرد)
                await PartyRegistrationSchemaV1.EnsureAsync(db);

                // پروفایل کاربر، وضعیت تأیید موبایل/ایمیل و جدول امن درخواست‌های OTP.
                await UserContactSchemaV1.EnsureAsync(db);

                // سامانه مودیان مبتنی بر گواهی امضا (SDK 2.0.29): پاک‌سازی جداول منسوخ قبلی، ساخت جداول Mdn* و بذرکاری واحدهای سنجش رسمی
                await MoadianCertSchemaV2.EnsureAsync(db);

                await WorkOrderSchemaV2.EnsureAsync(db);
                await DocEvolutionSchemaV1.EnsureAsync(db);
                await PushDeliverySchema.EnsureAsync(db);
                // مدت نمایش اعلان‌ها/پیام‌ها (سراسری، مدیر سامانه) — ستون‌های AppSettings
                await AppSettingsSchemaV1.EnsureAsync(db);
                await MeetingMinutesSchemaV1.EnsureAsync(db);
                await CompanyAccessSchemaV1.EnsureAsync(db);
                await AiSchemaV1.EnsureAsync(db);
                // گاوصندوق رمزهای تجهیزات (سرور، سوییچ، میکروتیک، فایروال، سامانه‌ها) — به تفکیک شرکت
                await ItVaultSchemaV1.EnsureAsync(db);
                // شناسنامه و پایش سرورهای HPE/iLO (رمز اتصال همچنان فقط در گاوصندوق است)
                await ServerManagementSchemaV1.EnsureAsync(db);
                await ServerMonitoringSchemaV2.EnsureAsync(db);
                await ServerAlertWorkflowSchemaV3.EnsureAsync(db);
                await ServerHardwareDetailsSchemaV4.EnsureAsync(db);
                await ServerIloCertSchemaV5.EnsureAsync(db);
                await IpamSchemaV1.EnsureAsync(db);
                // ماشین‌های اداری: شناسنامهٔ کامل، پایش SNMP، مواد مصرفی، سینی‌ها، شمارنده و رویدادها
                await OfficeMachineSchemaV2.EnsureAsync(db);
                await OfficeMachineSchemaV3.EnsureAsync(db);

                // دبیرخانه نامه صادره — ستون‌های CreatorUserId/CreatorId و ArchiveAt
                // باید برای دیتابیس‌های قدیمی نیز خودکار ایجاد شوند.
                await DabirkhaneSchemaV1.EnsureAsync(db);

                // ایندکس (Module, RefId) روی AppAttachments — باز کردن نامه و
                // «دارای پیوست» در فهرست کارتابل‌ها بدون این ایندکس کل جدولِ
                // پیوست‌ها (با ستون فایل) را اسکن می‌کرد و کند بود.
                await AttachmentIndexSchemaV1.EnsureAsync(db);

                // شرحِ بلند دستور کار (تصاویر inline نامه) — جدول جانبی
                await WorkOrderDescriptionSchemaV1.EnsureAsync(db);

                // رونوشت‌گیرندگان نامه صادره — این Schema قبلاً تعریف شده بود اما در
                // راه‌اندازی فراخوانی نمی‌شد و ثبت نامه دارای رونوشت با 400 شکست می‌خورد.
                await OutgoingCopyToSchemaV1.EnsureAsync(db);
                await OutgoingSignerReadSchemaV1.EnsureAsync(db);

                // فرم‌های متفرقه — صورتجلسه (جدول‌های MeetingMinutes*)
                await MeetingMinutesSchemaV1.EnsureAsync(db);

                // پروژه کاری سبک داخل دستور کار (جدول‌های WorkProject* + ستون‌های WorkOrders)
                await WorkProjectSchemaV1.EnsureAsync(db);
                await WorkProjectMindFlowSchemaV1.EnsureAsync(db);
                await WorkProjectMindFlowSchemaV2.EnsureAsync(db);
                await StandaloneMindFlowSchemaV1.EnsureAsync(db);

                // میز کار توسعه — مدیریت تیم نرم‌افزار (تسک/مشکل/changelog/گیت)
                await DevTeamSchemaV1.EnsureAsync(db);
                await DevTeamSchemaV2.EnsureAsync(db);
                await DevTeamSchemaV3.EnsureAsync(db);
                await DevTeamSchemaV4.EnsureAsync(db);
                await DevTeamSchemaV5.EnsureAsync(db);

                // نامه وارده — جدول IncomingLetters در مایگریشن SquashedInitial نیست؛
                // بدون این، SQL Server خطای «Invalid object name 'IncomingLetters'» می‌دهد.
                await IncomingLetterSchemaV1.EnsureAsync(db);
                if (!await IncomingLetterSchemaV1.TableExistsAsync(db))
                    Console.WriteLine("[DB] ⚠ جدول IncomingLetters در دیتابیس وجود ندارد! " +
                        "کارتابل نامه وارده و چارت‌های اتوماسیون اداری کار نخواهند کرد.");
                else
                    Console.WriteLine("[DB] ✔ جدول نامه وارده (IncomingLetters) آماده است.");

                // داشبورد شخصی کاربر — جدول داشبوردها و ویجت‌های چیده‌شده
                await DashboardSchemaV1.EnsureAsync(db);

                // گزارش‌ساز قدیمی حذف شده و از نو طراحی می‌شود — جدول‌های باقی‌مانده پاک می‌شوند
                await ReportBuilderCleanupV1.RunAsync(db);

                // گزارش‌ساز حرفه‌ای — گزارش‌ها و اشتراک با کاربران/نقش‌ها
                await ReportStudioSchemaV1.EnsureAsync(db);
                if (!await ReportStudioSchemaV1.TablesExistAsync(db))
                    Console.WriteLine("[DB] ⚠ جدول‌های گزارش‌ساز (RsReports/...) ساخته نشدند! " +
                                      (ReportStudioSchemaV1.LastError ?? ""));
                else
                    Console.WriteLine("[DB] ✔ جدول‌های گزارش‌ساز حرفه‌ای آماده‌اند.");
                await new Inventory.Api.Services.ItAssets.WorkOrderSchedulingService(db).UpgradeLegacyAsync(DateTime.Now);

                // ماژول انبارداری: ستون‌های جدید قیمت‌گذاری انبار، ارزش افزوده گروه و خطوط سند
                await WarehousingSchemaV1.EnsureAsync(db);

                // منابع انسانی اصلی — مدیریت پایه سازمانی (HrMain) + پیوند پرسنل به ساختار جدید
                await HrMainSchemaV1.EnsureAsync(db);

                // پرونده کارمندان — تحت‌تکفل، دوره‌ها، مهارت‌ها، زبان‌ها، اسناد + ستون‌های جدید پرسنل
                await HrEmployeeDossierSchemaV1.EnsureAsync(db);

                // حضور و غیاب فروغ آریا (ماژول جدید و مستقل) + داده پایه (انواع مرخصی/شیفت‌ها)
                await FaAttSchemaV1.EnsureAsync(db);
                await FaAttSchemaV2.EnsureAsync(db);
                await FaAttSchemaV3.EnsureAsync(db);
                FaAttMasterSeeder.Seed(db);
                await FaPaySchemaV1.EnsureAsync(db);
                await FaPaySchemaV2.EnsureAsync(db);
                FaPayMasterSeeder.Seed(db);
                await HrContractSchemaV2.EnsureAsync(db);
                await HrEmployeeSchemaV2.EnsureAsync(db);
                await HrRecruitmentSchemaV1.EnsureAsync(db);
                HrContractMasterSeeder.Seed(db);
                await FaLmsSchemaV1.EnsureAsync(db);
                await FaLmsSchemaV2.EnsureAsync(db);
                await FaLmsSchemaV3.EnsureAsync(db);
                await FaComSchemaV1.EnsureAsync(db);
                await FaComSchemaV2.EnsureAsync(db);
                await FaComSchemaV3.EnsureAsync(db);
                await HrTalentSchemaV1.EnsureAsync(db);
                await HrReportSchemaV1.EnsureAsync(db);
                // تفکیک کامل «منابع انسانی فروغ آریا» از «منابع انسانی اصلی» — جدول‌های اختصاصی
                // FaHrPersonnel / HrMainHolidays / FaPayMinWages + کپی یک‌بارهٔ داده‌های قبلی
                await ForoughAryaHrSeparationSchemaV1.EnsureAsync(db);

                // ---------- ماژول فروشگاهی چندشرکتی (اجرای دوم) ----------
                // اجرای اول بلافاصله بعد از ساخت/مهاجرت دیتابیس انجام شد. این اجرای دوم
                // جدول‌هایی را پوشش می‌دهد که در همین راه‌اندازی تازه ساخته می‌شوند
                // (GoodsIssues/GoodsIssueLines و RepairDevices) و در اجرای اول وجود نداشتند.
                await SalesCompanySchemaV1.EnsureAsync(db);

                if (seedDemo && !db.Products.Any())
                {
                    Console.WriteLine("[DB] حالت دمو فعال است؛ در حال بارگذاری داده نمونه...");
                    Seeder.Seed(db);
                    Console.WriteLine("[DB] داده نمونه با موفقیت بارگذاری شد.");
                }

                if (seedDemo && !db.HrEmployees.Any())
                {
                    Console.WriteLine("[DB] در حال بارگذاری داده نمونه منابع انسانی...");
                    HrDemoSeeder.Seed(db);
                    Console.WriteLine("[DB] داده نمونه منابع انسانی بارگذاری شد.");
                }

                if (seedDemo && db.HrEmployees.Any() && !db.FaAttLeaveBalances.Any())
                {
                    Console.WriteLine("[DB] در حال بارگذاری داده نمونه حضور و مرخصی...");
                    FaAttDemoSeeder.Seed(db);
                    Console.WriteLine("[DB] داده نمونه حضور و مرخصی بارگذاری شد.");
                }

                if (seedDemo && db.HrEmployees.Any() && !db.FaPayAdjustments.Any())
                {
                    Console.WriteLine("[DB] در حال بارگذاری داده نمونه حقوق و دستمزد...");
                    FaPayDemoSeeder.Seed(db);
                    Console.WriteLine("[DB] داده نمونه حقوق و دستمزد بارگذاری شد.");
                }

                if (seedDemo && db.HrEmployees.Any() && !db.FaComPolls.Any())
                {
                    Console.WriteLine("[DB] در حال بارگذاری داده نمونه ارتباطات...");
                    FaComDemoSeeder.Seed(db);
                    Console.WriteLine("[DB] داده نمونه ارتباطات بارگذاری شد.");
                }

                // ==================== RBAC Seed ====================
                await RbacSeeder.SeedAsync(db);

                // ==================== هوش مصنوعی فروغ آریا ====================
                await AiSeeder.EnsureAiUserAsync(db);
                await AiSeeder.EnsureKnowledgeAsync(db);

                // انبار پیش‌فرض (در اولین اجرا — قابل ویرایش/تغییر نام از بخش انبارها)
                if (!db.Warehouses.Any())
                {
                    db.Warehouses.Add(new Warehouse { Name = "انبار مرکزی" });
                    db.SaveChanges();
                    Console.WriteLine("[DB] انبار پیش‌فرض «انبار مرکزی» ساخته شد.");
                }

                // ابعاد تحلیلی پیش‌فرض (حسابداری تحلیلی): مرکز هزینه و شعبه
                if (!db.AccDimensions.Any())
                {
                    db.AccDimensions.AddRange(
                        new AccDimension { Code = "CC", Name = "مرکز هزینه", IsSystem = true, SortOrder = 1 },
                        new AccDimension { Code = "BR", Name = "شعبه", IsSystem = true, SortOrder = 2 });
                    db.SaveChanges();
                    Console.WriteLine("[DB] ابعاد تحلیلی پیش‌فرض (مرکز هزینه/شعبه) ساخته شد.");
                }


                // ==================== تگ‌های پیش‌فرض آرشیو اسناد ====================
                if (!db.DocTags.Any())
                {
                    db.DocTags.AddRange(
                        new DocTag { Name = "قرارداد", Color = "#4f46e5", Description = "قراردادها و پیمان‌ها" },
                        new DocTag { Name = "محرمانه", Color = "#dc2626", Description = "اسناد محرمانه و دارای طبقه‌بندی" },
                        new DocTag { Name = "مالی", Color = "#059669", Description = "اسناد مالی، فاکتورها و ضمانت‌نامه‌ها" },
                        new DocTag { Name = "فنی", Color = "#0284c7", Description = "نقشه‌ها، مشخصات و مدارک فنی" },
                        new DocTag { Name = "فوری", Color = "#d97706", Description = "مدارک نیازمند اقدام و بررسی فوری" },
                        new DocTag { Name = "ایزو", Color = "#7c3aed", Description = "دستورالعمل‌ها و فرم‌های استاندارد ISO" });
                    db.SaveChanges();
                    Console.WriteLine("[DB] تگ‌های پیش‌فرض آرشیو اسناد بارگذاری شد.");
                }

                // بازسازی گروه‌های کالا برای دیتابیس‌های قدیمی:
                // هر گروهی که روی کالاها ثبت شده ولی در جدول گروه‌ها نیست، اضافه می‌شود.
                var existing = db.ProductCategories.Select(c => c.Name).ToHashSet();
                var missing = db.Products.Where(p => p.Category != null)
                    .Select(p => p.Category!).Distinct().ToList()
                    .Where(n => !existing.Contains(n)).ToList();
                if (missing.Count > 0)
                {
                    foreach (var name in missing)
                        db.ProductCategories.Add(new ProductCategory { Name = name, CreatedAt = DateTime.Now });
                    db.SaveChanges();
                    Console.WriteLine($"[DB] {missing.Count} گروه کالا از روی کالاهای موجود ساخته شد.");
                }

                // بازسازی واحدهای شمارش برای دیتابیس‌های قدیمی:
                // واحدهای پیش‌فرض + واحدهای استفاده‌شده روی کالاها که در جدول نیستند اضافه می‌شوند.
                var knownUnits = db.MeasureUnits.Select(u => u.Name).ToHashSet();
                var wanted = Seeder.DefaultUnits
                    .Concat(db.Products.Select(p => p.Unit).Distinct().ToList())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .Where(n => !knownUnits.Contains(n))
                    .ToList();
                if (wanted.Count > 0)
                {
                    foreach (var name in wanted)
                        db.MeasureUnits.Add(new MeasureUnit { Name = name, CreatedAt = DateTime.Now });
                    db.SaveChanges();
                    Console.WriteLine($"[DB] {wanted.Count} واحد شمارش اضافه شد.");
                }

                // کاربر پیش‌فرض مدیر: admin / admin (در اولین اجرا)
                // ⚠️ رفع اشکال «قفل‌شدن نصب تازه»: قبلاً شرط فقط «جدول کاربران خالی باشد» بود،
                // ولی سیدر هوش مصنوعی کاربر «aria.ai» را پیش از این می‌سازد؛ پس در دیتابیس تازه
                // شرط هرگز برقرار نمی‌شد و هیچ کاربری برای ورود ساخته نمی‌شد (صفحهٔ ورود: رمز اشتباه/دسترسی‌ندارد).
                // الان اگر کاربری با نام admin یا نقشی با مقدار Admin وجود نداشته باشد، مدیر پیش‌فرض ساخته می‌شود.
                if (!db.Users.Any(u => u.Username == "admin") && !db.Users.Any(u => u.Role == "Admin"))
                {
                    db.Users.Add(new User
                    {
                        Username = "admin",
                        PasswordHash = Services.AuthService.HashPassword("admin"),
                        Role = "Admin",
                        CreatedAt = DateTime.Now
                    });
                    db.SaveChanges();
                    Console.WriteLine("[DB] کاربر پیش‌فرض ساخته شد: admin / admin — حتماً رمز را تغییر دهید.");
                }

                // ============ انبارداری: انواع پیش‌فرض رسید و حواله ============
                // ماهیت هر نوع، اثر سند روی موجودی را مشخص می‌کند:
                // افزایشی = رسید | کاهشی = حواله | خنثی = انتقال بین انبار یا سند یادداشتی
                if (!db.InvDocTypes.Any())
                {
                    db.InvDocTypes.AddRange(
                        // ---------- رسیدها (افزایشی) ----------
                        new InvDocType { Code = "RC-OPEN", Name = "رسید موجودی اول دوره", Nature = StockNature.Increase, RequiresPrice = true, NumberPrefix = "RCO", Color = "success", Icon = "bi-box-arrow-in-down", IsSystem = true, SortOrder = 1 },
                        new InvDocType { Code = "RC-BUY", Name = "رسید خرید", Nature = StockNature.Increase, RequiresParty = true, RequiresPrice = true, NumberPrefix = "RCB", Color = "success", Icon = "bi-bag-plus", IsSystem = true, SortOrder = 2 },
                        new InvDocType { Code = "RC-RET", Name = "رسید برگشت از فروش", Nature = StockNature.Increase, RequiresParty = true, RequiresPrice = true, NumberPrefix = "RCR", Color = "success", Icon = "bi-arrow-return-left", SortOrder = 3 },
                        new InvDocType { Code = "RC-PRD", Name = "رسید تولید", Nature = StockNature.Increase, NumberPrefix = "RCP", Color = "success", Icon = "bi-gear-wide-connected", SortOrder = 4 },
                        new InvDocType { Code = "RC-ADJ", Name = "رسید اضافی انبارگردانی", Nature = StockNature.Increase, NumberPrefix = "RCA", Color = "success", Icon = "bi-clipboard-plus", SortOrder = 5 },

                        // ---------- حواله‌ها (کاهشی) ----------
                        new InvDocType { Code = "IS-SELL", Name = "حواله فروش", Nature = StockNature.Decrease, RequiresParty = true, RequiresPrice = true, NumberPrefix = "ISS", Color = "danger", Icon = "bi-cart-dash", IsSystem = true, SortOrder = 6 },
                        new InvDocType { Code = "IS-USE", Name = "حواله مصرف", Nature = StockNature.Decrease, NumberPrefix = "ISU", Color = "danger", Icon = "bi-box-arrow-up", SortOrder = 7 },
                        new InvDocType { Code = "IS-RET", Name = "حواله برگشت از خرید", Nature = StockNature.Decrease, RequiresParty = true, RequiresPrice = true, NumberPrefix = "ISR", Color = "danger", Icon = "bi-arrow-return-right", SortOrder = 8 },
                        new InvDocType { Code = "IS-SCRP", Name = "حواله ضایعات", Nature = StockNature.Decrease, NumberPrefix = "ISC", Color = "danger", Icon = "bi-trash3", SortOrder = 9 },
                        new InvDocType { Code = "IS-ADJ", Name = "حواله کسری انبارگردانی", Nature = StockNature.Decrease, NumberPrefix = "ISA", Color = "danger", Icon = "bi-clipboard-minus", SortOrder = 10 },

                        // ---------- خنثی ----------
                        new InvDocType { Code = "TRN", Name = "انتقال بین انبار", Nature = StockNature.Neutral, IsTransfer = true, NumberPrefix = "TRN", Color = "primary", Icon = "bi-arrow-left-right", IsSystem = true, SortOrder = 11 },
                        new InvDocType { Code = "NOTE", Name = "سند یادداشتی (بدون اثر بر موجودی)", Nature = StockNature.Neutral, NumberPrefix = "NOT", Color = "muted", Icon = "bi-sticky", SortOrder = 12 });

                    db.SaveChanges();
                    Console.WriteLine("[DB] انواع پیش‌فرض رسید و حواله انبار ساخته شدند.");
                }

                // ============ حسابداری: سال مالی و کدینگ پیش‌فرض حساب‌ها ============
                SeedAccounting(db);

                // ============ فاکتور: پیکربندی پیش‌فرض انواع فاکتور ============
                SeedInvoicing(db);

                // ============ خزانه‌داری: صندوق/بانک و قواعد سند خودکار ============
                SeedTreasury(db);

                // انبار پیش‌فرض ماژول انبارداری
                var defaultWh = db.Warehouses.FirstOrDefault(w => w.IsDefault);
                if (defaultWh is null)
                {
                    var first = db.Warehouses.OrderBy(w => w.Id).FirstOrDefault();
                    if (first is not null) { first.IsDefault = true; db.SaveChanges(); }
                }

                // ============ اتوماسیون اداری: عملگرهای پیش‌فرض ارجاع ============
                // عملگر (Amalgar) تعیین می‌کند ارجاع «جهت اطلاع» است یا «جهت اقدام/تایید و امضا».
                // نکته: به‌جای «فقط وقتی هیچ عملگری نیست»، تک‌تک عملگرهای پیش‌فرض چک می‌شوند تا
                // عملگرهای تازه‌ای که بعداً به این فهرست اضافه می‌شوند (مثل «جهت استحضار») در
                // پایگاه‌داده‌های موجود هم ساخته شوند. عملگرهای ساخته‌شده توسط کاربر دست‌نخورده می‌مانند.
                var defaultAmalgars = new (string Title, string Taeed)[]
                {
                    ("جهت اطلاع", ""),
                    ("جهت استحضار", ""),
                    ("جهت اقدام", ""),
                    ("جهت بررسی و اعلام نظر", ""),
                    ("جهت تایید و امضا", "تایید"),
                    ("جهت پاسخگویی", ""),
                    ("جهت بایگانی", ""),
                };
                var addedAmalgars = 0;
                foreach (var (title, taeed) in defaultAmalgars)
                {
                    var existingAmalgar = db.Amalgars.FirstOrDefault(a => a.Title == title);
                    if (existingAmalgar is null)
                    {
                        db.Amalgars.Add(new Amalgar { Title = title, TaeedEmza = taeed });
                        addedAmalgars++;
                    }
                    else if (existingAmalgar.IsDelete)
                    {
                        // عملگر پیش‌فرضی که قبلاً حذف نرم شده بود، برگردانده می‌شود
                        existingAmalgar.IsDelete = false;
                        addedAmalgars++;
                    }
                }
                if (addedAmalgars > 0)
                {
                    db.SaveChanges();
                    Console.WriteLine($"[DB] {addedAmalgars} عملگر پیش‌فرض ارجاع نامه ساخته/بازگردانده شد.");
                }

                // ============ اتوماسیون اداری: سازمان‌ها — مبنای جزء «واحد» در شماره نامه ============
                // در اولین اجرا یک سازمان پیش‌فرض از تنظیمات ساخته می‌شود (کد قدیمی Letters:UnitCode
                // به‌عنوان NameUniq نگه داشته می‌شود تا شماره‌های قبلی مثل MQ/12/1404 تغییر نکنند).
                if (!db.Organizations.Any(o => !o.IsDelete))
                {
                    var unitCode = config?["Letters:UnitCode"] ?? "MQ";
                    var unitName = config?["Letters:UnitName"] ?? "سازمان پیش‌فرض";
                    db.Organizations.Add(new Organization
                    {
                        NameUnit = unitName,
                        NameUniq = unitCode,
                        IsDefault = true,
                        IsActive = true
                    });
                    db.SaveChanges();
                    Console.WriteLine($"[DB] سازمان پیش‌فرض «{unitName}» ({unitCode}) ساخته شد — مبنای جزء «واحد» شماره نامه.");
                }

                // ============ اتوماسیون اداری: ساختار پیش‌فرض شماره اندیکاتور ============
                // هر سه نوع نامه: 1=داخلی، 2=صادره، 3=وارده
                // ترتیب پیش‌فرض داخلی: واحد/شماره/سال → مثل MQ/1/1405 (ساختار مرجع کارفرما)
                // صادره و وارده: سال/شماره → مثل 1404/6 (جایگزین «سال/ص-شماره» هاردکد می‌شود)
                var defaultStructures = new Dictionary<int, string[]>
                {
                    [1] = new[] { "واحد", "شماره", "سال" },
                    [2] = new[] { "سال", "شماره" },
                    [3] = new[] { "سال", "شماره" }
                };
                foreach (var (typeForm, parts) in defaultStructures)
                {
                    if (db.LetterStratures.Any(s => s.TypeForm == typeForm)) continue;
                    foreach (var p in parts)
                        db.LetterStratures.Add(new LetterStrature { TypeForm = typeForm, TypeStrature = p });
                    db.SaveChanges();
                    Console.WriteLine($"[DB] ساختار پیش‌فرض شماره اندیکاتور TypeForm={typeForm} ({string.Join("/", parts)}) ساخته شد.");
                }

                // بازسازی شماره اندیکاتور نامه‌های قدیمی (داخلی) با ساختار جدید
                // (شماره ترتیبی Number ثابت می‌ماند؛ فقط رشته نمایشی بازتولید می‌شود)
                {
                    var structure = db.LetterStratures.Where(s => s.TypeForm == 1)
                        .OrderBy(s => s.StratureId).Select(s => s.TypeStrature).ToList();
                    if (structure.Count > 0)
                    {
                        var defaultOrg = db.Organizations.FirstOrDefault(o => !o.IsDelete && o.IsActive && o.IsDefault)
                                         ?? db.Organizations.FirstOrDefault(o => !o.IsDelete && o.IsActive);
                        var unit = defaultOrg?.NameUniq ?? config?["Letters:UnitCode"] ?? "MQ";
                        var pc = new PersianCalendar();
                        var toFix = db.InnerLetters.Where(l => !l.IsDelete).ToList();
                        int fixedCount = 0;
                        foreach (var l in toFix)
                        {
                            var parts = new List<string>();
                            foreach (var p in structure)
                                switch (p)
                                {
                                    case "سال": parts.Add(pc.GetYear(l.DateSabt).ToString()); break;
                                    case "واحد": if (!string.IsNullOrEmpty(unit)) parts.Add(unit); break;
                                    case "شماره": parts.Add(l.Number.ToString()); break;
                                }
                            var newNum = string.Join("/", parts);
                            if (l.LetterNumber != newNum) { l.LetterNumber = newNum; fixedCount++; }
                        }
                        if (fixedCount > 0)
                        {
                            db.SaveChanges();
                            Console.WriteLine($"[DB] شماره اندیکاتور {fixedCount} نامه قدیمی با ساختار جدید بازسازی شد.");
                        }
                    }
                }

                // کاربران نمونه برای دموی کارتابل نامه (فقط در حالت دمو)
                if (seedDemo && db.Users.Count() <= 1)
                {
                    db.Users.AddRange(
                        new User { Username = "ali", PasswordHash = Services.AuthService.HashPassword("ali123"), Role = "Operator", FirstName = "علی", LastName = "رضایی", CreatedAt = DateTime.Now },
                        new User { Username = "sahar", PasswordHash = Services.AuthService.HashPassword("sahar123"), Role = "Operator", FirstName = "سحر", LastName = "محمدی", CreatedAt = DateTime.Now },
                        new User { Username = "fatemeh", PasswordHash = Services.AuthService.HashPassword("fatemeh123"), Role = "Operator", FirstName = "فاطمه", LastName = "کریمی", CreatedAt = DateTime.Now });
                    db.SaveChanges();
                    Console.WriteLine("[DB] کاربران دمو ساخته شدند: ali/ali123 — sahar/sahar123 — fatemeh/fatemeh123");
                }

                // گروه‌های نمونه گیرندگان نامه (فقط در حالت دمو)
                if (seedDemo && !db.LetterGroups.Any())
                {
                    var demoAdmin = db.Users.FirstOrDefault(u => u.Username == "admin");
                    var demoUsers = db.Users.Where(u => u.Username != "admin").Take(3).ToList();
                    if (demoAdmin != null && demoUsers.Count > 0)
                    {
                        var g1 = new LetterGroup { NameGroup = "کارشناسان اداری", CreatorUserId = demoAdmin.Id };
                        foreach (var u in demoUsers) g1.Members.Add(new LetterGroupMember { UserId = u.Id });
                        var g2 = new LetterGroup { NameGroup = "مدیران", CreatorUserId = demoAdmin.Id };
                        g2.Members.Add(new LetterGroupMember { UserId = demoAdmin.Id });
                        g2.Members.Add(new LetterGroupMember { UserId = demoUsers[0].Id });
                        db.LetterGroups.AddRange(g1, g2);
                        db.SaveChanges();
                        Console.WriteLine("[DB] گروه‌های دمو گیرندگان نامه ساخته شدند.");
                    }
                }

                // ==================== تقویم کاری: تنظیمات پیش‌فرض ====================
                if (!db.WorkCalendarSettings.Any())
                {
                    db.WorkCalendarSettings.Add(new WorkCalendarSettings());
                    db.SaveChanges();
                    Console.WriteLine("[DB] تنظیمات پیش‌فرض تقویم کاری ساخته شد (۰۸:۰۰–۱۶:۳۰، جمعه تعطیل).");
                }

                // ==================== تقویم کاری: تعطیلات رسمی سال جاری ====================
                if (!db.CompanyHolidays.Any())
                {
                    var (jy, _, _) = Shared.PersianDate.FromGregorian(DateTime.Now);
                    var added = SeedOfficialHolidays(db, jy);
                    Console.WriteLine(added > 0
                        ? $"[DB] {added} تعطیل رسمی سال {jy} از کاتالوگ وارد شد."
                        : "[DB] تعطیل رسمی برای سال جاری یافت نشد.");
                }

                // تعطیلات منابع انسانی اصلی — جدول اختصاصی HrMainHolidays (جدا از CompanyHolidays فروغ آریا)
                if (!db.HrMainHolidays.Any())
                {
                    var (jy, _, _) = Shared.PersianDate.FromGregorian(DateTime.Now);
                    var added = SeedHrMainOfficialHolidays(db, jy);
                    if (added > 0) Console.WriteLine($"[DB] {added} تعطیل رسمی سال {jy} برای منابع انسانی اصلی از کاتالوگ وارد شد.");
                }

                // دسته‌های هزینه پیش‌فرض (در اولین اجرا — کاربر می‌تواند مدیریتشان کند)
                if (!db.ExpenseCategories.Any())
                {
                    foreach (var name in new[] { "اجاره", "حقوق و دستمزد", "قبوض (آب/برق/گاز/تلفن)", "حمل و نقل", "ملزومات و اداری", "پذیرایی", "تعمیر و نگهداری", "تبلیغات و بازاریابی", "متفرقه" })
                        db.ExpenseCategories.Add(new ExpenseCategory { Name = name, CreatedAt = DateTime.Now });
                    db.SaveChanges();
                    Console.WriteLine("[DB] دسته‌های هزینه پیش‌فرض ساخته شدند.");
                }

                // دیتابیس‌های قدیمی: کالاهای بدون انبار اختصاصی به «انبار مرکزی» اختصاص می‌یابند
                var centralWh = db.Warehouses.FirstOrDefault(w => w.Name.Contains("مرکزی"))
                                ?? db.Warehouses.OrderBy(w => w.Id).FirstOrDefault();
                if (centralWh is not null)
                {
                    var orphan = db.Products.Where(p => p.WarehouseId == null && !p.IsService).ToList();
                    if (orphan.Count > 0)
                    {
                        foreach (var p in orphan) p.WarehouseId = centralWh.Id;
                        db.SaveChanges();
                        Console.WriteLine($"[DB] {orphan.Count} کالا به «{centralWh.Name}» اختصاص یافت.");
                    }
                }

                Console.WriteLine("[DB] اتصال و آماده‌سازی دیتابیس با موفقیت انجام شد ✔");
                return;
            }
            catch (Exception ex) when (!provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)
                                        && IsTransientSqlConnectionError(ex))
            {
                Console.WriteLine($"[DB] تلاش {attempt} از {maxAttempts} برای اتصال به دیتابیس ناموفق بود: {ex.Message}");

                if (attempt == maxAttempts)
                {
                    Console.WriteLine();
                    Console.WriteLine("================= خطای اتصال به SQL Server =================");
                    Console.WriteLine("برنامه نتوانست به SQL Server وصل شود. موارد زیر را بررسی کنید:");
                    Console.WriteLine("  1) سرویس SQL Server در حال اجرا باشد:");
                    Console.WriteLine("     services.msc → SQL Server (MSSQLSERVER) → Start");
                    Console.WriteLine("  2) اگر نسخه Express دارید، آدرس سرور باید .\\SQLEXPRESS باشد؛");
                    Console.WriteLine("     در appsettings.json مقدار Server=. را به Server=.\\SQLEXPRESS تغییر دهید.");
                    Console.WriteLine("  3) پروتکل TCP/IP یا Shared Memory در SQL Server Configuration Manager فعال باشد.");
                    Console.WriteLine("  4) رشته اتصال فعلی:");
                    var safeConnection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(config.GetConnectionString("Default"));
                    safeConnection.Password = "***";
                    Console.WriteLine($"     {safeConnection.ConnectionString}");
                    Console.WriteLine("  5) برای تست بدون SQL Server، در appsettings.json مقدار");
                    Console.WriteLine("     Database:Provider را روی \"Sqlite\" و ConnectionStrings:Default را روی");
                    Console.WriteLine("     \"Data Source=TotallSoftwareForoughAriya.db\" بگذارید.");
                    Console.WriteLine("=============================================================");
                    throw;
                }

                Thread.Sleep(TimeSpan.FromSeconds(3));
            }
        }
    }

    /// <summary>
    /// اعمال مایگریشن‌های SQL Server. اگر Users/PK_Users از قبل باشد، AddUsers اصلاً اجرا نمی‌شود.
    /// </summary>
    private static void MigrateSqlServer(AppDbContext db)
    {
        var canConnect = false;
        try { canConnect = db.Database.CanConnect(); }
        catch (Exception ex) { Console.WriteLine($"[DB] CanConnect: {ex.Message}"); }

        if (canConnect)
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260815083133_AddUsers')
AND (
    EXISTS (SELECT 1 FROM sys.tables WHERE [name] = N'Users')
    OR EXISTS (SELECT 1 FROM sys.key_constraints WHERE [name] = N'PK_Users')
    OR EXISTS (SELECT 1 FROM sys.objects WHERE [name] = N'PK_Users')
    OR OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
)
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815083133_AddUsers', N'8.0.1');
");
        }

        for (var i = 0; i < 80; i++)
        {
            try
            {
                db.Database.Migrate();
                return;
            }
            catch (Exception ex) when (IsDuplicateSqlObject(ex))
            {
                var pending = db.Database.GetPendingMigrations().FirstOrDefault();
                if (pending is null) throw;
                Console.WriteLine($"[DB] مایگریشن {pending} رد شد (شیء از قبل در دیتابیس است).");
                StampMigration(db, pending);
            }
        }

        db.Database.Migrate();
    }

    /// <summary>ستون شماره تماس کاربران سیستم — دیتابیس‌های قدیمی این ستون را ندارند.</summary>
    private static void EnsureUserSignatureColumn(AppDbContext db)
    {
        try { db.Database.ExecuteSqlRaw(@"IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Users', N'SignaturePath') IS NULL ALTER TABLE dbo.Users ADD SignaturePath nvarchar(300) NULL;"); }
        catch (Exception ex) { Console.WriteLine($"[DB] هشدار: افزودن ستون SignaturePath: {ex.Message}"); }
    }

    private static void EnsureSqliteUserSignatureColumn(AppDbContext db)
    {
        try { db.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN SignaturePath TEXT NULL;"); } catch { }
    }

    private static void EnsureSystemUserPhoneColumn(AppDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.SystemUsers', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.SystemUsers', N'Phone') IS NULL
    ALTER TABLE dbo.SystemUsers ADD Phone nvarchar(30) NULL;");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] هشدار: افزودن ستون Phone به SystemUsers: {ex.Message}");
        }
    }

    private static void EnsureSqliteSystemUserPhone(AppDbContext db)
    {
        try
        {
            if (db.Database.GetDbConnection() is not Microsoft.Data.Sqlite.SqliteConnection sqlConn)
                return;
            using var raw = new Microsoft.Data.Sqlite.SqliteConnection(sqlConn.ConnectionString);
            raw.Open();
            using (var check = raw.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SystemUsers'";
                if (Convert.ToInt32(check.ExecuteScalar()) == 0) return;
            }
            var cols = new List<string>();
            using (var c = raw.CreateCommand())
            {
                c.CommandText = "SELECT name FROM pragma_table_info('SystemUsers')";
                using var rd = c.ExecuteReader();
                while (rd.Read()) cols.Add(rd.GetString(0));
            }
            if (!cols.Contains("Phone"))
            {
                using var c = raw.CreateCommand();
                c.CommandText = "ALTER TABLE SystemUsers ADD COLUMN Phone TEXT";
                c.ExecuteNonQuery();
                Console.WriteLine("[DB] SQLite: ستون Phone به SystemUsers اضافه شد.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] هشدار: ستون Phone در SQLite: {ex.Message}");
        }
    }

    private static void StampMigration(AppDbContext db, string migrationId)
    {
        db.Database.ExecuteSqlInterpolated($@"
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = {migrationId})
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES ({migrationId}, {"8.0.1"});");
    }

    private static bool IsDuplicateSqlObject(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is SqlException sql)
            {
                foreach (SqlError err in sql.Errors)
                {
                    if (err.Number is 2714 or 1913 or 1779 or 2705 or 1750 or 2627 or 2601)
                        return true;
                }
                if (sql.Message.Contains("already an object named", StringComparison.OrdinalIgnoreCase)
                    || sql.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                    || sql.Message.Contains("Could not create constraint", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static bool IsTransientSqlConnectionError(Exception ex)
    {
        if (IsDuplicateSqlObject(ex)) return false;
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is SqlException sql)
            {
                foreach (SqlError err in sql.Errors)
                {
                    // شبکه / سرویس خاموش / لاگین — نه خطای اسکیما
                    if (err.Number is 2 or 53 or 64 or 233 or 4060 or 18456 or -2 or 10054 or 10060 or 10061)
                        return true;
                }
                if (sql.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase)
                    || sql.Message.Contains("server was not found", StringComparison.OrdinalIgnoreCase)
                    || sql.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            if (e is InvalidOperationException &&
                e.Message.Contains("SQL Server", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// خودتعمیرِ دیتابیس‌های SQLite که با نسخه‌های قدیمی ساخته شده‌اند:
    /// EnsureCreated() جدول/ستونِ جدید اضافه نمی‌کند؛ پس بخش تقویم کاری را به‌صورت دستی تکمیل می‌کنیم.
    /// </summary>
    /// <summary>
    /// ایجاد سال مالی جاری و کدینگ استاندارد حساب‌ها (گروه ← کل ← معین) در اولین اجرا،
    /// به‌همراه قواعد پیش‌فرض صدور خودکار سند از روی اسناد انبار.
    /// </summary>
    private static void SeedAccounting(AppDbContext db)
    {
        // ---------- سال مالی جاری ----------
        if (!db.AccFiscalYears.Any())
        {
            var todayFa = PersianDate.FromGregorian(DateTime.Now);
            var start = PersianDate.ToGregorian(todayFa.Year, 1, 1);
            var end = PersianDate.ToGregorian(todayFa.Year, 12, PersianDate.DaysInMonth(todayFa.Year, 12));

            db.AccFiscalYears.Add(new AccFiscalYear
            {
                Title = $"سال مالی {todayFa.Year}",
                Code = todayFa.Year.ToString(),
                StartDate = start,
                EndDate = end,
                IsCurrent = true
            });
            db.SaveChanges();
            Console.WriteLine("[DB] سال مالی جاری ساخته شد.");
        }

        // ---------- کدینگ حساب‌ها ----------
        if (!db.AccAccounts.Any())
        {
            // (کد، نام، کد والد، نوع، ماهیت، قابل ثبت، سیستمی)
            var rows = new (string Code, string Name, string? Parent, AccountType Type, AccountNature Nature, bool Postable, bool System)[]
            {
                // ============ ۱) دارایی‌های جاری ============
                ("1",      "دارایی‌های جاری",            null,   AccountType.Asset,     AccountNature.Debit,  false, true),
                ("11",     "موجودی نقد و بانک",          "1",    AccountType.Asset,     AccountNature.Debit,  false, true),
                ("1101",   "صندوق",                      "11",   AccountType.Asset,     AccountNature.Debit,  true,  true),
                ("1102",   "بانک",                       "11",   AccountType.Asset,     AccountNature.Debit,  true,  true),
                ("1103",   "تنخواه‌گردان",                "11",   AccountType.Asset,     AccountNature.Debit,  true,  false),
                ("12",     "حساب‌های دریافتنی",           "1",    AccountType.Asset,     AccountNature.Debit,  false, true),
                ("1201",   "حساب‌های دریافتنی تجاری",     "12",   AccountType.Asset,     AccountNature.Debit,  true,  true),
                ("1202",   "اسناد دریافتنی (چک)",        "12",   AccountType.Asset,     AccountNature.Debit,  true,  false),
                ("13",     "موجودی کالا",                "1",    AccountType.Asset,     AccountNature.Debit,  false, true),
                ("1301",   "موجودی کالا — انبار",        "13",   AccountType.Asset,     AccountNature.Debit,  true,  true),
                ("1302",   "کالای در راه",               "13",   AccountType.Asset,     AccountNature.Debit,  true,  false),
                ("14",     "پیش‌پرداخت‌ها",               "1",    AccountType.Asset,     AccountNature.Debit,  false, false),
                ("1401",   "پیش‌پرداخت خرید",            "14",   AccountType.Asset,     AccountNature.Debit,  true,  false),

                // ============ ۲) دارایی‌های ثابت ============
                ("2",      "دارایی‌های ثابت",             null,   AccountType.Asset,     AccountNature.Debit,  false, false),
                ("21",     "اموال، ماشین‌آلات و تجهیزات",  "2",    AccountType.Asset,     AccountNature.Debit,  false, false),
                ("2101",   "اثاثیه و منصوبات",           "21",   AccountType.Asset,     AccountNature.Debit,  true,  false),
                ("2102",   "ماشین‌آلات",                  "21",   AccountType.Asset,     AccountNature.Debit,  true,  false),
                ("2103",   "استهلاک انباشته",            "21",   AccountType.Asset,     AccountNature.Credit, true,  false),

                // ============ ۳) بدهی‌ها ============
                ("3",      "بدهی‌های جاری",               null,   AccountType.Liability, AccountNature.Credit, false, true),
                ("31",     "حساب‌های پرداختنی",           "3",    AccountType.Liability, AccountNature.Credit, false, true),
                ("3101",   "حساب‌های پرداختنی تجاری",     "31",   AccountType.Liability, AccountNature.Credit, true,  true),
                ("3102",   "اسناد پرداختنی (چک)",        "31",   AccountType.Liability, AccountNature.Credit, true,  false),
                ("32",     "مالیات و عوارض",             "3",    AccountType.Liability, AccountNature.Credit, false, true),
                ("3201",   "مالیات بر ارزش افزوده",      "32",   AccountType.Liability, AccountNature.Both,   true,  true),
                ("3202",   "عوارض",                      "32",   AccountType.Liability, AccountNature.Both,   true,  false),
                ("33",     "پیش‌دریافت‌ها",               "3",    AccountType.Liability, AccountNature.Credit, false, false),
                ("3301",   "پیش‌دریافت فروش",            "33",   AccountType.Liability, AccountNature.Credit, true,  false),

                // ============ ۴) سرمایه ============
                ("4",      "حقوق صاحبان سهام",           null,   AccountType.Equity,    AccountNature.Credit, false, true),
                ("41",     "سرمایه",                     "4",    AccountType.Equity,    AccountNature.Credit, false, true),
                ("4101",   "سرمایه اولیه",               "41",   AccountType.Equity,    AccountNature.Credit, true,  true),
                ("4102",   "سود و زیان انباشته",         "41",   AccountType.Equity,    AccountNature.Both,   true,  true),

                // ============ ۵) درآمد ============
                ("5",      "درآمدها",                    null,   AccountType.Income,    AccountNature.Credit, false, true),
                ("51",     "درآمد عملیاتی",              "5",    AccountType.Income,    AccountNature.Credit, false, true),
                ("5101",   "فروش کالا",                  "51",   AccountType.Income,    AccountNature.Credit, true,  true),
                ("5102",   "برگشت از فروش و تخفیفات",    "51",   AccountType.Income,    AccountNature.Debit,  true,  true),
                ("52",     "درآمد غیرعملیاتی",           "5",    AccountType.Income,    AccountNature.Credit, false, false),
                ("5201",   "سایر درآمدها",               "52",   AccountType.Income,    AccountNature.Credit, true,  false),

                // ============ ۶) بهای تمام‌شده و هزینه‌ها ============
                ("6",      "بهای تمام‌شده و هزینه‌ها",     null,   AccountType.Expense,   AccountNature.Debit,  false, true),
                ("61",     "بهای تمام‌شده کالای فروش‌رفته", "6",   AccountType.Expense,   AccountNature.Debit,  false, true),
                ("6101",   "بهای تمام‌شده کالای فروش‌رفته", "61",  AccountType.Expense,   AccountNature.Debit,  true,  true),
                ("6102",   "کسری و ضایعات انبار",        "61",   AccountType.Expense,   AccountNature.Debit,  true,  true),
                ("62",     "خرید",                       "6",    AccountType.Expense,   AccountNature.Debit,  false, true),
                ("6201",   "خرید کالا",                  "62",   AccountType.Expense,   AccountNature.Debit,  true,  true),
                ("6202",   "برگشت از خرید",              "62",   AccountType.Expense,   AccountNature.Credit, true,  true),
                ("63",     "هزینه‌های عمومی و اداری",     "6",    AccountType.Expense,   AccountNature.Debit,  false, false),
                ("6301",   "حقوق و دستمزد",              "63",   AccountType.Expense,   AccountNature.Debit,  true,  false),
                ("6302",   "اجاره",                      "63",   AccountType.Expense,   AccountNature.Debit,  true,  false),
                ("6303",   "آب، برق، گاز و تلفن",        "63",   AccountType.Expense,   AccountNature.Debit,  true,  false),
                ("6304",   "حمل و نقل",                  "63",   AccountType.Expense,   AccountNature.Debit,  true,  false),
                ("6305",   "سایر هزینه‌ها",               "63",   AccountType.Expense,   AccountNature.Debit,  true,  false)
            };

            var map = new Dictionary<string, AccAccount>();
            var order = 1;
            foreach (var r in rows)
            {
                var level = r.Parent is null
                    ? AccountLevel.Group
                    : map[r.Parent].Level switch
                    {
                        AccountLevel.Group => AccountLevel.General,
                        AccountLevel.General => AccountLevel.Subsidiary,
                        _ => AccountLevel.Detail
                    };

                var acc = new AccAccount
                {
                    Code = r.Code,
                    Name = r.Name,
                    ParentId = r.Parent is null ? null : map[r.Parent].Id,
                    Level = level,
                    Type = r.Type,
                    Nature = r.Nature,
                    IsPermanent = r.Type is AccountType.Asset or AccountType.Liability or AccountType.Equity,
                    IsPostable = r.Postable,
                    IsSystem = r.System,
                    RequiresParty = r.Code is "1201" or "3101",
                    SortOrder = order++
                };
                db.AccAccounts.Add(acc);
                db.SaveChanges();   // برای گرفتن Id جهت گره‌های فرزند
                map[r.Code] = acc;
            }
            Console.WriteLine($"[DB] کدینگ پیش‌فرض حساب‌ها ({rows.Length} حساب) ساخته شد.");
        }

        // ---------- قواعد صدور خودکار سند از روی اسناد انبار ----------
        if (!db.AccInvRules.Any() && db.InvDocTypes.Any() && db.AccAccounts.Any())
        {
            int? Acc(string code) => db.AccAccounts.FirstOrDefault(a => a.Code == code)?.Id;

            var inventory = Acc("1301");     // موجودی کالا — انبار
            var purchase = Acc("6201");      // خرید کالا
            var cogs = Acc("6101");          // بهای تمام‌شده کالای فروش‌رفته
            var scrap = Acc("6102");         // کسری و ضایعات
            var salesReturn = Acc("5102");   // برگشت از فروش
            var purchaseReturn = Acc("6202");// برگشت از خرید
            var capital = Acc("4101");       // سرمایه اولیه (افتتاحیه انبار)

            // (کد نوع سند، حساب طرف مقابل)
            var pairs = new (string DocCode, int? Counter)[]
            {
                ("RC-OPEN", capital),
                ("RC-BUY",  purchase),
                ("RC-RET",  salesReturn),
                ("RC-ADJ",  scrap),
                ("IS-SELL", cogs),
                ("IS-USE",  cogs),
                ("IS-RET",  purchaseReturn),
                ("IS-SCRP", scrap),
                ("IS-ADJ",  scrap)
            };

            foreach (var (docCode, counter) in pairs)
            {
                var type = db.InvDocTypes.FirstOrDefault(t => t.Code == docCode);
                if (type is null || inventory is null || counter is null) continue;

                db.AccInvRules.Add(new AccInvRule
                {
                    DocTypeId = type.Id,
                    InventoryAccountId = inventory,
                    CounterAccountId = counter,
                    UseCostValue = true,
                    // پیش‌فرض غیرفعال: تا وقتی کاربر کدینگ را بررسی و تایید نکرده، سند خودکار صادر نمی‌شود
                    IsActive = false,
                    Description = "قاعده‌ی پیش‌فرض — پس از بررسی کدینگ، آن را فعال کنید."
                });
            }
            db.SaveChanges();
            Console.WriteLine("[DB] قواعد پیش‌فرض سند خودکار انبار ساخته شدند (غیرفعال).");
        }
    }

    /// <summary>
    /// پیکربندی پیش‌فرض انواع فاکتور: نوع سند انبار و حساب‌های سند خودکار.
    /// </summary>
    private static void SeedInvoicing(AppDbContext db)
    {
        if (db.FacRules.Any() || !db.AccAccounts.Any() || !db.InvDocTypes.Any()) return;

        int? Acc(string code) => db.AccAccounts.FirstOrDefault(a => a.Code == code)?.Id;
        int? Doc(string code) => db.InvDocTypes.FirstOrDefault(t => t.Code == code)?.Id;

        var receivable = Acc("1201");    // حساب‌های دریافتنی تجاری
        var payable = Acc("3101");       // حساب‌های پرداختنی تجاری
        var vat = Acc("3201");           // مالیات بر ارزش افزوده
        var cash = Acc("1101");          // صندوق
        var sales = Acc("5101");         // فروش کالا
        var salesReturn = Acc("5102");   // برگشت از فروش و تخفیفات
        var purchase = Acc("6201");      // خرید کالا
        var purchaseReturn = Acc("6202");// برگشت از خرید
        var freightIn = Acc("6304");     // حمل و نقل (هزینه)
        var otherIncome = Acc("5201");   // سایر درآمدها (حمل دریافتی از مشتری)

        db.FacRules.AddRange(
            // فاکتور خرید → رسید خرید | خرید بدهکار، پرداختنی بستانکار
            new FacRule
            {
                Kind = InvoiceKind.Purchase,
                DocTypeId = Doc("RC-BUY"),
                PartyAccountId = payable,
                MainAccountId = purchase,
                VatAccountId = vat,
                CashAccountId = cash,
                ShippingAccountId = freightIn,
                AutoInvDoc = true,
                AutoVoucher = true,
                IsActive = true,
                Description = "خرید کالا بدهکار / حساب‌های پرداختنی (یا صندوق) بستانکار"
            },
            // فاکتور فروش → حواله فروش | دریافتنی بدهکار، فروش بستانکار
            new FacRule
            {
                Kind = InvoiceKind.Sale,
                DocTypeId = Doc("IS-SELL"),
                PartyAccountId = receivable,
                MainAccountId = sales,
                VatAccountId = vat,
                CashAccountId = cash,
                ShippingAccountId = otherIncome,
                AutoInvDoc = true,
                AutoVoucher = true,
                IsActive = true,
                Description = "حساب‌های دریافتنی (یا صندوق) بدهکار / فروش کالا بستانکار"
            },
            // برگشت از خرید → حواله برگشت از خرید
            new FacRule
            {
                Kind = InvoiceKind.PurchaseReturn,
                DocTypeId = Doc("IS-RET"),
                PartyAccountId = payable,
                MainAccountId = purchaseReturn,
                VatAccountId = vat,
                CashAccountId = cash,
                ShippingAccountId = freightIn,
                AutoInvDoc = true,
                AutoVoucher = true,
                IsActive = true,
                Description = "حساب‌های پرداختنی بدهکار / برگشت از خرید بستانکار"
            },
            // برگشت از فروش → رسید برگشت از فروش
            new FacRule
            {
                Kind = InvoiceKind.SaleReturn,
                DocTypeId = Doc("RC-RET"),
                PartyAccountId = receivable,
                MainAccountId = salesReturn,
                VatAccountId = vat,
                CashAccountId = cash,
                ShippingAccountId = otherIncome,
                AutoInvDoc = true,
                AutoVoucher = true,
                IsActive = true,
                Description = "برگشت از فروش بدهکار / حساب‌های دریافتنی بستانکار"
            });

        db.SaveChanges();
        Console.WriteLine("[DB] پیکربندی پیش‌فرض انواع فاکتور ساخته شد.");
    }

    /// <summary>
    /// خزانه‌داری: حساب «اسناد در جریان وصول»، صندوق و بانک پیش‌فرض
    /// و قواعد سند خودکار دریافت/پرداخت.
    /// </summary>
    private static void SeedTreasury(AppDbContext db)
    {
        if (!db.AccAccounts.Any()) return;

        AccAccount? Acc(string code) => db.AccAccounts.FirstOrDefault(a => a.Code == code);

        // ---------- حساب «اسناد در جریان وصول» ----------
        // در کدینگ پیش‌فرض وجود ندارد چون فقط ماژول خزانه به آن نیاز دارد.
        var collection = Acc("1203");
        if (collection is null)
        {
            var parent = Acc("12");   // حساب‌های دریافتنی
            if (parent is not null)
            {
                collection = new AccAccount
                {
                    Code = "1203",
                    Name = "اسناد در جریان وصول",
                    ParentId = parent.Id,
                    Level = AccountLevel.Subsidiary,
                    Type = AccountType.Asset,
                    Nature = AccountNature.Debit,
                    IsPermanent = true,
                    IsPostable = true,
                    IsSystem = false,
                    SortOrder = (db.AccAccounts.Max(a => (int?)a.SortOrder) ?? 0) + 1
                };
                db.AccAccounts.Add(collection);
                db.SaveChanges();
                Console.WriteLine("[DB] حساب «اسناد در جریان وصول» (۱۲۰۳) ساخته شد.");
            }
        }

        // ---------- صندوق و بانک پیش‌فرض ----------
        if (!db.TrsAccounts.Any())
        {
            db.TrsAccounts.Add(new TrsAccount
            {
                Code = "CSH-01",
                Name = "صندوق مرکزی",
                Kind = TreasuryAccountKind.Cash,
                AccountId = Acc("1101")?.Id,
                IsDefault = true,
                IsActive = true,
                SortOrder = 1,
                Description = "صندوق نقدی پیش‌فرض سیستم"
            });

            db.TrsAccounts.Add(new TrsAccount
            {
                Code = "BNK-01",
                Name = "حساب جاری",
                Kind = TreasuryAccountKind.Bank,
                AccountId = Acc("1102")?.Id,
                BankName = "بانک ملت",
                IsActive = true,
                SortOrder = 2,
                Description = "حساب بانکی پیش‌فرض سیستم"
            });

            db.SaveChanges();
            Console.WriteLine("[DB] صندوق و حساب بانکی پیش‌فرض ساخته شدند.");
        }

        // ---------- قواعد سند خودکار خزانه ----------
        if (!db.TrsRules.Any())
        {
            var receivable = Acc("1201")?.Id;      // حساب‌های دریافتنی تجاری
            var payable = Acc("3101")?.Id;         // حساب‌های پرداختنی تجاری
            var chequeIn = Acc("1202")?.Id;        // اسناد دریافتنی (چک)
            var chequeOut = Acc("3102")?.Id;       // اسناد پرداختنی (چک)
            var inCollection = collection?.Id;     // اسناد در جریان وصول
            var salesDiscount = Acc("5102")?.Id;   // برگشت از فروش و تخفیفات
            var purchaseDiscount = Acc("6202")?.Id;// برگشت از خرید (تخفیف دریافتی)
            var bankFee = Acc("6305")?.Id;         // سایر هزینه‌ها (کارمزد بانکی)

            db.TrsRules.AddRange(
                new TrsRule
                {
                    Kind = TreasuryKind.Receipt,
                    PartyAccountId = receivable,
                    ChequeAccountId = chequeIn,
                    CollectionAccountId = inCollection,
                    DiscountAccountId = salesDiscount,
                    FeeAccountId = bankFee,
                    AutoVoucher = true,
                    IsActive = receivable is not null,
                    Description = "صندوق/بانک و اسناد دریافتنی بدهکار / حساب‌های دریافتنی بستانکار"
                },
                new TrsRule
                {
                    Kind = TreasuryKind.Payment,
                    PartyAccountId = payable,
                    ChequeAccountId = chequeOut,
                    CollectionAccountId = null,
                    DiscountAccountId = purchaseDiscount,
                    FeeAccountId = bankFee,
                    AutoVoucher = true,
                    IsActive = payable is not null,
                    Description = "حساب‌های پرداختنی بدهکار / صندوق‌ بانک و اسناد پرداختنی بستانکار"
                });

            db.SaveChanges();
            Console.WriteLine("[DB] قواعد پیش‌فرض سند خودکار خزانه ساخته شد.");
        }
    }

    /// <summary>
    /// خودتعمیر جداول/ستون‌های «تقویم کاری، تعطیلات و شیفت‌ها» روی SQL Server — معادل EnsureSqliteWorkCalendarSchema.
    /// برای دیتابیس‌های قدیمی ضروری است: مایگریشن squash شده وقتی جدولی مثل Users از قبل باشد stamp و رد می‌شود،
    /// پس جدول‌ها/ستون‌های بعدی (WorkCalendarSettings، CompanyHolidays.IsOfficial، OvertimeMode و...) هرگز ساخته نمی‌شوند
    /// و endpoint تقویم با خطای «Invalid object name / Invalid column name» از کار می‌افتد.
    /// همهٔ دستورها idempotent هستند (IF OBJECT_ID / COL_LENGTH) و خطای هر کدام فقط ثبت می‌شود.
    /// </summary>
    private static void EnsureSqlServerWorkCalendarSchema(AppDbContext db)
    {
        void Run(string sql, string what)
        {
            try { db.Database.ExecuteSqlRaw(sql); }
            catch (Exception ex) { Console.WriteLine($"[DB] هشدار: {what}: {ex.Message}"); }
        }

        // ---- WorkCalendarDays: جدول کامل (اگر نبود) ----
        Run(@"
IF OBJECT_ID(N'dbo.WorkCalendarDays', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkCalendarDays (
        Id int IDENTITY(1,1) NOT NULL,
        Date datetime2 NOT NULL,
        IsWorkday bit NOT NULL,
        StartTime time NULL,
        EndTime time NULL,
        GraceMinutes int NOT NULL CONSTRAINT DF_WorkCalendarDays_GraceMinutes DEFAULT 0,
        OvertimeHours float NOT NULL CONSTRAINT DF_WorkCalendarDays_OvertimeHours DEFAULT 0,
        OvertimeMode int NOT NULL CONSTRAINT DF_WorkCalendarDays_OvertimeMode DEFAULT 0,
        OvertimeStart time NULL,
        OvertimeEnd time NULL,
        Note nvarchar(100) NULL,
        CreatedAt datetime2 NOT NULL,
        UpdatedAt datetime2 NULL,
        CONSTRAINT PK_WorkCalendarDays PRIMARY KEY (Id)
    );
    CREATE UNIQUE INDEX IX_WorkCalendarDays_Date ON dbo.WorkCalendarDays (Date);
END", "ساخت جدول WorkCalendarDays");

        // ---- WorkCalendarDays: ستون‌های اضافه‌شدهٔ بعدی (جدول قدیمی) ----
        Run(@"IF OBJECT_ID(N'dbo.WorkCalendarDays', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarDays', N'StartTime') IS NULL ALTER TABLE dbo.WorkCalendarDays ADD StartTime time NULL;", "ستون StartTime");
        Run(@"IF OBJECT_ID(N'dbo.WorkCalendarDays', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarDays', N'EndTime') IS NULL ALTER TABLE dbo.WorkCalendarDays ADD EndTime time NULL;", "ستون EndTime");
        Run(@"IF OBJECT_ID(N'dbo.WorkCalendarDays', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarDays', N'GraceMinutes') IS NULL ALTER TABLE dbo.WorkCalendarDays ADD GraceMinutes int NOT NULL DEFAULT 0;", "ستون GraceMinutes");
        Run(@"IF OBJECT_ID(N'dbo.WorkCalendarDays', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarDays', N'OvertimeHours') IS NULL ALTER TABLE dbo.WorkCalendarDays ADD OvertimeHours float NOT NULL DEFAULT 0;", "ستون OvertimeHours");
        Run(@"IF OBJECT_ID(N'dbo.WorkCalendarDays', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarDays', N'OvertimeMode') IS NULL ALTER TABLE dbo.WorkCalendarDays ADD OvertimeMode int NOT NULL DEFAULT 0;", "ستون OvertimeMode");
        Run(@"IF OBJECT_ID(N'dbo.WorkCalendarDays', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarDays', N'OvertimeStart') IS NULL ALTER TABLE dbo.WorkCalendarDays ADD OvertimeStart time NULL;", "ستون OvertimeStart");
        Run(@"IF OBJECT_ID(N'dbo.WorkCalendarDays', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarDays', N'OvertimeEnd') IS NULL ALTER TABLE dbo.WorkCalendarDays ADD OvertimeEnd time NULL;", "ستون OvertimeEnd");
        Run(@"IF OBJECT_ID(N'dbo.WorkCalendarDays', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.WorkCalendarDays', N'Note') IS NULL ALTER TABLE dbo.WorkCalendarDays ADD Note nvarchar(100) NULL;", "ستون Note");

        // ---- CompanyHolidays: جدول (اگر نبود) + ستون IsOfficial ----
        Run(@"
IF OBJECT_ID(N'dbo.CompanyHolidays', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompanyHolidays (
        Id int IDENTITY(1,1) NOT NULL,
        HolidayDate datetime2 NOT NULL,
        Name nvarchar(100) NOT NULL,
        CreatedByName nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL,
        CONSTRAINT PK_CompanyHolidays PRIMARY KEY (Id)
    );
    CREATE INDEX IX_CompanyHolidays_HolidayDate ON dbo.CompanyHolidays (HolidayDate);
END", "ساخت جدول CompanyHolidays");
        Run(@"IF OBJECT_ID(N'dbo.CompanyHolidays', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.CompanyHolidays', N'IsOfficial') IS NULL ALTER TABLE dbo.CompanyHolidays ADD IsOfficial bit NOT NULL DEFAULT 0;", "ستون IsOfficial");

        // ---- WorkCalendarSettings: جدول تنظیمات تقویم کاری ----
        Run(@"
IF OBJECT_ID(N'dbo.WorkCalendarSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkCalendarSettings (
        Id int IDENTITY(1,1) NOT NULL,
        DefaultStart time NOT NULL,
        DefaultEnd time NOT NULL,
        GraceMinutes int NOT NULL CONSTRAINT DF_WorkCalendarSettings_GraceMinutes DEFAULT 10,
        RestDayFlags int NOT NULL CONSTRAINT DF_WorkCalendarSettings_RestDayFlags DEFAULT 32,
        ApplyOfficialHolidays bit NOT NULL CONSTRAINT DF_WorkCalendarSettings_ApplyOfficialHolidays DEFAULT 1,
        UpdatedAt datetime2 NOT NULL,
        CONSTRAINT PK_WorkCalendarSettings PRIMARY KEY (Id)
    );
    CREATE UNIQUE INDEX IX_WorkCalendarSettings_Id ON dbo.WorkCalendarSettings (Id);
END", "ساخت جدول WorkCalendarSettings");

        // ---- ShiftGroups: ستون‌های شیفت دوپاره ----
        Run(@"IF OBJECT_ID(N'dbo.ShiftGroups', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ShiftGroups', N'StartTime2') IS NULL ALTER TABLE dbo.ShiftGroups ADD StartTime2 time NULL;", "ستون StartTime2");
        Run(@"IF OBJECT_ID(N'dbo.ShiftGroups', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ShiftGroups', N'EndTime2') IS NULL ALTER TABLE dbo.ShiftGroups ADD EndTime2 time NULL;", "ستون EndTime2");

        // ---- AttendanceSegments: ستون دستگاه ورود ----
        Run(@"IF OBJECT_ID(N'dbo.AttendanceSegments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceSegments', N'EnterDevice') IS NULL ALTER TABLE dbo.AttendanceSegments ADD EnterDevice nvarchar(250) NULL;", "ستون EnterDevice");

        // ---- AuditLogs: جدول لاگ عملیات ----
        Run(@"
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs (
        Id bigint IDENTITY(1,1) NOT NULL,
        At datetime2 NOT NULL,
        UserId int NULL,
        Username nvarchar(100) NULL,
        Module nvarchar(80) NOT NULL,
        Action nvarchar(80) NOT NULL,
        HttpMethod nvarchar(10) NOT NULL,
        Path nvarchar(300) NULL,
        Summary nvarchar(200) NULL,
        Payload nvarchar(4000) NULL,
        Ip nvarchar(64) NULL,
        Device nvarchar(250) NULL,
        StatusCode int NOT NULL CONSTRAINT DF_AuditLogs_StatusCode DEFAULT 0,
        DurationMs int NOT NULL CONSTRAINT DF_AuditLogs_DurationMs DEFAULT 0,
        CONSTRAINT PK_AuditLogs PRIMARY KEY (Id)
    );
    CREATE INDEX IX_AuditLogs_At ON dbo.AuditLogs (At);
    CREATE INDEX IX_AuditLogs_UserId ON dbo.AuditLogs (UserId);
END", "ساخت جدول AuditLogs");
    }

    private static void EnsureSqliteWorkCalendarSchema(AppDbContext db)
    {
        try
        {
            if (db.Database.GetDbConnection() is not Microsoft.Data.Sqlite.SqliteConnection sqlConn)
                return;

            using var raw = new Microsoft.Data.Sqlite.SqliteConnection(sqlConn.ConnectionString);
            raw.Open();

            int TableCount(string sql)
            {
                using var c = raw.CreateCommand();
                c.CommandText = sql;
                return Convert.ToInt32(c.ExecuteScalar());
            }

            var tableExists = TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='WorkCalendarDays'") > 0;
            if (!tableExists)
            {
                using (var c = raw.CreateCommand())
                {
                    c.CommandText = @"
                        CREATE TABLE WorkCalendarDays (
                            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                            Date TEXT NOT NULL,
                            IsWorkday INTEGER NOT NULL,
                            StartTime TEXT,
                            EndTime TEXT,
                            GraceMinutes INTEGER NOT NULL DEFAULT 0,
                            OvertimeHours REAL NOT NULL DEFAULT 0,
                            OvertimeMode INTEGER NOT NULL DEFAULT 0,
                            OvertimeStart TEXT,
                            OvertimeEnd TEXT,
                            Note TEXT,
                            CreatedAt TEXT NOT NULL,
                            UpdatedAt TEXT
                        );
                        CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkCalendarDays_Date ON WorkCalendarDays (Date);";
                    c.ExecuteNonQuery();
                }
                Console.WriteLine("[DB] SQLite: جدول WorkCalendarDays ساخته شد.");
                return;
            }

            var cols = new List<string>();
            using (var c = raw.CreateCommand())
            {
                c.CommandText = "SELECT name FROM pragma_table_info('WorkCalendarDays')";
                using var rd = c.ExecuteReader();
                while (rd.Read()) cols.Add(rd.GetString(0));
            }

            void AddIfMissing(string name, string type, string? def = null)
            {
                if (!cols.Contains(name))
                {
                    using var c = raw.CreateCommand();
                    c.CommandText = $"ALTER TABLE WorkCalendarDays ADD COLUMN {name} {type}{(def == null ? "" : $" DEFAULT {def}")}";
                    c.ExecuteNonQuery();
                    Console.WriteLine($"[DB] SQLite: ستون {name} به WorkCalendarDays اضافه شد.");
                }
            }
            AddIfMissing("GraceMinutes", "INTEGER", "0");
            AddIfMissing("OvertimeHours", "REAL", "0");
            AddIfMissing("OvertimeMode", "INTEGER", "0");
            AddIfMissing("OvertimeStart", "TEXT");
            AddIfMissing("OvertimeEnd", "TEXT");

            // ---- ShiftGroups: ستون‌های شیفت دوپاره ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ShiftGroups'") > 0)
            {
                var shCols = new List<string>();
                using (var c = raw.CreateCommand())
                {
                    c.CommandText = "SELECT name FROM pragma_table_info('ShiftGroups')";
                    using var rd = c.ExecuteReader();
                    while (rd.Read()) shCols.Add(rd.GetString(0));
                }
                foreach (var colName in new[] { "StartTime2", "EndTime2" })
                {
                    if (!shCols.Contains(colName))
                    {
                        using var c = raw.CreateCommand();
                        c.CommandText = $"ALTER TABLE ShiftGroups ADD COLUMN {colName} TEXT";
                        c.ExecuteNonQuery();
                        Console.WriteLine($"[DB] SQLite: ستون {colName} به ShiftGroups اضافه شد.");
                    }
                }
            }

            // ---- AttendanceSegments: ستون دستگاه ورود ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AttendanceSegments'") > 0)
            {
                var segCols = new List<string>();
                using (var c = raw.CreateCommand())
                {
                    c.CommandText = "SELECT name FROM pragma_table_info('AttendanceSegments')";
                    using var rd = c.ExecuteReader();
                    while (rd.Read()) segCols.Add(rd.GetString(0));
                }
                if (!segCols.Contains("EnterDevice"))
                {
                    using var c = raw.CreateCommand();
                    c.CommandText = "ALTER TABLE AttendanceSegments ADD COLUMN EnterDevice TEXT";
                    c.ExecuteNonQuery();
                    Console.WriteLine("[DB] SQLite: ستون EnterDevice به AttendanceSegments اضافه شد.");
                }
            }

            // ---- AuditLogs: جدول لاگ عملیات ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AuditLogs'") == 0)
            {
                using var c = raw.CreateCommand();
                c.CommandText = @"
                    CREATE TABLE AuditLogs (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        At TEXT NOT NULL,
                        UserId INTEGER NULL,
                        Username TEXT NULL,
                        Module TEXT NOT NULL,
                        Action TEXT NOT NULL,
                        HttpMethod TEXT NOT NULL,
                        Path TEXT NULL,
                        Summary TEXT NULL,
                        Payload TEXT NULL,
                        Ip TEXT NULL,
                        Device TEXT NULL,
                        StatusCode INTEGER NOT NULL DEFAULT 0,
                        DurationMs INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE INDEX IF NOT EXISTS IX_AuditLogs_At ON AuditLogs (At);
                    CREATE INDEX IF NOT EXISTS IX_AuditLogs_UserId ON AuditLogs (UserId);";
                c.ExecuteNonQuery();
                Console.WriteLine("[DB] SQLite: جدول AuditLogs ساخته شد.");
            }

            // ---- CompanyHolidays: ستون IsOfficial ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CompanyHolidays'") > 0)
            {
                var holCols = new List<string>();
                using (var c = raw.CreateCommand())
                {
                    c.CommandText = "SELECT name FROM pragma_table_info('CompanyHolidays')";
                    using var rd = c.ExecuteReader();
                    while (rd.Read()) holCols.Add(rd.GetString(0));
                }
                if (!holCols.Contains("IsOfficial"))
                {
                    using var c = raw.CreateCommand();
                    c.CommandText = "ALTER TABLE CompanyHolidays ADD COLUMN IsOfficial INTEGER NOT NULL DEFAULT 0";
                    c.ExecuteNonQuery();
                    Console.WriteLine("[DB] SQLite: ستون IsOfficial به CompanyHolidays اضافه شد.");
                }
            }

            // ---- WorkCalendarSettings: جدول تنظیمات تقویم کاری ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='WorkCalendarSettings'") == 0)
            {
                using var c = raw.CreateCommand();
                c.CommandText = @"
                    CREATE TABLE WorkCalendarSettings (
                        Id INTEGER NOT NULL PRIMARY KEY,
                        DefaultStart TEXT NOT NULL,
                        DefaultEnd TEXT NOT NULL,
                        GraceMinutes INTEGER NOT NULL DEFAULT 10,
                        RestDayFlags INTEGER NOT NULL DEFAULT 32,
                        ApplyOfficialHolidays INTEGER NOT NULL DEFAULT 1,
                        UpdatedAt TEXT NOT NULL
                    );";
                c.ExecuteNonQuery();
                Console.WriteLine("[DB] SQLite: جدول WorkCalendarSettings ساخته شد.");
            }

            // ---- DocTags, DocumentTags, DocExtractedTexts: تگ‌ها و متن‌های استخراج‌شده آرشیو ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DocTags'") == 0)
            {
                using var c = raw.CreateCommand();
                c.CommandText = @"
                    CREATE TABLE DocTags (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Color TEXT NOT NULL,
                        Description TEXT NULL,
                        CreatedAt TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_DocTags_Name ON DocTags (Name);";
                c.ExecuteNonQuery();
                Console.WriteLine("[DB] SQLite: جدول DocTags ساخته شد.");
            }

            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DocumentTags'") == 0)
            {
                using var c = raw.CreateCommand();
                c.CommandText = @"
                    CREATE TABLE DocumentTags (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        DocumentId INTEGER NOT NULL,
                        TagId INTEGER NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_DocumentTags_DocumentId_TagId ON DocumentTags (DocumentId, TagId);";
                c.ExecuteNonQuery();
                Console.WriteLine("[DB] SQLite: جدول DocumentTags ساخته شد.");
            }

            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DocExtractedTexts'") == 0)
            {
                using var c = raw.CreateCommand();
                c.CommandText = @"
                    CREATE TABLE DocExtractedTexts (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        DocumentId INTEGER NOT NULL,
                        VersionId INTEGER NOT NULL,
                        AttachmentId INTEGER NOT NULL,
                        FileName TEXT NOT NULL,
                        ContentType TEXT NOT NULL,
                        SourceType TEXT NOT NULL,
                        ExtractedText TEXT NOT NULL,
                        NormalizedText TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        ErrorMessage TEXT NULL,
                        CharacterCount INTEGER NOT NULL,
                        IndexedAt TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_DocExtractedTexts_DocumentId_AttachmentId ON DocExtractedTexts (DocumentId, AttachmentId);
                    CREATE INDEX IF NOT EXISTS IX_DocExtractedTexts_AttachmentId ON DocExtractedTexts (AttachmentId);";
                c.ExecuteNonQuery();
                Console.WriteLine("[DB] SQLite: جدول DocExtractedTexts ساخته شد.");
            }

            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DocEntityLinks'") == 0)
            {
                using var c = raw.CreateCommand();
                c.CommandText = @"
                    CREATE TABLE DocEntityLinks (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        DocumentId INTEGER NOT NULL,
                        Module TEXT NOT NULL,
                        EntityId INTEGER NOT NULL,
                        EntityCode TEXT NULL,
                        EntityTitle TEXT NOT NULL,
                        Note TEXT NULL,
                        CreatedByUserId INTEGER NOT NULL,
                        CreatedByName TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_DocEntityLinks_DocumentId_Module_EntityId ON DocEntityLinks (DocumentId, Module, EntityId);
                    CREATE INDEX IF NOT EXISTS IX_DocEntityLinks_Module_EntityId ON DocEntityLinks (Module, EntityId);";
                c.ExecuteNonQuery();
                Console.WriteLine("[DB] SQLite: جدول DocEntityLinks ساخته شد.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] هشدار: خودتعمیرِ شمای SQLite انجام نشد: {ex.Message}");
        }
    }

    /// <summary>ذخیره‌ی اولیه‌ی تعطیلات رسمی یک سال شمسی از کاتالوگ — تعداد واردشده برمی‌گردد.</summary>
    public static int SeedOfficialHolidays(AppDbContext db, int jy)
    {
        var items = OfficialHolidayCatalog.GetForYear(jy);
        var added = 0;
        foreach (var (m, d, name) in items)
        {
            var g = Shared.PersianDate.ToGregorian(jy, m, d);
            if (g == DateTime.MinValue) continue;
            if (db.CompanyHolidays.Any(h => h.HolidayDate.Date == g.Date)) continue;
            db.CompanyHolidays.Add(new CompanyHoliday
            {
                HolidayDate = g,
                Name = name,
                IsOfficial = true,
                CreatedByName = "سیستم (کاتالوگ تعطیلات)",
                CreatedAt = DateTime.Now,
            });
            added++;
        }
        if (added > 0) db.SaveChanges();
        return added;
    }

    /// <summary>تعطیلات رسمی کاتالوگ برای «منابع انسانی اصلی» (جدول اختصاصی HrMainHolidays).</summary>
    public static int SeedHrMainOfficialHolidays(AppDbContext db, int jy)
    {
        var added = 0;
        foreach (var (m, d, name) in OfficialHolidayCatalog.GetForYear(jy))
        {
            var g = Shared.PersianDate.ToGregorian(jy, m, d);
            if (g == DateTime.MinValue) continue;
            if (db.HrMainHolidays.Any(h => h.HolidayDate.Date == g.Date)) continue;
            db.HrMainHolidays.Add(new HrMainHoliday
            {
                HolidayDate = g, Name = name, IsOfficial = true,
                CreatedByName = "سیستم (کاتالوگ تعطیلات)", CreatedAt = DateTime.Now,
            });
            added++;
        }
        if (added > 0) db.SaveChanges();
        return added;
    }

}
