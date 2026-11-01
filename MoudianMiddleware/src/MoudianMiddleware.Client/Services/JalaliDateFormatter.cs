using System.Globalization;
using System.Text;

namespace MoudianMiddleware.Client.Services;

public static class JalaliDateFormatter
{
    private static readonly PersianCalendar Calendar = new();

    public static string Format(DateTimeOffset? value)
    {
        if (value is null) return string.Empty;
        var date = value.Value.DateTime;
        var text = FormattableString.Invariant($"{Calendar.GetYear(date):0000}/{Calendar.GetMonth(date):00}/{Calendar.GetDayOfMonth(date):00}");
        return ToPersianDigits(text);
    }

    public static bool TryParse(string? input, out DateTimeOffset value)
    {
        value = default;
        var text = NormalizeDigits(input?.Trim() ?? string.Empty);
        var parts = text.Split(new[] { '/', '-', '.' }, StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day)) return false;
        try
        {
            var date = Calendar.ToDateTime(year, month, day, 12, 0, 0, 0);
            value = new DateTimeOffset(date, TimeSpan.FromHours(3.5));
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    public static string ToPersianDigits(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (var character in value) builder.Append(character is >= '0' and <= '9' ? (char)('۰' + character - '0') : character);
        return builder.ToString();
    }

    private static string NormalizeDigits(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var digit = character switch
            {
                >= '۰' and <= '۹' => character - '۰',
                >= '٠' and <= '٩' => character - '٠',
                _ => -1
            };
            builder.Append(digit >= 0 ? (char)('0' + digit) : character);
        }
        return builder.ToString();
    }
}
