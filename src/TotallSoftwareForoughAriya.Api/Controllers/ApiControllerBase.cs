using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inventory.Api.Controllers;

/// <summary>
/// کلاس پایه کنترلرهای عملیاتی — نقش «مدیر» و «اپراتور».
/// (بخش‌های مدیریتی مثل کاربران/تنظیمات/داشبورد مدیریتی، جداگانه فقط Admin هستند.)
/// خطاهای منطق کسب‌وکار توسط فیلتر سراسری به پاسخ 400 با پیام فارسی تبدیل می‌شوند.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Operator")]
public abstract class ApiControllerBase : ControllerBase
{
}

/// <summary>فیلتر سراسری خطا: هر Exception را به BadRequest با بدنه { message } تبدیل می‌کند.</summary>
public class ApiExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ApiExceptionFilter> _logger;

    public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) => _logger = logger;

    public void OnException(ExceptionContext context)
    {
        _logger.LogError(context.Exception, "خطا در پردازش درخواست {Path}", context.HttpContext.Request.Path);
        // زنجیره‌ی InnerException ها هم برگردانده می‌شود چون علت واقعی خطاهای دیتابیس (FK، طول ستون و…)
        // همیشه در InnerException است نه در پیام سطح اول.
        var sb = new System.Text.StringBuilder(context.Exception.Message);
        var inner = context.Exception.InnerException;
        while (inner is not null)
        {
            sb.Append(" | ").Append(inner.Message);
            inner = inner.InnerException;
        }
        context.Result = new BadRequestObjectResult(new { message = sb.ToString() });
        context.ExceptionHandled = true;
    }
}
