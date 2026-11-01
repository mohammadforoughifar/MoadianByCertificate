using System.Security.Claims;
using MoudianMiddleware.DataLayer.Abstractions;

namespace MoudianMiddleware.Api.Infrastructure;

public static class ApiClaims
{
    public const string TenantId = "tenant_id";
    public const string ApiClientId = "api_client_id";
    public const string ApiScope = "api_scope";
    public const string RequestsPerMinute = "requests_per_minute";
}

public sealed class HttpTenantProvider(IHttpContextAccessor accessor) : ITenantProvider
{
    public int? TenantId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ApiClaims.TenantId);
            return int.TryParse(value, out var tenantId) && tenantId > 0 ? tenantId : null;
        }
    }
}

public sealed class HttpCurrentUserProvider(IHttpContextAccessor accessor) : ICurrentUserProvider
{
    public string? UserName => accessor.HttpContext?.User.Identity?.Name;

    public int? UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(value, out var userId) && userId > 0 ? userId : null;
        }
    }
}
