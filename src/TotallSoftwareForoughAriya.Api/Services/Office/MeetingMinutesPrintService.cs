using System.Globalization;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace Inventory.Api.Services.Office;

// ============================================================
//  قالب چاپی صورتجلسه با سربرگ شرکت — A4
//  • سربرگ اختیاری است. اگر فایل سربرگ نباشد، صورتجلسه با حاشیهٔ عادی چاپ می‌شود.
//  • امضای الکترونیکی حاضرین به‌صورت تصویر در صفحهٔ چاپ درج می‌شود.
// ============================================================

public interface IMeetingMinutesPrintService
{
    /// <summary>تولید PDF صورتجلسه روی سربرگ شرکت — اگر سربرگ نباشد بدون پس‌زمینه برمی‌گردد.</summary>
    Task<byte[]?> GeneratePdfAsync(int minutesId);
}

public class MeetingMinutesPrintService : IMeetingMinutesPrintService
{
    private static bool _fontsRegistered;
    private static readonly object FontLock = new();

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _http;

    public MeetingMinutesPrintService(AppDbContext db, IWebHostEnvironment env, IHttpContextAccessor http)
    {
        _db = db;
        _env = env;
        _http = http;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ==================== فونت فارسی ====================

    private static bool _vazirReady;
    private static string Fnt => _vazirReady ? "Vazirmatn" : "Lato";
    private static string FntBold => _vazirReady ? "Vazirmatn-Bold" : "Lato";

    private void EnsureFonts()
    {
        if (_fontsRegistered) return;
        lock (FontLock)
        {
            if (_fontsRegistered) return;
            // نبودِ یک نویسه (ایموجی و…) نباید چاپ را قطع کند
            QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;

            var dirs = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Resources", "fonts"),
                Path.Combine(_env.ContentRootPath ?? "", "Resources", "fonts"),
                Path.Combine(Directory.GetCurrentDirectory(), "Resources", "fonts"),
                Path.Combine(AppContext.BaseDirectory, "fonts"),
            };
            string? reg = null, bold = null;
            foreach (var dir in dirs)
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
                var r = Path.Combine(dir, "Vazirmatn-Regular.ttf");
                var b = Path.Combine(dir, "Vazirmatn-Bold.ttf");
                if (reg == null && File.Exists(r)) reg = r;
                if (bold == null && File.Exists(b)) bold = b;
            }
            if (reg != null) FontManager.RegisterFontWithCustomName("Vazirmatn", File.OpenRead(reg));
            if (bold != null) FontManager.RegisterFontWithCustomName("Vazirmatn-Bold", File.OpenRead(bold));
            _vazirReady = reg != null;
            _fontsRegistered = true;
        }
    }

    // ==================== سربرگ ====================

    private string? ResolveLetterheadPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var clean = fileName.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadClean = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? clean["uploads/".Length..]
            : clean;

        var candidates = new[]
        {
            Path.Combine(webRoot, "uploads", uploadClean),
            Path.Combine(webRoot, clean),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(_env.ContentRootPath, Path.GetFileName(clean)),
            Path.Combine(AppContext.BaseDirectory, clean),
            Path.Combine(AppContext.BaseDirectory, Path.GetFileName(clean))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string FaDate(DateTime? d)
    {
        if (d is null) return "—";
        var v = d.Value;
        // تاریخ‌های خارج از بازهٔ تقویم جلالی نباید کل چاپ را متوقف کنند
        try
        {
            var pc = new PersianCalendar();
            if (v < pc.MinSupportedDateTime || v > pc.MaxSupportedDateTime) return "—";
            return $"{pc.GetYear(v)}/{pc.GetMonth(v):00}/{pc.GetDayOfMonth(v):00}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return "—";
        }
    }

    // ==================== تولید PDF ====================

    public async Task<byte[]?> GeneratePdfAsync(int minutesId)
    {
        var m = await _db.MeetingMinutes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == minutesId && !x.IsDeleted);
        if (m == null) return null;

        var parts = await _db.MeetingMinutesParticipants.AsNoTracking()
            .Where(p => p.MinutesId == minutesId)
            .OrderBy(p => p.Kind).ThenBy(p => p.Name)
            .ToListAsync();
        var items = await _db.MeetingMinutesItems.AsNoTracking()
            .Where(i => i.MinutesId == minutesId)
            .OrderBy(i => i.RowNo).ThenBy(i => i.Id)
            .ToListAsync();

        // سربرگ اختیاری است. نبودن شرکت، ستون سربرگ، یا خودِ فایل نباید چاپ را متوقف کند.
        string? companyName = null;
        string? letterheadPath = null;
        try
        {
            // شرکت فعال باید از میان‌افزار ActiveCompanyMiddleware خوانده شود، نه از هدر خام؛
            // هدر خام اعتبارسنجی دسترسی نمی‌شود و می‌توانست سربرگ شرکت دیگری را چاپ کند.
            var companyId = _http.HttpContext?.Items.TryGetValue(Inventory.Api.Infrastructure.ActiveCompanyMiddleware.ItemKey, out var resolved) == true
                            && resolved is int resolvedId && resolvedId > 0
                ? resolvedId
                : 0;
            var company = companyId > 0
                ? await _db.SystemCompanies.AsNoTracking()
                    .Where(c => c.Id == companyId && c.IsActive)
                    .Select(c => new { c.Name, c.LetterheadFileName })
                    .FirstOrDefaultAsync()
                : null;
            companyName = company?.Name;
            letterheadPath = ResolveLetterheadPath(company?.LetterheadFileName);
        }
        catch
        {
            letterheadPath = null;
        }

        byte[] contentPdf;
        try
        {
            contentPdf = BuildContentPdf(m, parts, items, letterheadPath != null, companyName);
        }
        catch
        {
            // اگر چیدمان (جدول/امضا) جا نشد، نسخهٔ ساده بدون جدول ثابت برمی‌گردد
            contentPdf = BuildFallbackPdf(m, parts, items, companyName);
            letterheadPath = null;
        }

        if (letterheadPath == null) return contentPdf;

        try
        {
            return OverlayOnLetterhead(contentPdf, letterheadPath);
        }
        catch
        {
            // فایل سربرگ خراب — دوباره بدون حاشیهٔ مخصوص سربرگ
            try { return BuildContentPdf(m, parts, items, false, companyName); }
            catch { return BuildFallbackPdf(m, parts, items, companyName); }
        }
    }

    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Line = "#e2e8f0";
    private const string Soft = "#f8fafc";
    private const string Brand = "#4f46e5";
    private const string BrandDark = "#312e81";

    private static string FaDigits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        const string map = "۰۱۲۳۴۵۶۷۸۹";
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is >= '0' and <= '9')
                chars[i] = map[chars[i] - '0'];
        return new string(chars);
    }

    private static string FaDatePretty(DateTime? d) => FaDigits(FaDate(d));

    private static void SectionHead(ColumnDescriptor col, string title, string? hint = null)
    {
        col.Item().PaddingTop(14).PaddingBottom(6).Row(r =>
        {
            r.ConstantItem(3).Background(Brand);
            r.ConstantItem(8);
            r.RelativeItem().AlignMiddle().Text(title).FontFamily(FntBold).FontSize(11.5f).FontColor(BrandDark);
            if (!string.IsNullOrWhiteSpace(hint))
                r.AutoItem().AlignMiddle().Text(hint).FontSize(8).FontColor(Muted);
        });
    }

    private byte[] BuildContentPdf(MeetingMinutes m, List<MeetingMinutesParticipant> parts, List<MeetingMinutesItem> items, bool reserveLetterhead, string? companyName)
    {
        EnsureFonts();

        const float fs = 10f;
        var number = FaDigits(m.Id.ToString());
        var attendees = parts.Where(p => p.Kind == MinutesParticipantKind.Attendee).ToList();
        var absent = parts.Where(p => p.Kind == MinutesParticipantKind.Absent).ToList();

        var signatureImages = new Dictionary<int, byte[]>();
        foreach (var p in attendees)
        {
            var png = TryDecodeSignaturePng(p.SignatureData);
            if (png != null) signatureImages[p.Id] = png;
        }

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(Fnt).FontSize(fs).FontColor(Ink).LineHeight(1.45f));

                if (reserveLetterhead)
                {
                    page.MarginTop(118);
                    page.MarginBottom(52);
                    page.MarginHorizontal(36);
                }
                else
                {
                    page.MarginTop(0);
                    page.MarginHorizontal(0);
                    page.MarginBottom(16);
                }

                if (!reserveLetterhead)
                {
                    page.Header().Column(h =>
                    {
                        h.Item().Height(7).Background(Brand);
                        h.Item().Background("#eef2ff").PaddingHorizontal(28).PaddingVertical(8).Row(row =>
                        {
                            row.RelativeItem().AlignMiddle().Text(string.IsNullOrWhiteSpace(companyName) ? "صورتجلسه" : companyName)
                                .FontFamily(FntBold).FontSize(11).FontColor(BrandDark);
                            row.AutoItem().AlignMiddle().Text($"شماره  {number}")
                                .FontFamily(FntBold).FontSize(9).FontColor(Brand);
                        });
                    });
                }

                page.Content().PaddingHorizontal(reserveLetterhead ? 0 : 28).PaddingTop(reserveLetterhead ? 0 : 14).Column(col =>
                {
                    col.Item().AlignCenter().Text("صورتجلسه")
                        .FontFamily(FntBold).FontSize(9).FontColor(Brand);
                    col.Item().PaddingTop(3).AlignCenter().Text(string.IsNullOrWhiteSpace(m.Title) ? "بدون عنوان" : m.Title)
                        .FontFamily(FntBold).FontSize(16).FontColor(Ink).LineHeight(1.35f);
                    col.Item().PaddingTop(6).AlignCenter().Width(56).Height(2.5f).Background(Brand);
                    if (!string.IsNullOrWhiteSpace(m.CreatedByName))
                        col.Item().PaddingTop(2).AlignCenter().Text($"ثبت‌کننده: {m.CreatedByName}")
                            .FontSize(8.5f).FontColor(Muted);

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        void Meta(string label, string value)
                        {
                            row.RelativeItem().PaddingHorizontal(3).Border(0.7f).BorderColor(Line).Background(Soft)
                                .PaddingVertical(6).PaddingHorizontal(4).Column(c =>
                                {
                                    c.Item().AlignCenter().Text(label).FontSize(7.5f).FontColor(Muted);
                                    c.Item().PaddingTop(2).AlignCenter().Text(value).FontFamily(FntBold).FontSize(9.5f).FontColor(Ink);
                                });
                        }
                        Meta("شماره", number);
                        Meta("تاریخ جلسه", FaDatePretty(m.MeetingDate));
                        Meta("تاریخ ثبت", FaDatePretty(m.DateRegistered));
                        Meta("وضعیت", MeetingMinutesStatus.ToFa(m.Status));
                    });

                    SectionHead(col, "حاضرین جلسه", attendees.Count > 0 ? FaDigits(attendees.Count.ToString()) + " نفر" : null);
                    col.Item().Border(0.7f).BorderColor(Line).Background("#fcfcfd").Padding(8).Column(box =>
                    {
                        box.Item().Text(attendees.Count > 0 ? string.Join("    ·    ", attendees.Select(p => p.Name)) : "—")
                            .FontSize(9.5f).LineHeight(1.6f).FontColor(Ink);
                        if (absent.Count > 0)
                            box.Item().PaddingTop(4).Text(t =>
                            {
                                t.Span("غایبین: ").FontFamily(FntBold).FontSize(8.5f).FontColor("#b91c1c");
                                t.Span(string.Join("، ", absent.Select(p => p.Name))).FontSize(8.5f).FontColor(Muted);
                            });
                    });

                    SectionHead(col, "بندها و تصمیمات", items.Count > 0 ? FaDigits(items.Count.ToString()) + " بند" : null);
                    if (items.Count == 0)
                    {
                        col.Item().Border(0.7f).BorderColor(Line).Padding(10)
                            .AlignCenter().Text("بندی ثبت نشده است.").FontSize(9).FontColor(Muted);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(28);
                                c.RelativeColumn(3.4f);
                                c.RelativeColumn(1.35f);
                                c.RelativeColumn(1.35f);
                                c.ConstantColumn(62);
                            });

                            table.Header(h =>
                            {
                                void Head(string txt) => h.Cell().Background(BrandDark).PaddingVertical(6).PaddingHorizontal(5)
                                    .AlignCenter().AlignMiddle()
                                    .Text(txt).FontFamily(FntBold).FontSize(8).FontColor("#ffffff");
                                Head("ردیف");
                                Head("شرح تصمیم");
                                Head("مسئول انجام");
                                Head("پیگیری");
                                Head("وضعیت");
                            });

                            for (var n = 0; n < items.Count; n++)
                            {
                                var it = items[n];
                                var bg = n % 2 == 0 ? "#ffffff" : "#f8fafc";
                                var statusBg = it.ItemStatus == MinutesItemStatus.Done ? "#dcfce7"
                                    : it.ItemStatus == MinutesItemStatus.Rejected ? "#fee2e2" : "#eef2ff";
                                var statusFg = it.ItemStatus == MinutesItemStatus.Done ? "#166534"
                                    : it.ItemStatus == MinutesItemStatus.Rejected ? "#991b1b" : BrandDark;

                                table.Cell().Background(bg).BorderBottom(0.6f).BorderColor(Line)
                                    .PaddingVertical(7).AlignCenter().AlignMiddle()
                                    .Text(FaDigits(it.RowNo.ToString())).FontFamily(FntBold).FontSize(9).FontColor(Brand);
                                table.Cell().Background(bg).BorderBottom(0.6f).BorderColor(Line)
                                    .PaddingVertical(7).PaddingHorizontal(6).AlignMiddle()
                                    .Text(it.Description).FontSize(9).LineHeight(1.5f).FontColor(Ink);
                                void Person(string? name, DateTime? date)
                                {
                                    table.Cell().Background(bg).BorderBottom(0.6f).BorderColor(Line)
                                        .PaddingVertical(6).PaddingHorizontal(4).AlignCenter().AlignMiddle()
                                        .Column(c =>
                                        {
                                            c.Item().AlignCenter().Text(string.IsNullOrWhiteSpace(name) ? "—" : name)
                                                .FontFamily(FntBold).FontSize(8).FontColor(Ink);
                                            c.Item().PaddingTop(1).AlignCenter().Text(FaDatePretty(date))
                                                .FontSize(7.5f).FontColor(Muted);
                                        });
                                }
                                Person(it.ResponsibleName, it.DueDate);
                                Person(it.FollowUpName, it.FollowUpDate);
                                table.Cell().Background(statusBg).BorderBottom(0.6f).BorderColor(Line)
                                    .PaddingVertical(6).AlignCenter().AlignMiddle()
                                    .Text(MinutesItemStatus.ToFa(it.ItemStatus)).FontFamily(FntBold).FontSize(7.5f).FontColor(statusFg);
                            }
                        });
                    }

                    if (attendees.Count > 0)
                    {
                        var signed = attendees.Count(p => p.SignedAt != null);
                        SectionHead(col, "امضای حاضرین", FaDigits(signed.ToString()) + " از " + FaDigits(attendees.Count.ToString()));

                        const int perRow = 3;
                        for (var i = 0; i < attendees.Count; i += perRow)
                        {
                            var row = attendees.Skip(i).Take(perRow).ToList();
                            col.Item().PaddingTop(2).Row(r =>
                            {
                                for (var k = 0; k < perRow; k++)
                                {
                                    var idx = k;
                                    r.RelativeItem().Padding(4).Element(box =>
                                    {
                                        if (idx >= row.Count) return;
                                        var person = row[idx];
                                        box.Border(0.7f).BorderColor(Line).Background("#fcfcfd").Padding(8).Column(sc =>
                                        {
                                            if (signatureImages.TryGetValue(person.Id, out var sig))
                                                sc.Item().Height(44).AlignCenter().AlignMiddle().Image(sig).FitArea();
                                            else
                                                sc.Item().Height(44).AlignBottom().PaddingHorizontal(14).PaddingBottom(8)
                                                    .LineHorizontal(0.7f).LineColor("#cbd5e1");
                                            sc.Item().PaddingTop(4).AlignCenter().Text(person.Name)
                                                .FontFamily(FntBold).FontSize(8.5f).FontColor(Ink);
                                            sc.Item().AlignCenter().Text(person.SignedAt != null ? FaDatePretty(person.SignedAt) : "در انتظار امضا")
                                                .FontSize(7.5f).FontColor(person.SignedAt != null ? "#166534" : Muted);
                                        });
                                    });
                                }
                            });
                        }
                    }
                });

                page.Footer().PaddingHorizontal(reserveLetterhead ? 0 : 28).Column(f =>
                {
                    f.Item().LineHorizontal(0.7f).LineColor(Line);
                    f.Item().PaddingTop(5).Row(r =>
                    {
                        r.RelativeItem().AlignMiddle().Text(string.IsNullOrWhiteSpace(companyName) ? "صورتجلسه" : companyName)
                            .FontSize(8).FontColor("#94a3b8");
                        r.AutoItem().AlignMiddle().Text(t =>
                        {
                            t.Span("صفحه ").FontSize(8).FontColor("#94a3b8");
                            t.CurrentPageNumber().FontSize(8).FontColor("#64748b").Format(n => FaDigits(n?.ToString() ?? "0"));
                            t.Span(" از ").FontSize(8).FontColor("#94a3b8");
                            t.TotalPages().FontSize(8).FontColor("#64748b").Format(n => FaDigits(n?.ToString() ?? "0"));
                        });
                    });
                });
            });
        });

        return doc.GeneratePdf();
    }

    /// <summary>
    /// امضای ذخیره‌شده را به PNG معتبر تبدیل می‌کند.
    /// پیشوند data:، فاصله و base64 ناقص را می‌پذیرد. اگر تصویر خراب باشد null برمی‌گرداند
    /// تا چاپ صورتجلسه به‌خاطر یک امضا متوقف نشود.
    /// </summary>
    private static byte[]? TryDecodeSignaturePng(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var sig = raw.Trim();
        var comma = sig.IndexOf(',');
        if (sig.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
            sig = sig[(comma + 1)..];

        sig = sig.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
        sig = sig.Replace('-', '+').Replace('_', '/');
        switch (sig.Length % 4)
        {
            case 2: sig += "=="; break;
            case 3: sig += "="; break;
        }

        byte[] bytes;
        try { bytes = Convert.FromBase64String(sig); }
        catch (FormatException) { return null; }
        if (bytes.Length < 8) return null;

        try
        {
            using var bmp = SKBitmap.Decode(bytes);
            if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0) return null;
            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>نسخهٔ ساده وقتی جدول یا امضا در صفحه جا نمی‌شود — باز هم بدون نیاز به سربرگ.</summary>
    private byte[] BuildFallbackPdf(MeetingMinutes m, List<MeetingMinutesParticipant> parts, List<MeetingMinutesItem> items, string? companyName)
    {
        EnsureFonts();
        var attendees = parts.Where(p => p.Kind == MinutesParticipantKind.Attendee).Select(p => p.Name).ToList();
        var absent = parts.Where(p => p.Kind == MinutesParticipantKind.Absent).Select(p => p.Name).ToList();
        var number = FaDigits(m.Id.ToString());

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(Fnt).FontSize(10.5f).FontColor(Ink).LineHeight(1.5f));
                page.Header().Column(h =>
                {
                    h.Item().Height(6).Background(Brand);
                    h.Item().PaddingTop(8).Text(string.IsNullOrWhiteSpace(companyName) ? "صورتجلسه" : companyName)
                        .FontFamily(FntBold).FontSize(12).FontColor(BrandDark);
                    h.Item().PaddingTop(4).PaddingBottom(6).LineHorizontal(0.6f).LineColor(Line);
                });
                page.Content().PaddingTop(8).Column(col =>
                {
                    col.Item().Text("صورتجلسه").FontFamily(FntBold).FontSize(9).FontColor(Brand);
                    col.Item().PaddingTop(2).Text(string.IsNullOrWhiteSpace(m.Title) ? "بدون عنوان" : m.Title)
                        .FontFamily(FntBold).FontSize(15);
                    col.Item().PaddingTop(4).Text($"شماره {number}    ·    جلسه {FaDatePretty(m.MeetingDate)}    ·    ثبت {FaDatePretty(m.DateRegistered)}    ·    {MeetingMinutesStatus.ToFa(m.Status)}")
                        .FontSize(9).FontColor(Muted);
                    col.Item().PaddingTop(8).Text("حاضرین: " + (attendees.Count > 0 ? string.Join("، ", attendees) : "—"));
                    if (absent.Count > 0)
                        col.Item().Text("غایبین: " + string.Join("، ", absent)).FontColor(Muted);
                    col.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor(Line);
                    if (items.Count == 0)
                        col.Item().PaddingTop(8).Text("بندی ثبت نشده است.").FontColor(Muted);
                    foreach (var it in items)
                    {
                        col.Item().PaddingTop(8).Text($"{FaDigits(it.RowNo.ToString())}.  {it.Description}").FontFamily(FntBold).FontSize(10.5f);
                        col.Item().Text($"انجام: {it.ResponsibleName ?? "—"}  ·  {FaDatePretty(it.DueDate)}      پیگیری: {it.FollowUpName ?? "—"}  ·  {FaDatePretty(it.FollowUpDate)}      {MinutesItemStatus.ToFa(it.ItemStatus)}")
                            .FontSize(8.5f).FontColor(Muted);
                    }
                });
            });
        });
        return doc.GeneratePdf();
    }

    /// <summary>قرار دادن صفحات روی سربرگ (PDF یا تصویر)</summary>
    private static byte[] OverlayOnLetterhead(byte[] contentPdf, string letterheadPath)
    {
        using var contentStream = new MemoryStream(contentPdf);
        using var contentDoc = PdfReader.Open(contentStream, PdfDocumentOpenMode.Import);

        using var output = new PdfDocument();

        var isPdf = letterheadPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        using var letterheadForm = isPdf ? XPdfForm.FromFile(letterheadPath) : null;
        using var letterheadImg = !isPdf ? XImage.FromFile(letterheadPath) : null;

        using var contentFormStream = new MemoryStream(contentPdf);
        using var contentForm = XPdfForm.FromStream(contentFormStream);

        for (int i = 0; i < contentDoc.PageCount; i++)
        {
            var srcPage = contentDoc.Pages[i];
            var page = output.AddPage();
            page.Width = srcPage.Width;
            page.Height = srcPage.Height;

            using var gfx = XGraphics.FromPdfPage(page);
            var rect = new XRect(0, 0, page.Width.Point, page.Height.Point);

            if (isPdf && letterheadForm != null)
            {
                letterheadForm.PageNumber = 1;
                gfx.DrawImage(letterheadForm, rect);
            }
            else if (letterheadImg != null)
            {
                gfx.DrawImage(letterheadImg, rect);
            }

            contentForm.PageNumber = i + 1;
            gfx.DrawImage(contentForm, rect);
        }

        using var ms = new MemoryStream();
        output.Save(ms);
        return ms.ToArray();
    }
}
