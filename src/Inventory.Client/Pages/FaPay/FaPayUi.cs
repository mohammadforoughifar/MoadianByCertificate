using Inventory.Shared;
using Inventory.Shared.Dtos;
using System.Globalization;

namespace Inventory.Client.Pages;

/// <summary>هلپرهای نمایشی حقوق و دستمزد فروغ آریا (FaPay §۸)</summary>
public static class FaPayUi
{
    private static readonly PersianCalendar Pc = new();
    public static int CurrentYear { get { try { return Pc.GetYear(DateTime.Today); } catch { return DateTime.Today.Year; } } }
    public static int CurrentMonth { get { try { return Pc.GetMonth(DateTime.Today); } catch { return 1; } } }

    public static string MonthName(int m) => m switch
    {
        1 => "فروردین", 2 => "اردیبهشت", 3 => "خرداد", 4 => "تیر", 5 => "مرداد", 6 => "شهریور",
        7 => "مهر", 8 => "آبان", 9 => "آذر", 10 => "دی", 11 => "بهمن", 12 => "اسفند", _ => "—"
    };

    public static string Money(double v) => Fa.Money((decimal)v);
    public static string Num(double v) => Fa.Number((decimal)v);
    public static string Num(int v) => Fa.Digits(v.ToString());

    public static string YearMonth(int y, int m) => $"{MonthName(m)} {Fa.Digits(y.ToString())}";

    public static string RunStatus(int s) => s switch { 0 => "پیش‌نویس", 1 => "نهایی", _ => "—" };
    public static string RunBadge(int s) => s switch { 0 => "text-bg-warning", 1 => "text-bg-success", _ => "text-bg-light" };

    public static string Kind(int k) => k switch { 0 => "مزایا", 1 => "کسور", _ => "—" };
    public static string KindBadge(int k) => k switch { 0 => "text-bg-success", 1 => "text-bg-danger", _ => "text-bg-light" };

    public static string RunUrl(int id) => $"/fa-pay/runs/{id}";
    public static string SlipUrl(int id) => $"/fa-pay/slips/{id}";
    public static string MySlipUrl(int id) => $"/fa-pay/my/{id}";

    public static List<FaPaySlipItemDto> Earn(FaPaySlipDto d) => d.Items.Where(i => i.Kind == 0).ToList();
    public static List<FaPaySlipItemDto> Ded(FaPaySlipDto d) => d.Items.Where(i => i.Kind == 1).ToList();

    public static string RunKind(int k) => k switch { 0 => "ماهانه", 1 => "عیدی و سنوات", _ => "—" };
    public static string RunKindBadge(int k) => k switch { 0 => "text-bg-primary", 1 => "text-bg-info", _ => "text-bg-light" };
    public static string RunName(FaPayRunDto r) => r.Kind == 1 ? $"عیدی و سنوات {Fa.Digits(r.Year.ToString())}" : YearMonth(r.Year, r.Month);

    public static string LoanStatus(int s) => s switch { 0 => "فعال", 1 => "تسویه‌شده", 2 => "لغوشده", _ => "—" };
    public static string LoanBadge(int s) => s switch { 0 => "text-bg-primary", 1 => "text-bg-success", 2 => "text-bg-secondary", _ => "text-bg-light" };

    public static string ArrearStatus(int s) => s switch { 0 => "پیش‌نویس", 1 => "اعمال‌شده", 2 => "لغوشده", _ => "—" };
    public static string ArrearBadge(int s) => s switch { 0 => "text-bg-warning", 1 => "text-bg-success", 2 => "text-bg-secondary", _ => "text-bg-light" };

    public static string SettleStatus(int s) => s switch { 0 => "پیش‌نویس", 1 => "نهایی", _ => "—" };
    public static string SettleBadge(int s) => s switch { 0 => "text-bg-warning", 1 => "text-bg-success", _ => "text-bg-light" };
    public static string SettleReason(int r) => r switch
    {
        0 => "استعفا", 1 => "اخراج/فسخ", 2 => "پایان قرارداد", 3 => "بازنشستگی", 4 => "توافق طرفین", _ => "سایر"
    };

    public static string CmpFlag(string f) => f switch { "Same" => "بدون تغییر", "Changed" => "تغییر کرده", "New" => "جدید", "Left" => "خارج‌شده", _ => f };
    public static string CmpBadge(string f) => f switch { "Same" => "text-bg-light", "Changed" => "text-bg-warning", "New" => "text-bg-success", "Left" => "text-bg-danger", _ => "text-bg-light" };
}
