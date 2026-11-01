using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Inventory.Shared.Entities;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemUsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FileStore _store;
    private readonly UserPhotoService _photos;
    public SystemUsersController(AppDbContext db, FileStore store, UserPhotoService photos)
    {
        _db = db;
        _store = store;
        _photos = photos;
    }

    // ================== CRUD کاربران سیستم ==================

    [HttpGet]
    public async Task<ActionResult> Get([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.SystemUsers.OrderBy(u => u.Id), skip, take));

    [HttpGet("{id:int}")]
    public async Task<ActionResult> Get(int id)
    {
        var user = await _db.SystemUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (user == null) return NotFound(new { message = "کاربر پیدا نشد." });
        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult> Post([FromBody] SystemUser u)
    {
        if (string.IsNullOrWhiteSpace(u.FirstName))
            return BadRequest(new { message = "نام کاربر الزامی است." });

        u.Id = 0; // جلوگیری از خطای IDENTITY_INSERT
        if (u.CreatedAt == default) u.CreatedAt = DateTime.Now;

        _db.SystemUsers.Add(u);
        await _db.SaveChangesAsync();
        return Ok(new { id = u.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult> Put(int id, [FromBody] SystemUser u)
    {
        var item = await _db.SystemUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound(new { message = "کاربر پیدا نشد." });

        item.Username = u.Username;
        item.FirstName = u.FirstName;
        item.LastName = u.LastName;
        item.Phone = u.Phone;
        item.StaffNumber = u.StaffNumber;
        item.DepartmentId = u.DepartmentId;
        item.CompanyId = u.CompanyId;
        item.OrganizationId = u.OrganizationId;
        item.Role = u.Role;
        item.IsActive = u.IsActive;

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var item = await _db.SystemUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound(new { message = "کاربر پیدا نشد." });

        _store.Delete(item.PhotoPath);
        _db.SystemUsers.Remove(item);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ================== عکس کاربر (فایل روی دیسک — uploads/users) ==================

    [HttpPost("photo/{id:int}")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult> UploadPhoto(int id, IFormFile file)
    {
        var item = await _db.SystemUsers.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return NotFound(new { message = "کاربر پیدا نشد." });
        if (file == null || file.Length == 0) return BadRequest(new { message = "عکسی انتخاب نشده است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        string newPath;
        try { newPath = await _photos.SaveUserPhotoAsync(item.Id, ms, file.FileName); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        if (!string.IsNullOrWhiteSpace(item.PhotoPath)) _store.Delete(item.PhotoPath);
        item.PhotoPath = newPath;
        await _db.SaveChangesAsync();
        return Ok(new { photoPath = newPath });
    }

    /// <summary>عکس کاربر — اگر تعریف نشده، عکس پیش‌فرض. (بدون احراز هویت — برای تگ img)</summary>
    [HttpGet("photo/{id:int}")]
    [AllowAnonymous]
    public ActionResult Photo(int id)
    {
        var path = _db.SystemUsers.AsNoTracking().FirstOrDefault(x => x.Id == id)?.PhotoPath;
        var (data, ct) = _photos.GetPhoto(path);
        return File(data, ct);
    }

    // توجه: تخصیص نقش (RBAC) از کاربران سیستم حذف شد —
    // نقش‌ها به کاربران «لاگین» (بخش کاربران) اختصاص داده می‌شوند.
}