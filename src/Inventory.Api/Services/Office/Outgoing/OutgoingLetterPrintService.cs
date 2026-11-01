using System.Globalization;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text;
using System.Security.Cryptography;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Xml.Linq;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Office.Outgoing;

// ============================================================
//  چاپ نامه صادره روی سربرگ شرکت — سایز A4 و A5
//  • منبع سربرگ به ترتیب:
//    ۱) فایل ثبت‌شده در SystemCompanies.LetterheadFileName (نسبت به wwwroot)
//    ۲) طرح سربرگ جاسازی‌شده در قالب چاپ خودِ شرکت
//       (Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{Size}.mrt —
//        Watermark.ImageBytes)؛ این طرح همیشه همراه خروجی سرور منتشر می‌شود
//        و چاپ را از تنظیمات بانک اطلاعاتی مستقل می‌کند.
//    ۳) سربرگ فوروث / لوگوی HR — فقط برای نامه‌های بدون شرکت.
//  • متن نامه با QuestPDF (فونت فارسی وزیرمتن، راست‌به‌چپ) تولید و سپس
//    با PDFsharp روی صفحه سربرگ قرار می‌گیرد (Overlay).
//  • نامه‌ای که «شرکت» دارد هرگز بدون سربرگ (روی صفحهٔ سفید) چاپ نمی‌شود؛
//    اگر سربرگ پیدا نشود یا اعمال آن شکست بخورد، چاپ با پیام دقیق متوقف می‌شود.
// ============================================================

public interface IOutgoingLetterPrintService
{
    /// <summary>
    /// تولید PDF نامه صادره روی سربرگ شرکت
    /// </summary>
    /// <param name="letterId">شناسه نامه صادره</param>
    /// <param name="size">"A4" یا "A5"</param>
    /// <param name="withCopy">
    /// نسخهٔ چاپ: true = «با رونوشت» (بلوک رونوشت چاپ می‌شود)،
    /// false = «بدون رونوشت» (بلوک رونوشت حذف می‌شود — برای نسخه‌ای که به
    /// سازمان مقصد تحویل داده می‌شود و نیازی به اطلاع رونوشت‌گیرندگان ندارد)
    /// </param>
    /// <param name="requireSignatures">
    /// وقتی true است (دبیرخانه/مدیر) تا کامل‌شدن امضاها و وجود تصویر امضای همهٔ امضاکنندگان، چاپ انجام نمی‌شود.
    /// برای ایجادکننده/دریافت‌کنندهٔ نامه false است تا آن‌ها بتوانند نسخهٔ چاپی بگیرند (جای امضا فقط نام و سِمَت می‌آید).
    /// </param>
    Task<byte[]?> GeneratePdfAsync(int letterId, string size, bool withCopy = true, bool withMargin = true, bool withLetterhead = true, bool requireSignatures = true);

    /// <summary>
    /// تشخیص مسیر سربرگ/طرح یک نامه (فقط خواندن) — برای پاسخ به «چرا طرح شرکت خوانده نمی‌شود؟».
    /// </summary>
    Task<OutgoingLetterPrintDiagnosticsDto?> DiagnoseAsync(int letterId, string size);
}

/// <summary>Raised when a company-required letterhead cannot be resolved or applied.</summary>
public sealed class RequiredCompanyLetterheadPrintException : Exception
{
    public RequiredCompanyLetterheadPrintException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class OutgoingSignaturePrintException : Exception
{
    public OutgoingSignaturePrintException(string message) : base(message) { }
}

public class OutgoingLetterPrintService : IOutgoingLetterPrintService
{
    private sealed record SignatureSvgData(string Svg, int Width, int Height);
    private sealed record CachedSignature(SignatureSvgData? Data);
    private sealed record SematPrintTitle(int SematId, int? UserId, string Title, string? CorrespondenceTitle, bool DefaultSemat, bool IsActive);
    private sealed class CachedLetterheadAsset
    {
        public CachedLetterheadAsset(bool isPdf, XPdfForm? pdf, XImage? image)
        {
            IsPdf = isPdf;
            Pdf = pdf;
            Image = image;
        }

        public bool IsPdf { get; }
        public XPdfForm? Pdf { get; }
        public XImage? Image { get; }
        public object DrawLock { get; } = new();
    }
    private static readonly ConcurrentDictionary<string, Lazy<CachedLetterheadAsset>> LetterheadAssets = new(StringComparer.OrdinalIgnoreCase);

    // ===== کش کوتاه‌مدت خروجی چاپ =====
    // ساخت PDF (QuestPDF + PDFsharp روی تصویر بزرگ سربرگ) چند ثانیه طول می‌کشد. اگر مرورگر
    // پیش از پایان، درخواست را قطع کند (مهلت کلاینت) یا کاربر «تلاش مجدد» بزند، بدون این کش
    // همان رندر سنگین از صفر تکرار می‌شد و چاپ هیچ‌وقت آماده نمی‌شد. اکنون نتیجه ۹۰ ثانیه
    // نگه داشته می‌شود و درخواست‌های هم‌زمان همان Task را به اشتراک می‌گذارند.
    private static readonly TimeSpan RenderedPdfLifetime = TimeSpan.FromSeconds(90);

    /// <summary>
    /// رندرِ مشترکِ درخواست‌های هم‌زمان. عمداً به‌جای <c>Lazy&lt;Task&lt;T&gt;&gt;</c> نوشته شده است:
    /// خواندن <c>Lazy&lt;Task&lt;T&gt;&gt;.Value</c> قاعدهٔ تحلیل‌گر VSTHRD011 را می‌شکند (خطر deadlock
    /// در زمینهٔ همگام) و ساخت پروژه را با «Error» متوقف می‌کند. اینجا Task یک‌بار ساخته می‌شود و
    /// همهٔ فراخوان‌ها فقط آن را await می‌کنند.
    /// </summary>
    private sealed class CachedRender
    {
        private readonly TaskCompletionSource<byte[]?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CachedRender(DateTime createdUtc) => CreatedUtc = createdUtc;

        public DateTime CreatedUtc { get; }

        public Task<byte[]?> Render => _completion.Task;

        public void Complete(byte[]? pdf) => _completion.TrySetResult(pdf);

        public void Fail(Exception error) => _completion.TrySetException(error);
    }

    private static readonly ConcurrentDictionary<string, CachedRender> RenderedPdfs = new(StringComparer.Ordinal);

    // These companies must use their own registered letterheads; never substitute Forough's.
    private const int HomepaCompanyId = 1003;
    private const int AriaCompanyId = 1005;

    // Companies whose artwork is the Homepa design. The same company can have a different
    // id in another installation (1003 in the main database, 3 in the sample one), and
    // Reports/OutgoingLetters/Companies/3 as well as /1003 both embed the Homepa artwork.
    // When the id is missing from this list, the letter falls back to the generic layout
    // and its number/date/attachment values land off the printed labels.
    // Override without a rebuild: Reporting:OutgoingLetters:HomepaCompanyIds = "3,1003".
    private static readonly int[] DefaultHomepaCompanyIds = { 3, HomepaCompanyId };
    private static readonly int[] DefaultAriaCompanyIds = { AriaCompanyId };

    private static bool _fontsRegistered;
    private static readonly object FontLock = new();

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<OutgoingLetterPrintService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor? _http;
    private readonly int[] _homepaCompanyIds;
    private readonly int[] _ariaCompanyIds;
    private readonly Dictionary<int, int> _companyTemplateMap;

    public OutgoingLetterPrintService(
        AppDbContext db,
        IWebHostEnvironment env,
        ILogger<OutgoingLetterPrintService> logger,
        IConfiguration? configuration = null,
        IMemoryCache? cache = null,
        IHttpContextAccessor? http = null)
    {
        _db = db;
        _env = env;
        _logger = logger;
        _configuration = configuration ?? new ConfigurationBuilder().Build();
        _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        _http = http;
        _homepaCompanyIds = CompanyIdsFor("Reporting:OutgoingLetters:HomepaCompanyIds", DefaultHomepaCompanyIds);
        _ariaCompanyIds = CompanyIdsFor("Reporting:OutgoingLetters:AriaCompanyIds", DefaultAriaCompanyIds);
        _companyTemplateMap = ReadCompanyTemplateMap();
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// فهرست شناسهٔ شرکت‌هایی که طرح سربرگ هومپا/آریا دارند. با تنظیم
    /// <c>Reporting:OutgoingLetters:HomepaCompanyIds</c> (مثلاً <c>"3,1003"</c>) می‌توان
    /// شناسهٔ شرکت را روی هر نصب دیگری، بدون تغییر کد، مشخص کرد.
    /// </summary>
    private int[] CompanyIdsFor(string key, int[] fallback)
    {
        var raw = _configuration[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        var ids = raw.Split(new[] { ',', ';', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        if (ids.Length == 0) return fallback;
        return ids;
    }

    /// <summary>
    /// نگاشت «شناسهٔ شرکتِ پایگاه داده ← شناسهٔ پوشهٔ طرح» از
    /// <c>Reporting:OutgoingLetters:CompanyTemplateMap</c>.
    /// تصویر سربرگ و مختصات برچسب‌ها هر دو از همان پوشه خوانده می‌شوند، پس با این نگاشت
    /// هر شرکتی — با هر شناسه‌ای — روی طرح درست خودش چاپ می‌شود.
    /// </summary>
    private Dictionary<int, int> ReadCompanyTemplateMap()
    {
        var map = new Dictionary<int, int>();
        foreach (var entry in _configuration.GetSection("Reporting:OutgoingLetters:CompanyTemplateMap").GetChildren())
        {
            if (!int.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId) || companyId <= 0) continue;
            if (!int.TryParse((entry.Value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var designId) || designId <= 0) continue;
            map[companyId] = designId;
        }
        return map;
    }

    /// <summary>شناسهٔ طرحی که این شرکت باید با آن چاپ شود (نگاشت صریح، وگرنه خودش).</summary>
    private int TemplateDesignId(int companyId) => _companyTemplateMap.TryGetValue(companyId, out var mapped) ? mapped : companyId;

    /// <summary>ترتیب جست‌وجوی پوشهٔ قالب: طرح نگاشت‌شده، سپس پوشهٔ هم‌نام خود شرکت.</summary>
    private IEnumerable<int> DesignCompanyIds(int companyId)
    {
        var designId = TemplateDesignId(companyId);
        if (designId != companyId) yield return designId;
        yield return companyId;
    }

    /// <summary>شرکت فعالِ همان درخواست (اعتبارسنجی‌شده در ActiveCompanyMiddleware).</summary>
    /// <summary>
    /// آیا طرح جاسازی‌شده در قالب MRT شرکت (Reports/OutgoingLetters/Companies/{id}) بر فایل
    /// ثبت‌شده روی رکورد شرکت اولویت دارد؟ پیش‌فرض: نه (رفتار قبلی).
    /// تنظیم: Reporting:OutgoingLetters:PreferTemplateLetterhead = true
    /// </summary>
    private bool PreferTemplateLetterhead
    {
        get
        {
            var raw = _configuration["Reporting:OutgoingLetters:PreferTemplateLetterhead"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    private int ActiveCompanyIdFromContext()
    {
        var ctx = _http?.HttpContext;
        if (ctx == null) return 0;
        return ctx.Items.TryGetValue(Inventory.Api.Infrastructure.ActiveCompanyMiddleware.ItemKey, out var v)
               && v is int id ? id : 0;
    }

    // ==================== فونت فارسی ====================

    private static string FontDir => Path.Combine(AppContext.BaseDirectory, "Resources", "fonts");

    private static void EnsureFonts()
    {
        if (_fontsRegistered) return;
        lock (FontLock)
        {
            if (_fontsRegistered) return;
            var reg = Path.Combine(FontDir, "Vazirmatn-Regular.ttf");
            var bold = Path.Combine(FontDir, "Vazirmatn-Bold.ttf");

            // فونت غایب قبلاً بی‌صدا نادیده گرفته می‌شد و بعد QuestPDF با فونت جانشین، متن
            // فارسی را ناخوانا/بریده چاپ می‌کرد (یا خطای مبهم می‌داد). اکنون علت روشن است.
            if (!File.Exists(reg))
                throw new InvalidOperationException(
                    $"فونت چاپ فارسی پیدا نشد: {reg}. پوشهٔ Resources/fonts را همراه خروجی API منتشر کنید " +
                    "(در csproj با «Content Include=\"Resources/fonts/**/*\"» کپی می‌شود).");

            using (var regularStream = File.OpenRead(reg))
                FontManager.RegisterFontWithCustomName("Vazirmatn", regularStream);
            if (File.Exists(bold))
            {
                using var boldStream = File.OpenRead(bold);
                FontManager.RegisterFontWithCustomName("Vazirmatn-Bold", boldStream);
            }
            _fontsRegistered = true;
        }
    }

    // ==================== سربرگ (PDF از مسیر روت API) ====================

    /// <summary>یافتن سربرگ نسبت به wwwroot، با پشتیبانی از نام فایل‌های قدیمی</summary>
    private string? ResolveLetterheadPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var clean = fileName.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadClean = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? clean["uploads/".Length..]
            : clean;

        var uploadFileName = Path.GetFileName(uploadClean);
        var candidates = new[]
        {
            // Supports canonical values such as uploads/letterheads/name.pdf.
            Path.Combine(webRoot, "uploads", uploadClean),
            // Older company records may store only the filename. Letterheads live
            // in this subfolder, not directly under wwwroot/uploads.
            Path.Combine(webRoot, "uploads", "letterheads", uploadFileName),
            Path.Combine(webRoot, clean),
            Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", "letterheads", uploadFileName),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(_env.ContentRootPath, Path.GetFileName(clean)),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads", "letterheads", uploadFileName),
            Path.Combine(AppContext.BaseDirectory, clean),
            Path.Combine(AppContext.BaseDirectory, Path.GetFileName(clean))
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found == null) return null;

        // Only renderable letterhead files (PDF/image) are accepted. Values that
        // point at an MRT report template or any other file type are ignored —
        // the template's embedded design is used through ExtractTemplateLetterhead.
        var extension = Path.GetExtension(found).ToLowerInvariant();
        return extension is ".pdf" or ".png" or ".jpg" or ".jpeg" ? found : null;
    }

    /// <summary>
    /// فایل سربرگ ثبت‌شده روی رکورد شرکت — بدون جانشین‌سازی. مقدار نامعتبر
    /// (مسیر قالب MRT، فایل غایب یا نوع غیرقابل‌رندر) نادیده گرفته می‌شود تا
    /// سربرگ جاسازی‌شده در قالب همان شرکت جایگزین شود.
    /// </summary>
    private string? ResolveRegisteredLetterheadFile(SystemCompany? company)
        => ResolveLetterheadPath(company?.LetterheadFileName);

    /// <summary>برای پیام خطا: فایل ثبت‌شده به هر نوعی که باشد، اگر جایی موجود است مسیرش برمی‌گردد.</summary>
    private string? TryResolveAnyPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var clean = fileName.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadClean = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? clean["uploads/".Length..]
            : clean;
        var uploadFileName = Path.GetFileName(uploadClean);
        var candidates = new[]
        {
            Path.Combine(webRoot, "uploads", uploadClean),
            Path.Combine(webRoot, "uploads", "letterheads", uploadFileName),
            Path.Combine(webRoot, clean),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(AppContext.BaseDirectory, clean)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // ==================== سربرگ جاسازی‌شده در قالب MRT شرکت ====================

    /// <summary>
    /// طرح سربرگ طراحی‌شدهٔ هر شرکت داخل فایل قالب چاپ همان شرکت
    /// (Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{Size}.mrt)
    /// به‌صورت Watermark.ImageBytes ذخیره شده است. این متد همان تصویر را
    /// استخراج و در پوشهٔ موقت کش می‌کند تا نامهٔ هر شرکت روی سربرگ خودش
    /// چاپ شود — حتی اگر رکورد شرکت در بانک اطلاعاتی سربرگی ثبت نکرده باشد
    /// یا فایل‌های wwwroot روی سرور موجود نباشند (قالب‌ها همراه بیلد منتشر می‌شوند).
    /// </summary>
    private string? ExtractTemplateLetterhead(int companyId, bool isA5)
    {
        var sizes = isA5 ? new[] { "A5", "A4" } : new[] { "A4", "A5" };
        foreach (var templateSize in sizes)
        {
            var templatePath = FindCompanyTemplate(companyId, templateSize);
            if (templatePath == null) continue;

            try
            {
                var templateInfo = new FileInfo(templatePath);
                var cacheKey = $"outgoing-letterhead:{templatePath}:{templateInfo.Length}:{templateInfo.LastWriteTimeUtc.Ticks}";
                if (_cache.TryGetValue(cacheKey, out string? cachedPath) && cachedPath != null && File.Exists(cachedPath))
                {
                    _logger.LogDebug("سربرگ MRT از کش آماده است. CompanyId={CompanyId}, TemplateSize={Size}", companyId, templateSize);
                    return cachedPath;
                }

                var watermarkBytes = ReadTemplateWatermark(templatePath);
                if (watermarkBytes is not { Length: > 0 })
                {
                    _logger.LogWarning(
                        "قالب MRT شرکت سربرگ جاسازی‌شده ندارد. CompanyId={CompanyId}, Path={Path}",
                        companyId, templatePath);
                    continue;
                }

                var templateIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(templatePath))))[..16].ToLowerInvariant();
                var cachePath = Path.Combine(
                    Path.GetTempPath(), "totall-letterheads",
                    $"company-{companyId}-outgoing-{templateSize.ToLowerInvariant()}-{templateIdentity}-{templateInfo.Length}-{templateInfo.LastWriteTimeUtc.Ticks}.jpg");
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                if (!File.Exists(cachePath))
                {
                    var temporaryPath = cachePath + $".{Guid.NewGuid():N}.tmp";
                    File.WriteAllBytes(temporaryPath, watermarkBytes);
                    try { File.Move(temporaryPath, cachePath, overwrite: true); }
                    finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                }
                _cache.Set(cacheKey, cachePath, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12),
                    Size = watermarkBytes.Length
                });

                _logger.LogInformation(
                    "سربرگ MRT بارگذاری و کش شد. CompanyId={CompanyId}, TemplateSize={TemplateSize}, Bytes={Bytes}",
                    companyId, templateSize, watermarkBytes.Length);
                return cachePath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "استخراج سربرگ از قالب MRT ناموفق بود. CompanyId={CompanyId}, Path={Path}",
                    companyId, templatePath);
            }
        }
        return null;
    }

    /// <summary>
    /// تشخیص مسیر سربرگ یک نامه بدون تولید PDF؛ پاسخ روشن به این پرسش که
    /// «چرا طرح شرکت در Reports/OutgoingLetters/Companies خوانده نمی‌شود؟»
    /// </summary>
    public async Task<OutgoingLetterPrintDiagnosticsDto?> DiagnoseAsync(int letterId, string size)
    {
        var letter = await _db.OutgoingLetters.AsNoTracking()
            .Where(l => l.Id == letterId && !l.IsDelete)
            .Select(l => new { l.Id, l.CompanyId, l.CreatorUserId, l.SadereNumber, l.LetterNumber, l.Title })
            .FirstOrDefaultAsync();

        if (letter == null) return null;

        var normalizedSize = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase) ? "A5" : "A4";
        var isA5 = normalizedSize == "A5";

        var activeCompanyIds = await _db.SystemCompanies.AsNoTracking()
            .Where(c => c.IsActive).Select(c => c.Id).ToListAsync();

        var letterCompanyId = letter.CompanyId is > 0 ? letter.CompanyId : null;
        var requestCompanyId = ActiveCompanyIdFromContext();
        var effectiveCompanyId = letterCompanyId;
        var companySource = letterCompanyId != null ? "Letter" : "None";

        if (effectiveCompanyId == null)
        {
            var pickId = activeCompanyIds.Count == 1
                ? activeCompanyIds[0]
                : requestCompanyId > 0 && activeCompanyIds.Contains(requestCompanyId) ? requestCompanyId : 0;
            if (pickId > 0)
            {
                effectiveCompanyId = pickId;
                companySource = activeCompanyIds.Count == 1 ? "OnlyActiveCompany" : "RequestActiveCompany";
            }
        }

        var company = effectiveCompanyId is int companyKey
            ? await _db.SystemCompanies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyKey)
            : null;

        var designId = effectiveCompanyId is int rawDesignId ? TemplateDesignId(rawDesignId) : (int?)null;
        var templatePath = effectiveCompanyId is int designCandidate
            ? FindCompanyTemplate(designCandidate, normalizedSize)
            : null;

        int? templateImageBytes = null;
        string? templateError = null;
        if (templatePath != null)
        {
            try
            {
                templateImageBytes = ReadTemplateWatermark(templatePath)?.Length ?? 0;
            }
            catch (Exception ex)
            {
                templateError = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        var registeredPath = company != null ? ResolveRegisteredLetterheadFile(company) : null;
        var preferTemplate = PreferTemplateLetterhead;

        string letterheadSource;
        if (preferTemplate && templatePath != null) letterheadSource = "Template";
        else if (registeredPath != null) letterheadSource = "Registered";
        else if (templatePath != null) letterheadSource = "Template";
        else letterheadSource = "None";

        var configuredRoot = _configuration["Reporting:Stimulsoft:TemplateRoot"];
        var templateRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine("Reports", "OutgoingLetters", "Companies")
            : configuredRoot.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var roots = new[] { _env.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
            .Distinct().ToList();

        var companyRecordMissing = effectiveCompanyId != null && company == null;
        var hint = BuildDiagnosticsHint(
            letterCompanyId, companySource, effectiveCompanyId, companyRecordMissing,
            templatePath, templateError, registeredPath, preferTemplate, letterheadSource);

        return new OutgoingLetterPrintDiagnosticsDto
        {
            LetterId = letter.Id,
            LetterNumber = letter.SadereNumber ?? letter.LetterNumber,
            Title = letter.Title,
            LetterCompanyId = letterCompanyId,
            RequestActiveCompanyId = requestCompanyId > 0 ? requestCompanyId : null,
            EffectiveCompanyId = effectiveCompanyId,
            CompanySource = companySource,
            CompanyName = company?.Name,
            CompanyRecordMissing = companyRecordMissing,
            LetterheadFileName = company?.LetterheadFileName,
            TemplateDesignId = designId,
            CompanyTemplateMapApplied = designId != null && effectiveCompanyId != null && designId != effectiveCompanyId,
            TemplatePath = templatePath,
            TemplateImageBytes = templateImageBytes,
            TemplateError = templateError,
            RegisteredLetterheadPath = registeredPath,
            PreferTemplateLetterhead = preferTemplate,
            LetterheadSource = letterheadSource,
            TemplateRoot = templateRoot,
            SearchRoots = roots,
            AvailableDesigns = DescribeAvailableDesigns(templateRoot, roots.ToArray()),
            Hint = hint
        };
    }

    /// <summary>ساخت جملهٔ علت به فارسی — همان چیزی که در لاگ/پاسخ تشخیص دیده می‌شود.</summary>
    private static string BuildDiagnosticsHint(
        int? letterCompanyId,
        string companySource,
        int? effectiveCompanyId,
        bool companyRecordMissing,
        string? templatePath,
        string? templateError,
        string? registeredPath,
        bool preferTemplate,
        string letterheadSource)
    {
        if (letterCompanyId == null)
        {
            var how = companySource switch
            {
                "OnlyActiveCompany" => "چون تنها یک شرکت فعال وجود دارد",
                "RequestActiveCompany" => "بر اساس شرکت فعال انتخاب‌شده در بالای صفحه",
                _ => "و شرکتی هم قابل انتخاب نبود"
            };
            return effectiveCompanyId == null
                ? "این نامه شرکت صادرکننده (CompanyId) ندارد و هیچ شرکت فعالی هم پیدا نشد؛ طرح شرکت قابل انتخاب نیست."
                : $"این نامه شرکت صادرکننده (CompanyId) ندارد؛ شرکت {effectiveCompanyId} {how} انتخاب شد. " +
                  "اگر طرح چاپی متعلق به شرکت دیگری است، نامه را ویرایش کنید و «شرکت صادرکننده» را انتخاب کنید.";
        }

        if (companyRecordMissing)
            return $"شناسهٔ شرکت این نامه ({effectiveCompanyId}) در جدول SystemCompanies نیست یا غیرفعال است؛ " +
                   "رکورد شرکت را درست کنید یا شناسه را با CompanyTemplateMap نگاشت کنید.";

        if (templatePath == null)
            return $"هیچ قالب چاپی برای شرکت {effectiveCompanyId} پیدا نشد؛ پوشهٔ " +
                   $"Reports/OutgoingLetters/Companies/{effectiveCompanyId} یا فایل Outgoing-A4/A5.mrt را بررسی کنید. " +
                   "فهرست پوشه‌های موجود در AvailableDesigns آمده است.";

        if (templateError != null)
            return $"فایل قالب {templatePath} خوانده شد اما تصویر سربرگ آن قابل استخراج نیست ({templateError}).";

        if (registeredPath != null && !preferTemplate)
            return $"سربرگ چاپ از فایل ثبت‌شدهٔ شرکت استفاده می‌کند ({registeredPath}) و طرح داخل قالب نادیده گرفته می‌شود. " +
                   "برای چاپ با طرح پوشهٔ Companies یا مقدار Reporting:OutgoingLetters:PreferTemplateLetterhead=true را تنظیم کنید " +
                   "یا فایل ثبت‌شدهٔ شرکت (LetterheadFileName) را خالی کنید.";

        return letterheadSource == "Template"
            ? $"سربرگ چاپ از طرح داخل قالب شرکت خوانده می‌شود: {templatePath}"
            : "سربرگی برای این نامه پیدا نشد؛ چاپ بدون سربرگ خواهد بود.";
    }

    /// <summary>
    /// مختصات/رنگ سه مقدار شماره، تاریخ و پیوست از قالب خود شرکت — با کش کوتاه‌مدت،
    /// چون هر چاپ یک فایل XML بزرگ را می‌خواند. کلید کش شامل طول و زمان تغییر فایل است،
    /// بنابراین ویرایش قالب بلافاصله اثر می‌گذارد.
    /// </summary>
    private LetterheadMetaFields? ReadDeclaredMetaFields(string? templatePath)
    {
        if (string.IsNullOrWhiteSpace(templatePath)) return null;

        var info = new FileInfo(templatePath);
        if (!info.Exists) return null;

        var cacheKey = $"outgoing-meta-fields:{templatePath}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        if (_cache.TryGetValue(cacheKey, out LetterheadMetaFields? cached) && cached != null) return cached;

        var parsed = LetterheadMetaFields.TryRead(templatePath);
        if (parsed != null)
        {
            _cache.Set(cacheKey, parsed, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            });
        }
        return parsed;
    }

    private string? FindCompanyTemplate(int companyId, string size)
    {
        // The active QuestPDF pipeline must honor the same template-root setting as the legacy renderer.
        var configuredRoot = _configuration["Reporting:Stimulsoft:TemplateRoot"];
        var templateRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine("Reports", "OutgoingLetters", "Companies")
            : configuredRoot.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var roots = new[] { _env.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() };

        // شناسهٔ شرکت بین نصب‌ها یکسان نیست (هومپا در یکی 1003 و در دیگری 3)؛ نگاشت صریح
        // جلوی چاپ روی طرح شرکت دیگر را می‌گیرد.
        foreach (var designId in DesignCompanyIds(companyId))
        {
            var folder = Path.Combine(templateRoot, designId.ToString(CultureInfo.InvariantCulture));
            var folders = Path.IsPathRooted(templateRoot)
                ? new[] { folder }
                : roots.Select(root => Path.Combine(root, folder)).Distinct().ToArray();

            foreach (var candidateFolder in folders)
            {
                var found = FindTemplateFile(candidateFolder, size, out var exactName);
                if (found == null) continue;

                if (designId != companyId)
                    _logger.LogInformation(
                        "قالب چاپ شرکت {CompanyId} از طرح شرکت {DesignCompanyId} استفاده می‌کند (Reporting:OutgoingLetters:CompanyTemplateMap). Path={Path}, Size={Size}",
                        companyId, designId, found, size);
                if (!exactName)
                    _logger.LogInformation(
                        "نام فایل قالب شرکت {CompanyId} مطابق استاندارد نیست؛ انتظار «{Expected}» بود و همین فایل استفاده شد: {Path}",
                        companyId, $"Outgoing-{size}.mrt", found);
                return found;
            }
        }

        // قبلاً این حالت فقط LogDebug بود و در سطح پیش‌فرض Information هیچ اثری در لاگ نداشت؛
        // نتیجه: «طرح خوانده نمی‌شود» بدون هیچ سرنخی. اکنون پوشه‌های موجود هم فهرست می‌شوند.
        _logger.LogWarning(
            "هیچ قالب چاپی برای شرکت پیدا نشد. CompanyId={CompanyId}, DesignIds={DesignIds}, Size={Size}, " +
            "TemplateRoot={TemplateRoot}, SearchedRoots={SearchedRoots}, AvailableDesigns={AvailableDesigns}",
            companyId, string.Join(",", DesignCompanyIds(companyId)), size, templateRoot,
            string.Join(" | ", roots.Distinct()), DescribeAvailableDesigns(templateRoot, roots));
        return null;
    }

    /// <summary>
    /// یافتن فایل قالب داخل پوشهٔ طرح یک شرکت. اگر نام فایل مطابق استاندارد
    /// (Outgoing-A4.mrt / Outgoing-A5.mrt) نباشد، همان پوشه بررسی می‌شود تا فایلی که
    /// طراح با نام دیگری ذخیره کرده «بی‌صدا نادیده» گرفته نشود.
    /// </summary>
    private static string? FindTemplateFile(string folder, string size, out bool exactName)
    {
        exactName = true;
        if (!Directory.Exists(folder)) return null;

        var expectedName = $"Outgoing-{size}.mrt";
        var expected = Path.Combine(folder, expectedName);
        if (File.Exists(expected)) return expected;

        var files = Directory.EnumerateFiles(folder, "*.mrt")
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) return null;

        var byName = files.FirstOrDefault(file =>
            string.Equals(Path.GetFileName(file), expectedName, StringComparison.OrdinalIgnoreCase));
        if (byName != null) { exactName = false; return byName; }

        var bySize = files.FirstOrDefault(file =>
            Path.GetFileNameWithoutExtension(file).Contains(size, StringComparison.OrdinalIgnoreCase));
        if (bySize != null) { exactName = false; return bySize; }

        if (files.Length == 1) { exactName = false; return files[0]; }
        return null;
    }

    /// <summary>فهرست پوشه‌های طرح موجود (برای پیام خطای قابل‌فهم در لاگ).</summary>
    private static string DescribeAvailableDesigns(string templateRoot, string[] roots)
    {
        var designs = new List<string>();
        foreach (var root in roots.Distinct())
        {
            var directory = Path.IsPathRooted(templateRoot) ? templateRoot : Path.Combine(root, templateRoot);
            if (!Directory.Exists(directory)) continue;
            foreach (var companyDirectory in Directory.EnumerateDirectories(directory)
                         .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase))
            {
                var files = Directory.EnumerateFiles(companyDirectory, "*.mrt")
                    .Select(Path.GetFileName);
                designs.Add($"{Path.GetFileName(companyDirectory)}:[{string.Join(",", files)}] @ {companyDirectory}");
            }
        }
        return designs.Count == 0 ? "هیچ پوشهٔ طرحی پیدا نشد" : string.Join(" | ", designs);
    }

    /// <summary>خواندن Watermark.ImageBytes از فایل قالب MRT (XML استیمول‌سافت).</summary>
    private static byte[]? ReadTemplateWatermark(string templatePath)
    {
        using var stream = File.OpenRead(templatePath);
        var document = XDocument.Load(stream, LoadOptions.None);
        var watermark = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Watermark");
        var imageBytesValue = watermark?.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "ImageBytes")?.Value;
        if (string.IsNullOrWhiteSpace(imageBytesValue)) return null;
        return Convert.FromBase64String(imageBytesValue);
    }

    private static string ToPersianDigits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (chars[index] is >= '0' and <= '9')
                chars[index] = (char)('\u06F0' + (chars[index] - '0'));
            else if (chars[index] is >= '\u0660' and <= '\u0669')
                chars[index] = (char)('\u06F0' + (chars[index] - '\u0660'));
        }

        return new string(chars);
    }

    private static string FaDate(DateTime? date)
    {
        if (date is null) return "—";
        var calendar = new PersianCalendar();
        var value = date.Value;
        return ToPersianDigits($"{calendar.GetYear(value)}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}");
    }

    // ==================== تولید PDF ====================

    /// <summary>
    /// تولید PDF نامه صادره — در دو نسخهٔ «با رونوشت» و «بدون رونوشت».
    /// نسخهٔ بدون رونوشت برای تحویل به سازمان مقصد است؛ نسخهٔ با رونوشت
    /// در پروندهٔ دبیرخانه باقی می‌ماند.
    /// </summary>
    public async Task<byte[]?> GeneratePdfAsync(int letterId, string size, bool withCopy = true,
        bool withMargin = true, bool withLetterhead = true, bool requireSignatures = true)
    {
        // کلید کش باید «همهٔ» گزینه‌های مؤثر در خروجی را ببیند، وگرنه چاپ با/بدون سربرگ و
        // با/بدون امضا خروجی یکدیگر را از کش برمی‌داشت.
        var cacheKey = await BuildPrintCacheKeyAsync(letterId, size, withCopy, withMargin, withLetterhead, requireSignatures);
        if (cacheKey == null) return null;

        // رکورد پیش از شروع رندر در فرهنگ لغت منتشر می‌شود تا درخواست‌های هم‌زمان
        // (دوبار کلیک، تلاش مجدد پس از قطع مرورگر) همان رندر را await کنند.
        var (cached, startedHere) = RentRenderedPdf(cacheKey);
        if (!startedHere)
        {
            _logger.LogInformation(
                "چاپ نامه {LetterId}: خروجی آمادهٔ همین درخواست استفاده شد (کش {AgeMs}ms).",
                letterId, (int)(DateTime.UtcNow - cached.CreatedUtc).TotalMilliseconds);
            return await cached.Render;
        }

        try
        {
            var pdf = await GeneratePdfCoreAsync(letterId, size, withCopy, withMargin, withLetterhead, requireSignatures);
            if (pdf == null) RenderedPdfs.TryRemove(new KeyValuePair<string, CachedRender>(cacheKey, cached));
            cached.Complete(pdf);
            return pdf;
        }
        catch (Exception ex)
        {
            // خطا هرگز کش نمی‌شود؛ تلاش بعدی باید واقعاً دوباره رندر کند.
            RenderedPdfs.TryRemove(new KeyValuePair<string, CachedRender>(cacheKey, cached));
            cached.Fail(ex);
            throw;
        }
    }

    /// <summary>
    /// رکورد رندرِ این کلید را برمی‌گرداند؛ اگر تازه ساخته شد <c>StartedHere=true</c> است و فقط
    /// همان فراخوان مسئول رندر است. رکورد منقضی با مقایسهٔ خودِ نمونه حذف می‌شود تا رکورد تازهٔ
    /// درخواست دیگری پاک نشود.
    /// </summary>
    private static (CachedRender Render, bool StartedHere) RentRenderedPdf(string cacheKey)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (RenderedPdfs.TryGetValue(cacheKey, out var existing))
            {
                if (DateTime.UtcNow - existing.CreatedUtc < RenderedPdfLifetime)
                    return (existing, false);

                RenderedPdfs.TryRemove(new KeyValuePair<string, CachedRender>(cacheKey, existing));
            }

            PruneRenderedPdfs();
            var fresh = new CachedRender(DateTime.UtcNow);
            if (RenderedPdfs.TryAdd(cacheKey, fresh))
                return (fresh, true);
        }

        // رقابت شدید و غیرمنتظره: بدون کش رندر می‌کنیم تا درخواست کاربر معلق نماند.
        return (new CachedRender(DateTime.UtcNow), true);
    }

    /// <summary>پاک‌سازی دوره‌ای انقضاشده‌ها تا حافظه با هر چاپ جدید رشد نکند.</summary>
    private static void PruneRenderedPdfs()
    {
        if (RenderedPdfs.Count < 24) return;
        var now = DateTime.UtcNow;
        foreach (var entry in RenderedPdfs)
        {
            if (now - entry.Value.CreatedUtc >= RenderedPdfLifetime)
                RenderedPdfs.TryRemove(entry.Key, out _);
        }
    }

    /// <summary>
    /// اثر انگشت (hash) دادهٔ مؤثر در چاپ: خود نامه، امضاکنندگان، رونوشت‌ها و پیوست‌ها.
    /// با هر ویرایش نامه/امضا، کلید عوض می‌شود و خروجی کهنه چاپ نمی‌شود.
    /// </summary>
    private async Task<string?> BuildPrintCacheKeyAsync(int letterId, string size, bool withCopy, bool withMargin, bool withLetterhead, bool requireSignatures)
    {
        var row = await _db.OutgoingLetters.AsNoTracking()
            .Where(l => l.Id == letterId && !l.IsDelete)
            .Select(l => new
            {
                l.CompanyId,
                l.SadereNumber,
                l.LetterNumber,
                l.DateSadere,
                l.DateSabt,
                l.Title,
                l.Text,
                l.ReceiverOrganization,
                l.ReceiverName,
                l.ReceiverTitle,
                l.CopyTo,
                l.ExternalRefNumber,
                SignerCount = _db.OutgoingLetterSigners.Count(s => s.SourceId == l.Id && !s.IsDelete),
                SignedCount = _db.OutgoingLetterSigners.Count(s => s.SourceId == l.Id && !s.IsDelete && s.IsSigned),
                SignerOrderSum = _db.OutgoingLetterSigners.Where(s => s.SourceId == l.Id && !s.IsDelete).Sum(s => (int?)s.Order) ?? 0,
                CopyToCount = _db.OutgoingLetterCopyToes.Count(c => c.OutgoingLetterId == l.Id && !c.IsDelete),
                CopyToLastId = _db.OutgoingLetterCopyToes.Where(c => c.OutgoingLetterId == l.Id && !c.IsDelete).Max(c => (int?)c.Id) ?? 0,
                AttachmentCount = _db.AppAttachments.Count(a => a.Module == "OutgoingLetters" && a.RefId == l.Id)
            })
            .FirstOrDefaultAsync();

        if (row == null)
        {
            _logger.LogInformation("چاپ نامه {LetterId}: نامه پیدا نشد.", letterId);
            return null;
        }

        var material = string.Join('\u001f',
            letterId.ToString(CultureInfo.InvariantCulture),
            size.ToUpperInvariant(),
            withCopy ? "1" : "0", withMargin ? "1" : "0", withLetterhead ? "1" : "0", requireSignatures ? "1" : "0",
            row.CompanyId?.ToString(CultureInfo.InvariantCulture) ?? "-",
            row.SadereNumber, row.LetterNumber,
            row.DateSadere?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "-",
            row.DateSabt.Ticks.ToString(CultureInfo.InvariantCulture),
            row.Title, row.Text, row.ReceiverOrganization, row.ReceiverName, row.ReceiverTitle,
            row.CopyTo, row.ExternalRefNumber,
            row.SignerCount.ToString(CultureInfo.InvariantCulture),
            row.SignedCount.ToString(CultureInfo.InvariantCulture),
            row.SignerOrderSum.ToString(CultureInfo.InvariantCulture),
            row.CopyToCount.ToString(CultureInfo.InvariantCulture),
            row.CopyToLastId.ToString(CultureInfo.InvariantCulture),
            row.AttachmentCount.ToString(CultureInfo.InvariantCulture));

        return "outgoing-print-v2:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>رندر واقعی نامه (بدون کش) — همان پیاده‌سازی قبلی GeneratePdfAsync.</summary>
    private async Task<byte[]?> GeneratePdfCoreAsync(int letterId, string size, bool withCopy = true, bool withMargin = true, bool withLetterhead = true, bool requireSignatures = true)
    {
        var total = Stopwatch.StartNew();
        long lastStageMs = 0;
        void MarkStage(string stage)
        {
            var elapsed = total.ElapsedMilliseconds;
            _logger.LogInformation(
                "OutgoingLetterPrintTiming LetterId={LetterId} Stage={Stage} StageMs={StageMs} TotalMs={TotalMs}",
                letterId, stage, elapsed - lastStageMs, elapsed);
            lastStageMs = elapsed;
        }

        // One query returns the letter, its company record (when set), and attachment existence.
        var printData = await (
            from l in _db.OutgoingLetters.AsNoTracking()
            join c in _db.SystemCompanies.AsNoTracking() on l.CompanyId equals (int?)c.Id into companies
            from c in companies.DefaultIfEmpty()
            where l.Id == letterId && !l.IsDelete
            select new
            {
                Letter = l,
                Company = c,
                HasAttachment = _db.AppAttachments.AsNoTracking()
                    .Any(a => a.Module == "OutgoingLetters" && a.RefId == l.Id)
            }).FirstOrDefaultAsync();
        MarkStage("db-letter-company-attachments");
        if (printData == null)
        {
            MarkStage("complete-not-found");
            return null;
        }

        var letter = printData.Letter;
        var letterCompanyId = letter.CompanyId;
        var hasCompanyId = letterCompanyId is > 0;
        var company = printData.Company;
        var activeCompanies = await _db.SystemCompanies.AsNoTracking().Where(c => c.IsActive)
            .Select(c => new { c.Id, c.LetterheadFileName }).ToListAsync();
        if (!hasCompanyId)
        {
            // نامهٔ بدون شرکت (دادهٔ قدیمی): فقط وقتی مجاز به انتخاب خودکار هستیم که
            // «تنها یک شرکت فعال» وجود داشته باشد یا شرکت فعالِ درخواست مشخص باشد.
            // پیش‌تر اولین شرکتِ دارای سربرگ انتخاب می‌شد و روی نصب چندشرکتی، نامه روی
            // سربرگ شرکت دیگری چاپ می‌شد.
            var activeCompanyId = ActiveCompanyIdFromContext();
            var pickId = activeCompanies.Count == 1
                ? activeCompanies[0].Id
                : activeCompanyId > 0 && activeCompanies.Any(c => c.Id == activeCompanyId)
                    ? activeCompanyId
                    : 0;
            company = pickId > 0
                ? await _db.SystemCompanies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pickId)
                : null;
            MarkStage("db-default-company");
            _logger.LogWarning(
                "نامه {LetterId} CompanyId ندارد؛ شرکت انتخابی برای چاپ: {SelectedCompanyId} (شرکت‌های فعال: {ActiveCount}, شرکت فعال درخواست: {RequestCompanyId}).",
                letterId, company?.Id, activeCompanies.Count, activeCompanyId);
        }

        var selectedCompanyId = company?.Id ?? letterCompanyId;
        var companyRecordMissing = hasCompanyId && company == null;
        var isA5 = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase);

        var registeredPath = withLetterhead && !companyRecordMissing ? ResolveRegisteredLetterheadFile(company) : null;
        var templateLetterheadPath = withLetterhead && selectedCompanyId is > 0
            ? ExtractTemplateLetterhead(selectedCompanyId.Value, isA5)
            : null;
        // اولویت منبع سربرگ: پیش‌فرض = فایل ثبت‌شدهٔ شرکت، وگرنه طرح جاسازی‌شده در قالب MRT.
        // با Reporting:OutgoingLetters:PreferTemplateLetterhead=true طرح پوشهٔ
        // Reports/OutgoingLetters/Companies/{id} اولویت می‌گیرد (همان‌جا که طرح‌ها نگهداری می‌شوند).
        var preferTemplate = PreferTemplateLetterhead;
        var letterheadPath = preferTemplate && templateLetterheadPath != null
            ? templateLetterheadPath
            : registeredPath ?? templateLetterheadPath;
        if (withLetterhead && !preferTemplate && registeredPath != null && templateLetterheadPath != null)
            _logger.LogInformation(
                "فایل ثبت‌شدهٔ شرکت بر طرح قالب اولویت دارد. CompanyId={CompanyId}, RegisteredPath={RegisteredPath}, TemplatePath={TemplatePath}. " +
                "برای چاپ با طرح پوشهٔ Companies مقدار Reporting:OutgoingLetters:PreferTemplateLetterhead=true را تنظیم کنید " +
                "یا LetterheadFileName شرکت را خالی کنید.",
                selectedCompanyId, registeredPath, templateLetterheadPath);
        var letterheadFromTemplate = letterheadPath != null && letterheadPath == templateLetterheadPath;

        if (withLetterhead && letterheadPath == null && !hasCompanyId)
        {
            // فقط نصب‌های تک‌شرکتیِ بدون رکورد شرکت: هیچ شرکت دیگری وجود ندارد که
            // اشتباهاً سربرگش استفاده شود. در نصب چندشرکتی هرگز سربرگ نمی‌گذاریم و
            // پایین‌تر خطای روشن داده می‌شود.
            if (activeCompanies.Count == 0)
            {
                letterheadPath = ResolveLetterheadPath("uploads/letterheads/forough-letterhead.pdf");
                if (letterheadPath == null)
                {
                    var hrLogoPath = await _db.HrMainCompanies.AsNoTracking()
                        .Select(c => c.LogoPath)
                        .FirstOrDefaultAsync();
                    MarkStage("db-hr-letterhead-logo");
                    letterheadPath = ResolveLetterheadPath(hrLogoPath);
                }
            }
        }
        MarkStage("letterhead-resolution-cache");

        if (withLetterhead && letterheadPath == null && !hasCompanyId && activeCompanies.Count > 0)
        {
            _logger.LogError(
                "چاپ نامه {LetterId} متوقف شد: نامه به هیچ شرکتی متصل نیست و نصب چندشرکتی است (شرکت‌های فعال: {ActiveCount}).",
                letterId, activeCompanies.Count);
            throw new RequiredCompanyLetterheadPrintException(
                "این نامه به هیچ شرکتی متصل نیست و سامانه چند شرکت فعال دارد؛ برای جلوگیری از چاپ روی سربرگ شرکت دیگر، " +
                "ابتدا شرکتِ نامه را مشخص کنید (ویرایش نامه ← شرکت) یا از بالای صفحه شرکت درست را انتخاب و دوباره چاپ کنید.");
        }

        if (withLetterhead && letterheadPath == null && hasCompanyId)
        {
            var companyName = string.IsNullOrWhiteSpace(company?.Name)
                ? letterCompanyId?.ToString(CultureInfo.InvariantCulture) ?? "شرکت"
                : company!.Name;
            var configuredStatus = companyRecordMissing
                ? "رکورد شرکت در بانک اطلاعاتی پیدا نشد"
                : string.IsNullOrWhiteSpace(company?.LetterheadFileName)
                    ? "روی رکورد شرکت فایلی ثبت نشده است"
                    : TryResolveAnyPath(company?.LetterheadFileName) is { } existing
                        ? $"فایل ثبت‌شده یافت شد ({existing}) اما نوع قابل‌چاپ نیست؛ فقط PDF/تصویر مجاز است"
                        : "فایل ثبت‌شده روی سرور پیدا نشد";
            var message =
                $"سربرگ شرکت «{companyName}» پیدا نشد؛ چاپ متوقف شد تا نامه روی صفحهٔ سفید چاپ نشود. " +
                $"وضعیت: {configuredStatus}؛ قالب MRT شرکت یافت نشد یا سربرگ جاسازی‌شده ندارد. " +
                $"مسیرهای جستجوی قالب: {_env.ContentRootPath} | {AppContext.BaseDirectory}";
            _logger.LogError(
                "چاپ نامه {LetterId} متوقف شد: سربرگ شرکت {CompanyId} یافت نشد. Configured={Configured}, ContentRoot={ContentRoot}, BaseDirectory={BaseDirectory}, WebRoot={WebRoot}",
                letterId, selectedCompanyId, company?.LetterheadFileName,
                _env.ContentRootPath, AppContext.BaseDirectory, _env.WebRootPath);
            throw new RequiredCompanyLetterheadPrintException(message);
        }

        var signers = await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SourceId == letterId && !s.IsDelete)
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .ToListAsync();
        MarkStage("db-signers");

        var sematIds = signers.Where(s => s.SematId.HasValue).Select(s => s.SematId!.Value).Distinct().ToArray();
        var signerUserIds = signers.Select(s => s.UserId).Distinct().ToArray();
        var sematRows = sematIds.Length == 0 && signerUserIds.Length == 0
            ? new List<SematPrintTitle>()
            : await _db.Semats.AsNoTracking()
                .Where(s => !s.IsDelete &&
                    (sematIds.Contains(s.SematId) ||
                     (s.UserId != null && signerUserIds.Contains(s.UserId.Value) && s.IsActive)))
                .OrderByDescending(s => s.DefaultSemat).ThenBy(s => s.SematId)
                .Select(s => new SematPrintTitle(s.SematId, s.UserId, s.Title, s.OnvanMokatebati, s.DefaultSemat, s.IsActive))
                .ToListAsync();
        var sematTitles = sematRows.Where(s => sematIds.Contains(s.SematId))
            .GroupBy(s => s.SematId)
            .ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().CorrespondenceTitle) ? g.First().Title : g.First().CorrespondenceTitle!);
        var sematTitlesByUser = sematRows.Where(s => s.UserId.HasValue && s.IsActive)
            .GroupBy(s => s.UserId!.Value)
            .ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().CorrespondenceTitle) ? g.First().Title : g.First().CorrespondenceTitle!);
        MarkStage("db-semat-titles");

        var copyTos = withCopy
            ? await _db.OutgoingLetterCopyToes.AsNoTracking()
                .Where(c => c.OutgoingLetterId == letterId && !c.IsDelete)
                .OrderBy(c => c.RowNo).ThenBy(c => c.Id)
                .ToListAsync()
            : new List<OutgoingLetterCopyTo>();
        MarkStage("db-copy-to");

        var missingSignatures = new List<string>();
        foreach (var signer in signers)
        {
            var name = FullName(signer.User);
            if (!signer.IsSigned || string.IsNullOrWhiteSpace(signer.User?.SignaturePath) ||
                GetCachedSignatureSvg(signer.User.SignaturePath) == null)
                missingSignatures.Add(string.IsNullOrWhiteSpace(name) ? $"کاربر {signer.UserId}" : name);
        }
        MarkStage("signature-read-decode-cache");
        if (missingSignatures.Count > 0)
        {
            if (requireSignatures)
                throw new OutgoingSignaturePrintException(
                    $"چاپ امکان‌پذیر نیست؛ امضای این امضاکننده ثبت نشده یا تصویر امضای او وجود ندارد: {string.Join("، ", missingSignatures)}. " +
                    "این محدودیت مربوط به وضعیت امضای نامه است (نه دسترسی شما)؛ پس از تکمیل امضاها چاپ روی سربرگ انجام می‌شود.");

            // ایجادکننده/دریافت‌کنندهٔ نامه (کاربران غیر دبیرخانه) هم باید بتوانند نسخهٔ چاپی بگیرند؛
            // در این حالت جای امضای ناقص خالی/با نام و سِمَت چاپ می‌شود و چاپ متوقف نمی‌شود.
            _logger.LogWarning(
                "چاپ بدون اعتبارسنجی امضا انجام شد (کاربر غیر دبیرخانه). LetterId={LetterId} امضاکنندگان ناقص: {Missing}",
                letterId, string.Join("، ", missingSignatures));
        }

        _logger.LogInformation(
            "سربرگ نامه انتخاب شد. LetterId={LetterId}, CompanyId={CompanyId}, Path={LetterheadPath}, FromTemplate={FromTemplate}",
            letterId, selectedCompanyId, letterheadPath, letterheadFromTemplate);

        byte[] contentPdf;
        try
        {
            contentPdf = BuildContentPdf(
                letter, signers, sematTitles, sematTitlesByUser, printData.HasAttachment, isA5, withCopy, copyTos,
                hasLetterhead: letterheadPath != null,
                letterheadCompanyId: selectedCompanyId,
                withMargin: withMargin);
        }
        finally
        {
            MarkStage("questpdf-compose-and-render");
        }

        if (letterheadPath == null)
        {
            if (withLetterhead)
                _logger.LogWarning("سربرگ نامه پیدا نشد؛ PDF بدون سربرگ ساخته شد. LetterId={LetterId}", letterId);
            MarkStage("complete");
            return contentPdf;
        }

        try
        {
            byte[] overlaidPdf;
            try
            {
                overlaidPdf = OverlayOnLetterhead(contentPdf, letterheadPath, letterId);
            }
            finally
            {
                MarkStage("pdfsharp-letterhead-overlay");
            }
            MarkStage("complete");
            return overlaidPdf;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "قرار دادن سربرگ نامه ناموفق بود. LetterId={LetterId}, CompanyId={CompanyId}, Path={LetterheadPath}, FromTemplate={FromTemplate}",
                letterId, selectedCompanyId, letterheadPath, letterheadFromTemplate);

            if (templateLetterheadPath != null && !letterheadFromTemplate)
            {
                try
                {
                    var retriedPdf = OverlayOnLetterhead(contentPdf, templateLetterheadPath, letterId);
                    MarkStage("pdfsharp-template-fallback-overlay");
                    MarkStage("complete");
                    return retriedPdf;
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx,
                        "اعمال سربرگ قالب MRT شرکت هم ناموفق بود. LetterId={LetterId}, CompanyId={CompanyId}, Path={TemplatePath}",
                        letterId, selectedCompanyId, templateLetterheadPath);
                    ex = retryEx;
                }
            }

            if (hasCompanyId)
            {
                var companyName = string.IsNullOrWhiteSpace(company?.Name)
                    ? letterCompanyId?.ToString(CultureInfo.InvariantCulture) ?? "شرکت"
                    : company!.Name;
                throw new RequiredCompanyLetterheadPrintException(
                    $"اعمال سربرگ شرکت «{companyName}» ناموفق بود؛ چاپ متوقف شد تا نامه روی صفحهٔ سفید چاپ نشود. جزئیات: {ex.Message}", ex);
            }
            MarkStage("complete-overlay-failed");
            return contentPdf;
        }
    }

    private string? ResolveSignaturePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..", StringComparison.Ordinal)) return null;
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        return new[]
        {
            Path.Combine(webRoot, clean),
            Path.Combine(webRoot, "uploads", clean),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(AppContext.BaseDirectory, clean)
        }.FirstOrDefault(File.Exists);
    }

    private byte[]? TryReadSignature(string? relativePath)
    {
        var path = ResolveSignaturePath(relativePath);
        if (path == null) return null;
        var info = new FileInfo(path);
        var key = $"outgoing-signature-bytes:{path}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        return _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            entry.Size = info.Length;
            return File.ReadAllBytes(path);
        });
    }

    private SignatureSvgData? GetCachedSignatureSvg(string? relativePath)
    {
        var path = ResolveSignaturePath(relativePath);
        if (path == null) return null;
        var info = new FileInfo(path);
        var key = $"outgoing-signature-svg:{path}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        var cacheTimer = Stopwatch.StartNew();
        var cacheHit = _cache.TryGetValue(key, out CachedSignature? cached);
        if (!cacheHit)
        {
            cached = _cache.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
                entry.Size = Math.Max(info.Length, 1);
                return new CachedSignature(TryReadSignatureSvg(TryReadSignature(relativePath)));
            });
        }

        if (!cacheHit || cacheTimer.ElapsedMilliseconds >= 100)
            _logger.LogInformation(
                "OutgoingLetterPrintTiming Stage=signature-svg-cache CacheHit={CacheHit} InputBytes={InputBytes} SvgChars={SvgChars} StageMs={StageMs}",
                cacheHit, info.Length, cached?.Data?.Svg.Length ?? 0, cacheTimer.ElapsedMilliseconds);
        return cached?.Data;
    }

    /// <summary>
    /// تبدیل امضا به مسیرهای SVG بدون پس‌زمینه. این مسیر چاپی عمداً PNG را به‌عنوان
    /// Image XObject در PDF نمی‌گذارد تا مشکل SMask/alpha در PDFsharp و درایور چاپگر حذف شود.
    /// </summary>
    private static SignatureSvgData? TryReadSignatureSvg(byte[]? pngBytes)
    {
        if (pngBytes is not { Length: > 0 }) return null;
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(pngBytes);
            // Trim legacy uploads at render time too, so printing remains tight even before
            // the one-time disk migration has been run. Keep a four-pixel transparent border.
            var left = image.Width;
            var top = image.Height;
            var right = -1;
            var bottom = -1;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var px = row[x];
                        var inkAlpha = (int)((255 - Math.Min(px.R, Math.Min(px.G, px.B))) * px.A / 255);
                        if (inkAlpha < 24) continue;
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x);
                        bottom = Math.Max(bottom, y);
                    }
                }
            });
            if (right < left || bottom < top) return null;
            var crop = new SixLabors.ImageSharp.Rectangle(left, top, right - left + 1, bottom - top + 1);
            image.Mutate(ctx => ctx.Crop(crop).Pad(crop.Width + 8, crop.Height + 8, new Rgba32(0, 0, 0, 0)));
            // Resize only after trimming; otherwise a large transparent canvas would reduce ink resolution.
            if (image.Width > 1200 || image.Height > 480)
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max,
                    Size = new SixLabors.ImageSharp.Size(1200, 480),
                    Sampler = KnownResamplers.Lanczos3
                }));

            long red = 0, green = 0, blue = 0, weight = 0;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    foreach (var px in row)
                    {
                        var min = Math.Min(px.R, Math.Min(px.G, px.B));
                        var inkAlpha = (int)((255 - min) * px.A / 255);
                        if (inkAlpha < 24) continue; // eliminate white/near-white paper pixels
                        red += (long)px.R * inkAlpha;
                        green += (long)px.G * inkAlpha;
                        blue += (long)px.B * inkAlpha;
                        weight += inkAlpha;
                    }
                }
            });
            if (weight == 0) return null;

            // Eight opacity bands retain antialiased edges without relying on a raster alpha mask.
            // Classify every pixel once, then close/open horizontal runs as the band changes.
            // The previous implementation rescanned every row eight times (once per opacity band).
            var paths = Enumerable.Range(0, 9).Select(_ => new StringBuilder()).ToArray();
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    var activeLevel = 0;
                    var runStart = -1;
                    for (var x = 0; x <= row.Length; x++)
                    {
                        var pixelLevel = 0;
                        if (x < row.Length)
                        {
                            var px = row[x];
                            var min = Math.Min(px.R, Math.Min(px.G, px.B));
                            var alpha = (int)((255 - min) * px.A / 255);
                            if (alpha >= 24) pixelLevel = Math.Clamp((alpha + 15) / 32, 1, 8);
                        }

                        if (pixelLevel == activeLevel) continue;
                        if (activeLevel > 0 && runStart >= 0)
                        {
                            var length = x - runStart;
                            paths[activeLevel].Append('M').Append(runStart).Append(' ').Append(y)
                                .Append('h').Append(length).Append("v1h-").Append(length).Append('z');
                        }

                        activeLevel = pixelLevel;
                        runStart = pixelLevel > 0 ? x : -1;
                    }
                }
            });

            var color = $"#{(byte)(red / weight):X2}{(byte)(green / weight):X2}{(byte)(blue / weight):X2}";
            var svg = new StringBuilder(256);
            svg.Append("<svg xmlns='http://www.w3.org/2000/svg' width='").Append(image.Width)
                .Append("px' height='").Append(image.Height).Append("px' viewBox='0 0 ")
                .Append(image.Width).Append(' ').Append(image.Height).Append("'>");
            for (var level = 1; level <= 8; level++)
            {
                if (paths[level].Length == 0) continue;
                svg.Append("<path fill='").Append(color).Append("' fill-opacity='")
                    .Append((level / 8d).ToString("0.###", CultureInfo.InvariantCulture))
                    .Append("' d='").Append(paths[level]).Append("'/>");
            }
            svg.Append("</svg>");
            return new SignatureSvgData(svg.ToString(), image.Width, image.Height);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>ساخت محتوای نامه با RTL صریح، فونت وزیرمتن و سربرگ ثبت‌شده.</summary>
    private byte[] BuildContentPdf(
        OutgoingLetter letter,
        List<OutgoingLetterSigner> signers,
        IReadOnlyDictionary<int, string> sematTitles,
        IReadOnlyDictionary<int, string> sematTitlesByUser,
        bool hasAttachment,
        bool isA5,
        bool withCopy,
        List<OutgoingLetterCopyTo>? copyTos,
        bool hasLetterhead,
        int? letterheadCompanyId,
        bool withMargin)
    {
        var fontTimer = Stopwatch.StartNew();
        EnsureFonts();
        _logger.LogInformation(
            "OutgoingLetterPrintTiming LetterId={LetterId} Stage=font-registration-or-cache StageMs={StageMs}",
            letter.Id, fontTimer.ElapsedMilliseconds);

        var fontSize = isA5 ? 8f : 10.5f;
        var titleFontSize = isA5 ? 9.5f : 12f;
        var smallFontSize = isA5 ? 7f : 9f;
        var lineHeight = isA5 ? 1.3f : 1.35f;

        // The rich-text body is kept as sanitized HTML and rendered structurally below.

        var number = ToPersianDigits(
            string.IsNullOrWhiteSpace(letter.SadereNumber)
                ? letter.LetterNumber ?? "—"
                : letter.SadereNumber);
        var date = FaDate(letter.DateSadere ?? letter.DateSabt);
        var receiverOrganization = ToPersianDigits(letter.ReceiverOrganization);
        var receiverLine = receiverOrganization;
        if (!string.IsNullOrWhiteSpace(letter.ReceiverName))
        {
            var receiverTitle = string.IsNullOrWhiteSpace(letter.ReceiverTitle)
                ? string.Empty
                : ToPersianDigits(letter.ReceiverTitle) + " ";
            receiverLine += $" — {receiverTitle}{ToPersianDigits(letter.ReceiverName)}";
        }
        else if (!string.IsNullOrWhiteSpace(letter.ReceiverTitle))
        {
            receiverLine += $" — {ToPersianDigits(letter.ReceiverTitle)}";
        }

        var title = ToPersianDigits(letter.Title);
        var externalReference = ToPersianDigits(letter.ExternalRefNumber);
        // The layout follows the *design* (template folder), not the raw database id, so a
        // company mapped to another installation's folder gets that design's spacing too.
        var designCompanyId = letterheadCompanyId is int designSource ? TemplateDesignId(designSource) : 0;
        var isHomepaLetterhead = hasLetterhead && _homepaCompanyIds.Contains(designCompanyId);
        var isAriaLetterhead = hasLetterhead && _ariaCompanyIds.Contains(designCompanyId);
        var signerCards = signers.Select(s =>
        {
            var role = s.SematId is int sematId && sematTitles.TryGetValue(sematId, out var sematTitle)
                ? sematTitle
                : (sematTitlesByUser.TryGetValue(s.UserId, out var userSematTitle) ? userSematTitle : "سمت ثبت نشده");
            return (
                Name: ToPersianDigits(FullName(s.User)),
                Role: ToPersianDigits(role),
                Signature: s.IsSigned ? GetCachedSignatureSvg(s.User?.SignaturePath) : null);
        }).ToList();

        // ── بلوک امضا: هر نفر یک ستونِ مستقل، تصویر سپس نام و سمت با فاصلهٔ کم ──
        var signerBlockWidth = isA5 ? 150f : 220f;
        var maxSignatureWidth = isA5 ? 138f : 205f;
        var maxSignatureHeight = isA5 ? 68f : 72f; // 91–96 CSS px at 96 dpi
        var signerColumns = signerCards.Count == 1 ? 1 : 2;

        void AddSignerCard(IContainer container, (string Name, string Role, SignatureSvgData? Signature) signer)
        {
            container.Width(signerBlockWidth).Column(card =>
            {
                if (signer.Signature is { } signature)
                {
                    var scale = Math.Min(maxSignatureWidth / signature.Width, maxSignatureHeight / signature.Height);
                    var imageWidth = signature.Width * scale;
                    var imageHeight = signature.Height * scale;
                    card.Item().AlignCenter().Width(imageWidth).Height(imageHeight)
                        .Svg(signature.Svg).FitArea();
                    card.Item().PaddingTop(2).Text(signer.Name)
                        .FontFamily("Vazirmatn-Bold").FontSize(fontSize)
                        .LineHeight(1.2f).AlignCenter().DirectionFromRightToLeft();
                    card.Item().Text(signer.Role)
                        .FontFamily("Vazirmatn").FontSize(smallFontSize)
                        .LineHeight(1.2f).AlignCenter().DirectionFromRightToLeft();
                }
                else
                {
                    card.Item().Text(signer.Name)
                        .FontFamily("Vazirmatn-Bold").FontSize(fontSize)
                        .LineHeight(1.2f).AlignCenter().DirectionFromRightToLeft();
                    card.Item().Text(signer.Role)
                        .FontFamily("Vazirmatn").FontSize(smallFontSize)
                        .LineHeight(1.2f).AlignCenter().DirectionFromRightToLeft();
                }
            });
        }

        void AddSignerBlock(ColumnDescriptor target)
        {
            var signerTopPadding = isHomepaLetterhead ? (isA5 ? 4 : 6) : (isA5 ? 8 : 12);
            var tableTopPadding = isHomepaLetterhead ? (isA5 ? 2 : 3) : (isA5 ? 4 : 6);
            var cellPadding = isHomepaLetterhead ? 2 : 4;

            // Keep the complete signer block at the physical lower-left of the flowing letter
            // for every company; individual cards remain centered within their own columns.
            target.Item().PaddingTop(signerTopPadding)
                .AlignLeft().Width(signerBlockWidth * signerColumns)
                .Column(block =>
                {
                    block.Item().PaddingTop(tableTopPadding).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            for (var i = 0; i < signerColumns; i++)
                                columns.RelativeColumn();
                        });

                        foreach (var signer in signerCards)
                            table.Cell().Padding(cellPadding).ShowEntire().AlignCenter()
                                .Element(card => AddSignerCard(card, signer));
                    });
                });
        }

        void AddAriaField(IContainer container, AriaLetterheadLayout.Field field, string value,
            float fieldFontSize, bool isolateLtr = false)
        {
            // Place values in fixed page-coordinate boxes over the printed dotted fields.
            // ScaleToFit prevents long dates/numbers from extending beyond each line.
            var displayValue = isolateLtr ? $"\u2066{value}\u2069" : value;
            var box = container.PaddingLeft(field.Left).PaddingTop(field.Top)
                .Width(field.Width).Height(field.Height).AlignMiddle().AlignRight();
            var text = box.ScaleToFit().Text(displayValue)
                .FontFamily("Vazirmatn-Bold").FontSize(fieldFontSize)
                .FontColor("#666666").LineHeight(1.05f);
            if (isolateLtr)
                text.DirectionFromLeftToRight();
            else
                text.DirectionFromRightToLeft();
        }

        void AddHomepaField(IContainer container, HomepaLetterheadLayout.Field field, string value,
            float fieldFontSize, bool isolateLtr = false)
        {
            // Captions are part of the enhanced company artwork, so only values are painted.
            // Keep date/number runs isolated in LTR and preserve their fixed row alignment.
            var displayValue = isolateLtr ? $"\u2066{value}\u2069" : value;
            var box = container.PaddingLeft(field.Left).PaddingTop(field.Top)
                .Width(field.Width).Height(field.Height).AlignMiddle().AlignRight();
            var text = box.ScaleToFit().Text(displayValue)
                .FontFamily("Vazirmatn-Bold").FontSize(fieldFontSize)
                .FontColor(Colors.White).LineHeight(1.05f);
            if (isolateLtr)
                text.DirectionFromLeftToRight();
            else
                text.DirectionFromRightToLeft();
        }

        void AddDeclaredField(IContainer container, LetterheadMetaFields.Field field, string value,
            float pageHeaderOrigin)
        {
            // The company template stores physical page coordinates (page margin + ClientRectangle)
            // and the colour/size/alignment the designer set, so any company — not only the two
            // calibrated ones — prints its values on the labels of its own artwork.
            // page.Header() starts after Page.MarginTop, hence the subtraction.
            var left = Math.Max(0f, field.Left);
            var top = Math.Max(0f, field.Top - pageHeaderOrigin);
            var displayValue = field.IsolateLtr ? $"\u2066{value}\u2069" : value;
            var box = container.PaddingLeft(left).PaddingTop(top)
                .Width(field.Width).Height(field.Height).AlignMiddle();
            box = field.Center ? box.AlignCenter() : box.AlignRight();
            var text = box.ScaleToFit().Text(displayValue)
                .FontFamily(field.Bold ? "Vazirmatn-Bold" : "Vazirmatn").FontSize(field.FontSize)
                .FontColor(field.Color).LineHeight(1.05f);
            if (field.IsolateLtr)
                text.DirectionFromLeftToRight();
            else
                text.DirectionFromRightToLeft();
        }

        void AddSubjectAndFollowingContent(ColumnDescriptor col, float subjectTopPadding = 0f)
        {
            col.Spacing(isA5 ? 4 : 6);

            col.Item().PaddingTop(subjectTopPadding).Text($"موضوع: {title}")
                .FontFamily("Vazirmatn-Bold").FontSize(titleFontSize).LineHeight(lineHeight)
                .AlignRight().DirectionFromRightToLeft();

            if (!string.IsNullOrWhiteSpace(externalReference))
            {
                col.Item().Text($"عطف به شماره: {externalReference}")
                    .FontSize(smallFontSize).LineHeight(lineHeight)
                    .AlignRight().DirectionFromRightToLeft();
            }

            col.Item().PaddingVertical(isA5 ? 3 : 5)
                .LineHorizontal(0.7f).LineColor("#999999");

            var signersKeptWithTrailingParagraph = false;
            if (!string.IsNullOrWhiteSpace(letter.Text))
            {
                // On 1003, keep a short final paragraph with signatures if pagination would otherwise strand them.
                // Other companies retain their existing body flow and signature placement.
                signersKeptWithTrailingParagraph = isHomepaLetterhead && signerCards.Count > 0
                    ? RichLetterPdfRenderer.AddTo(col, letter.Text, fontSize, lineHeight, AddSignerBlock)
                    : RichLetterPdfRenderer.AddTo(col, letter.Text, fontSize, lineHeight);
            }

            if (signerCards.Count > 0 && !signersKeptWithTrailingParagraph)
            {
                if (isHomepaLetterhead)
                {
                    // Keep heading and all signer cards as one pagination unit, and compact only 1003's block.
                    col.Item().ShowEntire().Column(AddSignerBlock);
                }
                else
                {
                    AddSignerBlock(col);
                }
            }

            if (withCopy && ((copyTos?.Count ?? 0) > 0 || !string.IsNullOrWhiteSpace(letter.CopyTo)))
            {
                col.Item().PaddingTop(isA5 ? 8 : 12)
                    .LineHorizontal(0.5f).LineColor("#cbd5e1");
                col.Item().Text("رونوشت:")
                    .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                    .LineHeight(lineHeight).AlignRight().DirectionFromRightToLeft();

                if (copyTos is { Count: > 0 })
                {
                    foreach (var copyTo in copyTos)
                    {
                        var details = new[] { copyTo.Desc, copyTo.RefNo }
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(ToPersianDigits)
                            .ToArray();
                        var suffix = details.Length == 0 ? string.Empty : $" — {string.Join("، ", details)}";
                        col.Item().Text($"• {ToPersianDigits(copyTo.Name)}{suffix}")
                            .FontSize(smallFontSize).LineHeight(lineHeight)
                            .AlignRight().DirectionFromRightToLeft();
                    }
                }
                else
                {
                    col.Item().Text(ToPersianDigits(letter.CopyTo))
                        .FontSize(smallFontSize).LineHeight(lineHeight)
                        .AlignRight().DirectionFromRightToLeft();
                }
            }
        }

        // QuestPDF starts page.Header() after Page.MarginTop. The template coordinates are
        // physical page coordinates, so the value renderer has to know this origin.
        var pageTopMargin = isHomepaLetterhead || isAriaLetterhead
            ? (isA5 ? 16f : 24f)
            : (withMargin ? (isA5 ? 16f : 24f) : 0f);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(isA5 ? PageSizes.A5 : PageSizes.A4);
                // Keep the 1003/1005 header coordinates stable even when body margins are toggled;
                // all other companies retain their previous margin behavior.
                page.MarginTop(pageTopMargin);
                // QuestPDF defaults to an opaque white page. When this PDF is drawn OVER the
                // company background by PDFsharp, that white fill would hide the entire letterhead.
                page.PageColor(hasLetterhead ? Colors.Transparent : Colors.White);
                // Keep content clear of company 1003's lower artwork; leave other companies unchanged.
                page.MarginBottom(isHomepaLetterhead
                    ? HomepaLetterheadLayout.FooterReserve(isA5)
                    : hasLetterhead ? (isA5 ? 52 : 90) : withMargin ? (isA5 ? 18 : 28) : 0);
                if (isHomepaLetterhead)
                    page.Footer().Height(20).AlignTop().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(style => style
                            .FontFamily("Vazirmatn")
                            .FontSize(8)
                            .LineHeight(1.1f)
                            .FontColor("#475569")
                            .DirectionFromRightToLeft());
                        text.Span("صفحه ");
                        text.CurrentPageNumber().Format(number => ToPersianDigits(number?.ToString() ?? "0"));
                        text.Span(" از ");
                        text.TotalPages().Format(number => ToPersianDigits(number?.ToString() ?? "0"));
                    });
                page.MarginHorizontal(0);

                page.ContentFromRightToLeft();
                page.DefaultTextStyle(style => style
                    .FontFamily("Vazirmatn")
                    .FontSize(fontSize)
                    .LineHeight(lineHeight)
                    .DirectionFromRightToLeft());

                // This offset belongs only to the flowing body. The company 1003
                // metadata fields remain in the separately positioned page header.
                // QuestPDF repeats both regions on every page.
                var contentTopPadding = isHomepaLetterhead
                    ? HomepaLetterheadLayout.ReceiverTopPadding(isA5)
                    : isAriaLetterhead
                        ? AriaLetterheadLayout.BodyTopPadding(isA5)
                        : (withMargin || hasLetterhead ? 30f : 0f);

                if (isAriaLetterhead)
                {
                    // The artwork already contains all three captions and dotted lines.
                    // Paint each value in its own fixed box; these coordinates are independent
                    // of the body start offset and are repeated by QuestPDF on every page.
                    page.Header().ContentFromLeftToRight()
                        .Height(AriaLetterheadLayout.HeaderHeight(isA5))
                        .Layers(layers =>
                        {
                            layers.PrimaryLayer().Extend();
                            AddAriaField(layers.Layer(), AriaLetterheadLayout.Date(isA5), date,
                                AriaLetterheadLayout.FieldFontSize(isA5), isolateLtr: true);
                            AddAriaField(layers.Layer(), AriaLetterheadLayout.Number(isA5), number,
                                AriaLetterheadLayout.FieldFontSize(isA5), isolateLtr: true);
                            AddAriaField(layers.Layer(), AriaLetterheadLayout.Attachment(isA5),
                                hasAttachment ? "دارد" : "ندارد", AriaLetterheadLayout.FieldFontSize(isA5));
                        });
                }
                else if (isHomepaLetterhead)
                {
                    // Match company 1003's MRT rectangles, not the RTL body's default right edge.
                    // Header fields retain their fixed height/coordinates; only page.Content receives the larger gap.
                    page.Header().ContentFromLeftToRight()
                        .Height(HomepaLetterheadLayout.HeaderHeight(isA5))
                        .Layers(layers =>
                        {
                            layers.PrimaryLayer().Extend();
                            AddHomepaField(layers.Layer(), HomepaLetterheadLayout.Number(isA5), number,
                                HomepaLetterheadLayout.HeaderValueFontSize(isA5), isolateLtr: true);
                            AddHomepaField(layers.Layer(), HomepaLetterheadLayout.Date(isA5), date,
                                HomepaLetterheadLayout.HeaderValueFontSize(isA5), isolateLtr: true);
                            AddHomepaField(layers.Layer(), HomepaLetterheadLayout.Attachment(isA5),
                                hasAttachment ? "دارد" : "ندارد", HomepaLetterheadLayout.HeaderValueFontSize(isA5));
                        });
                }
                else if (hasLetterhead)
                {
                    // Company templates that declare number/date/attachment (rectangle, colour,
                    // font, alignment) drive the print themselves. The letterhead artwork is read
                    // from the same MRT, so the values land exactly on its printed labels — this is
                    // what makes companies other than the two calibrated ones print correctly
                    // (previously a hard-coded white column hid the values on light letterheads).
                    var declared = ReadDeclaredMetaFields(
                        letterheadCompanyId is > 0
                            ? FindCompanyTemplate(letterheadCompanyId.Value, isA5 ? "A5" : "A4")
                            : null);

                    if (declared != null)
                    {
                        _logger.LogDebug(
                            "مقادیر شماره/تاریخ/پیوست از قالب شرکت خوانده شد. CompanyId={CompanyId}, Size={Size}, Color={Color}, FontSize={FontSize}",
                            letterheadCompanyId, isA5 ? "A5" : "A4", declared.Number.Color, declared.Number.FontSize);

                        page.Header().ContentFromLeftToRight()
                            .Height(isA5 ? 150 : 160)
                            .Layers(layers =>
                            {
                                layers.PrimaryLayer().Extend();
                                AddDeclaredField(layers.Layer(), declared.Number, number, pageTopMargin);
                                AddDeclaredField(layers.Layer(), declared.Date, date, pageTopMargin);
                                AddDeclaredField(layers.Layer(), declared.Attachment,
                                    hasAttachment ? "دارد" : "ندارد", pageTopMargin);
                            });
                    }
                    else
                    {
                        // No declared geometry in the company template: preserve the previous layout.
                        page.Header()
                            .Height(isA5 ? 150 : 160)
                            .PaddingTop(isA5 ? 40 : 60)
                            .PaddingLeft(isA5 ? 7 : 14)
                            .Column(meta =>
                            {
                                meta.Spacing(isA5 ? 4 : 6);
                                meta.Item().Width(isA5 ? 59 : 82).Text(number)
                                    .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                    .FontColor(Colors.White).LineHeight(1.3f)
                                    .AlignRight().DirectionFromRightToLeft();
                                meta.Item().Width(isA5 ? 59 : 82).Text(date)
                                    .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                    .FontColor(Colors.White).LineHeight(1.3f)
                                    .AlignRight().DirectionFromRightToLeft();
                                meta.Item().Width(isA5 ? 59 : 82).Text(hasAttachment ? "دارد" : "ندارد")
                                    .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                    .FontColor(Colors.White).LineHeight(1.3f)
                                    .AlignRight().DirectionFromRightToLeft();
                            });
                    }
                }
                else
                {
                    page.Header()
                        .Height(isA5 ? 150 : 160)
                        .PaddingTop(isA5 ? 40 : 60)
                        .PaddingHorizontal(isA5 ? 48 : 68)
                        .Column(meta =>
                        {
                            meta.Spacing(isA5 ? 4 : 6);
                            meta.Item().AlignRight().Text($"شماره: {number}")
                                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                .FontColor(Colors.Black).LineHeight(1.3f)
                                .DirectionFromRightToLeft();
                            meta.Item().AlignRight().Text($"تاریخ: {date}")
                                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                .FontColor(Colors.Black).LineHeight(1.3f)
                                .DirectionFromRightToLeft();
                            meta.Item().AlignRight().Text($"پیوست: {(hasAttachment ? "دارد" : "ندارد")}")
                                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                .FontColor(Colors.Black).LineHeight(1.3f)
                                .DirectionFromRightToLeft();
                        });
                }

                // Header values live in page.Header with fixed template coordinates.
                // For company 1003, keep the recipient at its existing top offset and
                // add the extra space only before subject/body and all following items.
                // Keep the PDF content transparent on 1003 so the per-page letterhead image remains visible beneath it.
                var contentContainer = isHomepaLetterhead
                    ? page.Content().Background(Colors.Transparent)
                    : page.Content();
                contentContainer.PaddingTop(contentTopPadding)
                    .PaddingHorizontal(withMargin ? (isA5 ? 48 : 68) : 0).Column(col =>
                {
                    col.Spacing(isA5 ? 4 : 6);
                    col.Item().Text($"به: {receiverLine}")
                        .FontFamily("Vazirmatn").FontSize(fontSize).LineHeight(lineHeight)
                        .AlignRight().DirectionFromRightToLeft();
                    AddSubjectAndFollowingContent(col,
                        isHomepaLetterhead ? HomepaLetterheadLayout.SubjectBodyGap(isA5) : 0f);
                });
            });
        });

        var renderTimer = Stopwatch.StartNew();
        var generatedPdf = document.GeneratePdf();
        _logger.LogInformation(
            "OutgoingLetterPrintTiming LetterId={LetterId} Stage=questpdf-generate-pdf StageMs={StageMs} Bytes={Bytes}",
            letter.Id, renderTimer.ElapsedMilliseconds, generatedPdf.Length);
        return generatedPdf;
    }

    private static string FullName(User? user)
    {
        if (user == null) return string.Empty;
        var value = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(value) ? user.Username : value;
    }

    /// <summary>قرار دادن صفحات نامه روی سربرگ یا لوگوی شرکت (PDF یا تصویر)</summary>
    private byte[] OverlayOnLetterhead(byte[] contentPdf, string letterheadPath, int letterId)
    {
        using var contentStream = new MemoryStream(contentPdf);
        using var contentDoc = PdfReader.Open(contentStream, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();

        var fullPath = Path.GetFullPath(letterheadPath);
        var info = new FileInfo(fullPath);
        var cacheKey = $"{fullPath}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        var assetTimer = Stopwatch.StartNew();
        var asset = LetterheadAssets.GetOrAdd(cacheKey, _ => new Lazy<CachedLetterheadAsset>(() =>
        {
            var isPdf = fullPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            return isPdf
                ? new CachedLetterheadAsset(true, XPdfForm.FromFile(fullPath), null)
                : new CachedLetterheadAsset(false, null, XImage.FromFile(fullPath));
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        _logger.LogInformation(
            "OutgoingLetterPrintTiming LetterId={LetterId} Stage=letterhead-image-pdf-cache-open StageMs={StageMs} AssetType={AssetType}",
            letterId, assetTimer.ElapsedMilliseconds, asset.IsPdf ? "PDF" : "Image");

        using var contentFormStream = new MemoryStream(contentPdf);
        using var contentForm = XPdfForm.FromStream(contentFormStream);

        // PDFsharp forms have mutable page selection. Serialize drawing through this cached
        // source while reusing its decoded pages/pixels across concurrent print requests.
        lock (asset.DrawLock)
        {
            for (var index = 0; index < contentDoc.PageCount; index++)
            {
                var sourcePage = contentDoc.Pages[index];
                var page = output.AddPage();
                page.Width = sourcePage.Width;
                page.Height = sourcePage.Height;

                using var graphics = XGraphics.FromPdfPage(page);
                var bounds = new XRect(0, 0, page.Width.Point, page.Height.Point);
                if (asset.IsPdf && asset.Pdf != null)
                {
                    asset.Pdf.PageNumber = 1;
                    graphics.DrawImage(asset.Pdf, bounds);
                }
                else if (asset.Image != null)
                {
                    graphics.DrawImage(asset.Image, bounds);
                }

                contentForm.PageNumber = index + 1;
                graphics.DrawImage(contentForm, bounds);
            }
        }

        using var outputStream = new MemoryStream();
        output.Save(outputStream);
        return outputStream.ToArray();
    }
}
