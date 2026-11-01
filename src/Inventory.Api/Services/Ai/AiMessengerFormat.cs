// قالب‌بندی پاسخ دستیار برای پیام‌رسان‌ها (§۱۸ — ربات بله).
// بله مارک‌داون وب را ندارد؛ لینک‌ها متن ساده، بولدها حذف، و جدول‌ها به خطوط خوانا تبدیل می‌شوند.
using System.Text;

namespace Inventory.Api.Services.Ai;

public static class AiMessengerFormat
{
    public const int MaxMessageChars = 3900;

    /// <summary>پاسخ چت وب → متن تمیز پیام‌رسان.</summary>
    public static string ToPlain(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return "";
        var text = AiTextUtil.StripLinks(reply);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
        text = ConvertTables(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    /// <summary>شکستن پیام بلند به چند پیام روی مرز خط (بدون برش وسط جمله تا حد ممکن).</summary>
    public static List<string> SplitMessage(string text, int max = MaxMessageChars)
    {
        var out_ = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return out_;
        var current = new StringBuilder();
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine;
            // خط تکیِ خیلی بلند: برش اجباری
            while (line.Length > max)
            {
                if (current.Length > 0) { out_.Add(current.ToString().TrimEnd()); current.Clear(); }
                out_.Add(line[..max]);
                line = line[max..];
            }
            if (current.Length + line.Length + 1 > max && current.Length > 0)
            {
                out_.Add(current.ToString().TrimEnd());
                current.Clear();
            }
            current.AppendLine(line);
        }
        if (current.Length > 0) out_.Add(current.ToString().TrimEnd());
        return out_.Where(p => p != "").ToList();
    }

    private static string ConvertTables(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("|") && i + 1 < lines.Length && IsSeparator(lines[i + 1]))
            {
                var header = Cells(line);
                sb.AppendLine($"📋 {string.Join(" / ", header)}:");
                i += 2;
                for (; i < lines.Length && lines[i].Trim().StartsWith("|"); i++)
                    sb.AppendLine($"• {string.Join(" / ", Cells(lines[i]))}");
                i--;
                continue;
            }
            sb.AppendLine(lines[i]);
        }
        return sb.ToString();
    }

    private static bool IsSeparator(string line)
    {
        var t = line.Trim();
        return t.Length >= 3 && t.StartsWith("|") && t.Trim('|', '-', ':', ' ', '\t').Length == 0;
    }

    private static List<string> Cells(string line) =>
        line.Trim().Trim('|').Split('|').Select(c => c.Trim()).Where(c => c != "").ToList();
}
