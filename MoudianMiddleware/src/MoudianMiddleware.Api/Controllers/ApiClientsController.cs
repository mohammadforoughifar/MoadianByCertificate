using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using MoudianMiddleware.Api.Infrastructure;
using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.Constants;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.Api.Controllers;

public sealed record ApiClientCreateRequest(string Name, ApiScope Scopes, int RequestsPerMinute = 60, DateTimeOffset? ExpiresAt = null);
public sealed record ApiClientView(int Id, string Name, string KeyPrefix, ApiScope Scopes, int RequestsPerMinute, bool IsActive, DateTimeOffset? ExpiresAt);
public sealed record ApiClientCreated(ApiClientView Client, string ApiKey);

[ApiController, Authorize(Policy = Permissions.ApiClientManage), Route("api/api-clients")]
public sealed class ApiClientsController(MoudianDbContext db, ITenantProvider tenantProvider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ApiClientView>>>> List(CancellationToken ct)
    {
        var tenant = RequireTenant();
        var clients = await db.ApiClients.AsNoTracking().Where(x => x.TenantId == tenant).OrderBy(x => x.Name)
            .Select(x => new ApiClientView(x.Id, x.Name, x.KeyPrefix, x.Scopes, x.RequestsPerMinute, x.IsActive, x.ExpiresAt)).ToListAsync(ct).ConfigureAwait(false);
        return Ok(Success<IReadOnlyList<ApiClientView>>(clients));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ApiClientCreated>>> Create(ApiClientCreateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || request.Scopes == ApiScope.None || request.RequestsPerMinute is < 1 or > 10000 || request.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            throw new FluentValidation.ValidationException("API key name, scopes, and rate limit are invalid.");
        var allowed = ApiScope.InvoiceWrite | ApiScope.InvoiceRead | ApiScope.BaseInfoRead | ApiScope.InquiryRead | ApiScope.PaymentWrite;
        if ((request.Scopes & ~allowed) != 0) throw new FluentValidation.ValidationException("Unknown API scope.");
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var entity = new ApiClient { TenantId = RequireTenant(), Name = request.Name.Trim(), KeyPrefix = key[..8],
            KeyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(key))), Scopes = request.Scopes,
            RequestsPerMinute = request.RequestsPerMinute, ExpiresAt = request.ExpiresAt };
        db.ApiClients.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var value = new ApiClientCreated(new ApiClientView(entity.Id, entity.Name, entity.KeyPrefix, entity.Scopes, entity.RequestsPerMinute, entity.IsActive, entity.ExpiresAt), key);
        return StatusCode(201, Success(value, ApiResponseCodes.Created));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse<object>>> Revoke(int id, CancellationToken ct)
    {
        var client = await db.ApiClients.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == RequireTenant(), ct).ConfigureAwait(false)
            ?? throw new MoudianMiddleware.CoreLayer.Common.Exceptions.EntityNotFoundException(nameof(ApiClient), id);
        client.IsActive = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(Success<object>(new { id }));
    }

    private int RequireTenant() => tenantProvider.TenantId is > 0 ? tenantProvider.TenantId.Value : throw new UnauthorizedAccessException();
    private ApiResponse<T> Success<T>(T data, string code = ApiResponseCodes.Ok) => new() { IsSuccess = true, Code = code, Data = data, TraceId = HttpContext.TraceIdentifier };
}
