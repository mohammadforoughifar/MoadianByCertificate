using System.Data;
using System.Drawing;
using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using Stimulsoft.Report;
using Stimulsoft.Report.Dictionary;

namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// رندر سروری قالب‌های قدیمی Stimulsoft برای نامه صادره.
/// قالب هر شرکت از مسیر Reports/OutgoingLetters/Companies/{CompanyId} خوانده می‌شود
/// و هیچ اتصال مستقیمی از MRT به SQL Server انجام نمی‌شود.
/// </summary>
public interface IStimulsoftOutgoingLetterPrintService
{
    /// <returns>PDF یا null اگر موتور غیرفعال/قالب ناموجود/رندر ناموفق باشد.</returns>
    Task<byte[]?> TryGeneratePdfAsync(int letterId, string size, bool withCopy = true, CancellationToken cancellationToken = default);
}

public sealed class StimulsoftOutgoingLetterPrintService : IStimulsoftOutgoingLetterPrintService
{
    private sealed class HameshPrintRow
    {
        public string Name { get; set; } = string.Empty;
        public string Desc { get; set; } = string.Empty;
    }

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly FileStore _files;
    private readonly ILogger<StimulsoftOutgoingLetterPrintService> _logger;
    private static int _fontsRegistered;

    public StimulsoftOutgoingLetterPrintService(
        AppDbContext db,
        IWebHostEnvironment env,
        IConfiguration configuration,
        FileStore files,
        ILogger<StimulsoftOutgoingLetterPrintService> logger)
    {
        _db = db;
        _env = env;
        _configuration = configuration;
        _files = files;
        _logger = logger;
    }

    public async Task<byte[]?> TryGeneratePdfAsync(
        int letterId,
        string size,
        bool withCopy = true,
        CancellationToken cancellationToken = default)
    {
        if (!_configuration.GetValue("Reporting:Stimulsoft:Enabled", true))
            return null;

        size = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase) ? "A5" : "A4";

        var letter = await _db.OutgoingLetters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == letterId && !x.IsDelete, cancellationToken);
        if (letter == null) return null;

        // قالب دارای سربرگ اختصاصی است؛ بدون شرکت نباید قالب شرکت دیگری انتخاب شود.
        if (letter.CompanyId is not > 0)
        {
            _logger.LogWarning("چاپ MRT انجام نشد: نامه {LetterId} شرکت صادرکننده ندارد.", letterId);
            return null;
        }

        var company = await _db.SystemCompanies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == letter.CompanyId && x.IsActive, cancellationToken);
        if (company == null)
        {
            _logger.LogWarning("چاپ MRT انجام نشد: شرکت {CompanyId} نامه {LetterId} پیدا نشد یا غیرفعال است.", letter.CompanyId, letterId);
            return null;
        }

        var templatePath = ResolveTemplatePath(company.Id, size);
        if (templatePath == null)
        {
            _logger.LogWarning(
                "قالب MRT پیدا نشد. LetterId={LetterId}, CompanyId={CompanyId}, Size={Size}. مسیر مورد انتظار: Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{Size}.mrt",
                letterId, company.Id, size, company.Id, size);
            return null;
        }

        var signers = await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.SourceId == letterId && !x.IsDelete)
            .OrderBy(x => x.Order).ThenBy(x => x.Id)
            .Take(3)
            .ToListAsync(cancellationToken);

        var sematIds = signers.Where(x => x.SematId.HasValue)
            .Select(x => x.SematId!.Value).Distinct().ToList();
        var sematTitles = await _db.Semats.AsNoTracking()
            .Where(x => sematIds.Contains(x.SematId) && !x.IsDelete)
            .ToDictionaryAsync(
                x => x.SematId,
                x => string.IsNullOrWhiteSpace(x.OnvanMokatebati) ? x.Title : x.OnvanMokatebati!,
                cancellationToken);

        var hasAttachment = await _db.AppAttachments.AsNoTracking()
            .AnyAsync(x => x.Module == "OutgoingLetters" && x.RefId == letterId, cancellationToken);

        // هامش/رونوشت نامه صادره از جدول مستقل CopyTo خوانده می‌شود.
        // Erja برای گردش داخلی نامه است و منبع هامش چاپ نامه صادره نیست.
        var hamesh = withCopy
            ? await _db.OutgoingLetterCopyToes.AsNoTracking()
                .Where(x => x.OutgoingLetterId == letterId && !x.IsDelete)
                .OrderBy(x => x.RowNo).ThenBy(x => x.Id)
                .Take(5)
                .Select(x => new HameshPrintRow
                {
                    Name = x.Name ?? string.Empty,
                    Desc = x.Desc ?? string.Empty
                })
                .ToListAsync(cancellationToken)
            : new List<HameshPrintRow>();

        try
        {
            RegisterStimulsoftFonts();
            using var report = new StiReport();
            report.Load(templatePath);

            // اتصال رمزگذاری‌شده قدیمی A5 هرگز نباید در نسخه وب استفاده شود.
            report.Dictionary.Databases.Clear();

            RegisterLetterData(report, letter, hamesh, hasAttachment);
            var persianDate = ToPersianDate(letter.DateSadere ?? letter.DateSabt);
            SetVariable(report, "dateshams", persianDate);
            SetVariable(report, "dateshamsi", persianDate);
            SetVariable(report, "peyvast_String", hasAttachment ? "دارد" : "ندارد");
            // برای قالب‌های قدیمی، هامش متنی نیز مقداردهی می‌شود.
            SetVariable(report, "hamesh", string.Join(Environment.NewLine,
                hamesh.Select(x => string.IsNullOrWhiteSpace(x.Desc) ? x.Name : $"{x.Name}: {x.Desc}")));

            for (var index = 0; index < 3; index++)
            {
                var signer = index < signers.Count ? signers[index] : null;
                var suffix = index == 0 ? string.Empty : (index + 1).ToString(CultureInfo.InvariantCulture);
                SetVariable(report, $"Name{index + 1}", signer == null ? string.Empty : FullName(signer.User));
                SetVariable(report, $"SematTitle{index + 1}",
                    signer?.SematId is int sematId && sematTitles.TryGetValue(sematId, out var title)
                        ? title
                        : string.Empty);
                SetVariable(report, $"ImageSignature{suffix}", null);
            }

            // فقط تصویر امضای واقعی امضاکننده‌ای که امضای او ثبت شده چاپ می‌شود.
            for (var index = 0; index < signers.Count; index++)
            {
                var signer = signers[index];
                if (!signer.IsSigned || string.IsNullOrWhiteSpace(signer.User?.SignaturePath)) continue;
                var signatureBytes = _files.ReadBytes(signer.User.SignaturePath);
                if (signatureBytes is not { Length: > 0 }) continue;
                var suffix = index == 0 ? string.Empty : (index + 1).ToString(CultureInfo.InvariantCulture);
                using var signatureStream = new MemoryStream(signatureBytes);
                using var source = Image.FromStream(signatureStream);
                SetVariable(report, $"ImageSignature{suffix}", new Bitmap(source));
            }

            report.Render(false);
            using var output = new MemoryStream();
            report.ExportDocument(StiExportFormat.Pdf, output);

            _logger.LogInformation(
                "نامه {LetterId} با Stimulsoft و قالب {Template} رندر شد.", letterId, templatePath);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "رندر Stimulsoft ناموفق بود. LetterId={LetterId}, CompanyId={CompanyId}, Size={Size}, Template={Template}",
                letterId, company.Id, size, templatePath);
            return null;
        }
    }

    private void RegisterStimulsoftFonts()
    {
        if (Interlocked.Exchange(ref _fontsRegistered, 1) != 0) return;
        var root = Path.Combine(AppContext.BaseDirectory, "Resources", "fonts");
        foreach (var file in new[] { "Vazirmatn-Regular.ttf", "Vazirmatn-Bold.ttf" })
        {
            var path = Path.Combine(root, file);
            if (File.Exists(path))
                Stimulsoft.Base.StiFontCollection.AddFontFile(path);
        }
    }

    private void RegisterLetterData(StiReport report, OutgoingLetter letter, IReadOnlyList<HameshPrintRow> hamesh, bool hasAttachment)
    {
        var table = new DataTable("DataSource1");
        table.Columns.Add("LetterNumber", typeof(string));
        table.Columns.Add("Date_Letter", typeof(DateTime));
        table.Columns.Add("TextLetter", typeof(string));
        table.Columns.Add("TitleLetter", typeof(string));
        table.Columns.Add("GirandeAsli", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Desc", typeof(string));
        table.Columns.Add("DateShams", typeof(string));
        table.Columns.Add("Peyvast", typeof(string));

        var number = string.IsNullOrWhiteSpace(letter.SadereNumber)
            ? letter.LetterNumber ?? "—"
            : letter.SadereNumber;

        table.Rows.Add(
            number,
            letter.DateSadere ?? letter.DateSabt,
            letter.Text ?? string.Empty,
            letter.Title,
            BuildReceiver(letter),
            string.Join(Environment.NewLine, hamesh.Select((x, index) => $"{index + 1}. {x.Name}")),
            string.Join(Environment.NewLine, hamesh.Select((x, index) => $"{index + 1}. {x.Desc}")),
            ToPersianDate(letter.DateSadere ?? letter.DateSabt),
            hasAttachment ? "دارد" : "ندارد");

        var dataSet = new DataSet("LetterData");
        dataSet.Tables.Add(table);

        // نام NameInSource در دو MRT قدیمی متفاوت بود؛ در زمان اجرا یکسان‌سازی می‌شود.
        // کالکشن DataSources نوع پایه StiDataSource برمی‌گرداند؛ NameInSource فقط
        // روی StiDataTableSource وجود دارد، بنابراین cast صریح لازم است.
        if (report.Dictionary.DataSources["DataSource1"] is StiDataTableSource source)
            source.NameInSource = "LetterData.DataSource1";

        report.RegData("LetterData", dataSet);
    }

    private string? ResolveTemplatePath(int companyId, string size)
    {
        var root = _configuration["Reporting:Stimulsoft:TemplateRoot"]
                   ?? "Reports/OutgoingLetters/Companies";
        return ResolveFile(Path.Combine(root, companyId.ToString(CultureInfo.InvariantCulture), $"Outgoing-{size}.mrt"));
    }

    private string? ResolveUserFile(string relativePath)
    {
        var clean = relativePath.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (clean.Split(Path.DirectorySeparatorChar).Contains("..")) return null;
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        return new[]
        {
            Path.Combine(webRoot, clean),
            Path.Combine(webRoot, "uploads", clean),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(AppContext.BaseDirectory, clean)
        }.FirstOrDefault(File.Exists);
    }

    private string? ResolveFile(string relativePath)
    {
        var clean = relativePath.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (clean.Split(Path.DirectorySeparatorChar).Contains("..")) return null;

        var candidates = new[]
        {
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(AppContext.BaseDirectory, clean)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void SetVariable(StiReport report, string name, object? value)
    {
        if (report.Dictionary.Variables[name] != null)
            report[name] = value;
    }

    private static string BuildReceiver(OutgoingLetter letter)
    {
        var parts = new[] { letter.ReceiverOrganization, letter.ReceiverTitle, letter.ReceiverName }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join(" - ", parts);
    }

    private static string FullName(User? user)
    {
        if (user == null) return string.Empty;
        var value = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(value) ? user.Username : value;
    }

    private static string ToPersianDate(DateTime value)
    {
        var calendar = new PersianCalendar();
        return $"{calendar.GetYear(value):0000}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}";
    }
}
