using Inventory.Api.Services.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inventory.Api.Infrastructure;

/// <summary>
/// RBAC عملیاتی برای کنترلرهای CRUD قدیمی که پیش‌تر فقط نقش کلی Admin/Operator را
/// بررسی می‌کردند. GET/POST/PUT/PATCH/DELETE به‌ترتیب به Read/Create-or-Update/
/// Update/Delete نگاشت می‌شوند. برای عملیات غیرمعمول نگاشت صریح زیر استفاده می‌شود.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ModulePermissionAttribute : TypeFilterAttribute
{
    public ModulePermissionAttribute(string module) : this(module, "Read") { }

    /// <param name="module">نام ماژول در RbacSeeder</param>
    /// <param name="readAction">اکشن لازم برای GET؛ برای گزارش‌های بخش‌محور مانند Kardex متفاوت است.</param>
    public ModulePermissionAttribute(string module, string readAction) : base(typeof(ModulePermissionFilter))
    {
        Arguments = new object[] { module, readAction };
    }
}

public sealed class ModulePermissionFilter : IAsyncActionFilter
{
    private readonly IEffectivePermissions _permissions;
    private readonly string _module;
    private readonly string _readAction;

    private sealed record PermissionTarget(string Module, string Action);

    // کنترلرهایی که یک route آن‌ها به ماژول/اکشن متفاوتی تعلق دارد یا HTTP verb آن
    // به‌تنهایی معنا را مشخص نمی‌کند. هر مورد خارج از این فهرست با قاعدهٔ CRUD کنترل می‌شود.
    private static readonly IReadOnlyDictionary<string, PermissionTarget> Overrides =
        new Dictionary<string, PermissionTarget>(StringComparer.Ordinal)
        {
            ["Stock.Adjust"] = new("Stock", "Update"),
            ["Settings.Save"] = new("Settings", "Update"),
            ["Referrers.GetWallets"] = new("ReferrerWallets", "Read"),
            ["Referrers.GetPayments"] = new("ReferrerWallets", "Read"),
            ["Referrers.AddPayment"] = new("ReferrerWallets", "Update"),
            ["Referrers.DeletePayment"] = new("ReferrerWallets", "Update")
        };

    public ModulePermissionFilter(IEffectivePermissions permissions, string module, string readAction)
    {
        _permissions = permissions;
        _module = module;
        _readAction = readAction;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
        {
            context.Result = Denied("کنترل دسترسی این عملیات قابل تشخیص نیست.");
            return;
        }

        var target = ResolveTarget(context, descriptor);
        if (target is null)
        {
            context.Result = Denied("برای این عملیات سطح دسترسی تعریف نشده است.");
            return;
        }

        var user = context.HttpContext.User;
        var allowed = user.Identity?.IsAuthenticated == true
                      && (await _permissions.IsLegacyAdminAsync(user)
                          || (await _permissions.GetAsync(user)).Contains($"{target.Module}.{target.Action}"));
        if (!allowed)
        {
            context.Result = Denied("شما مجوز لازم برای این عملیات را ندارید.");
            return;
        }

        await next();
    }

    private PermissionTarget? ResolveTarget(ActionExecutingContext context, ControllerActionDescriptor descriptor)
    {
        if (Overrides.TryGetValue($"{descriptor.ControllerName}.{descriptor.ActionName}", out var specific))
            return specific;

        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            return new PermissionTarget(_module, _readAction);
        if (HttpMethods.IsPut(method) || HttpMethods.IsPatch(method))
            return new PermissionTarget(_module, "Update");
        if (HttpMethods.IsDelete(method))
            return new PermissionTarget(_module, "Delete");
        if (HttpMethods.IsPost(method))
            return new PermissionTarget(_module, HasExistingEntity(context) ? "Update" : "Create");

        // مسیرهایی با verb ناشناخته تا زمان تعریف policy صریح، fail-closed باقی می‌مانند.
        return null;
    }

    /// <summary>
    /// در APIهای قدیمی Save با POST هم ایجاد و هم ویرایش انجام می‌دهد. اگر مدل ورودی
    /// شناسهٔ مثبت داشته باشد، همان فراخوانی باید مجوز Update داشته باشد، نه Create.
    /// </summary>
    private static bool HasExistingEntity(ActionExecutingContext context)
    {
        foreach (var value in context.ActionArguments.Values)
        {
            if (value is null) continue;
            var property = value.GetType().GetProperty("Id");
            if (property is null) continue;
            var raw = property.GetValue(value);
            // Nullable<int> هنگام boxing نیز (اگر مقدار داشته باشد) به int تبدیل می‌شود.
            if (raw is int id && id > 0) return true;
        }
        return false;
    }

    private static ObjectResult Denied(string message) => new(new { message }) { StatusCode = StatusCodes.Status403Forbidden };
}
