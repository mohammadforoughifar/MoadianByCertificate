namespace Inventory.Api.Services.Export;

/// <summary>ساخت مشخصات گزارش‌های قابل خروجی‌گیری (PDF و Excel).</summary>
public interface IExportService
{
    /// <summary>فهرست گزارش‌هایی که می‌توان از آن‌ها خروجی گرفت.</summary>
    IReadOnlyList<ExportReport> GetCatalog();

    /// <summary>یافتن یک گزارش بر اساس شناسه‌ی متنی آن.</summary>
    ExportReport? Find(string key);

    /// <summary>ساخت مشخصات گزارش با داده‌ی واقعی.</summary>
    Task<ExportSpec> BuildAsync(string key, ExportQuery q);
}
