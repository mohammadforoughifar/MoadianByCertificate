using System.IO.Compression;
using System.Text;
using Inventory.Api.Services.DocArchive;
using Inventory.Shared.Files;

namespace Inventory.PreviewTests;

internal static class PreviewVerification
{
    public static void Run(string fixtures)
    {
        var n = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + description);
            Console.WriteLine($"PASS {++n}: {description}");
        }
        byte[] Read(string name) => File.ReadAllBytes(Path.Combine(fixtures, name));
        string Build(string fixture, string? name = null)
        {
            Check(OfficePreviewHtml.TryBuild(Read(fixture), name ?? fixture, "application/octet-stream", out var html, out var error), "renderer accepts " + (name ?? fixture) + " (" + error + ")");
            return html;
        }
        var word = Build("sample.docx");
        Check(word.Contains("Word content") && word.Contains("متن فارسی"), "Word paragraph and Persian text");
        Check(word.Contains("Word table cell") && word.Contains("<table>"), "Word tables");
        Check(word.Contains("data:image/png;base64,"), "Word embedded image is real, not [image] placeholder");
        Check(word.Contains("&lt;script&gt;") && !word.Contains("<script>"), "Word text is escaped; document JavaScript cannot execute");
        Check(word.Contains("default-src 'none'") && word.Contains("base-uri 'none'"), "Office HTML has restrictive document CSP");
        Check(!word.Contains("preview-external.invalid"), "External relationships never become URLs in generated HTML");
        Check(Build("sample.docx", "wrong-extension.doc").Contains("Word content"), "ZIP-based renamed DOC is detected by package contents");
        Check(Build("sample.docx", "sample.docm").Contains("Word content"), "macro-enabled suffix accepted without running macros");
        var excel = Build("sample.xlsx");
        Check(excel.Contains("Excel text") && excel.Contains("متن فارسی"), "Excel shared/inline strings and Persian text");
        Check(excel.Contains("Second sheet marker"), "Excel multiple sheets");
        Check(excel.Contains("=A2+B2"), "uncached formula displayed, never executed");
        Check(excel.Contains("data:image/png;base64,"), "Excel embedded images");
        Check(excel.Contains("&lt;script&gt;"), "Excel cell text is escaped");
        Check(Build("large.xlsx").Contains("250") && !Build("large.xlsx").Contains("row-270"), "large sheet truncation is bounded and disclosed");
        var slides = Build("sample.pptx");
        Check(slides.Contains("PowerPoint slide 1") && slides.Contains("PowerPoint slide 2"), "PowerPoint presentation relationship order includes both slides");
        Check(slides.Contains("Slide table cell") && slides.Contains("<table>"), "PowerPoint tables");
        Check(slides.Contains("data:image/png;base64,"), "PowerPoint embedded images");
        Check(slides.Contains("متن فارسی") && slides.Contains("&lt;script&gt;"), "PowerPoint Persian text and escaping");
        Check(Build("sample.pptx", "sample.ppsx").Contains("PowerPoint slide 2"), "PowerPoint show suffix");
        var csv = Build("sample.csv");
        Check(csv.Contains("quoted &quot;word&quot;"), "CSV doubled quotes");
        Check(csv.Contains("line one\nline two"), "CSV quoted multiline cell");
        Check(Build("windows-1256.csv").Contains("سلام"), "legacy Persian Windows-1256 CSV fallback");
        Check(Build("quotes.csv").Contains("a;b;c;d") && Build("quotes.csv").Contains("<td dir=\"auto\">value</td>"), "CSV delimiter selection ignores separators inside quotes");
        foreach (var fixture in new[] { "legacy.doc", "legacy.xls", "legacy.ppt", "corrupt.docx", "bad-xml.docx" })
        {
            Check(!OfficePreviewHtml.TryBuild(Read(fixture), fixture, null, out var html, out var error) && html.Length == 0 && !string.IsNullOrWhiteSpace(error), "explicit failure for " + fixture);
        }
        Check(!OfficePreviewHtml.TryBuild(Array.Empty<byte>(), "empty.docx", null, out _, out _), "empty Office rejected");
        using (var ms = new MemoryStream())
        {
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var e = z.CreateEntry("word/document.xml", CompressionLevel.SmallestSize);
                using var output = e.Open(); var chunk = new byte[1024 * 1024];
                for (var i = 0; i < 65; i++) output.Write(chunk);
            }
            Check(!OfficePreviewHtml.TryBuild(ms.ToArray(), "bomb.docx", null, out _, out var reason) && reason!.Contains("حجم"), "oversized expanded ZIP rejected before XML loading");
        }
        var marked = OfficePreviewHtml.AddWatermark(word, "viewer <unsafe> & time");
        Check(marked.Contains("office-watermark") && marked.Contains("viewer &lt;unsafe&gt; &amp; time") && !marked.Contains("viewer <unsafe>"), "server Office watermark is escaped and embedded");
        foreach (var (name, kind) in new[] { ("a.png", "image"), ("a.pdf", "pdf"), ("a.docx", "office"), ("a.xlsx", "office"), ("a.pptx", "office"), ("a.ppsx", "office"), ("a.docm", "office"), ("a.txt", "text"), ("a.xyz", "other") })
            Check(AttachmentPreviewFormats.Kind(name, "application/octet-stream") == kind, "shared classification for " + name);
        Check(AttachmentPreviewFormats.Kind("sample.pptx", "text/plain") == "office", "recognized suffix beats wrong stored MIME");
        Check(AttachmentPreviewFormats.Mime("sample.pdf", "text/plain") == "application/pdf", "correct PDF response MIME despite old upload metadata");
        Check(AttachmentPreviewFormats.Mime("sample.png", "text/plain") == "image/png", "correct image response MIME despite old upload metadata");
        Check(AttachmentPreviewFormats.Kind("unknown", "application/vnd.openxmlformats-officedocument.presentationml.presentation") == "office", "MIME-only PowerPoint detection");
        Console.WriteLine($"ALL {n} PREVIEW RENDERER CHECKS PASSED");
    }
}
