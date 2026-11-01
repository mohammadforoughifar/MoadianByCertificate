using System.Security.Claims;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Infrastructure;

/// <summary>
/// تعیین «شرکت فعال» برای هر درخواست API — پایهٔ جداسازی دادهٔ شرکت‌ها (چند شرکتی).
/// <para>
/// کلاینت شناسهٔ شرکت انتخاب‌شده در کمبوی بالای برنامه را در هدر <c>X-Company-Id</c> می‌فرستد.
/// این میان‌افزار آن را <b>اعتبارسنجی</b> می‌کند (کاربر فقط به شرکت‌هایی که در
/// «دسترسی شرکت‌های کاربر» ثبت شده اجازه دارد) و نتیجه را در
/// <c>HttpContext.Items["ActiveCompanyId"]</c> می‌گذارد؛ سپس
/// <see cref="AppDbContext"/> همهٔ کوئری‌های دادهٔ فروشگاهی را به همان شرکت محدود می‌کند.
/// </para>
/// <para>
/// اگر هدر نیامده یا نامعتبر باشد، شرکت پیش‌فرض کاربر (IsDefault) و در نبود آن اولین شرکت مجاز
/// انتخاب می‌شود؛ یعنی رفتار برنامه هیچ‌گاه «همهٔ شرکت‌ها با هم» نمی‌شود.
/// </para>
/// </summary>
public sealed class ActiveCompanyMiddleware
{
    public const string HeaderName = "X-Company-Id";
    public const string ItemKey = "ActiveCompanyId";
    public const string NameItemKey = "ActiveCompanyName";

    private readonly RequestDelegate _next;
    private readonly ILogger<ActiveCompanyMiddleware> _log;

    public ActiveCompanyMiddleware(RequestDelegate next, ILogger<ActiveCompanyMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var resolved = await ResolveAsync(ctx, db);
                if (resolved.CompanyId is not null)
                {
                    // مقدار ۰ یعنی «کاربر به هیچ شرکتی دسترسی ندارد» → هیچ داده‌ای نباید ببیند
                    ctx.Items[ItemKey] = resolved.CompanyId!.Value;
                    ctx.Items[NameItemKey] = resolved.CompanyName;
                }
            }
            catch (Exception ex)
            {
                // تعیین شرکت نباید هیچ درخواستی را بشکند؛ در بدترین حالت بدون زمینه ادامه می‌دهیم.
                _log.LogWarning(ex, "تعیین شرکت فعال برای درخواست {Path} ناموفق بود", ctx.Request.Path);
            }
        }

        await _next(ctx);
    }

    private static int UserIdOf(HttpContext ctx)
        => int.TryParse(ctx.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private static Task<(int? CompanyId, string? CompanyName)> ResolveAsync(HttpContext ctx, AppDbContext db)
    {
        // کاربر وارد نشده (ورود/بازیابی رمز/سلامت سرور): زمینهٔ شرکت تعیین نمی‌شود.
        if (ctx.User?.Identity?.IsAuthenticated != true)
            return Task.FromResult<(int?, string?)>((null, null));

        var userId = UserIdOf(ctx);
        if (userId <= 0) return Task.FromResult<(int?, string?)>((null, null));

        // شرکت درخواستی از هدر (کمبوی بالای فرم اصلی)
        int? requested = int.TryParse(ctx.Request.Headers[HeaderName].FirstOrDefault(), out var h) && h > 0 ? h : null;

        return ResolveCoreAsync(db, userId, ctx.User!, requested);
    }

    private static async Task<(int? CompanyId, string? CompanyName)> ResolveCoreAsync(
        AppDbContext db, int userId, ClaimsPrincipal user, int? requested)
    {
        // ---------- ۱) دسترسی‌های ثبت‌شدهٔ کاربر ----------
        var access = await db.UserCompanyAccesses.AsNoTracking()
            .Where(x => x.UserId == userId && x.Company.IsActive)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Company.Name).ThenBy(x => x.CompanyId)
            .Select(x => new { x.CompanyId, x.Company.Name })
            .ToListAsync();

        if (access.Count > 0)
        {
            var pick = requested is > 0 && access.Any(a => a.CompanyId == requested.Value)
                ? access.First(a => a.CompanyId == requested.Value)
                : access[0];
            return (pick.CompanyId, pick.Name);
        }

        // ---------- ۲) سازگاری با نسخهٔ قدیمی: شرکت روی خودِ کاربر ----------
        var username = user.FindFirstValue(ClaimTypes.Name) ?? "";
        if (!string.IsNullOrWhiteSpace(username))
        {
            var legacy = await (from u in db.SystemUsers.AsNoTracking()
                                join c in db.SystemCompanies.AsNoTracking() on u.CompanyId equals c.Id
                                where u.Username == username && u.CompanyId != null && c.IsActive
                                select new { CompanyId = c.Id, c.Name })
                               .FirstOrDefaultAsync();
            if (legacy is not null && legacy.CompanyId > 0)
                return (legacy.CompanyId, legacy.Name);
        }

        // ---------- ۳) مدیر بدون ردیف دسترسی: همهٔ شرکت‌های فعال ----------
        if (user.IsInRole("Admin"))
        {
            var all = await db.SystemCompanies.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name).ThenBy(c => c.Id)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            if (all.Count > 0)
            {
                var pick = requested is > 0 && all.Any(c => c.Id == requested.Value)
                    ? all.First(c => c.Id == requested.Value)
                    : all[0];
                return (pick.Id, pick.Name);
            }
        }

        // ---------- ۴) شرکت‌ها تعریف نشده‌اند (نصب تک‌شرکتی قدیمی/بدون شرکت) ----------
        var anyCompany = await db.SystemCompanies.AsNoTracking().AnyAsync(c => c.IsActive);
        if (!anyCompany) return (null, null);

        // ---------- ۵) شرکت تعریف شده ولی کاربر به هیچ‌کدام دسترسی ندارد ----------
        //     عدد ۰ = «بدون داده» تا نه چیزی از شرکت‌های دیگر لو برود و نه رکوردی ساخته شود.
        return (0, null);
    }
}
