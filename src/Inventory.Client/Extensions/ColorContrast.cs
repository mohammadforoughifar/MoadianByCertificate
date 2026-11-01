using System.Globalization;

namespace Inventory.Client.Extensions;

/// <summary>
/// ابزار کنتراست رنگ بر اساس فرمول WCAG 2.1.
///
/// چرا لازم است؟ رنگ برچسب‌ها (تگ‌های آرشیو اسناد) را خود کاربر انتخاب می‌کند و ممکن است
/// روشن یا میانه باشد (زرد، سبز مغزپسته‌ای، فیروزه‌ای، آبی #0284c7…). اگر متن همیشه سفید
/// باشد خوانده نمی‌شود. اینجا برای هر پس‌زمینه «خواناترین جفت رنگ» ساخته می‌شود:
/// ۱) اگر متن سفید روی رنگ کاربر کنتراست ≥ ۴.۵ داشته باشد → همان رنگ + متن سفید،
/// ۲) وگرنه اگر متن جوهری تیره ≥ ۴.۵ باشد → همان رنگ + متن تیره،
/// ۳) وگرنه (رنگ میانه) همان رنگ با «حداقل» تغییر تیره می‌شود تا متن سفید خوانا شود؛
///    یعنی رنگ کاربر هرگز به رنگ دیگری تبدیل نمی‌شود، فقط در صورت لزوم کمی تیره‌تر می‌شود.
/// </summary>
public static class ColorContrast
{
    /// <summary>سفید — برای پس‌زمینه‌های تیره.</summary>
    public const string OnDark = "#ffffff";

    /// <summary>جوهری تیره — برای پس‌زمینه‌های روشن (کنتراست ~۱۷ روی سفید).</summary>
    public const string OnLight = "#101828";

    /// <summary>آستانهٔ WCAG AA برای متن معمولی.</summary>
    public const double MinRatio = 4.5;

    /// <summary>جفت رنگ خوانا: پس‌زمینهٔ قابل‌نمایش + رنگ متن.</summary>
    public readonly record struct ReadablePair(string Background, string Text, double Ratio);

    /// <summary>
    /// خواناترین رنگ متن برای قرار گرفتن روی <paramref name="background"/> (بدون تغییر پس‌زمینه).
    /// اگر رنگ قابل تشخیص نباشد، متن تیره برمی‌گردد (پس‌زمینهٔ روشن فرض می‌شود).
    /// </summary>
    public static string ReadableTextOn(this string? background)
    {
        if (!TryParseColor(background, out var bg)) return OnLight;
        var white = Ratio(Luminance(bg), Luminance((255, 255, 255)));
        var dark = Ratio(Luminance(bg), Luminance((16, 24, 40)));
        return white >= dark ? OnDark : OnLight;
    }

    /// <summary>
    /// جفت رنگ تضمین‌شده برای بج/چیپ: پس‌زمینه همان رنگ کاربر است مگر اینکه با هیچ متنی
    /// به کنتراست ۴.۵ نرسد که در آن صورت با حداقل تغییر تیره/روشن می‌شود.
    /// </summary>
    public static ReadablePair ReadablePairOn(this string? background)
    {
        if (!TryParseColor(background, out var bg))
            return new ReadablePair("#f1f5f9", OnLight, Ratio(Luminance((241, 245, 249)), Luminance((16, 24, 40))));

        var whiteRatio = Ratio(Luminance(bg), Luminance((255, 255, 255)));
        if (whiteRatio >= MinRatio) return new ReadablePair(ToHex(bg), OnDark, whiteRatio);

        var darkRatio = Ratio(Luminance(bg), Luminance((16, 24, 40)));
        if (darkRatio >= MinRatio) return new ReadablePair(ToHex(bg), OnLight, darkRatio);

        // رنگ میانه (مثل #0284c7): نه سفید روی آن خواناست نه جوهری. پس‌زمینه را با گام‌های
        // کوچک تیره می‌کنیم تا متن سفید از آستانهٔ ۴.۵ عبور کند — رنگ همان فام می‌ماند،
        // فقط کمی تیره‌تر می‌شود (ظاهر بج/چیپ هم طبیعی‌تر است تا روشن‌کردنش).
        var current = bg;
        var best = whiteRatio;

        for (var step = 1; step <= 14 && best < MinRatio; step++)
        {
            current = Mix(bg, (0, 0, 0), 0.06 * step);
            best = Ratio(Luminance(current), Luminance((255, 255, 255)));
        }

        return new ReadablePair(ToHex(current), OnDark, best);
    }

    /// <summary>
    /// استایل آمادهٔ inline برای بج/چیپ رنگی — به‌شکل
    /// <c>background-color:#0369a1;color:#ffffff</c>.
    /// </summary>
    public static string ReadableBadgeStyle(this string? background)
    {
        var pair = background.ReadablePairOn();
        return $"background-color:{pair.Background};color:{pair.Text}";
    }

    /// <summary>
    /// رنگ متن سفید روی این پس‌زمینه خوانا هست؟ (آستانهٔ WCAG AA برای متن معمولی: ۴.۵)
    /// </summary>
    public static bool WhiteIsReadableOn(this string? background, double minRatio = MinRatio)
    {
        if (!TryParseColor(background, out var bg)) return false;
        return Ratio(Luminance(bg), Luminance((255, 255, 255))) >= minRatio;
    }

    /// <summary>نسبت کنتراست دو رنگ (بین ۱ و ۲۱).</summary>
    public static double ContrastRatio(string? colorA, string? colorB)
    {
        if (!TryParseColor(colorA, out var a) || !TryParseColor(colorB, out var b)) return 21;
        return Ratio(Luminance(a), Luminance(b));
    }

    /// <summary>تجزیهٔ رنگ‌های رایج CSS: #rgb ، #rrggbb ، #rrggbbaa و rgb()/rgba().</summary>
    public static bool TryParseColor(string? value, out (byte R, byte G, byte B) rgb)
    {
        rgb = (0, 0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        var c = value.Trim();

        if (c.StartsWith('#'))
        {
            var hex = c[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            if ((hex.Length == 6 || hex.Length == 8) && IsHex(hex))
            {
                rgb = (
                    byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                return true;
            }
            return false;
        }

        if (c.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var open = c.IndexOf('(');
            var close = c.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                var nums = c[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries);
                if (nums.Length >= 3 &&
                    byte.TryParse(nums[0], out var r) && byte.TryParse(nums[1], out var g) && byte.TryParse(nums[2], out var b))
                {
                    rgb = (r, g, b);
                    return true;
                }
            }
        }

        return false;
    }

    static bool IsHex(string s) => s.Length > 0 && s.All(Uri.IsHexDigit);

    /// <summary>روشنایی نسبی (relative luminance) طبق WCAG.</summary>
    static double Luminance((byte R, byte G, byte B) c)
    {
        static double Channel(byte v)
        {
            var s = v / 255d;
            return s <= 0.03928d ? s / 12.92d : Math.Pow((s + 0.055d) / 1.055d, 2.4d);
        }
        return 0.2126d * Channel(c.R) + 0.7152d * Channel(c.G) + 0.0722d * Channel(c.B);
    }

    static double Ratio(double l1, double l2)
    {
        if (l1 < l2) (l1, l2) = (l2, l1);
        return (l1 + 0.05d) / (l2 + 0.05d);
    }

    static (byte R, byte G, byte B) Mix((byte R, byte G, byte B) from, (byte R, byte G, byte B) to, double amount)
    {
        if (amount > 1) amount = 1;
        if (amount < 0) amount = 0;
        byte Ch(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return (Ch(from.R, to.R), Ch(from.G, to.G), Ch(from.B, to.B));
    }

    static string ToHex((byte R, byte G, byte B) c) =>
        string.Create(CultureInfo.InvariantCulture, $"#{c.R:x2}{c.G:x2}{c.B:x2}");
}
