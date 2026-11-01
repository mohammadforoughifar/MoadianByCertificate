using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Inventory.Shared.Security;

/// <summary>
/// Small allow-list sanitizer for rich-text letter bodies. The editor stores HTML, so
/// render only formatting and document-structure markup and discard executable markup.
/// </summary>
public static class RichLetterHtml
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "ol", "ul", "li", "strong", "b", "em", "i", "u", "s", "del",
        "sub", "sup", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "table", "thead",
        "tbody", "tfoot", "tr", "td", "th", "span", "a", "img", "hr"
    };

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
        { "br", "hr", "img" };

    private static readonly Regex DangerousBlocks = new(
        @"<\s*(script|style|iframe|object|embed|svg|math|form|button|input|textarea|select|option|video|audio|source|link|meta)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex DangerousTags = new(
        @"</?\s*(script|style|iframe|object|embed|svg|math|form|button|input|textarea|select|option|video|audio|source|link|meta)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Comments = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]*>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Attributes = new(
        @"(?<name>[^\s=/>]+)(?:\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s>]+)))?",
        RegexOptions.Compiled);

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var safe = UnescapeEscapedMarkup(html!);

        // <pre> در فهرست تگ‌های مجاز نیست؛ برای اینکه متنِ بدنهٔ متنیِ ایمیل‌های قدیمی از
        // دست نرود، آن را به div تبدیل می‌کنیم (شکست خط با white-space حفظ می‌شود).
        safe = Regex.Replace(safe, @"<\s*pre\b", "<div", RegexOptions.IgnoreCase);
        safe = Regex.Replace(safe, @"<\s*/\s*pre\s*>", "</div>", RegexOptions.IgnoreCase);

        safe = DangerousBlocks.Replace(safe, string.Empty);
        safe = DangerousTags.Replace(safe, string.Empty);
        safe = Comments.Replace(safe, string.Empty);
        return Tags.Replace(safe, SanitizeTag);
    }

    /// <summary>
    /// متنی که به‌اشتباه escape شده (مثل <c>&amp;lt;p&amp;gt;</c>) یک‌بار رمزگشایی می‌شود تا
    /// تگ‌هایش واقعی شوند.
    /// <para>
    /// چرا لازم است: بدنهٔ متنیِ بعضی ایمیل‌ها خودش HTML است و در نسخه‌های قبلی وقتی به
    /// متن نامه منتقل می‌شد encode می‌شد؛ نتیجه این بود که در نامه و «دستور کار» به‌جای
    /// پاراگراف، عبارت‌هایی مثل &amp;lt;p&amp;gt; روی صفحه دیده می‌شد. فقط وقتی این
    /// رمزگشایی انجام می‌شود که متن هیچ تگ واقعی نداشته باشد، تا HTML سالم دست‌نخورده بماند.
    /// </para>
    /// </summary>
    private static string UnescapeEscapedMarkup(string html)
    {
        // الگوی «تگِ escape‌شده» مثل &lt;p&gt; یا &lt;br /&gt; → متن یک‌بار رمزگشایی می‌شود.
        // اگر چنین الگویی نبود، متن دست‌نخورده می‌ماند تا HTML سالم تغییر نکند.
        if (!Regex.IsMatch(html, @"&lt;\s*/?\s*[a-zA-Z][^&]{0,300}?&gt;", RegexOptions.IgnoreCase))
            return html;

        return WebUtility.HtmlDecode(html);
    }

    private static string SanitizeTag(Match match)
    {
        var token = match.Value;
        var header = Regex.Match(token, @"^<\s*(?<close>/?)\s*(?<tag>[a-zA-Z][a-zA-Z0-9]*)\b(?<attrs>.*?)\s*/?\s*>$", RegexOptions.Singleline);
        if (!header.Success) return string.Empty;

        var tag = header.Groups["tag"].Value.ToLowerInvariant();
        if (!AllowedTags.Contains(tag)) return string.Empty;
        if (header.Groups["close"].Value == "/") return VoidTags.Contains(tag) ? string.Empty : $"</{tag}>";

        var attrs = new List<string>();
        foreach (Match attribute in Attributes.Matches(header.Groups["attrs"].Value))
        {
            var name = attribute.Groups["name"].Value.ToLowerInvariant();
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || name is "srcdoc" or "formaction" or "xlink:href")
                continue;

            var value = attribute.Groups["dq"].Success ? attribute.Groups["dq"].Value
                : attribute.Groups["sq"].Success ? attribute.Groups["sq"].Value
                : attribute.Groups["bare"].Value;

            if (name == "style")
            {
                var style = SanitizeStyle(value);
                if (style.Length > 0) attrs.Add($"style=\"{HtmlEncoder.Default.Encode(style)}\"");
            }
            else if (name == "href" && tag == "a" && SafeLink(value))
                attrs.Add($"href=\"{HtmlEncoder.Default.Encode(value)}\"");
            else if (name == "src" && tag == "img" && SafeImageSource(value))
                attrs.Add($"src=\"{HtmlEncoder.Default.Encode(value)}\"");
            else if (name == "alt" && tag == "img")
                attrs.Add($"alt=\"{HtmlEncoder.Default.Encode(value)}\"");
            else if (name == "dir" && (value is "rtl" or "ltr" or "auto"))
                attrs.Add($"dir=\"{value}\"");
            else if ((name is "colspan" or "rowspan") && (tag is "td" or "th") && int.TryParse(value, out var span) && span is >= 1 and <= 20)
                attrs.Add($"{name}=\"{span}\"");
            else if ((name is "width" or "height") && (tag is "td" or "th" or "table" or "img") && SafeDimension(value))
                attrs.Add($"{name}=\"{HtmlEncoder.Default.Encode(value)}\"");
        }

        var suffix = VoidTags.Contains(tag) ? " />" : ">";
        return "<" + tag + (attrs.Count == 0 ? string.Empty : " " + string.Join(" ", attrs)) + suffix;
    }

    private static string SanitizeStyle(string source)
    {
        var safe = new List<string>();
        foreach (var declaration in source.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0) continue;
            var name = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim();
            if (value.Length == 0 || value.Contains("url", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("expression", StringComparison.OrdinalIgnoreCase) || value.Contains("var(", StringComparison.OrdinalIgnoreCase) ||
                value.Contains('<') || value.Contains('>')) continue;

            var valid = name switch
            {
                "font-weight" => Regex.IsMatch(value, @"^(normal|bold|[1-9]00)$", RegexOptions.IgnoreCase),
                "font-style" => value is "normal" or "italic" or "oblique",
                "text-decoration" => Regex.IsMatch(value, @"^(none|underline|line-through|overline)(\s+(underline|line-through|overline))*$", RegexOptions.IgnoreCase),
                "text-align" => value is "left" or "right" or "center" or "justify" or "start" or "end",
                "direction" => value is "ltr" or "rtl",
                "unicode-bidi" => value is "normal" or "embed" or "bidi-override" or "isolate",
                "font-size" or "width" or "height" or "max-width" or "min-width" or "margin" or "margin-top" or "margin-bottom" or "padding" or "padding-top" or "padding-bottom" => SafeDimension(value) || value == "auto",
                "font-family" => Regex.IsMatch(value, @"^[\w\s,'""-]{1,120}$"),
                "color" or "background-color" => SafeColor(value),
                "border" or "border-top" or "border-bottom" or "border-left" or "border-right" => SafeBorder(value),
                "border-collapse" => value is "collapse" or "separate",
                "vertical-align" => value is "top" or "middle" or "bottom" or "baseline",
                "white-space" => value is "normal" or "pre" or "pre-wrap" or "nowrap",
                "list-style-type" => value is "decimal" or "decimal-leading-zero" or "lower-alpha" or "upper-alpha" or "lower-roman" or "upper-roman" or "disc" or "circle" or "square",
                _ => false
            };
            if (!valid) continue;

            // ---------- هم‌خوان‌سازی با «کاغذ سفید» نامه ----------
            // بدنهٔ ایمیل‌های تبلیغاتی معمولاً «متن سفید روی پس‌زمینهٔ تیره» است. اگر همان
            // رنگ‌ها بدون تغییر داخل نامه/کادر فرم بریزند، متن ناپیدا می‌شود و پس‌زمینهٔ
            // تیره هم کادر را سیاه نشان می‌دهد. پس رنگ متنِ روشن تیره و پس‌زمینهٔ تیره
            // حذف می‌شود تا نامه همیشه خوانا بماند.
            if (name == "color" && TryLuminance(value, out var textLum) && textLum >= 0.72)
                value = "#1f2937";
            else if (name == "background-color" && TryLuminance(value, out var bgLum) && bgLum <= 0.30)
                continue;

            safe.Add($"{name}: {value}");
        }
        return string.Join("; ", safe);
    }

    private static bool SafeLink(string value)
    {
        var trimmed = WebUtility.HtmlDecode(value).Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsControl)) return false;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https" or "mailto" or "tel";
        return !trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith("\\\\", StringComparison.Ordinal);
    }

    private static bool SafeImageSource(string value)
    {
        var trimmed = WebUtility.HtmlDecode(value).Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsControl)) return false;
        if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return Regex.IsMatch(trimmed, @"^data:image/(png|jpeg|gif|webp|bmp);base64,[A-Za-z0-9+/=]+$", RegexOptions.IgnoreCase);
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https";
        return !trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith("\\\\", StringComparison.Ordinal);
    }

    /// <summary>روشنایی نسبی رنگ (۰=سیاه، ۱=سفید) — برای تشخیص متن سفید/پس‌زمینهٔ تیره.</summary>
    private static bool TryLuminance(string value, out double luminance)
    {
        luminance = 0.5;
        var v = (value ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return false;

        int r, g, b;
        if (v.StartsWith('#') && (v.Length == 4 || v.Length == 7 || v.Length == 9))
        {
            var hex = v[1..];
            if (hex.Length is 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            if (hex.Length >= 6 && int.TryParse(hex[..6], System.Globalization.NumberStyles.HexNumber, null, out var packed))
            {
                r = (packed >> 16) & 0xFF; g = (packed >> 8) & 0xFF; b = packed & 0xFF;
            }
            else return false;
        }
        else if (v.StartsWith("rgb"))
        {
            var open = v.IndexOf('(');
            if (open < 0) return false;
            var inside = v[(open + 1)..].TrimEnd(')');
            var parts = inside.Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;
            var rgb = new int[3];
            for (var i = 0; i < 3; i++)
            {
                var part = parts[i];
                var percent = part.EndsWith('%');
                if (percent) part = part[..^1];
                if (!int.TryParse(part, out var n)) return false;
                rgb[i] = Math.Clamp(percent ? (int)Math.Round(n * 2.55) : n, 0, 255);
            }
            r = rgb[0]; g = rgb[1]; b = rgb[2];
        }
        else if (NamedColors.TryGetValue(v, out var named))
        {
            r = named.R; g = named.G; b = named.B;
        }
        else return false;

        luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
        return true;
    }

    private static readonly Dictionary<string, (int R, int G, int B)> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["white"] = (255, 255, 255), ["whitesmoke"] = (245, 245, 245), ["snow"] = (255, 250, 250),
        ["ivory"] = (255, 255, 240), ["floralwhite"] = (255, 250, 240), ["ghostwhite"] = (248, 248, 255),
        ["lightgray"] = (211, 211, 211), ["lightgrey"] = (211, 211, 211), ["gainsboro"] = (220, 220, 220),
        ["silver"] = (192, 192, 192), ["black"] = (0, 0, 0), ["midnightblue"] = (25, 25, 112),
        ["navy"] = (0, 0, 128), ["darkblue"] = (0, 0, 139), ["darkgray"] = (169, 169, 169),
        ["darkgrey"] = (169, 169, 169), ["dimgray"] = (105, 105, 105), ["dimgrey"] = (105, 105, 105),
        ["transparent"] = (255, 255, 255)
    };

    private static bool SafeDimension(string value) =>
        Regex.IsMatch(value.Trim(), @"^\d{1,4}(?:\.\d{1,2})?(?:px|pt|em|rem|%)?$", RegexOptions.IgnoreCase);

    private static bool SafeColor(string value) =>
        Regex.IsMatch(value.Trim(), @"^(#[0-9a-f]{3,8}|[a-z]{1,24}|rgba?\(\s*[0-9.%\s,]+\))$", RegexOptions.IgnoreCase);

    private static bool SafeBorder(string value) =>
        Regex.IsMatch(value.Trim(), @"^(?:\d{1,3}(?:\.\d{1,2})?(?:px|pt)\s+)?(none|solid|dotted|dashed|double)(?:\s+(#[0-9a-f]{3,8}|[a-z]{1,24}))?$", RegexOptions.IgnoreCase);
}
