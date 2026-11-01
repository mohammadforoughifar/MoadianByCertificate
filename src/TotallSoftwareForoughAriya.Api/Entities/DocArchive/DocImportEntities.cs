namespace Inventory.Api.Data;

/// <summary>
/// یک نوبت ورود انبوه: کاربر پوشه‌ای را در مرورگر انتخاب می‌کند و فایل‌ها یکی‌یکی
/// می‌فرستد؛ هر فایل در همان درخواست وارد آرشیو می‌شود. هیچ کارگر پس‌زمینه و هیچ
/// ماشین وضعیتی در کار نیست — «نوبت» فقط برای گزارش اکسل و سابقه است.
/// </summary>
public class DocImportBatch
{
    public int Id { get; set; }

    /// <summary>لحظهٔ انتخاب پوشه.</summary>
    public DateTime StartedAt { get; set; } = DateTime.Now;

    /// <summary>لحظهٔ پایان (وقتی کاربر دکمهٔ «تمام شد» را زد یا آپلود کامل شد).</summary>
    public DateTime? FinishedAt { get; set; }

    public int CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";

    /// <summary>کل فایل‌هایی که در پیش‌نمایش دیده شدند.</summary>
    public int TotalFiles { get; set; }

    public int ImportedCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }

    /// <summary>پوشه‌هایی که حین ورود ساخته شدند.</summary>
    public int CreatedFolderCount { get; set; }

    /// <summary>پوشهٔ مقصد انتخابی کاربر (null = ریشهٔ آرشیو).</summary>
    public int? RootFolderId { get; set; }

    public bool CreateMissingFolders { get; set; } = true;
}

/// <summary>وضعیت یک فایل در نوبت ورود.</summary>
public static class DocImportResultStatus
{
    /// <summary>نام درست است و آمادهٔ ورود — هنوز ارسال نشده.</summary>
    public const string Pending = "Pending";

    /// <summary>وارد آرشیو شد.</summary>
    public const string Imported = "Imported";

    /// <summary>شماره‌اش قبلاً در آرشیو (یا در همین پوشه) بود؛ وارد نشد.</summary>
    public const string Duplicate = "Duplicate";

    /// <summary>نام یا فایل مشکل دارد؛ وارد نشد.</summary>
    public const string Failed = "Failed";

    /// <summary>فایل سیستمی (Thumbs.db و مثل آن) — نادیده.</summary>
    public const string Skipped = "Skipped";
}

/// <summary>نتیجهٔ یک فایل در نوبت ورود — هم پیش‌نمایش، هم نتیجهٔ نهایی.</summary>
public class DocImportResult
{
    public long Id { get; set; }

    public int BatchId { get; set; }

    /// <summary>مسیر نسبی فایل داخل پوشهٔ انتخابی، با جداکنندهٔ «/».</summary>
    public string RelativePath { get; set; } = "";

    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }

    public string Status { get; set; } = DocImportResultStatus.Pending;

    /// <summary>کد خطا (برای گزارش اکسل) — فقط وقتی Status = Failed.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>توضیح فارسی خطا.</summary>
    public string? MessageFa { get; set; }

    /// <summary>عنوان و شمارهٔ خوانده‌شده از نام فایل.</summary>
    public string? ParsedTitle { get; set; }
    public string? ParsedCode { get; set; }

    /// <summary>سند ساخته‌شده — فقط وقتی Status = Imported.</summary>
    public int? DocumentId { get; set; }

    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}
