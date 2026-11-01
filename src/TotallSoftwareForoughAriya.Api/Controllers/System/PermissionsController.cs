using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PermissionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public PermissionsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.Permissions.AsNoTracking()
            .Select(p => new { p.Id, p.Module, p.Action }), skip, take));
}