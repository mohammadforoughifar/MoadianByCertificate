using Inventory.Api.Data;
using Inventory.Api.Services.Moadian;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.Moadian;

/// <summary>
/// کنترلر یکپارچه سامانه مودیان مبتنی بر گواهی امضای الکترونیکی (SDK 2.0.29):
///   • مدیریت پرونده مالیاتی و گاوصندوق گواهی امضا (PKCS#8 + X.509 / PFX / توکن PKCS#11)
///   • تست اتصال، استعلام آنلاین کد اقتصادی و وضعیت ماده ۶
///   • مدیریت اطلاعات پایه (خریداران، کالاها/خدمات SSTID، واحدهای سنجش) و همگام‌سازی با ERP
///   • صدور صورتحساب (اصلی، اصلاحی، ابطالی، برگشت از فروش) و تبدیل از فاکتور فروش سیستم
///   • ارسال امضاشده JWS+JWE، استعلام وضعیت درگاه، استعلام به‌روز کارپوشه و ثبت پرداخت (RegisterPaymentRequest)
/// </summary>
[Route("api/moadian")]
public sealed class MoadianCertController : RbacControllerBase
{
    private const string Module = "Moadian";
    private readonly IMoadianCertService _svc;

    public MoadianCertController(AppDbContext db, IMoadianCertService svc) : base(db) => _svc = svc;

    // ==================== داشبورد ====================

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        if (await ForbiddenUnlessAsync(Module, "View") is { } forbid) return forbid;
        return Ok(await _svc.GetDashboardAsync());
    }

    // ==================== پروفایل مودی، گواهی امضا و تشخیص ====================

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        if (await ForbiddenUnlessAnyAsync(Module, "View", "Certificate", "InvoiceCreate") is { } forbid) return forbid;
        return Ok(await _svc.GetProfileAsync());
    }

    [HttpPost("profile")]
    public async Task<IActionResult> SaveProfile([FromBody] MdnSaveProfileRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "Certificate") is { } forbid) return forbid;
        return Ok(await _svc.SaveProfileAsync(req, MyUsername));
    }

    [HttpPost("profile/certificate")]
    public async Task<IActionResult> UploadCertificate([FromBody] MdnUploadCertificateRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "Certificate") is { } forbid) return forbid;
        return Ok(await _svc.UploadCertificateAsync(req, MyUsername));
    }

    [HttpDelete("profile/certificate")]
    public async Task<IActionResult> ClearCertificate()
    {
        if (await ForbiddenUnlessAsync(Module, "Certificate") is { } forbid) return forbid;
        return Ok(await _svc.ClearCertificateAsync(MyUsername));
    }

    [HttpPost("pkcs11/discover")]
    public async Task<IActionResult> DiscoverPkcs11([FromBody] MdnPkcs11DiscoverRequest? req)
    {
        if (await ForbiddenUnlessAnyAsync(Module, "Certificate", "Diagnose") is { } forbid) return forbid;
        return Ok(await _svc.DiscoverPkcs11Async(req ?? new MdnPkcs11DiscoverRequest()));
    }

    [HttpGet("windows-store/certificates")]
    public async Task<IActionResult> DiscoverWindowsStore()
    {
        if (await ForbiddenUnlessAnyAsync(Module, "Certificate", "Diagnose") is { } forbid) return forbid;
        return Ok(await _svc.DiscoverWindowsStoreAsync());
    }

    [HttpPost("diagnostics/connection")]
    public async Task<IActionResult> TestConnection()
    {
        if (await ForbiddenUnlessAnyAsync(Module, "Certificate", "Diagnose") is { } forbid) return forbid;
        return Ok(await _svc.TestConnectionAsync(MyUsername));
    }

    [HttpGet("diagnostics/taxpayer/{economicCode}")]
    public async Task<IActionResult> LookupTaxpayer(string economicCode)
    {
        if (await ForbiddenUnlessAnyAsync(Module, "View", "MasterData", "InvoiceCreate", "Diagnose") is { } forbid) return forbid;
        return Ok(await _svc.LookupTaxpayerAsync(economicCode));
    }

    [HttpPost("diagnostics/article6")]
    public async Task<IActionResult> CheckArticle6([FromBody] MdnArticle6CheckRequest req)
    {
        if (await ForbiddenUnlessAnyAsync(Module, "FolderStatus", "Diagnose", "Certificate") is { } forbid) return forbid;
        return Ok(await _svc.CheckArticle6StatusAsync(req));
    }

    // ==================== اطلاعات پایه و همگام‌سازی ERP ====================

    [HttpGet("invoice-lookups")]
    public async Task<IActionResult> GetInvoiceFormLookups()
    {
        if (await ForbiddenUnlessAnyAsync(Module, "View", "MasterData", "InvoiceCreate", "InvoiceUpdate") is { } forbid) return forbid;
        return Ok(await _svc.GetInvoiceFormLookupsAsync());
    }

    [HttpGet("buyers")]
    public async Task<IActionResult> GetBuyers([FromQuery] string? search = null)
    {
        if (await ForbiddenUnlessAnyAsync(Module, "View", "MasterData", "InvoiceCreate", "InvoiceUpdate") is { } forbid) return forbid;
        return Ok(await _svc.GetBuyersAsync(search));
    }

    [HttpPost("buyers")]
    public async Task<IActionResult> SaveBuyer([FromBody] MdnSaveBuyerRequest req)
    {
        if (await ForbiddenUnlessAnyAsync(Module, "MasterData", "InvoiceCreate") is { } forbid) return forbid;
        return Ok(await _svc.SaveBuyerAsync(req));
    }

    [HttpDelete("buyers/{id:int}")]
    public async Task<IActionResult> DeleteBuyer(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "MasterData") is { } forbid) return forbid;
        await _svc.DeleteBuyerAsync(id);
        return Ok(new { ok = true });
    }

    [HttpPost("buyers/sync-erp")]
    public async Task<IActionResult> SyncBuyersFromParties()
    {
        if (await ForbiddenUnlessAsync(Module, "MasterData") is { } forbid) return forbid;
        return Ok(await _svc.SyncBuyersFromPartiesAsync());
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog([FromQuery] string? search = null)
    {
        if (await ForbiddenUnlessAnyAsync(Module, "View", "MasterData", "InvoiceCreate", "InvoiceUpdate") is { } forbid) return forbid;
        return Ok(await _svc.GetCatalogItemsAsync(search));
    }

    [HttpPost("catalog")]
    public async Task<IActionResult> SaveCatalog([FromBody] MdnSaveCatalogItemRequest req)
    {
        if (await ForbiddenUnlessAnyAsync(Module, "MasterData", "InvoiceCreate") is { } forbid) return forbid;
        return Ok(await _svc.SaveCatalogItemAsync(req));
    }

    [HttpDelete("catalog/{id:int}")]
    public async Task<IActionResult> DeleteCatalog(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "MasterData") is { } forbid) return forbid;
        await _svc.DeleteCatalogItemAsync(id);
        return Ok(new { ok = true });
    }

    [HttpPost("catalog/sync-erp")]
    public async Task<IActionResult> SyncCatalogFromProducts()
    {
        if (await ForbiddenUnlessAsync(Module, "MasterData") is { } forbid) return forbid;
        return Ok(await _svc.SyncCatalogFromProductsAsync());
    }

    [HttpGet("units")]
    public async Task<IActionResult> GetUnits()
    {
        if (await ForbiddenUnlessAnyAsync(Module, "View", "MasterData", "InvoiceCreate", "InvoiceUpdate") is { } forbid) return forbid;
        return Ok(await _svc.GetMeasureUnitsAsync());
    }

    [HttpPost("units")]
    public async Task<IActionResult> SaveUnit([FromBody] MdnSaveMeasureUnitRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "MasterData") is { } forbid) return forbid;
        return Ok(await _svc.SaveMeasureUnitAsync(req));
    }

    // ==================== صورتحساب‌ها، پیش‌نمایش و تبدیل از ERP ====================

    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices(
        [FromQuery] string? search = null,
        [FromQuery] MdnSubmissionStatus? submissionStatus = null,
        [FromQuery] MdnFolderStatus? folderStatus = null,
        [FromQuery] MdnPaymentStatus? paymentStatus = null,
        [FromQuery] MdnInvoiceSubject? subject = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        if (await ForbiddenUnlessAsync(Module, "View") is { } forbid) return forbid;
        return Ok(await _svc.GetInvoicesAsync(new MdnInvoiceFilterRequest
        {
            Search = search,
            SubmissionStatus = submissionStatus,
            FolderStatus = folderStatus,
            PaymentStatus = paymentStatus,
            Subject = subject,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            PageSize = pageSize
        }));
    }

    [HttpGet("invoices/{id:int}")]
    public async Task<IActionResult> GetInvoiceDetail(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "View") is { } forbid) return forbid;
        return Ok(await _svc.GetInvoiceDetailAsync(id));
    }

    [HttpPost("invoices")]
    public async Task<IActionResult> SaveInvoice([FromBody] MdnSaveInvoiceRequest req)
    {
        var action = req.Id == 0 ? "InvoiceCreate" : "InvoiceUpdate";
        if (await ForbiddenUnlessAsync(Module, action) is { } forbid) return forbid;
        return Ok(await _svc.SaveInvoiceAsync(req, MyUserId, MyUsername));
    }

    [HttpDelete("invoices/{id:int}")]
    public async Task<IActionResult> DeleteInvoice(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "InvoiceDelete") is { } forbid) return forbid;
        await _svc.DeleteInvoiceAsync(id);
        return Ok(new { ok = true });
    }

    [HttpGet("invoices/{id:int}/preview-json")]
    public async Task<IActionResult> PreviewJson(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "View") is { } forbid) return forbid;
        return Ok(await _svc.PreviewInvoiceJsonAsync(id));
    }

    [HttpPost("invoices/{id:int}/derivative")]
    public async Task<IActionResult> CreateDerivative(int id, [FromBody] MdnCreateDerivativeRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "InvoiceUpdate") is { } forbid) return forbid;
        return Ok(await _svc.CreateDerivativeInvoiceAsync(id, req, MyUserId, MyUsername));
    }

    [HttpPost("invoices/{id:int}/cancel-and-inquire")]
    public async Task<IActionResult> CancelAndInquire(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Module, "InvoiceSend", "InvoiceUpdate", "InvoiceCreate") is { } forbid) return forbid;
        return Ok(await _svc.CancelInvoiceAndInquireAsync(id, MyUserId, MyUsername));
    }

    [HttpGet("erp-invoices")]
    public async Task<IActionResult> GetErpInvoices()
    {
        if (await ForbiddenUnlessAsync(Module, "InvoiceCreate") is { } forbid) return forbid;
        return Ok(await _svc.GetReceivableErpInvoicesAsync());
    }

    [HttpPost("erp-invoices/{facInvoiceId:int}/import")]
    public async Task<IActionResult> ImportErpInvoice(int facInvoiceId)
    {
        if (await ForbiddenUnlessAsync(Module, "InvoiceCreate") is { } forbid) return forbid;
        return Ok(await _svc.ImportFromErpInvoiceAsync(facInvoiceId, MyUserId, MyUsername));
    }

    // ==================== ارسال، استعلام وضعیت، استعلام کارپوشه و ثبت پرداخت ====================

    [HttpPost("invoices/send")]
    public async Task<IActionResult> SendInvoices([FromBody] MdnBulkIdsRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "InvoiceSend") is { } forbid) return forbid;
        return Ok(await _svc.SendInvoicesAsync(req.InvoiceIds, MyUsername));
    }

    [HttpPost("invoices/inquiry-submission")]
    public async Task<IActionResult> InquirySubmissions([FromBody] MdnBulkIdsRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "InvoiceInquiry") is { } forbid) return forbid;
        return Ok(await _svc.InquirySubmissionsAsync(req.InvoiceIds, MyUsername));
    }

    [HttpPost("invoices/inquiry-folder")]
    public async Task<IActionResult> InquiryFolder([FromBody] MdnBulkIdsRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "FolderStatus") is { } forbid) return forbid;
        return Ok(await _svc.InquiryFolderStatusesAsync(req.InvoiceIds, MyUsername));
    }

    [HttpPost("invoices/refresh-all")]
    public async Task<IActionResult> RefreshAll([FromBody] MdnBulkIdsRequest req)
    {
        if (await ForbiddenUnlessAnyAsync(Module, "InvoiceInquiry", "FolderStatus") is { } forbid) return forbid;
        return Ok(await _svc.RefreshAllStatusesAsync(req.InvoiceIds, MyUsername));
    }

    [HttpPost("invoices/inquiry-time-range")]
    public async Task<IActionResult> InquiryTimeRange([FromBody] MdnTimeRangeInquiryRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "InvoiceInquiry") is { } forbid) return forbid;
        return Ok(await _svc.InquiryByTimeRangeAsync(req, MyUsername));
    }

    [HttpPost("invoices/{id:int}/payments")]
    public async Task<IActionResult> RegisterPayment(int id, [FromBody] MdnRegisterPaymentRequest req)
    {
        if (await ForbiddenUnlessAsync(Module, "PaymentRegister") is { } forbid) return forbid;
        return Ok(await _svc.RegisterPaymentAsync(id, req, MyUsername));
    }

    [HttpPost("invoices/{id:int}/payments/{paymentId:int}/retry")]
    public async Task<IActionResult> RetryPayment(int id, int paymentId)
    {
        if (await ForbiddenUnlessAsync(Module, "PaymentRegister") is { } forbid) return forbid;
        return Ok(await _svc.RetryPaymentSyncAsync(id, paymentId, MyUsername));
    }

    [HttpDelete("invoices/{id:int}/payments/{paymentId:int}")]
    public async Task<IActionResult> DeletePayment(int id, int paymentId)
    {
        if (await ForbiddenUnlessAsync(Module, "PaymentRegister") is { } forbid) return forbid;
        await _svc.DeletePaymentAsync(id, paymentId);
        return Ok(new { ok = true });
    }
}
