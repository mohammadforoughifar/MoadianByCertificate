using System.Globalization;
using System.Xml.Linq;

namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// مختصات، رنگ، اندازه و ترازِ سه مقدار «شماره»، «تاریخ» و «پیوست» را از خودِ قالب
/// چاپ هر شرکت می‌خواند:
/// <c>Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{A4|A5}.mrt</c>
///
/// چرا لازم است؟
/// تصویر سربرگ از <c>Watermark.ImageBytes</c> همان قالب خوانده می‌شود، ولی جای
/// این سه مقدار تا پیش از این با اعداد ثابت داخل کدِ چاپ تعیین می‌شد. برای همین
/// هر شرکتی جز هومپا (۱۰۰۳) و آریا (۱۰۰۵) درست چاپ نمی‌شد؛ مثلاً قالب‌های شرکت
/// ۱ و ۲ (طرح فروغ آریا) این مقادیر را <c>Black</c> روی کاغذ سفید اعلام کرده‌اند
/// اما کد متن سفید می‌نوشت و مقادیر عملاً نامرئی می‌شد.
///
/// اکنون همان چیزی که طراح در قالب گذاشته (مستطیل، رنگ، فونت، تراز) مبنای چاپ
/// است؛ بنابراین هر شناسهٔ شرکتی که قالبش این سه مقدار را اعلام کند، روی برچسب
///‌های همان طرح چاپ می‌شود. اگر قالب موجود نباشد یا این سه جزء را اعلام نکند،
/// <c>null</c> برمی‌گردد و مسیر قبلی (چیدمان ثابت کد) دست‌نخورده اجرا می‌شود.
/// </summary>
internal sealed record LetterheadMetaFields(LetterheadMetaFields.Field Number, LetterheadMetaFields.Field Date, LetterheadMetaFields.Field Attachment)
{
    /// <summary>یک مقدار متنی روی صفحه، با مختصات فیزیکی point از گوشهٔ بالا-چپ.</summary>
    internal sealed record Field(
        float Left,
        float Top,
        float Width,
        float Height,
        string Color,
        float FontSize,
        bool Bold,
        bool Center,
        bool IsolateLtr);

    private const string NumberColumn = "LetterNumber";
    private const string DateColumn = "DateShams";
    private const string AttachmentColumn = "Peyvast";

    /// <summary>خواندن سه مقدار از فایل MRT؛ هر خطا/نبود اطلاعات یعنی <c>null</c> (بدون استثنا).</summary>
    internal static LetterheadMetaFields? TryRead(string? templatePath)
    {
        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath)) return null;

        try
        {
            using var stream = File.OpenRead(templatePath);
            var root = XDocument.Load(stream, LoadOptions.None).Root;
            if (root == null) return null;

            var factor = UnitToPoints(root.Elements().FirstOrDefault(element => element.Name.LocalName == "ReportUnit")?.Value);

            // قالب ممکن است چند صفحه داشته باشد؛ مقادیر شبکهٔ نامه در صفحهٔ اول هستند.
            var pages = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "Pages");
            var page = pages?.Elements().FirstOrDefault();
            if (page == null) return null;

            var margins = page.Elements().FirstOrDefault(element => element.Name.LocalName == "Margins")?.Value;
            var (marginLeft, marginTop) = ParseMargins(margins);

            var components = page.Elements().FirstOrDefault(element => element.Name.LocalName == "Components")
                ?.Elements().ToList() ?? new List<XElement>();

            Field? Read(string column)
            {
                var component = components.FirstOrDefault(candidate =>
                    (candidate.Elements().FirstOrDefault(element => element.Name.LocalName == "Text")?.Value ?? string.Empty)
                        .Contains(column, StringComparison.OrdinalIgnoreCase));
                if (component == null) return null;

                var rectangle = component.Elements().FirstOrDefault(element => element.Name.LocalName == "ClientRectangle")?.Value;
                if (!TryParseRectangle(rectangle, out var x, out var y, out var width, out var height)) return null;

                var font = component.Elements().FirstOrDefault(element => element.Name.LocalName == "Font")?.Value;
                var (fontSize, bold) = ParseFont(font);

                var center = string.Equals(
                    component.Elements().FirstOrDefault(element => element.Name.LocalName == "HorAlignment")?.Value?.Trim(),
                    "Center", StringComparison.OrdinalIgnoreCase);

                return new Field(
                    (marginLeft + x) * factor,
                    (marginTop + y) * factor,
                    Math.Max(width * factor, 8f),
                    Math.Max(height * factor, 6f),
                    ParseColor(component.Elements().FirstOrDefault(element => element.Name.LocalName == "TextBrush")?.Value),
                    fontSize <= 0f ? 8f : fontSize,
                    bold,
                    center,
                    // شماره و تاریخ ارقام/قالب لاتین دارند و باید در متن RTL جدا بمانند.
                    column is NumberColumn or DateColumn);
            }

            var number = Read(NumberColumn);
            var date = Read(DateColumn);
            var attachment = Read(AttachmentColumn);
            if (number == null || date == null || attachment == null) return null;

            return new LetterheadMetaFields(number, date, attachment);
        }
        catch
        {
            // قالب خراب/ناقص نباید چاپ را متوقف کند؛ فراخوان به مسیر پیش‌فرض برمی‌گردد.
            return null;
        }
    }

    /// <summary>واحد گزارش استیمول‌سافت → point (پیش‌فرض قالب‌های این پروژه Inches است).</summary>
    private static float UnitToPoints(string? unit) => (unit ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "centimeters" or "centimeter" => 72f / 2.54f,
        "millimeters" or "millimeter" => 72f / 25.4f,
        "hundredthsofinch" => 0.72f,
        "tenthsofmillimeter" => 72f / 254f,
        _ => 72f,
    };

    /// <summary>
    /// ترتیب مقادیر <c>Margins</c> در قالب: چپ، راست، بالا، پایین
    /// (همان قراردادی که مختصات کالیبره‌شدهٔ هومپا/آریا از آن ساخته شده است).
    /// </summary>
    private static (float Left, float Top) ParseMargins(string? value)
    {
        var parts = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f)
            .ToArray();
        return parts.Length switch
        {
            0 => (0f, 0f),
            1 => (parts[0], parts[0]),
            2 => (parts[0], parts[1]),
            _ => (parts[0], parts[2]),
        };
    }

    private static bool TryParseRectangle(string? value, out float x, out float y, out float width, out float height)
    {
        x = y = width = height = 0f;
        var parts = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4) return false;

        var numbers = new float[4];
        for (var index = 0; index < 4; index++)
        {
            if (!float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index]))
                return false;
        }

        x = numbers[0];
        y = numbers[1];
        width = numbers[2];
        height = numbers[3];
        return true;
    }

    /// <summary>«Vazirmatn,12,Bold» → اندازه ۱۲ و پرچم توپُر.</summary>
    private static (float Size, bool Bold) ParseFont(string? value)
    {
        var parts = (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var size = 0f;
        foreach (var part in parts)
        {
            if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0f)
            {
                size = parsed;
                break;
            }
        }

        var bold = parts.Any(part => part.Equals("Bold", StringComparison.OrdinalIgnoreCase));
        return (size, bold);
    }

    /// <summary>رنگ متنِ اعلام‌شده در قالب؛ ناشناخته‌ها با خاکستری تیره (خوانا روی کاغذ سفید) چاپ می‌شوند.</summary>
    private static string ParseColor(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return "#333333";
        if (text.StartsWith("#", StringComparison.Ordinal)) return text;
        if (text.Contains(',')) return "#333333";
        return text.ToLowerInvariant() switch
        {
            "white" => "#FFFFFF",
            "black" => "#000000",
            _ => "#333333",
        };
    }
}
