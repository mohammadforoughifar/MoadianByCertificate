using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Core;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// مدیریت طرف حساب‌ها (مشتریان / تأمین‌کنندگان).
/// <para>
/// چرا از <c>ModulePermission("Parties")</c> استفاده نمی‌شود؟
/// چون «مشتری» و «تأمین‌کننده» در یک کنترلر و یک جدول هستند ولی از نظر دسترسی باید
/// کاملاً جدا باشند (ویزیتور باید بتواند مشتری ثبت کند ولی هرگز تأمین‌کننده‌های شرکت را
/// نبیند). بنابراین مجوز هر درخواست بر اساس <see cref="PartyType"/> و مجوزهای RBAC
/// جداگانهٔ <c>Parties</c> / <c>Customers</c> / <c>Suppliers</c> سنجیده می‌شود.
/// </para>
/// <para>
/// قاعدهٔ سازگاری: نقش‌های قدیمی که فقط <c>Parties</c> دارند مثل قبل به هر دو نوع دسترسی دارند؛
/// نقش‌های جدید می‌توانند فقط <c>Customers</c> یا فقط <c>Suppliers</c> بگیرند.
/// </para>
/// <para>
/// محدودهٔ داده: «Read» یعنی دیدن همهٔ همان نوع؛ اگر نقشی فقط Create/Update داشته باشد
/// (مثل ویزیتور) فقط طرف حساب‌های پروندهٔ خودش را می‌بیند و مشتری جدیدش خودکار به همان
/// پرونده وصل می‌شود.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/parties")]
public class PartiesController : ControllerBase
{
    private readonly IInventoryService _service;
    private readonly IEffectivePermissions _permissions;
    private readonly AppDbContext _db;

    public PartiesController(IInventoryService service, IEffectivePermissions permissions, AppDbContext db)
    {
        _service = service;
        _permissions = permissions;
        _db = db;
    }

    // ==================== مجوزها ====================

    private async Task<HashSet<string>> MyPermissionsAsync()
    {
        if (await _permissions.IsLegacyAdminAsync(User))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" };
        return await _permissions.GetAsync(User);
    }

    private static bool Has(IReadOnlySet<string> perms, string module, string action)
        => perms.Contains("*") || perms.Contains($"{module}.{action}");

    /// <summary>ماژول مؤثر این نوع طرف حساب (تفکیک‌شده: Customers یا Suppliers).</summary>
    private static string ModuleOf(PartyType type) => type == PartyType.Supplier ? "Suppliers" : "Customers";

    /// <summary>مجوز «دیدن همهٔ» این نوع — هم از ماژول قدیمی و هم از ماژول تفکیک‌شده.</summary>
    private static bool CanReadAll(IReadOnlySet<string> perms, PartyType type)
        => Has(perms, "Parties", "Read") || Has(perms, ModuleOf(type), "Read");

    /// <summary>اجازهٔ کار با این نوع (ثبت/ویرایش) — حتی اگر محدود به پروندهٔ خودش باشد.</summary>
    private static bool CanWrite(IReadOnlySet<string> perms, PartyType type, bool update)
        => Has(perms, "Parties", update ? "Update" : "Create")
           || Has(perms, ModuleOf(type), update ? "Update" : "Create");

    private static bool CanDelete(IReadOnlySet<string> perms, PartyType type)
        => Has(perms, "Parties", "Delete") || Has(perms, ModuleOf(type), "Delete");

    private int MyReferrerId =>
        int.TryParse(User.FindFirstValue("referrerId"), out var rid) && rid > 0 ? rid : 0;

    private int MyUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

    /// <summary>نام نمایشی کاربر جاری (نام و نام خانوادگی، وگرنه نام کاربری) — برای ثبت «چه کسی ثبت کرد».</summary>
    private async Task<string> MyDisplayNameAsync()
    {
        var uid = MyUserId;
        var user = uid > 0 ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid) : null;
        var name = UserDisplay.Name(user);
        return string.IsNullOrWhiteSpace(name) ? (User.Identity?.Name ?? "") : name;
    }

    private ObjectResult Denied(string message) =>
        new(new { message }) { StatusCode = StatusCodes.Status403Forbidden };

    // ==================== عملیات ====================

    /// <summary>فهرست طرف حساب‌ها بر اساس نوع (مشتری/تأمین‌کننده).</summary>
    [HttpGet]
    public async Task<ActionResult> GetAll([FromQuery] PartyType type, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        var perms = await MyPermissionsAsync();
        var all = CanReadAll(perms, type);
        var scoped = !all && MyReferrerId > 0 && CanWrite(perms, type, update: false);

        if (!all && !scoped)
            return Denied(type == PartyType.Supplier
                ? "شما به بخش تأمین‌کنندگان دسترسی ندارید."
                : "شما به بخش مشتریان دسترسی ندارید.");

        int? scope = all ? null : MyReferrerId;

        return Ok(await Paging.ResultAsync(async pagination =>
            await _service.GetPartiesAsync(type, pagination, scope), skip, take));
    }

    /// <summary>ایجاد یا ویرایش طرف حساب.</summary>
    [HttpPost]
    public async Task<ActionResult> Save([FromBody] Inventory.Shared.Dtos.Party party)
    {
        var perms = await MyPermissionsAsync();
        var type = party.Type;

        if (!CanWrite(perms, type, update: party.Id > 0))
            return Denied(type == PartyType.Supplier
                ? "شما مجوز ثبت یا ویرایش تأمین‌کننده را ندارید."
                : "شما مجوز ثبت یا ویرایش مشتری را ندارید.");

        // کاربر محدود به پروندهٔ خودش (ویزیتور): هرگز تأمین‌کننده نمی‌سازد و از پرونده بیرون نمی‌زند
        if (!CanReadAll(perms, type))
        {
            if (MyReferrerId <= 0 && party.Id == 0)
                return Denied("حساب کاربری شما به هیچ معرفی متصل نیست؛ از مدیر سیستم بخواهید معرف شما را تعیین کند.");

            if (party.Id == 0)
            {
                party.Type = PartyType.Customer;
                party.ReferrerId = MyReferrerId;
            }
            else
            {
                var mine = await _service.GetPartiesAsync(type, null, MyReferrerId);
                var existing = mine.FirstOrDefault(p => p.Id == party.Id);
                if (existing is null)
                    return Denied("این طرف حساب در پروندهٔ شما نیست.");

                // ویزیتور نمی‌تواند مشتری را از پروندهٔ خودش جدا کند یا به پروندهٔ دیگری ببرد
                party.Type = PartyType.Customer;
                party.ReferrerId = existing.ReferrerId ?? (MyReferrerId > 0 ? MyReferrerId : null);
            }
        }

        // ثبت‌کننده را سرور تعیین می‌کند، نه کلاینت
        if (party.Id == 0)
        {
            party.RegisteredByUserId = MyUserId;
            var myName = (await MyDisplayNameAsync()).Trim();
            party.RegisteredByName = string.IsNullOrWhiteSpace(myName) ? null : myName;
            party.RegisteredByReferrerId = MyReferrerId > 0 ? MyReferrerId : null;
            party.RegisteredAt = DateTime.Now;
        }
        else
        {
            party.RegisteredByUserId = null;
            party.RegisteredByName = null;
            party.RegisteredByReferrerId = null;
            party.RegisteredAt = null;
        }

        return Ok(await _service.SavePartyAsync(party));
    }

    /// <summary>حذف طرف حساب.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] PartyType type = PartyType.Customer)
    {
        var perms = await MyPermissionsAsync();
        if (!CanDelete(perms, type)) return Denied("شما مجوز حذف این طرف حساب را ندارید.");
        await _service.DeletePartyAsync(id);
        return Ok(new { ok = true });
    }
}
