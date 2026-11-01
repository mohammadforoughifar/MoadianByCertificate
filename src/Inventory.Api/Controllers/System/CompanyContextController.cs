using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/company-context")]
public sealed class CompanyContextController : ControllerBase
{
    private readonly AppDbContext _db;
    public CompanyContextController(AppDbContext db) => _db = db;

    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> Get([FromHeader(Name = "X-Company-Id")] int? requested, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var access = _db.UserCompanyAccesses.AsNoTracking().Where(x => x.UserId == UserId && x.Company.IsActive);
        if (!await access.AnyAsync())
        {
            var username = User.FindFirstValue(ClaimTypes.Name) ?? "";
            var legacyCompany = await _db.SystemUsers.AsNoTracking().Where(u => u.Username == username && u.CompanyId != null).Select(u => u.CompanyId!.Value).FirstOrDefaultAsync();
            if (legacyCompany > 0)
                access = _db.SystemCompanies.AsNoTracking().Where(c => c.Id == legacyCompany && c.IsActive).Select(c => new UserCompanyAccess { UserId = UserId, CompanyId = c.Id, Company = c });
        }
        var pagination = new Paging.Request(skip, take);
        if (IsAdmin && !await access.AnyAsync())
        {
            var allQuery = _db.SystemCompanies.AsNoTracking().Where(c => c.IsActive)
                .OrderBy(c => c.Name).ThenBy(c => c.Id);
            // مدیر هم باید در اولین ورود یک زمینهٔ معتبر بگیرد؛ null قبلی باعث انتخاب‌گر خالی می‌شد.
            var adminActiveCompanyId = requested is > 0 && await allQuery.AnyAsync(c => c.Id == requested)
                ? requested
                : await allQuery.Select(c => (int?)c.Id).FirstOrDefaultAsync();
            var allCompanies = await allQuery.ToPageListAsync(pagination);
            if (pagination.IsPaged)
                return Ok(new { activeCompanyId = adminActiveCompanyId, total = pagination.Total, companies = allCompanies });
            return Ok(new { activeCompanyId = adminActiveCompanyId, companies = allCompanies });
        }

        // IsDefault همان شرکت فعال ثبت‌شده برای کاربر است؛ فقط در نبود آن، ترتیب نام شرکت fallback است.
        var orderedAccess = access.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Company.Name).ThenBy(x => x.CompanyId);
        var active = requested is > 0 && await access.AnyAsync(x => x.CompanyId == requested)
            ? requested
            : await orderedAccess.Select(x => (int?)x.CompanyId).FirstOrDefaultAsync();
        var query = orderedAccess.Select(x => x.Company);
        var companies = await query.ToPageListAsync(pagination);
        if (pagination.IsPaged)
            return Ok(new { activeCompanyId = active, total = pagination.Total, companies });
        return Ok(new { activeCompanyId = active, companies });
    }

    [HttpPut("users/{userId:int}")]
    public async Task<IActionResult> SetUserCompanies(int userId, [FromBody] CompanyAssignmentDto dto)
    {
        if (!IsAdmin) return Forbid();
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return NotFound(new { message = "کاربر پیدا نشد." });
        var ids = dto.CompanyIds.Distinct().Where(x => x > 0).ToList();
        var valid = await _db.SystemCompanies.Where(c => ids.Contains(c.Id) && c.IsActive).Select(c => c.Id).ToListAsync();
        if (valid.Count != ids.Count) return BadRequest(new { message = "یکی از شرکت‌های انتخاب‌شده معتبر یا فعال نیست." });
        var old = await _db.UserCompanyAccesses.Where(x => x.UserId == userId).ToListAsync();
        _db.UserCompanyAccesses.RemoveRange(old);
        // ترتیب انتخاب ارسالی، شرکت پیش‌فرض/فعال را مشخص می‌کند؛ ترتیب SQL برای IN تضمین‌شده نیست.
        _db.UserCompanyAccesses.AddRange(ids.Select((id, i) => new UserCompanyAccess { UserId = userId, CompanyId = id, IsDefault = i == 0 }));
        await _db.SaveChangesAsync();
        return Ok(new { count = valid.Count });
    }

    [HttpGet("users/{userId:int}")]
    public async Task<IActionResult> GetUserCompanies(int userId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!IsAdmin && userId != UserId) return Forbid();
        return Ok(pagination.Result(await _db.UserCompanyAccesses.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.CompanyId).Select(x => x.CompanyId).ToPageListAsync(pagination)));
    }

    public sealed class CompanyAssignmentDto { public List<int> CompanyIds { get; set; } = new(); }
}
