using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Inventory.Shared.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// Self-contained, script-free content previews. Never launches Office/LibreOffice,
/// evaluates macros/formulas, downloads linked images, or opens external relationships.
/// This deliberately is not an exact Office pagination/layout renderer.
/// </summary>
public static class OfficePreviewHtml
{
    private const long MaxInput = 40L * 1024 * 1024;
    private const long MaxExpanded = 256L * 1024 * 1024;
    private const long MaxXml = 12L * 1024 * 1024;
    private const int MaxRows = 250, MaxColumns = 50, MaxSheets = 20, MaxSlides = 120;
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static bool CanHandle(string fileName, string? contentType) => AttachmentPreviewFormats.Kind(fileName, contentType) == "office";

    public static bool TryBuild(byte[] bytes, string fileName, string? contentType, out string html, out string? error)
    {
        html = ""; error = null;
        if (bytes.Length == 0) { error = "فایل خالی است."; return false; }
        if (bytes.LongLength > MaxInput) { error = "پیش‌نمایش سبک آفیس برای فایل‌های حداکثر ۴۰ مگابایت فعال است."; return false; }
        try
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ext == ".csv" || (contentType ?? "").Contains("csv", StringComparison.OrdinalIgnoreCase))
            {
                html = Wrap(fileName, "Excel / CSV", Csv(bytes)); return true;
            }
            if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4b)
            {
                error = "فرمت قدیمی یا رمزگذاری‌شدهٔ آفیس پیش‌نمایش سبک ندارد. یک نسخهٔ بدون رمز با فرمت DOCX، XLSX، PPTX یا PDF تهیه کنید.";
                return false;
            }
            using var package = new Package(bytes);
            string body, kind;
            // Inspect the real package, rather than trusting an incorrectly stored MIME/suffix.
            if (package.Has("word/document.xml")) { kind = "Word"; body = Word(package); }
            else if (package.Has("xl/workbook.xml")) { kind = "Excel"; body = Excel(package); }
            else if (package.Has("ppt/presentation.xml")) { kind = "PowerPoint"; body = PowerPoint(package); }
            else { error = "ساختار فایل آفیس معتبر یا قابل پشتیبانی نیست."; return false; }
            html = Wrap(fileName, kind, body); return true;
        }
        catch (PreviewException ex) { error = ex.Message; return false; }
        catch (InvalidDataException) { error = "ساختار فشردهٔ فایل آفیس نامعتبر یا آسیب‌دیده است؛ پیش‌نمایش ساخته نشد."; return false; }
        catch (Exception ex) when (ex is XmlException or FormatException or ArgumentException or IOException or OverflowException)
        { error = "فایل آفیس آسیب‌دیده، رمزگذاری‌شده یا دارای ساختار غیرقابل پشتیبانی است؛ پیش‌نمایش ساخته نشد."; return false; }
    }

    public static string AddWatermark(string html, string line)
    {
        var b = new StringBuilder("<div class=\"office-watermark\" aria-hidden=\"true\">");
        for (var r = 0; r < 12; r++)
        {
            b.Append("<div>");
            for (var c = 0; c < 3; c++) b.Append("<span>").Append(Enc(line)).Append("</span>");
            b.Append("</div>");
        }
        b.Append("</div>");
        return html.Replace("</body>", b + "</body>", StringComparison.Ordinal);
    }

    private sealed class PreviewException(string message) : Exception(message);

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string Attr(XElement? e, string name) => e?.Attributes().FirstOrDefault(x => x.Name.LocalName == name)?.Value ?? "";
    private static string Notice(string text) => "<p class=\"notice\">" + Enc(text) + "</p>";
    private static string Wrap(string name, string kind, string body) =>
        "<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">" +
        "<title>" + Enc(name) + "</title><style>" + Css + "</style></head><body><header><strong>" + Enc(kind) + "</strong> — " + Enc(name) +
        "</header><main>" + Notice("پیش‌نمایش محتواییِ سبک؛ صفحه‌بندی، فونت‌ها، نمودارها و اشیای ویژه ممکن است با فایل اصلی متفاوت باشند.") + body + "</main></body></html>";

    private const string Css = ".office-watermark{position:fixed;inset:-10%;display:flex;flex-direction:column;justify-content:space-around;pointer-events:none;overflow:hidden;z-index:999}.office-watermark div{display:flex;justify-content:space-around;transform:rotate(-24deg);white-space:nowrap;gap:20px;color:rgba(30,64,175,.15);font-weight:bold}.office-watermark span{padding:10px}" +
        "html,body{margin:0;background:#e9eef5;color:#0f172a;font:14px/1.8 Tahoma,'Segoe UI',sans-serif}" +
        "header{padding:10px 18px;background:#17385b;color:white;word-break:break-word}main{max-width:1200px;margin:auto;padding:18px;background:white;min-height:100vh}" +
        "p{margin:.3em 0;overflow-wrap:anywhere}h1,h2,h3{line-height:1.5}.notice{background:#eff6ff;border-right:3px solid #60a5fa;padding:8px;color:#334155;font-size:12px}" +
        "table{border-collapse:collapse;margin:12px 0;font-size:13px}td,th{border:1px solid #cbd5e1;padding:5px 9px;vertical-align:top;white-space:pre-wrap;overflow-wrap:anywhere}" +
        "th{background:#eff6ff}.word table{width:100%}img{max-width:100%;height:auto}.embedded{display:inline-block;vertical-align:middle;object-fit:contain;max-height:700px}" +
        ".sheet{overflow:auto;margin-bottom:24px}.sheet table{direction:ltr;min-width:100%}.sheet td{min-width:70px}.sheet th{position:sticky;top:0}.sheet td[dir=auto]{text-align:start}" +
        ".slide-label{margin:18px 0 5px;color:#1e40af}.slide{position:relative;width:100%;overflow:hidden;border:1px solid #cbd5e1;box-sizing:border-box;background:#fff}" +
        ".shape{position:absolute;overflow:auto;box-sizing:border-box;padding:4px;line-height:1.35}.shape img{width:100%;height:100%;object-fit:contain}.shape table{width:100%;margin:0;font-size:12px}" +
        ".shape p{margin:.12em 0}.shape-picture{padding:0}.ph{font-size:12px;color:#64748b;background:#f1f5f9;padding:4px}.word{max-width:950px;margin:auto}";

    private sealed class Package : IDisposable
    {
        private readonly MemoryStream _stream;
        private readonly ZipArchive _zip;
        private readonly Dictionary<string, string?> _images = new(StringComparer.OrdinalIgnoreCase);
        private int _imageBudget = 16 * 1024 * 1024;
        public Package(byte[] bytes)
        {
            _stream = new(bytes, writable: false);
            try
            {
                _zip = new(_stream, ZipArchiveMode.Read);
                long total = 0;
                if (_zip.Entries.Count > 10000) throw new PreviewException("تعداد اجزای فایل برای پیش‌نمایش ایمن بیش از حد مجاز است.");
                foreach (var e in _zip.Entries)
                {
                    total = checked(total + e.Length);
                    if (total > MaxExpanded || e.Length > 64L * 1024 * 1024)
                        throw new PreviewException("حجم بازشدهٔ فایل برای پیش‌نمایش ایمن بیش از حد مجاز است.");
                }
            }
            catch { _zip?.Dispose(); _stream.Dispose(); throw; }
        }
        public bool Has(string part) => _zip.GetEntry(part) != null;
        public IEnumerable<string> Names => _zip.Entries.Select(e => e.FullName);
        public XDocument Xml(string part)
        {
            var entry = _zip.GetEntry(part) ?? throw new PreviewException("یکی از اجزای ضروری فایل آفیس یافت نشد.");
            if (entry.Length > MaxXml) throw new PreviewException("جزء متنی فایل برای پیش‌نمایش سبک بیش از حد بزرگ است.");
            using var s = entry.Open();
            using var reader = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXml });
            return XDocument.Load(reader);
        }
        public Dictionary<string, string> Relations(string part)
        {
            var slash = part.LastIndexOf('/');
            var rel = (slash >= 0 ? part[..(slash + 1)] : "") + "_rels/" + part[(slash + 1)..] + ".rels";
            if (!Has(rel)) return new();
            var result = new Dictionary<string, string>();
            foreach (var r in Xml(rel).Descendants().Where(x => x.Name.LocalName == "Relationship"))
            {
                if (Attr(r, "TargetMode").Equals("External", StringComparison.OrdinalIgnoreCase)) continue;
                var id = Attr(r, "Id"); var target = Resolve(part, Attr(r, "Target"));
                if (id.Length > 0 && target != null) result[id] = target;
            }
            return result;
        }
        public string Image(string part, Dictionary<string, string> rels, string id)
        {
            if (!rels.TryGetValue(id, out var name)) return "<span class=\"ph\">[تصویر پیوندی یا موجود نیست]</span>";
            if (!_images.TryGetValue(name, out var uri))
            {
                uri = null;
                var e = _zip.GetEntry(name);
                if (e != null && e.Length <= 8 * 1024 * 1024 && e.Length <= _imageBudget)
                {
                    using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms);
                    var b = ms.ToArray(); var mime = AttachmentPreviewFormats.Mime(name, null);
                    if (mime is "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "image/bmp" or "image/avif")
                    { uri = "data:" + mime + ";base64," + Convert.ToBase64String(b); _imageBudget -= b.Length; }
                    else if (mime == "image/tiff")
                    {
                        try
                        {
                            var info = SixLabors.ImageSharp.Image.Identify(b);
                            if (info != null && (long)info.Width * info.Height <= 25_000_000)
                            {
                                using var img = SixLabors.ImageSharp.Image.Load(b); using var output = new MemoryStream();
                                img.Save(output, new PngEncoder()); var png = output.ToArray();
                                if (png.Length <= _imageBudget) { uri = "data:image/png;base64," + Convert.ToBase64String(png); _imageBudget -= png.Length; }
                            }
                        }
                        catch (Exception) { /* Unsupported TIFF: show an explicit placeholder, not a broken document. */ }
                    }
                }
                _images[name] = uri;
            }
            return uri == null ? "<span class=\"ph\">[تصویر با فرمت خاص یا بیش از محدودیت پیش‌نمایش]</span>"
                : "<img class=\"embedded\" src=\"" + uri + "\" alt=\"تصویر داخل سند\">";
        }
        private static string? Resolve(string part, string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.Contains(':') || target.Contains('\\')) return null;
            var baseDir = part.Contains('/') ? part[..(part.LastIndexOf('/') + 1)] : "";
            var parts = new List<string>();
            foreach (var p in (target.StartsWith('/') ? target[1..] : baseDir + target).Split('/'))
            {
                if (p is "" or ".") continue;
                if (p == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
                else parts.Add(Uri.UnescapeDataString(p));
            }
            return string.Join('/', parts);
        }
        public void Dispose() { _zip.Dispose(); _stream.Dispose(); }
    }

    private static string Word(Package p)
    {
        const string part = "word/document.xml";
        var body = p.Xml(part).Root?.Element(W + "body") ?? throw new PreviewException("بدنهٔ سند Word یافت نشد.");
        var b = new StringBuilder("<article class=\"word\">");
        foreach (var header in p.Names.Where(n => n.StartsWith("word/header") && n.EndsWith(".xml")).Take(3))
            WordBlocks(p.Xml(header).Root!, p, header, p.Relations(header), b, 0);
        WordBlocks(body, p, part, p.Relations(part), b, 0);
        foreach (var footer in p.Names.Where(n => n.StartsWith("word/footer") && n.EndsWith(".xml")).Take(3))
            WordBlocks(p.Xml(footer).Root!, p, footer, p.Relations(footer), b, 0);
        return b.Append("</article>").ToString();
    }
    private static void WordBlocks(XElement container, Package p, string part, Dictionary<string, string> rels, StringBuilder b, int depth)
    {
        if (depth > 24) throw new PreviewException("تو در تو بودن سند برای پیش‌نمایش بیش از حد مجاز است.");
        foreach (var e in container.Elements())
        {
            if (b.Length > 24_000_000) throw new PreviewException("محتوای سند برای پیش‌نمایش سبک بیش از حد بزرگ است.");
            if (e.Name.LocalName == "p")
            {
                var prop = e.Element(W + "pPr"); var style = Attr(prop?.Element(W + "pStyle"), "val");
                var tag = style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) && int.TryParse(style[7..], out var h) ? "h" + Math.Clamp(h, 1, 3) : "p";
                var align = Attr(prop?.Element(W + "jc"), "val") switch { "center" => "center", "right" => "right", "both" => "justify", "left" => "left", _ => "start" };
                b.Append('<').Append(tag).Append(" dir=\"auto\" style=\"text-align:").Append(align).Append("\">");
                if (prop?.Element(W + "numPr") != null) b.Append("• ");
                foreach (var run in e.Descendants(W + "r"))
                {
                    var rp = run.Element(W + "rPr"); var text = new StringBuilder();
                    foreach (var n in run.Descendants())
                    {
                        if (n.Name == W + "t") text.Append(Enc(n.Value));
                        else if (n.Name == W + "tab") text.Append("&emsp;");
                        else if (n.Name == W + "br" || n.Name == W + "cr") text.Append("<br>");
                        else if (n.Name == A + "blip") text.Append(p.Image(part, rels, Attr(n, "embed")));
                        else if (n.Name.LocalName == "imagedata") text.Append(p.Image(part, rels, Attr(n, "id")));
                    }
                    var t = text.ToString();
                    if (Enabled(rp?.Element(W + "b"))) t = "<b>" + t + "</b>";
                    if (Enabled(rp?.Element(W + "i"))) t = "<i>" + t + "</i>";
                    if (Enabled(rp?.Element(W + "u"))) t = "<u>" + t + "</u>";
                    b.Append(t);
                }
                b.Append("</").Append(tag).Append('>');
            }
            else if (e.Name.LocalName == "tbl")
            {
                b.Append("<table>");
                foreach (var row in e.Elements(W + "tr"))
                {
                    b.Append("<tr>");
                    foreach (var cell in row.Elements(W + "tc"))
                    {
                        var span = int.TryParse(Attr(cell.Element(W + "tcPr")?.Element(W + "gridSpan"), "val"), out var v) ? Math.Clamp(v, 1, MaxColumns) : 1;
                        b.Append("<td colspan=\"").Append(span).Append("\">"); WordBlocks(cell, p, part, rels, b, depth + 1); b.Append("</td>");
                    }
                    b.Append("</tr>");
                }
                b.Append("</table>");
            }
            else if (e.Name.LocalName is "sdt" or "sdtContent" or "customXml" or "ins") WordBlocks(e, p, part, rels, b, depth + 1);
        }
    }
    private static bool Enabled(XElement? e) => e != null && Attr(e, "val") is not "0" and not "false" and not "none";

    private static string Excel(Package p)
    {
        var wb = p.Xml("xl/workbook.xml"); var rels = p.Relations("xl/workbook.xml");
        var strings = p.Has("xl/sharedStrings.xml") ? p.Xml("xl/sharedStrings.xml").Descendants().Where(x => x.Name.LocalName == "si")
            .Select(x => string.Concat(x.Descendants().Where(n => n.Name.LocalName == "t").Select(n => n.Value))).ToArray() : Array.Empty<string>();
        var date1904 = Attr(wb.Descendants().FirstOrDefault(e => e.Name.LocalName == "workbookPr"), "date1904") is "1" or "true";
        var dateStyles = new HashSet<int>();
        if (p.Has("xl/styles.xml"))
        {
            var styles = p.Xml("xl/styles.xml");
            var custom = styles.Descendants().Where(e => e.Name.LocalName == "numFmt").ToDictionary(e => Attr(e, "numFmtId"), e => Attr(e, "formatCode"));
            var xfs = styles.Descendants().FirstOrDefault(e => e.Name.LocalName == "cellXfs")?.Elements().ToArray() ?? Array.Empty<XElement>();
            for (var i = 0; i < xfs.Length; i++)
            {
                var id = Attr(xfs[i], "numFmtId");
                if (int.TryParse(id, out var num) && (num is >= 14 and <= 22 || num is >= 45 and <= 47)) dateStyles.Add(i);
                else if (custom.TryGetValue(id, out var code) && code.Contains('y') && (code.Contains('d') || code.Contains('m'))) dateStyles.Add(i);
            }
        }
        var sheets = wb.Descendants().Where(e => e.Name.LocalName == "sheet").ToArray(); var b = new StringBuilder();
        foreach (var sheet in sheets.Take(MaxSheets))
        {
            if (!rels.TryGetValue(Attr(sheet, "id"), out var part) || !p.Has(part)) continue;
            var xml = p.Xml(part); var rows = xml.Descendants().Where(e => e.Name.LocalName == "row").ToArray();
            var selected = rows.Take(MaxRows).ToArray(); var width = 1;
            foreach (var row in selected) foreach (var c in row.Elements().Where(e => e.Name.LocalName == "c")) width = Math.Max(width, Math.Min(MaxColumns, Column(Attr(c, "r"))));
            b.Append("<h3>").Append(Enc(Attr(sheet, "name"))).Append("</h3><section class=\"sheet\"><table><thead><tr><th>#</th>");
            for (var col = 1; col <= width; col++) b.Append("<th>").Append(ColumnName(col)).Append("</th>");
            b.Append("</tr></thead><tbody>");
            foreach (var row in selected)
            {
                var cells = new Dictionary<int, string>(); var fallback = 1;
                foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "c"))
                {
                    var col = Column(Attr(cell, "r")); if (col == 0) col = fallback; fallback = col + 1; if (col > MaxColumns) continue;
                    var value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
                    var type = Attr(cell, "t");
                    if (type == "s") value = int.TryParse(value, out var idx) && idx >= 0 && idx < strings.Length ? strings[idx] : "";
                    else if (type == "inlineStr") value = string.Concat(cell.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                    else if (type == "b") value = value == "1" ? "TRUE" : "FALSE";
                    else if (dateStyles.Contains(int.TryParse(Attr(cell, "s"), out var si) ? si : 0) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa) && oa is > 0 and < 2958466)
                    {
                        try { value = DateTime.FromOADate(oa + (date1904 ? 1462 : 0)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); } catch (ArgumentException) { }
                    }
                    if (value.Length == 0 && cell.Elements().FirstOrDefault(e => e.Name.LocalName == "f") is { } f) value = "=" + f.Value; // Formula displayed, never evaluated.
                    cells[col] = value;
                }
                b.Append("<tr><th>").Append(Enc(Attr(row, "r"))).Append("</th>");
                for (var col = 1; col <= width; col++) b.Append("<td dir=\"auto\">").Append(Enc(cells.GetValueOrDefault(col))).Append("</td>");
                b.Append("</tr>");
            }
            b.Append("</tbody></table></section>");
            if (rows.Length > MaxRows || xml.Descendants().Any(e => e.Name.LocalName == "c" && Column(Attr(e, "r")) > MaxColumns))
                b.Append(Notice($"برای این برگه حداکثر {MaxRows} ردیف موجود و {MaxColumns} ستون نمایش داده می‌شود؛ داده‌های فایل اصلی تغییر نکرده است."));
            var drawingRels = p.Relations(part);
            foreach (var draw in xml.Descendants().Where(e => e.Name.LocalName == "drawing"))
                if (drawingRels.TryGetValue(Attr(draw, "id"), out var drawingPart) && p.Has(drawingPart))
                    foreach (var image in p.Xml(drawingPart).Descendants(A + "blip").Take(30)) b.Append(p.Image(drawingPart, p.Relations(drawingPart), Attr(image, "embed")));
            if (b.Length > 24_000_000) throw new PreviewException("محتوای دفتر برای پیش‌نمایش سبک بیش از حد بزرگ است.");
        }
        if (sheets.Length > MaxSheets) b.Append(Notice($"فقط {MaxSheets} برگهٔ نخست نمایش داده شد."));
        return b.Length > 0 ? b.ToString() : Notice("دفتر خالی است یا برگهٔ قابل نمایش ندارد.");
    }
    private static int Column(string reference)
    {
        var n = 0; foreach (var c in reference) { if (!char.IsAsciiLetter(c)) break; n = Math.Min(1_000_000, n * 26 + char.ToUpperInvariant(c) - 'A' + 1); } return n;
    }
    private static string ColumnName(int n)
    { var s = ""; while (n > 0) { n--; s = (char)('A' + n % 26) + s; n /= 26; } return s; }

    private static string PowerPoint(Package p)
    {
        const string presentation = "ppt/presentation.xml";
        var doc = p.Xml(presentation); var rels = p.Relations(presentation);
        var size = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "sldSz");
        var width = Number(Attr(size, "cx"), 12192000); var height = Number(Attr(size, "cy"), 6858000);
        if (width <= 0 || height <= 0) throw new PreviewException("ابعاد اسلاید معتبر نیست.");
        var ids = doc.Descendants().Where(e => e.Name.LocalName == "sldId").ToArray();
        var b = new StringBuilder(); var count = 0;
        foreach (var id in ids.Take(MaxSlides))
        {
            if (!rels.TryGetValue(id.Attribute(R + "id")?.Value ?? "", out var part) || !p.Has(part)) continue;
            var slide = p.Xml(part); var slideRels = p.Relations(part); count++;
            var bg = Color(slide.Descendants().FirstOrDefault(e => e.Name.LocalName == "bg")?.Descendants(A + "srgbClr").FirstOrDefault());
            b.Append("<h3 class=\"slide-label\">اسلاید ").Append(count).Append("</h3><section class=\"slide\" style=\"aspect-ratio:")
                .Append(Inv(width)).Append('/').Append(Inv(height)).Append(";background:").Append(bg ?? "#ffffff").Append("\">");
            var fallback = 0;
            foreach (var shape in slide.Descendants().Where(e => e.Name.LocalName is "sp" or "pic" or "graphicFrame"))
            {
                var transform = shape.Descendants().FirstOrDefault(e => e.Name.LocalName == "xfrm");
                var off = transform?.Elements().FirstOrDefault(e => e.Name.LocalName == "off"); var ext = transform?.Elements().FirstOrDefault(e => e.Name.LocalName == "ext");
                var x = Number(Attr(off, "x"), width * .05); var y = Number(Attr(off, "y"), height * (.05 + .13 * fallback));
                var cx = Number(Attr(ext, "cx"), width * .9); var cy = Number(Attr(ext, "cy"), height * .12);
                var content = new StringBuilder();
                if (shape.Name.LocalName == "pic")
                    foreach (var image in shape.Descendants(A + "blip")) content.Append(p.Image(part, slideRels, Attr(image, "embed")));
                else if (shape.Descendants(A + "tbl").FirstOrDefault() is { } table)
                {
                    content.Append("<table>");
                    foreach (var row in table.Elements(A + "tr"))
                    { content.Append("<tr>"); foreach (var cell in row.Elements(A + "tc")) content.Append("<td dir=\"auto\">").Append(DrawingText(cell)).Append("</td>"); content.Append("</tr>"); }
                    content.Append("</table>");
                }
                else content.Append(DrawingText(shape));
                if (content.Length == 0) continue;
                var fill = Color(shape.Elements().FirstOrDefault(e => e.Name.LocalName == "spPr")?.Element(A + "solidFill")?.Element(A + "srgbClr"));
                b.Append("<div class=\"shape ").Append(shape.Name.LocalName == "pic" ? "shape-picture" : "")
                    .Append("\" style=\"left:").Append(Inv(x / width * 100)).Append("%;top:").Append(Inv(y / height * 100))
                    .Append("%;width:").Append(Inv(Math.Clamp(cx / width * 100, .1, 100))).Append("%;height:").Append(Inv(Math.Clamp(cy / height * 100, .1, 100)))
                    .Append("%;background:").Append(fill ?? "transparent").Append("\">").Append(content).Append("</div>"); fallback++;
            }
            b.Append("</section>");
            if (slide.Descendants().Any(e => e.Name.LocalName is "chart" or "oleObj" or "videoFile")) b.Append(Notice("نمودار، شیء تعبیه‌شده یا رسانهٔ این اسلاید در پیش‌نمایش سبک اجرا نمی‌شود."));
            if (b.Length > 16_000_000) throw new PreviewException("محتوای ارائه برای پیش‌نمایش سبک بیش از حد بزرگ است.");
        }
        if (ids.Length > MaxSlides) b.Append(Notice($"فقط {MaxSlides} اسلاید نخست نمایش داده شد."));
        return count > 0 ? b.ToString() : Notice("ارائه اسلاید قابل نمایش ندارد.");
    }
    private static string DrawingText(XElement container)
    {
        var b = new StringBuilder();
        foreach (var para in container.Descendants(A + "p"))
        {
            var align = Attr(para.Element(A + "pPr"), "algn") switch { "ctr" => "center", "r" => "right", _ => "start" };
            b.Append("<p dir=\"auto\" style=\"text-align:").Append(align).Append("\">");
            foreach (var item in para.Elements())
            {
                if (item.Name == A + "br") { b.Append("<br>"); continue; }
                if (item.Name != A + "r" && item.Name != A + "fld") continue;
                var rp = item.Element(A + "rPr"); var t = Enc(item.Element(A + "t")?.Value);
                if (Attr(rp, "b") == "1") t = "<b>" + t + "</b>";
                if (Attr(rp, "i") == "1") t = "<i>" + t + "</i>";
                var color = Color(rp?.Element(A + "solidFill")?.Element(A + "srgbClr"));
                var pt = Number(Attr(rp, "sz"), 1800) / 100;
                b.Append("<span style=\"font-size:").Append(Inv(Math.Clamp(pt, 8, 60))).Append("px;color:").Append(color ?? "inherit").Append("\">").Append(t).Append("</span>");
            }
            b.Append("</p>");
        }
        return b.ToString();
    }
    private static string? Color(XElement? e) { var v = Attr(e, "val"); return v.Length == 6 && v.All(Uri.IsHexDigit) ? "#" + v : null; }
    private static double Number(string text, double fallback) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : fallback;
    private static string Inv(double n) => n.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Csv(byte[] bytes)
    {
        string text;
        using (var s = new MemoryStream(bytes))
        using (var r = new StreamReader(s, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true))
        {
            try { text = r.ReadToEnd(); }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                text = Encoding.GetEncoding(1256).GetString(bytes);
            }
        }
        var firstLine = text.Split(new[] { '\r', '\n' }, 2)[0];
        int DelimiterCount(char delimiter)
        {
            var count = 0; var inside = false;
            for (var i = 0; i < firstLine.Length; i++)
            {
                if (firstLine[i] == '"') { if (inside && i + 1 < firstLine.Length && firstLine[i + 1] == '"') i++; else inside = !inside; }
                else if (!inside && firstLine[i] == delimiter) count++;
            }
            return count;
        }
        var separator = new[] { ',', ';', '\t' }.OrderByDescending(DelimiterCount).First();
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); var quoted = false; var truncated = false;
        void AddCell() { if (row.Count < MaxColumns) row.Add(cell.ToString()); else truncated = true; cell.Clear(); }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (!quoted && c == separator) AddCell();
            else if (!quoted && c is '\r' or '\n')
            {
                AddCell(); rows.Add(row); row = new(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (rows.Count >= MaxRows) { truncated |= i + 1 < text.Length; break; }
            }
            else cell.Append(c);
        }
        if (quoted) throw new PreviewException("نقل‌قول‌های فایل CSV بسته نشده‌اند؛ ساختار فایل معتبر نیست.");
        if (rows.Count < MaxRows && (cell.Length > 0 || row.Count > 0)) { AddCell(); rows.Add(row); }
        var b = new StringBuilder("<section class=\"sheet\"><table><tbody>");
        foreach (var line in rows) { b.Append("<tr>"); foreach (var value in line) b.Append("<td dir=\"auto\">").Append(Enc(value)).Append("</td>"); b.Append("</tr>"); }
        b.Append("</tbody></table></section>");
        if (truncated) b.Append(Notice($"CSV تا {MaxRows} ردیف و {MaxColumns} ستون نمایش داده می‌شود."));
        return b.ToString();
    }
}
