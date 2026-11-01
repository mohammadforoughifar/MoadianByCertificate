using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace Inventory.Api.Infrastructure;

/// <summary>
/// جایگزین FallbackPolicy برای تأمین امنیت پیش‌فرض APIها:
/// همهٔ کنترلرها به‌صورت خودکار [Authorize] می‌شوند و اکشن‌های عمومی همچنان با
/// [AllowAnonymous] باز می‌مانند (مثل ورود به سیستم).
/// چرا FallbackPolicy حذف شد؟ چون علاوه بر endpointها، «هر درخواست بدون endpoint»
/// (از جمله فایل‌های استاتیک کلاینت مثل /index.html و /_framework/*.wasm در استقرار
/// تک‌سرورهٔ منتشرشده) را هم با خطای 401 رد می‌کرد و برنامهٔ پابلیش‌شده اصلاً باز نمی‌شد.
/// فایل‌های حساس wwwroot همچنان توسط middlewareهای گاردِ قبل از StaticFiles محافظت می‌شوند.
/// </summary>
public sealed class DefaultAuthorizeConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        // AuthorizeFilter با متادیتای [AllowAnonymous] روی اکشن/کنترلر سازگار است و آن را رعایت می‌کند.
        controller.Filters.Add(new AuthorizeFilter());
    }
}
