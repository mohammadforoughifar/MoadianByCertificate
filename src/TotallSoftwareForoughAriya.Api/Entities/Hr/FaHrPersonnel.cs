using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>وضعیت پرسنل در «منابع انسانی فروغ آریا» (مستقل از وضعیت پرونده‌های منابع انسانی اصلی)</summary>
public enum FaHrPersonnelStatus
{
    Active = 0,
    OnLeave = 1,
    Suspended = 2,
    Terminated = 3,
    Retired = 4
}

/// <summary>
/// ================== پرسنل «منابع انسانی فروغ آریا» (برنامهٔ پیشین) ==================
/// جدول اختصاصی این ماژول — هیچ وابستگی‌ای به پرونده‌های پرسنلی منابع انسانی اصلی (HrEmployees) ندارد.
/// حقوق و دستمزد، ارزیابی، دورهٔ آزمایشی و شیردهیِ این ماژول فقط به همین جدول ارجاع می‌دهند.
/// پیوند با کاربر ورود از طریق جدول اختصاصی HrUserLinks است.
/// <para>
/// هنگام جداسازی (ForoughAryaHrSeparationSchemaV1) داده‌های قبلی با همان شناسه‌ها از HrEmployees کپی
/// شدند تا ارجاع‌های قبلی (فیش‌ها، وام‌ها، ارزیابی‌ها و…) سالم بمانند؛ از آن به بعد دو جدول کاملاً جدا هستند.
/// </para>
/// </summary>
public class FaHrPersonnel
{
    public int Id { get; set; }

    /// <summary>کد پرسنلی (یکتا در همین ماژول)</summary>
    [MaxLength(20)] public string Code { get; set; } = "";
    [MaxLength(80)] public string FirstName { get; set; } = "";
    [MaxLength(80)] public string LastName { get; set; } = "";
    [MaxLength(10)] public string NationalCode { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    /// <summary>0=مرد 1=زن</summary>
    public int Gender { get; set; }
    /// <summary>0=مجرد 1=متأهل</summary>
    public int MaritalStatus { get; set; }
    [MaxLength(20)] public string? Mobile { get; set; }
    [MaxLength(150)] public string? Email { get; set; }
    [MaxLength(300)] public string? Address { get; set; }
    public DateTime HireDate { get; set; } = DateTime.Today;
    /// <summary>عنوان واحد سازمانی (متن آزاد — به ساختار سازمانی منابع انسانی اصلی ارجاع نمی‌دهد)</summary>
    [MaxLength(150)] public string? UnitTitle { get; set; }
    [MaxLength(150)] public string? PostTitle { get; set; }
    /// <summary>مدیر مستقیم (ارجاع به همین جدول) — مبنای چارت و گردش تأییدها</summary>
    public int? ManagerId { get; set; }
    /// <summary>نوع استخدام (۰ رسمی، ۱ قراردادی، ۲ پیمانی، ۳ ساعتی، ۴ مشاوره‌ای، ۵ تمام‌وقت، ۶ پاره‌وقت)</summary>
    public int EmploymentType { get; set; } = 1;
    public FaHrPersonnelStatus Status { get; set; } = FaHrPersonnelStatus.Active;
    public decimal BaseSalary { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(29)] public string? Sheba { get; set; }
    [MaxLength(60)] public string? BankName { get; set; }
    [MaxLength(12)] public string? InsuranceNo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>ثبت مراحل یک‌بارِ مهاجرت/جداسازی داده‌های «منابع انسانی فروغ آریا» (جدول اختصاصی همین ماژول)</summary>
public class FaHrSchemaMark
{
    [Key, MaxLength(80)] public string Step { get; set; } = "";
    public DateTime DoneAt { get; set; } = DateTime.Now;
}
