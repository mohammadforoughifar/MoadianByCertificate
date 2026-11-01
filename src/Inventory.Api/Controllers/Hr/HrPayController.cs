using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>API حقوق و دستمزد: آیتم/فرمول، دوره‌ها، فیش، وام، خروجی‌ها، تسویه</summary>
[ApiController]
[Route("api/hr-pay")]
[Authorize]
public class HrPayController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HrPayService _svc;
    private readonly ForoughAryaHrUsers _fa;

    public HrPayController(AppDbContext db, HrPayService svc, ForoughAryaHrUsers fa) { _db = db; _svc = svc; _fa = fa; }

    /// <summary>پرسنلِ مجاز این ماژول: پیوند به کاربرِ فعالِ دارای دسترسی به «منابع انسانی فروغ آریا»</summary>
    private async Task<List<int>> AllowedEmpAsync() => (await _fa.PersonnelIdsAsync()).ToList();
    private async Task<bool> EmpOkAsync(int employeeId)
        => await _db.FaHrPersonnel.AnyAsync(e => e.Id == employeeId) && await _fa.IsAllowedPersonnelAsync(employeeId);
    private const string EmpNotAllowed = "پرسنل معتبر نیست یا به کاربرِ فعالِ دارای دسترسی به این ماژول پیوند ندارد.";

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyName => User.FindFirstValue(ClaimTypes.Name) ?? "";

    private bool? _mgr; private bool? _rd;
    private async Task<bool> IsManagerAsync()
    {
        if (_mgr.HasValue) return _mgr.Value;
        if (User.IsInRole("Admin")) { _mgr = true; return true; }
        if (User.HasClaim("permission", "HrPay.Manage")) { _mgr = true; return true; }
        _mgr = await HasPermAsync("Manage");
        return _mgr.Value;
    }
    private async Task<bool> CanReadAsync()
    {
        if (_rd.HasValue) return _rd.Value;
        if (await IsManagerAsync()) { _rd = true; return true; }
        if (User.HasClaim("permission", "HrPay.Read")) { _rd = true; return true; }
        _rd = await HasPermAsync("Read");
        return _rd.Value;
    }
    private async Task<bool> HasPermAsync(string action)
        => await _db.UserRoles.Where(ur => ur.UserId == MyUserId && _db.Roles.Any(r => r.Id == ur.RoleId && r.IsActive))
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, pm => pm.Id, (rp, pm) => pm)
            .AnyAsync(pm => pm.Module == "HrPay" && pm.Action == action);

    // ================= آیتم‌ها =================

    [HttpGet("items")]
    public async Task<IActionResult> Items([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        await _svc.EnsureSeedAsync();
        return Ok(await Paging.ResultAsync(_db.HrPayItems.AsNoTracking().OrderBy(i => i.SortOrder), skip, take));
    }

    public class ItemInput
    {
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string Kind { get; set; } = "Earning";
        public string Category { get; set; } = "Mazaya";
        public string? Formula { get; set; }
        public decimal DefaultAmount { get; set; }
        public bool IsTaxable { get; set; } = true;
        public bool IsInsuranceable { get; set; } = true;
        public bool IsAccrual { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
    }

    [HttpPost("items")]
    public async Task<IActionResult> CreateItem([FromBody] ItemInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        var code = (input.Code ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(input.Title))
            return BadRequest(new { message = "کد و عنوان آیتم الزامی است." });
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z][A-Z0-9_]*$"))
            return BadRequest(new { message = "کد باید انگلیسی و بدون فاصله باشد (مثل BONUS)." });
        if (await _db.HrPayItems.AnyAsync(i => i.Code == code))
            return BadRequest(new { message = "این کد تکراری است." });
        if (!string.IsNullOrWhiteSpace(input.Formula))
        {
            try { HrPayFormula.Eval(input.Formula, SampleVars()); }
            catch (Exception ex) { return BadRequest(new { message = "خطای فرمول: " + ex.Message }); }
        }
        var it = new HrPayItem
        {
            Code = code, Title = input.Title.Trim(),
            Kind = input.Kind == "Deduction" ? "Deduction" : "Earning",
            Category = input.Category, Formula = string.IsNullOrWhiteSpace(input.Formula) ? null : input.Formula.Trim(),
            DefaultAmount = input.DefaultAmount, IsTaxable = input.IsTaxable,
            IsInsuranceable = input.IsInsuranceable, IsAccrual = input.IsAccrual,
            IsActive = input.IsActive, SortOrder = input.SortOrder,
        };
        _db.HrPayItems.Add(it);
        await _db.SaveChangesAsync();
        return Ok(it);
    }

    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> UpdateItem(int id, [FromBody] ItemInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        var it = await _db.HrPayItems.FirstOrDefaultAsync(i => i.Id == id);
        if (it == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(input.Formula))
        {
            try { HrPayFormula.Eval(input.Formula, SampleVars()); }
            catch (Exception ex) { return BadRequest(new { message = "خطای فرمول: " + ex.Message }); }
        }
        it.Title = input.Title.Trim();
        it.Kind = input.Kind == "Deduction" ? "Deduction" : "Earning";
        it.Category = input.Category;
        it.Formula = string.IsNullOrWhiteSpace(input.Formula) ? null : input.Formula.Trim();
        it.DefaultAmount = input.DefaultAmount;
        it.IsTaxable = input.IsTaxable; it.IsInsuranceable = input.IsInsuranceable;
        it.IsAccrual = input.IsAccrual; it.IsActive = input.IsActive; it.SortOrder = input.SortOrder;
        await _db.SaveChangesAsync();
        return Ok(it);
    }

    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> DeleteItem(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var it = await _db.HrPayItems.FindAsync(id);
        if (it == null) return NotFound();
        _db.HrPayEmployeeItems.RemoveRange(_db.HrPayEmployeeItems.Where(x => x.ItemId == id));
        _db.HrPayItems.Remove(it);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static Dictionary<string, decimal> SampleVars() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["BASE"] = 100000000, ["DAILY_WAGE"] = 3333333, ["HOURLY_WAGE"] = 454545,
        ["MIN_WAGE"] = 103909270, ["MIN_DAILY"] = 3463642,
        ["DAYS_PAID"] = 30, ["DAYS_WORKED"] = 26, ["OT_H"] = 10, ["OT_H_HOL"] = 0,
        ["OT_H_NIGHT"] = 0, ["MISSION_DAYS"] = 0, ["LEAVE_UNPAID"] = 0, ["CHILDREN"] = 1,
        ["BON"] = 22000000, ["MASKAN"] = 9000000, ["EYAB"] = 0,
        ["OT_COEF_N"] = 1.4m, ["OT_COEF_H"] = 1.4m, ["OT_COEF_NT"] = 1.35m,
    };

    public class TestInput { public string Formula { get; set; } = ""; }

    [HttpPost("items/test")]
    public async Task<IActionResult> TestFormula([FromBody] TestInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(new { ok = true, value = HrPayFormula.Eval(input.Formula ?? "", SampleVars()) }); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ================= مبالغ اختصاصی =================

    [HttpGet("empitems")]
    public async Task<IActionResult> EmpItems([FromQuery] int? employeeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        var allowedEmp = await AllowedEmpAsync();
        var q = _db.HrPayEmployeeItems.AsNoTracking().Where(x => allowedEmp.Contains(x.EmployeeId));
        if (employeeId != null) q = q.Where(x => x.EmployeeId == employeeId);
        var pg = await Paging.QueryAsync(q, skip, take, 1000);
        var rows = pg.Rows;
        var items = await _db.HrPayItems.AsNoTracking().ToDictionaryAsync(i => i.Id);
        return Ok(pg.Result(rows.Select(r => new
        {
            r.Id, r.EmployeeId, r.ItemId, r.Amount,
            ItemCode = items.TryGetValue(r.ItemId, out var it) ? it.Code : null,
            ItemTitle = items.TryGetValue(r.ItemId, out var it2) ? it2.Title : null,
        })));
    }

    public class EmpItemInput { public int EmployeeId { get; set; } public int ItemId { get; set; } public decimal Amount { get; set; } }

    [HttpPost("empitems")]
    public async Task<IActionResult> SaveEmpItem([FromBody] EmpItemInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        if (!await EmpOkAsync(input.EmployeeId)) return BadRequest(new { message = EmpNotAllowed });
        if (!await _db.HrPayItems.AnyAsync(i => i.Id == input.ItemId)) return BadRequest(new { message = "آیتم معتبر نیست." });
        var ex = await _db.HrPayEmployeeItems.FirstOrDefaultAsync(x => x.EmployeeId == input.EmployeeId && x.ItemId == input.ItemId);
        if (ex == null) _db.HrPayEmployeeItems.Add(new HrPayEmployeeItem { EmployeeId = input.EmployeeId, ItemId = input.ItemId, Amount = input.Amount });
        else ex.Amount = input.Amount;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("empitems/{id:int}")]
    public async Task<IActionResult> DeleteEmpItem(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var r = await _db.HrPayEmployeeItems.FindAsync(id);
        if (r == null) return NotFound();
        _db.HrPayEmployeeItems.Remove(r);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= پروفایل =================

    [HttpGet("profiles")]
    public async Task<IActionResult> Profiles([FromQuery] int? employeeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        var allowedEmp = await AllowedEmpAsync();
        var q = _db.HrPayProfiles.AsNoTracking().Where(p => allowedEmp.Contains(p.EmployeeId));
        if (employeeId != null) q = q.Where(p => p.EmployeeId == employeeId);
        var pg = await Paging.QueryAsync(q, skip, take, 2000);
        return Ok(pg.Result());
    }

    public class ProfileInput
    {
        public int EmployeeId { get; set; }
        public int ChildrenCount { get; set; }
        public string? InsuranceNo { get; set; }
        public string? BankName { get; set; }
        public string? Iban { get; set; }
        public string? AccountNo { get; set; }
        public decimal ExtraTaxExempt { get; set; }
        public bool IsHardJob { get; set; }
    }

    [HttpPost("profiles")]
    public async Task<IActionResult> SaveProfile([FromBody] ProfileInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        if (!await EmpOkAsync(input.EmployeeId)) return BadRequest(new { message = EmpNotAllowed });
        var p = await _db.HrPayProfiles.FirstOrDefaultAsync(x => x.EmployeeId == input.EmployeeId);
        if (p == null) { p = new HrPayProfile { EmployeeId = input.EmployeeId }; _db.HrPayProfiles.Add(p); }
        p.ChildrenCount = Math.Max(0, input.ChildrenCount);
        p.InsuranceNo = input.InsuranceNo; p.BankName = input.BankName;
        p.Iban = input.Iban; p.AccountNo = input.AccountNo;
        p.ExtraTaxExempt = Math.Max(0, input.ExtraTaxExempt);
        p.IsHardJob = input.IsHardJob;
        await _db.SaveChangesAsync();
        return Ok(p);
    }

    // ================= پلکان مالیات =================

    [HttpGet("brackets")]
    public async Task<IActionResult> Brackets([FromQuery] int? year, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pagination = new Paging.Request(skip, take);
        if (!await CanReadAsync()) return Forbid();
        var y = year ?? 1405;
        await _svc.EnsureTaxSeedAsync(y);
        var query = _db.HrPayTaxBrackets.AsNoTracking().Where(b => b.Year == y);
        // SQLite cannot ORDER BY decimal; this conversion is only a sort key.
        // The original decimal amounts returned to the client are not converted.
        var ordered = _db.Database.IsSqlite()
            ? query.OrderBy(b => (double)b.FromAmount)
            : query.OrderBy(b => b.FromAmount);
        var rows = await ordered.ThenBy(b => b.Id).ToPageListAsync(pagination);
        return Ok(pagination.Result(rows));
    }

    public class BracketInput { public int Year { get; set; } public decimal FromAmount { get; set; } public decimal ToAmount { get; set; } public decimal Rate { get; set; } }

    [HttpPost("brackets")]
    public async Task<IActionResult> CreateBracket([FromBody] BracketInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        var b = new HrPayTaxBracket { Year = input.Year, FromAmount = input.FromAmount, ToAmount = input.ToAmount, Rate = input.Rate };
        _db.HrPayTaxBrackets.Add(b);
        await _db.SaveChangesAsync();
        return Ok(b);
    }

    [HttpPut("brackets/{id:int}")]
    public async Task<IActionResult> UpdateBracket(int id, [FromBody] BracketInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        var b = await _db.HrPayTaxBrackets.FindAsync(id);
        if (b == null) return NotFound();
        b.FromAmount = input.FromAmount; b.ToAmount = input.ToAmount; b.Rate = input.Rate;
        await _db.SaveChangesAsync();
        return Ok(b);
    }

    [HttpDelete("brackets/{id:int}")]
    public async Task<IActionResult> DeleteBracket(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var b = await _db.HrPayTaxBrackets.FindAsync(id);
        if (b == null) return NotFound();
        _db.HrPayTaxBrackets.Remove(b);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= قوانین =================

    [HttpGet("rules")]
    public async Task<IActionResult> Rules([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        var pagination = new Paging.Request(skip, take);
        var rules = await _svc.GetPayRulesAsync(pagination);
        return Ok(pagination.DictionaryResult(rules));
    }

    [HttpPut("rules")]
    public async Task<IActionResult> SaveRules([FromBody] Dictionary<string, string> values)
    {
        if (!await IsManagerAsync()) return Forbid();
        await _svc.SavePayRulesAsync(values ?? new());
        return Ok(new { ok = true });
    }

    // ================= وام =================

    [HttpGet("loans")]
    public async Task<IActionResult> Loans([FromQuery] int? employeeId, [FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        var allowedEmp = await AllowedEmpAsync();
        var q = _db.HrPayLoans.AsNoTracking().Where(l => allowedEmp.Contains(l.EmployeeId));
        if (employeeId != null) q = q.Where(l => l.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.Status == status);
        var pg = await Paging.QueryAsync(q.OrderByDescending(l => l.Id), skip, take, 1000);
        var rows = pg.Rows;
        var emps = await _db.FaHrPersonnel.AsNoTracking().ToDictionaryAsync(e => e.Id);
        return Ok(pg.Result(rows.Select(l => new
        {
            l.Id, l.EmployeeId, l.Kind, l.Amount, l.Installments, l.MonthlyAmount, l.PaidCount,
            l.StartYear, l.StartMonth, l.Status, l.Description, l.CreatedAt,
            EmployeeName = emps.TryGetValue(l.EmployeeId, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : null,
        })));
    }

    public class LoanInput
    {
        public int EmployeeId { get; set; }
        public string Kind { get; set; } = "Loan";
        public decimal Amount { get; set; }
        public int Installments { get; set; } = 1;
        public decimal MonthlyAmount { get; set; }
        public int StartYear { get; set; }
        public int StartMonth { get; set; }
        public string? Description { get; set; }
    }

    [HttpPost("loans")]
    public async Task<IActionResult> CreateLoan([FromBody] LoanInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        if (!await EmpOkAsync(input.EmployeeId)) return BadRequest(new { message = EmpNotAllowed });
        if (input.Amount <= 0) return BadRequest(new { message = "مبلغ نامعتبر است." });
        var n = input.Kind == "Advance" ? 1 : Math.Max(1, input.Installments);
        var monthly = input.MonthlyAmount > 0 ? input.MonthlyAmount : Math.Round(input.Amount / n);
        var ln = new HrPayLoan
        {
            EmployeeId = input.EmployeeId, Kind = input.Kind == "Advance" ? "Advance" : "Loan",
            Amount = input.Amount, Installments = n, MonthlyAmount = monthly,
            StartYear = input.StartYear, StartMonth = input.StartMonth,
            Status = "Active", Description = input.Description,
        };
        _db.HrPayLoans.Add(ln);
        await _db.SaveChangesAsync();
        return Ok(ln);
    }

    [HttpPost("loans/{id:int}/cancel")]
    public async Task<IActionResult> CancelLoan(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var ln = await _db.HrPayLoans.FindAsync(id);
        if (ln == null) return NotFound();
        ln.Status = "Cancelled";
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= معوقه / علی‌الحساب =================

    [HttpGet("arrears")]
    public async Task<IActionResult> Arrears([FromQuery] int? year, [FromQuery] int? month, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        var allowedEmp = await AllowedEmpAsync();
        var q = _db.HrPayArrears.AsNoTracking().Where(a => allowedEmp.Contains(a.EmployeeId));
        if (year != null) q = q.Where(a => a.Year == year);
        if (month != null) q = q.Where(a => a.Month == month);
        var pg = await Paging.QueryAsync(q.OrderByDescending(a => a.Id), skip, take, 500);
        var rows = pg.Rows;
        var emps = await _db.FaHrPersonnel.AsNoTracking().ToDictionaryAsync(e => e.Id);
        return Ok(pg.Result(rows.Select(a => new
        {
            a.Id, a.EmployeeId, a.Year, a.Month, a.Amount, a.Title, a.Status,
            EmployeeName = emps.TryGetValue(a.EmployeeId, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : null,
        })));
    }

    public class ArrearInput { public int EmployeeId { get; set; } public int Year { get; set; } public int Month { get; set; } public decimal Amount { get; set; } public string? Title { get; set; } }

    [HttpPost("arrears")]
    public async Task<IActionResult> CreateArrear([FromBody] ArrearInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        if (!await EmpOkAsync(input.EmployeeId)) return BadRequest(new { message = EmpNotAllowed });
        if (input.Amount <= 0) return BadRequest(new { message = "مبلغ نامعتبر است." });
        var a = new HrPayArrear { EmployeeId = input.EmployeeId, Year = input.Year, Month = input.Month, Amount = input.Amount, Title = input.Title, Status = "Pending" };
        _db.HrPayArrears.Add(a);
        await _db.SaveChangesAsync();
        return Ok(a);
    }

    [HttpDelete("arrears/{id:int}")]
    public async Task<IActionResult> DeleteArrear(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var a = await _db.HrPayArrears.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status != "Pending") return BadRequest(new { message = "این معوقه اعمال شده و قابل حذف نیست." });
        _db.HrPayArrears.Remove(a);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet("onaccounts")]
    public async Task<IActionResult> OnAccounts([FromQuery] int? year, [FromQuery] int? month, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        var allowedEmp = await AllowedEmpAsync();
        var q = _db.HrPayOnAccounts.AsNoTracking().Where(a => allowedEmp.Contains(a.EmployeeId));
        if (year != null) q = q.Where(a => a.Year == year);
        if (month != null) q = q.Where(a => a.Month == month);
        var pg = await Paging.QueryAsync(q.OrderByDescending(a => a.Id), skip, take, 500);
        var rows = pg.Rows;
        var emps = await _db.FaHrPersonnel.AsNoTracking().ToDictionaryAsync(e => e.Id);
        return Ok(pg.Result(rows.Select(a => new
        {
            a.Id, a.EmployeeId, a.Year, a.Month, a.Amount, a.PaidDate, a.Status,
            EmployeeName = emps.TryGetValue(a.EmployeeId, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : null,
        })));
    }

    [HttpPost("onaccounts")]
    public async Task<IActionResult> CreateOnAccount([FromBody] ArrearInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        if (!await EmpOkAsync(input.EmployeeId)) return BadRequest(new { message = EmpNotAllowed });
        if (input.Amount <= 0) return BadRequest(new { message = "مبلغ نامعتبر است." });
        var o = new HrPayOnAccount { EmployeeId = input.EmployeeId, Year = input.Year, Month = input.Month, Amount = input.Amount, Status = "Pending" };
        _db.HrPayOnAccounts.Add(o);
        await _db.SaveChangesAsync();
        return Ok(o);
    }

    [HttpDelete("onaccounts/{id:int}")]
    public async Task<IActionResult> DeleteOnAccount(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var o = await _db.HrPayOnAccounts.FindAsync(id);
        if (o == null) return NotFound();
        if (o.Status != "Pending") return BadRequest(new { message = "این علی‌الحساب اعمال شده و قابل حذف نیست." });
        _db.HrPayOnAccounts.Remove(o);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= دوره‌ها =================

    [HttpGet("runs")]
    public async Task<IActionResult> Runs([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        return Ok((await Paging.QueryAsync(_db.HrPayRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month), skip, take, 60)).Result());
    }

    public class RunInput { public int Year { get; set; } public int Month { get; set; } public string? Kind { get; set; } }

    [HttpPost("runs")]
    public async Task<IActionResult> CreateRun([FromBody] RunInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(await _svc.CreateRunAsync(input.Year, input.Month, MyName, input.Kind ?? "Monthly")); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class EydiInput { public int Year { get; set; } }

    [HttpPost("runs/eydi")]
    public async Task<IActionResult> CreateEydi([FromBody] EydiInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try
        {
            var run = await _svc.CreateRunAsync(input.Year, 12, MyName, "Eydi");
            return Ok(await _svc.CalculateRunAsync(run.Id));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("runs/{id:int}/calculate")]
    public async Task<IActionResult> Calculate(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(await _svc.CalculateRunAsync(id)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class StatusInput { public string Status { get; set; } = ""; }

    [HttpPost("runs/{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] StatusInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(await _svc.SetStatusAsync(id, input.Status)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("runs/{id:int}/compare")]
    public async Task<IActionResult> Compare(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        try { return Ok(await Paging.ResultAsync(async pagination => await _svc.CompareAsync(id, pagination: pagination), skip, take)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("runs/{id:int}/voucher")]
    public async Task<IActionResult> IssueVoucher(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        try
        {
            var v = await _svc.IssueVoucherAsync(id, MyName);
            return Ok(new { ok = true, voucherId = v.Id, number = v.Number });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ================= فیش‌ها =================

    [HttpGet("runs/{id:int}/slips")]
    public async Task<IActionResult> Slips(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        var allowedEmp = await AllowedEmpAsync();
        return Ok(await Paging.ResultAsync(_db.HrPaySlips.AsNoTracking().Where(s => s.RunId == id && allowedEmp.Contains(s.EmployeeId)).OrderBy(s => s.EmployeeName), skip, take));
    }

    [HttpGet("slips/{id:int}")]
    public async Task<IActionResult> Slip(int id)
    {
        var s = await _db.HrPaySlips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        if (s.UserId != MyUserId && !await CanReadAsync()) return Forbid();
        if (s.UserId != MyUserId && !await _fa.IsAllowedPersonnelAsync(s.EmployeeId)) return NotFound();
        var run = await _db.HrPayRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == s.RunId);
        return Ok(new
        {
            slip = s,
            runYear = run?.Year, runMonth = run?.Month, runStatus = run?.Status,
            lines = System.Text.Json.JsonSerializer.Deserialize<List<object>>(s.DetailsJson),
        });
    }

    [HttpGet("slips/my")]
    public async Task<IActionResult> MySlips([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var pg = await Paging.QueryAsync(_db.HrPaySlips.AsNoTracking().Where(s => s.UserId == MyUserId).OrderByDescending(s => s.Id), skip, take, 24);
        var rows = pg.Rows;
        var runIds = rows.Select(s => s.RunId).Distinct().ToList();
        var runs = await _db.HrPayRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        return Ok(pg.Result(rows.Select(s => new
        {
            s.Id, s.RunId, s.EmployeeName, s.DaysPaid, s.GrossEarnings, s.TaxAmount, s.InsuranceAmount, s.OtherDeductions, s.NetPay,
            Year = runs.TryGetValue(s.RunId, out var r) ? r.Year : 0,
            Month = runs.TryGetValue(s.RunId, out var r2) ? r2.Month : 0,
        })));
    }

    // ================= خروجی‌ها =================

    [HttpGet("exports/insurance")]
    public async Task<IActionResult> Insurance([FromQuery] int runId, [FromQuery] string format = "txt")
    {
        if (!await CanReadAsync()) return Forbid();
        try
        {
            var (bytes, name, ct) = await _svc.InsuranceFileAsync(runId, format == "csv" ? "csv" : "txt");
            return File(bytes, ct, name);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("exports/tax")]
    public async Task<IActionResult> Tax([FromQuery] int runId, [FromQuery] string format = "txt")
    {
        if (!await CanReadAsync()) return Forbid();
        try
        {
            var (bytes, name, ct) = await _svc.TaxFileAsync(runId, format == "csv" ? "csv" : "txt");
            return File(bytes, ct, name);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("exports/bank")]
    public async Task<IActionResult> Bank([FromQuery] int runId, [FromQuery] string format = "xlsx")
    {
        if (!await CanReadAsync()) return Forbid();
        try
        {
            var (bytes, name, ct) = await _svc.BankFileAsync(runId, format == "csv" ? "csv" : "xlsx");
            return File(bytes, ct, name);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ================= تسویه =================

    [HttpGet("settlements")]
    public async Task<IActionResult> Settlements([FromQuery] int? employeeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        var allowedEmp = await AllowedEmpAsync();
        var q = _db.HrPaySettlements.AsNoTracking().Where(s => allowedEmp.Contains(s.EmployeeId));
        if (employeeId != null) q = q.Where(s => s.EmployeeId == employeeId);
        var pg = await Paging.QueryAsync(q.OrderByDescending(s => s.Id), skip, take, 500);
        var rows = pg.Rows;
        var emps = await _db.FaHrPersonnel.AsNoTracking().ToDictionaryAsync(e => e.Id);
        return Ok(pg.Result(rows.Select(s => new
        {
            s.Id, s.EmployeeId, s.LeaveDate, s.YearsOfService, s.LastBase, s.UnusedLeaveDays,
            s.SeveranceAmount, s.LeaveRefund, s.EydiProrata, s.TotalAmount, s.Status, s.CreatedAt,
            EmployeeName = emps.TryGetValue(s.EmployeeId, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : null,
        })));
    }

    public class SettlementInput { public int EmployeeId { get; set; } public DateTime LeaveDate { get; set; } public double? UnusedLeaveDays { get; set; } }

    [HttpPost("settlements/calculate")]
    public async Task<IActionResult> CalcSettlement([FromBody] SettlementInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(await _svc.CalculateSettlementAsync(input.EmployeeId, input.LeaveDate, input.UnusedLeaveDays)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("settlements/{id:int}/status")]
    public async Task<IActionResult> SettlementStatus(int id, [FromBody] StatusInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        var s = await _db.HrPaySettlements.FindAsync(id);
        if (s == null) return NotFound();
        if (input.Status is not ("Approved" or "Paid" or "Draft"))
            return BadRequest(new { message = "وضعیت نامعتبر." });
        s.Status = input.Status;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet("employees")]
    public async Task<IActionResult> Employees([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        // کمبو/فهرست پرسنل: فقط جدول اختصاصی این ماژول + پیوند به کاربرِ فعالِ دارای دسترسی
        var allowedEmp = await AllowedEmpAsync();
        var pg = await Paging.QueryAsync(_db.FaHrPersonnel.AsNoTracking().Where(e => allowedEmp.Contains(e.Id))
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => new { e.Id, e.Code, e.FirstName, e.LastName, e.NationalCode, e.BaseSalary, e.Status, e.HireDate }), skip, take, 2000);
        return Ok(pg.Result());
    }

    // ================= کارکرد ماه (نمایش در فیش/گزارش) =================

    [HttpGet("attendance-month")]
    public async Task<IActionResult> AttendanceMonth([FromQuery] int employeeId, [FromQuery] int year, [FromQuery] int month)
    {
        if (!await CanReadAsync()) return Forbid();
        if (!await EmpOkAsync(employeeId)) return NotFound();
        var uid = await _db.HrUserLinks.AsNoTracking().Where(x => x.EmployeeId == employeeId).Select(x => (int?)x.UserId).FirstOrDefaultAsync();
        return Ok(await _svc.MonthAggAsync(uid, year, month));
    }

    // ================= فاز ۳: بایگانی ارسال لیست‌های قانونی =================

    [HttpGet("filings/preflight")]
    public async Task<IActionResult> FilingPreflight([FromQuery] int runId, [FromQuery] string kind)
    {
        if (!await CanReadAsync()) return Forbid();
        try
        {
            var r = await _svc.PreflightAsync(runId, kind ?? "");
            return Ok(new { errors = r.Errors, warnings = r.Warnings, canFile = r.Errors.Count == 0 });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class FileInput
    {
        public int RunId { get; set; }
        public string Kind { get; set; } = "";
        public string? ReceiptNo { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost("filings")]
    public async Task<IActionResult> FileList([FromBody] FileInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(await _svc.FileAsync(input.RunId, input.Kind ?? "", MyName, input.ReceiptNo, input.Note)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("filings")]
    public async Task<IActionResult> Filings([FromQuery] int? runId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await CanReadAsync()) return Forbid();
        return Ok(await Paging.ResultAsync(async pagination => await _svc.FilingsAsync(runId, pagination: pagination), skip, take));
    }

    [HttpGet("filings/verify")]
    public async Task<IActionResult> VerifyFiling([FromQuery] int runId, [FromQuery] string kind)
    {
        if (!await CanReadAsync()) return Forbid();
        try { return Ok(await _svc.VerifyFilingAsync(runId, kind ?? "")); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
