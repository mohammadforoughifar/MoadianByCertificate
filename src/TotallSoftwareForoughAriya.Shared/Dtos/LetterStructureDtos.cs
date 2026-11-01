namespace Inventory.Shared.Dtos;

// ============================================================
//  تنظیمات ساختار شماره نامه (شماره اندیکاتور) — اتوماسیون اداری
//  سه نوع نامه: داخلی (TypeForm=1) / صادره (2) / وارده (3)
//  هر ساختار = ترتیبی از اجزای «واحد» | «شماره» | «سال»
// ============================================================

/// <summary>سازمان/واحد — گزینه‌ی مبنای جزء «واحد»</summary>
public class OrganizationDto
{
    public int OrganizationId { get; set; }

    /// <summary>شناسه شرکت — واحدها به صورت جداگانه برای هر شرکت تعریف می‌شوند</summary>
    public int? CompanyId { get; set; }

    /// <summary>نام نمایشی — مثل «مدیریت کیفیت»</summary>
    public string NameUnit { get; set; } = "";

    /// <summary>نام اختصاصی که در شماره می‌نشیند — مثل MQ</summary>
    public string NameUniq { get; set; } = "";

    public bool IsDefault { get; set; }
}

/// <summary>ساختار یک نوع نامه — اجزا به ترتیب</summary>
public class LetterStructureItemDto
{
    /// <summary>1=نامه داخلی، 2=نامه صادره، 3=نامه وارده</summary>
    public int TypeForm { get; set; }

    /// <summary>عنوان فارسی نوع نامه</summary>
    public string Title { get; set; } = "";

    /// <summary>اجزای ساختار به ترتیب — «واحد» | «شماره» | «سال»</summary>
    public List<string> Parts { get; set; } = new();
}

/// <summary>داده‌ی کامل صفحه‌ی تنظیمات ساختار شماره</summary>
public class LetterStructurePageDto
{
    public List<LetterStructureItemDto> Items { get; set; } = new();

    /// <summary>سازمان‌های قابل انتخاب برای «سازمان پیش‌فرض»</summary>
    public List<OrganizationDto> Organizations { get; set; } = new();

    public int? DefaultOrganizationId { get; set; }
}

/// <summary>بدنه‌ی به‌روزرسانی ساختار یک نوع نامه</summary>
public class UpdateLetterStructureDto
{
    /// <summary>اجزای جدید به ترتیب — اعتبار: حداقل «شماره + یک جزء دیگر»، بدون تکرار</summary>
    public List<string> Parts { get; set; } = new();
}
