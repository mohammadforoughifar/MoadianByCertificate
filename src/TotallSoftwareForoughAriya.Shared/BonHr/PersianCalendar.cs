namespace Inventory.Shared.BonHr;

/// <summary>
/// توابع تقویم شمسی — پورت مستقیم از assets/calendar-settings.js و assets/statutory-rules.js
/// هشدار سازگاری: دو الگوریتم متفاوت برای «کبیسه» در برنامهٔ اصلی وجود دارد و هر دو
/// عیناً حفظ شده‌اند تا خروجی عددی مو به مو یکسان بماند:
///   - PayrollMonthDays  : فرمول ساده year % 4 == 3  (استفاده در محاسبات حقوق)
///   - CalendarMonthDays : الگوریتم دقیق jalCal      (استفاده در نمایش تقویم)
/// </summary>
public static class PersianCalendarUtil
{
    public static readonly string[] MonthNames =
    {
        "فروردین","اردیبهشت","خرداد","تیر","مرداد","شهریور",
        "مهر","آبان","آذر","دی","بهمن","اسفند"
    };

    // ---------- نسخهٔ حقوق و دستمزد (statutory-rules.js: persianMonthDays) ----------
    // if (m >= 1 && m <= 6) return 31;
    // if (m >= 7 && m <= 11) return 30;
    // return Number(year) % 4 === 3 ? 30 : 29;
    public static int PayrollMonthDays(int year, int month)
    {
        if (month >= 1 && month <= 6) return 31;
        if (month >= 7 && month <= 11) return 30;
        return year % 4 == 3 ? 30 : 29;
    }

    // ---------- شمارهٔ روز شمسی (guard-shifts.js / hse.js: jalaliDayNumber) ----------
    public static int? JalaliDayNumber(string? value)
    {
        var v = Digits(value);
        var parts = v.Split('/');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], out var y) ||
            !int.TryParse(parts[1], out var mo) ||
            !int.TryParse(parts[2], out var d)) return null;
        if (mo < 1 || mo > 12) return null;
        var max = mo <= 6 ? 31 : 30;
        if (d < 1 || d > max) return null;
        return y * 365
             + (int)Math.Floor((y - 1) / 33.0) * 8
             + (int)Math.Floor((((y - 1) % 33) + 3) / 4.0)
             + (mo <= 6 ? (mo - 1) * 31 : 186 + (mo - 7) * 30)
             + d;
    }

    /// <summary>افزودن روز به تاریخ شمسی — پورت addJalaliDays از guard-shifts.js</summary>
    public static string AddJalaliDays(string value, int amount)
    {
        var parts = Digits(value).Split('/');
        int y = int.Parse(parts[0]), m = int.Parse(parts[1]), d = int.Parse(parts[2]);
        while (amount-- > 0)
        {
            d++;
            var max = m <= 6 ? 31 : 30;
            if (d > max) { d = 1; m++; if (m > 12) { m = 1; y++; } }
        }
        return $"{y}/{m:00}/{d:00}";
    }

    // ---------- الگوریتم دقیق jalaali (calendar-settings.js) ----------
    private static int Div(int a, int b) => (int)Math.Truncate((double)a / b);
    private static int Mod(int a, int b) => a - (int)Math.Truncate((double)a / b) * b;

    private static readonly int[] Breaks =
    { -61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178 };

    public static (int Leap, int Gy, int March) JalCal(int jy)
    {
        int bl = Breaks.Length, gy = jy + 621, leapJ = -14, jp = Breaks[0], jm = 0, jump = 0, n = 0, i;
        if (jy < jp || jy >= Breaks[bl - 1]) throw new ArgumentOutOfRangeException(nameof(jy), "سال شمسی خارج از محدوده است");
        for (i = 1; i < bl; i++)
        {
            jm = Breaks[i]; jump = jm - jp;
            if (jy < jm) break;
            n = jump; leapJ += Div(n, 33) * 8 + Div(Mod(n, 33), 4); jp = jm;
        }
        n = jy - jp;
        leapJ += Div(n, 33) * 8 + Div(Mod(n, 33) + 3, 4);
        if (Mod(jump, 33) == 4 && jump - n == 4) leapJ++;
        var leapG = Div(gy, 4) - Div((Div(gy, 100) + 1) * 3, 4) - 150;
        var march = 20 + leapJ - leapG;
        if (jump - n < 6) n = n - jump + Div(jump + 4, 33) * 33;
        var leap = Mod(Mod(n + 1, 33) - 1, 4);
        if (leap == -1) leap = 4;
        return (leap, gy, march);
    }

    public static int G2d(int gy, int gm, int gd)
    {
        var d = Div((gy + Div(gm - 8, 6) + 100100) * 1461, 4)
              + Div(153 * Mod(gm + 9, 12) + 2, 5) + gd - 34840408;
        d = d - Div(Div(gy + 100100 + Div(gm - 8, 6), 100) * 3, 4) + 752;
        return d;
    }

    public static (int Gy, int Gm, int Gd) D2g(int jdn)
    {
        var j = 4 * jdn + 139361631;
        j = j + Div(Div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
        var i = Div(Mod(j, 1461), 4) * 5 + 308;
        var gd = Div(Mod(i, 153), 5) + 1;
        var gm = Mod(Div(i, 153), 12) + 1;
        var gy = Div(j, 1461) - 100100 + Div(8 - gm, 6);
        return (gy, gm, gd);
    }

    public static int J2d(int jy, int jm, int jd)
    {
        var r = JalCal(jy);
        return G2d(r.Gy, 3, r.March) + (jm - 1) * 31 - Div(jm, 7) * (jm - 7) + jd - 1;
    }

    public static (int Jy, int Jm, int Jd) D2j(int jdn)
    {
        var g = D2g(jdn);
        var jy = g.Gy - 621;
        var r = JalCal(jy);
        var jdn1f = G2d(g.Gy, 3, r.March);
        var k = jdn - jdn1f;
        if (k >= 0)
        {
            if (k <= 185) return (jy, 1 + Div(k, 31), Mod(k, 31) + 1);
            k -= 186;
        }
        else
        {
            var prev = jy - 1;
            k += 179;
            if (JalCal(prev).Leap == 1) k++;
            return (prev, 7 + Div(k, 30), Mod(k, 30) + 1);
        }
        return (jy, 7 + Div(k, 30), Mod(k, 30) + 1);
    }

    public static (int Gy, int Gm, int Gd) ToGregorian(int jy, int jm, int jd) => D2g(J2d(jy, jm, jd));

    public static bool IsLeap(int jy) => JalCal(jy).Leap == 0;

    /// <summary>تعداد روز ماه برای نمایش تقویم (الگوریتم دقیق) — daysInMonth از calendar-settings.js</summary>
    public static int CalendarMonthDays(int jy, int jm) =>
        jm <= 6 ? 31 : jm <= 11 ? 30 : (IsLeap(jy) ? 30 : 29);

    /// <summary>شاخص روز هفته: 0=شنبه ... 6=جمعه — weekdayIndex از calendar-settings.js</summary>
    public static int WeekdayIndex(int jy, int jm, int jd)
    {
        var g = ToGregorian(jy, jm, jd);
        var dow = (int)new DateTime(g.Gy, g.Gm, g.Gd).DayOfWeek; // Sunday=0
        return (dow + 1) % 7;
    }

    public static (int Jy, int Jm, int Jd) Today()
    {
        var now = DateTime.Now;
        var jdn = G2d(now.Year, now.Month, now.Day);
        return D2j(jdn);
    }

    public static string TodayKey()
    {
        var t = Today();
        return DateKey(t.Jy, t.Jm, t.Jd);
    }

    public static string DateKey(int jy, int jm, int jd) => $"{jy}/{jm:00}/{jd:00}";

    public static (int Jy, int Jm, int Jd)? ParseKey(string? value)
    {
        var v = Digits(value);
        var parts = v.Split('/');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m) || !int.TryParse(parts[2], out var d))
            return null;
        return (y, m, d);
    }

    /// <summary>نرمال‌سازی تاریخ شمسی — پورت jdate از attendance-engine.js</summary>
    public static string JDate(string? value)
    {
        var v = Digits(value);
        var match = System.Text.RegularExpressions.Regex.Match(v, @"(1[34]\d{2})[/-](\d{1,2})[/-](\d{1,2})");
        if (!match.Success) return "";
        return $"{match.Groups[1].Value}/{match.Groups[2].Value.PadLeft(2, '0')}/{match.Groups[3].Value.PadLeft(2, '0')}";
    }

    public static string MonthOf(string date) => date.Length >= 7 ? date[..7] : date;

    /// <summary>تبدیل ارقام فارسی/عربی به لاتین — پورت latin() از attendance-engine.js</summary>
    public static string Digits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        const string fa = "۰۱۲۳۴۵۶۷۸۹";
        const string ar = "٠١٢٣٤٥٦٧٨٩";
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            var i = fa.IndexOf(c);
            if (i >= 0) { sb.Append((char)('0' + i)); continue; }
            i = ar.IndexOf(c);
            if (i >= 0) { sb.Append((char)('0' + i)); continue; }
            if (c == 'ي') { sb.Append('ی'); continue; }
            if (c == 'ك') { sb.Append('ک'); continue; }
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
