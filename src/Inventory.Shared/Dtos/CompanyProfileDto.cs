namespace Inventory.Shared.Dtos;

/// <summary>
/// پروفایل شرکت/فروشگاه (تک یک ردیف) + تنظیمات چاپ فاکتور.
/// روی سربرگ و footer فاکتورهای چاپی (فروش/خرید) اعمال می‌شود.
/// </summary>
public class CompanyProfileDto
{
    public int Id { get; set; }

    /// <summary>نام فروشگاه / شرکت — داینامیک روی فاکتور چاپی</summary>
    public string Name { get; set; } = "";

    /// <summary>شماره حساب بانکی</summary>
    public string? AccountNumber { get; set; }

    /// <summary>شماره تماس</summary>
    public string? Phone { get; set; }

    public string? Address { get; set; }

    /// <summary>کد اقتصادی</summary>
    public string? EconomicCode { get; set; }

    /// <summary>شناسه ملی / مالیاتی</summary>
    public string? TaxId { get; set; }

    public string? Email { get; set; }

    public string? Website { get; set; }

    // ---------- تنظیمات چاپ فاکتور ----------
    /// <summary>متن‌های بالای فاکتور (هر خط جداگانه)</summary>
    public string? PrintHeaderLines { get; set; }

    /// <summary>متن‌های پایین فاکتور (هر خط جداگانه)</summary>
    public string? PrintFooterLines { get; set; }

    /// <summary>اندازه کاغذ: A4 یا A5 (پیش‌فرض A4)</summary>
    public string PrintPaperSize { get; set; } = "A4";

    /// <summary>نمایش کادر امضا در پایین فاکتور</summary>
    public bool PrintShowSignatures { get; set; } = true;

    public DateTime? UpdatedAt { get; set; }

    /// <summary>اندازه کاغذ A5 است؟</summary>
    public bool IsA5 => string.Equals(PrintPaperSize, "A5", StringComparison.OrdinalIgnoreCase);
}
