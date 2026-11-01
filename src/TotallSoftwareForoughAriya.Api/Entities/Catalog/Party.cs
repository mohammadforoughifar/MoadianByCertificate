using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

public class Party : ICompanyOwned
{
    public int Id { get; set; }
    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    public PartyType Type { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(50)]
    public string? Mobile { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    /// <summary>شناسه مالیاتی خریدار (کد ملی/شناسه اقتصادی)، برای صورتحساب نوع اول</summary>
    [MaxLength(20)]
    public string? TaxId { get; set; }

    /// <summary>کد پستی خریدار (۱۰ رقم)</summary>
    [MaxLength(20)]
    public string? PostalCode { get; set; }

    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>معرف پیش‌فرض مشتری (اختیاری — null = بدون معرف)</summary>
    public int? ReferrerId { get; set; }

    // ---------- ثبت‌کننده (چه کسی این طرف حساب را ساخت) ----------

    /// <summary>کاربری که این طرف حساب را ثبت کرده است (۰ = ثبت سیستمی/قدیمی)</summary>
    public int? RegisteredByUserId { get; set; }

    /// <summary>نام ثبت‌کننده در لحظهٔ ثبت — snapshot؛ تغییر نام/حذف کاربر سابقه را نمی‌شکند</summary>
    [MaxLength(150)]
    public string? RegisteredByName { get; set; }

    /// <summary>پروندهٔ معرفی که این مشتری از طریق آن ثبت شده (ویزیتور)</summary>
    public int? RegisteredByReferrerId { get; set; }

    /// <summary>زمان ثبت</summary>
    public DateTime? RegisteredAt { get; set; }
}