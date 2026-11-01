using Inventory.Api.Services;
using Inventory.Api.Data;
using Inventory.Api.Services.HrMain;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers.HrMain;

/// <summary>
/// ================== منابع انسانی اصلی — مدیریت پایه سازمانی ==================
/// ستون فقرات ماژول HR: پروفایل شرکت، شعب، ساختار سازمانی، پست‌ها،
/// زبان/تقویم، قوانین پیش‌فرض و تعطیلات رسمی.
/// مجوزها: HrMain.Read / Create / Update / Delete / Manage
/// </summary>
[Route("api/hr-main")]
public class HrMainController : RbacControllerBase
{
    private const string Mod = "HrMain";
    private readonly IHrMainService _svc;

    public HrMainController(AppDbContext db, IHrMainService svc) : base(db) => _svc = svc;

    // ------------------- نمای کلی -------------------

    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Overview") is { } f) return f;
        return Ok(await _svc.OverviewAsync());
    }

    // ------------------- پروفایل شرکت -------------------
    // دسترسی تفکیکی: کسی که فقط مجوز اختصاصی «HrMain.Company» دارد هم می‌تواند
    // بدون داشتن Read/Update عمومی، فقط پروفایل شرکت را مدیریت کند.

    [HttpGet("company")]
    public async Task<IActionResult> GetCompany()
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Company") is { } f) return f;
        return Ok(await _svc.GetCompanyAsync());
    }

    [HttpPut("company")]
    public async Task<IActionResult> SaveCompany([FromBody] HrMainCompanySaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", "Company") is { } f) return f;
        return Ok(await _svc.SaveCompanyAsync(dto));
    }

    [HttpPost("company/logo")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadLogo(IFormFile? file)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", "Company") is { } f) return f;
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "فایل لوگو ارسال نشد." });
        await using var stream = file.OpenReadStream();
        return Ok(await _svc.SaveLogoAsync(stream, file.FileName));
    }

    [HttpDelete("company/logo")]
    public async Task<IActionResult> DeleteLogo()
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", "Company") is { } f) return f;
        await _svc.DeleteLogoAsync();
        return Ok(new { ok = true });
    }

    // ------------------- شعب و دفاتر -------------------
    // دسترسی تفکیکی: مجوز اختصاصی «HrMain.Branches» اجازه می‌دهد یک نفر فقط
    // مسئول شعب باشد، بدون این‌که به تنظیمات شرکت/ساختار/قوانین دسترسی داشته باشد.

    [HttpGet("branches")]
    public async Task<IActionResult> Branches([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Branches") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListBranchesAsync(pagination: pagination), skip, take));
    }

    [HttpPost("branches")]
    public async Task<IActionResult> CreateBranch([FromBody] HrMainBranchSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Create", "Branches") is { } f) return f;
        return Ok(await _svc.SaveBranchAsync(null, dto));
    }

    [HttpPut("branches/{id:int}")]
    public async Task<IActionResult> UpdateBranch(int id, [FromBody] HrMainBranchSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", "Branches") is { } f) return f;
        return Ok(await _svc.SaveBranchAsync(id, dto));
    }

    [HttpDelete("branches/{id:int}")]
    public async Task<IActionResult> DeleteBranch(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Delete", "Branches") is { } f) return f;
        await _svc.DeleteBranchAsync(id);
        return Ok(new { ok = true });
    }

    // ------------------- ساختار سازمانی -------------------
    // دسترسی تفکیکی: مجوز اختصاصی «HrMain.Org» برای مسئول تشکیلات سازمانی،
    // بدون نیاز به مجوز عمومی که کل ماژول (شرکت/شعب/قوانین) را هم باز می‌کند.

    [HttpGet("org/tree")]
    public async Task<IActionResult> OrgTree([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Org") is { } f) return f;
        return Ok(await Paging.ResultAsync(pagination => _svc.GetTreeAsync(pagination), skip, take));
    }

    [HttpGet("org/nodes")]
    public async Task<IActionResult> OrgNodes([FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Org") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListNodesAsync(pagination: pagination), skip, take));
    }

    [HttpPost("org/nodes")]
    public async Task<IActionResult> CreateNode([FromBody] HrMainOrgNodeSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Create", "Org") is { } f) return f;
        return Ok(await _svc.SaveNodeAsync(null, dto, MyUserId, MyUsername));
    }

    [HttpPut("org/nodes/{id:int}")]
    public async Task<IActionResult> UpdateNode(int id, [FromBody] HrMainOrgNodeSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", "Org") is { } f) return f;
        return Ok(await _svc.SaveNodeAsync(id, dto, MyUserId, MyUsername));
    }

    [HttpDelete("org/nodes/{id:int}")]
    public async Task<IActionResult> DeleteNode(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Delete", "Org") is { } f) return f;
        await _svc.DeleteNodeAsync(id, MyUserId, MyUsername);
        return Ok(new { ok = true });
    }

    // ------------------- پست‌های سازمانی -------------------
    // دسترسی تفکیکی: مجوز اختصاصی «HrMain.Positions».

    [HttpGet("positions")]
    public async Task<IActionResult> Positions([FromQuery] int? orgNodeId, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Positions") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListPositionsAsync(orgNodeId, pagination: pagination), skip, take));
    }

    [HttpPost("positions")]
    public async Task<IActionResult> CreatePosition([FromBody] HrMainPositionSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Create", "Positions") is { } f) return f;
        return Ok(await _svc.SavePositionAsync(null, dto, MyUserId, MyUsername));
    }

    [HttpPut("positions/{id:int}")]
    public async Task<IActionResult> UpdatePosition(int id, [FromBody] HrMainPositionSaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", "Positions") is { } f) return f;
        return Ok(await _svc.SavePositionAsync(id, dto, MyUserId, MyUsername));
    }

    [HttpDelete("positions/{id:int}")]
    public async Task<IActionResult> DeletePosition(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Delete", "Positions") is { } f) return f;
        await _svc.DeletePositionAsync(id, MyUserId, MyUsername);
        return Ok(new { ok = true });
    }

    // ------------------- تاریخچه تغییرات ساختار سازمانی -------------------
    // دسترسی: همان مجوز «Org» (مشاهده تاریخچه گره/پست) کافی است.

    [HttpGet("org/change-log")]
    public async Task<IActionResult> ChangeLog([FromQuery] string? entity, [FromQuery] string? q,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Org") is { } f) return f;
        return Ok(await _svc.SearchChangeLogAsync(entity, q, from, to, skip, take));
    }

    /// <summary>مقایسه ساختار سازمانی بین دو تاریخ — چه چیزی تغییر کرده است</summary>
    [HttpGet("org/compare")]
    public async Task<IActionResult> CompareStructure([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Org") is { } f) return f;
        return Ok(await _svc.CompareStructureAsync(from, to));
    }

    // ------------------- زبان و تقویم -------------------
    // دسترسی تفکیکی: مجوز اختصاصی «HrMain.Calendar».

    [HttpGet("locale")]
    public async Task<IActionResult> GetLocale()
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Calendar") is { } f) return f;
        return Ok(await _svc.GetLocaleAsync());
    }

    [HttpPut("locale")]
    public async Task<IActionResult> SaveLocale([FromBody] HrMainLocaleDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", "Calendar") is { } f) return f;
        return Ok(await _svc.SaveLocaleAsync(dto));
    }

    // ------------------- قوانین پیش‌فرض -------------------
    // نکته مهم: این قوانین (مرخصی/تأخیر/اضافه‌کاری) پایه محاسبات همه پرسنل هستند؛
    // مجوز اختصاصی «HrMain.Rules» به‌عمد از «HrMain.Update» عمومی جداست تا بتوان
    // تغییر این پارامترهای حساس را فقط به نقش محدودی (مثلاً مدیر ارشد HR) سپرد،
    // بدون این‌که آن نقش لزوماً به ویرایش شعب/ساختار سازمانی هم دسترسی داشته باشد.

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules()
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Rules") is { } f) return f;
        return Ok(await _svc.GetRulesAsync());
    }

    [HttpPut("rules")]
    public async Task<IActionResult> SaveRules([FromBody] HrMainRulesDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Update", "Rules") is { } f) return f;
        return Ok(await _svc.SaveRulesAsync(dto));
    }

    // ------------------- تعطیلات رسمی/شرکتی -------------------
    // دسترسی تفکیکی: مجوز اختصاصی «HrMain.Calendar» (تعطیلات جزو تقویم سازمان است).

    [HttpGet("holidays")]
    public async Task<IActionResult> Holidays([FromQuery] int? year, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Read", "Calendar") is { } f) return f;
        return Ok(await Paging.ResultAsync(async pagination => await _svc.ListHolidaysAsync(year, pagination: pagination), skip, take));
    }

    [HttpPost("holidays")]
    public async Task<IActionResult> CreateHoliday([FromBody] HrMainHolidaySaveDto dto)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Create", "Calendar") is { } f) return f;
        return Ok(await _svc.SaveHolidayAsync(dto, MyUsername));
    }

    [HttpDelete("holidays/{id:int}")]
    public async Task<IActionResult> DeleteHoliday(int id)
    {
        if (await ForbiddenUnlessAnyAsync(Mod, "Delete", "Calendar") is { } f) return f;
        await _svc.DeleteHolidayAsync(id);
        return Ok(new { ok = true });
    }
}
