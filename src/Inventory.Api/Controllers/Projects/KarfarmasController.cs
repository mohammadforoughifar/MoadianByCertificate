using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

/// <summary>کارفرماها — ماژول دسترسی: Karfarmas</summary>
[Route("api/karfarmas")]
public class KarfarmasController : RbacControllerBase
{
    private const string Module = "Karfarmas";

    public KarfarmasController(AppDbContext db) : base(db) { }

    /// <summary>لیست کارفرماها + جستجو</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        var query = Db.KarFarmas.AsNoTracking().Where(k => !k.IsDelete);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(k => k.Name.Contains(search) ||
                                     (k.ShomareSabt != null && k.ShomareSabt.Contains(search)) ||
                                     (k.ModirAmelPhone != null && k.ModirAmelPhone.Contains(search)));

        var items = await query.OrderByDescending(k => k.CreatedAt).ThenBy(x => x.Id)
            .Select(k => new KarFarmaDto
            {
                Id = k.Id,
                Name = k.Name,
                Address = k.Address,
                ModirAmelPhone = k.ModirAmelPhone,
                Telephone = k.Telephone,
                Fax = k.Fax,
                ShomareSabt = k.ShomareSabt,
                CreatedAt = k.CreatedAt,
                ProjectCount = k.Projects.Count(p => !p.IsDelete)
            })
            .ToPageListAsync(pagination);
        return Ok(pagination.Result(items));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var k = await Db.KarFarmas.AsNoTracking()
            .Where(x => x.Id == id && !x.IsDelete)
            .Select(x => new KarFarmaDto
            {
                Id = x.Id,
                Name = x.Name,
                Address = x.Address,
                ModirAmelPhone = x.ModirAmelPhone,
                Telephone = x.Telephone,
                Fax = x.Fax,
                ShomareSabt = x.ShomareSabt,
                CreatedAt = x.CreatedAt,
                ProjectCount = x.Projects.Count(p => !p.IsDelete)
            }).FirstOrDefaultAsync();
        return k is null ? NotFound(new { message = "کارفرما پیدا نشد." }) : Ok(k);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] KarFarmaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "نام کارفرما الزامی است." });

        var entity = new KarFarma
        {
            Name = dto.Name.Trim(),
            Address = dto.Address?.Trim(),
            ModirAmelPhone = dto.ModirAmelPhone?.Trim(),
            Telephone = dto.Telephone?.Trim(),
            Fax = dto.Fax?.Trim(),
            ShomareSabt = dto.ShomareSabt?.Trim(),
            CreatedAt = DateTime.Now
        };
        Db.KarFarmas.Add(entity);
        await Db.SaveChangesAsync();
        dto.Id = entity.Id;
        dto.CreatedAt = entity.CreatedAt;
        return Ok(dto);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] KarFarmaDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "نام کارفرما الزامی است." });

        var entity = await Db.KarFarmas.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (entity is null) return NotFound(new { message = "کارفرما پیدا نشد." });

        entity.Name = dto.Name.Trim();
        entity.Address = dto.Address?.Trim();
        entity.ModirAmelPhone = dto.ModirAmelPhone?.Trim();
        entity.Telephone = dto.Telephone?.Trim();
        entity.Fax = dto.Fax?.Trim();
        entity.ShomareSabt = dto.ShomareSabt?.Trim();

        await Db.SaveChangesAsync();
        return Ok(dto);
    }

    /// <summary>حذف نرم</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;

        var entity = await Db.KarFarmas.FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (entity is null) return NotFound(new { message = "کارفرما پیدا نشد." });

        var inUse = await Db.ProjectEntryExits.AnyAsync(p => p.KarFarmaId == id && !p.IsDelete);
        if (inUse)
            return BadRequest(new { message = "این کارفرما در پروژه‌ها استفاده شده و قابل حذف نیست." });

        entity.IsDelete = true;
        await Db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
