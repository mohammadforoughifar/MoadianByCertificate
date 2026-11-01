using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Inventory.Api.Data;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inventory.Api.Services;

/// <summary>
/// فیلتر سراسریِ «لاگ عملیات» — هر عملیاتِ غیرِخواندن (POST/PUT/PATCH/DELETE) روی هر بخشِ
/// نرم‌افزار به‌صورت خودکار ثبت می‌شود: کاربر، ماژول، عملیات، بدنه‌ی درخواست (بدون مقادیر حساس)،
/// IP، دستگاه، کد وضعیت و مدت اجرا. مشاهده‌ی لاگ‌ها فقط از بخش تنظیمات (ویژه‌ی مدیر) است.
/// </summary>
public class AuditLogFilter : IAsyncActionFilter
{
    /// <summary>کلیدهای حساسی که در لاگ ذخیره نمی‌شوند</summary>
    private static readonly string[] SensitiveKeys =
    {
        "password", "oldpassword", "newpassword", "confirmpassword",
        "passwordhash", "token", "secret", "balechatchid", "eitaachatchid",
        "code", "otp", "destination", "email", "mobile", "newemail", "newmobile",
        // گاوصندوق رمزهای تجهیزات
        "extrasecret",
    };

    /// <summary>کنترلرهایی که خودشان ثبت نمی‌شوند (خودِ صفحه‌ی لاگ)</summary>
    private static readonly string[] SkippedControllers = { "AuditLogs" };

    private const int MaxPayload = 3900;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var method = http.Request.Method.ToUpperInvariant();
        var desc = context.ActionDescriptor as ControllerActionDescriptor;
        var controller = desc?.ControllerName ?? "";

        if (method is "GET" or "HEAD" or "OPTIONS" || SkippedControllers.Contains(controller))
        {
            await next();
            return;
        }

        var sw = Stopwatch.StartNew();
        await next();
        try
        {
            var payload = BuildPayload(context.ActionArguments);
            var (uid, uname) = CurrentUser(http);
            if (uid == null && !string.IsNullOrEmpty(payload))
            {
                // در ورود موفق/ناموفق (قبل از صدور توکن)، نام کاربری از بدنه استخراج می‌شود
                uname ??= TryGetString(payload, "username") ?? TryGetString(payload, "userName");
            }

            var log = new AuditLog
            {
                At = DateTime.Now,
                UserId = uid,
                Username = Truncate(uname, 100) ?? "-",
                Module = controller,
                Action = desc?.ActionName ?? "",
                HttpMethod = method,
                Path = Truncate(http.Request.Path + http.Request.QueryString, 300),
                Summary = Truncate(BuildSummary(desc, http, payload), 200),
                Payload = payload,
                Ip = http.Connection.RemoteIpAddress?.ToString(),
                Device = Truncate(http.Request.Headers.UserAgent.ToString(), 250),
                StatusCode = http.Response.StatusCode,
                DurationMs = (int)sw.ElapsedMilliseconds,
            };

            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            db.AuditLogs.Add(log);
            await db.SaveChangesAsync();
        }
        catch
        {
            // خطای ثبت لاگ هرگز نباید عملیات اصلی برنامه را متوقف کند
        }
    }

    // ================== کمکی‌ها ==================

    private static (int? Id, string? Name) CurrentUser(HttpContext http)
    {
        var idStr = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = http.User.FindFirstValue(ClaimTypes.Name);
        return (int.TryParse(idStr, out var id) ? id : null, string.IsNullOrWhiteSpace(name) ? null : name);
    }

    /// <summary>سریال‌سازی بدنه‌ی درخواست با پوشاندن مقادیر حساس</summary>
    private static string? BuildPayload(IDictionary<string, object?> args)
    {
        try
        {
            object? body = null;
            foreach (var kv in args)
            {
                if (kv.Value == null || kv.Value is CancellationToken) continue;
                var t = kv.Value.GetType();
                if (t.Name.StartsWith("CancellationToken") || t.Name.Contains("FormFile")) continue;
                body = kv.Value;
                break;
            }
            if (body == null) return null;

            var json = System.Text.Json.JsonSerializer.Serialize(body, body.GetType());
            if (json.Length > 200_000) json = json[..200_000];

            var node = JsonNode.Parse(json);
            if (node != null) Redact(node);
            var result = node?.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false,
            }) ?? json;
            return Truncate(result, MaxPayload);
        }
        catch { return null; }
    }

    private static void Redact(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var prop in obj.ToList())
            {
                if (prop.Key != null && SensitiveKeys.Contains(prop.Key.Replace("_", "").ToLowerInvariant()))
                {
                    obj[prop.Key] = "***";
                }
                else if (prop.Value != null)
                {
                    Redact(prop.Value!);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
                if (item != null) Redact(item);
        }
    }

    private static string? TryGetString(string json, string prop)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return null;
            foreach (var kv in o)
            {
                if (string.Equals(kv.Key, prop, StringComparison.OrdinalIgnoreCase) && kv.Value is JsonValue val)
                {
                    var s = val.GetValue<string>();
                    return string.IsNullOrWhiteSpace(s) ? null : s;
                }
            }
            return null;
        }
        catch { return null; }
    }

    private static string BuildSummary(ControllerActionDescriptor? desc, HttpContext http, string? payload)
    {
        var parts = new List<string>();
        if (http.Request.RouteValues.TryGetValue("id", out var rid) && rid != null)
            parts.Add($"#{rid}");
        if (!string.IsNullOrEmpty(payload))
        {
            var name = TryGetString(payload, "name") ?? TryGetString(payload, "title")
                     ?? TryGetString(payload, "username") ?? TryGetString(payload, "number");
            if (!string.IsNullOrEmpty(name)) parts.Add(name!);
        }
        return parts.Count > 0 ? string.Join(" — ", parts) : $"{desc?.ActionName}";
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
