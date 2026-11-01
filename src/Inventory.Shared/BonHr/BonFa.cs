using System.Globalization;

namespace Inventory.Shared.BonHr;

/// <summary>نمایش فارسی اعداد و ماه‌ها برای ماژول بن‌سازه. بدون CultureInfo فاقد globalization.</summary>
public static class BonFa
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly string[] MonthNames =
    {
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    };

    public static string N(decimal value) => ToPersianDigits(value.ToString("#,##0", Inv));
    public static string N(int value) => ToPersianDigits(value.ToString("#,##0", Inv));
    public static string N(double value) => ToPersianDigits(value.ToString("#,##0.##", Inv));
    public static string N(string? text) => ToPersianDigits(text ?? "");
    public static string Money(decimal value) => N(value);
    public static string Rial(decimal value) => value.ToString("#,##0", Inv);

    public static decimal ParseRial(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0m;
        var digits = new string(ToLatin(text).Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? 0m : decimal.Parse(digits, Inv);
    }

    public static string ToLatin(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '۰' && chars[i] <= '۹') chars[i] = (char)('0' + (chars[i] - '۰'));
            else if (chars[i] >= '٠' && chars[i] <= '٩') chars[i] = (char)('0' + (chars[i] - '٠'));
        }
        return new string(chars);
    }

    public static string ToPersianDigits(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] >= '0' && chars[i] <= '9') chars[i] = (char)('۰' + (chars[i] - '0'));
        return new string(chars);
    }

    public static string MonthLabel(string monthKey)
    {
        var parts = (monthKey ?? "").Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var m) || m < 1 || m > 12) return monthKey ?? "";
        return $"{MonthNames[m - 1]} {ToPersianDigits(parts[0])}";
    }

    public static string Percent(double value) => $"{ToPersianDigits(value.ToString("0.#", Inv))}٪";
    public static string Percent(decimal value) => $"{ToPersianDigits(value.ToString("0.##", Inv))}٪";

    public static bool IsMarried(string? married) =>
        (married ?? "").Replace('\u0622', '\u0627').Contains("متاهل");

    public static string Hm(int minutes)
    {
        var sign = minutes < 0 ? "-" : "";
        minutes = Math.Abs(minutes);
        return $"{sign}{ToPersianDigits((minutes / 60).ToString(Inv))}:{ToPersianDigits((minutes % 60).ToString("00", Inv))}";
    }
}
