using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Shared.Contracts;
using RadisHr.Shared.Models;
using Paging = Inventory.Api.Services.Paging;

namespace RadisHr.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeesController(AppDbContext db) => _db = db;

    // ==================== دسترسی تفکیکی پرسنل ====================
    // «افزودن» و «ویرایش» پرسنل دو مجوز مستقل‌اند تا در تنظیمات ← نقش‌ها جداگانه انتخاب شوند:
    //   RadisHr.EmployeeCreate → ایجاد پرسنل و ورود گروهی
    //   RadisHr.EmployeeUpdate → ویرایش پرونده پرسنلی
    // مجوز قدیمی RadisHr.EmployeeEntry (و RadisHr.Access / نقش‌های hr و ceo) برای سازگاری،
    // هر دو عمل را مجاز می‌کند تا با ارتقا دسترسی هیچ کاربری قطع نشود.
    private bool HasPerm(string perm) => User.HasClaim("permission", perm);
    private bool IsModulePrivileged =>
        User.IsInRole("hr") || User.IsInRole("ceo") || User.IsInRole("Admin") || HasPerm("RadisHr.Access");
    private bool CanCreateEmployee =>
        IsModulePrivileged || HasPerm("RadisHr.EmployeeCreate") || HasPerm("RadisHr.EmployeeEntry");
    private bool CanUpdateEmployee =>
        IsModulePrivileged || HasPerm("RadisHr.EmployeeUpdate") || HasPerm("RadisHr.EmployeeEntry");

    private ObjectResult ForbiddenCreate() => StatusCode(403, new ApiMessage(false,
        "برای ثبت پرسنل جدید مجوز «افزودن پرسنل» (RadisHr.EmployeeCreate) لازم است."));
    private ObjectResult ForbiddenUpdate() => StatusCode(403, new ApiMessage(false,
        "برای ویرایش پروندهٔ پرسنلی مجوز «ویرایش پرسنل» (RadisHr.EmployeeUpdate) لازم است."));

    [HttpGet]
    public async Task<ActionResult<List<Employee>>> GetAll([FromQuery] string? search, [FromQuery] string? unit, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var query = _db.Employees.Include(e => e.Contracts).AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(unit))
            query = query.Where(e => e.Unit == unit);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(e =>
                e.First.Contains(s) || e.Last.Contains(s) ||
                e.Code.Contains(s) || e.Nid.Contains(s) ||
                e.PositionTitle.Contains(s) || e.Unit.Contains(s));
        }

        return Ok(await Paging.ResultAsync(query.OrderBy(e => e.Code), skip, take));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Employee>> Get(int id)
    {
        var employee = await _db.Employees.Include(e => e.Contracts)
            .FirstOrDefaultAsync(e => e.Id == id);
        return employee == null ? NotFound(new ApiMessage(false, "پرسنل یافت نشد.")) : employee;
    }

    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<Employee>> GetByCode(string code)
    {
        var employee = await _db.Employees.Include(e => e.Contracts)
            .FirstOrDefaultAsync(e => e.Code == code);
        return employee == null ? NotFound(new ApiMessage(false, "پرسنل یافت نشد.")) : employee;
    }

    [Authorize(Roles = "hr,ceo")]
    [HttpPost]
    public async Task<ActionResult<IdResponse>> Create(Employee employee)
    {
        // مجوز تفکیکی «افزودن» (نقش‌های hr/ceo و مجوز قدیمی EmployeeEntry همچنان مجازند)
        if (!CanCreateEmployee) return ForbiddenCreate();
        if (string.IsNullOrWhiteSpace(employee.Code))
            return BadRequest(new ApiMessage(false, "کد پرسنلی الزامی است."));

        if (await _db.Employees.AnyAsync(e => e.Code == employee.Code))
            return Conflict(new ApiMessage(false, "کد پرسنلی تکراری است."));

        employee.Id = 0;
        foreach (var c in employee.Contracts) c.Id = 0;
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        return new IdResponse(employee.Id, "اطلاعات پرسنل ثبت شد.");
    }

    [Authorize(Roles = "hr,ceo")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiMessage>> Update(int id, Employee input)
    {
        // مجوز تفکیکی «ویرایش» — کسی که فقط EmployeeCreate دارد نمی‌تواند پرونده را تغییر دهد
        if (!CanUpdateEmployee) return ForbiddenUpdate();
        var employee = await _db.Employees.Include(e => e.Contracts)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (employee == null) return NotFound(new ApiMessage(false, "پرسنل یافت نشد."));

        if (await _db.Employees.AnyAsync(e => e.Code == input.Code && e.Id != id))
            return Conflict(new ApiMessage(false, "کد پرسنلی تکراری است."));

        _db.Entry(employee).CurrentValues.SetValues(input);

        _db.EmployeeContracts.RemoveRange(employee.Contracts);
        foreach (var c in input.Contracts)
            _db.EmployeeContracts.Add(new EmployeeContract
            {
                EmployeeId = id, Name = c.Name, Month = c.Month, FileId = c.FileId
            });

        await _db.SaveChangesAsync();
        return new ApiMessage(true, "اطلاعات پرسنل به‌روزرسانی شد.");
    }

    [Authorize(Roles = "hr,ceo")]
    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiMessage>> Delete(int id)
    {
        var employee = await _db.Employees.FindAsync(id);
        if (employee == null) return NotFound(new ApiMessage(false, "پرسنل یافت نشد."));
        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "پرسنل حذف شد.");
    }

    /// <summary>ورود گروهی اطلاعات پرسنل (جایگزین بارگذاری اکسل نسخهٔ قدیم)</summary>
    [Authorize(Roles = "hr,ceo")]
    [HttpPost("import")]
    public async Task<ActionResult<ImportEmployeesResult>> Import(ImportEmployeesRequest request)
    {
        // ورود گروهی «ایجاد» می‌کند؛ اگر Overwrite فعال باشد پرونده‌های موجود را هم ویرایش می‌کند
        // بنابراین هر دو مجوز لازم است (مجوز قدیمی EmployeeEntry هر دو را می‌دهد).
        if (!CanCreateEmployee) return ForbiddenCreate();
        if (request.Overwrite && !CanUpdateEmployee) return ForbiddenUpdate();
        int inserted = 0, updated = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var incoming in request.Employees)
        {
            if (string.IsNullOrWhiteSpace(incoming.Code))
            {
                errors.Add($"ردیف بدون کد پرسنلی: {incoming.First} {incoming.Last}");
                skipped++;
                continue;
            }

            var existing = await _db.Employees.FirstOrDefaultAsync(e => e.Code == incoming.Code);
            if (existing == null)
            {
                incoming.Id = 0;
                foreach (var c in incoming.Contracts) c.Id = 0;
                _db.Employees.Add(incoming);
                inserted++;
            }
            else if (request.Overwrite)
            {
                incoming.Id = existing.Id;
                _db.Entry(existing).CurrentValues.SetValues(incoming);
                updated++;
            }
            else skipped++;
        }

        await _db.SaveChangesAsync();
        return new ImportEmployeesResult(inserted, updated, skipped, errors);
    }
}
