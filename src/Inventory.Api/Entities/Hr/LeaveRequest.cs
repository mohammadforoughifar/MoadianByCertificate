using System.ComponentModel.DataAnnotations;
namespace Inventory.Api.Data;

/// <summary>درخواست مرخصی/ماموریت — نوع: روزانه | ساعتی | ماموریت</summary>
public class LeaveRequest
{
    public int Id { get; set; }

    /// <summary>شماره منحصربه‌فرد: LR/سال شمسی/سریال</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    /// <summary>Daily | Hourly | HourlyMission | Mission</summary>
    [MaxLength(20)]
    public string Type { get; set; } = "Daily";

    public int RequesterUserId { get; set; }
    [MaxLength(150)]
    public string RequesterName { get; set; } = "";

    /// <summary>تاریخ شروع (ذخیره میلادی — نمایش شمسی)</summary>
    public DateTime StartDate { get; set; }
    /// <summary>تاریخ پایان (برای روزانه/ماموریت چندروزه)</summary>
    public DateTime EndDate { get; set; }

    /// <summary>ساعت شروع (برای نوع ساعتی و ماموریت ساعتی — الزامی)</summary>
    public TimeSpan? StartTime { get; set; }

    /// <summary>ساعت پایان (برای نوع ساعتی و ماموریت ساعتی — الزامی)</summary>
    public TimeSpan? EndTime { get; set; }

    /// <summary>تعداد روز (نوع روزانه/ماموریت چندروزه) — برای ساعتی صفر است</summary>
    public double Days { get; set; }

    /// <summary>تعداد ساعت (نوع ساعتی/ماموریت ساعتی — خودکار از ساعت شروع تا پایان محاسبه می‌شود، حداکثر ۸)</summary>
    public double Hours { get; set; }

    /// <summary>مقصد (نوع ماموریت)</summary>
    [MaxLength(200)]
    public string? Destination { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }

    /// <summary>Pending | Approved | Rejected</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public int? ApprovedByUserId { get; set; }
    public string? ApprovedByName { get; set; } = "";
    public DateTime? ApprovedAt { get; set; }
    [MaxLength(500)]
    public string? ApproveNote { get; set; }

    /// <summary>این درخواست توسط مدیر «به‌جای کاربر» ثبت و مستقیم تایید شده است (نه درخواست خودکاربر)</summary>
    public bool AdminCreated { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
