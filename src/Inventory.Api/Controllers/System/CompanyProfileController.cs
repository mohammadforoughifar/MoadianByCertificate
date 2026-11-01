using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// پروفایل شرکت/فروشگاه که در سربرگ فاکتورها و رسید تحویل تعمیرات چاپ می‌شود.
/// <para>
/// پیش از این هیچ endpointی برای مسیر <c>api/company-profile</c> وجود نداشت و صفحه‌های
/// فاکتور/تعمیرات با 404 مواجه می‌شدند. منبع داده «شرکت فعال» (جدول SystemCompanies) است؛
/// بنابراین نام/تلفن/آدرس هر شرکت جداگانه چاپ می‌شود.
/// </para>
/// <para>
/// فیلدهای تکمیلی (کد اقتصادی، شناسهٔ مالیاتی، متن‌های سربرگ/پانویس) از بخش
/// <c>Company</c> در appsettings خوانده می‌شوند تا بدون تغییر اسکیما قابل تنظیم باشند.
/// </para>
/// </summary>
[ApiController]
[Route("api/company-profile")]
public class CompanyProfileController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public CompanyProfileController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>پروفایل شرکت فعال (در نبود شرکت فعال، اولین شرکت فعال سیستم).</summary>
    [HttpGet]
    public async Task<ActionResult<CompanyProfileDto>> Get()
    {
        var companyId = ActiveCompanyId();

        var company = companyId is > 0
            ? await _db.SystemCompanies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId.Value)
            : await _db.SystemCompanies.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Id).FirstOrDefaultAsync();

        var dto = new CompanyProfileDto
        {
            Id = company?.Id ?? 0,
            Name = FirstNonEmpty(company?.Name, _config["Company:Name"], "فروشگاه"),
            Phone = FirstNonEmpty(company?.Phone, _config["Company:Phone"]),
            Address = FirstNonEmpty(company?.Address, _config["Company:Address"]),
            AccountNumber = _config["Company:AccountNumber"],
            EconomicCode = _config["Company:EconomicCode"],
            TaxId = _config["Company:TaxId"],
            Email = _config["Company:Email"],
            Website = _config["Company:Website"],
            PrintHeaderLines = _config["Company:PrintHeaderLines"],
            PrintFooterLines = _config["Company:PrintFooterLines"],
            PrintPaperSize = string.IsNullOrWhiteSpace(_config["Company:PrintPaperSize"]) ? "A4" : _config["Company:PrintPaperSize"]!,
            PrintShowSignatures = !bool.TryParse(_config["Company:PrintShowSignatures"], out var show) || show,
            UpdatedAt = company?.CreatedAt
        };

        return Ok(dto);
    }

    /// <summary>
    /// ذخیرهٔ پروفایل — فیلدهای نام/تلفن/آدرس روی «شرکت فعال» نوشته می‌شوند
    /// (بقیهٔ فیلدها فقط بازگردانده می‌شوند و از appsettings خوانده می‌شوند).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CompanyProfileDto>> Save([FromBody] CompanyProfileDto profile)
    {
        if (profile is null) return BadRequest(new { message = "اطلاعات پروفایل خالی است." });

        var companyId = ActiveCompanyId();
        var company = companyId is > 0
            ? await _db.SystemCompanies.FirstOrDefaultAsync(c => c.Id == companyId.Value)
            : await _db.SystemCompanies.Where(c => c.IsActive).OrderBy(c => c.Id).FirstOrDefaultAsync();

        if (company is not null)
        {
            if (!string.IsNullOrWhiteSpace(profile.Name)) company.Name = profile.Name.Trim();
            if (profile.Phone is not null) company.Phone = profile.Phone.Trim();
            if (profile.Address is not null) company.Address = profile.Address.Trim();
            await _db.SaveChangesAsync();
        }

        return await Get();
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// شناسهٔ شرکت فعال — از زمینهٔ درخواست (در صورت وجود میان‌افزار شرکت) یا هدر X-Company-Id.
    /// </summary>
    private int? ActiveCompanyId()
    {
        if (HttpContext.Items["ActiveCompanyId"] is int resolved && resolved > 0) return resolved;
        return int.TryParse(Request.Headers["X-Company-Id"].FirstOrDefault(), out var id) && id > 0 ? id : null;
    }
}
