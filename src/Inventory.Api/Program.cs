using System.Text;
using System.Text.Json;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Infrastructure;
using Inventory.Api.Services;
using Inventory.Api.Services.Ai;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
Console.InputEncoding = System.Text.Encoding.UTF8;
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.Error.WriteLine("[STARTUP ERROR] برنامه متوقف شد؛ گزارش خطا در startup-log.txt ذخیره می‌شود.");
    Console.Error.WriteLine(e.ExceptionObject);
};
// Update both contexts using this installation's configuration, without starting the web server.
var updateDatabaseOnly = args.Any(a => a.Equals("--update-database", StringComparison.OrdinalIgnoreCase));
args = args.Where(a => !a.Equals("--update-database", StringComparison.OrdinalIgnoreCase)).ToArray();
var builder = WebApplication.CreateBuilder(args);
var schemaVerbose = string.Equals(Environment.GetEnvironmentVariable("DB_SCHEMA_VERBOSE"), "true", StringComparison.OrdinalIgnoreCase);
var schemaInitializing = true;
if (!schemaVerbose)
{
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", level => !schemaInitializing && level >= LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Model.Validation", LogLevel.Error);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Query", LogLevel.Error);
}


// ================== تشخیص میزبانی پشت IIS ==================
// ماژول ASP.NET Core در IIS (ANCM) این متغیرها را برای پروسهٔ برنامه تنظیم می‌کند. پشت IIS آدرس/پورت شنود را
// خود IIS تعیین می‌کند (در حالت OutOfProcess پورت تصادفی ASPNETCORE_PORT روی 127.0.0.1)؛ اگر برنامه خودش
// app.Run("http://0.0.0.0:5100") کند، IIS به پروسه وصل نمی‌شود و همهٔ درخواست‌ها (از جمله آپلود مدارک) خطای 502 می‌گیرند.
var runningUnderIis =
    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH")) ||
    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_POOL_ID")) ||
    (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_PORT")) &&
     !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_TOKEN")));
if (runningUnderIis)
    Console.WriteLine("[IIS] برنامه پشت IIS اجرا می‌شود؛ آدرس، پورت و HTTPS توسط bindingهای سایت IIS تعیین می‌شوند.");

builder.Services.AddHttpContextAccessor();
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .SetApplicationName("Inventory.MoadianSecrets.v1")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

// ================== HTTPS داخلی (لازم برای اعلان سیستمی مرورگر) ==================
// اعلان سیستمی و Service Worker فقط در «زمینهٔ امن» فعال می‌شوند؛ روی http://آی‌پی شبکه این امکان وجود ندارد.
// این بخش یک گواهی داخلی می‌سازد/بارگذاری می‌کند و در صورت فعال بودن، شنوندهٔ HTTPS را هم بالا می‌آورد
// (پیش‌فرض: پورت 5443 — قابل تغییر با Https:Port در appsettings یا متغیر محیطی HTTPS_PORT).
// JWT این سیستم همهٔ مجوزها را داخل خودش دارد (≈ ۱۰KB برای مدیر)؛ لینک‌های دانلود قدیمی که توکن را
// در query می‌برند با سقف پیش‌فرض Kestrel (۸KB) خطای 414 می‌گرفتند (ایمیل، نامه‌ها، آرشیو، SignalR).
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestLineSize = 64 * 1024;
    options.Limits.MaxRequestHeadersTotalSize = 128 * 1024;
    // بدون محدودیت سراسری حجم بدنه (مثل قبل). قبلاً فقط داخل بلوک HTTPS تنظیم می‌شد؛ پشت IIS (OutOfProcess،
    // Kestrel پشت ANCM) آن بلوک اجرا نمی‌شود و سقف پیش‌فرض ۳۰ مگابایت Kestrel آپلودها را می‌برید.
    // سقف هر endpoint همچنان با [RequestSizeLimit]/[DisableRequestSizeLimit] خودش اعمال می‌شود.
    options.Limits.MaxRequestBodySize = null;
});
var httpsCerts = Inventory.Api.Services.HttpsCertificates.Prepare(builder.Environment, builder.Configuration,
    skipPreparation: updateDatabaseOnly || EF.IsDesignTime);
foreach (var message in httpsCerts.Messages) Console.WriteLine($"[HTTPS] {message}");
// ================== تعیین آدرس/پورت HTTP از تنظیمات اجرا ==================
// آدرس از ASPNETCORE_URLS (مثل http://192.168.30.104:8181) یا سوئیچ --urls خوانده می‌شود.
// ⚠️ قبلاً پورتِ ثابت 5100 به Kestrel داده می‌شد (فقط متغیر PORT تأثیر داشت) و مقدار
// ASPNETCORE_URLS=http://192.168.30.104:8181 که start.bat/run-server.bat ست می‌کنند نادیده می‌ماند؛
// نتیجه: مرورگر روی آدرس/پورت معرفی‌شده هیچ سرویسی پیدا نمی‌کرد و صفحهٔ ورود «ارتباط با سرور برقرار نشد» می‌داد.
static (string Bind, int Port, string Display) ResolveHttpEndpoint()
{
    string? urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
                ?? Environment.GetEnvironmentVariable("DOTNET_URLS");
    var commandLine = Environment.GetCommandLineArgs();
    for (var i = 0; i < commandLine.Length; i++)
    {
        if (commandLine[i].StartsWith("--urls=", StringComparison.OrdinalIgnoreCase))
            urls = commandLine[i]["--urls=".Length..];
        else if (string.Equals(commandLine[i], "--urls", StringComparison.OrdinalIgnoreCase) && i + 1 < commandLine.Length)
            urls = commandLine[i + 1];
    }

    if (!string.IsNullOrWhiteSpace(urls))
    {
        foreach (var raw in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) continue;
            if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)) continue;
            var host = uri.Host.Trim('[', ']');
            if (host is "" or "*" or "+" or "0.0.0.0" or "any")
                return ("0.0.0.0", uri.Port, $"http://0.0.0.0:{uri.Port}");
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return ("127.0.0.1", uri.Port, $"http://localhost:{uri.Port}");
            if (System.Net.IPAddress.TryParse(host, out var ip))
                // بایندینگ روی همهٔ کارت‌های شبکه انجام می‌شود (اگر کارتی با آن آی‌پی بالا نیامده باشد،
                // بایندینگ دقیق به همان آی‌پی باعث خطای راه‌اندازی سرور می‌شود)؛ آدرس نمایشی = همان آدرس تنظیم‌شده.
                return ("0.0.0.0", uri.Port, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                    ? $"http://[{ip}]:{uri.Port}" : $"http://{ip}:{uri.Port}");
            // نام میزبان (مثل نام سرور در شبکه): روی همهٔ کارت‌های شبکه گوش می‌دهد تا با نام و آی‌پی هر دو در دسترس باشیم
            return ("0.0.0.0", uri.Port, $"http://0.0.0.0:{uri.Port}");
        }
    }

    var portEnv = Environment.GetEnvironmentVariable("PORT");
    var fallbackPort = int.TryParse(portEnv, out var parsedPort) && parsedPort is > 0 and < 65536 ? parsedPort : 5100;
    return ("0.0.0.0", fallbackPort, $"http://0.0.0.0:{fallbackPort}");
}

var httpEndpoint = ResolveHttpEndpoint();
Console.WriteLine($"[HTTP] آدرس سرویس‌دهی: {httpEndpoint.Display}");

if (!runningUnderIis && httpsCerts.Enabled && httpsCerts.Certificate is not null)
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        // بدون محدودیت حجم بدنهٔ درخواست — آپلود/پیوست فایل با هر حجمی مجاز است
        options.Limits.MaxRequestBodySize = null;
        // HTTP روی همان پورتی که ASPNETCORE_URLS تعیین کرده است (پیش‌فرض قبلی: 5100)
        options.ListenAnyIP(httpEndpoint.Port);
        // HTTPS: پورت اصلی (پیش‌فرض 5443) + پورت‌های اضافه (Https:ExtraPorts، مثلاً 443).
        // گواهی بر اساس نام درخواستی انتخاب می‌شود: دامنهٔ واقعی ← گواهی عمومی (Let's Encrypt)، آی‌پی/نام داخلی ← گواهی داخلی.
        foreach (var httpsPort in httpsCerts.AllPorts)
            options.ListenAnyIP(httpsPort, listen => listen.UseHttps(httpsCerts.CreateTlsOptions()));
    });
}

// ================== تنظیمات دیتابیس ==================
// پیش‌فرض: SQL Server — برای توسعه/تست می‌توان Provider را روی Sqlite گذاشت.
var provider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var connectionString = builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "رشته اتصال دیتابیس پیدا نشد. لطفاً مقدار ConnectionStrings:Default را در appsettings.json تنظیم کنید.");

// لاگ راه‌اندازی (رمز عبور پوشیده می‌شود) برای اشکال‌زدایی اتصال
Console.WriteLine($"[DB] Provider = {provider}");
Console.WriteLine($"[DB] ConnectionString = {Mask(connectionString)}");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        options.UseSqlite(connectionString);
    else
        options.UseSqlServer(connectionString);
});

// RADIS-HR از همان Connection String و همان دیتابیس استفاده می‌کند، ولی DbContext و
// Migration History مستقلش را حفظ می‌کند تا موجودیت‌ها و منطق بک‌اند اصلی تغییر نکنند.
builder.Services.AddRadisHrModule(builder.Configuration, provider, connectionString);

// ثبت سرویس‌ها با اینترفیس (اصل وارونگی وابستگی — DIP)
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<Inventory.Api.Services.Reports.IOperationsReportsService, Inventory.Api.Services.Reports.OperationsReportsService>(); // گزارش‌های بخش عملیات
builder.Services.AddScoped<Inventory.Api.Services.Catalog.IGoodsIssueService, Inventory.Api.Services.Catalog.GoodsIssueService>(); // حواله تحویل کالا (بدون قیمت)
builder.Services.AddScoped<Inventory.Api.Services.SalesVisit.IVisOrderService, Inventory.Api.Services.SalesVisit.VisOrderService>(); // ماژول ویزیتوری (پیش‌سند فروش)
builder.Services.AddScoped<Inventory.Api.Services.Cartable.ICartableSource, Inventory.Api.Services.Cartable.SalesVisitCartableSource>(); // منبع کارتابل: سفارش ویزیتور
builder.Services.AddScoped<Inventory.Api.Services.Cartable.ICartableService, Inventory.Api.Services.Cartable.CartableService>(); // کارتابل یکپارچه
builder.Services.AddScoped<IWarehousingService, WarehousingService>(); // ماژول انبارداری
builder.Services.AddScoped<Inventory.Api.Services.Accounting.IAccountingService, Inventory.Api.Services.Accounting.AccountingService>(); // ماژول حسابداری
builder.Services.AddScoped<Inventory.Api.Services.Accounting.IAnalyticalDimensionService, Inventory.Api.Services.Accounting.AnalyticalDimensionService>(); // ابعاد تحلیلی (مرکز هزینه/شعبه)
builder.Services.AddScoped<Inventory.Api.Services.Accounting.IFixedAssetService, Inventory.Api.Services.Accounting.FixedAssetService>(); // دارایی ثابت و استهلاک
builder.Services.AddScoped<Inventory.Api.Services.Accounting.IBudgetService, Inventory.Api.Services.Accounting.BudgetService>(); // بودجه و کنترل بودجه
builder.Services.AddScoped<Inventory.Api.Services.Invoicing.IInvoicingService, Inventory.Api.Services.Invoicing.InvoicingService>(); // ماژول فاکتور
// سامانه مودیان مبتنی بر گواهی امضای الکترونیکی (SDK 2.0.29)
builder.Services.AddSingleton<Inventory.Api.Services.Moadian.MoadianSecretVault>();
builder.Services.AddScoped<Inventory.Api.Services.Moadian.IMoadianSdkGateway, Inventory.Api.Services.Moadian.MoadianSdkGateway>();
builder.Services.AddScoped<Inventory.Api.Services.Moadian.IMoadianCertService, Inventory.Api.Services.Moadian.MoadianCertService>();
builder.Services.AddScoped<Inventory.Api.Services.Treasury.ITreasuryService, Inventory.Api.Services.Treasury.TreasuryService>(); // ماژول خزانه‌داری
builder.Services.AddScoped<Inventory.Api.Services.Stocktaking.IStocktakingService, Inventory.Api.Services.Stocktaking.StocktakingService>(); // ماژول انبارگردانی و بارکد
builder.Services.AddScoped<Inventory.Api.Services.Export.IExportService, Inventory.Api.Services.Export.ExportService>(); // خروجی PDF و Excel
builder.Services.AddScoped<Inventory.Api.Services.Dashboards.IWidgetDataService, Inventory.Api.Services.Dashboards.WidgetDataService>(); // داشبورد شخصی کاربر — تولید دادهٔ ویجت‌ها
builder.Services.AddScoped<Inventory.Api.Services.Core.IEffectivePermissions, Inventory.Api.Services.Core.EffectivePermissions>(); // دسترسی‌های مؤثر کاربر (مشترک بین داشبورد و گزارش‌ساز)
// گزارش‌ساز حرفه‌ای — کاتالوگ، اجرا و کنترل دسترسی
builder.Services.AddScoped<Inventory.Api.Services.ReportStudio.IRsRowSource, Inventory.Api.Services.ReportStudio.RsRowSource>();
builder.Services.AddScoped<Inventory.Api.Services.ReportStudio.IRsExecutor, Inventory.Api.Services.ReportStudio.RsExecutor>();
builder.Services.AddScoped<Inventory.Api.Services.ReportStudio.IRsAccessService, Inventory.Api.Services.ReportStudio.RsAccessService>();
builder.Services.AddScoped<Inventory.Api.Services.ReportStudio.IRsRowSecurity, Inventory.Api.Services.ReportStudio.RsRowSecurity>();
builder.Services.AddScoped<IAuthService, AuthService>();
// گاوصندوق رمزهای تجهیزات — رمزنگاری AES-256-GCM با کلید اصلی خارج از دیتابیس
builder.Services.AddSingleton<Inventory.Api.Services.ItAssets.CredentialVaultCrypto>();
// Redfish/iLO: فقط HTTPS، بدون redirect (از انتقال ناخواستهٔ اعتبارنامه به مقصد دیگر جلوگیری می‌کند).
builder.Services.AddHttpClient("HpeRedfish", client => client.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<Inventory.Api.Services.ItAssets.HpeRedfishClient>();
builder.Services.AddScoped<Inventory.Api.Services.ItAssets.ServerAlertNotificationService>();
// پایش خودکار iLO/Redfish — فقط برای سرورهایی که MonitoringEnabled دارند.
builder.Services.AddHostedService<Inventory.Api.Services.ItAssets.ServerMonitoringWorker>();
// پایش SNMP ماشین‌های اداری (پرینتر/MFP/فتوکپی) — فقط‌خواندنی
builder.Services.AddScoped<Inventory.Api.Services.Office.OfficeMachineMonitor>();
builder.Services.AddSingleton<Inventory.Api.Services.Office.OfficeMachineMonitorWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Inventory.Api.Services.Office.OfficeMachineMonitorWorker>());
// مدیریت آی‌پی — اسکنر رنج‌ها (Singleton) + اسکن خودکار زمان‌بندی‌شده (HostedService)
builder.Services.AddSingleton<Inventory.Api.Services.ItAssets.IpamScanner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Inventory.Api.Services.ItAssets.IpamScanner>());
builder.Services.AddScoped<AttendanceRecalcService>();
builder.Services.AddScoped<HrTimeService>(); // ماژول زمان‌بندی
builder.Services.AddScoped<HrPayService>(); // ماژول حقوق
builder.Services.AddScoped<HrPerfService>(); // ارزیابی عملکرد
builder.Services.AddScoped<ForoughAryaHrUsers>(); // کاربران مجاز منابع انسانی فروغ آریا (فعال + دارای دسترسی)
builder.Services.AddScoped<FaHrPersonnelService>(); // پرسنل و چارت اختصاصی فروغ آریا (FaHrPersonnel)
builder.Services.AddScoped<AttendanceSecurityService>();
builder.Services.AddScoped<IRepairService, RepairService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();

// ---------- اتوماسیون اداری — نامه داخلی (کارتابل، ارجاع، پیش‌نویس، گروه‌های گیرندگان) ----------
builder.Services.AddScoped<ILetterGroupService, LetterGroupService>();
builder.Services.AddScoped<IInnerLetterService, InnerLetterService>();
builder.Services.AddScoped<IIncomingLetterService, IncomingLetterService>();
builder.Services.AddScoped<IErjaService, ErjaService>();
builder.Services.AddScoped<IPishnevisService, PishnevisService>();
// ساختار شماره اندیکاتور (LetterStrature) و بایگانی درختی نامه‌ها
builder.Services.AddScoped<ILetterStratureService, LetterStratureService>();
builder.Services.AddScoped<IArchiveService, ArchiveService>();
// سازمان‌ها/سمت‌ها — مبنای جزء «واحد» در شماره نامه (Organization.NameUniq)
builder.Services.AddScoped<IOrganizationServices, OrganizationServices>();

// ---------- اتوماسیون اداری — نامه صادره (فاز دوم) — پوشه‌بندی تمیز ----------
builder.Services.AddScoped<Inventory.Api.Services.Office.Outgoing.IOutgoingPishnevisService, Inventory.Api.Services.Office.Outgoing.OutgoingPishnevisService>();
builder.Services.AddScoped<Inventory.Api.Services.Office.Outgoing.IOutgoingLetterService, Inventory.Api.Services.Office.Outgoing.OutgoingLetterService>();
builder.Services.AddScoped<Inventory.Api.Services.Office.Outgoing.IOutgoingLetterPrintService, Inventory.Api.Services.Office.Outgoing.OutgoingLetterPrintService>();
builder.Services.AddScoped<Inventory.Api.Services.Office.IInnerLetterPrintService, Inventory.Api.Services.Office.InnerLetterPrintService>();
// ایمیل سازمانی (پست الکترونیک) — SMTP/IMAP با MailKit + حساب‌های دبیرخانه
builder.Services.AddScoped<Inventory.Api.Services.Office.Email.IEmailService, Inventory.Api.Services.Office.Email.EmailService>();

// ---------- فرم‌های متفرقه — صورتجلسه (بندها، تصمیمات، امضا، چاپ با سربرگ) ----------
builder.Services.AddScoped<Inventory.Api.Services.Office.IMeetingMinutesService, Inventory.Api.Services.Office.MeetingMinutesService>();
builder.Services.AddScoped<Inventory.Api.Services.Office.IMeetingMinutesPrintService, Inventory.Api.Services.Office.MeetingMinutesPrintService>();
builder.Services.AddScoped<Inventory.Api.Services.DevTeam.IDevTeamService, Inventory.Api.Services.DevTeam.DevTeamService>(); // میز کار توسعه

// ---------- آرشیو اسناد و مدارک (پوشه، دسترسی، ورژن، گردش تایید) ----------
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.IDocAccessService, Inventory.Api.Services.DocArchive.DocAccessService>();
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.IDocumentService, Inventory.Api.Services.DocArchive.DocumentService>();
builder.Services.AddScoped<Inventory.Api.Services.HrCore.IHrCoreService, Inventory.Api.Services.HrCore.HrCoreService>(); // HrCore
builder.Services.AddScoped<Inventory.Api.Services.HrReports.IHrReportService, Inventory.Api.Services.HrReports.HrReportService>();
builder.Services.AddScoped<Inventory.Api.Services.HrTalent.IHrTalentService, Inventory.Api.Services.HrTalent.HrTalentService>(); // HrTalent
builder.Services.AddScoped<Inventory.Api.Services.HrCore.IHrRecruitmentService, Inventory.Api.Services.HrCore.HrRecruitmentService>(); // HrCore-Recruitment
builder.Services.AddScoped<Inventory.Api.Services.HrMain.IHrMainService, Inventory.Api.Services.HrMain.HrMainService>(); // HrMain — منابع انسانی اصلی
builder.Services.AddScoped<Inventory.Api.Services.FaAtt.IFaAttService, Inventory.Api.Services.FaAtt.FaAttService>(); // FaAtt — حضور و غیاب فروغ آریا
builder.Services.AddScoped<Inventory.Api.Services.FaCom.IFaComService, Inventory.Api.Services.FaCom.FaComService>(); // FaCom — ارتباطات داخلی
builder.Services.AddScoped<Inventory.Api.Services.FaCom.ISmsSender, Inventory.Api.Services.FaCom.ConfigSmsSender>(); // پیامک وب‌هوکی
builder.Services.Configure<Inventory.Api.Services.ItAssets.KavenegarSmsOptions>(builder.Configuration.GetSection("KavenegarSms"));
builder.Services.Configure<Inventory.Api.Services.Core.UserProfileOtpOptions>(builder.Configuration.GetSection("ProfileOtp"));
builder.Services.AddScoped<Inventory.Api.Services.Core.IUserContactOtpDelivery, Inventory.Api.Services.Core.UserContactOtpDelivery>();
builder.Services.AddScoped<Inventory.Api.Services.Core.IUserProfileService, Inventory.Api.Services.Core.UserProfileService>();
builder.Services.AddScoped<Inventory.Api.Services.ItAssets.IWorkOrderSmsService, Inventory.Api.Services.ItAssets.WorkOrderSmsService>();
builder.Services.AddScoped<Inventory.Api.Services.FaLms.IFaLmsService, Inventory.Api.Services.FaLms.FaLmsService>(); // FaLms — آموزش و توسعه
builder.Services.AddScoped<Inventory.Api.Services.FaPay.IFaPayService, Inventory.Api.Services.FaPay.FaPayService>(); // FaPay — حقوق و دستمزد
builder.Services.AddScoped<Inventory.Api.Services.FaPay.IFaPayExtraService, Inventory.Api.Services.FaPay.FaPayExtraService>(); // FaPay — افزونه‌های §۲

// اعطای موقت «تایید مجدد رمز» برای فایل‌های مدارک محرمانه (در حافظه — ۱۵ دقیقه)
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Inventory.Api.Services.DocArchive.IDocDownloadConfirmService, Inventory.Api.Services.DocArchive.DocDownloadConfirmService>();
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.IDocFolderZipService, Inventory.Api.Services.DocArchive.DocFolderZipService>();
// ورود انبوه آرشیو (بخش DocArchiveImport در appsettings). مسیر ریشه فقط از پیکربندی سرور خوانده می‌شود.
builder.Services.Configure<Inventory.Api.Services.DocArchive.DocImportOptions>(
    builder.Configuration.GetSection(Inventory.Api.Services.DocArchive.DocImportOptions.SectionName));
// ورود انبوه همیشه به‌طور پیش‌فرض روشن است. مقدار Enabled در فایل‌های appsettings (حتی false)
// نادیده گرفته می‌شود تا یک فایل پیکربندی قدیمی روی سرور نتواند قابلیت را بی‌صدا خاموش کند.
// تنها راه خاموش کردن: متغیر محیطی DocArchiveImport__Enabled=false
builder.Services.PostConfigure<Inventory.Api.Services.DocArchive.DocImportOptions>(o =>
{
    var kill = Environment.GetEnvironmentVariable("DocArchiveImport__Enabled");
    o.Enabled = !bool.TryParse(kill, out var v) || v;
});
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.IDocImportService, Inventory.Api.Services.DocArchive.DocImportService>();
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.IDocChatService, Inventory.Api.Services.DocArchive.DocChatService>(); // گفتگو با سند
// سرویس OCR پایتون (اختیاری — بخش OcrService در appsettings). اگر BaseUrl خالی باشد موتور محلی استفاده می‌شود.
var ocrOptions = builder.Configuration.GetSection("OcrService")
    .Get<Inventory.Api.Services.DocArchive.OcrServiceOptions>()
    ?? new Inventory.Api.Services.DocArchive.OcrServiceOptions();
builder.Services.AddSingleton(ocrOptions);
builder.Services.AddSingleton<Inventory.Api.Services.DocArchive.IOcrServiceClient,
    Inventory.Api.Services.DocArchive.OcrServiceClient>();
builder.Services.AddSingleton<Inventory.Api.Services.DocArchive.IDocTextExtractorService, Inventory.Api.Services.DocArchive.DocTextExtractorService>();
builder.Services.AddSingleton<Inventory.Api.Services.DocArchive.IDocIndexService, Inventory.Api.Services.DocArchive.DocIndexService>();
// سرویس پس‌زمینه هشدار انقضای مدارک (روزانه)
builder.Services.AddHostedService<Inventory.Api.Services.DocArchive.DocIndexWorker>();
// متن‌گیری شبانهٔ فایل‌های ورود انبوه (خارج از ساعت کاری)
builder.Services.Configure<Inventory.Api.Services.DocArchive.DocImportOcrOptions>(
    builder.Configuration.GetSection(Inventory.Api.Services.DocArchive.DocImportOcrOptions.SectionName));
builder.Services.AddHostedService<Inventory.Api.Services.DocArchive.DocImportOcrWorker>();
builder.Services.AddHostedService<Inventory.Api.Services.DocArchive.DocRenewalWorker>();
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.DocRenewalService>();
builder.Services.AddSingleton<Inventory.Api.Services.DocArchive.DocExpiryWatcher>();
builder.Services.AddSingleton<Inventory.Api.Services.FaCom.FaComBirthdayWatcher>();
builder.Services.AddSingleton<Inventory.Api.Services.FaCom.FaComAnniversaryWatcher>();
builder.Services.AddSingleton<Inventory.Api.Services.HrCore.HrOpsDailyWatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Inventory.Api.Services.DocArchive.DocExpiryWatcher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<Inventory.Api.Services.FaCom.FaComBirthdayWatcher>()); // یادآوری تولد
builder.Services.AddHostedService(sp => sp.GetRequiredService<Inventory.Api.Services.FaCom.FaComAnniversaryWatcher>()); // یادآوری سالگرد همکاری
builder.Services.AddHostedService(sp => sp.GetRequiredService<Inventory.Api.Services.HrCore.HrOpsDailyWatcher>()); // هشدار انقضای قرارداد + دوره آزمایشی خودکار
builder.Services.AddSingleton<Inventory.Api.Services.FaCom.FaComPublishWatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Inventory.Api.Services.FaCom.FaComPublishWatcher>()); // انتشار زمان‌بندی‌شده اطلاعیه‌ها

// سرویس پس‌زمینه یادآور مهلت دستور کار (هر ۱۵ دقیقه — آستانه‌ها در appsettings قابل تنظیم)
builder.Services.AddSingleton<Inventory.Api.Services.ItAssets.WorkOrderReminderService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Inventory.Api.Services.ItAssets.WorkOrderReminderService>());

// ذخیره‌سازی فایل‌ها روی دیسک (uploads/ در روت API) + عکس کاربران
builder.Services.AddSingleton<FileStore>();
// کانال شرکت‌های راه‌دور: احراز هویت کلید API بین نصبِ شرکت و سرور مرکزی
builder.Services.AddScoped<Inventory.Api.Services.ItExternalAuthService>();
// نگهبان دسترسی پیوست‌ها (بر اساس ماژول صاحب پیوست)
builder.Services.AddScoped<IAttachmentGuard, AttachmentGuard>();
// واترمارک سمت سرور (حک روی بایت تصویر/PDF) برای مدارکِ دارای فلگ واترمارک
builder.Services.AddSingleton<Inventory.Api.Services.Watermark.IServerWatermarkService,
                               Inventory.Api.Services.Watermark.ServerWatermarkService>();
// گزارش‌های خروجی آرشیو اسناد (نیازمند فیلتر دسترسی کاربر)
builder.Services.AddScoped<Inventory.Api.Services.Export.IDocArchiveExportService,
                           Inventory.Api.Services.Export.DocArchiveExportService>();
builder.Services.AddSingleton<UserPhotoService>();

// ================== پیوست‌های پروژه — رمزنگاری AES روی دیسک ==================
builder.Services.AddSingleton<IProjectFileProtection, ProjectFileProtection>();

// ================== احراز هویت JWT ==================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero,
            ValidateIssuerSigningKey = true,
            ValidIssuer = AuthService.JwtIssuer,
            ValidAudience = AuthService.JwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthService.JwtKey))
        };
        // دانلود پیوست با لینک مستقیم (تگ <a>) هدر Authorization ندارد؛
        // برای مسیرهای دانلود، توکن از query string خوانده می‌شود (الگوی استاندارد SignalR).
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var path = ctx.Request.Path.Value ?? "";

                // توکن کوتاه فایل‌های پیام‌رسان (ft): فقط روی مسیرهای download/preview چت.
                // JWT کامل ≈ ۱۰KB است و در URL باعث 414 می‌شد؛ این توکن ≈ ۷۰ نویسه است.
                var fileToken = ctx.Request.Query["ft"].ToString();
                if (!string.IsNullOrEmpty(fileToken) &&
                    path.StartsWith("/api/chat/", StringComparison.OrdinalIgnoreCase) &&
                    (path.EndsWith("/download", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith("/preview", StringComparison.OrdinalIgnoreCase)) &&
                    Inventory.Api.Services.Chat.ChatFileToken.TryValidate(fileToken, out var fileUserId))
                {
                    var identity = new System.Security.Claims.ClaimsIdentity(new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, fileUserId.ToString())
                    }, JwtBearerDefaults.AuthenticationScheme);
                    ctx.Principal = new System.Security.Claims.ClaimsPrincipal(identity);
                    ctx.Success();
                    return Task.CompletedTask;
                }

                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) &&
                    (ctx.Request.Path.StartsWithSegments("/hubs/chat") ||
                     ctx.Request.Path.StartsWithSegments("/hubs/notify") ||
                     path.Contains("/download", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains("/preview", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains("/export-zip", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains("/export", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains("/template", StringComparison.OrdinalIgnoreCase)))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    // دسترسی به APIهای منابع انسانی بن‌سازه:
    // - مدیر سیستم، یا
    // - دارای مجوز کل ماژول (RadisHr.Access)، یا
    // - دارای مجوز «حداقل یک بخش» (RadisHr.Dashboard / RadisHr.Employees / ...)
    //   — کاربر بخش‌محور فقط باید به دادهٔ همان بخش دسترسی داشته باشد؛
    //     عملیات مدیریتیِ حساس خودش در کنترلرها با نقش‌های ماژول کنترل می‌شود.
    options.AddPolicy("RadisHrAccess", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx => ctx.User.IsInRole("Admin")
                  || ctx.User.HasClaim("permission", "RadisHr.Access")
                  || ctx.User.Claims.Any(c => c.Type == "permission"
                                              && c.Value.StartsWith("RadisHr.", StringComparison.OrdinalIgnoreCase))));

    // تمام endpointهای API به‌صورت پیش‌فرض JWT می‌خواهند؛ مسیرهای عمومی باید
    // صراحتاً با [AllowAnonymous] علامت‌گذاری شده باشند (Login، درخواست عمومی و ...).
    // ⚠️ FallbackPolicy حذف شد: این سیاست علاوه بر endpointها، درخواست‌های «بدون endpoint»
    // (فایل‌های استاتیک کلاینت مثل /index.html و /_framework/*.wasm در استقرار تک‌سرورهٔ
    // پابلیش‌شده) را هم با 401 رد می‌کرد و برنامه بعد از انتشار اصلاً باز نمی‌شد.
    // امنیت پیش‌فرض APIها با DefaultAuthorizeConvention (همهٔ کنترلرها [Authorize]) حفظ می‌شود؛
    // مسیرهای عمومی همچنان با [AllowAnonymous] باز هستند و هاب‌های SignalR هم صریحاً [Authorize] شدند.
});

// CORS برای کلاینت Blazor WASM (در محیط توسعه)
builder.Services.AddCors(options =>
    options.AddPolicy("wasm", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
        .WithExposedHeaders("Content-Disposition", "Content-Length", "Accept-Ranges", "Content-Range")));

// بدون محدودیت آپلود/پیوست فایل در سراسر برنامه:
//  • FormOptions: سقف حجم چندبخشی (multipart) و طول مقادیر فرم برداشته شد
//  • Kestrel: MaxRequestBodySize بالاتر روی null (نامحدود) تنظیم شد
//  • IIS (در صورت میزبانی پشت IIS): سقف بدنه نامحدود
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = long.MaxValue;
    o.ValueLengthLimit = int.MaxValue;
    o.MultipartHeadersLengthLimit = int.MaxValue;
    o.MultipartBoundaryLengthLimit = int.MaxValue;
    o.KeyLengthLimit = int.MaxValue;
    o.ValueCountLimit = int.MaxValue;
});
builder.Services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(o => o.MaxRequestBodySize = null);

// کنترلرها + فیلتر سراسری خطا + JSON با نام‌گذاری camelCase
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ApiExceptionFilter>();
        // لاگ عملیات: ثبت خودکار هر POST/PUT/PATCH/DELETE در همه‌ی بخش‌ها
        options.Filters.Add<AuditLogFilter>();
        // میزبانی ماژول RADIS-HR: پیشوند مسیر radis-hr و سیاست دسترسی RadisHrAccess
        options.Conventions.Add(new RadisHrControllerConvention());
        // امنیت پیش‌فرض: همهٔ کنترلرها [Authorize] — جایگزین FallbackPolicy حذف‌شده
        options.Conventions.Add(new Inventory.Api.Infrastructure.DefaultAuthorizeConvention());
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        // تنظیمات اصلی RADIS-HR برای پاسخ‌های دارای navigation property.
        o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// ================== Swagger — مستندات و تست تعاملی API (/swagger) ==================
// ================== SignalR — داشبورد بلادرنگ ==================
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// ================== فشرده‌سازی پاسخ‌ها (Brotli/Gzip) ==================
// پاسخ‌های JSON نامه‌ها — به‌ویژه متن نامه‌هایی که تصویر درون‌متن دارند — چند
// مگابایت می‌شوند و بدون فشرده‌سازی روی شبکهٔ داخلی هم کند منتقل می‌شدند.
// لیست پیش‌فرض MIME شامل application/json و text/* است و فایل‌های باینری
// (octet-stream/zip/wasm) را فشرده نمی‌کند؛ واریانت‌های ازپیش‌فشردهٔ
// Blazor هم Content-Encoding دارند و میدل‌ور دوباره فشرده‌شان نمی‌کند.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.AddSingleton<DashboardBroadcaster>();
builder.Services.AddScoped<Inventory.Api.Hubs.INotifyService, Inventory.Api.Hubs.NotifyService>();
builder.Services.AddScoped<Inventory.Api.Hubs.IChatRealtimeNotifier, Inventory.Api.Hubs.ChatRealtimeNotifier>();
builder.Services.AddScoped<Inventory.Api.Services.Chat.IChatService, Inventory.Api.Services.Chat.ChatService>();
builder.Services.AddScoped<Inventory.Api.Services.Chat.ChatAttachmentService>();
builder.Services.AddSingleton<PushSettings>();
builder.Services.AddSingleton(httpsCerts); // وضعیت/گواهی HTTPS داخلی برای کنترلر تنظیمات
builder.Services.AddSingleton<PushKeyStore>(); // کلیدهای اعلان ساخته‌شده از داخل «تنظیمات» (App_Data/push-vapid.json)
builder.Services.AddScoped<IPushService, PushService>();
builder.Services.AddHttpClient("WebPush").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IPushTransport, WebPushTransport>();
builder.Services.AddHostedService<PushDeliveryWorker>();
builder.Services.AddScoped<IMessengerService, MessengerService>();
builder.Services.AddSingleton<IMessengerLinkCodes, MessengerLinkCodes>();
builder.Services.AddHostedService<BaleBotWorker>(); // خواندن خودکار پیام‌های ربات بله (/start و اشتراک شماره)
builder.Services.AddHttpClient("messenger", c => c.Timeout = TimeSpan.FromSeconds(10));

// ================== هوش مصنوعی فروغ آریا (مدل لوکال — Ollama) ==================
// تایم‌اوت هر درخواست داخل خود کلاینت مدیریت می‌شود (AiOptions.TimeoutSeconds).
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddHttpClient("ai", c => c.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddScoped<IAiChatClient, OpenAiCompatibleChatClient>();
builder.Services.AddScoped<IAiEmbeddingClient, OpenAiCompatibleEmbeddingClient>();
builder.Services.AddScoped<IAiTranscriptionClient, OpenAiCompatibleTranscriptionClient>(); // تبدیل ویس پیام‌رسان به متن
builder.Services.AddScoped<AiConversationService>();
builder.Services.AddScoped<AiKnowledgeService>();
builder.Services.AddScoped<AiToolRegistry>();
builder.Services.AddScoped<AiFallbackRouter>();
builder.Services.AddScoped<IAiAgentService, AiAgentService>();
builder.Services.AddScoped<ILetterAiService, LetterAiService>();
builder.Services.AddScoped<AiBriefingService>();
builder.Services.AddScoped<AiDigestService>();
builder.Services.AddScoped<AiReminderService>();
builder.Services.AddScoped<AiScheduleService>();
builder.Services.AddScoped<AiSearchService>();
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.DocArchiveSemanticService>();
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.DocArchiveAutoSuggestService>();
builder.Services.AddScoped<AiAlertRuleService>();
builder.Services.AddScoped<AiFeedbackService>();
builder.Services.AddScoped<AiAuditService>();
builder.Services.AddScoped<AiAlertsService>();
builder.Services.AddScoped<AiActionService>();
// ابزارهای دستیار
builder.Services.AddScoped<IAiTool, GuideSearchTool>();
builder.Services.AddScoped<IAiTool, MyLeaveBalanceTool>();
builder.Services.AddScoped<IAiTool, MyLeavesTool>();
builder.Services.AddScoped<IAiTool, MyAttendanceTool>();
builder.Services.AddScoped<IAiTool, MyPayslipTool>();
builder.Services.AddScoped<IAiTool, MyLoansTool>();
builder.Services.AddScoped<IAiTool, MyMissionsTool>();
builder.Services.AddScoped<IAiTool, MyLettersStatsTool>();
builder.Services.AddScoped<IAiTool, MyLettersInboxTool>();
builder.Services.AddScoped<IAiTool, LetterDetailTool>();
builder.Services.AddScoped<IAiTool, UsersLookupTool>();
builder.Services.AddScoped<IAiTool, RequestLeaveTool>();
builder.Services.AddScoped<IAiTool, ClockTool>();
builder.Services.AddScoped<IAiTool, ConfirmActionTool>();
builder.Services.AddScoped<IAiTool, CancelActionTool>();
builder.Services.AddScoped<IAiTool, PendingActionsTool>();
builder.Services.AddScoped<IAiTool, PendingApprovalsTool>();
builder.Services.AddScoped<IAiTool, MyReferralsPendingTool>();
builder.Services.AddScoped<IAiTool, RequestMissionTool>();
builder.Services.AddScoped<IAiTool, DecideLeaveTool>();
builder.Services.AddScoped<IAiTool, AnswerReferralTool>();
builder.Services.AddScoped<IAiTool, CreateTicketTool>();
builder.Services.AddScoped<IAiTool, ReportWorkTool>();
builder.Services.AddScoped<IAiTool, CreateLetterDraftTool>();
builder.Services.AddScoped<IAiTool, SalesSummaryTool>();
builder.Services.AddScoped<IAiTool, RecentInvoicesTool>();
builder.Services.AddScoped<IAiTool, StockStatusTool>();
builder.Services.AddScoped<IAiTool, ChequesDueTool>();
builder.Services.AddScoped<IAiTool, TopDebtorsTool>();
builder.Services.AddScoped<IAiTool, CashStatusTool>();
builder.Services.AddScoped<IAiTool, BuildReportTool>();
builder.Services.AddScoped<IAiTool, DataCatalogTool>();
builder.Services.AddScoped<IAiTool, ExploreDataTool>();
builder.Services.AddScoped<IAiTool, MyAlertsTool>();
builder.Services.AddScoped<IAiTool, ReferLetterTool>();
builder.Services.AddScoped<IAiTool, AnswerTicketTool>();
builder.Services.AddScoped<IAiTool, RegisterChequeTool>();
builder.Services.AddScoped<IAiTool, WeeklyDigestTool>();
builder.Services.AddScoped<IAiTool, SetReminderTool>();
builder.Services.AddScoped<IAiTool, MyRemindersTool>();
builder.Services.AddScoped<IAiTool, CancelReminderTool>();
builder.Services.AddScoped<IAiTool, DecideLeaveBulkTool>();
builder.Services.AddScoped<IAiTool, DraftMinutesTool>();
builder.Services.AddScoped<IAiTool, CreateMinutesTool>();
builder.Services.AddScoped<IAiTool, MyMinutesActionsTool>();
builder.Services.AddScoped<IAiTool, ScheduleReportTool>();
builder.Services.AddScoped<IAiTool, MySchedulesTool>();
builder.Services.AddScoped<IAiTool, CancelScheduleTool>();
builder.Services.AddScoped<IAiTool, SearchDocsTool>();
builder.Services.AddScoped<IAiTool, ReindexDocsTool>();
builder.Services.AddScoped<IAiTool, CreateAlertTool>();
builder.Services.AddScoped<IAiTool, MyAlertRulesTool>();
builder.Services.AddScoped<IAiTool, DeleteAlertTool>();
builder.Services.AddScoped<IAiTool, FeedbackStatsTool>();
builder.Services.AddScoped<IAiTool, AuditStatsTool>();
builder.Services.AddScoped<AiReportService>();
builder.Services.AddScoped<AiDataExplorer>();
// پاسخ‌گویی در پیام‌رسان داخلی
builder.Services.AddSingleton<AiReplyQueue>();
builder.Services.AddHostedService<AiChatReplyWorker>();
// گزارش صبحگاهی خودکار
builder.Services.AddHostedService<AiBriefingWorker>();
builder.Services.AddHostedService<AiDigestWorker>();
builder.Services.AddHostedService<AiReminderWorker>();
builder.Services.AddHostedService<AiScheduleWorker>();
builder.Services.AddHostedService<AiSearchWorker>();
builder.Services.AddHostedService<Inventory.Api.Services.DocArchive.DocArchiveSemanticWorker>();
builder.Services.AddHostedService<AiAlertRuleWorker>();
builder.Services.AddSingleton<HardwareMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HardwareMonitor>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "API برنامه جامع — فروغ آریا + RADIS-HR V019",
        Version = "v1",
        Description = "تست تعاملی همه‌ی سرویس‌ها؛ APIهای منابع انسانی جامع زیر /radis-hr/api قرار دارند."
    });
    // DTOهایی مثل LoginRequest در هر دو سامانه وجود دارند؛ نام کامل از برخورد Schema جلوگیری می‌کند.
    o.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace("+", "."));
});

var app = builder.Build();

if (updateDatabaseOnly)
{
    await using (app)
    {
        await DbInitializer.InitializeAsync(app);
        await app.InitializeRadisHrAsync(isDesignTime: false, failOnError: true);
        Console.WriteLine("[DB] به‌روزرسانی هسته و RADIS-HR پایان یافت؛ هشدارهای احتمالی اسکیما را در لاگ بررسی کنید.");
    }
    return;
}

// ================== محل ذخیرهٔ فایل مدارک آرشیو ==================
// همین ابتدای کار مسیر واقعی روی دیسک سرور و امکان نوشتن روی آن بررسی و لاگ می‌شود
// (هشدارها روی ویندوز در Event Viewer هم دیده می‌شوند)، نه وقتی کاربر اولین مدرک را می‌فرستد.
{
    var store = app.Services.GetRequiredService<FileStore>();
    var probe = store.ProbeDocArchiveWrite();
    Console.WriteLine($"[Storage] پوشهٔ فایل مدارک آرشیو: {probe.Path}");
    if (!probe.Ok)
        app.Logger.LogError("[Storage] نوشتن در پوشهٔ مدارک آرشیو ممکن نیست: {Path} — {Error}. روی IIS به هویت Application Pool (IIS AppPool\\<نام>) دسترسی Modify بدهید.",
            probe.Path, probe.Error);
    if (runningUnderIis && !store.HasExternalDocArchiveRoot)
        app.Logger.LogWarning("[Storage] مدارک آرشیو داخل پوشهٔ برنامه ({Path}) ذخیره می‌شوند. با هر انتشار جدید روی IIS این پوشه در خطر پاک شدن است؛ مقدار {Key} را در appsettings روی یک مسیر ثابت بیرون از پوشهٔ برنامه بگذارید (مثلاً D:\\InventoryData\\DocArchive).",
            probe.Path, FileStore.DocArchiveStorageRootKey);
}
// کلیدهای اعلان گوشی/مرورگر (VAPID): اگر مدیر آن‌ها را از داخل «تنظیمات» ساخته باشد، همین‌جا زنده اعمال می‌شوند
// تا پس از ساخت کلید نیازی به ویرایش appsettings.json یا ری‌استارت سرویس نباشد.
app.Services.GetRequiredService<Inventory.Api.Services.PushKeyStore>()
    .Load(app.Services.GetRequiredService<Inventory.Api.Services.PushSettings>());

// ================== سرویس OCR پایتون: شناسایی خودکار ==================
// اگر OcrService:BaseUrl خالی باشد، سرویس محلی پیش‌فرض (http://127.0.0.1:8765) جستجو می‌شود
// تا کاربر بدون دست‌زدن به تنظیمات هم بتواند وصل شود. برای خاموش‌کردن: OcrService:AutoDetect=false
if (!ocrOptions.IsConfigured && ocrOptions.AutoDetect)
{
    try
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var probeResp = await probe.GetAsync("http://127.0.0.1:8765/health");
        if (probeResp.IsSuccessStatusCode)
        {
            var probeBody = await probeResp.Content.ReadAsStringAsync();
            if (probeBody.Contains("totall-ocr", StringComparison.OrdinalIgnoreCase))
            {
                ocrOptions.BaseUrl = "http://127.0.0.1:8765";
                app.Logger.LogInformation("[OcrService] سرویس OCR پایتون به‌صورت خودکار در http://127.0.0.1:8765 شناسایی شد.");
            }
        }
    }
    catch
    {
        // سرویس بالا نیست؛ پیام زیر در لاگ توضیح می‌دهد
    }
}
if (ocrOptions.IsConfigured)
    app.Logger.LogInformation("[OcrService] اتصال به سرویس OCR: {Base}", ocrOptions.BaseUrl);
else if (schemaVerbose)
    app.Logger.LogWarning("[OcrService] سرویس OCR پایتون تنظیم/شناسایی نشد — OCR فقط با موتور محلی (tesseract/poppler روی سرور) تلاش می‌شود.");

// ساخت دیتابیس و داده اولیه (نه در زمان ابزارهای EF)
if (!EF.IsDesignTime)
    await DbInitializer.InitializeAsync(app);

// اجرای Migration و Seed اصلی RADIS-HR روی همان دیتابیس برنامه.
await app.InitializeRadisHrAsync(EF.IsDesignTime);
schemaInitializing = false;

// سازماندهی پیوست‌ها در پوشه‌های اختصاصی هر پروژه (کد پروژه ← تصاویر/مستندات) — فقط فایل‌های قدیمیِ بدون‌پوشه
if (!EF.IsDesignTime)
{
    using var orgScope = app.Services.CreateScope();
    var orgDb = orgScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await app.Services.GetRequiredService<IProjectFileProtection>().OrganizeProjectFoldersAsync(orgDb);
}

// در همه‌ی محیط‌ها فعال است (حتی Production) تا روی هر سیستمی قابل تست باشد
app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "API انبار v1");
    o.DocumentTitle = "API برنامه انبار";
});

// ================== هدایت HTTP → HTTPS (اختیاری: Https:RedirectHttp=true) ==================
// فقط صفحه‌ها (GET/HEAD) هدایت می‌شوند؛ API و هاب‌ها دست نمی‌خورند تا نسخه‌های قدیمی/اپ‌ها نشکنند.
// /https-setup و /ca.crt همیشه روی HTTP می‌مانند تا دستگاهِ بدون گواهی بتواند گواهی را نصب کند.
// هدایت فقط وقتی انجام می‌شود که برای آن نام/آی‌پی واقعاً گواهی داریم (وگرنه کاربر پشت خطای گواهی گیر می‌کند).
// پشت IIS، HTTPS و هدایت آن با binding و URL Rewrite خود IIS انجام می‌شود (پورت 5443 داخلی آنجا گوش نمی‌دهد).
if (!runningUnderIis && httpsCerts.Enabled && httpsCerts.RedirectHttp)
{
    app.Use(async (ctx, next) =>
    {
        var req = ctx.Request;
        var path = req.Path.Value ?? "";
        var isPage = HttpMethods.IsGet(req.Method) || HttpMethods.IsHead(req.Method);
        var exempt = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith("/.well-known/", StringComparison.OrdinalIgnoreCase)
                     || path.Equals("/https-setup", StringComparison.OrdinalIgnoreCase)
                     || path.Equals("/ca.crt", StringComparison.OrdinalIgnoreCase);
        var host = req.Host.Host;
        if (!req.IsHttps && isPage && !exempt && !string.IsNullOrEmpty(host) && httpsCerts.HasCertificateFor(host))
        {
            var targetPort = httpsCerts.IsPublicHost(host) ? httpsCerts.PublicPort : httpsCerts.Port;
            var hostPart = host.Contains(':') ? $"[{host}]" : host; // IPv6
            var portPart = targetPort == 443 ? "" : $":{targetPort}";
            ctx.Response.Redirect($"https://{hostPart}{portPart}{req.PathBase}{req.Path}{req.QueryString}", permanent: false);
            return;
        }
        await next();
    });
}

// فشرده‌سازی باید پیش از هر چیزی که پاسخ را می‌نویسد ثبت شود.
app.UseResponseCompression();

app.UseCors("wasm");

// Source development has a separate Blazor dev server. Do not serve an old published login
// page from the API root; refreshes must keep the developer on the current client build.
// Production and IIS retain single-server hosting and are deliberately unaffected.
if (app.Environment.IsDevelopment() && !runningUnderIis)
    app.UseMiddleware<DevelopmentClientRedirect>();

app.UseAuthentication();

// ================== تعیین شرکت فعال (چند شرکتی) ==================
// هدر X-Company-Id از کمبوی بالای برنامه می‌آید؛ اعتبارسنجی دسترسی و ذخیره در HttpContext.Items.
// از این‌جا به بعد، AppDbContext همهٔ کوئری‌های دادهٔ فروشگاهی را به همان شرکت محدود می‌کند.
app.UseMiddleware<Inventory.Api.Infrastructure.ActiveCompanyMiddleware>();

app.UseAuthorization();

// فایل‌های قدیمی چت هم فقط از endpoint دارای کنترل عضویت دانلود می‌شوند.
// این گارد مستقل از وجود index.html و از استقرار تک/دو سروره است.
app.Use(async (ctx, next) =>
{
    if (Inventory.Api.Services.Chat.ChatAttachmentService.IsLegacyPublicPath(ctx.Request.Path.Value))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});


// جلوگیری از کش‌شدن نسخه‌ی قدیمی کلاینت: همه‌ی پاسخ‌های HTML بدون کش (به‌علاوه‌ی StaticFiles)
app.Use(async (ctx, next) =>
{
    await next();
    if (!ctx.Response.HasStarted
        && ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true
        && !ctx.Response.Headers.CacheControl.Any())
    {
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    }
});

app.MapControllers();
app.MapGet("/radis-hr", () => Results.Redirect("/bon-hr"));

// ---- بررسی سلامت سرور (عمومی/بدون احراز هویت) ----
// کاربرد: تست سریع «آیا مرورگر به API وصل می‌شود؟» — http://192.168.30.104:8181/api/health
// کلاینت Blazor هم در زمان بالا آمدن همین مسیر را آزمایش می‌کند و آدرس درست API را انتخاب می‌کند
// (اگر آدرس اشتباهی ذخیره شده باشد، خودکار به آدرس درست برمی‌گردد).
app.MapGet("/api/health", (HttpContext ctx) => Results.Json(new
{
    ok = true,
    app = "TotallCollectionSoftware",
    server = $"{ctx.Request.Scheme}://{ctx.Request.Host}",
    environment = app.Environment.EnvironmentName,
    database = provider,
    httpAddress = httpEndpoint.Display,
    httpsPorts = httpsCerts.AllPorts.ToArray(),
    serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
})).AllowAnonymous();

// هاب بلادرنگ داشبورد
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapHub<Inventory.Api.Hubs.NotifyHub>("/hubs/notify");
app.MapHub<Inventory.Api.Hubs.ChatHub>("/hubs/chat");

// ایجاد و یکدست‌سازی ساختار پوشه‌های آپلود بر اساس ماژول در wwwroot/uploads
var uploadsRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads");
foreach (var moduleFolder in new[]
{
    "users",
    "office/innerletter",
    "office/outgoingletter",
    "office/email",
    "projects",
    "hr/employee/documents",
    "hr/employee/photos",
    "hr/logo",
    "itassets/workorders",
    "itassets/requests",
    "cctv",
    "catalog",
    "chat",
    "system/archive"
})
{
    Directory.CreateDirectory(Path.Combine(uploadsRoot, moduleFolder));
}

// Archive sharing must never be bypassed through raw file URLs or cached API responses.
app.Use(async (ctx, next) =>
{
    if (Inventory.Api.Services.DocArchive.DocStaticProtection.IsProtectedPath(ctx.Request.Path.Value ?? ""))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    if (ctx.Request.Path.StartsWithSegments("/api/doc-archive") || ctx.Request.Path.StartsWithSegments("/api/attachments"))
        ctx.Response.Headers.CacheControl = "no-store";
    await next();
});

// سرو فایل‌های استاتیک کلاینت (استقرار تک‌سروره — در صورت وجود پوشه wwwroot)
var clientRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(clientRoot) && File.Exists(Path.Combine(clientRoot, "index.html")))
{
    // امنیت پیوست‌ها: فایل‌های رمزنگاری‌شده زیر wwwroot/SecureFiles هرگز به‌صورت استاتیک و بدون احراز هویت
    // سرو نشوند — دسترسی به آن‌ها فقط از مسیر API (ProjectAttachController با RBAC) مجاز است.
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/SecureFiles", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/uploads/projects", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        // پیوست نامه صادره و پیوست ایمیل سازمانی هرگز به‌صورت استاتیک سرو نمی‌شوند —
        // دانلود فقط از مسیر API مجاز با کنترل دسترسی انجام می‌شود.
        if (ctx.Request.Path.StartsWithSegments("/فایل های صادره", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/%D9%81%D8%A7%DB%8C%D9%84%20%D9%87%D8%A7%DB%8C%20%D8%B5%D8%A7%D8%AF%D8%B1%D9%87", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/uploads/office/outgoingletter", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/فایل های ایمیل", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/%D9%81%D8%A7%DB%8C%D9%84%20%D9%87%D8%A7%DB%8C%20%D8%A7%DB%8C%D9%85%DB%8C%D9%84", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/uploads/office/email", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next();
    });
    // نصب زیر Application/Virtual Directory در IIS (مثلاً http://server/erp/): base href پوستهٔ Blazor باید
    // مسیر پایه را داشته باشد، وگرنه _framework و همهٔ آدرس‌های نسبی از ریشهٔ سایت خوانده می‌شوند و برنامه باز نمی‌شود.
    // روی ریشهٔ سایت (PathBase خالی) هیچ تغییری در رفتار قبلی ایجاد نمی‌شود.
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.PathBase.HasValue
            && (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
            && (ctx.Request.Path == "/" || ctx.Request.Path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)))
        {
            await ServeSpaIndexAsync(ctx, clientRoot);
            return;
        }
        await next();
    });
    // Upstream disables Webcil: serve public managed client DLLs ONLY under _framework.
    var managedFrameworkRoot = Path.Combine(clientRoot, "_framework");
    if (Directory.Exists(managedFrameworkRoot))
    {
        var managedFrameworkTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        managedFrameworkTypes.Mappings[".dll"] = "application/octet-stream";
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(managedFrameworkRoot),
            RequestPath = "/_framework",
            ContentTypeProvider = managedFrameworkTypes,
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate"
        });
    }
    app.UseDefaultFiles();
    var previewContentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
    previewContentTypes.Mappings[".bcmap"] = "application/octet-stream";
    previewContentTypes.Mappings[".pfb"] = "application/octet-stream";
    previewContentTypes.Mappings[".wasm"] = "application/wasm";
    app.UseStaticFiles(new StaticFileOptions
    {
        ContentTypeProvider = previewContentTypes,
        // جلوگیری از کش‌شدن نسخه‌ی قدیمی کلاینت (service worker مرورگر)
        OnPrepareResponse = ctx =>
        {
                // نام فایل‌های _framework هش ندارد؛ max-age بلند یعنی مرورگرِ کاربر بعد از هر
                // استقرار جدید همچنان wasm قدیمی را از کش خودش می‌خواند و نسخهٔ جدید را «نمی‌بیند».
                // با no-cache هر بار با سرور اعتبارسنجی می‌شود و نسخهٔ تازه بلافاصله می‌رسد.
                ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        }
    });
    // ⚠️ حیاتی: FallbackPolicy سرویس‌های احراز هویت، «همهٔ» endpointهای بدون متادیتا را JWT-اجباری می‌کند.
    // endpoint فالبک (سرو فایل‌های کلاینت در استقرار تک‌سروره) هم بی‌متادیتا است؛ بدون AllowAnonymous،
    // بعد از پابلیش خودِ index.html و همهٔ فایل‌های WASM خطای 401 می‌گیرند و برنامه اصلاً باز نمی‌شود
    // (در حالت سورس، کلاینت با dev server جدا سرو می‌شود و مشکل دیده نمی‌شود).
    // APIها همچنان با همان FallbackPolicy محافظت می‌شوند؛ فقط پوستهٔ SPA عمومی می‌ماند (مثل صفحهٔ ورود).
    // ⚠ مسیرهای /api/* هرگز نباید پوستهٔ SPA را بگیرند: اگر درخواست API با هیچ endpoint ای
    // مطابقت نداشت (نسخهٔ قدیمی سرور / مسیر اشتباه)، به‌جای HTML باید 404 JSON برگردد تا
    // کلاینت به‌جای «کد صفحهٔ HTML» در باکس خطا، پیام قابل‌فهم ببیند.
    app.MapFallback("{*path:nonfile}", ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            return ctx.Response.WriteAsync("{\"message\": \"این مسیر در API یافت نشد؛ احتمالاً نسخهٔ سرور قدیمی است یا آدرس درخواست اشتباه است.\"}");
        }
        return ServeSpaIndexAsync(ctx, clientRoot);
    }).AllowAnonymous();
}

if (runningUnderIis)
{
    // آدرس را IIS می‌دهد؛ هیچ URL/پورتی اینجا تحمیل نشود
    app.Run();
}
else if (httpsCerts.Enabled && httpsCerts.Certificate is not null)
{
    Console.WriteLine($"[HTTPS] سامانه روی {httpEndpoint.Display} و {string.Join(" و ", httpsCerts.AllPorts.Select(p => $"https://0.0.0.0:{p}"))} در دسترس است.");
    Console.WriteLine($"[HTTPS] راهنمای نصب گواهی روی گوشی:  {httpEndpoint.Display}/https-setup");
    app.Run(); // بایندینگ‌ها در ConfigureKestrel تعیین شده‌اند (HTTP + HTTPS)
}
else
{
    app.Run(httpEndpoint.Display);
}

// پوشاندن رمز عبور در لاگ رشته اتصال
static string Mask(string cs)
{
    foreach (var key in new[] { "Password", "Pwd", "User Id" })
    {
        var idx = cs.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var valStart = idx + key.Length + 1;
            var end = cs.IndexOf(';', valStart);
            if (end < 0) end = cs.Length;
            return cs[..valStart] + "***" + cs[end..];
        }
    }
    return cs;
}

// ================== سرو پوستهٔ SPA با base href درست ==================
// اگر برنامه زیر یک مسیر پایه (PathBase، مثلاً Application «erp» در IIS) اجرا شود، <base href="/"> با
// «{PathBase}/» جایگزین می‌شود تا Blazor فایل‌ها و APIها را از همان مسیر بخواند. روی ریشهٔ سایت فایل دست‌نخورده است.
static async Task ServeSpaIndexAsync(HttpContext ctx, string clientRoot)
{
    var html = await File.ReadAllTextAsync(Path.Combine(clientRoot, "index.html"));
    var pathBase = ctx.Request.PathBase.Value?.TrimEnd('/') ?? "";
    if (pathBase.Length > 0)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(pathBase);
        html = html.Replace("<base href=\"/\" />", $"<base href=\"{encoded}/\" />")
                   .Replace("<base href=\"/\">", $"<base href=\"{encoded}/\">");
    }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    if (HttpMethods.IsHead(ctx.Request.Method)) return;
    await ctx.Response.WriteAsync(html);
}
