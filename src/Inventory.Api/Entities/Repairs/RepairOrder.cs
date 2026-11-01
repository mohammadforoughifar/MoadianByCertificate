using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>پذیرش دستگاه تعمیری (لپ‌تاپ، کامپیوتر، مانیتور، دوربین و ...)</summary>
public class RepairOrder : ICompanyOwned
{
    public int Id { get; set; }

    /// <summary>شرکت مالک رکورد — دادهٔ هر شرکت مستقل نگه‌داری می‌شود (فیلتر خودکار).</summary>
    public int? CompanyId { get; set; }
    /// <summary>شماره پذیرش (RP-0001)</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    /// <summary>مشتری (صاحب دستگاه)</summary>
    public int PartyId { get; set; }

    /// <summary>تعمیرکار مسئول</summary>
    public int? TechnicianId { get; set; }

    /// <summary>فیلدهای سازگاریِ کارت اول؛ فهرست کامل دستگاه‌ها در Devices نگهداری می‌شود.</summary>
    [MaxLength(100)]
    public string DeviceType { get; set; } = "";

    /// <summary>برند و مدل دستگاه کارت اول (برای سازگاری با گزارش‌ها و APIهای قدیمی).</summary>
    [MaxLength(200)]
    public string? DeviceModel { get; set; }

    /// <summary>سریال / شناسه دستگاه کارت اول.</summary>
    [MaxLength(100)]
    public string? SerialNumber { get; set; }

    /// <summary>شرح ایراد اعلامی مشتری برای دستگاه کارت اول.</summary>
    [MaxLength(1000)]
    public string? ProblemDescription { get; set; }

    /// <summary>لوازم همراه دستگاه کارت اول (برای سازگاری با نسخه‌های قدیمی).</summary>
    [MaxLength(500)]
    public string? Accessories { get; set; }

    public RepairStatus Status { get; set; } = RepairStatus.Received;

    /// <summary>تاریخ ورود به مجموعه (پذیرش)</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>تاریخ خروج از مجموعه (تحویل)</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>جمع مبالغ برآوردی کارت‌های دستگاه؛ برای سازگاری با گزارش‌ها و نسخه‌های قدیمی.</summary>
    public decimal QuotedPrice { get; set; }

    /// <summary>شناسه فاکتور فروش صادرشده (بعد از تحویل)</summary>
    public int? InvoiceTransactionId { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>دستگاه‌های این پذیرش (هر پذیرش می‌تواند چند دستگاه داشته باشد).</summary>
    public List<RepairDevice> Devices { get; set; } = new();

    /// <summary>کارها و قطعات مشترکِ کل پذیرش، نه مختص یک دستگاه.</summary>
    public List<RepairItem> Items { get; set; } = new();
}
