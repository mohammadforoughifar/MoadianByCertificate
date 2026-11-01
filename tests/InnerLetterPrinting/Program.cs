using Inventory.Api.Services.Office;
using PdfSharp.Pdf.IO;

// Exercises the actual production renderer and assets, never the API Program or a database.
var root = AppContext.BaseDirectory;
var fonts = Path.Combine(root, "Resources", "fonts");
var output = Path.Combine(root, "inner-print-probe");
Directory.CreateDirectory(output);
var hamesh = new[] { new InnerLetterHameshRow("گیرنده آزمایشی هامش", "جهت بررسی و اطلاع", "جهت اطلاع") };
var shortBody = "<p>این متن نمونهٔ نامهٔ داخلی است.</p><p>پاراگراف دوم برای بررسی حفظ شکست خط.</p>";
var longBody = string.Concat(Enumerable.Range(1, 100).Select(i =>
    $"<p>بند {i}: متن آزمایشی برای کنترل ادامهٔ نامه در صفحهٔ بعد و تکرار سربرگ شرکت هومپا بدون حذف هیچ بخش از نامه.</p>"));
Check(InnerLetterPdfRenderer.PlainText("<p>الف</p><p>ب<br>ج</p>") == "الف\nب\nج", "paragraphs and line breaks preserved");

foreach (var a5 in new[] { false, true })
foreach (var longLetter in new[] { false, true })
foreach (var withHamesh in new[] { false, true })
{
    var size = a5 ? "A5" : "A4";
    var background = InnerLetterheadTemplates.LoadHomepa(root, root, a5);
    Check(background.Length > 100_000, "actual company template loaded");
    var rows = longLetter ? Enumerable.Range(1, 60).Select(i => new InnerLetterHameshRow(
        $"گیرنده هامش {i}", "متن طولانی دستور ارجاع برای بررسی ادامهٔ هامش در صفحات بعد و حفظ سربرگ روی همهٔ صفحات", "جهت بررسی")).ToArray() : hamesh;
    var data = new InnerLetterPrintData("1405/1003/12345", new DateTime(2026, 10, 22), "آزمایش نامه داخلی",
        longLetter ? longBody : shortBody, "گیرنده نامه", "فرستنده نامه", rows);
    var pdf = InnerLetterPdfRenderer.Render(data, a5, withHamesh, background, true, fonts);
    using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
    Check(document.PageCount >= (longLetter ? 2 : 1), "long body paginates rather than clipping");
    foreach (var page in document.Pages)
    {
        Check(Math.Abs(page.Width.Point - (a5 ? 419.53 : 595.28)) < 1, "requested page size");
        var objects = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
        Check(objects != null && objects.Elements.Keys.Any(key =>
            objects.Elements.GetDictionary(key)?.Elements.GetName("/Subtype") == "/Image"),
            "company background image is referenced by every page, including overflow");
    }
    var name = $"1003-{size}-{(longLetter ? "long" : "short")}-{(withHamesh ? "with-hamesh" : "without-hamesh")}.pdf";
    File.WriteAllBytes(Path.Combine(output, name), pdf);

    if (longLetter && withHamesh)
    {
        var noHameshPdf = InnerLetterPdfRenderer.Render(data, a5, false, background, true, fonts);
        using var noHameshDocument = PdfReader.Open(new MemoryStream(noHameshPdf), PdfDocumentOpenMode.Import);
        Check(document.PageCount > noHameshDocument.PageCount, "enabled long hamesh flows onto extra pages; disabled hamesh is omitted");
    }
    if (!withHamesh)
    {
        var withoutRows = data with { Hamesh = Array.Empty<InnerLetterHameshRow>() };
        var withoutRowsPdf = InnerLetterPdfRenderer.Render(withoutRows, a5, false, background, true, fonts);
        using var withoutRowsDocument = PdfReader.Open(new MemoryStream(withoutRowsPdf), PdfDocumentOpenMode.Import);
        Check(document.PageCount == withoutRowsDocument.PageCount, "hamesh records do not alter pagination when disabled");
    }
}

var missingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
try
{
    InnerLetterheadTemplates.LoadHomepa(missingRoot, missingRoot, false);
    throw new Exception("Missing company art must not silently print a blank/wrong header.");
}
catch (InnerLetterPrintException) { Console.WriteLine("PASS: missing company art fails explicitly"); }
Console.WriteLine($"PDF probes: {output}. No database was accessed. Inspect all pages for final visual approval.");

static void Check(bool value, string message)
{
    if (!value) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}
