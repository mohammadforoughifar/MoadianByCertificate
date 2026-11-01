namespace Inventory.Shared;

/// <summary>
/// ابزار تبدیل تاریخ میلادی به شمسی (جلالی) و برعکس.
/// پیاده‌سازی الگوریتم استاندارد jalaali — بدون وابستگی به کتابخانه خارجی.
/// </summary>
public static class PersianDate
{
    private static readonly string[] MonthNames =
    {
        "", "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    };

    private static readonly string[] DayNames =
    {
        "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه"
    };

    public static string MonthName(int month) => month is >= 1 and <= 12 ? MonthNames[month] : "";

    public static string DayName(DateTime dt) => DayNames[(int)dt.DayOfWeek];

    /// <summary>نام روز هفته (فارسی) — alias برای DayName.</summary>
    public static string WeekdayName(DateTime dt) => DayName(dt);

    /// <summary>تاریخ شمسی امروز (بر اساس ساعت محلی سیستم).</summary>
    public static DateTime Today => DateTime.Now;

    /// <summary>ساخت DateTime از اجزای تاریخ شمسی. در صورت نامعتبر بودن، DateTime.MinValue.</summary>
    public static DateTime ToGregorian(int jy, int jm, int jd)
    {
        if (jy < 1 || jm < 1 || jm > 12 || jd < 1 || jd > 31) return DateTime.MinValue;
        long jdn = J2D(jy, jm, jd);
        var (gy, gm, gd) = D2G(jdn);
        try { return new DateTime(gy, gm, gd); }
        catch { return DateTime.MinValue; }
    }

    /// <summary>تجزیه متن تاریخ شمسی (فرمت 1403/05/12 یا 1403-05-12) به میلادی. null در صورت نامعتبر بودن.</summary>
    public static DateTime? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // پشتیبانی از ارقام فارسی/عربی در ورودی تاریخ
        text = Fa.ToEn(text);
        var parts = text.Trim().Split('/', '-', '.');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0].Trim(), out var y) ||
            !int.TryParse(parts[1].Trim(), out var m) ||
            !int.TryParse(parts[2].Trim(), out var d)) return null;
        var g = ToGregorian(y, m, d);
        return g == DateTime.MinValue ? null : g;
    }

    /// <summary>تبدیل میلادی به (سال، ماه، روز) شمسی.</summary>
    public static (int Year, int Month, int Day) FromGregorian(DateTime dt)
        => D2J(G2D(dt.Year, dt.Month, dt.Day));

    /// <summary>رشته تاریخ شمسی: 1403/05/12</summary>
    public static string ToShort(DateTime dt)
    {
        var (y, m, d) = FromGregorian(dt);
        return $"{y:0000}/{m:00}/{d:00}";
    }

    /// <summary>رشته تاریخ شمسی با ارقام فارسی: ۱۴۰۳/۰۵/۱۲</summary>
    public static string ToShortFa(DateTime dt) => Fa.Digits(ToShort(dt));

    /// <summary>رشته تاریخ شمسی بلند: ۱۲ مرداد ۱۴۰۳</summary>
    public static string ToLong(DateTime dt)
    {
        var (y, m, d) = FromGregorian(dt);
        return $"{Fa.Digits(d)} {MonthName(m)} {Fa.Digits(y)}";
    }

    /// <summary>رشته تاریخ و ساعت شمسی: 1403/05/12 - 14:30</summary>
    public static string ToShortDateTime(DateTime dt) => $"{ToShort(dt)} - {dt:HH:mm}";

    /// <summary>نام ماه شمسی به‌همراه سال برای گزارش‌ها: مرداد ۱۴۰۳</summary>
    public static string MonthLabel(DateTime dt)
    {
        var (y, m, _) = FromGregorian(dt);
        return $"{MonthName(m)} {Fa.Digits(y)}";
    }

    /// <summary>امروز به‌صورت شمسی.</summary>
    public static string TodayShort() => ToShort(DateTime.Now);

    /// <summary>امروز شمسی برای ورودی date (فرمت استاندارد HTML: yyyy-MM-dd).</summary>
    public static string TodayInput() => ToInput(DateTime.Now);

    /// <summary>تبدیل میلادی به قالب ورودی input date (yyyy-MM-dd).</summary>
    public static string ToInput(DateTime dt)
    {
        var (y, m, d) = FromGregorian(dt);
        return $"{y:0000}-{m:00}-{d:00}";
    }

    /// <summary>تبدیل تاریخ به اجزای شمسی برای نمایش در کاردکس.</summary>
    public static string ToShortWithTime(DateTime dt)
    {
        var (y, m, d) = FromGregorian(dt);
        return $"{y:0000}/{m:00}/{d:00} {dt:HH:mm}";
    }

    /// <summary>آیا سال شمسی کبیسه است؟</summary>
    public static bool IsLeapYear(int jy) => JalCal(jy).Leap == 1;

    /// <summary>تعداد روزهای یک ماه شمسی.</summary>
    public static int DaysInMonth(int jy, int jm)
    {
        if (jm < 1 || jm > 12) return 0;
        if (jm <= 6) return 31;
        if (jm <= 11) return 30;
        return IsLeapYear(jy) ? 30 : 29;
    }

    /// <summary>جابه‌جایی ماه شمسی به اندازه delta (مثبت یا منفی).</summary>
    public static (int Year, int Month) AddMonths(int jy, int jm, int delta)
    {
        int total = jy * 12 + (jm - 1) + delta;
        int y = (int)Math.Floor((double)total / 12);
        int m = ((total % 12) + 12) % 12 + 1;
        return (y, m);
    }

    /// <summary>ستون روزِ اولِ ماه در تقویم هفتگی (شنبه = ستون صفر).</summary>
    public static int FirstDayColumn(int jy, int jm)
    {
        var first = ToGregorian(jy, jm, 1);
        if (first == DateTime.MinValue) return 0;
        return ((int)first.DayOfWeek + 1) % 7; // شنبه (Saturday=6) → 0
    }

    // ============================ هسته الگوریتم jalaali ============================

    private static long Div(long a, long b) => a / b;
    private static long Mod(long a, long b) => a - Div(a, b) * b;

    private static (int Leap, int Gy, int March) JalCal(long jy)
    {
        long[] breaks =
        {
            -61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210,
            1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178
        };
        int bl = breaks.Length;
        long gy = jy + 621;
        long leapJ = -14;
        long jp = breaks[0];
        long jump = 0;

        for (int i = 1; i < bl; i++)
        {
            long jm = breaks[i];
            jump = jm - jp;
            if (jy < jm) break;
            leapJ = leapJ + Div(jump, 33) * 8 + Div(Mod(jump, 33), 4);
            jp = jm;
        }

        long n = jy - jp;
        leapJ = leapJ + Div(n, 33) * 8 + Div(Mod(n, 33) + 3, 4);
        if (Mod(jump, 33) == 4 && jump - n == 4) leapJ++;

        long leapG = Div(gy, 4) - Div((Div(gy, 100) + 1) * 3, 4) - 150;
        long march = 20 + leapJ - leapG;

        if (jump - n < 6) n = n - jump + Div(jump + 4, 33) * 33;
        long leap = Mod(Mod(n + 1, 33) - 1, 4);
        if (leap == -1) leap = 4;

        return ((int)leap, (int)gy, (int)march);
    }

    private static long J2D(long jy, long jm, long jd)
    {
        var r = JalCal(jy);
        return G2D(r.Gy, 3, r.March) + (jm - 1) * 31 - Div(jm, 7) * (jm - 7) + jd - 1;
    }

    private static (int Year, int Month, int Day) D2J(long jdn)
    {
        var g = D2G(jdn);
        long gy = g.Gy;
        long jy = gy - 621;
        var r = JalCal(jy);
        long jdn1f = G2D(gy, 3, r.March);
        long k = jdn - jdn1f;

        if (k >= 0)
        {
            if (k <= 185)
            {
                int jm = (int)(1 + Div(k, 31));
                int jd = (int)(Mod(k, 31) + 1);
                return ((int)jy, jm, jd);
            }
            k -= 186;
        }
        else
        {
            jy -= 1;
            k += 179;
            if (r.Leap == 1) k += 1;
        }

        int jm2 = (int)(7 + Div(k, 30));
        int jd2 = (int)(Mod(k, 30) + 1);
        return ((int)jy, jm2, jd2);
    }

    private static long G2D(long gy, long gm, long gd)
    {
        long d = Div((gy + Div(gm - 8, 6) + 100100) * 1461, 4)
               + Div(153 * Mod(gm + 9, 12) + 2, 5)
               + gd - 34840408;
        d = d - Div(Div(gy + 100100 + Div(gm - 8, 6), 100) * 3, 4) + 752;
        return d;
    }

    private static (int Gy, int Gm, int Gd) D2G(long jdn)
    {
        long j = 4 * jdn + 139361631;
        j = j + Div(Div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
        long i = Div(Mod(j, 1461), 4) * 5 + 308;
        long gd = Div(Mod(i, 153), 5) + 1;
        long gm = Mod(Div(i, 153), 12) + 1;
        long gy = Div(j, 1461) - 100100 + Div(8 - gm, 6);
        return ((int)gy, (int)gm, (int)gd);
    }
}
