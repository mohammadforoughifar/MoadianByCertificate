using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Inventory.Api.Services.DocArchive;

public record DocExtractionResult(
    bool Success,
    string SourceType,
    string ExtractedText,
    string NormalizedText,
    int CharacterCount,
    string? ErrorMessage);

public interface IDocTextExtractorService
{
    Task<DocExtractionResult> ExtractAsync(byte[] fileBytes, string fileName, string? contentType);
    string Normalize(string? text);
    List<string> Tokenize(string? text);
    string? MakeSnippet(string content, string query, int maxContext = 100);
}

public class DocTextExtractorService : IDocTextExtractorService
{
    private readonly ILogger<DocTextExtractorService> _logger;
    private readonly IOcrServiceClient _ocr;

    public DocTextExtractorService(ILogger<DocTextExtractorService> logger, IOcrServiceClient ocr)
    {
        _logger = logger;
        _ocr = ocr;
    }

    /// <summary>آیا سرویس OCR پایتون تنظیم شده است؟ (برای نمایش در لاگ‌ها)</summary>
    public bool RemoteOcrConfigured => _ocr.IsConfigured;

    public async Task<DocExtractionResult> ExtractAsync(byte[] fileBytes, string fileName, string? contentType)
    {
        if (fileBytes == null || fileBytes.Length == 0)
            return new DocExtractionResult(false, "Unknown", "", "", 0, "فایل خالی است.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var ct = (contentType ?? "").ToLowerInvariant();

        try
        {
            // 1) PDF Documents (Digital + Scanned OCR)
            if (ext == ".pdf" || ct == "application/pdf")
            {
                // اگر سرویس OCR پایتون تنظیم شده بود، اول از آن استفاده می‌کنیم
                if (_ocr.IsConfigured)
                {
                    var remote = await _ocr.TryOcrPdfAsync(fileBytes, fileName).ConfigureAwait(false);
                    if (remote != null)
                    {
                        if (remote.Success)
                        {
                            var remoteText = remote.Text ?? "";
                            var source = string.Equals(remote.Method, "pdf-text", StringComparison.OrdinalIgnoreCase)
                                ? "Pdf" : "PdfOcr";
                            _logger.LogInformation("[OcrService] استخراج PDF با موتور {Engine} ({Method}) — {Chars} نویسه",
                                remote.Engine, remote.Method, remoteText.Length);
                            return new DocExtractionResult(true, source, remoteText, Normalize(remoteText),
                                remoteText.Length, null);
                        }
                        return new DocExtractionResult(false, "PdfOcr", "", "", 0,
                            TruncateError(remote.Error, "سرویس OCR"));
                    }
                    _logger.LogWarning("سرویس OCR (PDF) در دسترس نبود — ادامه با موتور محلی...");
                }
                return await ExtractPdfAsync(fileBytes);
            }

            // 2) Word Documents (.docx)
            if (ext == ".docx" || ct.Contains("wordprocessingml"))
            {
                // اگر سرویس آفیس پایتون تنظیم شده بود، اول از آن استفاده می‌کنیم (جدول‌ها + کامل‌تر)
                if (_ocr.IsConfigured)
                {
                    var remote = await _ocr.TryOcrOfficeAsync(fileBytes, fileName).ConfigureAwait(false);
                    if (remote != null)
                    {
                        if (remote.Success)
                        {
                            var remoteText = remote.Text ?? "";
                            _logger.LogInformation("[OcrService] استخراج Word با سرویس پایتون — {Chars} نویسه",
                                remoteText.Length);
                            return new DocExtractionResult(true, "Word", remoteText, Normalize(remoteText),
                                remoteText.Length, null);
                        }
                        return new DocExtractionResult(false, "Word", "", "", 0,
                            TruncateError(remote.Error, "سرویس آفیس"));
                    }
                    _logger.LogWarning("سرویس آفیس (Word) در دسترس نبود — ادامه با استخراج محلی...");
                }
                return ExtractDocx(fileBytes);
            }

            // 3) Excel Spreadsheets (.xlsx)
            if (ext == ".xlsx" || ct.Contains("spreadsheetml"))
            {
                // اگر سرویس آفیس پایتون تنظیم شده بود، اول از آن استفاده می‌کنیم (همهٔ شیت‌ها)
                if (_ocr.IsConfigured)
                {
                    var remote = await _ocr.TryOcrOfficeAsync(fileBytes, fileName).ConfigureAwait(false);
                    if (remote != null)
                    {
                        if (remote.Success)
                        {
                            var remoteText = remote.Text ?? "";
                            _logger.LogInformation("[OcrService] استخراج Excel با سرویس پایتون — {Chars} نویسه",
                                remoteText.Length);
                            return new DocExtractionResult(true, "Excel", remoteText, Normalize(remoteText),
                                remoteText.Length, null);
                        }
                        return new DocExtractionResult(false, "Excel", "", "", 0,
                            TruncateError(remote.Error, "سرویس آفیس"));
                    }
                    _logger.LogWarning("سرویس آفیس (Excel) در دسترس نبود — ادامه با استخراج محلی...");
                }
                return ExtractXlsx(fileBytes);
            }

            // 4) Plain Text / Structured Data Files
            if (ext is ".txt" or ".csv" or ".json" or ".xml" or ".html" or ".md" or ".sql" or ".log"
                || ct.StartsWith("text/") || ct.Contains("json") || ct.Contains("xml"))
            {
                return ExtractPlainText(fileBytes);
            }

            // 5) Images (PNG, JPG, JPEG, BMP, WEBP, TIFF) -> Advanced Dual-Language OCR (Persian + English)
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tiff" or ".tif"
                || ct.StartsWith("image/"))
            {
                // اگر سرویس OCR پایتون تنظیم شده بود، اول از آن استفاده می‌کنیم
                if (_ocr.IsConfigured)
                {
                    var remote = await _ocr.TryOcrImageAsync(fileBytes, fileName).ConfigureAwait(false);
                    if (remote != null)
                    {
                        if (remote.Success)
                        {
                            var remoteText = remote.Text ?? "";
                            _logger.LogInformation("[OcrService] OCR تصویر با موتور {Engine} — {Chars} نویسه",
                                remote.Engine, remoteText.Length);
                            return new DocExtractionResult(true, "ImageOcr", remoteText, Normalize(remoteText),
                                remoteText.Length, null);
                        }
                        return new DocExtractionResult(false, "ImageOcr", "", "", 0,
                            TruncateError(remote.Error, "سرویس OCR"));
                    }
                    _logger.LogWarning("سرویس OCR (تصویر) در دسترس نبود — ادامه با موتور محلی...");
                }
                return await ExtractImageOcrAsync(fileBytes, ext);
            }

            return new DocExtractionResult(false, "Unsupported", "", "", 0, $"فرمت فایل ({ext}) برای استخراج متن پشتیبانی نمی‌شود.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "خطا در استخراج متن از فایل {FileName}", fileName);
            return new DocExtractionResult(false, "Error", "", "", 0, ex.Message);
        }
    }

    // =========================================================================
    // PDF Extraction & Scanned PDF OCR Fallback
    // =========================================================================

    private async Task<DocExtractionResult> ExtractPdfAsync(byte[] bytes)
    {
        var tempPdf = Path.Combine(Path.GetTempPath(), $"doc_extract_{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempPdf, bytes);

            // 1. Try pdftotext first (fast & accurate for digital PDFs)
            var (text, startError) = await RunProcessAsync("pdftotext", $"-layout \"{tempPdf}\" -", 15);
            var cleanText = text?.Trim() ?? "";
            if (startError != null)
            {
                _logger.LogWarning("ابزار pdftotext روی سرور در دسترس نیست ({Error}). اگر سرویس OCR پایتون را تنظیم کنید دیگر به آن نیاز نیست.", startError);
            }

            // Check if PDF has real embedded text
            if (CountMeaningfulChars(cleanText) >= 30)
            {
                var norm = Normalize(cleanText);
                return new DocExtractionResult(true, "Pdf", cleanText, norm, cleanText.Length, null);
            }

            // 2. If text is empty or very short, it's likely a scanned PDF -> Run OCR on pages
            _logger.LogInformation("PDF فاقد لایه متنی کافی است، اجرای OCR بر روی صفحات اسکن‌شده...");
            var (ocrText, ocrError) = await OcrPdfPagesAsync(tempPdf);
            if (!string.IsNullOrWhiteSpace(ocrText) && CountMeaningfulChars(ocrText) > 5)
            {
                var norm = Normalize(ocrText);
                return new DocExtractionResult(true, "PdfOcr", ocrText, norm, ocrText.Length, null);
            }

            // هیچ متنی به‌دست نیامد — اگر ابزارهای سیستم نبودند دلیل را شفاف اعلام کن
            if (ocrError != null)
            {
                var (pfx, sol) = OcrFailureTexts();
                return new DocExtractionResult(false, "PdfOcr", "", "", 0,
                    pfx + "تشخیص متن اسکن انجام نشد: " + TruncateError(ocrError, "موتور محلی") + " " + sol);
            }
            if (startError != null)
            {
                var (pfx, sol) = OcrFailureTexts();
                return new DocExtractionResult(false, "Pdf", "", "", 0,
                    pfx + "استخراج متن PDF ممکن نشد: " + TruncateError(startError, "موتور محلی") + " " + sol);
            }

            var fallbackNorm = Normalize(cleanText);
            return new DocExtractionResult(true, "Pdf", cleanText, fallbackNorm, cleanText.Length, null);
        }
        finally
        {
            try { if (File.Exists(tempPdf)) File.Delete(tempPdf); } catch { }
        }
    }

    private async Task<(string Text, string? Error)> OcrPdfPagesAsync(string pdfPath)
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"pdfpage_{Guid.NewGuid():N}");
        try
        {
            // Render first 6 pages to PNG (200 DPI for high OCR accuracy)
            var (_, renderError) = await RunProcessAsync("pdftoppm", $"-png -r 200 -f 1 -l 6 \"{pdfPath}\" \"{prefix}\"", 25);

            var dir = Path.GetDirectoryName(prefix)!;
            var pattern = Path.GetFileName(prefix) + "*.png";
            var pageFiles = Directory.GetFiles(dir, pattern).OrderBy(f => f).ToList();
            if (pageFiles.Count == 0)
            {
                return ("", renderError ?? "تصویری از صفحات PDF ساخته نشد (pdftoppm).");
            }

            var sb = new StringBuilder();
            string? firstError = renderError;
            foreach (var pageFile in pageFiles)
            {
                try
                {
                    var (ocr, ocrError) = await PerformOptimizedOcrAsync(pageFile);
                    if (firstError == null && ocrError != null) firstError = ocrError;
                    if (!string.IsNullOrWhiteSpace(ocr))
                    {
                        sb.AppendLine(ocr);
                        sb.AppendLine();
                    }
                }
                finally
                {
                    try { File.Delete(pageFile); } catch { }
                }
            }

            var text = sb.ToString().Trim();
            return (text, string.IsNullOrWhiteSpace(text) ? firstError : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "خطا در اجرای OCR روی صفحات PDF");
            return ("", ex.Message);
        }
    }

    // =========================================================================
    // Word Document (.docx) Extraction (Body + Headers + Footers)
    // =========================================================================

    private DocExtractionResult ExtractDocx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var sb = new StringBuilder();

        // Extract from body, headers, and footers
        var docXmlEntries = zip.Entries.Where(e =>
            e.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase)).ToList();

        if (docXmlEntries.Count == 0)
            return new DocExtractionResult(false, "Word", "", "", 0, "فایل‌های متنی در سند Word یافت نشد.");

        foreach (var entry in docXmlEntries)
        {
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8);
            var xmlContent = reader.ReadToEnd();

            var paragraphMatches = Regex.Matches(xmlContent, @"<w:p\b[^>]*>(.*?)</w:p>", RegexOptions.Singleline);
            if (paragraphMatches.Count > 0)
            {
                foreach (Match p in paragraphMatches)
                {
                    var textMatches = Regex.Matches(p.Value, @"<w:t\b[^>]*>(.*?)</w:t>", RegexOptions.Singleline);
                    if (textMatches.Count > 0)
                    {
                        foreach (Match t in textMatches)
                        {
                            sb.Append(System.Net.WebUtility.HtmlDecode(t.Groups[1].Value));
                        }
                        sb.AppendLine();
                    }
                }
            }
            else
            {
                var textMatches = Regex.Matches(xmlContent, @"<w:t\b[^>]*>(.*?)</w:t>", RegexOptions.Singleline);
                foreach (Match t in textMatches)
                {
                    sb.Append(System.Net.WebUtility.HtmlDecode(t.Groups[1].Value)).Append(' ');
                }
            }
        }

        var text = sb.ToString().Trim();
        var norm = Normalize(text);
        return new DocExtractionResult(true, "Word", text, norm, text.Length, null);
    }

    // =========================================================================
    // Excel Spreadsheet (.xlsx) Extraction
    // =========================================================================

    private DocExtractionResult ExtractXlsx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var sb = new StringBuilder();

        // 1. Read shared strings
        var sharedStrings = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedStrings != null)
        {
            using var sStream = sharedStrings.Open();
            using var sReader = new StreamReader(sStream, Encoding.UTF8);
            var sXml = sReader.ReadToEnd();
            var matches = Regex.Matches(sXml, @"<t\b[^>]*>(.*?)</t>", RegexOptions.Singleline);
            foreach (Match m in matches)
            {
                sb.Append(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).Append(" | ");
            }
            sb.AppendLine();
        }

        // 2. Read sheet cells (inline strings / values)
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml")))
        {
            using var sheetStream = entry.Open();
            using var sheetReader = new StreamReader(sheetStream, Encoding.UTF8);
            var sheetXml = sheetReader.ReadToEnd();
            var inlineMatches = Regex.Matches(sheetXml, @"<v>(.*?)</v>", RegexOptions.Singleline);
            foreach (Match m in inlineMatches)
            {
                var val = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(val)) sb.Append(val).Append(' ');
            }
        }

        var text = sb.ToString().Trim();
        var norm = Normalize(text);
        return new DocExtractionResult(true, "Excel", text, norm, text.Length, null);
    }

    // =========================================================================
    // Plain Text Extraction
    // =========================================================================

    private DocExtractionResult ExtractPlainText(byte[] bytes)
    {
        string text;
        try
        {
            text = Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            text = Encoding.GetEncoding("windows-1256").GetString(bytes);
        }

        var norm = Normalize(text);
        return new DocExtractionResult(true, "Text", text, norm, text.Length, null);
    }

    // =========================================================================
    // Enhanced Image OCR Extraction (Persian + English with Preprocessing)
    // =========================================================================

    private async Task<DocExtractionResult> ExtractImageOcrAsync(byte[] bytes, string ext)
    {
        var tempImg = Path.Combine(Path.GetTempPath(), $"ocr_raw_{Guid.NewGuid():N}{ext}");
        try
        {
            await File.WriteAllBytesAsync(tempImg, bytes);
            var (cleanText, ocrError) = await PerformOptimizedOcrAsync(tempImg);

            if (ocrError != null)
            {
                // ابزار OCR موجود نبود — به‌جای سکوت، دلیل را شفاف اعلام کن
                _logger.LogWarning("OCR محلی برای تصویر ممکن نشد: {Error}", ocrError);
                var (pfx, sol) = OcrFailureTexts();
                return new DocExtractionResult(false, "ImageOcr", "", "", 0,
                    pfx + "تشخیص متن انجام نشد: " + TruncateError(ocrError, "موتور محلی") + " " + sol);
            }

            var norm = Normalize(cleanText);
            return new DocExtractionResult(true, "ImageOcr", cleanText, norm, cleanText.Length, null);
        }
        finally
        {
            try { if (File.Exists(tempImg)) File.Delete(tempImg); } catch { }
        }
    }

    /// <summary>
    /// اجرای OCR بهینه با پشتیبانی همزمان از زبان‌های فارسی و انگلیسی و استفاده از Tesseract.
    /// خروجی دوم = خطا؛ فقط وقتی که خودِ ابزار (tesseract) قابل‌اجرا نبود.
    /// </summary>
    private async Task<(string Text, string? Error)> PerformOptimizedOcrAsync(string imagePath)
    {
        // ۱. اجرای مستقیم Tesseract با زبان‌های فارسی و انگلیسی
        var (text, err) = await RunProcessAsync("tesseract", $"\"{imagePath}\" stdout -l fas+eng --psm 3", 25);
        if (err != null)
            return ("", err);
        var clean = text?.Trim() ?? "";

        // ۲. اگر خروجی کم بود، حالت بلوک یکنواخت (--psm 6) را امتحان کن
        if (CountMeaningfulChars(clean) < 10)
        {
            var (textPsm6, errPsm6) = await RunProcessAsync("tesseract", $"\"{imagePath}\" stdout -l fas+eng --psm 6", 20);
            if (errPsm6 == null && !string.IsNullOrWhiteSpace(textPsm6) && textPsm6.Length > clean.Length)
            {
                clean = textPsm6.Trim();
            }
        }

        // ۳. در صورتی که خروجی فارسی ناچیز بود، فقط انگلیسی را امتحان کن
        if (CountMeaningfulChars(clean) < 10)
        {
            var (textEng, errEng) = await RunProcessAsync("tesseract", $"\"{imagePath}\" stdout -l eng --psm 3", 15);
            if (errEng == null && !string.IsNullOrWhiteSpace(textEng)) clean = textEng.Trim();
        }

        return (clean, null);
    }

    // =========================================================================
    // Persian & Arabic Normalization & Tokenization
    // =========================================================================

    public string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // حذف اعراب و نشانه‌های صوتی عربی (حرکات: فتحه، کسره، ضمه، تنوین‌ها، تشدید، سکون)
            if (ch >= '\u064B' && ch <= '\u065F') continue;
            if (ch == '\u0670') continue; // الف کوتاه خنجری

            switch (ch)
            {
                case 'ي':
                case 'ى':
                case 'ئ':
                    sb.Append('ی');
                    break;
                case 'ك':
                case 'ڪ':
                    sb.Append('ک');
                    break;
                case 'ة':
                    sb.Append('ه');
                    break;
                case 'ؤ':
                    sb.Append('و');
                    break;
                case 'أ':
                case 'إ':
                case 'آ':
                case 'ٱ':
                    sb.Append('ا');
                    break;
                // یکدست‌سازی ارقام فارسی و عربی به ارقام انگلیسی
                case '۰':
                case '٠':
                    sb.Append('0');
                    break;
                case '۱':
                case '١':
                    sb.Append('1');
                    break;
                case '۲':
                case '٢':
                    sb.Append('2');
                    break;
                case '۳':
                case '٣':
                    sb.Append('3');
                    break;
                case '۴':
                case '٤':
                    sb.Append('4');
                    break;
                case '۵':
                case '٥':
                    sb.Append('5');
                    break;
                case '۶':
                case '٦':
                    sb.Append('6');
                    break;
                case '۷':
                case '٧':
                    sb.Append('7');
                    break;
                case '۸':
                case '٨':
                    sb.Append('8');
                    break;
                case '۹':
                case '٩':
                    sb.Append('9');
                    break;
                // تبدیل نیم‌فاصله و نویسه‌های کنترلی به فاصله
                case '\u200c': // Zero-width non-joiner
                case '\u200d': // Zero-width joiner
                case '\ufeff': // Byte order mark
                case '\u00a0': // Non-breaking space
                case '\u200e': // Left-to-right mark
                case '\u200f': // Right-to-left mark
                case '\r':
                case '\n':
                case '\t':
                    sb.Append(' ');
                    break;
                default:
                    sb.Append(char.ToLowerInvariant(ch));
                    break;
            }
        }

        // یکدست‌سازی فاصله‌های متوالی
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    public List<string> Tokenize(string? text)
    {
        var norm = Normalize(text);
        if (string.IsNullOrWhiteSpace(norm)) return new List<string>();

        return norm.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .Distinct()
            .ToList();
    }

    public string? MakeSnippet(string content, string query, int maxContext = 100)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(query))
            return null;

        var normContent = Normalize(content);
        var normQuery = Normalize(query);

        var idx = normContent.IndexOf(normQuery, StringComparison.OrdinalIgnoreCase);
        var matchLen = normQuery.Length;

        if (idx < 0)
        {
            // تلاش برای انطباق بر روی کلمات جداگانه عبارت جستجو
            var tokens = Tokenize(query);
            foreach (var token in tokens)
            {
                var tokenIdx = normContent.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (tokenIdx >= 0)
                {
                    idx = tokenIdx;
                    matchLen = token.Length;
                    break;
                }
            }
        }

        if (idx < 0)
        {
            return content.Length > 180 ? content[..180] + "…" : content;
        }

        var start = Math.Max(0, idx - maxContext);
        var length = Math.Min(content.Length - start, matchLen + (maxContext * 2));

        var prefix = start > 0 ? "… " : "";
        var suffix = (start + length) < content.Length ? " …" : "";

        var sub = content.Substring(start, length).Replace("\r", " ").Replace("\n", " ");
        return prefix + Regex.Replace(sub, @"\s+", " ").Trim() + suffix;
    }

    private static int CountMeaningfulChars(string text)
    {
        return text.Count(c => char.IsLetterOrDigit(c));
    }

    /// <summary>کوتاه‌سازی پیام خطا برای ذخیره در ErrorMessage (حداکثر ۵۰۰ نویسه).</summary>
    private static string TruncateError(string? message, string source)
    {
        var msg = string.IsNullOrWhiteSpace(message) ? "خطای نامشخص" : message!.Trim();
        var line = msg.Replace("\r", " ").Replace("\n", " ").Trim();
        return $"({source}: {line})";
    }

    /// <summary>
    /// متن‌های راهنمای خطای OCR بر اساس وضعیت سرویس:
    /// سرویس تنظیم شده ولی در دسترس نبوده (خطای اتصال) یا اصلاً تنظیم نشده.
    /// </summary>
    private (string Prefix, string Solution) OcrFailureTexts()
    {
        if (_ocr.IsConfigured)
            return (
                $"سرویس OCR ({_ocr.BaseUrl}) در دسترس نبود؛ ",
                "اتصال سرویس را بررسی کنید: آیا سرویس پایتون اجراست؟ پورت/فایروال باز است؟"
            );
        return (
            "",
            "راه‌حل: سرویس OCR پایتون را راه‌اندازی کنید و OcrService:BaseUrl را در appsettings تنظیم کنید (یا OcrService:AutoDetect را فعال بگذارید و برنامه را ری‌استارت کنید)."
        );
    }

    /// <summary>
    /// اجرای ابزار سیستمی. خروجی دوم فقط وقتی مقدار دارد که خودِ ابزار قابل‌اجرا نبود
    /// (مثلاً نصب نیست) — تا به‌جای سکوت، دلیل شفاف گزارش شود.
    /// </summary>
    private static async Task<(string? Output, string? StartError)> RunProcessAsync(string command, string args, int timeoutSeconds)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                return (null, $"ابزار «{command}» روی سرور پیدا نشد ({ex.GetType().Name}). برای نصب: poppler-utils و tesseract با زبان فارسی (fas).");
            }

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var completed = await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), timeoutTask);

            if (completed == timeoutTask)
            {
                try { p.Kill(true); } catch { }
                return (null, $"اجرای «{command}» از زمان مجاز ({timeoutSeconds} ثانیه) بیشتر شد.");
            }

            await p.WaitForExitAsync();
            return (await stdoutTask, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DocTextExtractor] Process {command} error: {ex.Message}");
            return (null, $"اجرای «{command}» با خطا مواجه شد: {ex.Message}");
        }
    }
}
