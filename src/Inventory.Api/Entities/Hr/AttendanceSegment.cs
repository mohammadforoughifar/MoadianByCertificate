using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// یک بازه‌ی ورود/خروج در طول روز (حداکثر ۵ بازه به ازای هر کاربر در هر روز).
/// بازه‌ی غیبت بعد از هر خروج باید با مرخصی ساعتی یا ماموریت ساعتی تاییدشده پوشش داده شود.
/// </summary>
public class AttendanceSegment
{
    public int Id { get; set; }

    public int UserId { get; set; }
    [MaxLength(150)]
    public string UserName { get; set; } = "";

    /// <summary>تاریخ روز کاری (بدون زمان)</summary>
    public DateTime WorkDate { get; set; }

    /// <summary>شماره‌ی بازه در روز (۱ تا ۵)</summary>
    public int Seq { get; set; }

    // ---------- زمان‌ها ----------
    public DateTime? EnterAt { get; set; }
    public string? EnterIp { get; set; }

    /// <summary>دستگاه/مرورگر ورود (User-Agent کوتاه‌شده) — برای تشخیص ورود با دستگاه جدید</summary>
    [MaxLength(250)]
    public string? EnterDevice { get; set; }

    public DateTime? ExitAt { get; set; }
    public string? ExitIp { get; set; }

    /// <summary>OnTime | Late | Return (بازگشت بعد از غیبت)</summary>
    [MaxLength(20)]
    public string? EnterStatus { get; set; }

    /// <summary>تاخیر ورود (فقط برای اولین ورود روز محاسبه می‌شود)</summary>
    public int LateMinutes { get; set; }

    /// <summary>
    /// آیا بازه‌ی غیبتِ بعد از این خروج، به‌طور کامل با مرخصی/ماموریت ساعتی تاییدشده پوشش دارد؟
    /// (موقتی هنگام خروج — قطعی هنگام ورود مجدد یا پایان شیفت)
    /// </summary>
    public bool ExitCovered { get; set; }

    /// <summary>این بازه تردد غیرمجاز بوده (خارج از بازه‌ی مجاز تقویم کاری / حضور در تعطیل بدون اضافه‌کاری مجاز)</summary>
    public bool IsUnauthorized { get; set; }

    /// <summary>دقیقه‌های اضافه‌کاریِ این بازه (کارکرد بعد از پایان شیفت یا کار مجاز در تعطیلات)</summary>
    public int OvertimeMinutes { get; set; }

    /// <summary>دقیقه‌های «مازادِ در انتظار تصمیم» این بازه (ماندن بعد از پایان شیفت)</summary>
    public int PendingMinutes { get; set; }

    /// <summary>
    /// تصمیم مدیر درباره‌ی مازادِ این بازه (ExtraDecision):
    /// 0 = در انتظار تصمیم | 1 = تأیید (→ اضافه‌کاری) | 2 = رد (→ تردد غیرمجاز).
    /// چون تصمیم روی خودِ بازه ذخیره می‌شود، در بازحساب‌های بعدی (تغییر شیفت/مرخصی/تقویم) پاک نمی‌شود.
    /// </summary>
    public int ExtraDecision { get; set; }

    /// <summary>شناسه‌ی کاربری که تصمیم مازاد را گرفته (null = هنوز تصمیمی گرفته نشده)</summary>
    public int? ExtraDecidedByUserId { get; set; }

    /// <summary>زمان ثبت تصمیم مازاد</summary>
    public DateTime? ExtraDecidedAt { get; set; }

    /// <summary>یادداشت مدیر درباره‌ی تصمیم مازاد (اختیاری)</summary>
    [MaxLength(300)]
    public string? ExtraDecisionNote { get; set; }

    /// <summary>درخواست مرخصی/ماموریت ساعتی که بازه را پوشش می‌دهد (در صورت وجود)</summary>
    public int? LinkedLeaveRequestId { get; set; }
    public LeaveRequest? LinkedLeaveRequest { get; set; }

    /// <summary>شماره‌ی درخواست پوشش‌دهنده (برای نمایش)</summary>
    [MaxLength(30)]
    public string? LinkedLeaveNumber { get; set; }

    /// <summary>یادداشت (مثلاً دلیل خروج میانی)</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
