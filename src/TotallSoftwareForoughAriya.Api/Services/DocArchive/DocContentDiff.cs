using System.IO.Compression;
using System.Xml.Linq;
using Inventory.Shared.Dtos;
namespace Inventory.Api.Services.DocArchive;

public static class DocContentDiff
{
    public static DocContentCompareDto Text(string left, string right)
    {
        // Bound quadratic alignment; never label a partial comparison as identical.
        var result = new DocContentCompareDto();
        const int limit = 200_000;
        if (left.Length > limit || right.Length > limit) { result.Truncated = true; left = left[..Math.Min(limit, left.Length)]; right = right[..Math.Min(limit, right.Length)]; }
        string[] Lines(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var a = Lines(left); var b = Lines(right);
        if (a.Length > 2000 || b.Length > 2000 || (long)a.Length * b.Length > 2_000_000)
        {
            result.Truncated = true;
            result.Notice = "فایل بزرگ است؛ مقایسه محدود به خطوط هم‌موقعیت است و هم‌ترازی کامل انجام نشده.";
            for (var k = 0; k < Math.Min(2000, Math.Max(a.Length, b.Length)); k++) Add(result, $"خط {k + 1}", a.ElementAtOrDefault(k), b.ElementAtOrDefault(k));
            return result;
        }
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var x = a.Length - 1; x >= 0; x--) for (var y = b.Length - 1; y >= 0; y--) lcs[x, y] = a[x] == b[y] ? 1 + lcs[x + 1, y + 1] : Math.Max(lcs[x + 1, y], lcs[x, y + 1]);
        int i = 0, j = 0;
        while (i < a.Length || j < b.Length)
        {
            if (i < a.Length && j < b.Length && a[i] == b[j]) { Add(result, $"{i + 1} ← {j + 1}", a[i++], b[j++]); }
            else if (j < b.Length && (i == a.Length || lcs[i, j + 1] > lcs[i + 1, j])) Add(result, $"جدید {j + 1}", null, b[j++]);
            else Add(result, $"قدیم {i + 1}", a[i++], null);
        }
        if (result.Truncated) result.Notice = "بخشی از متن به دلیل محدودیت حجم مقایسه نشده است.";
        return result;
    }
    public static void Add(DocContentCompareDto result, string field, string? a, string? b) => result.Rows.Add(new DocVersionDiffRow
    {
        Field = field,
        Left = a,
        Right = b,
        Kind = a == b ? "same" : a == null ? "added" : b == null ? "removed" : "changed"
    });

    public static Dictionary<string, string> Cells(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (zip.Entries.Sum(e => e.Length) > 30_000_000 || zip.Entries.Count > 2000) throw new InvalidOperationException("فایل Excel برای مقایسه مستقیم بیش از حد بزرگ است.");
        XDocument? Read(string path) { var entry = zip.GetEntry(path); if (entry == null) return null; using var stream = entry.Open(); return XDocument.Load(stream); }
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var strings = Read("xl/sharedStrings.xml")?.Descendants(s + "si").Select(si => string.Concat(si.Descendants(s + "t").Select(t => t.Value))).ToArray() ?? Array.Empty<string>();
        var relationships = Read("xl/_rels/workbook.xml.rels")?.Descendants(rel + "Relationship").ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!) ?? new();
        var book = Read("xl/workbook.xml") ?? throw new InvalidOperationException("ساختار Excel معتبر نیست.");
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sheet in book.Descendants(s + "sheet"))
        {
            var name = (string?)sheet.Attribute("name") ?? "Sheet";
            if (!relationships.TryGetValue((string?)sheet.Attribute(r + "id") ?? "", out var target)) throw new InvalidOperationException("رابط شیت Excel یافت نشد.");
            var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            var xml = Read(path) ?? throw new InvalidOperationException("محتوای شیت Excel یافت نشد.");
            foreach (var cell in xml.Descendants(s + "c"))
            {
                var address = (string?)cell.Attribute("r"); if (address == null) continue;
                var value = cell.Element(s + "v")?.Value ?? "";
                var type = (string?)cell.Attribute("t");
                if (type == "s" && int.TryParse(value, out var index) && index >= 0 && index < strings.Length) value = strings[index];
                if (type == "inlineStr") value = string.Concat(cell.Descendants(s + "t").Select(t => t.Value));
                var formula = cell.Element(s + "f");
                if (formula != null) value = $"={formula.Value}\n{value}";
                if (value.Length == 0) continue;
                if (value.Length > 10000 || cells.Count >= 10000) throw new InvalidOperationException("سقف مقایسه Excel ده‌هزار سلول غیرخالی است.");
                cells[name + "!" + address] = value;
            }
        }
        return cells;
    }
    public static DocContentCompareDto Spreadsheet(byte[] left, byte[] right)
    {
        var a = Cells(left); var b = Cells(right); var result = new DocContentCompareDto { Mode = "Cells", Notice = "مقایسه مقدار ذخیره‌شده و فرمول سلول‌ها؛ قالب‌بندی و محاسبه مجدد فرمول‌ها شامل نمی‌شود." };
        foreach (var key in a.Keys.Union(b.Keys).OrderBy(k => k, StringComparer.Ordinal)) Add(result, key, a.GetValueOrDefault(key), b.GetValueOrDefault(key));
        return result;
    }
}
