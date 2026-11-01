using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>معرف (بازاریاب) با درصد پورسانت جداگانه برای کالا و خدمات.</summary>
public class Referrer : ICompanyOwned
{
    public int Id { get; set; }

    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    [MaxLength(150)]
    public string Name { get; set; } = "";

    /// <summary>نام مجموعه / شرکت</summary>
    [MaxLength(200)]
    public string? CompanyName { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    /// <summary>درصد پورسانت فروش کالا (بر مبنای سود کالا)</summary>
    public decimal GoodsCommissionPercent { get; set; }

    /// <summary>درصد پورسانت فروش خدمات (بر مبنای کل مبلغ خدمات)</summary>
    public decimal ServiceCommissionPercent { get; set; }

    /// <summary>شماره کارت بانکی معرف (برای واریز پورسانت)</summary>
    [MaxLength(20)]
    public string? CardNumber { get; set; }

    /// <summary>شماره شبا (IBAN) معرف</summary>
    [MaxLength(30)]
    public string? Iban { get; set; }

    /// <summary>
    /// سقف درصد تخفیف مجاز این ویزیتور/معرف در سفارش میدانی.
    /// صفر = بدون سقف (تخفیف با نظر مدیر فروش بررسی می‌شود).
    /// </summary>
    public decimal MaxDiscountPercent { get; set; }

    /// <summary>اجازه مشاهده کالاهای موجود در پنل معرف</summary>
    public bool CanViewProducts { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}