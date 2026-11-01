using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// ============================================================
//  گزارش‌ساز حرفه‌ای — موجودیت‌ها
//
//  RsReport            : تعریف گزارش (کوئری به‌صورت JSON)
//  RsReportUserShare   : اشتراک با کاربرِ مشخص + سطح دسترسی
//  RsReportRoleShare   : اشتراک با نقش + سطح دسترسی
//
//  چرا QueryJson؟ ساختار کوئری درختی و متغیر است؛ نگه‌داشتنش در چند
//  جدول رابطه‌ای، خواندن/نوشتن را بدون سود واقعی پیچیده می‌کند.
//  اعتبارسنجی هنگام ذخیره و هنگام اجرا روی سرور انجام می‌شود.
// ============================================================

public class RsReport
{
    public int Id { get; set; }

    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(500)] public string? Description { get; set; }
    [MaxLength(60)] public string Icon { get; set; } = "bi-file-earmark-bar-graph";
    /// <summary>پوشهٔ دلخواه کاربر برای دسته‌بندی گزارش‌ها</summary>
    [MaxLength(100)] public string? Folder { get; set; }

    /// <summary>سازندهٔ گزارش — همیشه دسترسی کامل دارد</summary>
    public int OwnerUserId { get; set; }

    /// <summary>0=خصوصی، 1=اشتراکی، 2=همه</summary>
    public int Visibility { get; set; }

    /// <summary>تعریف کوئری (RsQueryDto) به‌صورت JSON با camelCase</summary>
    public string QueryJson { get; set; } = "{}";

    /// <summary>
    /// ماژول‌های RBAC که این گزارش به آن‌ها دست می‌زند (با کاما جدا).
    /// هنگام اجرا بررسی می‌شود تا اشتراک‌گذاری نتواند مجوز داده را دور بزند.
    /// </summary>
    [MaxLength(500)] public string ModulesCsv { get; set; } = "";

    public bool IsDelete { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<RsReportUserShare> UserShares { get; set; } = new List<RsReportUserShare>();
    public ICollection<RsReportRoleShare> RoleShares { get; set; } = new List<RsReportRoleShare>();
}

/// <summary>اشتراک گزارش با یک کاربر مشخص.</summary>
public class RsReportUserShare
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public int UserId { get; set; }
    /// <summary>0=View، 1=Export، 2=Edit</summary>
    public int Access { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public RsReport? Report { get; set; }
}

/// <summary>اشتراک گزارش با یک نقش.</summary>
public class RsReportRoleShare
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public int RoleId { get; set; }
    /// <summary>0=View، 1=Export، 2=Edit</summary>
    public int Access { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public RsReport? Report { get; set; }
}
