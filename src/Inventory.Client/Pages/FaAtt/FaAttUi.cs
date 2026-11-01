namespace Inventory.Client.Pages;

/// <summary>هلپرهای نمایشی ماژول حضور و غیاب فروغ آریا (FaAtt)</summary>
public static class FaAttUi
{
    public static string ShiftType(int t) => t switch
    {
        0 => "ثابت", 1 => "چرخشی", 2 => "شب‌کاری", 3 => "شناور", _ => "—"
    };

    public static string LogType(int t) => t switch
    {
        0 => "ورود", 1 => "خروج", _ => "نامشخص"
    };

    public static string LogSource(int s) => s switch
    {
        0 => "دستگاه", 1 => "موبایل", 2 => "وب‌کلاک", 3 => "دستی", _ => "—"
    };

    public static string DayStatus(int s) => s switch
    {
        0 => "حاضر", 1 => "تأخیر", 2 => "تعجیل", 3 => "تأخیر و تعجیل", 4 => "غیبت",
        5 => "مأموریت", 6 => "مرخصی", 7 => "تعطیل رسمی", 8 => "روز استراحت",
        9 => "تعطیل‌کار", 10 => "ناقص", _ => "—"
    };

    public static string DayBadge(int s) => s switch
    {
        0 => "text-bg-success",
        1 or 2 or 3 or 10 => "text-bg-warning",
        4 => "text-bg-danger",
        5 => "text-bg-primary",
        6 or 8 => "text-bg-secondary",
        7 => "text-bg-info",
        9 => "text-bg-dark",
        _ => "text-bg-light"
    };

    public static string ReqStatus(int s) => s switch
    {
        0 => "در انتظار", 1 => "تأیید شده", 2 => "رد شده", _ => "—"
    };

    public static string ReqBadge(int s) => s switch
    {
        0 => "text-bg-warning",
        1 => "text-bg-success",
        2 => "text-bg-danger",
        _ => "text-bg-light"
    };

    public static string DeviceType(int t) => t switch
    {
        0 => "اثر انگشت", 1 => "تشخیص چهره", 2 => "کارتی", _ => "—"
    };

    /// <summary>نمایش دقایق به‌صورت «ساعت:دقیقه» — مثل 8:30</summary>
    public static string Min(int minutes)
    {
        if (minutes <= 0) return "—";
        return $"{minutes / 60}:{minutes % 60:00}";
    }

    public static string WorkflowStep(int s) => s switch
    {
        0 => "نزد مدیر مستقیم", 1 => "نزد منابع انسانی", 2 => "تمام‌شده", _ => "—"
    };

    public static string WorkflowBadge(int s) => s switch
    {
        0 => "text-bg-warning",
        1 => "text-bg-info",
        2 => "text-bg-secondary",
        _ => "text-bg-light"
    };

    public static int JalaliYear(DateTime d)
    {
        try { return new System.Globalization.PersianCalendar().GetYear(d); }
        catch { return d.Year; }
    }

    public static int CurrentYear => JalaliYear(DateTime.Today);

    public static int CurrentMonth
    {
        get
        {
            try { return new System.Globalization.PersianCalendar().GetMonth(DateTime.Today); }
            catch { return 1; }
        }
    }

    public static string MonthName(int m) => m switch
    {
        1 => "فروردین", 2 => "اردیبهشت", 3 => "خرداد", 4 => "تیر", 5 => "مرداد", 6 => "شهریور",
        7 => "مهر", 8 => "آبان", 9 => "آذر", 10 => "دی", 11 => "بهمن", 12 => "اسفند", _ => "—"
    };

    public static string Num(double v) => Inventory.Shared.Fa.Digits(v.ToString("0.##"));
    public static string Num(double? v) => v == null ? "—" : Num(v.Value);
    public static string Money(double v) => Inventory.Shared.Fa.Digits(v.ToString("#,0"));
}
