using System.Security.Claims;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Api.Infrastructure;
using MoudianMiddleware.Api.Services;
using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.Constants;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Customers;
using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.Contracts.ViewModels.Moadian;
using MoudianMiddleware.Contracts.ViewModels.Products;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.Api.Controllers;

[Authorize, Route("api/invoices")]
public sealed class InvoicesController(IInvoiceService invoices, IInvoicePreparationService preparation, IMoadianSubmissionService submission) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Permissions.InvoiceView)]
    public async Task<ActionResult<ApiResponse<PagedResult<InvoiceViewModel>>>> List([FromQuery] InvoiceListQuery query, CancellationToken ct) => Ok(Success(await invoices.ListAsync(query, ct).ConfigureAwait(false)));
    [HttpGet("{id:int}"), Authorize(Policy = Permissions.InvoiceView)]
    public async Task<ActionResult<ApiResponse<InvoiceViewModel>>> Get(int id, CancellationToken ct) => Ok(Success(await invoices.GetAsync(id, ct).ConfigureAwait(false)));
    [HttpPost, Authorize(Policy = Permissions.InvoiceEdit)]
    public async Task<ActionResult<ApiResponse<InvoiceViewModel>>> Create(InvoiceWriteRequest request, CancellationToken ct)
    { var item = await invoices.CreateAsync(request, ct).ConfigureAwait(false); return item.IsIdempotentReplay ? Ok(Success(item, ApiResponseCodes.IdempotentReplay)) : StatusCode(201, Success(item, ApiResponseCodes.Created)); }
    [HttpPut("{id:int}/draft"), Authorize(Policy = Permissions.InvoiceEdit)]
    public async Task<ActionResult<ApiResponse<InvoiceViewModel>>> UpdateDraft(int id, InvoiceWriteRequest request, CancellationToken ct) => Ok(Success(await invoices.UpdateDraftAsync(id, request, ct).ConfigureAwait(false)));
    [HttpPost("{id:int}/send"), Authorize(Policy = Permissions.InvoiceSend)]
    public async Task<ActionResult<ApiResponse<MoadianSendReceipt>>> Send(int id, CancellationToken ct) { await preparation.PrepareAsync(id, ct).ConfigureAwait(false); return Ok(Success(await submission.SubmitAsync(id, ct).ConfigureAwait(false))); }
    [HttpPost("{id:int}/inquiry"), Authorize(Policy = Permissions.InquiryExecute)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MoadianInquiryResult>>>> Inquiry(int id, CancellationToken ct) => Ok(Success(await submission.InquiryAsync(id, ct).ConfigureAwait(false)));
    [HttpPost("{id:int}/inquiry-by-tax-id"), Authorize(Policy = Permissions.InquiryExecute)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MoadianInvoiceStatusResult>>>> InquiryByTaxId(int id, CancellationToken ct) => Ok(Success(await submission.InquiryByTaxIdAsync(id, ct).ConfigureAwait(false)));
    [HttpPost("{id:int}/resend"), Authorize(Policy = Permissions.InvoiceRetry)]
    public async Task<ActionResult<ApiResponse<MoadianSendReceipt>>> Resend(int id, CancellationToken ct) => Ok(Success(await submission.SubmitAsync(id, ct).ConfigureAwait(false)));
}

[Authorize, Route("api/customers")]
public sealed class CustomersController(ICustomerService customers) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Permissions.CustomerView)] public async Task<ActionResult<ApiResponse<PagedResult<CustomerViewModel>>>> List([FromQuery] CustomerListQuery query, CancellationToken ct) => Ok(Success(await customers.ListAsync(query, ct).ConfigureAwait(false)));
    [HttpGet("{id:int}"), Authorize(Policy = Permissions.CustomerView)] public async Task<ActionResult<ApiResponse<CustomerViewModel>>> Get(int id, CancellationToken ct) => Ok(Success(await customers.GetAsync(id, ct).ConfigureAwait(false)));
    [HttpPost, Authorize(Policy = Permissions.CustomerEdit)] public async Task<ActionResult<ApiResponse<CustomerViewModel>>> Create(CustomerWriteRequest request, CancellationToken ct) => StatusCode(201, Success(await customers.CreateAsync(request, ct).ConfigureAwait(false), ApiResponseCodes.Created));
    [HttpPut("{id:int}"), Authorize(Policy = Permissions.CustomerEdit)] public async Task<ActionResult<ApiResponse<CustomerViewModel>>> Update(int id, CustomerWriteRequest request, CancellationToken ct) => Ok(Success(await customers.UpdateAsync(id, request, ct).ConfigureAwait(false)));
    [HttpDelete("{id:int}"), Authorize(Policy = Permissions.CustomerDelete)] public async Task<ActionResult<ApiResponse<object>>> Delete(int id, CancellationToken ct) { await customers.DeleteAsync(id, ct).ConfigureAwait(false); return Ok(Success<object>(new { id })); }
}

[Authorize, Route("api/products")]
public sealed class ProductsController(IProductService products) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Permissions.ProductView)] public async Task<ActionResult<ApiResponse<PagedResult<ProductViewModel>>>> List([FromQuery] ProductListQuery query, CancellationToken ct) => Ok(Success(await products.ListAsync(query, ct).ConfigureAwait(false)));
    [HttpGet("{id:int}"), Authorize(Policy = Permissions.ProductView)] public async Task<ActionResult<ApiResponse<ProductViewModel>>> Get(int id, CancellationToken ct) => Ok(Success(await products.GetAsync(id, ct).ConfigureAwait(false)));
    [HttpPost, Authorize(Policy = Permissions.ProductEdit)] public async Task<ActionResult<ApiResponse<ProductViewModel>>> Create(ProductWriteRequest request, CancellationToken ct) => StatusCode(201, Success(await products.CreateAsync(request, ct).ConfigureAwait(false), ApiResponseCodes.Created));
    [HttpPut("{id:int}"), Authorize(Policy = Permissions.ProductEdit)] public async Task<ActionResult<ApiResponse<ProductViewModel>>> Update(int id, ProductWriteRequest request, CancellationToken ct) => Ok(Success(await products.UpdateAsync(id, request, ct).ConfigureAwait(false)));
    [HttpDelete("{id:int}"), Authorize(Policy = Permissions.ProductDelete)] public async Task<ActionResult<ApiResponse<object>>> Delete(int id, CancellationToken ct) { await products.DeleteAsync(id, ct).ConfigureAwait(false); return Ok(Success<object>(new { id })); }
}

[Authorize, Route("api/tenants/current/moadian-settings")]
public sealed class MoadianSettingsController(IMoadianSettingService settings, IMoadianCredentialService credentials) : ApiControllerBase
{
    [HttpGet, Authorize(Policy = Permissions.MoadianView)] public async Task<ActionResult<ApiResponse<MoadianSettingViewModel?>>> Get(CancellationToken ct) => Ok(Success(await settings.GetAsync(ct).ConfigureAwait(false)));
    [HttpPut, Authorize(Policy = Permissions.MoadianEdit)] public async Task<ActionResult<ApiResponse<MoadianSettingViewModel>>> Save(MoadianSettingWriteRequest request, CancellationToken ct) => Ok(Success(await settings.SaveAsync(request, ct).ConfigureAwait(false)));
    [HttpGet("credential"), Authorize(Policy = Permissions.MoadianView)] public async Task<ActionResult<ApiResponse<MoadianCredentialViewModel?>>> GetCredential(CancellationToken ct) => Ok(Success(await credentials.GetAsync(ct).ConfigureAwait(false)));
    [HttpPut("credential"), Authorize(Policy = Permissions.MoadianEdit)] public async Task<ActionResult<ApiResponse<MoadianCredentialViewModel>>> SaveCredential(SaveMoadianCredentialRequest request, CancellationToken ct) => Ok(Success(await credentials.SaveAsync(request, ct).ConfigureAwait(false)));
}

[Authorize(Policy = Permissions.ReportView), Route("api/reports")]
public sealed class ReportsController(MoudianDbContext db, ITenantProvider tenant) : ApiControllerBase
{
    [HttpGet("invoice-summary")]
    public async Task<ActionResult<ApiResponse<Dictionary<string, int>>>> Summary(CancellationToken ct)
    {
        var counts = await db.Invoices.AsNoTracking().GroupBy(x => x.Status).Select(group => new { Status = group.Key, Count = group.Count() }).ToListAsync(ct).ConfigureAwait(false);
        return Ok(Success(counts.ToDictionary(item => item.Status.ToString(), item => item.Count)));
    }
    [HttpGet("invoices.xlsx"), Authorize(Policy = Permissions.ReportExport)]
    public async Task<IActionResult> Export(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (from > to) throw new FluentValidation.ValidationException("Invalid report date range.");
        var rows = await db.Invoices.AsNoTracking().Where(x => x.IssueDate >= new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) && x.IssueDate < new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            .OrderBy(x => x.IssueDate).Select(x => new { x.InvoiceNumber, x.BuyerName, x.Status, x.TotalAmount, x.IssueDate }).ToListAsync(ct).ConfigureAwait(false);
        using var workbook = new XLWorkbook(); var sheet = workbook.Worksheets.Add("Invoices");
        sheet.Cell(1,1).Value="شماره صورتحساب";sheet.Cell(1,2).Value="خریدار";sheet.Cell(1,3).Value="وضعیت";sheet.Cell(1,4).Value="جمع ریال";sheet.Cell(1,5).Value="تاریخ";
        for(var index=0;index<rows.Count;index++){var row=index+2;var item=rows[index];sheet.Cell(row,1).Value=item.InvoiceNumber;sheet.Cell(row,2).Value=item.BuyerName;sheet.Cell(row,3).Value=item.Status.ToString();sheet.Cell(row,4).Value=item.TotalAmount;sheet.Cell(row,5).Value=item.IssueDate.ToString("yyyy-MM-dd");}
        using var stream=new MemoryStream();workbook.SaveAs(stream);return File(stream.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","invoices.xlsx");
    }
}

[Authorize, Route("api/external/invoices")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
[EnableRateLimiting("ApiClient")]
public sealed class ExternalInvoicesController(IInvoiceService invoices) : ApiControllerBase
{
    [HttpPost("{externalId}"), Authorize(Policy="ApiScope.InvoiceWrite")]
    public async Task<ActionResult<ApiResponse<InvoiceViewModel>>> Create(string externalId, InvoiceWriteRequest request, CancellationToken ct)
    {
        var value = await invoices.CreateAsync(new InvoiceWriteRequest { InvoiceNumber=request.InvoiceNumber,ExternalId=externalId,Type=request.Type,PatternCode=request.PatternCode,SubjectCode=request.SubjectCode,SettlementMethod=request.SettlementMethod,IssueDate=request.IssueDate,CustomerId=request.CustomerId,BuyerName=request.BuyerName,BuyerType=request.BuyerType,BuyerNationalId=request.BuyerNationalId,BuyerEconomicCode=request.BuyerEconomicCode,BuyerPostalCode=request.BuyerPostalCode,BuyerAddress=request.BuyerAddress,BuyerBranchCode=request.BuyerBranchCode,ReferenceTaxId=request.ReferenceTaxId,Items=request.Items,Payments=request.Payments }, ct).ConfigureAwait(false);
        return value.IsIdempotentReplay ? Ok(Success(value, ApiResponseCodes.IdempotentReplay)) : StatusCode(201, Success(value, ApiResponseCodes.Created));
    }
}
