using System.Security.Claims;
using Inventory.Api.Services.Core;
using Inventory.Api.Infrastructure;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.Core;

/// <summary>پروفایل خودِ کاربر جاری؛ هیچ شناسهٔ کاربری از کلاینت برای خواندن/تغییر داده پذیرفته نمی‌شود.</summary>
[ApiController]
[Authorize]
[RequirePermission("PersonalSettings", "Read")]
[Route("api/profile")]
public sealed class UserProfileController : ControllerBase
{
    private readonly IUserProfileService _profile;

    public UserProfileController(IUserProfileService profile) => _profile = profile;

    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : 0;

    [HttpGet]
    public async Task<ActionResult<UserProfileDto>> Get(CancellationToken cancellationToken)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        return Ok(await _profile.GetProfileAsync(CurrentUserId, cancellationToken));
    }

    [HttpPost("mobile/otp")]
    public async Task<IActionResult> RequestMobileOtp(
        [FromBody] UserContactOtpRequestDto request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        await _profile.RequestMobileOtpAsync(CurrentUserId, request.Destination, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpPost("mobile/verify")]
    public async Task<IActionResult> VerifyMobileOtp(
        [FromBody] UserContactOtpVerifyDto request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        await _profile.VerifyMobileOtpAsync(CurrentUserId, request.Code, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpPost("email/otp")]
    public async Task<IActionResult> RequestEmailOtp(
        [FromBody] UserContactOtpRequestDto request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        await _profile.RequestEmailOtpAsync(CurrentUserId, request.Destination, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpPost("email/verify")]
    public async Task<IActionResult> VerifyEmailOtp(
        [FromBody] UserContactOtpVerifyDto request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        await _profile.VerifyEmailOtpAsync(CurrentUserId, request.Code, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpDelete("email")]
    public async Task<IActionResult> ClearEmail(CancellationToken cancellationToken)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        await _profile.ClearEmailAsync(CurrentUserId, cancellationToken);
        return Ok(new { ok = true });
    }
}
