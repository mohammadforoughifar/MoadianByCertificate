using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>وضعیت‌های پروژه کاری — مثل دستور کار: باز یا بسته.</summary>
public static class WorkProjectStatus
{
    public const string Open = "Open";
    public const string Closed = "Closed";

    public static string ToFa(string? s) => (s ?? Open) == Closed ? "بسته" : "باز";
}

/// <summary>
/// پروژه کاری — پوشه‌ای سبک روی چند دستور کار، داخل ماژول دستور کار.
/// (با «پروژه ورود/خروج» ماژول Projects فرق دارد — آن برای ورود/خروج کالا است، این برای مدیریت کار تیمی.)
/// تاریخ‌ها دقت «روز» دارند؛ کلِ روزِ مهلت جزو مهلت حساب می‌شود.
/// </summary>
public class WorkProject
{
    public int Id { get; set; }

    /// <summary>شرکت فعال مالک پروژه (مثل دستور کار — شرکت‌محور).</summary>
    public int CompanyId { get; set; }

    /// <summary>شماره منحصربه‌فرد: PRJ/سال شمسی/سریال</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>شرح ساده (متن چندخطی — بدون ادیتور).</summary>
    [MaxLength(4000)]
    public string? Description { get; set; }

    /// <summary>ساختار نقشهٔ ذهنی پروژه (JSON نسخه‌دار؛ دادهٔ پیشرفت پروژه را تغییر نمی‌دهد).</summary>
    public string? MindFlowJson { get; set; }

    /// <summary>مدیر پروژه (سازنده).</summary>
    public int OwnerUserId { get; set; }

    [MaxLength(150)]
    public string OwnerName { get; set; } = "";

    /// <summary>تاریخ شروع (دقت روز).</summary>
    public DateTime StartAt { get; set; }

    /// <summary>مهلت پایان (دقت روز).</summary>
    public DateTime DueAt { get; set; }

    /// <summary>Open | Closed</summary>
    [MaxLength(20)]
    public string Status { get; set; } = WorkProjectStatus.Open;

    /// <summary>اولویت: 0=کم | 1=عادی | 2=بالا | 3=فوری (مشترک با دستور کار).</summary>
    public int Priority { get; set; } = WorkOrderPriority.Normal;

    /// <summary>تعداد تمدیدها — حداکثر ۵ بار (مثل دستور کار).</summary>
    public int ExtensionCount { get; set; }

    /// <summary>برچسب‌ها — همان قالب دستور کار: «,تگ1,تگ2,».</summary>
    [MaxLength(300)]
    public string? Tags { get; set; }

    [MaxLength(1000)]
    public string? CloseNote { get; set; }
    public DateTime? ClosedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<WorkOrder> WorkOrders { get; set; } = new List<WorkOrder>();
    public ICollection<WorkProjectMember> Members { get; set; } = new List<WorkProjectMember>();
    public ICollection<WorkProjectMindFlowMember> MindFlowMembers { get; set; } = new List<WorkProjectMindFlowMember>();
    public ICollection<WorkProjectLog> Logs { get; set; } = new List<WorkProjectLog>();
}

/// <summary>عضو تیم پروژه کاری — «چه کسانی روی این پروژه کار می‌کنند».</summary>
public class WorkProjectMember
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int UserId { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = "";

    public DateTime AddedAt { get; set; } = DateTime.Now;

    public WorkProject? Project { get; set; }
}

/// <summary>رویدادهای پروژه کاری — تاریخچه فعالیت (حسابرسی + «آخرین فعالیت»).</summary>
public class WorkProjectLog
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    [MaxLength(150)]
    public string ActorName { get; set; } = "";

    /// <summary>
    /// Created | Edited | Extended | Closed | Reopened | Deleted |
    /// MemberAdded | MemberRemoved | OrderAdded | OrderRemoved | OrderClosed | OrderReopened
    /// </summary>
    [MaxLength(30)]
    public string Action { get; set; } = "";

    [MaxLength(4000)]
    public string? Text { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public WorkProject? Project { get; set; }
}
