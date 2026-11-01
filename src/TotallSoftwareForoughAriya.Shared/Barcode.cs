using System.Text;

namespace Inventory.Shared;

/// <summary>
/// تولید بارکد به‌صورت SVG — کاملاً مستقل و بدون وابستگی خارجی.
///
/// چرا SVG؟ برچسب‌ها باید هم در پیش‌نمایش مرورگر و هم در چاپ برداری و خوانا باشند
/// و هیچ کتابخانه یا فونت بیرونی هم لازم نشود.
///
/// پشتیبانی: Code 128 (زیرمجموعه B و C) و EAN-13 / EAN-8.
/// </summary>
public static class Barcode
{
    // =====================================================================
    // ۱) الگوی میله‌های Code 128
    // هر عنصر ۱۱ ماژول است؛ رشته‌ی زیر پهنای شش بخش متوالی را نگه می‌دارد
    // (میله، فاصله، میله، فاصله، میله، فاصله).
    // =====================================================================
    private static readonly string[] Code128Patterns =
    {
        "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
        "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
        "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
        "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
        "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
        "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
        "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
        "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
        "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
        "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
        "114131","311141","411131","211412","211214","211232","2331112"
    };

    private const int StartB = 104;
    private const int StartC = 105;
    private const int CodeB = 100;
    private const int CodeC = 99;
    private const int Stop = 106;

    // =====================================================================
    // ۲) جدول‌های EAN
    // =====================================================================
    private static readonly string[] EanL =
    {
        "0001101","0011001","0010011","0111101","0100011",
        "0110001","0101111","0111011","0110111","0001011"
    };

    private static readonly string[] EanG =
    {
        "0100111","0110011","0011011","0100001","0011101",
        "0111001","0000101","0010001","0001001","0010111"
    };

    private static readonly string[] EanR =
    {
        "1110010","1100110","1101100","1000010","1011100",
        "1001110","1010000","1000100","1001000","1110100"
    };

    /// <summary>الگوی L/G شش رقم سمت چپ بر اساس رقم اول EAN-13</summary>
    private static readonly string[] EanParity =
    {
        "LLLLLL","LLGLGG","LLGGLG","LLGGGL","LGLLGG",
        "LGGLLG","LGGGLL","LGLGLG","LGLGGL","LGGLGL"
    };

    // =====================================================================
    // ۳) API عمومی
    // =====================================================================

    /// <summary>
    /// تولید SVG بارکد. اگر داده معتبر نباشد یک SVG با پیام خطا برمی‌گرداند
    /// تا صفحه‌ی چاپ برچسب هیچ‌وقت نشکند.
    /// </summary>
    /// <param name="data">محتوای بارکد</param>
    /// <param name="type">استاندارد بارکد</param>
    /// <param name="heightPx">ارتفاع میله‌ها به پیکسل</param>
    /// <param name="moduleWidth">پهنای باریک‌ترین میله به پیکسل</param>
    /// <param name="showText">نمایش متن زیر بارکد</param>
    public static string ToSvg(string? data, BarcodeType type = BarcodeType.Code128,
        int heightPx = 48, double moduleWidth = 1.6, bool showText = true)
    {
        if (string.IsNullOrWhiteSpace(data)) return Placeholder("بدون بارکد");

        var value = data.Trim();

        try
        {
            var bits = type switch
            {
                BarcodeType.Ean13 => EncodeEan13(value, out value),
                BarcodeType.Ean8 => EncodeEan8(value, out value),
                _ => EncodeCode128(value)
            };

            return Render(bits, value, heightPx, moduleWidth, showText);
        }
        catch (Exception ex)
        {
            return Placeholder(ex.Message);
        }
    }

    /// <summary>آیا این مقدار برای استاندارد داده‌شده معتبر است؟</summary>
    public static bool IsValid(string? data, BarcodeType type)
    {
        if (string.IsNullOrWhiteSpace(data)) return false;
        var v = data.Trim();

        return type switch
        {
            BarcodeType.Ean13 => v.Length is 12 or 13 && v.All(char.IsDigit)
                                 && (v.Length == 12 || v[12] == CheckDigit(v[..12])),
            BarcodeType.Ean8 => v.Length is 7 or 8 && v.All(char.IsDigit)
                                && (v.Length == 7 || v[7] == CheckDigit(v[..7])),
            BarcodeType.Code128 => v.All(c => c >= 32 && c < 127),
            _ => true
        };
    }

    /// <summary>محاسبه رقم کنترلی EAN (روی ۱۲ یا ۷ رقم اول).</summary>
    public static char CheckDigit(string digits)
    {
        var sum = 0;
        // از راست به چپ: رقم اول ضریب ۳
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? d * 3 : d;
        }
        return (char)('0' + (10 - sum % 10) % 10);
    }

    /// <summary>
    /// ساخت یک EAN-13 داخلی معتبر از روی شناسه کالا.
    /// پیشوند ۲۰۰ در استاندارد GS1 برای استفاده‌ی داخلی فروشگاه رزرو شده است.
    /// </summary>
    public static string BuildInternalEan13(int productId, string prefix = "200")
    {
        var body = (prefix + productId.ToString("D9"));
        if (body.Length > 12) body = body[..12];
        body = body.PadRight(12, '0');
        return body + CheckDigit(body);
    }

    // =====================================================================
    // ۴) Code 128
    // =====================================================================

    private static List<bool> EncodeCode128(string value)
    {
        foreach (var c in value)
            if (c < 32 || c > 126)
                throw new InvalidOperationException("Code 128 فقط کاراکترهای ASCII چاپی را می‌پذیرد.");

        var codes = new List<int>();

        // اگر همه رقم و تعداد زوج باشد، زیرمجموعه C فشرده‌تر است
        var allDigits = value.Length >= 4 && value.Length % 2 == 0 && value.All(char.IsDigit);

        if (allDigits)
        {
            codes.Add(StartC);
            for (var i = 0; i < value.Length; i += 2)
                codes.Add(int.Parse(value.Substring(i, 2)));
        }
        else
        {
            codes.Add(StartB);
            foreach (var c in value) codes.Add(c - 32);
        }

        // رقم کنترلی: مجموع وزنی به پیمانه ۱۰۳
        var sum = codes[0];
        for (var i = 1; i < codes.Count; i++) sum += codes[i] * i;
        codes.Add(sum % 103);
        codes.Add(Stop);

        var bits = new List<bool>();
        foreach (var code in codes)
        {
            var pattern = Code128Patterns[code];
            var bar = true;                       // هر الگو با میله شروع می‌شود
            foreach (var ch in pattern)
            {
                var width = ch - '0';
                for (var i = 0; i < width; i++) bits.Add(bar);
                bar = !bar;
            }
        }
        return bits;
    }

    // =====================================================================
    // ۵) EAN-13 / EAN-8
    // =====================================================================

    private static List<bool> EncodeEan13(string value, out string normalized)
    {
        if (!value.All(char.IsDigit))
            throw new InvalidOperationException("EAN-13 فقط رقم می‌پذیرد.");

        if (value.Length == 12) value += CheckDigit(value);
        if (value.Length != 13)
            throw new InvalidOperationException("EAN-13 باید ۱۲ یا ۱۳ رقم باشد.");
        if (value[12] != CheckDigit(value[..12]))
            throw new InvalidOperationException("رقم کنترلی EAN-13 نادرست است.");

        normalized = value;

        var sb = new StringBuilder();
        sb.Append("101");                                     // گارد شروع

        var parity = EanParity[value[0] - '0'];
        for (var i = 1; i <= 6; i++)
        {
            var d = value[i] - '0';
            sb.Append(parity[i - 1] == 'L' ? EanL[d] : EanG[d]);
        }

        sb.Append("01010");                                   // گارد میانی
        for (var i = 7; i <= 12; i++) sb.Append(EanR[value[i] - '0']);
        sb.Append("101");                                     // گارد پایان

        return sb.ToString().Select(c => c == '1').ToList();
    }

    private static List<bool> EncodeEan8(string value, out string normalized)
    {
        if (!value.All(char.IsDigit))
            throw new InvalidOperationException("EAN-8 فقط رقم می‌پذیرد.");

        if (value.Length == 7) value += CheckDigit(value);
        if (value.Length != 8)
            throw new InvalidOperationException("EAN-8 باید ۷ یا ۸ رقم باشد.");

        normalized = value;

        var sb = new StringBuilder();
        sb.Append("101");
        for (var i = 0; i < 4; i++) sb.Append(EanL[value[i] - '0']);
        sb.Append("01010");
        for (var i = 4; i < 8; i++) sb.Append(EanR[value[i] - '0']);
        sb.Append("101");

        return sb.ToString().Select(c => c == '1').ToList();
    }

    // =====================================================================
    // ۶) رندر SVG
    // =====================================================================

    private static string Render(List<bool> bits, string text, int height, double module, bool showText)
    {
        var quiet = module * 10;                       // حاشیه سفید دو طرف
        var width = bits.Count * module + quiet * 2;
        var textH = showText ? 14 : 0;
        var totalH = height + textH + 4;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(width)} {F(totalH)}\" ");
        sb.Append($"width=\"100%\" height=\"{F(totalH)}\" preserveAspectRatio=\"xMidYMid meet\" shape-rendering=\"crispEdges\">");
        sb.Append($"<rect width=\"{F(width)}\" height=\"{F(totalH)}\" fill=\"#fff\"/>");

        // میله‌های متوالی را در یک مستطیل ادغام می‌کنیم تا SVG سبک بماند
        var x = quiet;
        var i = 0;
        while (i < bits.Count)
        {
            if (!bits[i]) { x += module; i++; continue; }

            var run = 0;
            while (i + run < bits.Count && bits[i + run]) run++;

            sb.Append($"<rect x=\"{F(x)}\" y=\"0\" width=\"{F(run * module)}\" height=\"{height}\" fill=\"#000\"/>");
            x += run * module;
            i += run;
        }

        if (showText && !string.IsNullOrEmpty(text))
        {
            var fontSize = Math.Max(9, Math.Min(13, module * 7));
            sb.Append($"<text x=\"{F(width / 2)}\" y=\"{F(totalH - 2)}\" text-anchor=\"middle\" ");
            sb.Append($"font-family=\"monospace\" font-size=\"{F(fontSize)}\" fill=\"#000\" letter-spacing=\"1\">");
            sb.Append(Escape(text));
            sb.Append("</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Placeholder(string message) =>
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 40\" width=\"100%\" height=\"40\">" +
        "<rect width=\"200\" height=\"40\" fill=\"#fef2f2\" stroke=\"#fecaca\"/>" +
        "<text x=\"100\" y=\"24\" text-anchor=\"middle\" font-size=\"10\" fill=\"#b91c1c\">" +
        Escape(message) + "</text></svg>";

    private static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
