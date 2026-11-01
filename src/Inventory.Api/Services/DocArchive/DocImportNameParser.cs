using System.Text;
using System.Text.RegularExpressions;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// تحلیل نام فایل بر اساس الگوی «عنوان+شماره».
/// قواعد دقیقاً همان قواعد اسکریپت اسکن لپ‌تاپ (scan-archive-folder.ps1) است تا
/// عددِ گزارش اسکن با عددِ اعتبارسنجی سرور یکی باشد.
/// </summary>
public static partial class DocImportNameParser
{
    public const char Separator = '+';
    public const int MaxTitleLength = 250;
    public const int MaxCodeLength = 80;
    public const int MaxFileNameLength = 255;

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"^[\p{L}\p{Nd} \-_\.]+$")]
    private static partial Regex CodeAllowedRegex();

    /// <summary>یکسان‌سازی حروف عربی به فارسی و یک‌دست کردن فاصله‌ها.</summary>
    public static string NormalizeTitle(string? text)
    {
        var t = text ?? "";
        t = t.Replace('\u064A', '\u06CC')   // ي → ی
             .Replace('\u0643', '\u06A9')   // ك → ک
             .Replace('\u0629', '\u0647')   // ة → ه
             .Replace('\u0649', '\u06CC');  // ى → ی
        return SpaceRegex().Replace(t, " ").Trim();
    }

    /// <summary>
    /// نرمال‌سازی شماره برای مقایسه و ذخیره: عربی به فارسی، ارقام به لاتین،
    /// حذف نیم‌فاصله، بدون حساسیت به بزرگی حروف.
    /// </summary>
    public static string NormalizeCode(string? text)
    {
        var t = NormalizeTitle(text).Replace("\u200C", "").Replace("\u200D", "");
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
        {
            if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)(ch - '\u06F0' + '0'));
            else if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)(ch - '\u0660' + '0'));
            else sb.Append(ch);
        }
        return sb.ToString().ToUpperInvariant();
    }

    /// <summary>
    /// برداشتن پسوند از نام فایل. برخلاف Path.GetFileNameWithoutExtension که فقط آخرین
    /// نقطه را می‌بیند، اینجا همهٔ پسوندهای مرکب برداشته می‌شوند:
    /// «نقشه+1006.tar.gz» باید عنوان «نقشه» و شمارهٔ «1006» بدهد، نه شمارهٔ «1006.TAR».
    /// نقطه‌ای که بخشی از خودِ شماره است (مثل «1006.1») دست‌نخورده می‌ماند،
    /// چون فقط دنباله‌های حرفیِ شناخته‌شده به‌عنوان پسوند برداشته می‌شوند.
    /// </summary>
    public static string StripExtensions(string? fileName)
    {
        var name = fileName ?? "";
        while (true)
        {
            var dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1) return name;
            var tail = name[(dot + 1)..];
            // پسوند = ۱ تا ۱۰ حرف (tar, gz, pdf, docx, jpeg, msg, …)
            if (tail.Length > 10) return name;
            foreach (var ch in tail)
            {
                if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))) return name;
            }
            name = name[..dot];
        }
    }

    /// <summary>فایل‌های سیستمی و موقت که باید نادیده گرفته شوند.</summary>
    public static bool IsSystemFile(string fileName)
        => string.Equals(fileName, "Thumbs.db", StringComparison.OrdinalIgnoreCase)
           || string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("~$", StringComparison.Ordinal);

    /// <summary>کد خطای مربوط به حروف غیرمجاز شماره؛ اگر مجاز باشد null.</summary>
    public static string? CodeCharsError(string code)
        => CodeAllowedRegex().IsMatch(code) ? null : DocImportErrorCodes.CodeChars;

    /// <summary>
    /// جداسازی عنوان و شماره از نام فایل.
    /// نام باید دقیقاً یک «+» داشته باشد؛ سیستم هیچ‌وقت حدس نمی‌زند.
    /// </summary>
    public static (string NameStatus, string Title, string Code) Parse(string fileNameWithoutExtension)
    {
        var baseName = fileNameWithoutExtension ?? "";
        var plusCount = baseName.Length - baseName.Replace(Separator.ToString(), "").Length;

        if (plusCount == 0) return ("NO_SEPARATOR", "", "");
        if (plusCount > 1) return ("MULTI_SEPARATOR", "", "");

        var pos = baseName.IndexOf(Separator);
        var title = NormalizeTitle(baseName[..pos]);
        var code = NormalizeCode(baseName[(pos + 1)..]);

        if (title.Length == 0 || code.Length == 0) return ("EMPTY_PART", title, code);
        return ("OK", title, code);
    }
}
