using Db = Inventory.Api.Data;
using Inventory.Api.Services.Export;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.Export;

// =====================================================================
// کنترلر خروجی PDF و Excel
//   api/exp/reports              فهرست گزارش‌های قابل خروجی‌گیری
//   api/exp/report/{key}         خروجی یک گزارش با فرمت دلخواه
// =====================================================================

/// <summary>خروجی گرفتن از گزارش‌های همه‌ی ماژول‌ها به صورت PDF یا Excel.</summary>
[Route("api/exp")]
public class ExportController : RbacControllerBase
{
    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PdfMime = "application/pdf";

    private readonly IExportService _svc;
    private readonly IDocArchiveExportService _docSvc;

    public ExportController(Db.AppDbContext db, IExportService svc, IDocArchiveExportService docSvc)
        : base(db)
    {
        _svc = svc;
        _docSvc = docSvc;
    }

    /// <summary>فهرست گزارش‌هایی که کاربر می‌تواند از آن‌ها خروجی بگیرد — «مرکز خروجی».</summary>
    [HttpGet("reports")]
    public ActionResult<List<ExportReportInfo>> GetReports([FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(Paging.Result(_svc.GetCatalog()
            .Select(r => new ExportReportInfo
            {
                Key = r.Key,
                Title = r.Title,
                Module = r.Module
            })
            .ToList(), skip, take));

    /// <summary>
    /// خروجی یک گزارش. فرمت با پارامتر <c>format</c> مشخص می‌شود: <c>pdf</c> یا <c>xlsx</c>.
    /// بقیه‌ی پارامترها همان فیلترهای صفحه‌ی گزارش هستند و به‌صورت query string می‌آیند.
    /// </summary>
    [HttpGet("report/{key}")]
    public async Task<IActionResult> GetReport(string key, [FromQuery] string format = "pdf",
        [FromQuery] ExportQuery? query = null)
    {
        var report = _svc.Find(key);
        if (report is null) return NotFound(new { message = $"گزارش «{key}» یافت نشد." });

        // دسترسی: کاربر باید مجوز Export همان ماژول را داشته باشد
        if (await ForbiddenUnlessAsync(report.RbacModule, "Export") is ObjectResult forbidden)
            return forbidden;

        query ??= new ExportQuery();
        if (query.MaxRows is <= 0 or > 50000) query.MaxRows = 5000;

        ExportSpec spec;
        try
        {
            // گزارش‌های آرشیو اسناد بر اساس دسترسی کاربرِ درخواست‌کننده فیلتر
            // می‌شوند؛ هویت از توکن گرفته می‌شود نه از query string.
            if (report.RbacModule == "DocArchive")
            {
                var isManager = await HasAsync("DocArchive", "Manage");
                spec = await _docSvc.BuildAsync(key, query, MyUserId, isManager);
                spec.Module = report.Module;
                spec.FileBaseName ??= report.Key;
            }
            else
            {
                spec = await _svc.BuildAsync(key, query);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var isExcel = format.Equals("xlsx", StringComparison.OrdinalIgnoreCase)
                   || format.Equals("excel", StringComparison.OrdinalIgnoreCase);

        var bytes = isExcel ? ExcelWriter.Build(spec) : PdfWriter.Build(spec);
        var name = spec.FileName(isExcel ? "xlsx" : "pdf");

        return File(bytes, isExcel ? XlsxMime : PdfMime, name);
    }
}

/// <summary>معرفی خلاصه‌ی یک گزارش برای نمایش در «مرکز خروجی».</summary>
public class ExportReportInfo
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Module { get; set; } = "";
}
