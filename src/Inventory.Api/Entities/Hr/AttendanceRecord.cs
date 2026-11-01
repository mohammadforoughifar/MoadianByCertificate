using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// حضور و غیاب روزانه پرسنل — یک رکورد در روز به ازای هر کاربر
/// </summary>
public class AttendanceRecord
{
    public int Id { get; set; }

    /// <summary>تاریخ روز کاری (بدون زمان — برمبنای شمسی محاسبه می‌شود)</summary>
    public DateTime WorkDate { get; set; }

    public int UserId { get; set; }
    [MaxLength(150)]
    public string UserName { get; set; } = "";

    /// <summary>شیفت انتخابی برای امروز</summary>
    public int? ShiftGroupId { get; set; }
    public ShiftGroup? ShiftGroup { get; set; }

    // ---------- زمان‌ها ----------
    public DateTime? EnterAt { get; set; }   // ورود
    public DateTime? ExitAt { get; set; }    // خروج

    /// <summary>یادداشت توضیح (اختیاری — مثلاً ماموریت بین روز)</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>IP ثبت ورود/خروج (برای امنیت)</summary>
    [MaxLength(50)]
    public string? EnterIp { get; set; }
    [MaxLength(50)]
    public string? ExitIp { get; set; }

    // ---------- فیلدهای محاسبه‌شده هنگام ورود/خروج ----------
    /// <summary>وضعیت ورود: OnTime | Late | EarlyLeave | Absent | Present</summary>
    [MaxLength(20)]
    public string? EnterStatus { get; set; }

    /// <summary>تاخیر به دقیقه</summary>
    public int LateMinutes { get; set; }

    /// <summary>تعجیل در خروج به دقیقه</summary>
    public int EarlyLeaveMinutes { get; set; }

    /// <summary>کل حضور به دقیقه</summary>
    public int WorkMinutes { get; set; }

    /// <summary>کسری کار (دقیقه) — تفاضل ساعت کاری موظفی و کارکرد واقعی، با احتساب مرخصی تاییدشده</summary>
    public int DeficitMinutes { get; set; }

    /// <summary>دقیقه‌های اضافه‌کاری (کارکرد بعد از پایان شیفت روز یا کار در تعطیلات دارای اضافه‌کاری مجاز)</summary>
    public int OvertimeMinutes { get; set; }

    /// <summary>دقیقه‌های تردد غیرمجاز (حضور خارج از بازه‌ی مجاز تعریف‌شده در تقویم کاری)</summary>
    public int UnauthorizedMinutes { get; set; }

    /// <summary>
    /// دقیقه‌های «مازادِ در انتظار تصمیم مدیر» — ماندنِ بعد از پایان شیفت که نه کسری شده و نه
    /// هنوز به اضافه‌کاری/تردد غیرمجاز تبدیل شده است (تصمیم در <c>AttendanceSegment.ExtraDecision</c> است).
    /// </summary>
    public int PendingOvertimeMinutes { get; set; }

    /// <summary>دقیقه‌های غیبتِ پوشش‌شده با مرخصی/ماموریت ساعتی تاییدشده (از مجموع کسری کم می‌شود)</summary>
    public int CoveredGapMinutes { get; set; }

    /// <summary>مرخصی تاییدشده در این روز دارد (برای عدم احتساب کسری)</summary>
    public bool HasApprovedLeave { get; set; }

    /// <summary>وضعیت نهایی روز: Present | Absent | HalfDay | LeaveDay | Holiday</summary>
    [MaxLength(20)]
    public string? FinalStatus { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}
