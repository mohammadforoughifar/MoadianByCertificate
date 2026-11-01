using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// تقویم کاری — تعریف روزبه‌روز ماه: روز کاری یا تعطیل، ساعت شروع/پایان هر روز،
/// و ساعت اضافه‌کاری مجاز برای روزهای تعطیل و جمعه.
/// ورود/خروج پرسنل بر اساس همین تقویم ارزیابی می‌شود؛ تردد خارج از بازه = تردد غیرمجاز/کسری.
/// اگر برای روزی ردیف تعریف نشده باشد، به‌ترتیب: تعطیلی شرکتی → شیفت کاربر → پیش‌فرض (جمعه تعطیل، ۰۸:۰۰–۱۶:۳۰) اعمال می‌شود.
/// </summary>
public class WorkCalendarDay
{
    public int Id { get; set; }

    /// <summary>تاریخ روز (فقط تاریخ — یکتا در کل تقویم)</summary>
    public DateTime Date { get; set; }

    /// <summary>true = روز کاری — false = تعطیل/غیرکاری</summary>
    public bool IsWorkday { get; set; } = true;

    /// <summary>ساعت شروع کارِ این روز (برای روز کاری)</summary>
    public TimeSpan? StartTime { get; set; }

    /// <summary>ساعت پایان کارِ این روز (برای روز کاری)</summary>
    public TimeSpan? EndTime { get; set; }

    /// <summary>دقیقه تاخیر مجازِ این روز (0 = استفاده از تاخیر مجاز شیفت کاربر)</summary>
    public int GraceMinutes { get; set; }

    /// <summary>ساعت اضافه‌کاری مجاز برای این روز (فقط در حالت «سقف ساعتی» — 0 = بدون اضافه‌کاری مجاز)</summary>
    public double OvertimeHours { get; set; }

    /// <summary>
    /// حالت اضافه‌کاریِ این روز:
    /// 0 = بدون (تعطیل: تردد مجاز نیست / کاری: کار بعد از پایان = اضافه‌کاری)
    /// 1 = بازه‌ی زمانی (از OvertimeStart تا OvertimeEnd — می‌تواند از نیمه‌شب عبور کند)
    /// 2 = کلِ روز (همه‌ی حضورِ روز = اضافه‌کاری)
    /// 3 = سقفِ ساعتی (تا OvertimeHours اضافه‌کاری مجاز است، مازاد = تردد غیرمجاز)
    /// </summary>
    public int OvertimeMode { get; set; }

    /// <summary>شروعِ بازه‌ی اضافه‌کاری (فقط در حالت «بازه‌ی زمانی»)</summary>
    public TimeSpan? OvertimeStart { get; set; }

    /// <summary>پایانِ بازه‌ی اضافه‌کاری (فقط در حالت «بازه‌ی زمانی» — ممکن است قبل از شروع = عبور از نیمه‌شب)</summary>
    public TimeSpan? OvertimeEnd { get; set; }

    /// <summary>عنوان/یادداشت روز (مثلاً: ایام عزاداری، نوبت نهایی‌حساب)</summary>
    [MaxLength(100)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}
