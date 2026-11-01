using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// درخواست «ویرایش» یا «حذف» یک پروژه که باید مدیر آن را تایید کند.
/// تا زمانی که مدیر تایید نکرده، هیچ تغییری روی خود پروژه اعمال نمی‌شود؛
/// با تایید مدیر، تغییرِ ذخیره‌شده (PayloadJson) اعمال یا پروژه حذف نرم می‌شود.
/// </summary>
public class ProjectChangeRequest
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    /// <summary>نوع درخواست: ۱ = ویرایش اطلاعات، ۲ = حذف پروژه</summary>
    public int Kind { get; set; }

    /// <summary>وضعیت: ۰ = در انتظار تایید مدیر، ۱ = تایید و اعمال شد، ۲ = رد شد</summary>
    public int Status { get; set; }

    /// <summary>مقادیر جدید پروژه به‌صورت JSON (فقط برای درخواست ویرایش)</summary>
    public string? PayloadJson { get; set; }

    /// <summary>خلاصهٔ خوانای تغییرات برای نمایش به مدیر</summary>
    [MaxLength(2000)] public string Summary { get; set; } = "";

    /// <summary>توضیح درخواست‌دهنده (اختیاری)</summary>
    [MaxLength(500)] public string? RequestNote { get; set; }

    /// <summary>یادداشت مدیر (دلیل رد یا توضیح تایید)</summary>
    [MaxLength(500)] public string? ManagerNote { get; set; }

    public int RequestedById { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.Now;

    public int? ManagerActionById { get; set; }
    public DateTime? ManagerActionAt { get; set; }

    // ---------- Navigation ----------
    public ProjectEntryExit? Project { get; set; }
    public User? RequestedBy { get; set; }
}
