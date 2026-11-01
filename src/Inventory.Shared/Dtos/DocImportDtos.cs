namespace Inventory.Shared.Dtos;

/// <summary>وضعیت پیکربندی ورود انبوه روی سرور.</summary>
public class DocImportConfigDto
{
    public bool Enabled { get; set; }
    public long MaxFileBytes { get; set; }
    public bool CreateMissingFolders { get; set; }

    /// <summary>اگر ورود خاموش است، توضیح فارسی آن.</summary>
    public string? ProblemFa => Enabled ? null
        : "ورود انبوه روی سرور خاموش است؛ چون متغیر محیطی DocArchiveImport__Enabled=false تنظیم شده. آن را حذف کنید تا دوباره روشن شود.";
}

public class DocImportBatchCreateDto
{
    /// <summary>پوشهٔ مقصد در آرشیو (null = ریشه).</summary>
    public int? RootFolderId { get; set; }

    public bool CreateMissingFolders { get; set; } = true;
}

/// <summary>یک نوبت ورود — برای سابقه و گزارش.</summary>
public class DocImportBatchDto
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string CreatedByName { get; set; } = "";

    public int TotalFiles { get; set; }
    public int ImportedCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public int CreatedFolderCount { get; set; }

    public string StatusFa { get; set; } = "";
}

/// <summary>یک فایل در درخواست پیش‌نمایش.</summary>
public class DocImportPreviewItemDto
{
    public string RelativePath { get; set; } = "";
    public long SizeBytes { get; set; }
}

public class DocImportPreviewRequestDto
{
    public List<DocImportPreviewItemDto> Items { get; set; } = new();
}

/// <summary>فایلی که مشکل دارد — برای نمایش در صفحه و گزارش.</summary>
public class DocImportProblemDto
{
    public string RelativePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ErrorCode { get; set; }
    public string MessageFa { get; set; } = "";
}

public class DocImportPreviewSummaryDto
{
    public int BatchId { get; set; }
    public int Total { get; set; }
    public int Ready { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Duplicate { get; set; }
    public List<DocImportProblemDto> Problems { get; set; } = new();
}

/// <summary>نتیجهٔ ورود یک فایل.</summary>
public class DocImportFileResultDto
{
    public string Status { get; set; } = "";
    public bool Duplicate { get; set; }
    public string MessageFa { get; set; } = "";
}

/// <summary>وضعیت زندهٔ آپلود — از جاوااسکریپت به صفحه می‌آید.</summary>
public class DocImportUploadStateDto
{
    /// <summary>مرحلهٔ جاری: picked, batch, preview, previewed, progress, done</summary>
    public string Stage { get; set; } = "";

    public int BatchId { get; set; }
    public int Total { get; set; }
    public int Index { get; set; }

    public int Ready { get; set; }
    public int Imported { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Duplicate { get; set; }

    public string? Name { get; set; }
    public string? Message { get; set; }
    public List<string> FailedNames { get; set; } = new();

    /// <summary>فایل‌های مشکل‌دار — در مرحلهٔ پیش‌نمایش پر می‌شود.</summary>
    public List<DocImportProblemDto> Problems { get; set; } = new();

    public bool Cancelled { get; set; }

    /// <summary>خطایی که کل جریان را متوقف کرد.</summary>
    public string? Fatal { get; set; }
}
