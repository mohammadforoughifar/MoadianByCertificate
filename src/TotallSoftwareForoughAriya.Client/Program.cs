using Inventory.Client;
using Inventory.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HttpClient با آدرس داینامیک — همان آدرسی که ApiOptions در زمان اجرا تعیین می‌کند
// (قبلاً هاردکد به http://localhost:5100 بود و صفحات سیستم روی پورت/دامنه دیگر کار نمی‌کردند)
builder.Services.AddScoped(sp =>
{
    var opts = sp.GetRequiredService<ApiOptions>();
    var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl) ? "http://localhost:5100" : opts.BaseUrl;
    var baseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    // PathBaseHandler: اگر برنامه روی IIS زیر یک Application/Virtual Directory نصب شود (مثلاً /erp/)،
    // درخواست‌هایی که با «/api/...» (اسلش اول) نوشته شده‌اند از ریشهٔ سایت خوانده می‌شدند و 404 می‌گرفتند.
    // روی ریشهٔ سایت هیچ اثری ندارد.
    return new HttpClient(new PathBaseHandler(baseAddress) { InnerHandler = new HttpClientHandler() })
    {
        BaseAddress = baseAddress,
        // سقف پشتیبان؛ سقف واقعی هر فراخوانی در ApiClient (۱۰۰ ثانیه معمولی، ۱۰ دقیقه برای AI) اعمال می‌شود
        Timeout = TimeSpan.FromMinutes(15),
    };
});
builder.Services.AddScoped<TabService>(); // تب‌های کاری (نگه‌داری صفحاتِ باز)
builder.Services.AddSingleton<ApiOptions>();
builder.Services.AddSingleton<IAuthState, AuthState>();
builder.Services.AddScoped<IAuthApi, AuthApi>();

// ---------- ثبت سرویس‌ها با اینترفیس (اصل وارونگی وابستگی — DIP) ----------
builder.Services.AddScoped<IApiClient, ApiClient>();
builder.Services.AddScoped<BonHrApi>();
builder.Services.AddScoped<IMoadianClientService, MoadianClientService>(); // سامانه مودیان
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<ICompanyProfileService, CompanyProfileService>(); // تعریف شرکت/فروشگاه و تنظیمات چاپ فاکتور
builder.Services.AddScoped<IDashboardClient, DashboardClient>(); // داشبورد شخصی کاربر
builder.Services.AddScoped<IReportStudioClient, ReportStudioClient>(); // گزارش‌ساز حرفه‌ای
builder.Services.AddScoped<IReferrerService, ReferrerService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IUnitService, UnitService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();
builder.Services.AddScoped<IPartyService, PartyService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IGoodsIssueService, GoodsIssueService>(); // حواله تحویل کالا (بدون قیمت)
builder.Services.AddScoped<ISalesVisitApi, SalesVisitApi>(); // ماژول ویزیتوری
builder.Services.AddScoped<ICartableApi, CartableApi>(); // کارتابل یکپارچه
builder.Services.AddScoped<IReportService, ReportService>();
// ---------- ماژول انبارداری ----------
builder.Services.AddScoped<IInvCategoryService, InvCategoryService>();
builder.Services.AddScoped<IInvAttributeService, InvAttributeService>();
builder.Services.AddScoped<IInvProductService, InvProductService>();
builder.Services.AddScoped<IInvWarehouseService, InvWarehouseService>();
builder.Services.AddScoped<IInvDocTypeService, InvDocTypeService>();
builder.Services.AddScoped<IInvDocService, InvDocService>();
builder.Services.AddScoped<IInvReportService, InvReportService>();
builder.Services.AddScoped<IInvSettingsService, InvSettingsService>();
builder.Services.AddScoped<IDocArchiveService, DocArchiveService>(); // ماژول آرشیو اسناد و مدارک
builder.Services.AddScoped<IDocImportClient, DocImportClient>(); // آرشیو — ورود انبوه
builder.Services.AddScoped<IHrCoreService, HrCoreService>(); // HrCore
builder.Services.AddScoped<IHrTalentService, HrTalentService>(); // HrTalent
builder.Services.AddScoped<IHrMainService, HrMainService>(); // HrMain — منابع انسانی اصلی
builder.Services.AddScoped<IFaAttService, FaAttService>(); // FaAtt — حضور و غیاب فروغ آریا
builder.Services.AddScoped<IFaComService, FaComService>(); // FaCom — ارتباطات داخلی
builder.Services.AddScoped<IFaLmsService, FaLmsService>(); // FaLms — آموزش و توسعه
builder.Services.AddScoped<IFaPayClientService, FaPayService>(); // FaPay — حقوق و دستمزد

builder.Services.AddScoped<IChatClientService, ChatClientService>(); // ماژول پیام‌رسان سازمانی (Chat)
builder.Services.AddScoped<AiAssistantService>(); // هوش مصنوعی فروغ آریا

// ---------- ماژول حسابداری ----------
builder.Services.AddScoped<IAccFiscalYearService, AccFiscalYearService>();
builder.Services.AddScoped<IAccAccountService, AccAccountService>();
builder.Services.AddScoped<IAccVoucherService, AccVoucherService>();
builder.Services.AddScoped<IAccReportService, AccReportService>();
builder.Services.AddScoped<IAccInvRuleService, AccInvRuleService>();
builder.Services.AddScoped<IAccDimensionService, AccDimensionService>();
builder.Services.AddScoped<IFixedAssetCategoryService, FixedAssetCategoryService>();
builder.Services.AddScoped<IFixedAssetService, FixedAssetService>();
builder.Services.AddScoped<IFixedAssetRunService, FixedAssetRunService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();

// ---------- ماژول فاکتور ----------
builder.Services.AddScoped<IFacInvoiceService, FacInvoiceService>();
builder.Services.AddScoped<IFacRuleService, FacRuleService>();
builder.Services.AddScoped<IFacReportService, FacReportService>();


// ماژول خزانه‌داری
builder.Services.AddScoped<ITrsAccountService, TrsAccountService>();
builder.Services.AddScoped<ITrsVoucherService, TrsVoucherService>();
builder.Services.AddScoped<ITrsChequeService, TrsChequeService>();
builder.Services.AddScoped<ITrsRuleService, TrsRuleService>();
builder.Services.AddScoped<ITrsReportService, TrsReportService>();

// ماژول انبارگردانی و بارکد
builder.Services.AddScoped<IBcdBarcodeService, BcdBarcodeService>();
builder.Services.AddScoped<IStkSessionService, StkSessionService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IRepairService, RepairService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
// ---------- ماژول مدیریت پروژه‌ها ----------
builder.Services.AddScoped<IKarfarmaService, KarfarmaService>();
builder.Services.AddScoped<ITypeFactorService, TypeFactorService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IProjectCartableService, ProjectCartableService>();
builder.Services.AddScoped<IReportWorkService, ReportWorkService>();
// ---------- ماژول اتوماسیون اداری (نامه داخلی + صادره) ----------
builder.Services.AddScoped<ILetterService, LetterService>();
builder.Services.AddScoped<IOutgoingLetterService, OutgoingLetterService>();
builder.Services.AddScoped<IIncomingLetterService, IncomingLetterService>();
builder.Services.AddScoped<IEmailClientService, EmailClientService>(); // ایمیل سازمانی (پست الکترونیک)
// ---------- فرم‌های متفرقه — صورتجلسه ----------
builder.Services.AddScoped<IMinutesApiService, MinutesApiService>();
builder.Services.AddScoped<IDevTeamClient, DevTeamClient>(); // میز کار توسعه
// تنظیمات ساختار شماره نامه (سازمان‌ها — جزء «واحد»)
builder.Services.AddScoped<ILetterStructureService, LetterStructureService>();
builder.Services.AddSingleton<IToastService, ToastService>();
builder.Services.AddSingleton<LayoutState>();
builder.Services.AddSingleton<RealtimeService>(); // هسته‌ی بلادرنگ سراسری (SignalR)

builder.Services.AddScoped<LetterPrintWorkflow>();
var host = builder.Build();

// تعیین آدرس API در زمان اجرا (پشتیبانی از پیش‌نمایش ابری و localhost)
var js = host.Services.GetRequiredService<Microsoft.JSInterop.IJSRuntime>();
var options = host.Services.GetRequiredService<ApiOptions>();
// ابتدا گزینه‌های ممکنِ آدرس API با یک درخواست واقعی آزمایش می‌شوند (هم‌مبدأ ← آدرس ذخیره‌شده ← آدرس پیش‌فرض سرور)
// تا اگر مرورگر به آدرس اول دسترسی نداشت، خودکار سراغ آدرس درست برود و صفحهٔ ورود «ارتباط برقرار نشد» ندهد.
try
{
    options.BaseUrl = await js.InvokeAsync<string>("inventoryResolveApiBase",
        new object[] { "http://192.168.30.104:8181" });
}
catch
{
    options.BaseUrl = await js.InvokeAsync<string>("inventoryApiBase",
        new object[] { "http://192.168.30.104:8181" });
}
if (string.IsNullOrWhiteSpace(options.BaseUrl) || options.BaseUrl.StartsWith("__")) options.BaseUrl = "http://192.168.30.104:8181";

// بازیابی نشست ورود از localStorage
await host.Services.GetRequiredService<IAuthState>().InitializeAsync();

await host.RunAsync();
