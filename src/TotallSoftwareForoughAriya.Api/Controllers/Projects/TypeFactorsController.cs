using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

/// <summary>انواع فاکتور — ماژول دسترسی: TypeFactors</summary>
[Route("api/typefactors")]
public class TypeFactorsController : RbacControllerBase
{
    private const string Module = "TypeFactors";

    public TypeFactorsController(AppDbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        var query = Db.TypeFactors.AsNoTracking().Where(t => !t.IsDelete);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Name.Contains(search));

        var items = await query.OrderByDescending(t => t.CreatedAt).ThenBy(x => x.Id)
            .Select(t => new TypeFactorDto
            {
                Id = t.Id,
                Name = t.Name,
                CreatedAt = t.CreatedAt,
                ProjectCount = t.Projects.Count(p => !p.IsDelete)
            })
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(items));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var t = await Db.TypeFactors.AsNoTracking()
            .Where(x => x.Id == id && !x.IsDelete)
            .Select(x => new TypeFactorDto { Id = x.Id, Name = x.Name, CreatedAt = x.CreatedAt })
            .FirstOrDefaultAsync();
        return t is null ? NotFound(new { message = "نوع فاکتور پیدا نشد." }) : Ok(t);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TypeFactorDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "نام نوع فاکتور الزامی است." });

        var exists = await Db.TypeFactors.AnyAsync(t => !t.IsDelete && t.Name == dto.Name.Trim());
        if (exists) return BadRequest(new { message = "این نوع فاکتور قبلاً ثبت شده است." });

        var entity = new TypeFactor { Name = dto.Name.Trim(), CreatedAt = DateTime.Now };
        Db.TypeFactors.Add(entity);
        await Db.SaveChangesAsync();
        dto.Id = entity.Id;
        dto.CreatedAt = entity.CreatedAt;
        return Ok(dto);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TypeFactorDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "نام نوع فاکتور الزامی است." });

        var entity = await Db.TypeFactors.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (entity is null) return NotFound(new { message = "نوع فاکتور پیدا نشد." });

        var dup = await Db.TypeFactors.AnyAsync(t => t.Id != id && !t.IsDelete && t.Name == dto.Name.Trim());
        if (dup) return BadRequest(new { message = "نوع فاکتوری با این نام وجود دارد." });

        entity.Name = dto.Name.Trim();
        await Db.SaveChangesAsync();
        return Ok(dto);
    }

    /// <summary>حذف نرم</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;

        var entity = await Db.TypeFactors.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (entity is null) return NotFound(new { message = "نوع فاکتور پیدا نشد." });

        var inUse = await Db.ProjectEntryExits.AnyAsync(p => p.FactorTypeId == id && !p.IsDelete);
        if (inUse)
            return BadRequest(new { message = "این نوع فاکتور در پروژه‌ها استفاده شده و قابل حذف نیست." });

        entity.IsDelete = true;
        await Db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
