using System.Globalization;
using System.Text;

namespace Inventory.Shared;

/// <summary>
/// کلید جست‌وجوی پیام‌رسان؛ فقط برای مقایسهٔ متن، نه تغییر نام یا شناسهٔ کاربران.
/// حروف فارسی/عربی، ارقام، فاصله و نیم‌فاصله و اعراب در جست‌وجو یکسان می‌شوند.
/// </summary>
public static class ChatSearchText
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var result = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormKC))
        {
            var category = char.GetUnicodeCategory(ch);
            if (char.IsWhiteSpace(ch) || ch == '\u0640' ||
                category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
                continue;

            var normalized = ch switch
            {
                '\u064a' or '\u0649' => '\u06cc', // ي / ى → ی
                '\u0643' => '\u06a9',             // ك → ک
                >= '\u06f0' and <= '\u06f9' => (char)('0' + ch - '\u06f0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + ch - '\u0660'),
                _ => char.ToLowerInvariant(ch)
            };
            result.Append(normalized);
        }
        return result.ToString();
    }
}
