using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Inventory.Shared.Entities;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RolesController : ControllerBase
{
    private readonly AppDbContext _db;

    public RolesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.Roles.AsNoTracking().OrderBy(r => r.Id), skip, take));

    [HttpGet("{id}/permissions")]
    public async Task<IActionResult> GetPermissions(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await _db.Roles.AnyAsync(r => r.Id == id)) return NotFound();
        return Ok(await Paging.ResultAsync(_db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == id).OrderBy(rp => rp.PermissionId)
            .Select(rp => rp.PermissionId), skip, take));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RoleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "نام نقش الزامی است." });

        var role = new Role
        {
            Name = dto.Name.Trim(),
            Description = dto.Description,
            IsActive = dto.IsActive
        };

        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        // Assign permissions
        if (dto.PermissionIds?.Any() == true)
        {
            foreach (var permId in dto.PermissionIds)
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permId
                });
            }
            await _db.SaveChangesAsync();
        }

        return Ok(new { id = role.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] RoleDto dto)
    {
        var role = await _db.Roles.FindAsync(id);
        if (role == null) return NotFound();

        role.Name = dto.Name.Trim();
        role.Description = dto.Description;
        role.IsActive = dto.IsActive;

        // Remove old permissions
        var oldPerms = await _db.RolePermissions.Where(rp => rp.RoleId == id).ToListAsync();
        _db.RolePermissions.RemoveRange(oldPerms);

        // Add new permissions
        if (dto.PermissionIds?.Any() == true)
        {
            foreach (var permId in dto.PermissionIds)
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    RoleId = id,
                    PermissionId = permId
                });
            }
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var role = await _db.Roles.FindAsync(id);
        if (role == null) return NotFound();

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
        return Ok();
    }

    public class RoleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public List<int> PermissionIds { get; set; } = new();
    }
}