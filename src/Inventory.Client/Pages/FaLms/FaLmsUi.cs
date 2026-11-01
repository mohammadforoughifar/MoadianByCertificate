using System.Globalization;
using Inventory.Shared;

namespace Inventory.Client.Pages;

/// <summary>هلپرهای نمایشی ماژول آموزش و توسعه فروغ آریا (FaLms)</summary>
public static class FaLmsUi
{
    private static readonly PersianCalendar Pc = new();
    public static int JalaliYear(DateTime d) { try { return Pc.GetYear(d); } catch { return d.Year; } }
    public static int CurrentYear => JalaliYear(DateTime.Today);

    public static string MonthName(int m) => m switch
    {
        1 => "فروردین", 2 => "اردیبهشت", 3 => "خرداد", 4 => "تیر", 5 => "مرداد", 6 => "شهریور",
        7 => "مهر", 8 => "آبان", 9 => "آذر", 10 => "دی", 11 => "بهمن", 12 => "اسفند", _ => "—"
    };

    public static string Kind(int k) => k switch
    {
        0 => "داخلی", 1 => "خارجی", 2 => "آنلاین", 3 => "کارگاه", _ => "—"
    };

    public static string CourseStatus(int s) => s switch
    {
        0 => "پیش‌نویس", 1 => "باز", 2 => "در حال برگزاری", 3 => "اتمام‌شده", 4 => "لغوشده", _ => "—"
    };

    public static string CourseBadge(int s) => s switch
    {
        0 => "text-bg-secondary",
        1 => "text-bg-success",
        2 => "text-bg-primary",
        3 => "text-bg-dark",
        4 => "text-bg-danger",
        _ => "text-bg-light"
    };

    public static string NeedSource(int s) => s switch
    {
        0 => "ارزیابی عملکرد", 1 => "هدف سازمانی", 2 => "مدیر", 3 => "خوداظهاری", _ => "—"
    };

    public static string NeedStatus(int s) => s switch
    {
        0 => "جدید", 1 => "تأییدشده", 2 => "تبدیل به دوره", 3 => "ردشده", _ => "—"
    };

    public static string NeedBadge(int s) => s switch
    {
        0 => "text-bg-warning",
        1 => "text-bg-success",
        2 => "text-bg-primary",
        3 => "text-bg-danger",
        _ => "text-bg-light"
    };

    public static string EnrollStatus(int s) => s switch
    {
        0 => "در انتظار تأیید", 1 => "تأییدشده", 2 => "ردشده", 3 => "انصرافی", _ => "—"
    };

    public static string EnrollBadge(int s) => s switch
    {
        0 => "text-bg-warning",
        1 => "text-bg-success",
        2 => "text-bg-danger",
        3 => "text-bg-secondary",
        _ => "text-bg-light"
    };

    public static string Priority(int p) => p switch
    {
        0 => "کم", 1 => "متوسط", 2 => "زیاد", _ => "—"
    };

    public static string PriorityBadge(int p) => p switch
    {
        0 => "text-bg-secondary",
        1 => "text-bg-info",
        2 => "text-bg-danger",
        _ => "text-bg-light"
    };

    public static string CalKind(int k) => k switch
    {
        0 => "دوره", 1 => "جلسه", 2 => "آزمون", _ => "—"
    };

    public static string CalBadge(int k) => k switch
    {
        0 => "text-bg-success",
        1 => "text-bg-primary",
        2 => "text-bg-warning",
        _ => "text-bg-light"
    };

    public static string Money(double v) => Fa.Digits(v.ToString("#,0"));
    public static string Num(double v) => Fa.Digits(v.ToString("0.##"));
    public static string Num(double? v) => v == null ? "—" : Num(v.Value);
}
