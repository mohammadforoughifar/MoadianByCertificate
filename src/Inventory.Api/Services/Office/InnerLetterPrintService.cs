using System.Globalization;
using Inventory.Api.Data;
using Inventory.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Inventory.Api.Services.Office;

public interface IInnerLetterPrintService
{
    Task<byte[]?> GeneratePdfAsync(int letterId, string size, int userId, CancellationToken ct = default, bool withHamesh = false);
}

public sealed class InnerLetterPrintService : IInnerLetterPrintService
{
    private readonly AppDbContext _db;
    private readonly FileStore _files;
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<InnerLetterPrintService> _log;

    public InnerLetterPrintService(AppDbContext db, FileStore files, IWebHostEnvironment env, IHttpContextAccessor http, ILogger<InnerLetterPrintService> log)
    {
        _db = db; _files = files; _env = env; _http = http; _log = log;
    }

    public async Task<byte[]?> GeneratePdfAsync(int letterId, string size, int userId, CancellationToken ct = default, bool withHamesh = false)
    {
        size = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase) ? "A5" : "A4";
        var letter = await _db.InnerLetters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == letterId && !x.IsDelete, ct);
        if (letter == null) return null;
        if (!await _db.Erjas.AsNoTracking().AnyAsync(x => x.SourceId == letterId && x.ReciverUserId == userId && !x.IsDelete, ct)
            && letter.CreatorUserId != userId)
            return null;

        var receivers = await _db.Erjas.AsNoTracking()
            .Where(x => x.SourceId == letterId && !x.IsDelete && (x.Type == "گیرنده" || x.Type == "ارجاع"))
            .OrderBy(x => x.ErjaId)
            .Select(x => new
            {
                Name = x.UserReciver == null ? "" : ((x.UserReciver.FirstName ?? "") + " " + (x.UserReciver.LastName ?? "")).Trim(),
                ReferralDate = x.Date
            }).ToListAsync(ct);
        var creator = await _db.Users.AsNoTracking().Where(x => x.Id == letter.CreatorUserId)
            .Select(x => ((x.FirstName ?? "") + " " + (x.LastName ?? "")).Trim()).FirstOrDefaultAsync(ct) ?? "";
        var hasAttachment = await _db.AppAttachments.AsNoTracking().AnyAsync(x => x.Module == "InnerLetters" && x.RefId == letterId, ct);
        var companyId = await ActiveCompanyIdAsync(userId, ct);
        var company = companyId is int cid
            ? await _db.SystemCompanies.AsNoTracking().Where(x => x.Id == cid && x.IsActive)
                .Select(x => new { x.Id, x.Code, x.Name, x.LetterheadFileName }).FirstOrDefaultAsync(ct)
            : null;

        // Print actual hamesh referrals, not signatures/private answers or every circulation row.
        IReadOnlyList<InnerLetterHameshRow> hamesh = Array.Empty<InnerLetterHameshRow>();
        if (withHamesh)
            hamesh = await _db.Erjas.AsNoTracking()
                .Where(x => x.SourceId == letterId && !x.IsDelete && x.Type == "هامش")
                .OrderBy(x => x.ErjaId)
                .Select(x => new InnerLetterHameshRow(
                    x.UserReciver == null ? "" : ((x.UserReciver.FirstName ?? "") + " " + (x.UserReciver.LastName ?? "")).Trim(),
                    x.MatnErja, x.Amalgar == null ? "" : x.Amalgar.Title)).ToListAsync(ct);
        var calendar = new PersianCalendar();
        var referrals = string.Join("، ", receivers.Select(x =>
            $"{calendar.GetYear(x.ReferralDate):0000}/{calendar.GetMonth(x.ReferralDate):00}/{calendar.GetDayOfMonth(x.ReferralDate):00}"));
        var data = new InnerLetterPrintData(letter.LetterNumber ?? "—", letter.DateSabt, letter.Title,
            letter.Text ?? "", string.Join("، ", receivers.Select(x => x.Name)), creator, hamesh, referrals);
        var a5 = size == "A5";
        var fonts = new[] { Path.Combine(AppContext.BaseDirectory, "Resources", "fonts"), Path.Combine(_env.ContentRootPath, "Resources", "fonts") }
            .FirstOrDefault(p => File.Exists(Path.Combine(p, "Vazirmatn-Regular.ttf")))
            ?? throw new InnerLetterPrintException("فونت فارسی چاپ پیدا نشد؛ پوشهٔ Resources/fonts را همراه API منتشر کنید.");
        ct.ThrowIfCancellationRequested();

        // ==================== انتخاب سربرگ ====================
        // قاعدهٔ سخت: سربرگ همیشه از «همان شرکت» گرفته می‌شود و هیچ‌گاه سربرگ شرکت دیگری
        // جانشین نمی‌شود. پیش‌تر شناسه‌های ۱۰۰۳ و ۱۰۰۵ در کد ثابت بودند و در نبود سربرگ،
        // فایل نمونهٔ «forough-letterhead.pdf» به‌صورت بی‌صدا استفاده می‌شد؛ نتیجه این بود
        // که روی نصب مشتری، نامه‌ها روی سربرگ شرکتی دیگر چاپ می‌شدند.
        var companyArt = company is null ? null
            : InnerLetterheadTemplates.TryLoadForCompany(_env.ContentRootPath, AppContext.BaseDirectory, company.Id, a5);
        if (companyArt != null)
        {
            try
            {
                var pdf = InnerLetterPdfRenderer.Render(data, a5, withHamesh, companyArt, hasAttachment, fonts);
                _log.LogInformation(
                    "چاپ نامه داخلی روی قالب اختصاصی شرکت. LetterId={LetterId}, CompanyId={CompanyId}, Template={Template}",
                    letterId, company!.Id, InnerLetterheadTemplates.CompanyTemplatePath(_env.ContentRootPath, company!.Id, a5));
                return pdf;
            }
            catch (Exception ex)
            {
                throw new InnerLetterPrintException(
                    $"تولید PDF نامه داخلی روی سربرگ شرکت «{company!.Name}» ناموفق بود؛ چاپ بدون سربرگ انجام نشد.", ex);
            }
        }

        var header = company?.LetterheadFileName;
        var headerPath = ResolveLetterheadPath(header);

        // نصب‌های تک‌شرکتیِ قدیمی که هیچ رکورد شرکتی ندارند: تنها جایی که استفاده از
        // سربرگ پیش‌فرض مجاز است، چون شرکت دیگری در سیستم وجود ندارد.
        if (headerPath == null && company is null)
        {
            var anyActiveCompany = await _db.SystemCompanies.AsNoTracking().AnyAsync(x => x.IsActive, ct);
            if (!anyActiveCompany)
                headerPath = ResolveLetterheadPath("uploads/letterheads/forough-letterhead.pdf");
        }

        var content = InnerLetterPdfRenderer.Render(data, a5, withHamesh, null, hasAttachment, fonts);

        if (headerPath == null)
        {
            if (company is not null)
            {
                // شرکت مشخص است ولی سربرگش تنظیم نشده — عمداً خطا می‌دهیم تا نامه روی
                // سربرگ شرکت دیگر یا صفحهٔ سفید چاپ نشود.
                var status = string.IsNullOrWhiteSpace(header)
                    ? "روی رکورد شرکت فایلی ثبت نشده است"
                    : $"فایل ثبت‌شده یافت نشد: {header}";
                _log.LogError(
                    "چاپ نامه داخلی متوقف شد: سربرگ شرکت {CompanyId} ({CompanyName}) تنظیم نشده. Status={Status}",
                    company!.Id, company!.Name, status);
                throw new InnerLetterPrintException(
                    $"سربرگ شرکت «{company!.Name}» تنظیم نشده است؛ چاپ متوقف شد تا نامه روی سربرگ شرکت دیگری چاپ نشود. " +
                    $"وضعیت: {status}. از مسیر «سیستم ← شرکت‌ها» فایل PDF سربرگ این شرکت را بارگذاری کنید.");
            }
            _log.LogWarning("سربرگ نامه داخلی پیدا نشد. LetterId={LetterId}, CompanyId={CompanyId}, File={File}", letterId, companyId, header);
            return content;
        }

        try
        {
            _log.LogInformation("چاپ نامه داخلی روی سربرگ شرکت. LetterId={LetterId}, CompanyId={CompanyId}, File={File}",
                letterId, companyId, Path.GetFileName(headerPath));
            return Overlay(content, headerPath);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "چاپ نامه داخلی روی سربرگ ناموفق بود. LetterId={LetterId}", letterId);
            if (company is not null)
                throw new InnerLetterPrintException(
                    $"چاپ نامه روی سربرگ شرکت «{company!.Name}» ناموفق بود؛ فایل سربرگ را بررسی کنید.", ex);
            return content;
        }
    }

    private string? ResolveLetterheadPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..", StringComparison.Ordinal)) return null;
        var noUploads = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase) ? clean["uploads/".Length..] : clean;
        var candidates = new[]
        {
            _files.ToFull(clean), _files.ToFull(noUploads),
            Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), clean),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(AppContext.BaseDirectory, clean),
            Path.Combine(AppContext.BaseDirectory, Path.GetFileName(clean))
        };
        return candidates.FirstOrDefault(x => x != null && File.Exists(x));
    }

    private async Task<int?> ActiveCompanyIdAsync(int userId, CancellationToken ct)
    {
        // Middleware handles membership, legacy-user company and admin fallback. Never trust raw header alone.
        var context = _http.HttpContext;
        if (context != null && context.Items.TryGetValue(ActiveCompanyMiddleware.ItemKey, out var resolved) && resolved is int selected)
        {
            if (selected <= 0) throw new UnauthorizedAccessException("به شرکت انتخاب‌شده برای چاپ دسترسی ندارید.");
            return selected;
        }
        var raw = _http.HttpContext?.Request.Headers[ActiveCompanyMiddleware.HeaderName].FirstOrDefault();
        int.TryParse(raw, out var requested);
        return await _db.UserCompanyAccesses.AsNoTracking()
            .Where(x => x.UserId == userId && x.Company.IsActive)
            .OrderByDescending(x => x.CompanyId == requested).ThenByDescending(x => x.IsDefault).ThenBy(x => x.CompanyId)
            .Select(x => (int?)x.CompanyId).FirstOrDefaultAsync(ct);
    }

    private static byte[] Overlay(byte[] content, string headerPath)
    {
        using var input = new MemoryStream(content); using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import); using var output = new PdfDocument();
        var isPdf = headerPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase); using var form = isPdf ? XPdfForm.FromFile(headerPath) : null; using var image = !isPdf ? XImage.FromFile(headerPath) : null; using var cs = new MemoryStream(content); using var contentForm = XPdfForm.FromStream(cs);
        for (var i = 0; i < source.PageCount; i++) { var sp = source.Pages[i]; var p = output.AddPage(); p.Width = sp.Width; p.Height = sp.Height; using var g = XGraphics.FromPdfPage(p); var r = new XRect(0, 0, p.Width.Point, p.Height.Point); if (form != null) { form.PageNumber = 1; g.DrawImage(form, r); } else if (image != null) g.DrawImage(image, r); contentForm.PageNumber = i + 1; g.DrawImage(contentForm, r); }
        using var ms = new MemoryStream(); output.Save(ms); return ms.ToArray();
    }
}
