using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// گروه شیفت (شیفت کاری) — تعیین ساعت ورود/خروج، ساعات کاری و تاخیر مجاز
/// </summary>
public class ShiftGroup
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = ""; // مثلاً: شیفت صبح، شیفت عصر، نگهبانی شب

    [MaxLength(200)]
    public string? Description { get; set; }

    /// <summary>ساعت ورود قانونی (TimeSpan — فقط ساعت:دقیقه)</summary>
    public TimeSpan StartTime { get; set; } = new TimeSpan(8, 0, 0);

    /// <summary>ساعت خروج قانونی</summary>
    public TimeSpan EndTime { get; set; } = new TimeSpan(16, 30, 0);

    /// <summary>شروعِ پنجره‌ی دومِ شیفت دوپاره (مثلاً 17:00 برای «۸–۱۳ + ۱۷–۲۱:۳۰») — null = شیفت تک‌بازه‌ای</summary>
    public TimeSpan? StartTime2 { get; set; }

    /// <summary>پایانِ پنجره‌ی دومِ شیفت دوپاره (مثلاً 21:30) — باید بعد از StartTime2 باشد</summary>
    public TimeSpan? EndTime2 { get; set; }

    /// <summary>دقیقه تاخیر مجاز بدون جریمه</summary>
    public int GraceMinutes { get; set; } = 10;

    /// <summary>کار روز جمعه هم حساب شود (برای شیفت‌های چرخشی)</summary>
    public bool IncludeFriday { get; set; }

    /// <summary>
    /// ماندنِ این شیفت بعد از پایان ساعت، «در انتظار تصمیم مدیر» ثبت شود (نه کسری، نه اضافه‌کار خودکار).
    /// null = از تنظیم سراسری تقویم کاری (<c>WorkCalendarSettings.RequireExtraApproval</c>) پیروی کند.
    /// </summary>
    public bool? RequireExtraApproval { get; set; }

    /// <summary>فعال/غیرفعال</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ---------- رابطه ----------
    public List<AttendanceRecord> AttendanceRecords { get; set; } = new();
}
