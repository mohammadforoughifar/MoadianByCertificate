namespace Inventory.Api.Data;

/// <summary>
/// تنظیمات تقویم کاری (رکورد یکتا با Id=1) — ساعت کاری پیش‌فرض روزها، تاخیر مجاز،
/// روزهای تعطیل هفته و اعمال خودکار تعطیلات رسمی کشور.
/// این تنظیمات مبنای قاعده‌ی «پیش‌فرض» در WorkRules.Resolve است.
/// </summary>
public class WorkCalendarSettings
{
    /// <summary>همیشه یک ردیف — Id به‌صورت identity تولید می‌شود (مقدار صریح ندهید)</summary>
    public int Id { get; set; }

    /// <summary>ساعت شروع کاری پیش‌فرض روزهای کاری (مثلاً 08:00)</summary>
    public TimeSpan DefaultStart { get; set; } = new(8, 0, 0);

    /// <summary>ساعت پایان کاری پیش‌فرض روزهای کاری (مثلاً 16:30 — پایان قبل از شروع = شیفت شب)</summary>
    public TimeSpan DefaultEnd { get; set; } = new(16, 30, 0);

    /// <summary>تاخیر مجاز پیش‌فرض (دقیقه)</summary>
    public int GraceMinutes { get; set; } = 10;

    /// <summary>
    /// روزهای تعطیل هفته — بیت‌ها بر اساس DayOfWeek:
    /// Sunday=1، Monday=2، Tuesday=4، Wednesday=8، Thursday=16، Friday=32، Saturday=64.
    /// پیش‌فرض: فقط جمعه (32). برای «پنجشنبه و جمعه» = 48.
    /// </summary>
    public int RestDayFlags { get; set; } = RestFriday;

    /// <summary>اعمال خودکار تعطیلات رسمی کشور (ثبت‌شده در تعطیلات رسمی) روی ارزیابی حضور</summary>
    public bool ApplyOfficialHolidays { get; set; } = true;

    /// <summary>
    /// ماندنِ پرسنل بعد از پایان شیفت، «مازادِ در انتظار تصمیم مدیر» ثبت شود (پیش‌فرض: بله).
    /// در این حالت مازاد نه کسری می‌شود و نه خودکار اضافه‌کار؛ مدیر در «حضور و غیاب» تصمیم می‌گیرد:
    /// تأیید → اضافه‌کاری، رد → تردد غیرمجاز.
    /// اگر خاموش شود، رفتار قدیمی برمی‌گردد: مازاد = اضافه‌کاری خودکار.
    /// (شیفتِ هر کاربر می‌تواند با <c>ShiftGroup.RequireExtraApproval</c> این تنظیم را بازنویسی کند.)
    /// </summary>
    public bool RequireExtraApproval { get; set; } = true;

    /// <summary>
    /// ساعت شروع/پایانِ شیفتِ اختصاصیِ کاربر بر ساعتِ ثبت‌شده در تقویم کاری مقدم باشد (پیش‌فرض: بله).
    /// اگر روشن باشد، مثلاً شیفت «۸ تا ۱۳» حتی وقتی تقویم کاری ۸ تا ۱۶:۳۰ ثبت شده، ملاک است
    /// (تقویم همچنان کاری/تعطیلی، اضافه‌کاری و یادداشت روز را تعیین می‌کند).
    /// </summary>
    public bool ShiftOverridesCalendarTimes { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // ================== کمکی‌های بیت روز هفته ==================

    public const int RestSunday = 1 << (int)DayOfWeek.Sunday;
    public const int RestMonday = 1 << (int)DayOfWeek.Monday;
    public const int RestTuesday = 1 << (int)DayOfWeek.Tuesday;
    public const int RestWednesday = 1 << (int)DayOfWeek.Wednesday;
    public const int RestThursday = 1 << (int)DayOfWeek.Thursday;
    public const int RestFriday = 1 << (int)DayOfWeek.Friday;
    public const int RestSaturday = 1 << (int)DayOfWeek.Saturday;

    /// <summary>آیا این روز هفته جزو روزهای تعطیل تنظیمات است؟</summary>
    public static bool IsRestDay(int flags, DateTime date) => (flags & (1 << (int)date.DayOfWeek)) != 0;
}
