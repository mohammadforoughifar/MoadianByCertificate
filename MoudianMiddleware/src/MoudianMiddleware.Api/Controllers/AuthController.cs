using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MoudianMiddleware.Api.Services;
using MoudianMiddleware.Contracts.Common;

namespace MoudianMiddleware.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    [AllowAnonymous, EnableRateLimiting("AuthByIp"), HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResult>>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await auth.LoginAsync(request, RemoteIp, UserAgent, ct).ConfigureAwait(false);
        return Ok(new ApiResponse<LoginResult> { IsSuccess = true, Code = ApiResponseCodes.Ok, Data = result, TraceId = HttpContext.TraceIdentifier });
    }

    [AllowAnonymous, EnableRateLimiting("AuthByIp"), HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<TokenResult>>> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await auth.RefreshAsync(request, RemoteIp, UserAgent, ct).ConfigureAwait(false);
        return Ok(new ApiResponse<TokenResult> { IsSuccess = true, Code = ApiResponseCodes.Ok, Data = result, TraceId = HttpContext.TraceIdentifier });
    }

    [Authorize, HttpPost("select-tenant/{tenantId:int}")]
    public async Task<ActionResult<ApiResponse<TokenResult>>> SelectTenantAsync(int tenantId, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var result = await auth.SelectTenantAsync(userId, tenantId, RemoteIp, UserAgent, ct).ConfigureAwait(false);
        return Ok(new ApiResponse<TokenResult> { IsSuccess = true, Code = ApiResponseCodes.Ok, Data = result, TraceId = HttpContext.TraceIdentifier });
    }

    private string? RemoteIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? UserAgent => Request.Headers.UserAgent.ToString() is { Length: > 400 } value ? value[..400] : Request.Headers.UserAgent.ToString();
}
