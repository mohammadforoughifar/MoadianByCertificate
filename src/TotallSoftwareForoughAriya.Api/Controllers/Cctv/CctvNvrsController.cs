using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

/// <summary>مدیریت دستگاه‌های NVR/DVR مداربسته.</summary>
[ApiController]
[Route("api/[controller]")]
public class CctvNvrsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Hubs.DashboardBroadcaster _dash;
    public CctvNvrsController(AppDbContext db, Hubs.DashboardBroadcaster dash)
    {
        _db = db;
        _dash = dash;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var list = _db.CctvNvrs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            list = list.Where(n =>
                n.Model.Contains(q) || n.SerialNumber.Contains(q) ||
                (n.Ip != null && n.Ip.Contains(q)) ||
                (n.Location != null && n.Location.Contains(q)));
        }
        return Ok(await Paging.ResultAsync(list.OrderBy(n => n.Id), skip, take));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var item = await _db.CctvNvrs.FirstOrDefaultAsync(n => n.Id == id);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpGet("{id}/cameras")]
    public async Task<IActionResult> Cameras(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(_db.CctvCameras.AsNoTracking().Where(c => c.NvrId == id).OrderBy(c => c.Id), skip, take));

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] CctvNvr n)
    {
        if (string.IsNullOrWhiteSpace(n.Model) || string.IsNullOrWhiteSpace(n.SerialNumber))
            return BadRequest(new { message = "مدل و شماره سریال الزامی است." });
        if (await _db.CctvNvrs.AnyAsync(x => x.SerialNumber == n.SerialNumber))
            return BadRequest(new { message = $"دستگاهی با سریال «{n.SerialNumber}» قبلاً ثبت شده است." });
        _db.CctvNvrs.Add(n);
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok(new { id = n.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(int id, [FromBody] CctvNvr n)
    {
        var item = await _db.CctvNvrs.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        if (await _db.CctvNvrs.AnyAsync(x => x.SerialNumber == n.SerialNumber && x.Id != id))
            return BadRequest(new { message = $"سریال «{n.SerialNumber}» برای دستگاه دیگری ثبت شده است." });
        item.Model = n.Model;
        item.SerialNumber = n.SerialNumber;
        item.Ip = n.Ip;
        item.Mac = n.Mac;
        item.Location = n.Location;
        item.Notes = n.Notes;
        item.IsActive = n.IsActive;
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.CctvNvrs.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        // دوربین‌های متصل را جدا می‌کنیم تا یتیم نمانند
        var cams = _db.CctvCameras.Where(c => c.NvrId == id);
        foreach (var c in cams) c.NvrId = null;
        _db.CctvNvrs.Remove(item);
        await _db.SaveChangesAsync();
        _ = _dash.BroadcastAsync();
        return Ok();
    }
}
