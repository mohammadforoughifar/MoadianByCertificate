namespace Inventory.Shared.Dtos;

/// <summary>تعمیرکار مجموعه.</summary>
public class Technician
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }

    /// <summary>تخصص (لپ‌تاپ، موبایل، دوربین، ...)</summary>
    public string? Specialty { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد پذیرش‌های فعال این تعمیرکار (پر شده توسط سرور)</summary>
    public int ActiveRepairs { get; set; }
}

/// <summary>پذیرش دستگاه تعمیری.</summary>
public class RepairOrderDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";

    public int PartyId { get; set; }
    public string? PartyName { get; set; }
    public string? PartyPhone { get; set; }
    public string? PartyMobile { get; set; }

    public int? TechnicianId { get; set; }
    public string? TechnicianName { get; set; }
    public string? TechnicianPhone { get; set; }

    /// <summary>فیلدهای سازگاریِ کارت اول؛ برای دریافت همه دستگاه‌ها از Devices استفاده کنید.</summary>
    public string DeviceType { get; set; } = "";
    public string? DeviceModel { get; set; }
    public string? SerialNumber { get; set; }
    public string? ProblemDescription { get; set; }
    public string? Accessories { get; set; }

    public RepairStatus Status { get; set; } = RepairStatus.Received;

    /// <summary>تاریخ ورود به مجموعه</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>تاریخ خروج (تحویل)</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>جمع برآورد دستگاه‌ها؛ مبلغ مستقل هر دستگاه در Devices.QuotedPrice است.</summary>
    public decimal QuotedPrice { get; set; }

    /// <summary>شناسه فاکتور فروش صادرشده</summary>
    public int? InvoiceTransactionId { get; set; }

    /// <summary>شماره فاکتور فروش صادرشده (پر شده توسط سرور)</summary>
    public string? InvoiceNumber { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>دستگاه‌های این پذیرش؛ مبلغ برآوردی و مشخصات هر دستگاه مستقل است.</summary>
    public List<RepairDeviceDto> Devices { get; set; } = new();

    /// <summary>کارها و قطعات مصرفی مشترکِ کل پذیرش.</summary>
    public List<RepairItemDto> Items { get; set; } = new();

    // ---------- جمع‌های محاسباتی (پر شده توسط سرور) ----------

    /// <summary>جمع دریافتی از مشتری (اجرت + قطعات)</summary>
    public decimal TotalPrice { get; set; }

    /// <summary>جمع هزینه داخلی (بهای قطعات + هزینه کار)</summary>
    public decimal TotalCost { get; set; }

    /// <summary>سود = دریافتی − هزینه</summary>
    public decimal Profit { get; set; }
}

/// <summary>مشخصات و مبلغ برآوردی یک دستگاه در پذیرش.</summary>
public class RepairDeviceDto
{
    public int Id { get; set; }
    public string DeviceType { get; set; } = "";
    public string? DeviceModel { get; set; }
    public string? SerialNumber { get; set; }
    public string? ProblemDescription { get; set; }
    public string? Accessories { get; set; }
    public decimal QuotedPrice { get; set; }
}

/// <summary>ردیف کار انجام‌شده / قطعه مصرفی؛ مشترکِ کل پذیرش.</summary>
public class RepairItemDto
{
    public int Id { get; set; }

    /// <summary>شرح کار یا قطعه</summary>
    public string Description { get; set; } = "";

    /// <summary>کالای مصرفی از انبار (null = فقط اجرت/کار)</summary>
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }

    public decimal Quantity { get; set; } = 1;

    /// <summary>هزینه داخلی</summary>
    public decimal Cost { get; set; }

    /// <summary>مبلغ دریافتی از مشتری</summary>
    public decimal Price { get; set; }
}

/// <summary>درخواست صدور فاکتور فروش از روی پذیرش تعمیر.</summary>
public class RepairInvoiceRequest
{
    /// <summary>انبار برداشت قطعات (پیش‌فرض: انبار مرکزی)</summary>
    public int WarehouseId { get; set; } = 1;

    /// <summary>روش تسویه: نقدی (Cash) یا نسیه (Credit)</summary>
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    /// <summary>نوع دریافت نقدی (کارت‌خوان، وجه نقد، کارت به کارت)</summary>
    public CashType? CashType { get; set; } = Inventory.Shared.CashType.CardReader;

    /// <summary>شناسه بانک / حساب خزانه انتخابی برای تسویه نقدی یا پیش‌دریافت</summary>
    public int? TrsAccountId { get; set; }

    /// <summary>تاریخ سررسید (در حالت نسیه)</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>پیش‌دریافت نقدی در حالت نسیه</summary>
    public decimal CashAmount { get; set; }
}
