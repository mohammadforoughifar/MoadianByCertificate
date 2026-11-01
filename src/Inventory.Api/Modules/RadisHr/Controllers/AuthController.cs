using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Api.Services;
using RadisHr.Shared.Contracts;
using RadisHr.Shared.Models;
using Paging = Inventory.Api.Services.Paging;

namespace RadisHr.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TokenService _tokens;

    public AuthController(AppDbContext db, TokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    /// <summary>فهرست نقش‌ها برای صفحهٔ ورود (بدون افشای رمز)</summary>
    [HttpGet("roles")]
    public async Task<ActionResult<List<UserAccountInfo>>> Roles([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        var users = await _db.Users.Where(u => u.IsActive).OrderBy(x => x.Id).ToPageListAsync(pagination);
        return Ok(pagination.Result(users.Select(u => new UserAccountInfo(
            u.UserKey, u.DisplayName, u.RoleTitle, u.MustChangePassword, u.PasswordChangedAt, u.LastLoginAt))
            .ToList()));
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserKey == request.UserKey && u.IsActive);
        if (user == null)
            return Unauthorized(new ApiMessage(false, "کاربر یافت نشد."));

        if (!PasswordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            user.FailedAttempts++;
            await _db.SaveChangesAsync();
            return Unauthorized(new ApiMessage(false, "رمز عبور نادرست است."));
        }

        user.FailedAttempts = 0;
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var meta = RolePages.Map.TryGetValue(user.UserKey, out var m)
            ? m : (user.DisplayName, user.RoleTitle, new[] { "notices" }, "notices");

        var ageDays = user.PasswordChangedAt.HasValue
            ? (int)(DateTime.UtcNow - user.PasswordChangedAt.Value).TotalDays : 0;

        // سیاست نسخهٔ اصلی: رمز پس از ۳۰ روز باید تعویض شود
        var mustChange = user.MustChangePassword || ageDays >= AppUser.PasswordMaxAgeDays;

        var (token, expires) = _tokens.Create(user);
        return new LoginResponse(token, user.UserKey, meta.Item1, meta.Item2,
            meta.Item3, meta.Item4, mustChange, ageDays, expires);
    }

    [HttpPost("change-password")]
    public async Task<ActionResult<ApiMessage>> ChangePassword(ChangePasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserKey == request.UserKey);
        if (user == null) return NotFound(new ApiMessage(false, "کاربر یافت نشد."));

        if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash, user.PasswordSalt))
            return BadRequest(new ApiMessage(false, "رمز فعلی نادرست است."));

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return BadRequest(new ApiMessage(false, "رمز جدید باید حداقل ۶ نویسه باشد."));

        var (hash, salt) = PasswordHasher.Hash(request.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.MustChangePassword = false;
        user.PasswordChangedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new ApiMessage(true, "رمز عبور با موفقیت تغییر کرد.");
    }

    /// <summary>بازنشانی رمز توسط مدیر اداری</summary>
    [Authorize(Roles = "hr,ceo")]
    [HttpPost("reset-password")]
    public async Task<ActionResult<ApiMessage>> ResetPassword(ResetPasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserKey == request.UserKey);
        if (user == null) return NotFound(new ApiMessage(false, "کاربر یافت نشد."));

        var (hash, salt) = PasswordHasher.Hash("Radis@1405");
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.MustChangePassword = true;
        user.PasswordChangedAt = null;
        await _db.SaveChangesAsync();

        return new ApiMessage(true, "رمز عبور بازنشانی شد؛ کاربر در ورود بعدی باید رمز جدید تعریف کند.");
    }
}
