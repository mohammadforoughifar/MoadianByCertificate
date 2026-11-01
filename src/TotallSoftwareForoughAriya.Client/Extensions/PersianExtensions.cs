using System.Globalization;
using Inventory.Shared;

namespace Inventory.Client.Extensions;

public static class PersianExtensions
{
    // ---------- قالب‌بندی ساعت (ایمن در حالت InvariantGlobalization) ----------
    // در این حالت، فرمت‌های سفارشی TimeSpan مثل ToString("HH:mm") با FormatException خطا می‌دهند؛
    // برای همین از اجزای عددی مستقیم قالب می‌سازیم (24 ساعته).
    /// <summary>HH:MM برای TimeSpan? (24 ساعته)</summary>
    public static string HHMM(this TimeSpan? t) => t.HasValue ? $"{t.Value.Hours:D2}:{t.Value.Minutes:D2}" : "—";
    /// <summary>HH:MM برای TimeSpan (24 ساعته)</summary>
    public static string HHMM(this TimeSpan t) => $"{t.Hours:D2}:{t.Minutes:D2}";
    /// <summary>HH:MM برای DateTime? (24 ساعته)</summary>
    public static string HHMM(this DateTime? dt) => dt.HasValue ? $"{dt.Value.Hour:D2}:{dt.Value.Minute:D2}" : "—";
    /// <summary>HH:MM برای DateTime (24 ساعته)</summary>
    public static string HHMM(this DateTime dt) => $"{dt.Hour:D2}:{dt.Minute:D2}";

    /// <summary>تاریخ شمسی کوتاه: 1403/05/12</summary>
    public static string ToFa(this DateTime dt) => PersianDate.ToShort(dt);

    /// <summary>تاریخ شمسی بلند: ۱۲ مرداد ۱۴۰۳</summary>
    public static string ToFaLong(this DateTime dt) => PersianDate.ToLong(dt);

    /// <summary>تاریخ و ساعت شمسی</summary>
    public static string ToFaDateTime(this DateTime dt) => PersianDate.ToShortDateTime(dt);

    /// <summary>مبلغ با ارقام فارسی و جداکننده هزارگان</summary>
    public static string Money(this decimal v) => Fa.Money(v);
    public static string Money(this long v) => Fa.Money(v);

    /// <summary>عدد صحیح با ارقام فارسی</summary>
    public static string Num(this decimal v) => Fa.Number(v);
    public static string Num(this long v) => Fa.Number(v);

    public static string Num(this int v) => Fa.Number(v);

    /// <summary>
    /// عددِ شناسه‌ای (شمارهٔ سند/صورتحساب/فاکتور) با ارقام فارسی و **بدون** جداکنندهٔ سه‌رقمی.
    /// شمارهٔ سند یک شناسه است نه مبلغ؛ «۱۲۳٬۴۵۶» هم اشتباه خوانده می‌شود و هم در کپی/جستجو
    /// مشکل ایجاد می‌کند، بنابراین برای شماره‌ها به‌جای <see cref="Num(int)"/> از این استفاده کنید.
    /// </summary>
    public static string NoGroup(this int v) => Fa.Digits(v.ToString(CultureInfo.InvariantCulture));

    /// <summary>مثل <see cref="NoGroup(int)"/> برای شناسه‌های بزرگ‌تر از int.</summary>
    public static string NoGroup(this long v) => Fa.Digits(v.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// مقدار/تعداد با ارقام فارسی و جداکننده هزارگان، با حفظ اعشار — مثلاً ۶٫۵ یا ۱۲۳۴٫۲۵.
    /// برخلاف <see cref="Num(decimal)"/> عدد را گرد نمی‌کند (برای مقدارهای کسری مثل ۲٫۵ لازم است).
    /// </summary>
    public static string Qty(this decimal v)
    {
        var s = decimal.Round(v, 3, MidpointRounding.AwayFromZero)
                      .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var dot = s.IndexOf('.');
        var intPart = dot < 0 ? s : s.Substring(0, dot);
        var frac = dot < 0 ? "" : s.Substring(dot).Replace('.', '\u066b');   // جداکننده اعشار فارسی

        if (intPart.Length > 4)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < intPart.Length; i++)
            {
                if (i > 0 && (intPart.Length - i) % 3 == 0) sb.Append(',');
                sb.Append(intPart[i]);
            }
            intPart = sb.ToString();
        }
        return Fa.Digits(intPart + frac);
    }
    /// <summary>درصد با ارقام فارسی — مثلاً ۸۵٫۵٪</summary>
    public static string Percent(this decimal v) => $"{Fa.Number(Math.Round(v, 1))}٪";


    // ================= ماژول مدیریت پروژه‌ها (چاپ/گرید/ارقام فارسی) =================

    /// <summary>ارقام لاتین متن به فارسی — بدون جداکننده هزارگان (برای کد پروژه، سریال و…)</summary>
    public static string FaDigits(this string? s) => Fa.Digits(s);

    /// <summary>عدد صحیح با ارقام فارسی — بدون جداکننده هزارگان (برای شمارنده‌ها و ردیف جدول)</summary>
    public static string FaDigits(this int n) => Fa.Digits(n);

    /// <summary>عدد بلند با ارقام فارسی — بدون جداکننده هزارگان</summary>
    public static string FaDigits(this long n) => Fa.Digits(n);

    /// <summary>تاریخ شمسی کوتاه با ارقام فارسی: ۱۴۰۳/۰۵/۱۲</summary>
    public static string ToFaDate(this DateTime dt) => PersianDate.ToShortFa(dt);

    /// <summary>تاریخ شمسی کوتاه با ارقام فارسی — «—» در صورت خالی بودن</summary>
    public static string ToFaDate(this DateTime? dt) => dt.HasValue ? PersianDate.ToShortFa(dt.Value) : "—";

    /// <summary>ساعت ۲۴ساعته با ارقام فارسی: ۰۸:۳۰</summary>
    public static string TimeFa(this TimeOnly t) => Fa.Digits($"{t.Hour:D2}:{t.Minute:D2}");

    /// <summary>ساعت ۲۴ساعته با ارقام فارسی — «—» در صورت خالی بودن</summary>
    public static string TimeFa(this TimeOnly? t) => t.HasValue ? t.Value.TimeFa() : "—";

    /// <summary>
    /// مدت زمان به‌صورت «ساعت:دقیقه» با ارقام فارسی — برخلاف HHMM، ساعت‌های بیش از ۲۴
    /// را هم درست نشان می‌دهد (مثلاً ۱۲۵:۳۰ برای جمع ساعات یک پروژه).
    /// </summary>
    public static string HoursFa(this TimeSpan t)
    {
        var neg = t < TimeSpan.Zero;
        if (neg) t = t.Negate();
        var hours = (int)t.TotalHours;
        return (neg ? "-" : "") + Fa.Digits($"{hours:D2}:{t.Minutes:D2}");
    }

    /// <summary>مدت زمان با ارقام فارسی — «—» در صورت خالی بودن</summary>
    public static string HoursFa(this TimeSpan? t) => t.HasValue ? t.Value.HoursFa() : "—";

    /// <summary>ارقام فارسی برای اعداد صحیح اختیاری — رشته‌ی خالی در صورت نبود مقدار
    /// (افزوده‌شده برای ماژول اتوماسیون اداری)</summary>
    public static string FaDigits(this int? n) => n.HasValue ? Fa.Digits(n.Value) : "";
}
