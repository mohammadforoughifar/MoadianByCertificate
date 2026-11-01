using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Inventory.Api.Data;
using Paging = Inventory.Api.Services.Paging;
using System.IO;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemCompaniesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public SystemCompaniesController(AppDbContext db, IWebHostEnvironment env) { _db = db; _env = env; }

    [HttpGet] public async Task<ActionResult> Get([FromQuery] int skip = 0, [FromQuery] int? take = null) => Ok(await Paging.ResultAsync(_db.SystemCompanies, skip, take));
    [HttpGet("{id}")] public async Task<ActionResult> Get(int id) => Ok(await _db.SystemCompanies.FirstOrDefaultAsync(x => x.Id == id));
    [HttpPost] public async Task<ActionResult> Post([FromBody] SystemCompany c) { _db.SystemCompanies.Add(c); await _db.SaveChangesAsync(); return Ok(new { id = c.Id }); }
    [HttpPut("{id}")] public async Task<ActionResult> Put(int id, [FromBody] SystemCompany c) { var item = await _db.SystemCompanies.FirstOrDefaultAsync(x => x.Id == id); if (item == null) return NotFound(); item.Name = c.Name; item.Code = c.Code; item.Phone = c.Phone; item.Address = c.Address; item.IsActive = c.IsActive; item.LetterheadFileName = c.LetterheadFileName; await _db.SaveChangesAsync(); return Ok(); }

    /// <summary>آپلود فایل PDF سربرگ شرکت و ذخیره در wwwroot/uploads/letterheads</summary>
    [HttpPost("{id}/letterhead")]
    public async Task<ActionResult> UploadLetterhead(int id, IFormFile file)
    {
        var item = await _db.SystemCompanies.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound();
        if (file == null || file.Length == 0) return BadRequest(new { message = "فایل انتخاب نشده است." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "فقط فایل PDF برای سربرگ مجاز است." });

        var uploads = Path.Combine(_env.WebRootPath, "uploads", "letterheads");
        Directory.CreateDirectory(uploads);
        var fileName = $"company-{id}-{Guid.NewGuid():N}.pdf";
        var fullPath = Path.Combine(uploads, fileName);
        await using (var stream = new FileStream(fullPath, FileMode.Create))
            await file.CopyToAsync(stream);

        var relativePath = Path.Combine("uploads", "letterheads", fileName).Replace("\\", "/");
        item.LetterheadFileName = relativePath;
        await _db.SaveChangesAsync();
        return Ok(new { letterheadFileName = relativePath });
    }

    [HttpDelete("{id}")] public async Task<ActionResult> Delete(int id) { var item = await _db.SystemCompanies.FirstOrDefaultAsync(x => x.Id == id); if (item == null) return NotFound(); _db.SystemCompanies.Remove(item); await _db.SaveChangesAsync(); return Ok(); }
}
