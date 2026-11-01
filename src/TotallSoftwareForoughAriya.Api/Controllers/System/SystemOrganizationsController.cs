using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

/// <summary>مدیریت سازمان‌ها / واحدها — برای تخصیص به کاربران و جزء «واحد» در شماره نامه</summary>
[ApiController]
[Route("api/[controller]")]
public class SystemOrganizationsController : RbacControllerBase
{
    private readonly AppDbContext _db;
    private readonly IOrganizationServices _org;

    public SystemOrganizationsController(AppDbContext db, IOrganizationServices org) : base(db)
    {
        _db = db;
        _org = org;
    }

    // خواندن فهرست واحدها برای صفحاتِ دیگر هم لازم است (کاربران/کاربران سیستم/نامه صادره)؛
    // پس علاوه بر مجوز خودِ این ماژول، داشتنِ مجوز «کاربران» یا «شرکت‌ها» هم کافی است.
    private async Task<IActionResult?> ReadGuardAsync()
    {
        if (await HasAnyAsync("SystemOrganizations", "Read", "Create", "Update", "Delete")) return null;
        if (await HasAsync("SystemUsers", "Read")) return null;
        if (await HasAsync("SystemCompanies", "Read")) return null;
        return StatusCode(403, new { message = "شما به واحدهای سازمانی دسترسی ندارید." });
    }

    private Task<IActionResult?> WriteGuardAsync(string action) =>
        ForbiddenUnlessAsync("SystemOrganizations", action);

    /// <summary>فهرست واحدها — می‌تواند بر اساس شرکت فیلتر شود (companyId)</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? companyId = null, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ReadGuardAsync() is { } forbid) return forbid;
        IQueryable<Organization> query = _db.Organizations.Where(o => !o.IsDelete);
        if (companyId is > 0)
            query = query.Where(o => o.CompanyId == companyId);
        return Ok(await Paging.ResultAsync(query.OrderBy(o => o.NameUnit), skip, take));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ReadGuardAsync() is { } forbidId) return forbidId;
        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.OrganizationId == id);
        if (org == null) return NotFound(new { message = "سازمان پیدا نشد." });
        return Ok(org);
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Organization o)
    {
        if (await WriteGuardAsync("Create") is { } forbidPost) return forbidPost;
        if (o.CompanyId is null or 0)
            return BadRequest(new { message = "شرکتِ واحد سازمانی را انتخاب کنید (هر واحد باید به یک شرکت تعلق داشته باشد)." });
        if (string.IsNullOrWhiteSpace(o.NameUnit))
            return BadRequest(new { message = "نام واحد را وارد کنید." });
        o.OrganizationId = 0;
        if (o.CreatedAt == default) o.CreatedAt = DateTime.Now;
        _db.Organizations.Add(o);
        await _db.SaveChangesAsync();
        return Ok(new { id = o.OrganizationId });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Put(int id, [FromBody] Organization o)
    {
        if (await WriteGuardAsync("Update") is { } forbidPut) return forbidPut;
        var item = await _db.Organizations.FirstOrDefaultAsync(x => x.OrganizationId == id);
        if (item == null) return NotFound(new { message = "سازمان پیدا نشد." });

        item.NameUnit = o.NameUnit;
        item.NameUniq = o.NameUniq;
        item.CompanyId = o.CompanyId;
        item.IsDefault = o.IsDefault;
        item.IsActive = o.IsActive;
        item.IsDelete = o.IsDelete;

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await WriteGuardAsync("Delete") is { } forbidDel) return forbidDel;
        var item = await _db.Organizations.FirstOrDefaultAsync(x => x.OrganizationId == id);
        if (item == null) return NotFound(new { message = "سازمان پیدا نشد." });

        _db.Organizations.Remove(item);
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>فهرست سازمان‌های فعال برای د virgehenden انتخاب (combeobox) — می‌تواند بر اساس شرکت فیلتر شود</summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetActive([FromQuery] int? companyId = null)
    {
        if (await ReadGuardAsync() is { } forbidActive) return forbidActive;
        var orgs = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsDelete && o.IsActive)
            .OrderBy(o => o.NameUnit)
            .ToListAsync();

        if (companyId is > 0)
            orgs = orgs.Where(o => o.CompanyId == companyId).ToList();

        return Ok(orgs.Select(o => new OrganizationDto
        {
            OrganizationId = o.OrganizationId,
            CompanyId = o.CompanyId,
            NameUnit = o.NameUnit,
            NameUniq = o.NameUniq,
            IsDefault = o.IsDefault
        }).ToList());
    }
}
