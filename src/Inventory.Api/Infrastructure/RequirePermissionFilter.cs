using Inventory.Api.Services.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inventory.Api.Infrastructure;

/// <summary>
/// کنترل دسترسی دقیق برای عملیاتی که با الگوی CRUD هم‌خوان نیستند.
/// برخلاف بررسی claim خام JWT، مجوزهای مؤثر از نقش‌های فعالِ فعلی در پایگاه‌داده
/// خوانده می‌شوند؛ بنابراین تغییر نقش کاربر فوراً در API اعمال می‌شود.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : TypeFilterAttribute
{
    public RequirePermissionAttribute(string module, string action) : base(typeof(RequirePermissionFilter))
    {
        Arguments = new object[] { module, action };
    }
}

public sealed class RequirePermissionFilter : IAsyncActionFilter
{
    private readonly IEffectivePermissions _permissions;
    private readonly string _module;
    private readonly string _action;

    public RequirePermissionFilter(IEffectivePermissions permissions, string module, string action)
    {
        _permissions = permissions;
        _module = module;
        _action = action;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var allowed = user.Identity?.IsAuthenticated == true
                      && (await _permissions.IsLegacyAdminAsync(user)
                          || (await _permissions.GetAsync(user)).Contains($"{_module}.{_action}"));
        if (!allowed)
        {
            context.Result = new ObjectResult(new { message = "شما مجوز لازم برای این عملیات را ندارید." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
